using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// One hull's 1130 SEND cadence, measured: how far apart consecutive control
    /// points actually left the server in wall-clock time, versus how far apart
    /// their timestamps claim they are.
    ///
    /// WHY THIS EXISTS (docs/research/findings-turn-vibration.md, production
    /// section). Under <c>WAREBORN_FLIGHT_FIXED_STEP=1</c> - the live production
    /// configuration - every emitted point is stamped exactly
    /// <see cref="ShipMotionPolicy.SendIntervalSeconds"/> after the last one
    /// (<see cref="FlightStampMode.PhaseLocked"/>). The SEND, however, still
    /// happens on whichever turn of the ENet poll loop completed the twelfth
    /// fixed step, and that loop turns once per event under a 50 ms timeout.
    /// Stamp spacing and send spacing are therefore two different numbers, and
    /// the difference is not cosmetic - it drives two things in the stock client:
    ///
    /// <list type="number">
    /// <item><b>The client's server-latency estimate.</b>
    ///   <c>PathFollower.AddControlPoint</c> computes
    ///   <c>_serverLatency = SynchronisedTime.UpdateNow - (stamp - ExtrapolationTime)</c>
    ///   at ARRIVAL time (decompile PathFollower.cs:146-147). With a phase-locked
    ///   stamp the arrival jitter lands in that estimate undiluted, and the
    ///   estimate is smoothed by a convolution whose duration is at least two
    ///   seconds (PathFollower.cs:106), so the correction is a slow wander of the
    ///   playback clock <c>num3 = SmoothFixedNow - smoothServerLatency</c>
    ///   (PathFollower.cs:251-256). The playback clock's RATE is the rendered
    ///   angular rate, because attitude is a bare
    ///   <c>Quaternion.SlerpUnclamped</c> across the segment
    ///   (SplineInterpolator.cs:44) with no tangent to stabilise it.</item>
    /// <item><b>The client's playback buffer depth.</b> Every point that is sent
    ///   later than its stamp interval shortens the buffer by the difference. If
    ///   the buffer reaches zero the follower stops interpolating and
    ///   extrapolates - and
    ///   <c>ControlPoint.ExtrapolateWithConstantVelocity</c> copies the previous
    ///   ROTATION unchanged (ControlPoint.cs:71-76), because there is no angular
    ///   velocity on the wire. The hull's yaw therefore FREEZES for the duration
    ///   of the extrapolation and is then corrected back over
    ///   <c>SlowSplineCorrectionTime = 5 s</c>. That is invisible in straight
    ///   flight, where constant-velocity extrapolation is exact, and is exactly
    ///   the reported symptom in a turn.</item>
    /// </list>
    ///
    /// <see cref="CumulativeDriftMilliseconds"/> is the number that matters: the
    /// running total of (send interval - stamp interval). Bounded jitter makes it
    /// oscillate around zero and the client is fine. A drift that grows is the
    /// client's buffer being eaten, and predicts the extrapolation/freeze cycle.
    ///
    /// PURE AND ALLOCATION-LIGHT ON PURPOSE. It holds one bounded window per hull
    /// and does no I/O, so the glue can call it on every emitted point without
    /// changing what goes on the wire. Nothing here influences a pose, a stamp or
    /// a packet - it only observes.
    /// </summary>
    public sealed class FlightSendCadence
    {
        /// <summary>
        /// How many recent intervals the percentiles are taken over. 64 points at
        /// the 4.2 Hz control-point cadence is about 15 seconds - long enough to
        /// cover a whole sustained turn, short enough that a report describes what
        /// the pilot is doing NOW rather than an average of the last minute.
        /// </summary>
        public const int WindowSize = 64;

        private readonly double _nominalIntervalMs;
        private readonly List<double> _sendIntervals = new List<double>(WindowSize);
        private readonly List<double> _stampIntervals = new List<double>(WindowSize);
        private bool _seeded;
        private double _lastSendMs;
        private long _lastStampMs;

        public FlightSendCadence(double nominalIntervalSeconds = ShipMotionPolicy.SendIntervalSeconds)
        {
            if (!(nominalIntervalSeconds > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(nominalIntervalSeconds));
            }
            _nominalIntervalMs = nominalIntervalSeconds * 1000.0;
        }

        /// <summary>Points observed since construction, including the seeding one.</summary>
        public long Observed { get; private set; }

        /// <summary>
        /// Running total of (send interval - stamp interval), milliseconds. The
        /// buffer-erosion number: positive means the client has received less
        /// playback material than wall-clock time has consumed.
        /// </summary>
        public double CumulativeDriftMilliseconds { get; private set; }

        /// <summary>
        /// Observes one emitted control point. <paramref name="sendAtMs"/> is the
        /// server's monotonic clock at the instant the point is published;
        /// <paramref name="stampMs"/> is the timestamp it carries. The first call
        /// only seeds the baseline - an interval needs two points.
        /// </summary>
        public void Observe(double sendAtMs, long stampMs)
        {
            Observed++;
            if (!_seeded)
            {
                _seeded = true;
                _lastSendMs = sendAtMs;
                _lastStampMs = stampMs;
                return;
            }

            double sendInterval = sendAtMs - _lastSendMs;
            double stampInterval = stampMs - _lastStampMs;
            _lastSendMs = sendAtMs;
            _lastStampMs = stampMs;

            // A non-advancing or backwards clock is a caller error, not data.
            if (!double.IsFinite(sendInterval) || sendInterval < 0.0)
            {
                return;
            }

            CumulativeDriftMilliseconds += sendInterval - stampInterval;
            Append(_sendIntervals, sendInterval);
            Append(_stampIntervals, stampInterval);
        }

        private static void Append(List<double> window, double value)
        {
            if (window.Count == WindowSize)
            {
                window.RemoveAt(0);
            }
            window.Add(value);
        }

        /// <summary>Samples currently inside the percentile window.</summary>
        public int WindowCount => _sendIntervals.Count;

        /// <summary>
        /// The worst absolute deviation of a SEND interval from nominal, in the
        /// current window. This is the jitter the client's latency estimator sees.
        /// </summary>
        public double WorstSendDeviationMilliseconds => WorstDeviation(_sendIntervals);

        /// <summary>
        /// The worst absolute deviation of a STAMP interval from nominal. Under
        /// the fixed-step publisher this must be 0: if it is not, the phase lock
        /// is not doing what it claims and that is a separate defect.
        /// </summary>
        public double WorstStampDeviationMilliseconds => WorstDeviation(_stampIntervals);

        private double WorstDeviation(List<double> window)
        {
            double worst = 0.0;
            for (int i = 0; i < window.Count; i++)
            {
                double deviation = Math.Abs(window[i] - _nominalIntervalMs);
                if (deviation > worst)
                {
                    worst = deviation;
                }
            }
            return worst;
        }

        /// <summary>
        /// The <paramref name="percentile"/>-th send interval in the window, in
        /// milliseconds; 0 when nothing has been observed yet. Nearest-rank on a
        /// sorted copy - the windows are 64 entries, so simplicity beats a
        /// streaming estimator here.
        /// </summary>
        public double SendIntervalPercentileMilliseconds(double percentile)
        {
            if (_sendIntervals.Count == 0)
            {
                return 0.0;
            }
            double[] sorted = _sendIntervals.ToArray();
            Array.Sort(sorted);
            int rank = (int)Math.Ceiling(Math.Clamp(percentile, 0.0, 1.0) * sorted.Length) - 1;
            return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
        }

        /// <summary>
        /// Whether the measured cadence is healthy enough that the stock client's
        /// playback buffer is not being eroded.
        ///
        /// The threshold is the client's own extrapolation headroom,
        /// <c>ShipConfiguration.ExtrapolationTime = 0.75 s</c> (decompile
        /// ShipConfiguration.cs), halved. Once cumulative drift passes that, the
        /// buffer is within one publication interval of the point where
        /// <c>SplineInterpolator.Interpolate</c> fails and the follower starts
        /// extrapolating a FROZEN rotation. Half rather than the whole value so
        /// the warning arrives before the artefact does.
        /// </summary>
        public const double BufferErosionWarnMilliseconds = 375.0;

        public bool BufferErosionSuspected => CumulativeDriftMilliseconds >= BufferErosionWarnMilliseconds;

        /// <summary>One log line; the caller decides how often to ask for it.</summary>
        public string Describe()
        {
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
            return "points=" + Observed
                + " sendP50=" + SendIntervalPercentileMilliseconds(0.50).ToString("0.0", culture) + "ms"
                + " sendP95=" + SendIntervalPercentileMilliseconds(0.95).ToString("0.0", culture) + "ms"
                + " worstSendDev=" + WorstSendDeviationMilliseconds.ToString("0.0", culture) + "ms"
                + " worstStampDev=" + WorstStampDeviationMilliseconds.ToString("0.0", culture) + "ms"
                + " drift=" + CumulativeDriftMilliseconds.ToString("0.0", culture) + "ms"
                + (BufferErosionSuspected ? " BUFFER-EROSION-SUSPECTED" : string.Empty);
        }
    }
}
