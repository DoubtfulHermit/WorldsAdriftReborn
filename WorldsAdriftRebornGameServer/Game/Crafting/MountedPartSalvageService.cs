using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>Authoritative salvage-beam dismantle transaction for mounted ship parts.</summary>
    internal static class MountedPartSalvageService
    {
        /// <summary>Returns false only when the target is not a mounted part.</summary>
        internal static bool HandleShot(long playerEntityId, long partEntityId)
        {
            MountedParts.Mount? found = MountedParts.MountFor(partEntityId);
            if (!found.HasValue) return false;
            MountedParts.Mount mount = found.Value;
            long yardId = BuiltShips.ShipyardForHull(mount.HullEntityId);
            string requester = CharacterOwnership.UidForEntity(playerEntityId);
            string yardOwner = yardId > 0 && Placement.PlacedShipyards.IsPlacedShipyard(yardId)
                ? Placement.PlacedShipyards.SeedFor(yardId).OwnerCharacterUid
                : "";
            SchematicRecord? recipe = SchematicHelper.Get(
                LooseParts.DefFor(partEntityId)?.SchematicId ?? mount.ItemType);
            IReadOnlyList<ShipPartSalvageRefund> refunds = recipe == null
                ? Array.Empty<ShipPartSalvageRefund>()
                : ShipPartSalvagePolicy.Refunds(recipe);
            ShipPartSalvageReject verdict = ShipPartSalvagePolicy.Evaluate(
                true, mount.HullEntityId, yardId,
                yardId > 0 ? BuiltShips.DockedShipFor(yardId) : 0,
                !string.IsNullOrEmpty(requester) && requester == yardOwner,
                recipe != null && refunds.Count > 0);
            if (verdict != ShipPartSalvageReject.Accept)
            {
                Console.WriteLine("[part-salvage] ignored shot on part " + partEntityId
                    + " by player " + playerEntityId + ": " + verdict + ".");
                return true;
            }

            if (!CanFitRefund(playerEntityId, refunds))
            {
                Console.WriteLine("[part-salvage] refused part " + partEntityId
                    + ": player inventory cannot hold the complete refund.");
                return true;
            }

            string? uid = LooseParts.PartUidFor(partEntityId);
            if (!WorldStatePersistence.RemoveMountedPart(uid ?? ""))
            {
                Console.WriteLine("[part-salvage] persistence failed for part " + partEntityId
                    + "; leaving it intact.");
                return true;
            }

            MountedParts.Unmount(partEntityId);
            WorldsAdriftRebornGameServer.Sails.Unregister(partEntityId);
            WorldsAdriftRebornGameServer.Lamps.Unregister(partEntityId);
            WorldsAdriftRebornGameServer.Horns.Unregister(partEntityId);
            LooseParts.Unregister(partEntityId);
            WorldsAdriftRebornGameServer.WorldEntities.Unregister(partEntityId);

            foreach (var peer in PeerManager.Instance.playerState.Keys.ToList())
            {
                if (SendOPHelper.SendRemoveEntityOP(peer, partEntityId))
                    PeerCheckoutCleanup.RemoveEntity(peer, partEntityId);
            }

            foreach (ShipPartSalvageRefund refund in refunds)
            {
                if (InventoryService.Grant(playerEntityId, refund.ItemTypeId, refund.Amount) != null)
                    SalvageFeedback.Send(playerEntityId, refund.ItemTypeId, refund.Amount,
                        "dismantled mounted " + mount.ItemType);
            }
            Console.WriteLine("[part-salvage] player " + playerEntityId + " dismantled mounted '"
                + mount.ItemType + "' entity " + partEntityId + " on docked hull "
                + mount.HullEntityId + "; refunded " + string.Join(", ", refunds.Select(
                    x => x.Amount + "x " + x.ItemTypeId)) + ".");
            return true;
        }

        private static bool CanFitRefund(long playerEntityId, IReadOnlyList<ShipPartSalvageRefund> refunds)
        {
            var trial = InventoryService.ForEntity(playerEntityId).Copy();
            int nextId = trial.Items.Count == 0 ? 1 : trial.Items.Max(x => x.ItemId) + 1;
            foreach (ShipPartSalvageRefund refund in refunds)
            {
                var item = Multiplayer.Inventory.InventoryPolicy.TryStackInto(
                    trial, refund.ItemTypeId, refund.Amount, 0,
                    InventoryWire.StackMaxOf(refund.ItemTypeId));
                if (item == null)
                {
                    item = Multiplayer.Inventory.InventoryPolicy.TryGrant(trial, nextId++,
                        refund.ItemTypeId, refund.Amount, 0,
                        new Dictionary<string, string>(), null, InventoryWire.Footprints);
                }
                if (item == null) return false;
            }
            return true;
        }
    }
}
