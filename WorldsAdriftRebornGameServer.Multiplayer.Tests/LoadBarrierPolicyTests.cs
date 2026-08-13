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

            // THE WHOLE STATIC WORLD is initial now: this client instantiates
            // entities synchronously on the main thread, so anything streaming in
            // AFTER the screen lifts is a visible hitch on any OS - the observed
            // "stutters when the game starts rendering". A small island's statics
            // cost a few extra seconds of loading screen instead.
            Assert.True(LoadBarrierPolicy.IsInitialKey("tree-7"));
            Assert.True(LoadBarrierPolicy.IsInitialKey("metal-node-3"));
            Assert.True(LoadBarrierPolicy.IsInitialKey("deposit-4"));
            Assert.True(LoadBarrierPolicy.IsInitialKey("atlas-shard-deposit-4"));
            Assert.True(LoadBarrierPolicy.IsInitialKey("fuel-pod-1"));
            Assert.True(LoadBarrierPolicy.IsInitialKey("databank-0"));

            // Truly non-world keys still stream late and never gate the screen.
            Assert.False(LoadBarrierPolicy.IsInitialKey(WorldEntities.ProofIslandKey));
            Assert.False(LoadBarrierPolicy.IsInitialKey("shipwreck"));
            Assert.False(LoadBarrierPolicy.IsInitialKey(null));
        }

        [Fact]
        public void A_built_ships_hull_and_every_deck_panel_are_in_the_initial_set()
        {
            // The heaviest join work is the built hull's mesh and the client's
            // per-panel MakeDeck collider generation; both must load BEHIND the
            // loading screen, so every entity of a built ship is initial.
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldsAdriftRebornGameServer.Multiplayer.Ship.BuiltShipPlacement.HullKey(0)));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldsAdriftRebornGameServer.Multiplayer.Ship.BuiltShipPlacement.HullKey(7)));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldsAdriftRebornGameServer.Multiplayer.Ship.BuiltShipPlacement.DeckKey(7)));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldsAdriftRebornGameServer.Multiplayer.Ship.BuiltShipPlacement.DeckKey(7, 0)));
            Assert.True(LoadBarrierPolicy.IsInitialKey(WorldsAdriftRebornGameServer.Multiplayer.Ship.BuiltShipPlacement.DeckKey(7, 11)));

            // A key that merely mentions the word is not a built-ship entity key.
            Assert.False(LoadBarrierPolicy.IsInitialKey("shipwreck"));
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

            // DESIGN CHANGE: the static world moved INTO the initial set (synchronous
            // client instantiation makes anything streaming after activation a visible
            // hitch), so the initial set is now the bulk of the registry and the
            // distant set holds only the leftovers (proof island, diagnostics).
            Assert.True(initial.Count > distant.Count,
                "the static world loads behind the screen now - initial should dominate");
        }

        [Fact]
        public void The_initial_set_grows_with_the_static_world_so_it_all_loads_behind_the_screen()
        {
            // DESIGN CHANGE: the static world is IN the initial set now, so a
            // bigger world means a longer loading screen - never more in-view
            // stutter. The initial count must therefore grow with the scenery.
            WorldEntityRegistry small = WorldEntities.Default(new EntityIdAllocator(),
                treeCountEnv: "3", oreCountEnv: "3");
            WorldEntityRegistry big = WorldEntities.Default(new EntityIdAllocator(),
                treeCountEnv: "40", oreCountEnv: "40");

            Assert.True(
                LoadBarrierPolicy.InitialEntities(big).Count
                    > LoadBarrierPolicy.InitialEntities(small).Count);
        }
    }
}
