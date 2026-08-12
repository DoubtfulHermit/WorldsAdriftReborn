using System;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// A point on the shared day/night clock: an integer day counter plus a
    /// fractional time-of-day in [0,1). These are exactly the two fields the
    /// client's <c>WorldStateVisualizer</c> reads out of 1131 WorldData
    /// (<c>Days</c> and <c>Time</c>) before it free-runs the cycle from its own
    /// <c>Time.deltaTime</c>.
    /// </summary>
    public readonly struct WorldTime
    {
        public WorldTime(int days, float dayTime)
        {
            Days = days;
            DayTime = dayTime;
        }

        /// <summary>The integer day counter (1131 WorldData.days).</summary>
        public int Days { get; }

        /// <summary>The fraction of the current day in [0,1) (1131 WorldData.time).</summary>
        public float DayTime { get; }
    }

    /// <summary>
    /// The SHARED world-time epoch, seeded into 1131 WorldData so every client
    /// checks out the CURRENT shared time of day rather than a per-checkout
    /// constant.
    ///
    /// THE BUG THIS FIXES. The server used to seed every 1131 request with the
    /// same snapshot (time=0.15, timeRate=1, days=1). The client treats those as
    /// an INITIAL clock and free-runs the day/night cycle from local
    /// <c>Time.deltaTime</c> thereafter (<c>WorldStateVisualizer.Update</c>:
    /// <c>_predictedDayTime += Time.deltaTime * (timeRate / 86400)</c>). So two
    /// clients that joined minutes apart both started at 0.15 and stayed that many
    /// minutes out of phase forever - sun, lighting and ambience desynced.
    ///
    /// THE FIX. Advance the epoch by the REAL server elapsed time at the same rate
    /// the client integrates, and seed 1131 with the result. A client that checks
    /// out at server-uptime E is handed the world time a client present since boot
    /// would now be showing; it then free-runs from there. Two clients that check
    /// out at different uptimes are handed each other's CURRENT time, so they land
    /// in phase and stay in phase (they integrate the same rate from the same
    /// point). This stays a ONE-TIME checkout seed - the client owns the cycle
    /// after that - NOT a per-frame stream.
    ///
    /// This type is pure: <see cref="Advance"/> takes the elapsed seconds as a
    /// parameter, so there is no <c>DateTime.Now</c> / <c>Random</c> here and the
    /// advance is fully deterministic w.r.t. its inputs. The monotonic
    /// boot-relative clock that supplies the elapsed seconds lives in the
    /// server-side glue (<see cref="ServerWorldClock"/>).
    /// </summary>
    public static class WorldClock
    {
        /// <summary>Real seconds in one full day-fraction, matching the client's 86400 divisor.</summary>
        public const float SecondsPerDay = 86400f;

        /// <summary>
        /// The day counter the world starts from at server boot. Keeps the prior
        /// baseline (days=1) so nothing else about the seed changes.
        /// </summary>
        public const int EpochDays = 1;

        /// <summary>
        /// The time-of-day the world starts from at server boot. Keeps the prior
        /// baseline (0.15).
        /// </summary>
        public const float EpochDayTime = 0.15f;

        /// <summary>
        /// The rate the clock advances, in day-fractions per <see cref="SecondsPerDay"/>
        /// real seconds. Held at 1 (a real-time 24h cycle) exactly as before; the
        /// server advance and the client free-run use the same value, so raising it
        /// no longer desyncs the two clocks.
        /// </summary>
        public const float TimeRate = 1f;

        /// <summary>
        /// The world time <paramref name="elapsedSeconds"/> of real time after the
        /// given epoch, integrating at <paramref name="timeRate"/> day-fractions
        /// per <see cref="SecondsPerDay"/> seconds - the continuous form of the
        /// client's per-frame integration, so a client seeded with this result and
        /// then free-running is indistinguishable from one present since the epoch.
        ///
        /// Pure and deterministic: same inputs, same output; no wall-clock read.
        /// </summary>
        public static WorldTime Advance(int epochDays, float epochDayTime, float timeRate, double elapsedSeconds)
        {
            double dayFraction = epochDayTime + elapsedSeconds * (timeRate / SecondsPerDay);
            int wholeDays = (int)Math.Floor(dayFraction);
            double fraction = dayFraction - wholeDays;
            return new WorldTime(epochDays + wholeDays, (float)fraction);
        }

        /// <summary>
        /// The current shared world time, advancing the boot epoch by
        /// <paramref name="elapsedSecondsSinceBoot"/>. Convenience over
        /// <see cref="Advance"/> that pins the epoch/rate constants; still pure.
        /// </summary>
        public static WorldTime Current(double elapsedSecondsSinceBoot)
        {
            return Advance(EpochDays, EpochDayTime, TimeRate, elapsedSecondsSinceBoot);
        }
    }
}
