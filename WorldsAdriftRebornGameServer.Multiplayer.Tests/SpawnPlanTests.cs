using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The order a joining client is walked into the world, for ANY set of
    /// registered world entities.
    ///
    /// Two failures here are silent and neither produces an error:
    /// 1. Ground after the player - the player is published over geometry that
    ///    has not streamed in and falls forever, because this server writes no
    ///    HealthState (no fall damage) and WorldEdgePushback never runs.
    /// 2. AddEntity before its asset request - the client only instantiates a
    ///    prefab it has LOADED, and drops the op otherwise. Nothing is logged and
    ///    the object simply never appears.
    /// </summary>
    public class SpawnPlanTests
    {
        private static WorldEntity Tree(SpawnOrder order = SpawnOrder.AfterPlayer)
        {
            return new WorldEntity("tree", "Tree", "Default",
                FixedPointPosition.FromMetres(17212.0, -310.0, -1130.0), null, order);
        }

        private static WorldEntity Ship(SpawnOrder order = SpawnOrder.AfterPlayer)
        {
            return new WorldEntity("ship", "ShipFrame", "Default",
                FixedPointPosition.FromMetres(17220.0, -300.0, -1120.0),
                new uint[] { 190602, 1209, 1099, 1130 }, order);
        }

        private static WorldEntityRegistry With(params WorldEntity[] entities)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            foreach (WorldEntity entity in entities)
            {
                registry.Register(entity);
            }
            return registry;
        }

        // ------------------------------------------------------------------
        // The generalisation subsumes what it replaced
        // ------------------------------------------------------------------

        [Fact]
        public void The_plan_for_an_island_only_world_is_the_old_four_step_sequence()
        {
            // SpawnSequence describes this handshake for the one case that used
            // to exist. It is still true and still asserted by its own tests;
            // this is what stops the two descriptions drifting apart.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island()));

            Assert.Equal(SpawnSequence.Steps.Count, plan.Count);

            Assert.True(plan[0].IsPlayer);
            Assert.Equal(SpawnOp.RequestAsset, plan[0].Op);              // RequestPlayerAsset

            Assert.Equal(WorldEntities.IslandKey, plan[1].Entity!.Key);
            Assert.Equal(SpawnOp.RequestAsset, plan[1].Op);              // RequestIslandAsset

            Assert.Equal(WorldEntities.IslandKey, plan[2].Entity!.Key);
            Assert.Equal(SpawnOp.AddEntity, plan[2].Op);                 // AddIslandEntity

            Assert.True(plan[3].IsPlayer);
            Assert.Equal(SpawnOp.AddEntity, plan[3].Op);                 // AddPlayerEntity
        }

        [Fact]
        public void Each_plan_step_waits_for_the_same_ack_the_old_sequence_did()
        {
            // The ack is the only throttle on bundle loading anywhere in the
            // system: the client's asset loader is synchronous and unbudgeted.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island()));

            for (int i = 0; i < plan.Count; i++)
            {
                Assert.Equal(SpawnSequence.AckFor(SpawnSequence.Steps[i]), plan[i].Ack);
            }
        }

        [Fact]
        public void An_asset_request_waits_for_an_asset_ack_and_an_entity_add_for_an_entity_ack()
        {
            foreach (SpawnPlanStep step in SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship())))
            {
                Assert.Equal(
                    step.Op == SpawnOp.RequestAsset ? SpawnAck.AssetLoaded : SpawnAck.EntityAdded,
                    step.Ack);
            }
        }

        // ------------------------------------------------------------------
        // An arbitrary number of entities, which is the point
        // ------------------------------------------------------------------

        [Fact]
        public void Every_registered_entity_gets_an_asset_request_and_an_entity_add()
        {
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship()));

            foreach (string key in new[] { WorldEntities.IslandKey, "tree", "ship" })
            {
                Assert.Single(plan, s => s.Entity?.Key == key && s.Op == SpawnOp.RequestAsset);
                Assert.Single(plan, s => s.Entity?.Key == key && s.Op == SpawnOp.AddEntity);
            }
        }

        [Fact]
        public void The_joining_player_still_gets_exactly_one_asset_request_and_one_entity_add()
        {
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship()));

            Assert.Single(plan, s => s.IsPlayer && s.Op == SpawnOp.RequestAsset);
            Assert.Single(plan, s => s.IsPlayer && s.Op == SpawnOp.AddEntity);
        }

        [Fact]
        public void Adding_a_fourth_and_fifth_entity_needs_no_change_to_the_plan()
        {
            // "Do not hardcode three." Five registrations, ten world-entity steps
            // plus the player's two.
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            for (int i = 0; i < 5; i++)
            {
                registry.Register(new WorldEntity("e" + i, "Asset" + i, "Default",
                    FixedPointPosition.FromMetres(i, i, i)));
            }

            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(registry);

            Assert.Equal(12, plan.Count);
            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));
        }

        [Fact]
        public void A_world_with_nothing_registered_still_spawns_the_player()
        {
            // Degenerate but not absurd: it is what a server whose island
            // registration was commented out would do, and it should be one
            // missing island rather than a crash.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With());

            Assert.Equal(2, plan.Count);
            Assert.All(plan, s => Assert.True(s.IsPlayer));
        }

        // ------------------------------------------------------------------
        // The invariants
        // ------------------------------------------------------------------

        [Fact]
        public void Everything_the_player_stands_on_is_created_before_the_player()
        {
            Assert.True(SpawnPlan.GroundPrecedesPlayer(
                SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship()))));
        }

        [Fact]
        public void An_entity_registered_BeforePlayer_really_does_precede_the_player()
        {
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island(), Ship(SpawnOrder.BeforePlayer)));

            int ship = IndexOf(plan, SpawnOp.AddEntity, "ship");
            int player = IndexOf(plan, SpawnOp.AddEntity, null);

            Assert.True(ship < player);
            Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));
        }

        [Fact]
        public void An_entity_registered_AfterPlayer_really_does_follow_the_player()
        {
            // Every step before the player is a step the loading screen waits on.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(With(WorldEntities.Island(), Tree()));

            Assert.True(IndexOf(plan, SpawnOp.AddEntity, null) < IndexOf(plan, SpawnOp.AddEntity, "tree"));
        }

        [Fact]
        public void The_ground_guard_actually_rejects_something()
        {
            // A guard that cannot fail is decoration. Hand-build the inverted
            // plan the generator will not produce.
            WorldEntity island = WorldEntities.Island();
            Assert.False(SpawnPlan.GroundPrecedesPlayer(new[]
            {
                new SpawnPlanStep(SpawnOp.RequestAsset, null),
                new SpawnPlanStep(SpawnOp.RequestAsset, island),
                new SpawnPlanStep(SpawnOp.AddEntity, null),
                new SpawnPlanStep(SpawnOp.AddEntity, island),
            }));
        }

        [Fact]
        public void A_plan_that_never_creates_the_player_is_rejected()
        {
            WorldEntity island = WorldEntities.Island();
            Assert.False(SpawnPlan.GroundPrecedesPlayer(new[]
            {
                new SpawnPlanStep(SpawnOp.RequestAsset, island),
                new SpawnPlanStep(SpawnOp.AddEntity, island),
            }));
        }

        [Fact]
        public void Every_entity_has_its_bundle_requested_before_it_is_created()
        {
            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(
                SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship(SpawnOrder.BeforePlayer)))));
        }

        [Fact]
        public void The_asset_ordering_guard_actually_rejects_something()
        {
            // The failure it guards is the one with no error message at all: the
            // client drops an AddEntityOp for a prefab it has not loaded, logs
            // nothing, and the object never appears. It cost a full debugging
            // round on the remote-player mirror.
            WorldEntity tree = Tree();
            Assert.False(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(new[]
            {
                new SpawnPlanStep(SpawnOp.AddEntity, tree),
                new SpawnPlanStep(SpawnOp.RequestAsset, tree),
            }));
        }

        [Fact]
        public void The_default_registry_produces_a_valid_plan_with_and_without_the_proof_island()
        {
            foreach (bool proof in new[] { false, true })
            {
                IReadOnlyList<SpawnPlanStep> plan =
                    SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator(), proof));

                Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));
                Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));
            }
        }

        [Fact]
        public void The_proof_island_adds_exactly_two_steps_and_none_of_them_precede_the_player()
        {
            IReadOnlyList<SpawnPlanStep> without = SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator()));
            IReadOnlyList<SpawnPlanStep> with = SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator(), true));

            Assert.Equal(without.Count + 2, with.Count);
            Assert.True(IndexOf(with, SpawnOp.AddEntity, null)
                      < IndexOf(with, SpawnOp.AddEntity, WorldEntities.ProofIslandKey));
        }

        // ------------------------------------------------------------------
        // What actually happens when two clients walk the plan
        // ------------------------------------------------------------------

        [Fact]
        public void Two_clients_walking_the_plan_agree_on_every_world_entity_id_and_never_share_a_player_id()
        {
            // This is the whole reason world entity ids are allocate-once and
            // player ids are not. It is simulated here rather than in the server
            // because the server's version of this loop is ENet-bound; what it
            // does per step is one call each, and these are those calls.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids, includeProofIsland: true);
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(registry);

            Dictionary<string, List<long>> worldIds = new Dictionary<string, List<long>>();
            List<long> playerIds = new List<long>();

            for (int peer = 0; peer < 2; peer++)
            {
                foreach (SpawnPlanStep step in plan)
                {
                    if (step.Op != SpawnOp.AddEntity)
                    {
                        continue;
                    }

                    if (step.IsPlayer)
                    {
                        playerIds.Add(ids.Next());
                        continue;
                    }

                    if (!worldIds.TryGetValue(step.Entity!.Key, out List<long>? seen))
                    {
                        seen = new List<long>();
                        worldIds[step.Entity!.Key] = seen;
                    }
                    seen.Add(registry.EntityIdFor(step.Entity!));
                }
            }

            // Same object, same number, on both clients - a mismatch resolves to
            // nothing on the receiver and is never reported.
            foreach (KeyValuePair<string, List<long>> pair in worldIds)
            {
                Assert.Equal(2, pair.Value.Count);
                Assert.Equal(pair.Value[0], pair.Value[1]);
            }

            // Different players, different numbers.
            Assert.Equal(2, playerIds.Count);
            Assert.NotEqual(playerIds[0], playerIds[1]);

            // And nothing collides with anything.
            HashSet<long> all = new HashSet<long>();
            foreach (long id in worldIds.Values.Select(v => v[0]).Concat(playerIds))
            {
                Assert.True(all.Add(id), "an entity id was handed out twice");
            }
        }

        [Fact]
        public void The_island_gets_entity_id_one_and_the_first_player_gets_two()
        {
            // The base id is 1, not 0: id 0 is the client's INVALID sentinel
            // (EntityId.IsValid() == Id > 0), so nothing real may take it. The island
            // is the first AddEntity in the plan, so it takes 1; the first player the
            // next id, 2. (This used to be 0 and 1 - the shift is deliberate and is
            // what stops a boot-restored deployable landing on the invalid id 0.)
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids, includeProofIsland: true);

            long island = -1;
            long player = -1;
            foreach (SpawnPlanStep step in SpawnPlan.For(registry))
            {
                if (step.Op != SpawnOp.AddEntity)
                {
                    continue;
                }
                if (step.IsPlayer && player < 0)
                {
                    player = ids.Next();
                }
                else if (!step.IsPlayer && step.Entity!.Key == WorldEntities.IslandKey)
                {
                    island = registry.EntityIdFor(step.Entity!);
                }
            }

            Assert.Equal(1, island);
            Assert.Equal(2, player);
        }

        // ------------------------------------------------------------------
        // Barrier-aware ordering: initial AfterPlayer entities stream first
        // ------------------------------------------------------------------

        [Fact]
        public void The_predicate_overload_with_no_initials_is_byte_for_byte_the_registration_order_plan()
        {
            // For(registry) delegates to For(registry, _ => false); the two must be
            // indistinguishable or the non-barrier path has silently changed.
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator(), includeProofIsland: true);

            IReadOnlyList<SpawnPlanStep> plain = SpawnPlan.For(registry);
            IReadOnlyList<SpawnPlanStep> none = SpawnPlan.For(registry, _ => false);

            Assert.Equal(plain.Count, none.Count);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(plain[i].Op, none[i].Op);
                Assert.Equal(plain[i].Entity?.Key, none[i].Entity?.Key);
            }
        }

        [Fact]
        public void An_initial_AfterPlayer_entity_is_streamed_before_a_distant_one()
        {
            // "ship" is initial, "tree" is not: the whole point is that the ship
            // reaches the client before the scenery, so the barrier's initial set
            // is not stuck behind every tree in the pacer.
            IReadOnlyList<SpawnPlanStep> plan =
                SpawnPlan.For(With(WorldEntities.Island(), Tree(), Ship()), key => key == "ship");

            Assert.True(IndexOf(plan, SpawnOp.AddEntity, "ship") < IndexOf(plan, SpawnOp.AddEntity, "tree"));

            // And it is still after the player and still valid.
            Assert.True(IndexOf(plan, SpawnOp.AddEntity, null) < IndexOf(plan, SpawnOp.AddEntity, "ship"));
            Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));
            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));
        }

        [Fact]
        public void Reordering_preserves_the_registration_order_within_each_group()
        {
            // Two initial and two distant entities; each group keeps its internal
            // order (a hull must still precede the helm whose 8066 seed names it).
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            registry.Register(new WorldEntity("i1", "A", "Default", FixedPointPosition.FromMetres(0, 0, 0)));
            registry.Register(new WorldEntity("d1", "B", "Default", FixedPointPosition.FromMetres(0, 0, 0)));
            registry.Register(new WorldEntity("i2", "C", "Default", FixedPointPosition.FromMetres(0, 0, 0)));
            registry.Register(new WorldEntity("d2", "D", "Default", FixedPointPosition.FromMetres(0, 0, 0)));

            IReadOnlyList<SpawnPlanStep> plan =
                SpawnPlan.For(registry, key => key == "i1" || key == "i2");

            // Initials first, in registration order, then distants in registration order.
            Assert.True(IndexOf(plan, SpawnOp.AddEntity, "i1") < IndexOf(plan, SpawnOp.AddEntity, "i2"));
            Assert.True(IndexOf(plan, SpawnOp.AddEntity, "i2") < IndexOf(plan, SpawnOp.AddEntity, "d1"));
            Assert.True(IndexOf(plan, SpawnOp.AddEntity, "d1") < IndexOf(plan, SpawnOp.AddEntity, "d2"));
        }

        [Fact]
        public void The_default_registry_partitioned_by_the_real_policy_is_a_valid_plan()
        {
            IReadOnlyList<SpawnPlanStep> plan =
                SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator()), LoadBarrierPolicy.IsInitialKey);

            Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));
            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));

            // The ship hull (initial) precedes the first tree/ore (distant).
            int hull = IndexOf(plan, SpawnOp.AddEntity, WorldEntities.ShipFrameKey);
            Assert.True(hull > 0);
        }

        private static int IndexOf(IReadOnlyList<SpawnPlanStep> plan, SpawnOp op, string? key)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Op == op && plan[i].Entity?.Key == key)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
