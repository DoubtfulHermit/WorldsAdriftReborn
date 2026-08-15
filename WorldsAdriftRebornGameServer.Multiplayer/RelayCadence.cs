using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// HOW OFTEN the relay emitter speaks, and nothing else.
    ///
    /// WHY A CADENCE AT ALL. The receiving client renders remote players through
    /// a delayed interpolator with a FIXED 100 ms playback delay and a 5-slot
    /// queue. We cannot change either - they are compiled into every client - so
    /// the only knob that exists is on our side: how often, and how regularly,
    /// samples arrive. Forwarding raw packets in arrival order (the old path)
    /// couples every sender hiccup and server backlog straight into that queue.
    /// A fixed-cadence emit of the latest accepted state decouples them: the
    /// receiver sees a metronome whatever the ingest saw.
    ///
    /// WHY 20 Hz IS THE DEFAULT. The client's 100 ms budget divided by a 50 ms
    /// emit interval is exactly the classic Source-engine pairing: even with ONE
    /// lost snapshot there are still two samples bracketing render time. At
    /// 18 Hz (55.6 ms) a single loss opens a 111 ms gap - just over budget,
    /// a visible stutter that 20 Hz would have absorbed. The floor of the
    /// allowed range keeps the interval inside the budget with one loss
    /// (15 Hz = 66.7 ms, marginal but usable on a LAN); the ceiling stops a
    /// typo'd 300 from tripling everyone's movement bandwidth.
    /// </summary>
    public static class RelayCadencePolicy
    {
        public const double DefaultHz = 20.0;
        public const double MinHz = 15.0;
        public const double MaxHz = 30.0;

        /// <summary>How often the per-peer relay statistics are logged.</summary>
        public static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// The emit rate from an environment-variable string: parsed
        /// invariant-culture, clamped to [<see cref="MinHz"/>, <see cref="MaxHz"/>],
        /// and <see cref="DefaultHz"/> for anything unset or unparsable. Never
        /// throws - a bad env var must not take the server down.
        /// </summary>
        public static double HzFrom(string? env)
        {
            if (string.IsNullOrWhiteSpace(env)
                || !double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out double hz)
                || double.IsNaN(hz))
            {
                return DefaultHz;
            }

            return Math.Clamp(hz, MinHz, MaxHz);
        }

        /// <summary>The emit interval for a rate.</summary>
        public static TimeSpan IntervalFor(double hz) => TimeSpan.FromSeconds(1.0 / hz);

        /// <summary>
        /// The synthetic-timeline step for a rate, in seconds. Derived from the
        /// configured cadence, never hardcoded: the receiver's interpolator
        /// advances its playback clock in real seconds, so the emitted stamps
        /// must advance at the same rate the samples are actually sent or the
        /// avatar plays back too fast (stamps ahead of arrival: queue starves)
        /// or too slow (stamps behind: queue overflows its 5 slots).
        /// </summary>
        public static double StepSecondsFor(double hz) => 1.0 / hz;
    }

    /// <summary>Recipient-side movement pressure; healthy peers remain byte-for-byte normal.</summary>
    public enum RecipientRelayPressure
    {
        Normal,
        Degraded,
        Severe,
    }

    /// <summary>
    /// Hysteretic protection for a peer whose reliable RTT proves its client is
    /// no longer servicing ENet promptly. Movement snapshots supersede older
    /// snapshots, so reducing only that recipient's cadence gives its Unity main
    /// thread room to recover without slowing healthy players or reliable game
    /// commands. Live local evidence: one peer reached 3 seconds while its peer
    /// on the same route remained at 36 ms.
    /// </summary>
    public static class RelayBackpressurePolicy
    {
        public const uint DegradedEnterRttMs = 500;
        public const uint SevereEnterRttMs = 1500;
        public const uint DegradedRecoverRttMs = 250;
        public const uint SevereRecoverRttMs = 1000;

        public static RecipientRelayPressure Next(
            RecipientRelayPressure current, uint rttMs) => current switch
        {
            RecipientRelayPressure.Normal => rttMs > SevereEnterRttMs
                ? RecipientRelayPressure.Severe
                : rttMs > DegradedEnterRttMs
                    ? RecipientRelayPressure.Degraded
                    : RecipientRelayPressure.Normal,
            RecipientRelayPressure.Degraded => rttMs > SevereEnterRttMs
                ? RecipientRelayPressure.Severe
                : rttMs < DegradedRecoverRttMs
                    ? RecipientRelayPressure.Normal
                    : RecipientRelayPressure.Degraded,
            RecipientRelayPressure.Severe => rttMs < DegradedRecoverRttMs
                ? RecipientRelayPressure.Normal
                : rttMs < SevereRecoverRttMs
                    ? RecipientRelayPressure.Degraded
                    : RecipientRelayPressure.Severe,
            _ => RecipientRelayPressure.Normal,
        };

        public static TimeSpan MinimumInterval(RecipientRelayPressure pressure) =>
            pressure switch
            {
                RecipientRelayPressure.Degraded => TimeSpan.FromMilliseconds(100), // 10 Hz
                RecipientRelayPressure.Severe => TimeSpan.FromMilliseconds(200),   // 5 Hz
                _ => TimeSpan.Zero,
            };

        public static bool IsDue(TimeSpan now, TimeSpan? lastSent,
            RecipientRelayPressure pressure)
        {
            TimeSpan minimum = MinimumInterval(pressure);
            return minimum == TimeSpan.Zero || !lastSent.HasValue
                || now - lastSent.Value >= minimum;
        }
    }

    /// <summary>
    /// A metronome fed a monotonic time. Says "tick now" at most once per
    /// interval, schedules on the IDEAL grid (nextDue += interval, not
    /// now + interval) so jitter in when the main loop happens to call it does
    /// not accumulate into rate drift - and refuses to burst-catch-up after a
    /// stall, because emitting a backlog of ticks at once is exactly the
    /// arrival-order clumping the cadence exists to remove.
    /// </summary>
    public sealed class CadenceTimer
    {
        private readonly TimeSpan _interval;
        private bool _started;
        private TimeSpan _nextDue;

        public CadenceTimer(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }
            _interval = interval;
        }

        /// <summary>
        /// Whether a tick is due at this instant. The first call is always due,
        /// which is what makes "the emitter starts emitting as soon as there is
        /// anything to emit" true without a warm-up interval.
        /// </summary>
        public bool Due(TimeSpan now)
        {
            if (!_started)
            {
                _started = true;
                _nextDue = now + _interval;
                return true;
            }

            if (now < _nextDue)
            {
                return false;
            }

            _nextDue += _interval;
            if (_nextDue <= now)
            {
                // The loop stalled for more than a whole interval. Skip the
                // missed ticks rather than firing them back-to-back.
                _nextDue = now + _interval;
            }
            return true;
        }
    }
}
