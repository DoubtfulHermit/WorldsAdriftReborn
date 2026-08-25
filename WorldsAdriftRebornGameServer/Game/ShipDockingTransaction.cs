using System;
using System.Collections.Generic;
using System.Linq;
using Bossa.Travellers.Ship;
using Improbable;
using Improbable.Math;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The one game-side docking transaction (kill-list item 8), DURABLE FIRST:
    /// TryCommit persists the stable docking snapshot + authoritative pose in one
    /// atomic document write, and only after that write succeeded publishes the
    /// 1114 hull and 1205 yard updates. A durable failure returns
    /// <see cref="DockingCommitResult.RolledBack"/> with the persistence layer's
    /// in-memory record restored to its exact prior values, so the runtime's
    /// rollback leaves NOTHING visible or durable. A per-peer publication failure
    /// after the durable write is peer desync, not commit failure: the commit
    /// stands and the committed truth is remembered for
    /// <see cref="RepublishIfNeeded"/> to re-push until every peer converges.
    /// No other code path may write docked state for a runtime-managed hull while
    /// WAREBORN_FLIGHT_DOCKING_TXN=1.
    /// </summary>
    internal sealed class ShipDockingTransaction : IDockingRuntimeTransaction
    {
        /// <summary>
        /// The yard each hull last published a 1205 link for. An unlink commit's
        /// component projection deliberately carries yard 0, so the previous yard
        /// is remembered here to broadcast its cleared DockedShipId.
        /// </summary>
        private readonly Dictionary<long, long> _lastLinkedYardByHull = new Dictionary<long, long>();

        /// <summary>
        /// Durably committed truth whose peer publication was incomplete, per hull.
        /// Merged across commits (a later commit overrides the 1114 and its own
        /// yards but keeps an older pending yard-clear) and re-pushed by
        /// <see cref="RepublishIfNeeded"/> until every peer received it.
        /// </summary>
        private readonly Dictionary<long, PendingPublication> _pendingRepublishByHull =
            new Dictionary<long, PendingPublication>();

        public DockingCommitResult TryCommit(DockingRuntimeCommit commit)
        {
            long hullEntityId = commit.Components.HullEntityId;
            long linkedYard = commit.Components.YardEntityId;
            bool linked = linkedYard > 0;

            // 1) DURABLE first: snapshot (+ legacy dock link while docked, cleared
            //    on release) and the authoritative pose, atomically. Any failure
            //    here rolls back with memory and disk both untouched.
            try
            {
                int? persistentIndex = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
                if (!persistentIndex.HasValue)
                {
                    Console.WriteLine("[warning] docking-txn: hull " + hullEntityId
                        + " has no persistent index; refusing the docking commit.");
                    return DockingCommitResult.RolledBack;
                }

                FixedPointPosition hullPosition = FixedPointPosition.FromMetres(
                    commit.Snapshot.X, commit.Snapshot.Y, commit.Snapshot.Z);
                FixedPointPosition? yardPosition = null;
                if (commit.Components.Docked && linked)
                {
                    yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                        .TransformSeedFor(linkedYard);
                }

                if (!Persistence.WorldStatePersistence.UpdateBuiltShipDockingSnapshot(
                        persistentIndex.Value, hullPosition, commit.Snapshot.YawRadians,
                        linked ? commit.Snapshot : null,
                        yardPosition, clearDockLink: commit.LinkReleased))
                {
                    Console.WriteLine("[warning] docking-txn: durable write failed for hull "
                        + hullEntityId + "; commit rolled back, nothing became visible.");
                    return DockingCommitResult.RolledBack;
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("[warning] docking-txn: durable phase failed for hull "
                    + hullEntityId + " and the commit is rolled back: " + exception.Message);
                return DockingCommitResult.RolledBack;
            }

            // 2) VISIBLE second, only after the durable write: 1114 on the hull,
            //    1205 on the linked yard, and a cleared 1205 on a previously linked
            //    yard. A per-peer failure no longer fails the commit - the durable
            //    truth already stands - it becomes republish debt instead.
            _lastLinkedYardByHull.TryGetValue(hullEntityId, out long previousYard);
            DockableState.Update hullUpdate = new DockableState.Update()
                .SetDockEntityId(new EntityId(linked ? linkedYard : 0))
                .SetDockLocation(new Coordinates(commit.Components.DockLocation.X,
                    commit.Components.DockLocation.Y, commit.Components.DockLocation.Z))
                .SetDocked(commit.Components.Docked)
                .SetApproachingDock(commit.Components.ApproachingDock);
            PendingPublication publication = _pendingRepublishByHull.TryGetValue(
                hullEntityId, out PendingPublication? pending)
                ? pending : new PendingPublication();
            publication.HullUpdate = hullUpdate;
            if (linked)
            {
                publication.DockedShipIdByYard[linkedYard] =
                    commit.Components.YardDockedHullEntityId;
            }
            // Only a commit that RELEASED our own link may clear the old yard's
            // 1205: a stale-claim reset means another hull now legitimately owns
            // that yard's DockedShipId.
            if (commit.LinkReleased && previousYard > 0 && previousYard != linkedYard)
            {
                publication.DockedShipIdByYard[previousYard] = 0;
            }

            if (linked) _lastLinkedYardByHull[hullEntityId] = linkedYard;
            else _lastLinkedYardByHull.Remove(hullEntityId);

            if (TryPublish(hullEntityId, publication))
            {
                _pendingRepublishByHull.Remove(hullEntityId);
                return DockingCommitResult.Committed;
            }
            _pendingRepublishByHull[hullEntityId] = publication;
            return DockingCommitResult.CommittedRepublishNeeded;
        }

        /// <summary>
        /// Re-pushes a durably committed publication that some peer missed, until
        /// every peer received it. Called once per docking scan so a partially
        /// published hull converges instead of fossilizing under the steady-docked
        /// suppression.
        /// </summary>
        internal void RepublishIfNeeded(long hullEntityId)
        {
            if (!_pendingRepublishByHull.TryGetValue(hullEntityId,
                    out PendingPublication? pendingPublication)) return;
            if (TryPublish(hullEntityId, pendingPublication))
            {
                _pendingRepublishByHull.Remove(hullEntityId);
                Console.WriteLine("[info] docking-txn: republished committed docking state"
                    + " for hull " + hullEntityId + "; peers converged.");
            }
        }

        internal void Retire(long hullEntityId)
        {
            _lastLinkedYardByHull.Remove(hullEntityId);
            _pendingRepublishByHull.Remove(hullEntityId);
        }

        /// <summary>
        /// Sends one committed publication to every connected peer. Each peer is
        /// attempted independently; a failure is logged as desync and reported via
        /// the return value, never thrown.
        /// </summary>
        private static bool TryPublish(long hullEntityId, PendingPublication publication)
        {
            List<ENetPeerHandle> peers;
            try
            {
                peers = PeerManager.Instance.playerState.Keys.ToList();
            }
            catch (Exception exception)
            {
                Console.WriteLine("[warning] docking-txn: could not enumerate peers for hull "
                    + hullEntityId + " (commit stands, will republish): " + exception.Message);
                return false;
            }

            bool complete = true;
            foreach (ENetPeerHandle peer in peers)
            {
                try
                {
                    SendOPHelper.SendComponentUpdateOp(peer, hullEntityId,
                        new List<uint> { 1114 }, new List<object> { publication.HullUpdate });
                    foreach (KeyValuePair<long, long> push in publication.DockedShipIdByYard)
                    {
                        Crafting.BuiltShipSpawner.PushDockedShipId(peer, push.Key, push.Value);
                    }
                }
                catch (Exception exception)
                {
                    complete = false;
                    Console.WriteLine("[warning] docking-txn: peer publication failed for hull "
                        + hullEntityId + " (peer desync, commit stands, will republish): "
                        + exception.Message);
                }
            }
            return complete;
        }

        /// <summary>
        /// One hull's committed 1114 plus the absolute 1205 DockedShipId per touched
        /// yard. Absolute values make a resend idempotent, and keying pushes by yard
        /// lets a newer commit override its own yards while an older pending
        /// yard-clear survives the merge.
        /// </summary>
        private sealed class PendingPublication
        {
            public DockableState.Update HullUpdate = new DockableState.Update();
            public readonly Dictionary<long, long> DockedShipIdByYard = new Dictionary<long, long>();
        }
    }
}
