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

        /// <summary>The recipe category the personal (multitool) Craft tab shows and accepts.</summary>
        public const string PersonalCategory = "Personal";

        /// <summary>The recipe category the generic Assembly Station shows and accepts.</summary>
        public const string CraftingStationCategory = "CraftingStation";

        /// <summary>
        /// The recipe category a crafting TARGET accepts. The personal multitool model
        /// (target == player) shows exactly the Personal records; a placed Assembly
        /// Station shows exactly the CraftingStation records. This is baked per prefab
        /// on the client (MultitoolCraft -> Personal; the station's _craftingCategory ->
        /// CraftingStation), so the server mirrors it to decide which selections are
        /// legal in which context.
        /// </summary>
        public static string ExpectedCategoryFor(bool isPersonalTarget)
            => isPersonalTarget ? PersonalCategory : CraftingStationCategory;

        /// <summary>
        /// Whether a selected recipe's category is allowed to be loaded into a target's
        /// 1005 model. THE personal-tab crash guard: the client builds a Personal-only
        /// category hierarchy for the multitool tab and then selects whatever recipe the
        /// player's 1005 retained WITHOUT a compatibility check
        /// (CraftingStationSchematicList.SelectSchematic dereferences the null slot from
        /// CategoryPressed for a mismatched category). By refusing to ever store a
        /// non-Personal recipe in the personal target's session - and symmetrically a
        /// non-CraftingStation recipe in a station target - the player 1005 can only ever
        /// hold "" or a Personal recipe, so CategoryPressed(Personal) always resolves and
        /// the tab never throws. Comparison is ordinal: the client bins by the exact,
        /// case-sensitive category string.
        /// </summary>
        public static bool CategoryMatchesTarget(bool isPersonalTarget, string? recordCategory)
            => string.Equals(recordCategory ?? "", ExpectedCategoryFor(isPersonalTarget), System.StringComparison.Ordinal);

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
