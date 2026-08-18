using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pose a ship is drawn at when the last MEASUREMENT of it is a few
    /// seconds old, and - just as important - how far that drawing may be wrong.
    ///
    /// WHY THIS EXISTS AND WHY IT IS NOT THE FAUNA ANSWER. Every creature in this
    /// world moves on a closed form of the clock, so the console evaluates the
    /// server's own function and draws the pose the server actually holds. A SHIP
    /// has no such function: it moves under a player's hands and under the flight
    /// integrator, and nothing outside that loop can know what the stick did. The
    /// only truthful inputs a reader has are the ones the snapshot carries, and the
    /// snapshot lands every few seconds.
    ///
    /// So the console does what the GAME CLIENT does between two control points: it
    /// carries the hull forward along the velocity the server itself reported. That
    /// is dead reckoning, not interpolation - there is no second sample to
    /// interpolate toward, and inventing one would be a smooth guess dressed up as
    /// a measurement.
    ///
    /// AND IT IS BOUNDED, which is the part that makes it honest. The reckoning is
    /// wrong only to the extent the ship's velocity CHANGED since the measurement,
    /// and the server's own flight integrator limits that: speed chases its target
    /// under an acceleration cap, so after <c>t</c> seconds the position can be off
    /// by at most <c>0.5 * accel * t^2</c>. This type turns that around and asks
    /// how long the reckoning may run before it could be wrong by more than
    /// <see cref="ToleratedErrorMetres"/> - and stops there. Past that point the
    /// mark holds still and the console says in words how old the measurement is,
    /// because a mark that keeps gliding on nothing is a lie and a mark that has
    /// visibly stopped is a question the operator can answer.
    ///
    /// Pure, engine-free and total, so both the game server and the browser mirror
    /// can evaluate it and a parity test can hold them together.
    /// </summary>
    public static class ShipMapMotion
    {
        /// <summary>
        /// How far the drawn hull may be from the measured one before the console
        /// stops reckoning, in metres.
        ///
        /// WAREBORN TUNING, and a deliberate one: 20 m is roughly the keel of a
        /// five-cell ship, so at the limit the mark is off by about its own length
        /// - visible as a slight lead at close zoom, invisible at any zoom where a
        /// whole island is a few pixels, and never enough to put a ship over the
        /// wrong island. Retail published no such number; there was no operator map.
        /// </summary>
        public const double ToleratedErrorMetres = 20.0;

        /// <summary>The shortest reckoning window, seconds; below this a mark would step visibly every poll.</summary>
        public const double MinWindowSeconds = 0.5;

        /// <summary>
        /// The longest reckoning window, seconds. A server configured with a very
        /// gentle acceleration would otherwise be allowed to reckon for a minute,
        /// and a minute-old pose is not a position however small its bound is: the
        /// helm can be released, the ship recalled, the domain torn down.
        /// </summary>
        public const double MaxWindowSeconds = 8.0;

        /// <summary>
        /// How long the console may carry a hull forward, given the acceleration
        /// limit the RUNNING server's flight tuning is using. Solved from the
        /// bound, not chosen: <c>0.5 * a * t^2 = ToleratedErrorMetres</c>.
        ///
        /// The acceleration is passed in rather than read from
        /// <see cref="Flight.FlightTuning"/> here so that the number which reaches
        /// the browser is the live one - the tuning is env-configurable per
        /// deployment, and a console hard-coding the default would quietly reckon
        /// too far on a server that was tuned to be twitchier.
        /// </summary>
        public static double WindowSecondsFor(double accelMps2)
        {
            if (double.IsNaN(accelMps2) || double.IsInfinity(accelMps2) || accelMps2 <= 0)
            {
                return MinWindowSeconds;
            }

            double solved = Math.Sqrt(2.0 * ToleratedErrorMetres / accelMps2);
            return Clamp(solved, MinWindowSeconds, MaxWindowSeconds);
        }

        /// <summary>
        /// The furthest the reckoned position can be from the true one after
        /// <paramref name="seconds"/>, at the server's acceleration limit. The
        /// console prints this rather than a reassurance.
        /// </summary>
        public static double ErrorBoundMetres(double accelMps2, double seconds)
        {
            if (accelMps2 <= 0 || seconds <= 0) return 0;
            return 0.5 * accelMps2 * seconds * seconds;
        }

        /// <summary>
        /// Where the hull is drawn, <paramref name="ageSeconds"/> after the
        /// measurement it was last reported in.
        ///
        /// Straight-line in the reported velocity and constant in the reported turn
        /// rate: the two derivatives the flight state actually carries. Nothing is
        /// integrated twice, no acceleration is assumed to continue, and the age is
        /// clamped into <c>[0, window]</c> - a negative age (a clock that ran
        /// backwards between two hosts) draws the measurement itself rather than
        /// reckoning into the past.
        /// </summary>
        public static ShipMapPose PoseAt(ShipMapPose measured, double ageSeconds, double windowSeconds)
        {
            double t = Reckoned(ageSeconds, windowSeconds);
            return new ShipMapPose(
                measured.X + measured.VelocityXMps * t,
                measured.Z + measured.VelocityZMps * t,
                measured.YawRadians + measured.YawRateRadPerSec * t,
                measured.VelocityXMps,
                measured.VelocityZMps,
                measured.YawRateRadPerSec);
        }

        /// <summary>
        /// The seconds actually applied: the age, floored at zero and capped at the
        /// window. Exposed because the console shows the difference between it and
        /// the true age - that gap IS the "the mark has stopped, the ship has not"
        /// state, and it must be computed once.
        /// </summary>
        public static double Reckoned(double ageSeconds, double windowSeconds)
        {
            if (double.IsNaN(ageSeconds) || ageSeconds <= 0) return 0;

            // Deliberately NOT re-clamped up to MinWindowSeconds. The floor
            // belongs to WindowSecondsFor, where a window is PRODUCED; applying
            // it again here would mean a caller who asked for no reckoning at
            // all - a reader that has no model to reckon with, which is exactly
            // what an older game server leaves it holding - still got half a
            // second of it. Zero in must mean zero out, or the browser mirror and
            // this cannot agree on the one case where agreement is easiest.
            double window = double.IsNaN(windowSeconds) || windowSeconds <= 0 ? 0
                : (windowSeconds > MaxWindowSeconds ? MaxWindowSeconds : windowSeconds);
            return ageSeconds > window ? window : ageSeconds;
        }

        /// <summary>
        /// Whether there is anything to reckon at all. A hull sitting still reports
        /// exactly zero on every axis (the integrator snaps them), so its mark is
        /// the MEASURED pose and the console may say so without qualification -
        /// which is most ships, most of the time, and is worth distinguishing from
        /// a reckoned one rather than hedging about all of them equally.
        /// </summary>
        public static bool IsMeasuredExactly(ShipMapPose measured) =>
            measured.VelocityXMps == 0.0
            && measured.VelocityZMps == 0.0
            && measured.YawRateRadPerSec == 0.0;

        private static double Clamp(double value, double low, double high) =>
            value < low ? low : value > high ? high : value;
    }

    /// <summary>
    /// A ship's plan-view pose and its two derivatives, in world metres and
    /// radians. The subset of <see cref="Flight.FlightState"/> a top-down map can
    /// use: altitude, roll and pitch are real and are reported elsewhere, but none
    /// of them moves a mark on a chart.
    /// </summary>
    public readonly record struct ShipMapPose(
        double X,
        double Z,
        double YawRadians,
        double VelocityXMps,
        double VelocityZMps,
        double YawRateRadPerSec);
}
