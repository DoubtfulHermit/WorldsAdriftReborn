using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>The authoritative lifecycle missing from the current radius snap.</summary>
    public enum DockingPhase
    {
        Undocked = 0,
        Approaching = 1,
        Captured = 2,
        Docked = 3,
        Departing = 4
    }

    public enum DockingPropulsion
    {
        None = 0,
        Sail = 1,
        Engine = 2,
        SailAndEngine = 3
    }

    public enum DockingRejectReason
    {
        None = 0,
        InvalidEntity,
        InvalidState,
        YardUnavailable,
        Unauthorized,
        PropulsionActive,
        OutsideApproachRadius,
        ApproachTooFast,
        CollisionBlocked,
        YardOccupied,
        HullAlreadyLinked,
        StaleClaim,
        InvalidSnapshot
    }

    /// <summary>Engine-free authoritative pose, in metres and radians.</summary>
    public readonly struct DockingPose : IEquatable<DockingPose>
    {
        public DockingPose(double x, double y, double z, double yawRadians)
        {
            X = x; Y = y; Z = z; YawRadians = NormalizeYaw(yawRadians);
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double YawRadians { get; }
        public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z) && Finite(YawRadians);

        public double DistanceTo(DockingPose other)
        {
            double dx = other.X - X, dy = other.Y - Y, dz = other.Z - Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Retail's docked client uses Lerp/Slerp with 5 * fixedDeltaTime. This
        /// engine-free equivalent uses the shortest yaw arc and clamps large steps.
        /// </summary>
        public DockingPose InterpolateToward(DockingPose target, double ratePerSecond,
            double deltaSeconds)
        {
            double t = Math.Clamp(ratePerSecond * deltaSeconds, 0.0, 1.0);
            double yawDelta = NormalizeYaw(target.YawRadians - YawRadians);
            return new DockingPose(
                X + (target.X - X) * t,
                Y + (target.Y - Y) * t,
                Z + (target.Z - Z) * t,
                YawRadians + yawDelta * t);
        }

        public bool Equals(DockingPose other) => X.Equals(other.X) && Y.Equals(other.Y)
            && Z.Equals(other.Z) && YawRadians.Equals(other.YawRadians);
        public override bool Equals(object? obj) => obj is DockingPose pose && Equals(pose);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, YawRadians);

        internal static double NormalizeYaw(double radians)
        {
            if (!Finite(radians)) return radians;
            double wrapped = radians % (Math.PI * 2.0);
            if (wrapped > Math.PI) wrapped -= Math.PI * 2.0;
            if (wrapped < -Math.PI) wrapped += Math.PI * 2.0;
            return wrapped;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public readonly struct DockingMotion
    {
        public DockingMotion(double vx, double vy, double vz, double angularSpeedRadiansPerSecond)
        {
            Vx = vx; Vy = vy; Vz = vz; AngularSpeedRadiansPerSecond = angularSpeedRadiansPerSecond;
        }

        public double Vx { get; }
        public double Vy { get; }
        public double Vz { get; }
        public double AngularSpeedRadiansPerSecond { get; }
        public double LinearSpeed => Math.Sqrt(Vx * Vx + Vy * Vy + Vz * Vz);
        public bool IsFinite => Finite(Vx) && Finite(Vy) && Finite(Vz)
            && Finite(AngularSpeedRadiansPerSecond);
        public static DockingMotion Frozen => default;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Injectable policy values. Impact radius 35 m and interpolation rate 5/s are
    /// recovered client defaults. The other values are conservative Wareborn tuning
    /// because retail's server/serialized values did not survive.
    /// </summary>
    public sealed class DockingTuning
    {
        public double ApproachRadiusMetres { get; init; } = 35.0; // recovered Shipyard.ImpactRadius default
        public double CaptureRadiusMetres { get; init; } = 9.0; // existing Wareborn tuning
        public double ReleaseRadiusMetres { get; init; } = 18.0; // existing Wareborn tuning
        public double MaximumCaptureSpeedMetresPerSecond { get; init; } = 2.0; // Wareborn tuning
        public double MaximumCaptureAngularSpeedRadiansPerSecond { get; init; } = 0.25; // Wareborn tuning
        public double DockInterpolationRatePerSecond { get; init; } = 5.0; // recovered client code
        public double PositionSnapToleranceMetres { get; init; } = 0.02; // Wareborn tuning
        public double YawSnapToleranceRadians { get; init; } = 0.002; // Wareborn tuning

        public bool IsValid => Positive(ApproachRadiusMetres) && Positive(CaptureRadiusMetres)
            && ReleaseRadiusMetres > CaptureRadiusMetres
            && Positive(MaximumCaptureSpeedMetresPerSecond)
            && Positive(MaximumCaptureAngularSpeedRadiansPerSecond)
            && Positive(DockInterpolationRatePerSecond)
            && Positive(PositionSnapToleranceMetres) && Positive(YawSnapToleranceRadians);
        private static bool Positive(double value) => value > 0 && !double.IsInfinity(value);
    }

    public readonly struct DockingApproachRequest
    {
        public DockingApproachRequest(long hullEntityId, long yardEntityId,
            string? hullOwner, string? yardOwner, bool crewAuthorized, bool yardAbandoned,
            bool yardExists, bool propulsionNeutral, bool collisionClear,
            DockingPose hullPose, DockingPose targetPose, DockingMotion motion)
        {
            HullEntityId = hullEntityId; YardEntityId = yardEntityId;
            HullOwner = hullOwner; YardOwner = yardOwner;
            CrewAuthorized = crewAuthorized; YardAbandoned = yardAbandoned;
            YardExists = yardExists; PropulsionNeutral = propulsionNeutral;
            CollisionClear = collisionClear; HullPose = hullPose; TargetPose = targetPose;
            Motion = motion;
        }

        public long HullEntityId { get; }
        public long YardEntityId { get; }
        public string? HullOwner { get; }
        public string? YardOwner { get; }
        public bool CrewAuthorized { get; }
        public bool YardAbandoned { get; }
        public bool YardExists { get; }
        public bool PropulsionNeutral { get; }
        public bool CollisionClear { get; }
        public DockingPose HullPose { get; }
        public DockingPose TargetPose { get; }
        public DockingMotion Motion { get; }
    }

    public readonly struct DockingFrame
    {
        public DockingFrame(double deltaSeconds, bool yardExists, bool permissionValid,
            DockingPropulsion propulsion,
            bool collisionClear, bool outsideReleaseEnvelope, DockingPose observedPose,
            DockingMotion observedMotion)
        {
            DeltaSeconds = deltaSeconds; YardExists = yardExists;
            PermissionValid = permissionValid;
            Propulsion = propulsion; CollisionClear = collisionClear;
            OutsideReleaseEnvelope = outsideReleaseEnvelope;
            ObservedPose = observedPose; ObservedMotion = observedMotion;
        }

        public double DeltaSeconds { get; }
        public bool YardExists { get; }
        public bool PermissionValid { get; }
        public DockingPropulsion Propulsion { get; }
        public bool PropulsionNeutral => Propulsion == DockingPropulsion.None;
        public bool CollisionClear { get; }
        public bool OutsideReleaseEnvelope { get; }
        public DockingPose ObservedPose { get; }
        public DockingMotion ObservedMotion { get; }
    }

    public readonly struct DockingStepResult
    {
        public DockingStepResult(DockingPhase phase, DockingPose pose, DockingMotion motion,
            bool freezeVelocity, bool linkReleased, DockingRejectReason reason)
        {
            Phase = phase; Pose = pose; Motion = motion; FreezeVelocity = freezeVelocity;
            LinkReleased = linkReleased; Reason = reason;
        }
        public DockingPhase Phase { get; }
        public DockingPose Pose { get; }
        public DockingMotion Motion { get; }
        public bool FreezeVelocity { get; }
        public bool LinkReleased { get; }
        public DockingRejectReason Reason { get; }
    }

    public static class DockingPermissionPolicy
    {
        /// <summary>Update 30: own/crew-authorized or abandoned yards may accept a ship.</summary>
        public static bool MayApproach(string? hullOwner, string? yardOwner,
            bool crewAuthorized, bool yardAbandoned) =>
            crewAuthorized || yardAbandoned || (!string.IsNullOrEmpty(hullOwner)
                && string.Equals(hullOwner, yardOwner, StringComparison.Ordinal));
    }

    /// <summary>
    /// Pure, engine-free docking aggregate. It owns no client state and emits no
    /// components; later integration adapts these transitions to 1114/1205 and the
    /// collision service after Tracks 2 and 5 land.
    /// </summary>
    public sealed class AuthenticDockingLifecycle
    {
        private readonly DockingTuning _tuning;

        public AuthenticDockingLifecycle(long hullEntityId, DockingTuning? tuning = null)
        {
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            _tuning = tuning ?? new DockingTuning();
            if (!_tuning.IsValid) throw new ArgumentException("Invalid docking tuning.", nameof(tuning));
            HullEntityId = hullEntityId;
        }

        public long HullEntityId { get; }
        public long YardEntityId { get; private set; }
        public DockingPhase Phase { get; private set; }
        public DockingPose Pose { get; private set; }
        public DockingPose TargetPose { get; private set; }
        public DockingMotion Motion { get; private set; }
        public DockingPropulsion DeparturePropulsion { get; private set; }

        public bool TryBeginApproach(DockingApproachRequest request, ShipDockRegistry claims,
            out DockingRejectReason reason)
        {
            reason = ValidateApproach(request);
            if (reason != DockingRejectReason.None) return false;

            ShipDockClaimResult claim = claims.TryClaim(request.YardEntityId, HullEntityId);
            if (claim == ShipDockClaimResult.RejectedYardOccupied)
                reason = DockingRejectReason.YardOccupied;
            else if (claim == ShipDockClaimResult.RejectedHullLinked)
                reason = DockingRejectReason.HullAlreadyLinked;
            else if (claim == ShipDockClaimResult.RejectedInvalidEntity)
                reason = DockingRejectReason.InvalidEntity;
            if (reason != DockingRejectReason.None) return false;

            YardEntityId = request.YardEntityId;
            Phase = DockingPhase.Approaching;
            Pose = request.HullPose;
            TargetPose = request.TargetPose;
            Motion = request.Motion;
            DeparturePropulsion = DockingPropulsion.None;
            return true;
        }

        public DockingStepResult Step(DockingFrame frame, ShipDockRegistry claims)
        {
            if (Phase == DockingPhase.Undocked)
                return Result(false, false, DockingRejectReason.InvalidState);
            if (!FrameIsFinite(frame))
                return Release(claims, DockingRejectReason.InvalidSnapshot);
            if (!frame.YardExists)
                return Release(claims, DockingRejectReason.YardUnavailable);
            if (!frame.PermissionValid)
                return Release(claims, DockingRejectReason.Unauthorized);
            if (claims.DockedShipFor(YardEntityId) != HullEntityId
                || claims.ShipyardForHull(HullEntityId) != YardEntityId)
                return ResetWithoutRelease(DockingRejectReason.StaleClaim);

            if (Phase == DockingPhase.Departing)
            {
                Pose = frame.ObservedPose;
                Motion = frame.ObservedMotion;
                return frame.OutsideReleaseEnvelope
                    ? Release(claims, DockingRejectReason.None)
                    : Result(false, false, DockingRejectReason.None);
            }

            if ((Phase == DockingPhase.Captured || Phase == DockingPhase.Docked)
                && !frame.PropulsionNeutral)
            {
                Phase = DockingPhase.Departing;
                DeparturePropulsion = frame.Propulsion;
                Pose = frame.ObservedPose;
                Motion = frame.ObservedMotion;
                return frame.OutsideReleaseEnvelope
                    ? Release(claims, DockingRejectReason.None)
                    : Result(false, false, DockingRejectReason.None);
            }

            if (!frame.PropulsionNeutral)
                return Release(claims, DockingRejectReason.PropulsionActive);

            if (Phase == DockingPhase.Approaching)
            {
                Pose = frame.ObservedPose;
                Motion = frame.ObservedMotion;
                if (!frame.CollisionClear)
                    return Result(false, false, DockingRejectReason.CollisionBlocked);
                if (Pose.DistanceTo(TargetPose) <= _tuning.CaptureRadiusMetres
                    && Motion.LinearSpeed <= _tuning.MaximumCaptureSpeedMetresPerSecond
                    && Math.Abs(Motion.AngularSpeedRadiansPerSecond)
                        <= _tuning.MaximumCaptureAngularSpeedRadiansPerSecond)
                {
                    Phase = DockingPhase.Captured;
                    Motion = DockingMotion.Frozen;
                    return Result(true, false, DockingRejectReason.None);
                }
                return Result(false, false, DockingRejectReason.None);
            }

            // Captured and docked both hold a zero-velocity authoritative pose. Retail
            // visibly converges the body rather than teleporting it.
            Motion = DockingMotion.Frozen;
            Pose = Pose.InterpolateToward(TargetPose,
                _tuning.DockInterpolationRatePerSecond, frame.DeltaSeconds);
            double yawError = Math.Abs(DockingPose.NormalizeYaw(TargetPose.YawRadians - Pose.YawRadians));
            if (Pose.DistanceTo(TargetPose) <= _tuning.PositionSnapToleranceMetres
                && yawError <= _tuning.YawSnapToleranceRadians)
            {
                Pose = TargetPose;
                Phase = DockingPhase.Docked;
            }
            return Result(true, false, DockingRejectReason.None);
        }

        /// <summary>
        /// Either sail or engine command begins departure. The occupancy reservation
        /// deliberately remains until the collision service confirms the release
        /// envelope is clear, preventing capture/undock churn.
        /// </summary>
        public bool TryBeginDeparture(DockingPropulsion propulsion)
        {
            if ((Phase != DockingPhase.Docked && Phase != DockingPhase.Captured)
                || propulsion == DockingPropulsion.None) return false;
            Phase = DockingPhase.Departing;
            DeparturePropulsion = propulsion;
            return true;
        }

        public DockingStepResult DeleteHull(ShipDockRegistry claims) =>
            Release(claims, DockingRejectReason.None);

        public DockingSnapshotV1 CaptureSnapshot() => DockingSnapshotV1.Capture(this);

        public static bool TryRestore(DockingSnapshotV1 snapshot,
            long restoredHullEntityId, long restoredYardEntityId, ShipDockRegistry claims,
            out AuthenticDockingLifecycle? lifecycle, out DockingRejectReason reason,
            DockingTuning? tuning = null)
        {
            lifecycle = null;
            reason = DockingRejectReason.InvalidSnapshot;
            if (!snapshot.TryRead(out DockingPhase phase, out DockingPose pose,
                    out DockingPose target, out DockingMotion motion,
                    out DockingPropulsion departure)) return false;

            if (restoredHullEntityId <= 0
                || (phase != DockingPhase.Undocked && restoredYardEntityId <= 0)) return false;
            var restored = new AuthenticDockingLifecycle(restoredHullEntityId, tuning);
            if (phase != DockingPhase.Undocked)
            {
                ShipDockClaimResult claim = claims.TryClaim(restoredYardEntityId, restoredHullEntityId);
                if (claim == ShipDockClaimResult.RejectedYardOccupied)
                    reason = DockingRejectReason.YardOccupied;
                else if (claim == ShipDockClaimResult.RejectedHullLinked)
                    reason = DockingRejectReason.HullAlreadyLinked;
                else if (claim == ShipDockClaimResult.RejectedInvalidEntity)
                    reason = DockingRejectReason.InvalidEntity;
                if (claim != ShipDockClaimResult.Claimed
                    && claim != ShipDockClaimResult.AlreadyClaimed) return false;
            }
            restored.YardEntityId = phase == DockingPhase.Undocked ? 0 : restoredYardEntityId;
            restored.Phase = phase;
            restored.Pose = pose;
            restored.TargetPose = target;
            restored.Motion = phase == DockingPhase.Captured || phase == DockingPhase.Docked
                ? DockingMotion.Frozen : motion;
            restored.DeparturePropulsion = departure;
            lifecycle = restored;
            reason = DockingRejectReason.None;
            return true;
        }

        private DockingRejectReason ValidateApproach(DockingApproachRequest request)
        {
            if (request.HullEntityId != HullEntityId || request.YardEntityId <= 0)
                return DockingRejectReason.InvalidEntity;
            if (Phase != DockingPhase.Undocked) return DockingRejectReason.InvalidState;
            if (!request.YardExists) return DockingRejectReason.YardUnavailable;
            if (!DockingPermissionPolicy.MayApproach(request.HullOwner, request.YardOwner,
                    request.CrewAuthorized, request.YardAbandoned))
                return DockingRejectReason.Unauthorized;
            if (!request.PropulsionNeutral) return DockingRejectReason.PropulsionActive;
            if (!request.CollisionClear) return DockingRejectReason.CollisionBlocked;
            if (!request.HullPose.IsFinite || !request.TargetPose.IsFinite || !request.Motion.IsFinite)
                return DockingRejectReason.InvalidSnapshot;
            if (request.HullPose.DistanceTo(request.TargetPose) > _tuning.ApproachRadiusMetres)
                return DockingRejectReason.OutsideApproachRadius;
            if (request.Motion.LinearSpeed > _tuning.MaximumCaptureSpeedMetresPerSecond
                || Math.Abs(request.Motion.AngularSpeedRadiansPerSecond)
                    > _tuning.MaximumCaptureAngularSpeedRadiansPerSecond)
                return DockingRejectReason.ApproachTooFast;
            return DockingRejectReason.None;
        }

        private bool FrameIsFinite(DockingFrame frame) => frame.DeltaSeconds >= 0
            && !double.IsInfinity(frame.DeltaSeconds) && frame.ObservedPose.IsFinite
            && frame.ObservedMotion.IsFinite
            && Enum.IsDefined(typeof(DockingPropulsion), frame.Propulsion);

        private DockingStepResult Release(ShipDockRegistry claims, DockingRejectReason reason)
        {
            bool released = YardEntityId > 0 && claims.Release(YardEntityId, HullEntityId);
            Reset();
            return Result(false, released, reason);
        }

        private DockingStepResult ResetWithoutRelease(DockingRejectReason reason)
        {
            Reset();
            return Result(false, false, reason);
        }

        private void Reset()
        {
            YardEntityId = 0; Phase = DockingPhase.Undocked;
            Motion = DockingMotion.Frozen; DeparturePropulsion = DockingPropulsion.None;
        }

        private DockingStepResult Result(bool freeze, bool released, DockingRejectReason reason) =>
            new DockingStepResult(Phase, Pose, Motion, freeze, released, reason);
    }

    /// <summary>
    /// Additive JSON-friendly docking record. Runtime entity ids deliberately are
    /// NOT persisted: the owning BuiltShipRecord is the stable ship identity and its
    /// existing shipyard-position link resolves the newly allocated hull/yard ids on
    /// restore. Track 2 can add this as a nullable member beside its flight snapshot;
    /// old records remain null and old binaries ignore it.
    /// </summary>
    public sealed class DockingSnapshotV1
    {
        public const int CurrentVersion = 1;
        public int Version { get; set; } = CurrentVersion;
        public int Phase { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double YawRadians { get; set; }
        public double TargetX { get; set; }
        public double TargetY { get; set; }
        public double TargetZ { get; set; }
        public double TargetYawRadians { get; set; }
        public double Vx { get; set; }
        public double Vy { get; set; }
        public double Vz { get; set; }
        public double AngularSpeedRadiansPerSecond { get; set; }
        public int DeparturePropulsion { get; set; }

        public static DockingSnapshotV1 Capture(AuthenticDockingLifecycle lifecycle) => new DockingSnapshotV1
        {
            Phase = (int)lifecycle.Phase,
            X = lifecycle.Pose.X, Y = lifecycle.Pose.Y, Z = lifecycle.Pose.Z,
            YawRadians = lifecycle.Pose.YawRadians,
            TargetX = lifecycle.TargetPose.X, TargetY = lifecycle.TargetPose.Y,
            TargetZ = lifecycle.TargetPose.Z, TargetYawRadians = lifecycle.TargetPose.YawRadians,
            Vx = lifecycle.Motion.Vx, Vy = lifecycle.Motion.Vy, Vz = lifecycle.Motion.Vz,
            AngularSpeedRadiansPerSecond = lifecycle.Motion.AngularSpeedRadiansPerSecond,
            DeparturePropulsion = (int)lifecycle.DeparturePropulsion
        };

        public bool TryRead(out DockingPhase phase, out DockingPose pose,
            out DockingPose target, out DockingMotion motion,
            out DockingPropulsion departure)
        {
            phase = DockingPhase.Undocked; pose = default; target = default;
            motion = default; departure = DockingPropulsion.None;
            if (Version != CurrentVersion || !Enum.IsDefined(typeof(DockingPhase), Phase)
                || !Enum.IsDefined(typeof(DockingPropulsion), DeparturePropulsion)) return false;
            phase = (DockingPhase)Phase;
            pose = new DockingPose(X, Y, Z, YawRadians);
            target = new DockingPose(TargetX, TargetY, TargetZ, TargetYawRadians);
            motion = new DockingMotion(Vx, Vy, Vz, AngularSpeedRadiansPerSecond);
            departure = (DockingPropulsion)DeparturePropulsion;
            if (!pose.IsFinite || !target.IsFinite || !motion.IsFinite) return false;
            if (phase != DockingPhase.Departing && departure != DockingPropulsion.None) return false;
            if (phase == DockingPhase.Departing && departure == DockingPropulsion.None) return false;
            return true;
        }
    }
}
