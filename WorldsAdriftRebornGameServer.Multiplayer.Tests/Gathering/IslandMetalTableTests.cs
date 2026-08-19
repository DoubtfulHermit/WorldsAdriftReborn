using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Gathering
{
    /// <summary>
    /// THE REGRESSION THESE EXIST FOR: every deposit a live player could reach was
    /// iron, because the live server runs Haven only and Haven's metal was a
    /// hardcoded literal. The per-island table that should have supplied it was
    /// imported, provenance-labelled and already driving the release world's 1930
    /// deposits - this path just never asked it.
    ///
    /// The tests that matter here are the ones that FAIL IF IT COLLAPSES BACK: if
    /// Haven's ring loses its variety, or if the release catalogue stops varying,
    /// these say so rather than the world quietly going uniform again.
    /// </summary>
    public class IslandMetalTableTests
    {
        [Fact]
        public void Haven_yields_more_than_one_metal()
        {
            IReadOnlyList<string> metals = Enumerable.Range(0, IslandMetalTable.HavenRing.Count)
                .Select(i => IslandMetalTable.ItemTypeIdOf(
                    IslandMetalTable.DrawFor(IslandCatalog.HavenId, i)!))
                .ToList();

            Assert.True(metals.Distinct().Count() >= 5,
                "Haven's ring should span the tier-1 cohort, not one metal");
        }

        [Fact]
        public void Havens_metals_are_exactly_the_surveyed_tier_one_cohort()
        {
            // The ring is WAREBORN TUNING, but its MEMBERSHIP is not free: it is the
            // set of metals the 46 surveyed tier-1 islands actually carry. If a metal
            // appears here that no tier-1 island has, somebody has started inventing
            // rather than inferring, and that is the line this project does not cross.
            HashSet<string> cohort = ReleaseWorldCatalog.All
                .Where(island => island.Survey.Tier == 1)
                .SelectMany(island => island.Survey.Metals)
                .Select(IslandMetalTable.ItemTypeIdOf)
                .ToHashSet(StringComparer.Ordinal);

            Assert.NotEmpty(cohort);

            foreach (string metal in IslandMetalTable.HavenRing.Distinct())
            {
                Assert.Contains(metal, cohort);
            }
        }

        [Fact]
        public void Haven_leans_on_iron_so_the_first_recipe_is_not_starved()
        {
            int iron = IslandMetalTable.HavenRing
                .Count(m => string.Equals(m, IslandMetalTable.FallbackMetal, StringComparison.Ordinal));

            Assert.True(iron * 2 >= IslandMetalTable.HavenRing.Count,
                "iron should be at least half of Haven's ring; it is what the starter loop consumes");
        }

        [Fact]
        public void The_deposit_nearest_the_spawn_point_is_always_iron()
        {
            // Index 0 is the proven placement 8.9 m from the spawn. A new player
            // finding bronze there would read as the starter recipe being broken.
            Assert.Equal(IslandMetalTable.FallbackMetal, MetalDeposits.NodeAt(0).MetalType);
        }

        [Fact]
        public void Havens_deposits_are_no_longer_all_one_metal()
        {
            // THE LIVE PATH, not the ring in isolation. MetalDeposits.HavenPlacements
            // is what the server actually spawns; if this ever reads 1 again, the
            // wiring between the table and the placements has been undone.
            IReadOnlyList<string> metals = Enumerable.Range(0, MetalDeposits.HavenPlacements.Count)
                .Select(i => MetalDeposits.NodeAt(i).MetalType)
                .ToList();

            Assert.True(metals.Count > 1, "Haven should have deposits at all");
            Assert.True(metals.Distinct().Count() >= 5,
                "Haven's spawned deposits should span several metals, not one");
        }

        [Fact]
        public void Every_metal_Haven_can_yield_is_a_real_metal_row()
        {
            // An itemTypeId the client's item database has never heard of is a hard
            // client-side NRE. These are the eighteen ids itemData.json ships under
            // category Metal, minus the two this project invented (cobalt, aurium),
            // which is deliberately a stricter bar than "it grants something".
            HashSet<string> shipped = new(StringComparer.Ordinal)
            {
                "iron", "lead", "bronze", "tin", "orthite", "steel", "copper", "titanium",
                "nickel", "epilar", "silver", "aluminium", "gold", "eternium", "tungsten",
            };

            foreach (string metal in IslandMetalTable.HavenRing.Distinct())
            {
                Assert.Contains(metal, shipped);
            }
        }

        [Fact]
        public void Haven_carries_its_declared_quality_rather_than_the_tier_one_band()
        {
            // Pinned deliberately. Dropping Haven to the surveyed tier-1 band (1..4)
            // is a legitimate follow-up and a maintainer's call, but it is a BALANCE
            // CUT and must not arrive silently inside a metal-variety change.
            for (int i = 0; i < IslandMetalTable.HavenRing.Count; i++)
            {
                Assert.Equal(IslandMetalTable.HavenQuality,
                    IslandMetalTable.DrawFor(IslandCatalog.HavenId, i)!.Quality);
            }
        }

        [Fact]
        public void A_surveyed_island_draws_its_own_metals_and_nothing_else()
        {
            ReleaseIslandRecord island = ReleaseWorldCatalog.All
                .First(record => record.Survey.Metals.Count > 1);

            HashSet<string> own = island.Survey.Metals
                .Select(IslandMetalTable.ItemTypeIdOf)
                .ToHashSet(StringComparer.Ordinal);

            for (int i = 0; i < own.Count * 3; i++)
            {
                SurveyedMetal draw = IslandMetalTable.DrawFor(island.Definition.Id, i)!;
                Assert.Contains(IslandMetalTable.ItemTypeIdOf(draw), own);
            }
        }

        [Fact]
        public void An_island_nobody_has_heard_of_draws_nothing_rather_than_iron()
        {
            // Null, not a fallback: what an unknown island means is the CALLER's
            // decision, and silently answering "iron" here is exactly the shape of
            // the hardcode this type replaces.
            Assert.Null(IslandMetalTable.DrawFor(new IslandId("no-such-island"), 0));
            Assert.Empty(IslandMetalTable.SurveyedMetalsFor(new IslandId("no-such-island")));
        }

        [Fact]
        public void The_draw_is_deterministic_across_calls()
        {
            // No RNG, no clock: a restart has to reproduce the same world, or state
            // keyed on a deposit's index stops meaning anything.
            for (int i = 0; i < 40; i++)
            {
                Assert.Equal(
                    IslandMetalTable.DrawFor(IslandCatalog.HavenId, i)!.Name,
                    IslandMetalTable.DrawFor(IslandCatalog.HavenId, i)!.Name);
            }
        }

        [Fact]
        public void Catalogue_capitalisation_is_normalised_to_the_item_database_spelling()
        {
            // The catalogues say "Aluminium"; itemData.json is keyed "aluminium".
            Assert.Equal("aluminium", IslandMetalTable.ItemTypeIdOf(new SurveyedMetal("Aluminium", 4)));
        }

        [Fact]
        public void A_negative_deposit_index_is_a_throw_rather_than_a_wrapped_lookup()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => IslandMetalTable.DrawFor(IslandCatalog.HavenId, -1));
        }
    }
}
