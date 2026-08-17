using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>Authoritative salvage-beam dismantle transaction for mounted ship parts.</summary>
    internal static class MountedPartSalvageService
    {
        /// <summary>Returns false only when the target is not a crafted ship part.</summary>
        internal static bool HandleShot(long playerEntityId, long partEntityId)
        {
            MountedParts.Mount? found = MountedParts.MountFor(partEntityId);
            LoosePartDefinition? definition = LooseParts.DefFor(partEntityId);
            if (definition == null) return false;
            MountedParts.Mount? mount = found;
            string requester = CharacterOwnership.UidForEntity(playerEntityId);
            FixedPointPosition targetPosition = TargetPosition(partEntityId, mount);
            long yardId = OwnedYardContaining(requester, targetPosition);
            SchematicRecord? recipe = SchematicHelper.Get(definition.SchematicId);
            IReadOnlyList<ShipPartSalvageRefund> refunds = recipe == null
                ? Array.Empty<ShipPartSalvageRefund>()
                : ShipPartSalvagePolicy.Refunds(recipe);
            ShipPartSalvageReject verdict = ShipPartSalvagePolicy.Evaluate(
                craftedPart: true, insideOwnedShipyard: yardId > 0,
                recipeKnown: recipe != null && refunds.Count > 0);
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
            bool durableRemoved = mount.HasValue
                ? WorldStatePersistence.RemoveMountedPart(uid ?? "")
                : WorldStatePersistence.RemoveLoosePart(uid ?? "");
            if (!durableRemoved)
            {
                Console.WriteLine("[part-salvage] persistence failed for part " + partEntityId
                    + "; leaving it intact.");
                return true;
            }

            if (mount.HasValue)
            {
                MountedParts.Unmount(partEntityId);
                WorldsAdriftRebornGameServer.ShipMembership.Unregister(partEntityId, mount.Value.HullEntityId);
            }
            WorldsAdriftRebornGameServer.Sails.Unregister(partEntityId);
            WorldsAdriftRebornGameServer.Lamps.Unregister(partEntityId);
            WorldsAdriftRebornGameServer.Horns.Unregister(partEntityId);
            LooseParts.Unregister(partEntityId);
            LocalDomainOwnership.RemoveEntity(
                WorldsAdriftRebornGameServer.DomainHost, partEntityId);
            if (mount.HasValue)
                WorldsAdriftRebornGameServer.Flight.RefreshDomainOwnership(mount.Value.HullEntityId);
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
                        "dismantled " + definition.ItemType);
            }
            Console.WriteLine("[part-salvage] player " + playerEntityId + " dismantled "
                + (mount.HasValue ? "mounted" : "loose") + " '" + definition.ItemType
                + "' entity " + partEntityId + " inside owned shipyard " + yardId
                + "; refunded " + string.Join(", ", refunds.Select(
                    x => x.Amount + "x " + x.ItemTypeId)) + ".");
            return true;
        }

        private static FixedPointPosition TargetPosition(long partEntityId, MountedParts.Mount? mount)
        {
            if (mount.HasValue)
            {
                long hullId = mount.Value.HullEntityId;
                if (WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(
                        hullId, out FixedPointPosition flown, out _)) return flown;
                WorldEntity? hull = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(hullId);
                if (hull != null) return hull.Position;
            }
            return WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(partEntityId)?.Position
                ?? default;
        }

        private static long OwnedYardContaining(string requester, FixedPointPosition position)
        {
            if (string.IsNullOrEmpty(requester)) return 0;
            foreach (long yardId in Placement.PlacedShipyards.EntityIds)
            {
                if (Placement.PlacedShipyards.SeedFor(yardId).OwnerCharacterUid != requester) continue;
                WorldEntity? yard = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(yardId);
                if (yard != null && ShipyardDockingPolicy.IsWithin(
                        position, yard.Position, ShipPartSalvagePolicy.WorkRadiusMetres)) return yardId;
            }
            return 0;
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
