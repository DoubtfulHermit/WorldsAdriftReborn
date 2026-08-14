using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>Authoritative docked-frame salvage transaction behind the retail UI verb.</summary>
    internal static class ShipSalvageService
    {
        /// <summary>
        /// Permanently deletes an exact uncrewed built ship for the authenticated
        /// operator console. Unlike player salvage, mounted parts are deleted with
        /// the hull instead of becoming loose drops.
        /// </summary>
        internal static bool AdminDelete(long hullId, out string error)
        {
            error = string.Empty;
            if (!BuiltShips.IsBuiltHull(hullId))
            {
                error = "Hull " + hullId + " is not a live built ship.";
                return false;
            }
            if (WorldsAdriftRebornGameServer.Flight.IsPiloted(hullId)
                || WorldsAdriftRebornGameServer.Aboard.AnyoneAboard(hullId))
            {
                error = "Hull " + hullId + " is piloted or occupied; delete refused.";
                return false;
            }
            int? persistentIndex = BuiltShips.PersistentIndexFor(hullId);
            if (!persistentIndex.HasValue
                || !WorldStatePersistence.SalvageBuiltShip(
                    persistentIndex.Value, Array.Empty<LoosePartRecord>()))
            {
                error = "Could not durably tombstone hull " + hullId + ".";
                return false;
            }

            List<long> mountedIds = MountedParts.OnHull(hullId).Select(x => x.Key).ToList();
            foreach (long partId in mountedIds)
            {
                MountedParts.Unmount(partId);
                WorldsAdriftRebornGameServer.Sails.Unregister(partId);
                WorldsAdriftRebornGameServer.Lamps.Unregister(partId);
                WorldsAdriftRebornGameServer.Horns.Unregister(partId);
                WorldsAdriftRebornGameServer.ShipMembership.Unregister(partId, hullId);
                WorldsAdriftRebornGameServer.WorldEntities.Unregister(partId);
            }

            long yardId = BuiltShips.ShipyardForHull(hullId);
            if (yardId != 0)
            {
                BuiltShips.ClearDocked(yardId);
                BuiltShipSpawner.PushUndocked(yardId);
            }
            WorldsAdriftRebornGameServer.Flight.RetireHull(hullId);
            IReadOnlyList<long> deckIds = BuiltShips.UnregisterShip(hullId);
            foreach (long deckId in deckIds)
            {
                WorldsAdriftRebornGameServer.ShipMembership.Unregister(deckId, hullId);
                WorldsAdriftRebornGameServer.WorldEntities.Unregister(deckId);
            }
            WorldsAdriftRebornGameServer.ShipMembership.Unregister(hullId, hullId);
            WorldsAdriftRebornGameServer.WorldEntities.Unregister(hullId);

            List<long> removed = mountedIds.Concat(deckIds).Concat(new[] { hullId }).ToList();
            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                foreach (long entityId in removed)
                {
                    if (SendOPHelper.SendRemoveEntityOP(peer, entityId))
                        PeerCheckoutCleanup.RemoveEntity(peer, entityId);
                }
            }
            error = "Permanently deleted hull " + hullId + " with " + deckIds.Count
                + " deck(s) and " + mountedIds.Count + " mounted part(s).";
            return true;
        }

        internal static ShipSalvageReject Reclaim(long playerEntityId, long shipyardEntityId,
            bool ownsPlayerEntity)
        {
            long hullId = BuiltShips.DockedShipFor(shipyardEntityId);
            string requester = CharacterOwnership.UidForEntity(playerEntityId);
            string yardOwner = Placement.PlacedShipyards.IsPlacedShipyard(shipyardEntityId)
                ? Placement.PlacedShipyards.SeedFor(shipyardEntityId).OwnerCharacterUid
                : "";
            ShipSalvageReject verdict = ShipSalvagePolicy.Evaluate(
                ownsPlayerEntity, requester, yardOwner, hullId, BuiltShips.IsBuiltHull(hullId),
                BuiltShips.ShipyardForHull(hullId), shipyardEntityId);
            if (verdict != ShipSalvageReject.Accept) return verdict;
            if (WorldsAdriftRebornGameServer.Flight.IsPiloted(hullId))
            {
                Console.WriteLine("[salvage] rejected hull " + hullId + ": it is currently piloted.");
                return ShipSalvageReject.HullPiloted;
            }

            int? persistentIndex = BuiltShips.PersistentIndexFor(hullId);
            if (!persistentIndex.HasValue) return ShipSalvageReject.HullNotBuilt;

            // Snapshot before mutation: OnHull lazily enumerates the ledger.
            List<KeyValuePair<long, MountedParts.Mount>> mounts = MountedParts.OnHull(hullId).ToList();
            var looseRecords = new List<LoosePartRecord>(mounts.Count);
            FixedPointPosition hullPosition = WorldsAdriftRebornGameServer.WorldEntities
                .ByEntityId(hullId)!.Position;
            uint hullRotation = WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullId);
            if (WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(
                    hullId, out FixedPointPosition flownPosition, out uint flownRotation))
            {
                hullPosition = flownPosition;
                hullRotation = flownRotation;
            }
            foreach (KeyValuePair<long, MountedParts.Mount> entry in mounts)
            {
                (FixedPointPosition dropPosition, uint dropRotation) = ShipSalvagePolicy.DropPose(
                    hullPosition, hullRotation, entry.Value.LocalOffset, entry.Value.PackedRotation);
                LoosePartRecord? loose = LoosePartSpawner.LooseRecord(
                    entry.Key, entry.Value.OwnerCharacterUid, dropPosition, dropRotation);
                if (loose != null) looseRecords.Add(loose);
            }

            if (looseRecords.Count != mounts.Count)
            {
                Console.WriteLine("[error] salvage: could not construct durable loose records for every"
                    + " mounted part on hull " + hullId + "; refusing to delete anything.");
                return ShipSalvageReject.HullNotBuilt;
            }

            // Durable truth first and in ONE atomic write. No runtime/client mutation has
            // happened yet, so a refused/failed save leaves the intact ship untouched.
            if (!WorldStatePersistence.SalvageBuiltShip(persistentIndex.Value, looseRecords))
            {
                Console.WriteLine("[error] salvage: persistence refused built-ship index "
                    + persistentIndex.Value + "; hull " + hullId + " remains registered.");
                return ShipSalvageReject.HullNotBuilt;
            }

            foreach (KeyValuePair<long, MountedParts.Mount> entry in mounts)
            {
                long partId = entry.Key;
                MountedParts.Unmount(partId);
                WorldsAdriftRebornGameServer.Sails.Unregister(partId);
                WorldsAdriftRebornGameServer.Lamps.Unregister(partId);
                WorldsAdriftRebornGameServer.Horns.Unregister(partId);
                PartMountService.BroadcastDetach(partId, entry.Value);
            }

            BuiltShips.ClearDocked(shipyardEntityId);
            BuiltShipSpawner.PushUndocked(shipyardEntityId);
            WorldsAdriftRebornGameServer.Flight.RetireHull(hullId);
            IReadOnlyList<long> deckIds = BuiltShips.UnregisterShip(hullId);
            foreach (long deckId in deckIds)
            {
                WorldsAdriftRebornGameServer.ShipMembership.Unregister(deckId, hullId);
                WorldsAdriftRebornGameServer.WorldEntities.Unregister(deckId);
            }
            WorldsAdriftRebornGameServer.ShipMembership.Unregister(hullId, hullId);
            WorldsAdriftRebornGameServer.WorldEntities.Unregister(hullId);

            // Parts remain as loose entities. Retire only deck panels and hull from every
            // checked-out peer and clear component references so no later broadcast can
            // target a dead client-side component.
            List<long> removed = deckIds.Concat(new[] { hullId }).ToList();
            foreach (ENetPeerHandle peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                foreach (long entityId in removed)
                {
                    if (SendOPHelper.SendRemoveEntityOP(peer, entityId))
                    {
                        PeerCheckoutCleanup.RemoveEntity(peer, entityId);
                    }
                }
            }

            Console.WriteLine("[salvage] player " + playerEntityId + " reclaimed docked hull "
                + hullId + " at shipyard " + shipyardEntityId + ": removed " + deckIds.Count
                + " deck panel(s), dropped " + mounts.Count + " mounted part(s), and freed the yard.");
            return ShipSalvageReject.Accept;
        }
    }
}
