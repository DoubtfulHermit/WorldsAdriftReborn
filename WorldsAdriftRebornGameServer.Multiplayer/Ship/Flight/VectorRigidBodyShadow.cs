using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Engine-free, double-precision vector used by the flight SHADOW. Axes are
    /// Unity hull-local axes: +X right, +Y up, +Z forward. It deliberately has no
    /// conversion to a live component or 1130 control point.
    /// </summary>
    public readonly struct ShadowVector3 : IEquatable<ShadowVector3>
    {
        public static readonly ShadowVector3 Zero = new ShadowVector3(0.0, 0.0, 0.0);
        public static readonly ShadowVector3 Right = new ShadowVector3(1.0, 0.0, 0.0);
        public static readonly ShadowVector3 Up = new ShadowVector3(0.0, 1.0, 0.0);
        public static readonly ShadowVector3 Forward = new ShadowVector3(0.0, 0.0, 1.0);

        public ShadowVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
        public double SqrMagnitude => X * X + Y * Y + Z * Z;
        public double Magnitude => Math.Sqrt(SqrMagnitude);

        public static ShadowVector3 operator +(ShadowVector3 a, ShadowVector3 b) =>
            new ShadowVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static ShadowVector3 operator -(ShadowVector3 a, ShadowVector3 b) =>
            new ShadowVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static ShadowVector3 operator -(ShadowVector3 v) =>
            new ShadowVector3(-v.X, -v.Y, -v.Z);
        public static ShadowVector3 operator *(ShadowVector3 v, double scale) =>
            new ShadowVector3(v.X * scale, v.Y * scale, v.Z * scale);
        public static ShadowVector3 operator *(double scale, ShadowVector3 v) => v * scale;
        public static ShadowVector3 operator /(ShadowVector3 v, double divisor) =>
            new ShadowVector3(v.X / divisor, v.Y / divisor, v.Z / divisor);

        public static double Dot(ShadowVector3 a, ShadowVector3 b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>Right-handed cross product, identical to Unity Vector3.Cross.</summary>
        public static ShadowVector3 Cross(ShadowVector3 a, ShadowVector3 b) =>
            new ShadowVector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public ShadowVector3 NormalizedOrZero()
        {
            double magnitude = Magnitude;
            return IsFinite && magnitude > VectorRigidBodyShadowPolicy.VectorEpsilon
                ? this / magnitude
                : Zero;
        }

        public bool Equals(ShadowVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object? obj) => obj is ShadowVector3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }

    /// <summary>Normalised hull-local rotation, W-first like Quaternion32Packing.</summary>
    public readonly struct ShadowQuaternion
    {
        private ShadowQuaternion(double w, double x, double y, double z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public static ShadowQuaternion Identity => new ShadowQuaternion(1.0, 0.0, 0.0, 0.0);
        public double W { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public bool IsValid
        {
            get
            {
                double squared = W * W + X * X + Y * Y + Z * Z;
                return double.IsFinite(squared) && Math.Abs(squared - 1.0) <= 1e-12;
            }
        }

        public static bool TryNormalized(double w, double x, double y, double z, out ShadowQuaternion value)
        {
            double squared = w * w + x * x + y * y + z * z;
            if (!double.IsFinite(squared) || squared <= VectorRigidBodyShadowPolicy.VectorEpsilon)
            {
                value = Identity;
                return false;
            }

            double inverse = 1.0 / Math.Sqrt(squared);
            value = new ShadowQuaternion(w * inverse, x * inverse, y * inverse, z * inverse);
            return true;
        }

        public static ShadowQuaternion FromAxisAngle(ShadowVector3 axis, double radians)
        {
            ShadowVector3 normal = axis.NormalizedOrZero();
            if (normal.Equals(ShadowVector3.Zero) || !double.IsFinite(radians))
            {
                return Identity;
            }

            double half = radians * 0.5;
            double sin = Math.Sin(half);
            TryNormalized(Math.Cos(half), normal.X * sin, normal.Y * sin, normal.Z * sin, out ShadowQuaternion q);
            return q;
        }

        public ShadowVector3 Rotate(ShadowVector3 vector)
        {
            // q*v*q^-1, expanded to avoid allocations and preserve operation order.
            ShadowVector3 qv = new ShadowVector3(X, Y, Z);
            ShadowVector3 twiceCross = 2.0 * ShadowVector3.Cross(qv, vector);
            return vector + W * twiceCross + ShadowVector3.Cross(qv, twiceCross);
        }

        /// <summary>The inverse rotation of a unit quaternion (its conjugate).</summary>
        public ShadowQuaternion Conjugated() => new ShadowQuaternion(W, -X, -Y, -Z);

        /// <summary>Rotates a WORLD vector into this orientation's local frame.</summary>
        public ShadowVector3 InverseRotate(ShadowVector3 vector) => Conjugated().Rotate(vector);

        /// <summary>
        /// Hamilton product a*b: applying b first, then a - the same composition
        /// order Unity's Quaternion operator* uses. The result is renormalised so
        /// long integration chains cannot drift off the unit sphere.
        /// </summary>
        public static bool TryMultiply(ShadowQuaternion a, ShadowQuaternion b, out ShadowQuaternion value)
        {
            double w = a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z;
            double x = a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y;
            double y = a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X;
            double z = a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W;
            return TryNormalized(w, x, y, z, out value);
        }
    }

    /// <summary>Hard safety bounds for untrusted or corrupted mounted-part geometry.</summary>
    public static class VectorRigidBodyShadowPolicy
    {
        public const int MaxParts = 256;
        public const double MaxMountOffsetMetres = 256.0;
        public const double MaxPartMassKg = 100000.0;
        public const double MaxForceNewtons = 100000000.0;
        public const double VectorEpsilon = 1e-12;

        // RECOVERED from ShipMotionVisualizer.LateUpdate.
        public const double RetailTorqueDeadZoneNewtonMetres = 2500.0;
        public const double RetailTorqueScale = 0.5;
    }

    public enum ShadowPartKind
    {
        Engine,
        Sail
    }

    /// <summary>
    /// One validated propulsor. Power remains explicitly WAREBORN tuning because
    /// retail received the final EngineState/SailState values from the lost GSim.
    /// Geometry is recovered: position and rotation are hull-local metres/quaternion.
    /// </summary>
    public readonly struct ShadowPropulsor
    {
        public ShadowPropulsor(ShadowPartKind kind, ShadowVector3 localPosition,
            ShadowQuaternion localRotation, double power, double massKg, bool torqueless = false)
        {
            Kind = kind;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            Power = power;
            MassKg = massKg;
            Torqueless = torqueless;
        }

        public ShadowPartKind Kind { get; }
        public ShadowVector3 LocalPosition { get; }
        public ShadowQuaternion LocalRotation { get; }
        public double Power { get; }
        public double MassKg { get; }
        public bool Torqueless { get; }

        public bool IsValid => (Kind == ShadowPartKind.Engine || Kind == ShadowPartKind.Sail)
            && LocalRotation.IsValid
            && LocalPosition.IsFinite
            && LocalPosition.Magnitude <= VectorRigidBodyShadowPolicy.MaxMountOffsetMetres
            && double.IsFinite(Power) && Power >= 0.0
            && Power <= VectorRigidBodyShadowPolicy.MaxForceNewtons
            && double.IsFinite(MassKg) && MassKg >= 0.0
            && MassKg <= VectorRigidBodyShadowPolicy.MaxPartMassKg;
    }

    /// <summary>
    /// Deterministic point-mass approximation. Retail let Unity calculate the real
    /// collider inertia tensor; those collider meshes and the GSim mass distribution
    /// are not available server-side, so this is labelled APPROXIMATION rather than
    /// recovered physics.
    /// </summary>
    public readonly struct ShadowMassProperties
    {
        public ShadowMassProperties(double totalMassKg, ShadowVector3 centreOfMass,
            ShadowVector3 diagonalInertiaKgM2, bool isApproximation)
        {
            TotalMassKg = totalMassKg;
            CentreOfMass = centreOfMass;
            DiagonalInertiaKgM2 = diagonalInertiaKgM2;
            IsApproximation = isApproximation;
        }

        public double TotalMassKg { get; }
        public ShadowVector3 CentreOfMass { get; }
        public ShadowVector3 DiagonalInertiaKgM2 { get; }
        public bool IsApproximation { get; }

        public static bool TryEstimate(double hullMassKg, ShadowVector3 hullHalfExtentsMetres,
            IReadOnlyList<ShadowPropulsor> parts, out ShadowMassProperties properties)
        {
            properties = default;
            if (!double.IsFinite(hullMassKg) || hullMassKg <= 0.0
                || !hullHalfExtentsMetres.IsFinite
                || hullHalfExtentsMetres.X <= 0.0 || hullHalfExtentsMetres.Y <= 0.0 || hullHalfExtentsMetres.Z <= 0.0
                || parts == null || parts.Count > VectorRigidBodyShadowPolicy.MaxParts)
            {
                return false;
            }

            double totalMass = hullMassKg;
            ShadowVector3 weightedPosition = ShadowVector3.Zero;
            for (int i = 0; i < parts.Count; i++)
            {
                ShadowPropulsor part = parts[i];
                if (!part.IsValid)
                {
                    return false;
                }
                totalMass += part.MassKg;
                weightedPosition += part.LocalPosition * part.MassKg;
            }
            if (!double.IsFinite(totalMass) || totalMass <= 0.0)
            {
                return false;
            }

            ShadowVector3 com = weightedPosition / totalMass;
            // Solid cuboid about its centre: Ixx=1/3*m*(hy^2+hz^2) for half-extents.
            double ixx = hullMassKg / 3.0 * (hullHalfExtentsMetres.Y * hullHalfExtentsMetres.Y
                + hullHalfExtentsMetres.Z * hullHalfExtentsMetres.Z);
            double iyy = hullMassKg / 3.0 * (hullHalfExtentsMetres.X * hullHalfExtentsMetres.X
                + hullHalfExtentsMetres.Z * hullHalfExtentsMetres.Z);
            double izz = hullMassKg / 3.0 * (hullHalfExtentsMetres.X * hullHalfExtentsMetres.X
                + hullHalfExtentsMetres.Y * hullHalfExtentsMetres.Y);

            // Shift hull point from origin to aggregate COM, then add part point masses.
            AddPointMassInertia(-com, hullMassKg, ref ixx, ref iyy, ref izz);
            for (int i = 0; i < parts.Count; i++)
            {
                ShadowVector3 relative = parts[i].LocalPosition - com;
                AddPointMassInertia(relative, parts[i].MassKg, ref ixx, ref iyy, ref izz);
            }

            if (!double.IsFinite(ixx) || !double.IsFinite(iyy) || !double.IsFinite(izz))
            {
                return false;
            }
            properties = new ShadowMassProperties(totalMass, com,
                new ShadowVector3(ixx, iyy, izz), isApproximation: true);
            return true;
        }

        private static void AddPointMassInertia(ShadowVector3 r, double mass,
            ref double ixx, ref double iyy, ref double izz)
        {
            ixx += mass * (r.Y * r.Y + r.Z * r.Z);
            iyy += mass * (r.X * r.X + r.Z * r.Z);
            izz += mass * (r.X * r.X + r.Y * r.Y);
        }
    }

    /// <summary>Pure force/torque accumulator. It owns no clock and advances no state.</summary>
    public sealed class ShadowForceAccumulator
    {
        private int _forceCount;
        private ShadowVector3 _force;
        private ShadowVector3 _rawTorque;

        public ShadowVector3 ForceNewtons => _force;
        public ShadowVector3 RawTorqueNewtonMetres => _rawTorque;
        public int ForceCount => _forceCount;

        public bool TryAdd(ShadowVector3 forceNewtons, ShadowVector3 localPosition,
            ShadowVector3 centreOfMass, bool torqueless)
        {
            if (_forceCount >= VectorRigidBodyShadowPolicy.MaxParts
                || !forceNewtons.IsFinite || !localPosition.IsFinite || !centreOfMass.IsFinite
                || forceNewtons.Magnitude > VectorRigidBodyShadowPolicy.MaxForceNewtons
                || localPosition.Magnitude > VectorRigidBodyShadowPolicy.MaxMountOffsetMetres)
            {
                return false;
            }

            _force += forceNewtons;
            if (!torqueless)
            {
                _rawTorque += ShadowVector3.Cross(localPosition - centreOfMass, forceNewtons);
            }
            _forceCount++;
            return _force.IsFinite && _rawTorque.IsFinite;
        }

        /// <summary>
        /// RECOVERED retail output: suppress X/roll torque, subtract a 2500 N·m
        /// radial dead zone, then halve the remainder.
        /// </summary>
        public ShadowVector3 RetailFilteredTorque()
        {
            ShadowVector3 filtered = new ShadowVector3(0.0, _rawTorque.Y, _rawTorque.Z);
            double magnitude = filtered.Magnitude;
            if (!double.IsFinite(magnitude) || magnitude <= VectorRigidBodyShadowPolicy.RetailTorqueDeadZoneNewtonMetres)
            {
                return ShadowVector3.Zero;
            }
            double scale = (magnitude - VectorRigidBodyShadowPolicy.RetailTorqueDeadZoneNewtonMetres)
                / magnitude * VectorRigidBodyShadowPolicy.RetailTorqueScale;
            return filtered * scale;
        }
    }

    public readonly struct VectorRigidBodyShadowResult
    {
        public VectorRigidBodyShadowResult(ShadowMassProperties mass, ShadowVector3 force,
            ShadowVector3 rawTorque, ShadowVector3 retailTorque, int acceptedParts, int rejectedParts)
        {
            Mass = mass;
            ForceNewtons = force;
            RawTorqueNewtonMetres = rawTorque;
            RetailTorqueNewtonMetres = retailTorque;
            AcceptedParts = acceptedParts;
            RejectedParts = rejectedParts;
        }

        public ShadowMassProperties Mass { get; }
        public ShadowVector3 ForceNewtons { get; }
        public ShadowVector3 RawTorqueNewtonMetres { get; }
        public ShadowVector3 RetailTorqueNewtonMetres { get; }
        public int AcceptedParts { get; }
        public int RejectedParts { get; }
    }

    public readonly struct ForceModelComparison
    {
        public ForceModelComparison(double scalarEngineNewtons, double scalarSailNewtons,
            double shadowForwardNewtons, double shadowLateralNewtons, double shadowVerticalNewtons)
        {
            ScalarEngineNewtons = scalarEngineNewtons;
            ScalarSailNewtons = scalarSailNewtons;
            ShadowForwardNewtons = shadowForwardNewtons;
            ShadowLateralNewtons = shadowLateralNewtons;
            ShadowVerticalNewtons = shadowVerticalNewtons;
        }

        public double ScalarEngineNewtons { get; }
        public double ScalarSailNewtons { get; }
        public double ScalarTotalNewtons => ScalarEngineNewtons + ScalarSailNewtons;
        public double ShadowForwardNewtons { get; }
        public double ShadowLateralNewtons { get; }
        public double ShadowVerticalNewtons { get; }
        public double ForwardDeltaNewtons => ShadowForwardNewtons - ScalarTotalNewtons;
    }

    /// <summary>
    /// Engine/sail force geometry in observation-only form. There is intentionally
    /// no environment switch and no integration method: callers may record the
    /// answer, but this type cannot move a live hull.
    /// </summary>
    public static class VectorRigidBodyShadow
    {
        public static bool TryEvaluate(double hullMassKg, ShadowVector3 hullHalfExtentsMetres,
            IReadOnlyList<ShadowPropulsor> parts, double engineSpin,
            ShadowVector3 hullLocalWind, out VectorRigidBodyShadowResult result)
        {
            result = default;
            if (parts == null || parts.Count > VectorRigidBodyShadowPolicy.MaxParts
                || !double.IsFinite(engineSpin) || engineSpin < -1.0 || engineSpin > 1.0
                || !hullLocalWind.IsFinite
                || !ShadowMassProperties.TryEstimate(hullMassKg, hullHalfExtentsMetres, parts, out ShadowMassProperties mass))
            {
                return false;
            }

            var accumulator = new ShadowForceAccumulator();
            int accepted = 0;
            int rejected = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                ShadowPropulsor part = parts[i];
                ShadowVector3 force = part.Kind == ShadowPartKind.Engine
                    ? EngineForce(part, engineSpin)
                    : TrimmedSailForce(part, hullLocalWind);
                bool ok = accumulator.TryAdd(force, part.LocalPosition, mass.CentreOfMass,
                    part.Kind == ShadowPartKind.Engine && part.Torqueless);
                if (ok) accepted++; else rejected++;
            }

            result = new VectorRigidBodyShadowResult(mass, accumulator.ForceNewtons,
                accumulator.RawTorqueNewtonMetres, accumulator.RetailFilteredTorque(), accepted, rejected);
            return rejected == 0;
        }

        /// <summary>RECOVERED equation; per-engine Power is WAREBORN tuning.</summary>
        public static ShadowVector3 EngineForce(ShadowPropulsor engine, double currentPercentSpin)
        {
            if (!engine.IsValid || engine.Kind != ShadowPartKind.Engine || !double.IsFinite(currentPercentSpin))
            {
                return ShadowVector3.Zero;
            }
            double spin = Math.Clamp(currentPercentSpin, -1.0, 1.0);
            return engine.LocalRotation.Rotate(ShadowVector3.Forward)
                * (ShipForceModel.ShipThrustMultiplier * spin * engine.Power);
        }

        /// <summary>
        /// RECOVERED SailBehaviour + ShipMotionVisualizer.AddSailForce shape;
        /// SailState.Power remains lost-GSim data supplied as WAREBORN tuning.
        /// </summary>
        public static ShadowVector3 SailForce(ShadowPropulsor sail, ShadowVector3 localWind)
        {
            if (!sail.IsValid || sail.Kind != ShadowPartKind.Sail || !localWind.IsFinite)
            {
                return ShadowVector3.Zero;
            }
            // RECOVERED and slightly surprising: SailBehaviour substitutes local
            // +Z for exact calm and normalises every sub-1 m/s wind to unit speed.
            // Do this before both efficiency and minimum-power calculations.
            ShadowVector3 effectiveWind = localWind;
            if (effectiveWind.SqrMagnitude < 1.0)
            {
                effectiveWind = effectiveWind.SqrMagnitude < 1e-5
                    ? ShadowVector3.Forward
                    : effectiveWind.NormalizedOrZero();
            }

            double windMagnitude = effectiveWind.Magnitude;
            ShadowVector3 windNormal = effectiveWind / windMagnitude;
            ShadowVector3 yawRight = sail.LocalRotation.Rotate(ShadowVector3.Right).NormalizedOrZero();
            double efficiency = Math.Abs(ShadowVector3.Dot(windNormal, yawRight));
            ShadowVector3 lift = yawRight * (efficiency * windMagnitude * sail.Power);
            if (ShadowVector3.Dot(effectiveWind, lift) < 0.0)
            {
                lift = -lift;
            }

            // Retail's constant-false branch projects hull-right out before applying.
            ShadowVector3 force = lift - ShadowVector3.Right * ShadowVector3.Dot(ShadowVector3.Right, lift);
            double minimum = ShipForceModel.SailMinEfficiency * windMagnitude * sail.Power;
            if (force.SqrMagnitude < minimum * minimum)
            {
                ShadowVector3 normal = force.NormalizedOrZero();
                force = normal * minimum;
            }
            return force;
        }

        /// <summary>
        /// RECOVERED equilibrium sail trim. SailBehaviour turns the yaw joint
        /// toward LookRotation(base.forward*1.01-wind.normalized), flattened in
        /// the mounted sail's local frame with Slerp(current,target,6*deltaTime).
        /// The server does not own that render-step state. The scalar force model
        /// and this comparison shadow therefore evaluate the same final target;
        /// this is not a claim to reproduce transient joint motion or flutter.
        /// </summary>
        public static ShadowVector3 TrimmedSailForce(
            ShadowPropulsor sail, ShadowVector3 localWind)
        {
            if (!sail.IsValid || sail.Kind != ShadowPartKind.Sail || !localWind.IsFinite)
            {
                return ShadowVector3.Zero;
            }

            ShadowVector3 effectiveWind = localWind;
            if (effectiveWind.SqrMagnitude < 1.0)
            {
                effectiveWind = effectiveWind.SqrMagnitude < 1e-5
                    ? ShadowVector3.Forward
                    : effectiveWind.NormalizedOrZero();
            }

            ShadowVector3 windNormal = effectiveWind.NormalizedOrZero();
            ShadowVector3 baseRight = sail.LocalRotation.Rotate(ShadowVector3.Right);
            ShadowVector3 baseUp = sail.LocalRotation.Rotate(ShadowVector3.Up);
            ShadowVector3 baseForward = sail.LocalRotation.Rotate(ShadowVector3.Forward);

            // InverseTransformDirection without constructing another quaternion:
            // the components in an orthonormal basis are its three dot products.
            ShadowVector3 windInSail = new(
                ShadowVector3.Dot(windNormal, baseRight),
                ShadowVector3.Dot(windNormal, baseUp),
                ShadowVector3.Dot(windNormal, baseForward));
            ShadowVector3 targetForward = new ShadowVector3(
                -windInSail.X, 0.0, 1.01 - windInSail.Z).NormalizedOrZero();
            if (targetForward.Equals(ShadowVector3.Zero))
            {
                return ShadowVector3.Zero;
            }

            ShadowVector3 trimmedLocalRight = new(
                targetForward.Z, 0.0, -targetForward.X);
            ShadowVector3 trimmedRight = sail.LocalRotation.Rotate(trimmedLocalRight)
                .NormalizedOrZero();
            double windMagnitude = effectiveWind.Magnitude;
            double efficiency = Math.Abs(ShadowVector3.Dot(windNormal, trimmedRight));
            ShadowVector3 lift = trimmedRight * (efficiency * windMagnitude * sail.Power);
            if (ShadowVector3.Dot(effectiveWind, lift) < 0.0) lift = -lift;

            ShadowVector3 force = lift
                - ShadowVector3.Right * ShadowVector3.Dot(ShadowVector3.Right, lift);
            double minimum = ShipForceModel.SailMinEfficiency * windMagnitude * sail.Power;
            if (force.SqrMagnitude < minimum * minimum)
            {
                force = force.NormalizedOrZero() * minimum;
            }
            return force;
        }

        public static ForceModelComparison Compare(ShipForceEvaluation scalar,
            VectorRigidBodyShadowResult shadow)
        {
            return new ForceModelComparison(scalar.EngineForceNewtons, scalar.SailForceNewtons,
                shadow.ForceNewtons.Z, shadow.ForceNewtons.X, shadow.ForceNewtons.Y);
        }
    }
}
