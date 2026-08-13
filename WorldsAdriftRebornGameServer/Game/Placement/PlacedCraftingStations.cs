using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Game.Placement
{
    /// <summary>
    /// The server's record of every generic crafting station (the Assembly Station)
    /// a player has DEPLOYED this session, keyed by the world-entity id it was spawned
    /// as. It is the CraftingStation-category counterpart of <c>PlacedShipyards</c>,
    /// and it exists for exactly one reason the shipyard ledger also serves: to let
    /// the two entity-aware seams recognise a placed station by its shared entity id -
    ///
    ///   * <c>ComponentsSerializer</c>'s 1210 branch seeds the "Craft" interaction verb
    ///     (rather than PickUp) when the entity is a placed station, and
    ///   * <c>PlacementService.OpenCraftingStationConsole</c> answers the Craft
    ///     interaction with the 1005 PlayerStartCrafting echo only for a placed station.
    ///
    /// UNLIKE the shipyard, a placed crafting station carries NO ledger-seeded state:
    /// its 1004/1005 seeds are fixed idle defaults (ComponentsSerializer serves them
    /// entity-agnostically), and the prefab's baked <c>_craftingCategory</c> =
    /// CraftingStation is what makes the SAME 1005 signal open the generic parts UI
    /// instead of ship-build. So this ledger is a pure membership set (plus the owner
    /// uid for parity and future per-owner gating), not a seed source.
    ///
    /// In-memory only, exactly like <c>PlacedShipyards</c>: durable across a session,
    /// re-populated on boot because the restore path runs through the same
    /// <c>PlacementService.RegisterDeployable</c> core that records it. NOT
    /// thread-safe, deliberately: every writer runs on the single server poll loop.
    /// </summary>
    internal static class PlacedCraftingStations
    {
        private static readonly Dictionary<long, string> ByEntityId = new Dictionary<long, string>();

        /// <summary>Records a newly deployed crafting station. Idempotent per id.</summary>
        internal static void Register(long entityId, string ownerCharacterUid)
        {
            ByEntityId[entityId] = ownerCharacterUid ?? "";
        }

        /// <summary>Whether this entity id is a placed crafting station.</summary>
        internal static bool IsPlacedCraftingStation(long entityId)
        {
            return ByEntityId.ContainsKey(entityId);
        }

        /// <summary>The owner character uid for a placed station, or "" if unknown.</summary>
        internal static string OwnerFor(long entityId)
        {
            return ByEntityId.TryGetValue(entityId, out string? owner) ? owner : "";
        }

        /// <summary>
        /// Drops a station that was PACKED back into inventory (station pickup).
        /// The pickup tombstone (StationPickupLedger) is what keeps the ghost
        /// entity sunk and unavailable for late joiners. A no-op for an unknown
        /// id; returns whether anything was removed.
        /// </summary>
        internal static bool Remove(long entityId)
        {
            return ByEntityId.Remove(entityId);
        }

        /// <summary>How many crafting stations have been deployed this session.</summary>
        internal static int Count => ByEntityId.Count;
    }
}
