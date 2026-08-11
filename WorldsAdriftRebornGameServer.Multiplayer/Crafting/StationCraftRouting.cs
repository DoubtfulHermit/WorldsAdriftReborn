using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// Pure routing rules for a crafting-state (1005) push: WHICH entity it must
    /// land on, and WHEN a station console re-open may reset the panel to idle.
    ///
    /// WHY THIS EXISTS (the multi-slot station-craft bug): the client's crafting UI
    /// reads its slot state - and clears its per-slot "waiting for server" flag -
    /// from ONE entity's 1005 CraftingStationClientState. For personal (multitool)
    /// crafting that reader (MultitoolCraftingBehaviour) lives on the PLAYER entity,
    /// so its CraftingEntityId is the player. For a placed crafting station the
    /// reader (CraftingStationBehaviour) lives on the STATION entity, so its
    /// CraftingEntityId is the station. The client sends every 1003 crafting action
    /// on its own player component but tags each with craftingStationEntityId (the
    /// active UI's CraftingEntityId - VERIFIED CraftingUI/CharacterSheetScreen). So
    /// the server must mirror the 1005 back to the SAME entity the active UI binds
    /// to. Pushing a station craft's per-slot 1005 to the player (as personal craft
    /// does) never reaches the station UI, so its wait flag is never cleared and
    /// every slot after the first hangs on "waiting for server".
    ///
    /// These are element-agnostic and depend on nothing but longs and a membership
    /// predicate, so they unit-test natively - no game install, no wire.
    /// </summary>
    public static class StationCraftRouting
    {
        /// <summary>
        /// The entity whose 1005 the active crafting UI reads its slot state from:
        /// the STATION when the 1003 update names a placed crafting station that is
        /// not the player itself, otherwise the PLAYER (personal multitool craft,
        /// whose own reader's CraftingEntityId is the player entity). The
        /// <paramref name="isPlacedCraftingStation"/> gate keeps a redirect from ever
        /// aiming at an entity with no CraftingStationBehaviour reader seeded.
        /// </summary>
        public static long ResolvePushTarget(long playerEntityId, long craftingStationEntityId, Func<long, bool> isPlacedCraftingStation)
        {
            if (isPlacedCraftingStation != null
                && craftingStationEntityId > 0
                && craftingStationEntityId != playerEntityId
                && isPlacedCraftingStation(craftingStationEntityId))
            {
                return craftingStationEntityId;
            }

            return playerEntityId;
        }

        /// <summary>
        /// Whether a player has a craft in progress: a recipe is selected on their
        /// session. An empty/absent schematic id means no craft is underway.
        /// </summary>
        public static bool HasActiveCraft(string? schematicId) => !string.IsNullOrEmpty(schematicId);

        /// <summary>
        /// Whether a station console open must re-assert the crash-safe idle shape
        /// (empty schematic + empty slots + closed countdown) on the station's 1005.
        ///
        /// It must on a FIRST/fresh open (no craft in progress bound to THIS station),
        /// so a stale clientSchematicId left on the station cannot drive the client's
        /// CraftingStationSchematicList.SelectSchematic NRE / SyncCraftingItems OOB.
        /// It must NOT when this player already has an active craft AT THIS station -
        /// re-asserting idle there would wipe the in-progress recipe and slot display
        /// the player is halfway through filling. In that case the station's 1005
        /// already holds the live slot state (the per-slot fills now push to it), so
        /// the open is a plain re-echo that preserves what is there.
        /// </summary>
        public static bool ShouldResetToIdleOnOpen(string? sessionSchematicId, long? sessionStationEntityId, long openingStationEntityId)
        {
            bool activeHere = HasActiveCraft(sessionSchematicId)
                && sessionStationEntityId.HasValue
                && sessionStationEntityId.Value == openingStationEntityId;
            return !activeHere;
        }
    }
}
