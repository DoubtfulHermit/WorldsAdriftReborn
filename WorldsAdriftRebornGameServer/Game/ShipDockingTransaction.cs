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
    /// The one game-side docking transaction (kill-list item 8): persistence of the
    /// stable docking snapshot + authoritative pose, then the 1114 hull and 1205
    /// yard publications, all inside TryCommit. DockingRuntime calls this for every
    /// stamped mutation; returning false means the runtime rolls the lifecycle and
    /// claim back and nothing became visible or durable. No other code path may
    /// write docked state for a runtime-managed hull while WAREBORN_FLIGHT_DOCKING_TXN=1.
    /// </summary>
    internal sealed class ShipDockingTransaction : IDockingRuntimeTransaction
    {
        /// <summary>
        /// The yard each hull last published a 1205 link for. An unlink commit's
        /// component projection deliberately carries yard 0, so the previous yard
        /// is remembered here to broadcast its cleared DockedShipId.
        /// </summary>
        private readonly Dictionary<long, long> _lastLinkedYardByHull = new Dictionary<long, long>();

        public bool TryCommit(DockingRuntimeCommit commit)
        {
            try
            {
                long hullEntityId = commit.Components.HullEntityId;
                int? persistentIndex = Crafting.BuiltShips.PersistentIndexFor(hullEntityId);
                if (!persistentIndex.HasValue)
                {
                    Console.WriteLine("[warning] docking-txn: hull " + hullEntityId
                        + " has no persistent index; refusing the docking commit.");
                    return false;
                }

                long linkedYard = commit.Components.YardEntityId;
                bool linked = linkedYard > 0;
                FixedPointPosition hullPosition = FixedPointPosition.FromMetres(
                    commit.Snapshot.X, commit.Snapshot.Y, commit.Snapshot.Z);
                FixedPointPosition? yardPosition = null;
                if (commit.Components.Docked && linked)
                {
                    yardPosition = WorldsAdriftRebornGameServer.WorldEntities
                        .TransformSeedFor(linkedYard);
                }

                // 1) DURABLE first: snapshot (+ legacy dock link while docked,
                //    cleared on release) and the authoritative pose, atomically.
                Persistence.WorldStatePersistence.UpdateBuiltShipDockingSnapshot(
                    persistentIndex.Value, hullPosition, commit.Snapshot.YawRadians,
                    linked ? commit.Snapshot : null,
                    yardPosition, clearDockLink: commit.LinkReleased);

                // 2) VISIBLE second: 1114 on the hull, 1205 on the linked yard,
                //    and a cleared 1205 on a previously linked yard.
                _lastLinkedYardByHull.TryGetValue(hullEntityId, out long previousYard);
                DockableState.Update hullUpdate = new DockableState.Update()
                    .SetDockEntityId(new EntityId(linked ? linkedYard : 0))
                    .SetDockLocation(new Coordinates(commit.Components.DockLocation.X,
                        commit.Components.DockLocation.Y, commit.Components.DockLocation.Z))
                    .SetDocked(commit.Components.Docked)
                    .SetApproachingDock(commit.Components.ApproachingDock);
                foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
                {
                    SendOPHelper.SendComponentUpdateOp(peer, hullEntityId,
                        new List<uint> { 1114 }, new List<object> { hullUpdate });
                    if (linked)
                    {
                        Crafting.BuiltShipSpawner.PushDockedShipId(peer, linkedYard,
                            commit.Components.YardDockedHullEntityId);
                    }
                    // Only a commit that RELEASED our own link may clear the old
                    // yard's 1205: a stale-claim reset means another hull now
                    // legitimately owns that yard's DockedShipId.
                    if (commit.LinkReleased && previousYard > 0 && previousYard != linkedYard)
                    {
                        Crafting.BuiltShipSpawner.PushDockedShipId(peer, previousYard, 0);
                    }
                }

                if (linked) _lastLinkedYardByHull[hullEntityId] = linkedYard;
                else _lastLinkedYardByHull.Remove(hullEntityId);
                return true;
            }
            catch (Exception exception)
            {
                Console.WriteLine("[warning] docking-txn: commit failed and will be"
                    + " rolled back: " + exception.Message);
                return false;
            }
        }

        internal void Retire(long hullEntityId) => _lastLinkedYardByHull.Remove(hullEntityId);
    }
}
