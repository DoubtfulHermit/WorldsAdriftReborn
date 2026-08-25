using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Per-hull driver for the Steps 4-5 runtime: holds each hull's in-tick
    /// collision observation and its transactional DockingRuntime. All decidable
    /// logic lives in the Multiplayer assembly (HullCollisionObserver,
    /// CollisionRuntime, DockingRuntime, AuthenticDockingLifecycle); this class
    /// only gathers runtime inputs from the ledgers and forwards them.
    ///
    /// Stable persisted keys: hulls key by persistent index ("ship:&lt;index&gt;"),
    /// yards by their exact fixed-point position ("yard:x:y:z") - never runtime
    /// entity ids (kill-list item 10).
    /// </summary>
    internal sealed class ShipDockingRuntimeDriver
    {
        private static readonly DockingTuning Tuning = new DockingTuning();

        private readonly ShipDockRegistry _claims;
        private readonly ShipDockingTransaction _transaction = new ShipDockingTransaction();
        private readonly Dictionary<long, DockingRuntime> _runtimes = new Dictionary<long, DockingRuntime>();
        private readonly Dictionary<long, HullCollisionObservation> _lastObservation =
            new Dictionary<long, HullCollisionObservation>();
        private readonly Dictionary<long, ShadowVector3> _halfExtentsByHull =
            new Dictionary<long, ShadowVector3>();
        private readonly HashSet<long> _geometryWarned = new HashSet<long>();

        internal ShipDockingRuntimeDriver(ShipDockRegistry? claims = null)
        {
            _claims = claims ?? ShipDockRegistry.Shared;
        }

        internal HullCollisionObservation? ObservationFor(long hullEntityId) =>
            _lastObservation.TryGetValue(hullEntityId, out HullCollisionObservation observation)
                ? observation : (HullCollisionObservation?)null;

        internal DockingPhase? PhaseFor(long hullEntityId) =>
            _runtimes.TryGetValue(hullEntityId, out DockingRuntime? runtime)
                ? runtime.Lifecycle.Phase : (DockingPhase?)null;

        /// <summary>Whether this hull's dock lifecycle is under the transactional runtime.</summary>
        internal bool Manages(long hullEntityId) =>
            _runtimes.TryGetValue(hullEntityId, out DockingRuntime? runtime)
            && runtime.Lifecycle.Phase != DockingPhase.Undocked;

        /// <summary>
        /// The committed 1114/1205 truth for a runtime-managed hull, or null when
        /// the hull is not under the transactional runtime. This is what the
        /// read-only checkout serve path answers with, so a peer that missed a
        /// live push converges on its next component checkout.
        /// </summary>
        internal DockingComponentProjection? ProjectionFor(long hullEntityId) =>
            _runtimes.TryGetValue(hullEntityId, out DockingRuntime? runtime)
            && runtime.Lifecycle.Phase != DockingPhase.Undocked
                ? DockingComponentProjection.From(runtime.Lifecycle)
                : (DockingComponentProjection?)null;

        /// <summary>
        /// In-tick collision observation for one committed fixed-step slice. The
        /// stamp and pose arrive together from the hull's ONE authority adapter
        /// (<see cref="FlightAuthorityAdapter.LastStamp"/> /
        /// <see cref="FlightAuthorityAdapter.CurrentPose"/>); this driver never
        /// mints a stamp or integrates a pose of its own.
        /// </summary>
        internal void ObserveAfterSlice(long hullEntityId, FlightAuthorityStamp stamp,
            AuthoritativeFlightPose pose, double massKg)
        {
            if (!stamp.IsValid || !pose.IsValid) return; // no honest frame -> no observation
            ShadowVector3? half = HalfExtentsFor(hullEntityId);
            if (!half.HasValue) return; // no honest geometry -> no observation, never a clearance

            ShadowVector3 position = new ShadowVector3(pose.X, pose.Y, pose.Z);
            IslandCollisionProxyBatch terrain = IslandCollisionProxyAdapter.Nearby(
                position, stamp.FixedStep, stamp.AuthorityGeneration);
            HullCollisionObservation observation = HullCollisionObserver.Observe(stamp,
                HullKey(hullEntityId), position,
                new ShadowVector3(pose.VxMps, pose.VyMps, pose.VzMps),
                half.Value, Math.Max(1.0, massKg), FixedFlightClock.StepSeconds, terrain,
                new CollisionRuntimeOptions
                {
                    ObserveEnabled = true,
                    ResponseEnabled = ShipFlightService.RuntimeFlags.CollisionResponseEnabled
                });
            _lastObservation[hullEntityId] = observation;

            if (observation.Result.MutatesAuthoritativeVelocity)
            {
                // Unreachable today by construction: the hull subject is always
                // ConservativeEnvelope, so the response gate rejects as ambiguous
                // geometry. This tripwire fires if a reviewed-convex source ever
                // appears before the vector authority adapter (the only sanctioned
                // velocity write path) is integrated in Step 6.
                Console.WriteLine("[warning] flight collision: hull " + hullEntityId
                    + " produced an Applied response but no authority adapter is"
                    + " integrated to apply it; the correction was NOT applied.");
            }
        }

        /// <summary>
        /// One publication-paced docking decision for one hull. Returns the runtime
        /// result when a lifecycle decision was made this scan, else null.
        /// </summary>
        internal DockingRuntimeResult? Scan(long hullEntityId, ShipDomain domain,
            FlightSession session)
        {
            // A durably committed publication some peer missed is re-pushed every
            // scan until it lands, regardless of what (if anything) is decided
            // below - so the steady-docked suppression can never fossilize a
            // diverged peer, and an unlink whose broadcast was cut short still
            // converges even though an undocked hull decides nothing.
            _transaction.RepublishIfNeeded(hullEntityId);

            if (!_lastObservation.TryGetValue(hullEntityId,
                    out HullCollisionObservation observation)) return null;
            // Old-generation evidence is dead; wait for a fresh observation.
            if (observation.Stamp.AuthorityGeneration != domain.Generation.Value) return null;

            DockingRuntime runtime = RuntimeFor(hullEntityId);
            runtime.RebaseGeneration(domain.Generation.Value);

            FlightState state = session.State;
            var observedPose = new DockingPose(state.X, state.Y, state.Z, state.YawRadians);
            var observedMotion = new DockingMotion(state.VxMps, state.VyMps, state.VzMps,
                state.YawRateRadPerSec);
            DockingPropulsion propulsion = PropulsionOf(hullEntityId, session);

            if (runtime.Lifecycle.Phase == DockingPhase.Undocked)
            {
                return TryBeginApproach(hullEntityId, runtime, observation,
                    observedPose, observedMotion, propulsion, session);
            }

            long yardEntityId = runtime.Lifecycle.YardEntityId;
            bool yardExists = Placement.PlacedShipyards.EntityIds.Contains(yardEntityId);
            string hullOwner = Crafting.BuiltShips.OwnerFor(hullEntityId);
            string? yardOwner = yardExists
                ? Placement.PlacedShipyards.SeedFor(yardEntityId).OwnerCharacterUid : null;
            bool permissionValid = yardExists && DockingPermissionPolicy.MayApproach(
                hullOwner, yardOwner, crewAuthorized: false,
                yardAbandoned: string.IsNullOrEmpty(yardOwner));
            ShipyardBubble bubble = BubbleFor(yardEntityId);

            // Steady docked state: nothing to decide, so nothing NEW is committed
            // or persisted (event-on-change, like the rest of the publisher);
            // outstanding republish debt was already flushed above. Any
            // propulsion, permission, yard or claim change falls through to a
            // real stamped lifecycle step.
            if (runtime.Lifecycle.Phase == DockingPhase.Docked
                && propulsion == DockingPropulsion.None
                && yardExists && permissionValid
                && _claims.DockedShipFor(yardEntityId) == hullEntityId
                && _claims.ShipyardForHull(hullEntityId) == yardEntityId)
            {
                return null;
            }

            var frame = new DockingFrame(ShipMotionPolicy.SendIntervalSeconds, yardExists,
                permissionValid, propulsion,
                observation.ClearanceFor(runtime.Lifecycle.YardStableKey),
                bubble, observedPose, observedMotion,
                helmManned: session.IsManned,
                hullClearanceRadiusMetres: HullClearanceRadiusFor(hullEntityId));
            DockingRuntimeResult result = runtime.Step(frame,
                new StampedCollisionClearance(frame.CollisionClearance, observation.Stamp));
            if (result.FreezeVelocity)
            {
                DockingPose pose = runtime.Lifecycle.Pose;
                session.DockAt(pose.X, pose.Y, pose.Z, pose.YawRadians);
            }
            return result;
        }

        /// <summary>Restores a persisted docking lifecycle onto fresh runtime ids at boot.</summary>
        internal bool Restore(long hullEntityId, DockingSnapshotV1 snapshot,
            long resolvedYardEntityId, long authorityGeneration)
        {
            DockingRuntime runtime = RuntimeFor(hullEntityId);
            runtime.RebaseGeneration(authorityGeneration);
            bool restored = runtime.TryRestore(snapshot, resolvedYardEntityId,
                new FlightAuthorityStamp(0, authorityGeneration));
            Console.WriteLine(restored
                ? "[info] docking-txn: restored " + ((DockingPhase)snapshot.Phase)
                    + " lifecycle for hull " + hullEntityId + " at yard " + resolvedYardEntityId + "."
                : "[warning] docking-txn: could not restore docking snapshot for hull "
                    + hullEntityId + "; it boots undocked.");
            return restored;
        }

        /// <summary>
        /// Transactionally unlinks and forgets a retired/salvaged hull. The caller
        /// passes the hull's real domain generation; with no observation held the
        /// deletion is stamped with an explicitly INVALID stamp (step -1) carrying
        /// that generation - a stamp is never invented here, and Delete already
        /// treats an invalid stamp as unlink-and-publish without recording it.
        /// </summary>
        internal void Retire(long hullEntityId, long authorityGeneration)
        {
            if (_runtimes.TryGetValue(hullEntityId, out DockingRuntime? runtime)
                && runtime.Lifecycle.Phase != DockingPhase.Undocked)
            {
                FlightAuthorityStamp stamp = _lastObservation.TryGetValue(hullEntityId,
                        out HullCollisionObservation observation)
                    ? observation.Stamp
                    : new FlightAuthorityStamp(-1, authorityGeneration);
                runtime.Delete(stamp);
            }
            _runtimes.Remove(hullEntityId);
            _lastObservation.Remove(hullEntityId);
            _halfExtentsByHull.Remove(hullEntityId);
            _geometryWarned.Remove(hullEntityId);
            _transaction.Retire(hullEntityId);
        }

        private DockingRuntimeResult? TryBeginApproach(long hullEntityId,
            DockingRuntime runtime, HullCollisionObservation observation,
            DockingPose observedPose, DockingMotion observedMotion,
            DockingPropulsion propulsion, FlightSession session)
        {
            string hullOwner = Crafting.BuiltShips.OwnerFor(hullEntityId);
            // NEAREST yard first (id breaks ties, so the choice stays deterministic):
            // domes can overlap, and the yard whose bubble the hull is deepest inside
            // is the one the player means.
            foreach (long yardEntityId in Placement.PlacedShipyards.EntityIds
                         .OrderBy(id => BubbleFor(id).DistanceFromYard(observedPose.Position))
                         .ThenBy(id => id))
            {
                FixedPointPosition yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                    .TransformSeedFor(yardEntityId);
                ShipyardBubble bubble = BubbleFor(yardEntityId);
                // Inside the bubble AND above the yard: the dome, not a sphere that
                // also reaches under an island-mounted shipyard.
                if (!bubble.ContainsDock(observedPose.Position)) continue;

                FixedPointPosition target = Multiplayer.Ship.ShipyardDockingPolicy
                    .DockPose(yardPosition);
                double targetYaw = Multiplayer.Ship.ShipyardDockingPolicy.YawFromPacked(
                    WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(yardEntityId));
                var targetPose = new DockingPose(target.MetresX, target.MetresY,
                    target.MetresZ, targetYaw);

                string? yardOwner = Placement.PlacedShipyards
                    .SeedFor(yardEntityId).OwnerCharacterUid;
                var request = new DockingApproachRequest(hullEntityId, yardEntityId,
                    HullKey(hullEntityId), YardKey(yardPosition), hullOwner, yardOwner,
                    crewAuthorized: false,
                    yardAbandoned: string.IsNullOrEmpty(yardOwner),
                    yardExists: true,
                    propulsionNeutral: propulsion == DockingPropulsion.None,
                    observation.ClearanceFor(YardKey(yardPosition)),
                    observedPose, targetPose, observedMotion, bubble,
                    helmManned: session.IsManned);
                DockingRuntimeResult result = runtime.TryBeginApproach(request,
                    new StampedCollisionClearance(request.CollisionClearance,
                        observation.Stamp));
                if (result.Disposition == DockingRuntimeDisposition.Committed)
                {
                    Console.WriteLine("[flight] docking-txn: hull " + hullEntityId
                        + " began a stamped approach to shipyard " + yardEntityId + ".");
                    return result;
                }
                // One decision per scan: the next stamped observation retries.
                return result;
            }
            return null;
        }

        /// <summary>
        /// One shipyard's influence dome, from the yard's own registered transform.
        /// Every radius/floor/margin value comes from the shared
        /// <see cref="DockingTuning"/>, so the approach gate, the capture volume, the
        /// departure boundary and the reviewed dock volume are one geometry.
        /// </summary>
        private static ShipyardBubble BubbleFor(long yardEntityId)
        {
            FixedPointPosition yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                .TransformSeedFor(yardEntityId);
            return Tuning.BubbleAt(new ShadowVector3(yardPosition.MetresX,
                yardPosition.MetresY, yardPosition.MetresZ));
        }

        /// <summary>
        /// The hull's yaw-invariant bounding radius, so "fully outside the bubble"
        /// is measured from the hull's near edge. Zero when the geometry is unknown -
        /// which is also a hull that never gets an observation, so it never reaches
        /// a docking decision anyway.
        /// </summary>
        private double HullClearanceRadiusFor(long hullEntityId)
        {
            ShadowVector3? half = HalfExtentsFor(hullEntityId);
            return half.HasValue
                ? HullCollisionObserver.RotationExpandedHalfExtents(half.Value).Magnitude
                : 0.0;
        }

        /// <summary>
        /// The yard-side 1205 truth for a shipyard the transactional runtime manages:
        /// the hull whose bubble is currently raised there, or 0 when a managed yard
        /// has no docked ship. Null when NO runtime manages this yard, which is
        /// always the case with the docking gate off - the 1205 checkout serve then
        /// falls back to the legacy ledger byte-identically.
        /// </summary>
        internal long? RuntimeDockedShipFor(long yardEntityId)
        {
            if (yardEntityId <= 0) return null;
            foreach (DockingRuntime runtime in _runtimes.Values)
            {
                AuthenticDockingLifecycle lifecycle = runtime.Lifecycle;
                if (lifecycle.Phase == DockingPhase.Undocked
                    || lifecycle.YardEntityId != yardEntityId) continue;
                return DockingComponentProjection.From(lifecycle).YardDockedHullEntityId;
            }
            return null;
        }

        private DockingRuntime RuntimeFor(long hullEntityId)
        {
            if (_runtimes.TryGetValue(hullEntityId, out DockingRuntime? existing))
                return existing;
            var runtime = new DockingRuntime(hullEntityId, _claims, _transaction,
                new DockingRuntimeOptions { Enabled = ShipFlightService.RuntimeFlags.DockingTxnEnabled },
                Tuning);
            _runtimes[hullEntityId] = runtime;
            return runtime;
        }

        private DockingPropulsion PropulsionOf(long hullEntityId, FlightSession session)
        {
            bool sails = WorldsAdriftRebornGameServer.Sails.UnfurledCountFor(hullEntityId) > 0;
            bool engine = !session.Input.IsNeutral;
            if (sails && engine) return DockingPropulsion.SailAndEngine;
            if (sails) return DockingPropulsion.Sail;
            if (engine) return DockingPropulsion.Engine;
            return DockingPropulsion.None;
        }

        private ShadowVector3? HalfExtentsFor(long hullEntityId)
        {
            if (_halfExtentsByHull.TryGetValue(hullEntityId, out ShadowVector3 cached))
                return cached;
            byte[]? hullBytes = Crafting.BuiltShips.HullBytesFor(hullEntityId);
            if (hullBytes == null
                || !Multiplayer.Ship.ShipPlanModel.TryDecode(hullBytes,
                    out Multiplayer.Ship.ShipPlanModel? plan, out _)
                || plan == null)
            {
                if (_geometryWarned.Add(hullEntityId))
                {
                    Console.WriteLine("[warning] flight collision: hull " + hullEntityId
                        + " has no decodable geometry; it gets no observation and can"
                        + " never produce a clearance.");
                }
                return null;
            }
            Multiplayer.Ship.ShipHullMetrics metrics =
                Multiplayer.Ship.ShipHullMetrics.Measure(plan);
            ShadowVector3 half = new ShadowVector3(
                Math.Max(0.25, metrics.BeamMetres * 0.5),
                Math.Max(0.25, metrics.DeckPlaneMetres * 0.5),
                Math.Max(0.25, metrics.KeelMetres * 0.5));
            _halfExtentsByHull[hullEntityId] = half;
            return half;
        }

        private static string HullKey(long hullEntityId)
        {
            int? persistentIndex = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
            return persistentIndex.HasValue
                ? "ship:" + persistentIndex.Value.ToString(CultureInfo.InvariantCulture)
                : "ship:runtime:" + hullEntityId.ToString(CultureInfo.InvariantCulture);
        }

        private static string YardKey(FixedPointPosition position) =>
            "yard:" + position.X.ToString(CultureInfo.InvariantCulture)
            + ":" + position.Y.ToString(CultureInfo.InvariantCulture)
            + ":" + position.Z.ToString(CultureInfo.InvariantCulture);
    }
}
