using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The release world's trees: the budget calibration, the species join, the
    /// shipped seats, and the entities they become.
    ///
    /// The point of most of these is to keep an OFFLINE artefact honest. The seats
    /// are authored by tools/world-import/generate-release-tree-placements.py and
    /// embedded as JSON, so nothing at runtime re-derives them and a bad
    /// regeneration would otherwise ship unnoticed. Each rule the generator claims
    /// to enforce is re-checked here against the file it actually produced.
    /// </summary>
    public sealed class ReleaseTreeBudgetTests
    {
        /// <summary>
        /// Haven is the anchor: 80 trees over a 90-cell surface is the only tree
        /// population this server has run live, and the density is taken from it
        /// rather than invented. If this stops holding, the calibration story in
        /// ReleaseTreeBudget's remarks has quietly stopped being true.
        ///
        /// Note Haven's OWN density lands above the release-world ceiling - 90 cells
        /// asks for 80 and the clamp allows 60. That is intended and is asserted
        /// here so it is on the record rather than a surprise: the ceiling exists to
        /// bound boot registration across 72 islands, a pressure Haven alone never
        /// had. Haven is not in the release catalogue and keeps its own 80.
        /// </summary>
        [Fact]
        public void Haven_density_is_the_anchor_and_sits_above_the_release_ceiling()
        {
            Assert.Equal(80.0, ReleaseTreeBudget.TreesPerCell * 90.0, 9);
            Assert.Equal(40, ReleaseTreeBudget.CountFor(45));
            Assert.Equal(ReleaseTreeBudget.MaxTrees, ReleaseTreeBudget.CountFor(90));
        }

        [Fact]
        public void Small_islands_get_the_floor_and_large_islands_get_the_ceiling()
        {
            Assert.Equal(ReleaseTreeBudget.MinTrees, ReleaseTreeBudget.CountFor(1));
            Assert.Equal(ReleaseTreeBudget.MinTrees, ReleaseTreeBudget.CountFor(9));
            Assert.Equal(ReleaseTreeBudget.MaxTrees, ReleaseTreeBudget.CountFor(734));
            Assert.Equal(ReleaseTreeBudget.MaxTrees, ReleaseTreeBudget.CountFor(100000));
        }

        /// <summary>A malformed surface must give a thin island, never a crash.</summary>
        [Fact]
        public void Non_positive_cell_counts_degrade_to_the_floor()
        {
            Assert.Equal(ReleaseTreeBudget.MinTrees, ReleaseTreeBudget.CountFor(0));
            Assert.Equal(ReleaseTreeBudget.MinTrees, ReleaseTreeBudget.CountFor(-5));
        }

        [Fact]
        public void Count_never_leaves_the_clamp_and_never_decreases_with_area()
        {
            int previous = 0;
            for (int cells = 0; cells <= 900; cells++)
            {
                int count = ReleaseTreeBudget.CountFor(cells);
                Assert.InRange(count, ReleaseTreeBudget.MinTrees, ReleaseTreeBudget.MaxTrees);
                Assert.True(count >= previous, "budget must be monotonic in surface area");
                previous = count;
            }
        }
    }

    public sealed class ReleaseTreeSpeciesTests
    {
        private static readonly string[] SurveyedWoods =
        {
            "ash", "birch", "cedar", "chestnut", "elm", "hemlock", "oak", "palm",
        };

        /// <summary>
        /// The survey's whole species vocabulary must be placeable, or an island
        /// recorded as wooded would silently grow fewer trees than its seats.
        /// </summary>
        [Fact]
        public void Every_surveyed_wood_has_a_verified_prefab()
        {
            Assert.All(SurveyedWoods, wood => Assert.NotNull(ReleaseTreeSpecies.PrefabForWood(wood)));
            Assert.Equal(8, ReleaseTreeSpecies.PlaceableWoods.Count);
        }

        /// <summary>
        /// The map is derived from VerifiedSpecies, so every prefab it hands back
        /// must genuinely drop the wood it was asked for. This is the guard against
        /// a future edit to VerifiedSpecies quietly pairing a name with the wrong
        /// wood - which would pay out the wrong material forever rather than fail.
        /// </summary>
        [Fact]
        public void Every_prefab_actually_drops_the_wood_it_is_mapped_from()
        {
            foreach (string wood in SurveyedWoods)
            {
                string prefab = ReleaseTreeSpecies.PrefabForWood(wood)!;
                Assert.Equal(wood, TreeSpecies.WoodFor(prefab));
                Assert.Contains(prefab, WorldEntities.VerifiedSpecies);
            }
        }

        [Fact]
        public void Species_cycle_round_robin_so_placement_is_stable()
        {
            string[] woods = { "cedar", "oak" };
            Assert.Equal(ReleaseTreeSpecies.PrefabForWood("cedar"), ReleaseTreeSpecies.PrefabAt(woods, 0));
            Assert.Equal(ReleaseTreeSpecies.PrefabForWood("oak"), ReleaseTreeSpecies.PrefabAt(woods, 1));
            Assert.Equal(ReleaseTreeSpecies.PrefabForWood("cedar"), ReleaseTreeSpecies.PrefabAt(woods, 2));
            Assert.Equal(ReleaseTreeSpecies.PrefabForWood("oak"), ReleaseTreeSpecies.PrefabAt(woods, 3));
        }

        /// <summary>Null, not a birch default - a silent substitution is the bug.</summary>
        [Fact]
        public void Unknown_input_yields_null_rather_than_a_default()
        {
            Assert.Null(ReleaseTreeSpecies.PrefabForWood("mahogany"));
            Assert.Null(ReleaseTreeSpecies.PrefabForWood(null));
            Assert.Null(ReleaseTreeSpecies.PrefabForWood("  "));
            Assert.Null(ReleaseTreeSpecies.PrefabAt(Array.Empty<string>(), 0));
            Assert.Null(ReleaseTreeSpecies.PrefabAt(new[] { "oak" }, -1));
        }
    }

    public sealed class ReleaseTreeCatalogTests
    {
        [Fact]
        public void Shipped_catalogue_covers_every_island_the_survey_does_not_call_treeless()
        {
            Assert.Equal(252, ReleaseTreeCatalog.All.Count);
            Assert.Equal(13266, ReleaseTreeCatalog.TotalTrees);
            Assert.Equal(252, ReleaseTreeCatalog.All.Select(x => x.WorkshopId).Distinct().Count());
            Assert.Equal(254, ReleaseWorldCatalog.All.Count);
        }

        /// <summary>
        /// THE REGRESSION GUARD FOR THE BUG THIS FILE EXISTS TO FIX.
        ///
        /// The generator used to skip any island whose survey `trees` array was
        /// empty, which is 180 islands - including 32 of the 46 a graduating player
        /// can be teleported to. An empty array is UNSURVEYED, not treeless; the
        /// evidence is in tools/world-import/wood_inference.py. The only islands
        /// that may carry no wood are the two the survey names explicitly.
        /// </summary>
        [Fact]
        public void Only_an_explicit_No_trees_survey_leaves_an_island_without_wood()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                bool surveyedTreeless = island.Survey.Trees
                    .Any(name => string.Equals(name, "No trees", StringComparison.OrdinalIgnoreCase));
                ReleaseTreeIsland? seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId);

                if (surveyedTreeless)
                {
                    Assert.Null(seats);
                    continue;
                }

                Assert.NotNull(seats);
                Assert.NotEmpty(seats!.Woods);
            }

            Assert.Equal(2, ReleaseWorldCatalog.All.Count(island => island.Survey.Trees
                .Any(name => string.Equals(name, "No trees", StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>
        /// A surveyed island grows exactly what the survey recorded, and says so.
        /// An unsurveyed one grows an inference and says THAT - the label is what
        /// stops a composed species being read back later as evidence.
        /// </summary>
        [Fact]
        public void Surveyed_islands_keep_their_own_species_and_the_rest_are_labelled_inferred()
        {
            int surveyed = 0;
            int inferred = 0;
            foreach (ReleaseTreeIsland seats in ReleaseTreeCatalog.All)
            {
                ReleaseIslandRecord island = ReleaseWorldCatalog.All
                    .Single(x => x.Survey.WorkshopId == seats.WorkshopId);
                List<string> species = island.Survey.Trees
                    .Select(name => name.ToLowerInvariant())
                    .Distinct()
                    .ToList();

                if (species.Count > 0)
                {
                    Assert.Equal(WoodTableSource.Survey, seats.WoodSource);
                    Assert.Equal(species.OrderBy(x => x), seats.Woods.OrderBy(x => x));
                    surveyed++;
                }
                else
                {
                    Assert.Equal(WoodTableSource.InferredTier, seats.WoodSource);
                    Assert.NotEmpty(seats.Woods);
                    inferred++;
                }

                // Whatever the rung, the vocabulary is still exactly the eight
                // authored woods - the inference draws from the survey's palette
                // and can never mint a ninth name.
                Assert.All(seats.Woods, wood => Assert.NotNull(ReleaseTreeSpecies.PrefabForWood(wood)));
            }

            Assert.Equal(72, surveyed);
            Assert.Equal(180, inferred);
        }

        /// <summary>
        /// The tier palette derived in wood_inference.py is monotone: a wood seen in
        /// the shallows is available deeper, never the reverse. Cedar was never
        /// observed on a tier-1 island and hemlock never on tier 1 or 2, so an
        /// INFERRED wilderness island must not grow either. Surveyed islands are
        /// exempt - evidence outranks the rule derived from it.
        /// </summary>
        [Fact]
        public void An_inferred_tier_one_island_grows_only_woods_observed_at_tier_one()
        {
            string[] tierOnePalette = { "ash", "birch", "chestnut", "elm", "oak", "palm" };
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                ReleaseTreeIsland seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId)!;
                Assert.NotNull(seats);
                if (seats.WoodSource != WoodTableSource.InferredTier) continue;
                Assert.All(seats.Woods, wood => Assert.Contains(wood, tierOnePalette));
            }
        }

        /// <summary>
        /// The count formula lives in two languages - C# here and Python in the
        /// generator - so this asserts they agree on every shipped island. Without
        /// it the two drift apart silently and the calibration documented in
        /// ReleaseTreeBudget stops describing the file that actually ships.
        ///
        /// The budget is a CEILING, not a promise: a surface may be too small or
        /// too steep to seat what the formula asks for. Exactly one island is in
        /// that state - Belial, three surface samples wide, all three already taken
        /// by its own surveyed databanks - and the count of exceptions is pinned so
        /// a regression that starts under-filling islands shows up here.
        /// </summary>
        [Fact]
        public void Shipped_seat_counts_match_the_budget_formula()
        {
            Assert.All(ReleaseTreeCatalog.All, island =>
                Assert.InRange(island.Points.Count, 0, ReleaseTreeBudget.CountFor(island.Lod0Cells)));
            Assert.Equal(251, ReleaseTreeCatalog.All.Count(
                island => island.Points.Count == ReleaseTreeBudget.CountFor(island.Lod0Cells)));
            Assert.Equal(new[] { "Belial" }, ReleaseTreeCatalog.All
                .Where(island => island.Points.Count != ReleaseTreeBudget.CountFor(island.Lod0Cells))
                .Select(island => island.Name)
                .ToArray());
        }

        /// <summary>
        /// THE ACCEPTANCE CRITERION FOR THE WILDERNESS, and the reason the generator
        /// draws its first seats from an annulus around the island's arrival pad
        /// rather than scattering uniformly.
        ///
        /// A player graduating from the Wilderness shrine is teleported onto
        /// `Landing` and has to find wood from there. Hash-ordered scattering does
        /// not know the pad exists: before this rule the nearest of Monkees Greenful
        /// Hills' 60 trees was 50.6 m away and nothing bounded it. It also has to be
        /// bounded BELOW, because the scatter was free to pick the pad's own surface
        /// sample and put a trunk at 0.0 m on three tier-1 islands.
        /// </summary>
        [Fact]
        public void Every_tier_one_island_has_wood_within_a_short_walk_of_its_arrival_pad()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                ReleaseTreeIsland seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId)!;
                Assert.NotNull(seats);
                Assert.NotEmpty(seats.Points);

                double nearest = seats.Points.Min(seat => Math.Sqrt(
                    Math.Pow(seat.X - island.Landing.LocalX, 2)
                    + Math.Pow(seat.Y - island.Landing.LocalY, 2)
                    + Math.Pow(seat.Z - island.Landing.LocalZ, 2)));
                Assert.InRange(nearest, 6.0, 60.0);
            }
        }

        /// <summary>The same guarantee for ore, from the runtime catalogue.</summary>
        [Fact]
        public void Every_tier_one_island_has_ore_within_a_short_walk_of_its_arrival_pad()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                Assert.NotEmpty(island.Deposits);
                // Deposits are stored already lifted into world fixed point, so the
                // pad is lifted the same way rather than the deposits pushed back.
                FixedPointPosition pad = island.Definition.LocalToGlobal(
                    island.Landing.LocalX, island.Landing.LocalY, island.Landing.LocalZ);
                double nearest = island.Deposits.Min(deposit => Math.Sqrt(
                    Math.Pow((deposit.Position.X - pad.X) / 4096.0, 2)
                    + Math.Pow((deposit.Position.Y - pad.Y) / 4096.0, 2)
                    + Math.Pow((deposit.Position.Z - pad.Z) / 4096.0, 2)));
                Assert.InRange(nearest, 6.0, 60.0);
            }
        }

        /// <summary>
        /// The spacing rule is what stops trees growing inside each other. It is
        /// applied offline, so it is verified here against the shipped result -
        /// with the generator's documented relaxation ladder as the floor, since
        /// small or steep islands legitimately fall back to a tighter rung.
        /// </summary>
        [Fact]
        public void Seats_on_an_island_are_never_closer_than_the_relaxed_spacing()
        {
            const double RelaxedFloor = 5.0;
            Assert.All(ReleaseTreeCatalog.All, island =>
            {
                for (int i = 0; i < island.Points.Count; i++)
                {
                    for (int j = i + 1; j < island.Points.Count; j++)
                    {
                        (double ax, double ay, double az) = island.Points[i];
                        (double bx, double by, double bz) = island.Points[j];
                        double distance = Math.Sqrt(
                            (ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
                        Assert.True(distance >= RelaxedFloor,
                            island.Name + ": seats " + i + " and " + j + " are " + distance + " m apart");
                    }
                }
            });
        }

        /// <summary>
        /// A seat outside its own island's collision envelope is a tree in mid-air
        /// or underground. The extractor once produced surfaces wrong by tens of
        /// metres, so this is checked rather than trusted.
        /// </summary>
        [Fact]
        public void Every_seat_lies_inside_its_own_island_envelope()
        {
            const double Tolerance = 1.0;
            foreach (ReleaseTreeIsland island in ReleaseTreeCatalog.All)
            {
                ReleaseIslandRecord record = ReleaseWorldCatalog.All
                    .Single(x => x.Survey.WorkshopId == island.WorkshopId);
                IslandTerrainEnvelope envelope = record.Envelope;
                foreach ((double x, double y, double z) in island.Points)
                {
                    Assert.True(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z));
                    Assert.InRange(x, envelope.MinX - Tolerance, envelope.MaxX + Tolerance);
                    Assert.InRange(y, envelope.MinY - Tolerance, envelope.MaxY + Tolerance);
                    Assert.InRange(z, envelope.MinZ - Tolerance, envelope.MaxZ + Tolerance);
                }
            }
        }

        /// <summary>
        /// Trees must not grow through the deposits and databanks already on the
        /// island. The generator passes those in as occupied seats; this proves the
        /// shipped file actually respects it.
        /// </summary>
        [Fact]
        public void No_seat_collides_with_an_existing_deposit_or_databank()
        {
            const double MinClearance = 5.0;
            foreach (ReleaseTreeIsland island in ReleaseTreeCatalog.All)
            {
                ReleaseIslandRecord record = ReleaseWorldCatalog.All
                    .Single(x => x.Survey.WorkshopId == island.WorkshopId);

                // Both sides are compared in world fixed point: the catalogue keeps
                // deposits and databanks already lifted out of island-local space.
                foreach ((double x, double y, double z) in island.Points)
                {
                    FixedPointPosition seat = record.Definition.LocalToGlobal(x, y, z);
                    foreach (FixedPointPosition other in record.Deposits.Select(d => d.Position)
                                 .Concat(record.Databanks))
                    {
                        double dx = (seat.X - other.X) / 4096.0;
                        double dy = (seat.Y - other.Y) / 4096.0;
                        double dz = (seat.Z - other.Z) / 4096.0;
                        Assert.True(Math.Sqrt(dx * dx + dy * dy + dz * dz) >= MinClearance,
                            island.Name + ": a tree seat sits on an existing resource");
                    }
                }
            }
        }
    }

    public sealed class ReleaseWorldTreeEntityTests
    {
        private static List<WorldEntity> AllTrees() => ReleaseWorldCatalog.All
            .SelectMany(ReleaseWorldTrees.For)
            .ToList();

        [Fact]
        public void Every_authored_seat_becomes_exactly_one_entity_with_a_unique_key()
        {
            List<WorldEntity> trees = AllTrees();
            Assert.Equal(ReleaseTreeCatalog.TotalTrees, trees.Count);
            Assert.Equal(trees.Count, trees.Select(tree => tree.Key).Distinct().Count());
        }

        /// <summary>
        /// THE AUTHORITATIVE-NOT-DECORATIVE GUARD, and the most important test here.
        ///
        /// A resource key outside ResourceInterestPolicy's streamed list is
        /// broadcast eagerly instead of spatially streamed AND is skipped by
        /// WorldResourceActivation.ActivateBoundResources - which renders a tree
        /// that yields nothing when cut. That is the exact bug class HANDOVER
        /// records. Keeping the "tree-" stem is what prevents it, so it is asserted
        /// rather than left to the naming convention holding.
        /// </summary>
        [Fact]
        public void Every_tree_is_streamed_and_therefore_activated()
        {
            Assert.All(AllTrees(), tree =>
                Assert.True(ResourceInterestPolicy.IsStreamedResourceKey(tree.Key),
                    tree.Key + " would render but never become harvestable"));
        }

        /// <summary>
        /// The activation path recognises a tree ONLY by asset name, via
        /// TreeSpecies.WoodFor. An asset that is not a known tree prefab would
        /// register, stream, render from baked geometry, and never be planted.
        /// </summary>
        [Fact]
        public void Every_tree_asset_resolves_to_a_wood_and_a_recovered_topology()
        {
            Assert.All(AllTrees(), tree =>
            {
                Assert.NotNull(TreeSpecies.WoodFor(tree.AssetName));
                Assert.NotNull(TreeTopologies.For(tree.AssetName));
                Assert.Contains(tree.AssetName, WorldEntities.VerifiedSpecies);
            });
        }

        /// <summary>
        /// An island grows the species the survey recorded for it, and nothing else.
        /// This is the whole reason species variety is switched ON out here while it
        /// stays off on Haven: here it is reconstruction, there it would be taste.
        /// </summary>
        [Fact]
        public void An_island_grows_only_its_own_catalogued_species()
        {
            foreach (ReleaseIslandRecord island in ReleaseWorldCatalog.All)
            {
                ReleaseTreeIsland? seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId);
                if (seats == null || seats.Points.Count == 0)
                {
                    Assert.Empty(ReleaseWorldTrees.For(island));
                    continue;
                }

                HashSet<string> expected = seats.Woods.ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> actual = ReleaseWorldTrees.For(island)
                    .Select(tree => TreeSpecies.WoodFor(tree.AssetName)!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
            }
        }

        /// <summary>
        /// Seeded components stay null and ordering stays AfterPlayer, for the same
        /// reasons Haven's trees do: the client states its own component needs over
        /// SEND_COMPONENT_INTEREST, and nothing ordered before the player should be
        /// something nobody stands on.
        /// </summary>
        [Fact]
        public void Trees_are_unseeded_and_never_delay_the_player()
        {
            Assert.All(AllTrees(), tree =>
            {
                // WorldEntity normalises a null seed list to an empty one, so
                // "unseeded" is asserted as "carries no component seeds".
                Assert.Empty(tree.SeedComponents);
                Assert.Equal(SpawnOrder.AfterPlayer, tree.Order);
            });
        }

        /// <summary>
        /// Position is the island's own authored local seat lifted by the island's
        /// own definition - never a world coordinate carried in the file, which is
        /// how an island that moves leaves its trees behind.
        /// </summary>
        [Fact]
        public void Positions_are_the_island_local_seats_lifted_by_the_island()
        {
            ReleaseTreeIsland seats = ReleaseTreeCatalog.All[0];
            ReleaseIslandRecord island = ReleaseWorldCatalog.All
                .Single(x => x.Survey.WorkshopId == seats.WorkshopId);

            List<WorldEntity> trees = ReleaseWorldTrees.For(island).ToList();
            for (int i = 0; i < trees.Count; i++)
            {
                (double x, double y, double z) = seats.Points[i];
                Assert.Equal(island.Definition.LocalToGlobal(x, y, z), trees[i].Position);
                Assert.Equal(ReleaseWorldTrees.KeyFor(seats.WorkshopId, i), trees[i].Key);
            }
        }

        /// <summary>
        /// END TO END THROUGH THE REAL REGISTRY. The unit tests above prove the
        /// factory; this proves the wiring, because a factory nobody calls seeds
        /// nothing. It also pins the two properties that make the change safe to
        /// carry alongside other work:
        ///
        ///   * with no districts enabled - the default - not one tree is registered,
        ///     so existing sessions are untouched;
        ///   * with districts enabled the trees appear scoped by that same dial,
        ///     alongside the deposits and databanks rather than instead of them.
        ///
        /// B3 is used because it is the wooded tier-1 cell the concurrent tier-one
        /// rollout cares about, so a regression shows up where it would be felt.
        /// </summary>
        [Fact]
        public void Default_registry_grows_trees_only_where_districts_are_enabled()
        {
            WorldEntityRegistry off = WorldEntities.Default(new EntityIdAllocator());
            Assert.DoesNotContain(off.Registrations,
                entity => entity.Key.StartsWith(ReleaseWorldTrees.KeyPrefix, StringComparison.Ordinal));

            int expected = ReleaseWorldRolloutPolicy.Select("B3")
                .Sum(island => ReleaseWorldTrees.For(island).Count());
            Assert.True(expected > 0, "B3 must contain wooded islands for this test to mean anything");

            WorldEntityRegistry on = WorldEntities.Default(
                new EntityIdAllocator(), releaseWorldDistricts: "B3");
            List<WorldEntity> trees = on.Registrations
                .Where(entity => entity.Key.StartsWith(ReleaseWorldTrees.KeyPrefix, StringComparison.Ordinal))
                .ToList();

            Assert.Equal(expected, trees.Count);
            // Deposits and databanks still register beside them, untouched.
            Assert.Contains(on.Registrations, entity => entity.Key.StartsWith("deposit-release-", StringComparison.Ordinal));
            Assert.Contains(on.Registrations, entity => entity.Key.StartsWith("databank-release-", StringComparison.Ordinal));
        }

        /// <summary>
        /// The new keys must not collide with the Haven trees (tree-haven, tree-0..N)
        /// that share the registry, nor with any other resource family's prefix.
        /// </summary>
        [Fact]
        public void Keys_never_collide_with_havens_trees_or_other_resources()
        {
            Assert.All(AllTrees(), tree =>
            {
                Assert.StartsWith(ReleaseWorldTrees.KeyPrefix, tree.Key);
                Assert.NotEqual(WorldEntities.HavenTreeKey, tree.Key);
                Assert.False(MetalDeposits.IsDepositKey(tree.Key));
                Assert.False(FuelPods.IsPodKey(tree.Key));
            });
        }
    }
}
