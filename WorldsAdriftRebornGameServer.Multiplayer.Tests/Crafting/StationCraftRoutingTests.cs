using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The routing rules behind the multi-slot station-craft fix: a station craft's
    /// per-slot 1005 must land on the STATION entity (whose UI reader clears the
    /// "waiting for server" flag), a personal craft's on the PLAYER, and a repeated
    /// station console-open must never reset an in-progress craft at that station.
    /// These run natively - no game install, no wire.
    /// </summary>
    public class StationCraftRoutingTests
    {
        private const long Player = 1001;
        private const long Station = 5005;
        private const long OtherStation = 6006;

        // A stand-in for PlacedCraftingStations.IsPlacedCraftingStation: only the
        // stations we "placed" are recognised.
        private static System.Func<long, bool> Placed(params long[] ids)
        {
            HashSet<long> set = new HashSet<long>(ids);
            return id => set.Contains(id);
        }

        [Fact]
        public void PersonalCraft_FieldEqualsPlayer_RoutesToPlayer()
        {
            // The multitool's own CraftingEntityId is the player entity, so the client
            // tags the 1003 update with the player id: this must stay on the player.
            long target = StationCraftRouting.ResolvePushTarget(Player, Player, Placed(Station));
            Assert.Equal(Player, target);
        }

        [Fact]
        public void PersonalCraft_UnsetField_RoutesToPlayer()
        {
            Assert.Equal(Player, StationCraftRouting.ResolvePushTarget(Player, 0, Placed(Station)));
            Assert.Equal(Player, StationCraftRouting.ResolvePushTarget(Player, -1, Placed(Station)));
        }

        [Fact]
        public void StationCraft_PlacedStation_RoutesToStation()
        {
            long target = StationCraftRouting.ResolvePushTarget(Player, Station, Placed(Station));
            Assert.Equal(Station, target);
        }

        [Fact]
        public void StationCraft_UnknownStation_FallsBackToPlayer()
        {
            // A valid-looking id we have no record of placing has no seeded reader;
            // routing there would silently drop the push, so fall back to the player.
            long target = StationCraftRouting.ResolvePushTarget(Player, Station, Placed(OtherStation));
            Assert.Equal(Player, target);
        }

        [Fact]
        public void NullPredicate_RoutesToPlayer()
        {
            Assert.Equal(Player, StationCraftRouting.ResolvePushTarget(Player, Station, null!));
        }

        [Fact]
        public void MultiSlotStationCraft_EverySlotFillTargetsTheStation()
        {
            // The bug: slot 0 filled, then every further slot hung. The invariant is
            // that the push target is stable across ALL slots of a station recipe - a
            // 4-slot Procedural Engine fills slot 0..3 all against the STATION, never
            // flipping to the player.
            System.Func<long, bool> placed = Placed(Station);
            for (int slot = 0; slot < 4; slot++)
            {
                long target = StationCraftRouting.ResolvePushTarget(Player, Station, placed);
                Assert.Equal(Station, target);
            }
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("engine.casing", true)]
        public void HasActiveCraft_TracksSelectedRecipe(string? schematicId, bool expected)
        {
            Assert.Equal(expected, StationCraftRouting.HasActiveCraft(schematicId));
        }

        [Fact]
        public void ShouldResetToIdle_FreshOpen_Resets()
        {
            // A brand-new session: no schematic, no station binding. The stale-schematic
            // crash guard must still fire on this genuine first open.
            Assert.True(StationCraftRouting.ShouldResetToIdleOnOpen(null, null, Station));
        }

        [Fact]
        public void ShouldResetToIdle_ActiveCraftAtThisStation_Preserves()
        {
            // The no-reset-of-active-session invariant: a repeated open while the player
            // is mid-fill at THIS station must NOT wipe the recipe/slots.
            Assert.False(StationCraftRouting.ShouldResetToIdleOnOpen("engine.casing", Station, Station));
        }

        [Fact]
        public void ShouldResetToIdle_ActiveCraftAtDifferentStation_Resets()
        {
            // A recipe left selected but bound to another station is not this station's
            // craft - open crash-safe.
            Assert.True(StationCraftRouting.ShouldResetToIdleOnOpen("engine.casing", OtherStation, Station));
        }

        [Fact]
        public void ShouldResetToIdle_LeftoverPersonalRecipe_Resets()
        {
            // A schematic left over from a personal craft (no station binding) must not
            // suppress the reset when opening a station.
            Assert.True(StationCraftRouting.ShouldResetToIdleOnOpen("torch", null, Station));
        }

        // ---- Category gate: the personal-tab blank-panel crash guard --------------
        //
        // The empty personal Crafting tab was an uncaught client NRE: a CraftingStation
        // recipe (lamp / proceduralEngineDefault) selected at the Assembly Station was
        // retained in the PLAYER'S 1005, and the Personal-only tab then tried to select
        // it against a category hierarchy that has no CraftingStation slot. The server
        // guarantee is that a recipe's category must match its crafting target, so the
        // player 1005 can never hold a CraftingStation recipe.

        [Fact]
        public void ExpectedCategory_PersonalVsStation()
        {
            Assert.Equal("Personal", StationCraftRouting.ExpectedCategoryFor(isPersonalTarget: true));
            Assert.Equal("CraftingStation", StationCraftRouting.ExpectedCategoryFor(isPersonalTarget: false));
        }

        [Theory]
        [InlineData("lamp category is CraftingStation", "CraftingStation")]
        [InlineData("proceduralEngineDefault category is CraftingStation", "CraftingStation")]
        public void CategoryGate_StationRecipe_RejectedFromPersonalTarget(string _, string category)
        {
            // THE fix: a station recipe can never be stored in / pushed to the player 1005.
            Assert.False(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: true, category));
        }

        [Fact]
        public void CategoryGate_PersonalRecipe_AcceptedByPersonalTarget()
        {
            // The 18 Personal records still list and select normally in the personal tab.
            Assert.True(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: true, "Personal"));
        }

        [Fact]
        public void CategoryGate_StationRecipe_AcceptedByStationTarget()
        {
            // Lamp + Procedural Engine still craft at the Assembly Station.
            Assert.True(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: false, "CraftingStation"));
        }

        [Fact]
        public void CategoryGate_PersonalRecipe_RejectedFromStationTarget()
        {
            // Symmetric: a Personal recipe cannot be loaded into a station's 1005 either.
            Assert.False(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: false, "Personal"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("personal")]   // case matters - the client bins by the exact string
        [InlineData("Cooking")]
        [InlineData("Shipyard")]
        public void CategoryGate_WrongOrMissingCategory_RejectedFromPersonalTarget(string? category)
        {
            Assert.False(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: true, category));
        }

        [Fact]
        public void OpenStation_SelectLamp_SelectEngine_ThenOpenPersonal_LeavesPlayerModelClean()
        {
            // End-to-end acceptance shape at the pure-routing level: with the Assembly
            // Station recognised, selecting Lamp then Engine both route to the STATION
            // and both pass the station's category gate; the personal context never
            // accepts either, so the player 1005 stays empty/Personal-only.
            System.Func<long, bool> placed = Placed(Station);

            // 1003 updates tagged with the station -> station target.
            long lampTarget = StationCraftRouting.ResolvePushTarget(Player, Station, placed);
            long engineTarget = StationCraftRouting.ResolvePushTarget(Player, Station, placed);
            Assert.Equal(Station, lampTarget);
            Assert.Equal(Station, engineTarget);
            Assert.True(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: lampTarget == Player, "CraftingStation"));
            Assert.True(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: engineTarget == Player, "CraftingStation"));

            // Opening the personal tab (field unset/own id) -> player target, which
            // rejects the CraftingStation recipe outright.
            long personalTarget = StationCraftRouting.ResolvePushTarget(Player, 0, placed);
            Assert.Equal(Player, personalTarget);
            Assert.False(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: personalTarget == Player, "CraftingStation"));
        }

        [Fact]
        public void UnknownStationFallsBackToPlayer_ButCategoryGateStillProtectsPlayerModel()
        {
            // The routing fallback for an UNRECOGNISED station id is the player (so the
            // wait flag still clears). Without the category gate that fallback is exactly
            // the contamination path - a CraftingStation recipe pushed to the player 1005.
            // The gate closes it: the personal target refuses the CraftingStation category.
            long target = StationCraftRouting.ResolvePushTarget(Player, Station, Placed(OtherStation));
            Assert.Equal(Player, target);
            Assert.False(StationCraftRouting.CategoryMatchesTarget(isPersonalTarget: target == Player, "CraftingStation"));
        }
    }
}
