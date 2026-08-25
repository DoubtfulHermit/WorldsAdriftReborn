using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public readonly record struct StampedCollisionClearance(
        CollisionClearanceRecord Clearance,
        FlightAuthorityStamp Stamp)
    {
        public bool IsValid => Stamp.IsValid && Clearance.IsValid
            && Clearance.FixedStep == Stamp.FixedStep;
    }

    /// <summary>The exact 1114/1205 truth to publish after a committed transition.</summary>
    public readonly record struct DockingComponentProjection(
        long HullEntityId,
        long YardEntityId,
        DockingPose DockLocation,
        bool ApproachingDock,
        bool Docked,
        long YardDockedHullEntityId)
    {
        public static DockingComponentProjection From(AuthenticDockingLifecycle lifecycle)
        {
            bool linked = lifecycle.Phase != DockingPhase.Undocked;
            return new DockingComponentProjection(lifecycle.HullEntityId,
                linked ? lifecycle.YardEntityId : 0,
                lifecycle.TargetPose,
                lifecycle.Phase == DockingPhase.Approaching,
                lifecycle.Phase == DockingPhase.Captured
                    || lifecycle.Phase == DockingPhase.Docked,
                linked ? lifecycle.HullEntityId : 0);
        }
    }

    public readonly record struct DockingRuntimeCommit(
        FlightAuthorityStamp Stamp,
        DockingSnapshotV1 Snapshot,
        DockingComponentProjection Components,
        bool FreezeVelocity,
        bool LinkReleased);

    /// <summary>
    /// The game adapter implements this as one transaction: persist the stable
    /// docking snapshot and authoritative flight pose, then publish 1114 and 1205.
    /// Returning false means nothing became visible or durable.
    /// </summary>
    public interface IDockingRuntimeTransaction
    {
        bool TryCommit(DockingRuntimeCommit commit);
    }

    public sealed class DockingRuntimeOptions
    {
        public bool Enabled { get; init; }
        public static DockingRuntimeOptions Off { get; } = new();
    }

    public enum DockingRuntimeDisposition
    {
        Off,
        Committed,
        RejectedStampMismatch,
        RejectedLifecycle,
        TransactionRolledBack
    }

    public readonly record struct DockingRuntimeResult(
        DockingRuntimeDisposition Disposition,
        DockingRejectReason RejectReason,
        DockingPhase Phase,
        bool FreezeVelocity,
        bool LinkReleased);

    /// <summary>
    /// Default-off transactional adapter for the recovered docking lifecycle.
    /// Every mutation is stamped. On publication/persistence failure the exact old
    /// aggregate and claim are restored before returning.
    /// </summary>
    public sealed class DockingRuntime
    {
        private readonly ShipDockRegistry _claims;
        private readonly IDockingRuntimeTransaction _transaction;
        private readonly DockingRuntimeOptions _options;
        private AuthenticDockingLifecycle _lifecycle;
        private FlightAuthorityStamp? _lastStamp;

        public DockingRuntime(long hullEntityId, ShipDockRegistry claims,
            IDockingRuntimeTransaction transaction,
            DockingRuntimeOptions? options = null, DockingTuning? tuning = null)
        {
            _claims = claims ?? throw new ArgumentNullException(nameof(claims));
            _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
            _options = options ?? DockingRuntimeOptions.Off;
            _lifecycle = new AuthenticDockingLifecycle(hullEntityId, tuning);
        }

        public AuthenticDockingLifecycle Lifecycle => _lifecycle;

        public DockingRuntimeResult TryBeginApproach(DockingApproachRequest request,
            StampedCollisionClearance stamped)
        {
            if (!_options.Enabled) return Off();
            if (!StampAcceptable(stamped)) return StampRejected();
            if (!request.CollisionClearance.Equals(stamped.Clearance)) return StampRejected();

            DockingSnapshotV1 before = _lifecycle.CaptureSnapshot();
            long beforeYard = _lifecycle.YardEntityId;
            if (!_lifecycle.TryBeginApproach(request, _claims, out DockingRejectReason reason))
                return Rejected(reason);
            if (!Commit(stamped.Stamp, freeze: false, released: false))
            {
                Rollback(before, beforeYard);
                return RolledBack();
            }
            _lastStamp = stamped.Stamp;
            return Committed(false, false);
        }

        public DockingRuntimeResult Step(DockingFrame frame,
            StampedCollisionClearance stamped)
        {
            if (!_options.Enabled) return Off();
            if (!StampAcceptable(stamped) || !frame.CollisionClearance.Equals(stamped.Clearance))
                return StampRejected();

            DockingSnapshotV1 before = _lifecycle.CaptureSnapshot();
            long beforeYard = _lifecycle.YardEntityId;
            DockingStepResult step = _lifecycle.Step(frame, _claims);
            if (!Commit(stamped.Stamp, step.FreezeVelocity, step.LinkReleased))
            {
                Rollback(before, beforeYard);
                return RolledBack();
            }
            _lastStamp = stamped.Stamp;
            if (step.Reason != DockingRejectReason.None)
                return Rejected(step.Reason);
            return Committed(step.FreezeVelocity, step.LinkReleased);
        }

        public bool TryRestore(DockingSnapshotV1 snapshot, long restoredYardEntityId,
            FlightAuthorityStamp stamp)
        {
            if (!_options.Enabled || !stamp.IsValid) return false;
            // A legacy boot path may already hold this exact pair (SetDocked on
            // restore). A failed commit must then leave that pre-existing claim
            // alone: this method only releases what its own restore created.
            bool pairPreClaimed = restoredYardEntityId > 0
                && _claims.DockedShipFor(restoredYardEntityId) == _lifecycle.HullEntityId;
            if (!AuthenticDockingLifecycle.TryRestore(snapshot, _lifecycle.HullEntityId,
                    restoredYardEntityId, _claims, out AuthenticDockingLifecycle? restored,
                    out _) || restored == null) return false;
            AuthenticDockingLifecycle before = _lifecycle;
            _lifecycle = restored;
            if (!Commit(stamp,
                    restored.Phase == DockingPhase.Captured
                        || restored.Phase == DockingPhase.Docked, false))
            {
                if (!pairPreClaimed)
                    _claims.Release(restoredYardEntityId, restored.HullEntityId);
                _lifecycle = before;
                return false;
            }
            _lastStamp = stamp;
            return true;
        }

        private bool StampAcceptable(StampedCollisionClearance stamped) => stamped.IsValid
            && (!_lastStamp.HasValue
                || stamped.Stamp.SupersedesWithinGeneration(_lastStamp.Value));

        private bool Commit(FlightAuthorityStamp stamp, bool freeze, bool released) =>
            _transaction.TryCommit(new DockingRuntimeCommit(stamp,
                _lifecycle.CaptureSnapshot(),
                DockingComponentProjection.From(_lifecycle), freeze, released));

        private void Rollback(DockingSnapshotV1 before, long beforeYard)
        {
            long yard = _lifecycle.YardEntityId;
            if (yard > 0) _claims.Release(yard, _lifecycle.HullEntityId);
            if (before.Phase == (int)DockingPhase.Undocked)
            {
                _lifecycle = new AuthenticDockingLifecycle(_lifecycle.HullEntityId);
                return;
            }
            if (!AuthenticDockingLifecycle.TryRestore(before, _lifecycle.HullEntityId,
                    beforeYard, _claims, out AuthenticDockingLifecycle? restored, out _)
                || restored == null)
            {
                // The exact prior claim no longer exists (e.g. a stale-claim reset
                // where another hull now holds the yard). Throwing here would escape
                // into the server tick; the honest fail-closed outcome is Undocked
                // with no claim held and nothing published.
                _lifecycle = new AuthenticDockingLifecycle(_lifecycle.HullEntityId);
                return;
            }
            _lifecycle = restored;
        }

        private DockingRuntimeResult Off() => new(DockingRuntimeDisposition.Off,
            DockingRejectReason.None, _lifecycle.Phase, false, false);
        private DockingRuntimeResult StampRejected() => new(
            DockingRuntimeDisposition.RejectedStampMismatch, DockingRejectReason.StaleClaim,
            _lifecycle.Phase, false, false);
        private DockingRuntimeResult Rejected(DockingRejectReason reason) => new(
            DockingRuntimeDisposition.RejectedLifecycle, reason, _lifecycle.Phase, false, false);
        private DockingRuntimeResult RolledBack() => new(
            DockingRuntimeDisposition.TransactionRolledBack, DockingRejectReason.None,
            _lifecycle.Phase, false, false);
        private DockingRuntimeResult Committed(bool freeze, bool released) => new(
            DockingRuntimeDisposition.Committed, DockingRejectReason.None,
            _lifecycle.Phase, freeze, released);
    }
}
