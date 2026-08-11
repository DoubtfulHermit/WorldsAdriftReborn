using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The env-to-config and initial/distant partition rules of the loading
    /// barrier. Every rule must fail SAFE: a bad env var can neither stop the
    /// server booting nor disable the timeout that stops a client being trapped on
    /// the loading screen.
    /// </summary>
    public class LoadBarrierPolicyTests
    {
        // ------------------------------------------------------------------
        // Enable flag - strictly opt-in
        // ------------------------------------------------------------------

        [Fact]
        public void The_barrier_is_off_unless_the_flag_is_exactly_one()
        {
            Assert.False(LoadBarrierPolicy.IsEnabled(null));
            Assert.False(LoadBarrierPolicy.IsEnabled(""));
            Assert.False(LoadBarrierPolicy.IsEnabled("0"));
            Assert.False(LoadBarrierPolicy.IsEnabled("true"));
            Assert.False(LoadBarrierPolicy.IsEnabled("2"));
            Assert.False(LoadBarrierPolicy.IsEnabled(" 1 "));
        }

        [Fact]
        public void The_barrier_is_on_only_for_the_exact_string_one()
        {
            Assert.True(LoadBarrierPolicy.IsEnabled("1"));
        }

        // ------------------------------------------------------------------
        // Timeout - clamped, and never disabled by a bad value
        // ------------------------------------------------------------------

        [Fact]
        public void An_unset_or_unparsable_timeout_is_the_default()
        {
            Assert.Equal(LoadBarrierPolicy.DefaultTimeoutMs, LoadBarrierPolicy.TimeoutFrom(null).TotalMilliseconds);
            Assert.Equal(LoadBarrierPolicy.DefaultTimeoutMs, LoadBarrierPolicy.TimeoutFrom("").TotalMilliseconds);
            Assert.Equal(LoadBarrierPolicy.DefaultTimeoutMs, LoadBarrierPolicy.TimeoutFrom("nonsense").TotalMilliseconds);
        }

        [Fact]
        public void A_zero_or_negative_timeout_never_disables_the_safety_net()
        {
            // 0 is the "disable pacing" convention elsewhere, but a barrier with no
            // timeout is an immortal loading screen - the one thing the timeout
            // exists to prevent - so it falls back to the default instead.
            Assert.Equal(LoadBarrierPolicy.DefaultTimeoutMs, LoadBarrierPolicy.TimeoutFrom("0").TotalMilliseconds);
            Assert.Equal(LoadBarrierPolicy.DefaultTimeoutMs, LoadBarrierPolicy.TimeoutFrom("-500").TotalMilliseconds);
        }

        [Fact]
        public void A_timeout_is_clamped_to_the_sane_band()
        {
            Assert.Equal(LoadBarrierPolicy.MinTimeoutMs, LoadBarrierPolicy.TimeoutFrom("50").TotalMilliseconds);
            Assert.Equal(LoadBarrierPolicy.MaxTimeoutMs, LoadBarrierPolicy.TimeoutFrom("9999999").TotalMilliseconds);
            Assert.Equal(8000, LoadBarrierPolicy.TimeoutFrom("8000").TotalMilliseconds);
        }

        // ------------------------------------------------------------------
        // The initial / distant partition
        // ------------------------------------------------------------------

        [Fact]
        public void The_ground_and_the_ship_are_initial_and_scenery_is_not()
        {
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldEntities.IslandKey));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldEntities.ShipFrameKey));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldEntities.HelmKey));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldEntities.DeckKey));

            // Trees, ore, and the diagnostic proof island are distant scenery: they
            // stream in but must not gate the loading screen.
            Assert.False(LoadBarrierPolicy.IsInitialKey("tree"));
            Assert.False(LoadBarrierPolicy.IsInitialKey("metal-node-3"));
            Assert.False(LoadBarrierPolicy.IsInitialKey(WorldEntities.ProofIslandKey));
            Assert.False(LoadBarrierPolicy.IsInitialKey(null));
        }

        [Fact]
        public void The_initial_and_distant_partition_covers_every_registration_once()
        {
            // The default registry has one island (BeforePlayer), a ship + parts,
            // and dozens of trees/ore. The two lists must reconstruct it exactly.
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator());

            IReadOnlyList<WorldEntity> initial = LoadBarrierPolicy.InitialEntities(registry);
            IReadOnlyList<WorldEntity> distant = LoadBarrierPolicy.DistantEntities(registry);

            Assert.Equal(registry.Registrations.Count, initial.Count + distant.Count);

            HashSet<string> initialKeys = new HashSet<string>(initial.Select(e => e.Key));
            HashSet<string> distantKeys = new HashSet<string>(distant.Select(e => e.Key));
            Assert.Empty(initialKeys.Intersect(distantKeys));

            // The island and the ship hull are load-bearing and must be initial.
            Assert.Contains(WorldEntities.IslandKey, initialKeys);
            Assert.Contains(WorldEntities.ShipFrameKey, initialKeys);

            // The distant set is the bulk of the registry - the scenery that used to
            // sit on the join critical path.
            Assert.True(distant.Count > initial.Count,
                "the whole point is that most entities are distant and no longer block activation");
        }

        [Fact]
        public void The_initial_set_is_bounded_and_does_not_grow_with_the_tree_and_ore_counts()
        {
            // A world with ten times the scenery still has the same small initial
            // set: island + ship + parts. This is why join cost stops being
            // proportional to total world size.
            WorldEntityRegistry small = WorldEntities.Default(new EntityIdAllocator(),
                treeCountEnv: "3", oreCountEnv: "3");
            WorldEntityRegistry big = WorldEntities.Default(new EntityIdAllocator(),
                treeCountEnv: "40", oreCountEnv: "40");

            Assert.Equal(
                LoadBarrierPolicy.InitialEntities(small).Count,
                LoadBarrierPolicy.InitialEntities(big).Count);
        }
    }
}
