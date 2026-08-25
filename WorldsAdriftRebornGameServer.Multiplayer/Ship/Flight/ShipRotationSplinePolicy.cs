using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>One buffered control point's attitude, with the time it is stamped at.</summary>
    /// <remarks>
    /// Deliberately not a Unity type. This file is compiled into BOTH the net35
    /// BepInEx mod and the net6.0 Multiplayer library so the unit tests exercise
    /// the same code the client runs; UnityEngine is not available on the second.
    /// The client patch converts to and from UnityEngine.Quaternion at the seam.
    /// </remarks>
    public struct SplineRotationSample
    {
        public double Time;
        public double X;
        public double Y;
        public double Z;
        public double W;

        public SplineRotationSample(double time, double x, double y, double z, double w)
        {
            Time = time;
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    /// <summary>
    /// Gives the client's ship attitude playback the derivative the wire does not
    /// carry.
    ///
    /// THE DEFECT. A 1130 ShipControlPoint is
    /// {timestamp, position, rotation, velocity, fsim_id_hash} and `velocity` is
    /// LINEAR only - there is no angular-velocity field anywhere in the component
    /// (Schema.Bossa.Travellers.Motion.Prediction/ShipControlPoint.cs). So retail's
    /// SplineInterpolator.CubicHermiteInterpolation builds POSITION from a cubic
    /// with real endpoint tangents - C1 continuous - and ATTITUDE from a bare
    /// Quaternion.SlerpUnclamped between the two endpoints - C0. Angular velocity
    /// is therefore piecewise constant with a kink at every 240 ms control point.
    /// The resulting attitude-rate error is ~0 at the hull origin and is multiplied
    /// by each mounted part's lever arm before it reaches the eye, which is exactly
    /// why the helm, wings, sails and outboard engines shake while the deck barely
    /// does, and why nothing shakes in straight flight.
    ///
    /// THE CORRECTION. Squad (spherical-quadrangle) interpolation derives the
    /// missing derivative from control points the client ALREADY has buffered:
    ///
    ///     s_i    = q_i * exp( -( a*log(q_i^-1 q_i+1) + b*log(q_i^-1 q_i-1) ) / 2 )
    ///     squad  = slerp( slerp(q_i, q_i+1, t), slerp(s_i, s_i+1, t), 2t(1-t) )
    ///
    /// with the Kim/Kim/Shin non-uniform weights a = h_prev/(h_prev+h_next) and
    /// b = h_next/(h_prev+h_next), where h_prev and h_next are the intervals on
    /// either side of the point whose inner control quaternion is being built.
    ///
    /// Three properties earn this its place, and each one is pinned by a test:
    ///
    /// 1. IT IS THE IDENTITY ON A STEADY TURN. Feed it a constant angular rate
    ///    and both weighted logs cancel exactly, s_i collapses to q_i, and squad
    ///    degenerates to the slerp retail already does - for uneven stamps too.
    ///    So this changes nothing while the turn rate is held; it acts only where
    ///    the rate CHANGES, which is precisely where the C0 kink lives.
    /// 2. IT PASSES THROUGH EVERY SERVER POINT. At t=0 and t=1 the result is
    ///    q_i and q_i+1 exactly. The authoritative attitudes are never moved, so
    ///    this cannot drift the client away from the server: it only chooses a
    ///    smoother route BETWEEN two poses the server actually sent.
    /// 3. s_i DEPENDS ONLY ON THE POINT AND ITS TWO NEIGHBOURS, never on which
    ///    segment is being evaluated. That is what makes the join C1: the segment
    ///    ending at q_i+1 and the segment starting at q_i+1 compute the same
    ///    s_i+1 and therefore agree on the angular velocity there.
    ///
    /// WHAT IT DELIBERATELY DOES NOT DO. It does not bypass a receive-side apply
    /// threshold, repeat a pose, or write any follower state. The rejected
    /// 2026.08.24-1 client trial did exactly that and failed live acceptance:
    /// making the rendered hull continuous while leaving PathFollower's
    /// PreviousSample and Rigidbody velocity on an older sample desynchronised the
    /// contact/carry path a standing player reads, which drifted and then hard
    /// corrected (docs/architecture/client-ship-motion-continuity-2026-08-24.md).
    /// This policy returns a DIFFERENT VALUE for one field of the single sample
    /// retail was already going to apply, on retail's own schedule, through
    /// retail's own PathFollower.Move. Nothing downstream changes shape.
    /// </summary>
    public static class ShipRotationSplinePolicy
    {
        /// <summary>
        /// Largest neighbour interval, as a multiple of the segment being drawn,
        /// still trusted as a tangent source. A far longer gap on one side means
        /// the buffer was cleared, re-seeded or halted across it, so the implied
        /// rate is about a discontinuity rather than about the turn.
        /// </summary>
        public const double MaxNeighbourIntervalRatio = 4.0;

        /// <summary>
        /// Any adjacent attitude delta beyond this is a correction, a re-seed or a
        /// teleport, not a turn. A ship yaws single-digit degrees per 240 ms point;
        /// smoothing a 90 degree step would invent a swing through the intermediate
        /// arc, so the whole segment falls back to stock slerp instead.
        /// </summary>
        public const double MaxSegmentDegrees = 90.0;

        private const double UnitEpsilon = 1e-9;
        private const double LerpFallbackDot = 0.9995;

        /// <summary>
        /// Produces the squad-interpolated attitude for <paramref name="param"/>
        /// along the segment from <paramref name="from"/> to <paramref name="to"/>.
        ///
        /// Returns FALSE for every case the caller must render with retail's own
        /// slerp: non-finite input, a non-advancing segment, an implausible
        /// attitude step, or - importantly - BOTH neighbours being unavailable.
        /// A false return is the fail-safe path and is bit-for-bit stock, because
        /// the caller then simply does not touch the value retail computed.
        ///
        /// When only ONE neighbour is available the tangent on the missing side is
        /// the point itself (s = q), which is the textbook clamped end condition
        /// for a quaternion spline. That invents no data: an absent neighbour
        /// contributes a zero tangent term, never a guessed rotation, and if both
        /// are absent the formula would collapse to plain slerp anyway - which is
        /// why that case short-circuits to a false return rather than recomputing
        /// retail's answer in double precision and handing back a rounding delta.
        /// </summary>
        public static bool TrySmooth(
            SplineRotationSample from,
            SplineRotationSample to,
            bool hasPrevious,
            SplineRotationSample previous,
            bool hasNext,
            SplineRotationSample next,
            double param,
            out SplineRotationSample result)
        {
            result = to;

            if (!hasPrevious && !hasNext)
            {
                return false;
            }
            if (!IsFinite(param) || !IsUsable(from) || !IsUsable(to))
            {
                return false;
            }

            double h = to.Time - from.Time;
            if (!(h > 0.0) || !IsFinite(h))
            {
                return false;
            }

            Normalise(ref from);
            Normalise(ref to);
            AlignTo(from, ref to);
            if (AngleDegrees(from, to) > MaxSegmentDegrees)
            {
                return false;
            }

            // h_prev for the `from` point: the interval that ENDS at it.
            double hPrevious = 0.0;
            if (hasPrevious)
            {
                hPrevious = from.Time - previous.Time;
                hasPrevious = IsUsable(previous)
                    && hPrevious > 0.0
                    && hPrevious <= h * MaxNeighbourIntervalRatio;
                if (hasPrevious)
                {
                    Normalise(ref previous);
                    AlignTo(from, ref previous);
                    hasPrevious = AngleDegrees(from, previous) <= MaxSegmentDegrees;
                }
            }

            // h_next for the `to` point: the interval that STARTS at it.
            double hNext = 0.0;
            if (hasNext)
            {
                hNext = next.Time - to.Time;
                hasNext = IsUsable(next)
                    && hNext > 0.0
                    && hNext <= h * MaxNeighbourIntervalRatio;
                if (hasNext)
                {
                    Normalise(ref next);
                    AlignTo(to, ref next);
                    hasNext = AngleDegrees(to, next) <= MaxSegmentDegrees;
                }
            }

            // Re-check AFTER validation: a rejected neighbour on each side leaves
            // nothing to derive a tangent from, and stock slerp is the answer.
            if (!hasPrevious && !hasNext)
            {
                return false;
            }

            SplineRotationSample innerFrom = from;
            if (hasPrevious)
            {
                innerFrom = InnerControlPoint(from, to, previous, hPrevious, h);
            }

            SplineRotationSample innerTo = to;
            if (hasNext)
            {
                innerTo = InnerControlPoint(to, next, from, h, hNext);
            }

            if (!IsUsable(innerFrom) || !IsUsable(innerTo))
            {
                return false;
            }

            SplineRotationSample onChord = Slerp(from, to, param);
            SplineRotationSample onInner = Slerp(innerFrom, innerTo, param);
            SplineRotationSample smoothed = Slerp(onChord, onInner, 2.0 * param * (1.0 - param));

            if (!IsUsable(smoothed))
            {
                return false;
            }

            Normalise(ref smoothed);
            smoothed.Time = to.Time;
            result = smoothed;
            return true;
        }

        /// <summary>
        /// The inner control quaternion at <paramref name="q"/>, whose neighbours
        /// are <paramref name="ahead"/> across <paramref name="intervalAhead"/> and
        /// <paramref name="behind"/> across <paramref name="intervalBehind"/>.
        ///
        /// The two weights are what make this correct for uneven stamps. Under a
        /// constant angular rate the ahead log is +w*intervalAhead and the behind
        /// log is -w*intervalBehind; weighting them by the OPPOSITE interval over
        /// the total makes the sum cancel to zero for ANY spacing, so s = q and
        /// squad reproduces slerp exactly. Uniform spacing reduces the pair to the
        /// familiar (log_ahead + log_behind)/4.
        /// </summary>
        private static SplineRotationSample InnerControlPoint(
            SplineRotationSample q,
            SplineRotationSample ahead,
            SplineRotationSample behind,
            double intervalBehind,
            double intervalAhead)
        {
            double span = intervalBehind + intervalAhead;
            double weightAhead = intervalBehind / span;
            double weightBehind = intervalAhead / span;

            double ax, ay, az;
            double bx, by, bz;
            Log(Multiply(Conjugate(q), ahead), out ax, out ay, out az);
            Log(Multiply(Conjugate(q), behind), out bx, out by, out bz);

            double tx = -(weightAhead * ax + weightBehind * bx) * 0.5;
            double ty = -(weightAhead * ay + weightBehind * by) * 0.5;
            double tz = -(weightAhead * az + weightBehind * bz) * 0.5;

            SplineRotationSample inner = Multiply(q, Exp(tx, ty, tz));
            inner.Time = q.Time;
            Normalise(ref inner);
            return inner;
        }

        private static SplineRotationSample Slerp(
            SplineRotationSample a, SplineRotationSample b, double t)
        {
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0.0)
            {
                b = Negate(b);
                dot = -dot;
            }
            if (dot > LerpFallbackDot)
            {
                SplineRotationSample near = new SplineRotationSample(
                    a.Time,
                    a.X + (b.X - a.X) * t,
                    a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t,
                    a.W + (b.W - a.W) * t);
                Normalise(ref near);
                return near;
            }

            if (dot > 1.0) dot = 1.0;
            double theta = Math.Acos(dot);
            double sinTheta = Math.Sin(theta);
            double wa = Math.Sin((1.0 - t) * theta) / sinTheta;
            double wb = Math.Sin(t * theta) / sinTheta;
            return new SplineRotationSample(
                a.Time,
                a.X * wa + b.X * wb,
                a.Y * wa + b.Y * wb,
                a.Z * wa + b.Z * wb,
                a.W * wa + b.W * wb);
        }

        /// <summary>Logarithm of a UNIT quaternion, as the pure-imaginary vector part.</summary>
        private static void Log(SplineRotationSample q, out double x, out double y, out double z)
        {
            double magnitude = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            if (magnitude < UnitEpsilon)
            {
                x = 0.0;
                y = 0.0;
                z = 0.0;
                return;
            }
            // Callers hemisphere-align first, so w >= 0 and this angle is the
            // short way round; atan2 rather than acos(w) keeps it accurate for
            // the very small deltas a 240 ms turn actually produces.
            double angle = Math.Atan2(magnitude, q.W);
            double scale = angle / magnitude;
            x = q.X * scale;
            y = q.Y * scale;
            z = q.Z * scale;
        }

        private static SplineRotationSample Exp(double x, double y, double z)
        {
            double angle = Math.Sqrt(x * x + y * y + z * z);
            if (angle < UnitEpsilon)
            {
                return new SplineRotationSample(0.0, 0.0, 0.0, 0.0, 1.0);
            }
            double scale = Math.Sin(angle) / angle;
            return new SplineRotationSample(0.0, x * scale, y * scale, z * scale, Math.Cos(angle));
        }

        private static SplineRotationSample Multiply(
            SplineRotationSample a, SplineRotationSample b)
        {
            return new SplineRotationSample(
                a.Time,
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
                a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        private static SplineRotationSample Conjugate(SplineRotationSample q)
        {
            return new SplineRotationSample(q.Time, -q.X, -q.Y, -q.Z, q.W);
        }

        private static SplineRotationSample Negate(SplineRotationSample q)
        {
            return new SplineRotationSample(q.Time, -q.X, -q.Y, -q.Z, -q.W);
        }

        /// <summary>Puts <paramref name="q"/> in the same hemisphere as <paramref name="reference"/>.</summary>
        private static void AlignTo(SplineRotationSample reference, ref SplineRotationSample q)
        {
            double dot = reference.X * q.X + reference.Y * q.Y
                + reference.Z * q.Z + reference.W * q.W;
            if (dot < 0.0)
            {
                q = Negate(q);
            }
        }

        /// <summary>Angle in degrees between two aligned unit quaternions.</summary>
        private static double AngleDegrees(SplineRotationSample a, SplineRotationSample b)
        {
            double dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0.0) dot = -dot;
            if (dot > 1.0) dot = 1.0;
            return 2.0 * Math.Acos(dot) * (180.0 / Math.PI);
        }

        private static void Normalise(ref SplineRotationSample q)
        {
            double magnitude = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (magnitude < UnitEpsilon)
            {
                return;
            }
            double scale = 1.0 / magnitude;
            q.X *= scale;
            q.Y *= scale;
            q.Z *= scale;
            q.W *= scale;
        }

        private static bool IsUsable(SplineRotationSample q)
        {
            if (!IsFinite(q.Time) || !IsFinite(q.X) || !IsFinite(q.Y)
                || !IsFinite(q.Z) || !IsFinite(q.W))
            {
                return false;
            }
            double square = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
            return square > UnitEpsilon;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
