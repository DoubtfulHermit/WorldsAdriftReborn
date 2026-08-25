using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

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
        InvalidSnapshot,
        /// <summary>
        /// Inside the influence sphere but UNDER the yard's own plane, so not
        /// "above the shipyard". Appended so every value above keeps its number.
        /// </summary>
        BelowShipyard
    }

    /// <summary>Engine-free authoritative pose, in metres and radians.</summary>
    public readonly struct DockingPose : IEquatable<DockingPose>
    {
        public DockingPose(double x, double y, double z, double yawRadians)
        {
            Position = new ShadowVector3(x, y, z);
            YawRadians = NormalizeYaw(yawRadians);
        }

        public ShadowVector3 Position { get; }
        public double X => Position.X;
        public double Y => Position.Y;
        public double Z => Position.Z;
        public double YawRadians { get; }
        public bool IsFinite => Position.IsFinite && Finite(YawRadians);

        public double DistanceTo(DockingPose other)
        {
            return (other.Position - Position).Magnitude;
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
            LinearVelocity = new ShadowVector3(vx, vy, vz);
            AngularSpeedRadiansPerSecond = angularSpeedRadiansPerSecond;
        }

        public ShadowVector3 LinearVelocity { get; }
        public double Vx => LinearVelocity.X;
        public double Vy => LinearVelocity.Y;
        public double Vz => LinearVelocity.Z;
        public double AngularSpeedRadiansPerSecond { get; }
        public double LinearSpeed => LinearVelocity.Magnitude;
        public bool IsFinite => LinearVelocity.IsFinite
            && Finite(AngularSpeedRadiansPerSecond);
        public static DockingMotion Frozen => default;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Injectable policy values. Impact radius 35 m and interpolation rate 5/s are
    /// RECOVERED client defaults. The other values are conservative WAReborn tuning
    /// because retail's server/serialized values did not survive.
    ///
    /// The former <c>CaptureRadiusMetres</c> (9 m) and <c>ReleaseRadiusMetres</c>
    /// (18 m) are gone from this path on purpose: both were WAReborn tuning
    /// standing in for a docking envelope, and the recovered bubble - the influence
    /// dome of <see cref="ApproachRadiusMetres"/> - is the envelope the PLAYER can
    /// actually see. Capture and departure clearance are now decided against that
    /// one visible volume (<see cref="ShipyardBubble"/>) so the server's rule and
    /// the client's dome are the same boundary. The legacy radius-snap path keeps
    /// its own 9 m/18 m constants in <see cref="ShipyardDockingPolicy"/>.
    /// </summary>
    public sealed class DockingTuning
    {
        /// <summary>RECOVERED: <c>Shipyard.ImpactRadius</c> default, 35 m.</summary>
        public double ApproachRadiusMetres { get; init; } = 35.0;

        /// <summary>
        /// WAREBORN TUNING: where "above the shipyard" starts, measured from the
        /// yard's own registered Y. Zero puts the dome floor on the yard's
        /// registration plane, which is where the recovered dock geometry says the
        /// yard sits: a ship built here materialises
        /// <c>BuiltShipPlacement.HoverHeightMetres</c> (3.4 m hull body + 2.6 m
        /// clearance = 6.0 m) directly ABOVE that plane, so anything at or above it
        /// is genuinely over the yard, and the convergence has only ever to settle a
        /// hull DOWN onto the dock pose. Raise this if a live dome shows the visible
        /// hemisphere starting higher.
        /// </summary>
        public double DomeFloorOffsetMetres { get; init; } = 0.0;

        /// <summary>
        /// WAREBORN TUNING: the departure hysteresis band outside the bubble. Entry
        /// tests at exactly <see cref="ApproachRadiusMetres"/>; the link is only cut
        /// past radius + this margin, so a hull hovering on the visible edge cannot
        /// flap between docked and undocked. 2 m is ~6% of the 35 m radius and four
        /// times the 0.48 m a hull covers in one 0.24 s docking scan at the 2 m/s
        /// capture-negotiation ceiling, so one scan can never straddle the band.
        /// </summary>
        public double BubbleExitMarginMetres { get; init; } = 2.0;

        public double MaximumCaptureSpeedMetresPerSecond { get; init; } = 2.0; // WAReborn tuning
        public double MaximumCaptureAngularSpeedRadiansPerSecond { get; init; } = 0.25; // WAReborn tuning
        public double DockInterpolationRatePerSecond { get; init; } = 5.0; // RECOVERED client code
        public double PositionSnapToleranceMetres { get; init; } = 0.02; // WAReborn tuning
        public double YawSnapToleranceRadians { get; init; } = 0.002; // WAReborn tuning

        public bool IsValid => Positive(ApproachRadiusMetres)
            && double.IsFinite(DomeFloorOffsetMetres)
            && double.IsFinite(BubbleExitMarginMetres) && BubbleExitMarginMetres >= 0.0
            && Positive(MaximumCaptureSpeedMetresPerSecond)
            && Positive(MaximumCaptureAngularSpeedRadiansPerSecond)
            && Positive(DockInterpolationRatePerSecond)
            && Positive(PositionSnapToleranceMetres) && Positive(YawSnapToleranceRadians);

        /// <summary>The bubble of one shipyard at its registered world position.</summary>
        public ShipyardBubble BubbleAt(ShadowVector3 yardPosition) =>
            new ShipyardBubble(yardPosition, ApproachRadiusMetres,
                DomeFloorOffsetMetres, BubbleExitMarginMetres);

        private static bool Positive(double value) => value > 0 && !double.IsInfinity(value);
    }

    public readonly struct DockingApproachRequest
    {
        public DockingApproachRequest(long hullEntityId, long yardEntityId,
            string hullStableKey, string yardStableKey,
            string? hullOwner, string? yardOwner, bool crewAuthorized, bool yardAbandoned,
            bool yardExists, bool propulsionNeutral, CollisionClearanceRecord collisionClearance,
            DockingPose hullPose, DockingPose targetPose, DockingMotion motion,
            ShipyardBubble bubble, bool helmManned = false)
        {
            HullEntityId = hullEntityId; YardEntityId = yardEntityId;
            HullStableKey = hullStableKey; YardStableKey = yardStableKey;
            HullOwner = hullOwner; YardOwner = yardOwner;
            CrewAuthorized = crewAuthorized; YardAbandoned = yardAbandoned;
            YardExists = yardExists; PropulsionNeutral = propulsionNeutral;
            CollisionClearance = collisionClearance; HullPose = hullPose; TargetPose = targetPose;
            Motion = motion; Bubble = bubble; HelmManned = helmManned;
        }

        public long HullEntityId { get; }
        public long YardEntityId { get; }
        public string HullStableKey { get; }
        public string YardStableKey { get; }
        public string? HullOwner { get; }
        public string? YardOwner { get; }
        public bool CrewAuthorized { get; }
        public bool YardAbandoned { get; }
        public bool YardExists { get; }
        public bool PropulsionNeutral { get; }
        public CollisionClearanceRecord CollisionClearance { get; }
        public DockingPose HullPose { get; }
        public DockingPose TargetPose { get; }
        public DockingMotion Motion { get; }

        /// <summary>The yard's influence dome - the bubble the player sees.</summary>
        public ShipyardBubble Bubble { get; }

        /// <summary>Whether a pilot currently holds this hull's helm.</summary>
        public bool HelmManned { get; }
    }

    public readonly struct DockingFrame
    {
        public DockingFrame(double deltaSeconds, bool yardExists, bool permissionValid,
            DockingPropulsion propulsion,
            CollisionClearanceRecord collisionClearance, ShipyardBubble bubble,
            DockingPose observedPose, DockingMotion observedMotion,
            bool helmManned = false, double hullClearanceRadiusMetres = 0.0)
        {
            DeltaSeconds = deltaSeconds; YardExists = yardExists;
            PermissionValid = permissionValid;
            Propulsion = propulsion; CollisionClearance = collisionClearance;
            Bubble = bubble;
            ObservedPose = observedPose; ObservedMotion = observedMotion;
            HelmManned = helmManned;
            HullClearanceRadiusMetres = hullClearanceRadiusMetres;
        }

        public double DeltaSeconds { get; }
        public bool YardExists { get; }
        public bool PermissionValid { get; }
        public DockingPropulsion Propulsion { get; }
        public bool PropulsionNeutral => Propulsion == DockingPropulsion.None;
        public CollisionClearanceRecord CollisionClearance { get; }

        /// <summary>The yard's influence dome - the bubble the player sees.</summary>
        public ShipyardBubble Bubble { get; }

        /// <summary>
        /// Whether a pilot currently holds the helm. Docking capture is a HELM
        /// RELEASE event, never mere proximity, and manning a docked ship is not a
        /// departure - only propulsion is.
        /// </summary>
        public bool HelmManned { get; }

        /// <summary>
        /// The hull's own yaw-invariant bounding radius, so "fully outside the
        /// bubble" means the hull's near edge is outside, not just its centre.
        /// Zero when the hull's geometry is unknown.
        /// </summary>
        public double HullClearanceRadiusMetres { get; }

        public DockingPose ObservedPose { get; }
        public DockingMotion ObservedMotion { get; }

        /// <summary>
        /// Departure-completion evidence: the hull is FULLY outside the bubble,
        /// past the hysteresis margin. Derived from the one visible volume rather
        /// than passed in, so no caller can hand the lifecycle a different answer
        /// than the geometry gives.
        /// </summary>
        public bool OutsideReleaseEnvelope =>
            Bubble.HasFullyCleared(ObservedPose.Position, HullClearanceRadiusMetres);
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
    /// components; <see cref="DockingComponentProjection"/> turns its phase into the
    /// 1114/1205 truth the transaction publishes.
    ///
    /// The player-visible contract it implements:
    /// <list type="number">
    /// <item>a ship inside the bubble and above the yard whose pilot LEAVES THE HELM
    /// snaps into the dock pose, and the bubble comes up;</item>
    /// <item>taking the helm back leaves it docked - manning is not departure;</item>
    /// <item>only propulsion starts a departure, and the link (with it the bubble)
    /// drops only once the hull is FULLY outside the bubble.</item>
    /// </list>
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
        public string HullStableKey { get; private set; } = string.Empty;
        public string YardStableKey { get; private set; } = string.Empty;
        public long LastCollisionClearanceStep { get; private set; } = -1;

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
            HullStableKey = request.HullStableKey;
            YardStableKey = request.YardStableKey;
            LastCollisionClearanceStep = request.CollisionClearance.FixedStep;
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
                // A reservation is not a lease: a hull that leaves the bubble
                // entirely while approaching gives the yard back to everyone else.
                if (frame.OutsideReleaseEnvelope)
                    return Release(claims, DockingRejectReason.OutsideApproachRadius);
                if (!ClearanceMatches(frame.CollisionClearance))
                    return Result(false, false, DockingRejectReason.CollisionBlocked);
                LastCollisionClearanceStep = frame.CollisionClearance.FixedStep;
                // CAPTURE IS A HELM-RELEASE EVENT. A ship the player is still flying
                // stays Approaching however close it parks; the snap happens when
                // they leave the wheel with the ship inside the bubble and above the
                // yard, which is when the bubble comes up. An unmanned hull that
                // drifts or is restored into the dome captures for the same reason:
                // nobody is at its helm.
                if (!frame.HelmManned
                    && frame.Bubble.ContainsDock(Pose.Position)
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
        /// Either sail or engine command begins departure - taking the helm back
        /// does NOT. The occupancy reservation deliberately remains until the hull
        /// is FULLY outside the bubble (past the hysteresis margin), so the link
        /// drops exactly when the player sees the dome fall behind them and a hull
        /// hovering on the edge cannot churn.
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
            restored.HullStableKey = snapshot.HullStableKey ?? string.Empty;
            restored.YardStableKey = snapshot.YardStableKey ?? string.Empty;
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
            if (!ClearanceMatches(request.CollisionClearance,
                    request.HullStableKey, request.YardStableKey, -1))
                return DockingRejectReason.CollisionBlocked;
            if (!request.HullPose.IsFinite || !request.TargetPose.IsFinite || !request.Motion.IsFinite)
                return DockingRejectReason.InvalidSnapshot;
            if (!request.Bubble.IsValid) return DockingRejectReason.InvalidSnapshot;
            // The bubble is the approach volume: the recovered 35 m influence sphere
            // about the YARD (Shipyard.IsWithinRange), and only its upper half - a
            // hull passing beneath an island-mounted yard is not approaching it.
            if (!request.Bubble.IsWithinRange(request.HullPose.Position))
                return DockingRejectReason.OutsideApproachRadius;
            if (!request.Bubble.IsAboveYard(request.HullPose.Position))
                return DockingRejectReason.BelowShipyard;
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

        private bool ClearanceMatches(CollisionClearanceRecord clearance) =>
            ClearanceMatches(clearance, HullStableKey, YardStableKey, LastCollisionClearanceStep);

        private static bool ClearanceMatches(CollisionClearanceRecord clearance,
            string hullStableKey, string yardStableKey, long minimumStep) =>
            clearance.IsClear && clearance.FixedStep >= minimumStep
            && string.Equals(clearance.SubjectStableKey, hullStableKey, StringComparison.Ordinal)
            && string.Equals(clearance.ExpectedTargetStableKey, yardStableKey, StringComparison.Ordinal);

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
            HullStableKey = string.Empty; YardStableKey = string.Empty;
            LastCollisionClearanceStep = -1;
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
        public string? HullStableKey { get; set; }
        public string? YardStableKey { get; set; }
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
            HullStableKey = lifecycle.HullStableKey,
            YardStableKey = lifecycle.YardStableKey,
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
            if (phase != DockingPhase.Undocked
                && (string.IsNullOrWhiteSpace(HullStableKey)
                    || string.IsNullOrWhiteSpace(YardStableKey))) return false;
            if (phase != DockingPhase.Departing && departure != DockingPropulsion.None) return false;
            if (phase == DockingPhase.Departing && departure == DockingPropulsion.None) return false;
            return true;
        }
    }
}
