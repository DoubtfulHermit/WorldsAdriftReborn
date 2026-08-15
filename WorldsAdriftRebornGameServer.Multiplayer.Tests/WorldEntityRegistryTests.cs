using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The seam that lets this server put something in the world that is not a
    /// player or the island.
    ///
    /// Everything asserted here used to be a hardcoded answer: "which entity is
    /// this" was one boolean (is the id the island's?), "what asset is it" was a
    /// string constant, and "where does it go" was a two-branch ternary. Each of
    /// those has exactly one more caller queued behind it - a tree and a ship
    /// frame - so each is now a lookup, and these are the tests that say the
    /// lookup gives the same answers the constants did.
    /// </summary>
    public class WorldEntityRegistryTests
    {
        private static WorldEntity Tree(SpawnOrder order = SpawnOrder.AfterPlayer)
        {
            // A stand-in for a downstream caller's registration. Deliberately not
            // the real tree: nothing here should have to be revisited when the
            // tree's real seeds are decided.
            return new WorldEntity("tree", "Tree", "Default",
                FixedPointPosition.FromMetres(17212.0, -310.0, -1130.0),
                new uint[] { 190602, 1035, 1036 }, order);
        }

        // ------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------

        [Fact]
        public void A_world_entity_needs_a_key_and_an_asset_name_because_the_asset_name_goes_on_the_wire()
        {
            Assert.Throws<ArgumentException>(() =>
                new WorldEntity("", "Tree", "Default", default));
            Assert.Throws<ArgumentException>(() =>
                new WorldEntity("tree", "  ", "Default", default));
        }

        [Fact]
        public void A_world_entity_seeds_no_components_unless_it_asks_to()
        {
            // Empty is the right default and is what the island uses: the client
            // checks the entity out and asks for what it wants. A pushed batch
            // goes out all-or-nothing, so every id listed is a new way to fail.
            Assert.Empty(new WorldEntity("x", "X", "notNeeded?", default).SeedComponents);
        }

        [Fact]
        public void A_world_entity_spawns_after_the_player_unless_it_asks_not_to()
        {
            // Every step before the player is a step the loading screen waits on,
            // so "before" has to be earned by being the ground under their feet.
            Assert.Equal(SpawnOrder.AfterPlayer, new WorldEntity("x", "X", "notNeeded?", default).Order);
        }

        [Fact]
        public void Two_registrations_cannot_share_a_key_because_they_would_share_an_entity_id()
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            registry.Register(Tree());

            Assert.Throws<ArgumentException>(() => registry.Register(Tree()));
        }

        [Fact]
        public void Retiring_an_entity_removes_it_from_future_plans_without_reusing_its_id()
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            WorldEntity tree = registry.Register(Tree());
            long id = registry.EntityIdFor(tree);

            Assert.True(registry.Unregister(id));
            Assert.Null(registry.ByEntityId(id));
            Assert.Null(registry.ByKey(tree.Key));
            Assert.DoesNotContain(tree, registry.Registrations);
            Assert.False(registry.Unregister(id));
        }

        [Fact]
        public void Relocating_a_surviving_entity_updates_its_recheckout_pose_and_keeps_its_id()
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            WorldEntity tree = registry.Register(Tree());
            long id = registry.EntityIdFor(tree);
            FixedPointPosition moved = FixedPointPosition.FromMetres(1, 2, 3);

            Assert.True(registry.Relocate(id, moved,
                WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity));
            Assert.Equal(id, registry.BoundEntityIdFor(tree.Key));
            Assert.Equal(moved, registry.TransformSeedFor(id));
        }

        [Fact]
        public void A_frozen_spawn_plan_cannot_restore_a_stale_registration_after_relocation()
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            WorldEntity frozenPlanEntity = registry.Register(Tree());
            long id = registry.EntityIdFor(frozenPlanEntity);
            FixedPointPosition firstMove = FixedPointPosition.FromMetres(1, 2, 3);
            FixedPointPosition secondMove = FixedPointPosition.FromMetres(4, 5, 6);

            Assert.True(registry.Relocate(id, firstMove,
                WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity));

            // A later peer executes the boot-time plan, which still holds the
            // pre-relocation object. Binding it must keep the canonical moved
            // registration rather than poisoning the entity-id lookup.
            Assert.Equal(id, registry.EntityIdFor(frozenPlanEntity));
            Assert.Equal(firstMove, registry.TransformSeedFor(id));
            Assert.True(registry.Relocate(id, secondMove,
                WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity));
            Assert.Equal(secondMove, registry.TransformSeedFor(id));
        }

        [Fact]
        public void An_unregistered_entity_cannot_be_given_an_id()
        {
            // Otherwise its id would exist but nothing could look it up by id,
            // which is precisely the state that renders an entity and leaves it
            // inert.
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());

            Assert.Throws<ArgumentException>(() => registry.EntityIdFor(Tree()));
        }

        // ------------------------------------------------------------------
        // Entity ids: the rule that used to apply only to the island
        // ------------------------------------------------------------------

        [Fact]
        public void Every_client_is_told_the_same_entity_id_for_the_same_world_entity()
        {
            // The island's rule, generalised. A remote client resolves a
            // cross-client reference BY ID against its own world; two clients
            // holding different ids for one object resolve to nothing, and
            // nothing is logged.
            WorldEntityRegistry registry = new WorldEntityRegistry(new EntityIdAllocator());
            WorldEntity tree = registry.Register(Tree());

            long forFirstClient = registry.EntityIdFor(tree);
            long forSecondClient = registry.EntityIdFor(tree);

            Assert.Equal(forFirstClient, forSecondClient);
        }

        [Fact]
        public void World_entity_ids_come_off_the_same_counter_as_player_ids_so_they_cannot_collide()
        {
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids, includeProofIsland: true);

            long island = registry.EntityIdFor(registry.ByKey(WorldEntities.IslandKey)!);
            long second = registry.EntityIdFor(registry.ByKey(WorldEntities.ProofIslandKey)!);

            HashSet<long> seen = new HashSet<long> { island, second };
            for (int i = 0; i < 100; i++)
            {
                Assert.True(seen.Add(ids.Next()), "a world entity id collided with a player entity id");
            }
        }

        [Fact]
        public void Asking_which_entity_an_id_belongs_to_never_allocates_an_id()
        {
            // Allocation on read is what makes ids shared; allocation on a
            // QUESTION would make the answer depend on who asked first. Id 0 is the
            // INVALID sentinel on the client (EntityId.IsValid() == Id > 0) and is
            // never handed out, so it must always read back as "not a world entity".
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            WorldEntity island = registry.ByKey(WorldEntities.IslandKey)!;

            Assert.False(registry.IsBound(island));
            Assert.Null(registry.ByEntityId(0));
            Assert.Equal(SeededEntityKind.Player, registry.KindOf(0));
            Assert.False(registry.IsBound(island));

            // The island is the first thing allocated, so it takes the base id 1 -
            // never 0, which no valid entity may occupy.
            Assert.Equal(EntityIdAllocator.FirstEntityId, registry.EntityIdFor(island));
            Assert.True(registry.IsBound(island));
        }

        // ------------------------------------------------------------------
        // What the component serializer asks the registry
        // ------------------------------------------------------------------

        [Fact]
        public void A_world_entity_is_seeded_with_its_own_position_not_a_shared_default()
        {
            // The bug the whole spawn-policy module exists to kill, one step
            // further out: it used to be that the island and the player shared a
            // transform seed. Now a third entity must not share the island's.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);
            WorldEntity island = registry.Register(WorldEntities.Island());
            WorldEntity tree = registry.Register(Tree());

            long islandId = registry.EntityIdFor(island);
            long treeId = registry.EntityIdFor(tree);
            long playerId = ids.Next();

            Assert.Equal(SpawnPolicy.IslandPosition, registry.TransformSeedFor(islandId));
            Assert.Equal(tree.Position, registry.TransformSeedFor(treeId));
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, registry.TransformSeedFor(playerId));

            Assert.NotEqual(registry.TransformSeedFor(islandId), registry.TransformSeedFor(treeId));
            Assert.NotEqual(registry.TransformSeedFor(treeId), registry.TransformSeedFor(playerId));
        }

        [Fact]
        public void An_unregistered_id_is_a_player_which_is_what_a_mirrored_remote_avatar_is()
        {
            // Remote rigs are seeded with 190602 too. Handing one a world
            // entity's position would park somebody else's body inside the
            // terrain until the first relayed update.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            registry.EntityIdFor(registry.ByKey(WorldEntities.IslandKey)!);

            Assert.Contains(190602u, MirrorSendPolicy.RemoteSeedComponents);
            Assert.Equal(SeededEntityKind.Player, registry.KindOf(7));
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, registry.TransformSeedFor(7));
        }

        [Fact]
        public void The_registry_answer_agrees_with_the_island_only_answer_it_replaced()
        {
            // The two-argument overload is the degenerate case of the registry
            // one. If they ever disagree, one of the two callers is wrong.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            long islandId = registry.EntityIdFor(registry.ByKey(WorldEntities.IslandKey)!);
            long playerId = ids.Next();

            Assert.Equal(SpawnPolicy.KindOf(islandId, islandId), registry.KindOf(islandId));
            Assert.Equal(SpawnPolicy.KindOf(playerId, islandId), registry.KindOf(playerId));
            Assert.Equal(SpawnPolicy.TransformSeedFor(islandId, islandId), registry.TransformSeedFor(islandId));
            Assert.Equal(SpawnPolicy.TransformSeedFor(playerId, islandId), registry.TransformSeedFor(playerId));
        }

        [Fact]
        public void A_third_entity_is_reported_as_World_not_as_the_island_and_not_as_a_player()
        {
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);
            WorldEntity tree = registry.Register(Tree());

            Assert.Equal(SeededEntityKind.World, registry.KindOf(registry.EntityIdFor(tree)));
        }

        // ------------------------------------------------------------------
        // The default registry the server runs with
        // ------------------------------------------------------------------

        [Fact]
        public void The_island_is_registered_and_is_the_only_thing_that_precedes_the_player()
        {
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator(), includeProofIsland: true);

            Assert.Equal(new[] { WorldEntities.IslandKey },
                registry.InOrder(SpawnOrder.BeforePlayer).Select(e => e.Key).ToArray());
        }

        [Fact]
        public void The_island_registration_carries_the_same_asset_and_position_the_constants_did()
        {
            // Those constants were read at three sites that MUST agree - the
            // asset request, the AddEntityOp and 1041 IslandState's prefab name.
            // The registration is now the single source; this pins that moving
            // them did not change them.
            WorldEntity island = WorldEntities.Island();

            Assert.Equal("1431299145@Island", island.AssetName);
            Assert.Equal(SpawnPolicy.IslandAssetName, island.AssetName);
            Assert.Equal(SpawnPolicy.IslandPosition, island.Position);
            Assert.Equal("notNeeded?", island.AssetContext);
            Assert.Empty(island.SeedComponents);
        }

        [Fact]
        public void The_proof_island_is_off_unless_asked_for()
        {
            // It has never been in front of a running client. Enabling it by
            // default would change what every player sees on the strength of a
            // unit test.
            WorldEntityRegistry off = WorldEntities.Default(new EntityIdAllocator());
            Assert.Null(off.ByKey(WorldEntities.ProofIslandKey));

            WorldEntityRegistry on = WorldEntities.Default(new EntityIdAllocator(), includeProofIsland: true);
            Assert.NotNull(on.ByKey(WorldEntities.ProofIslandKey));
        }

        [Fact]
        public void The_production_second_island_is_off_unless_asked_for_and_is_classified_as_terrain()
        {
            string key = global::WorldsAdriftRebornGameServer.Multiplayer.Islands
                .IslandCatalog.TradesChallenge.WorldEntityKey;
            WorldEntityRegistry off = WorldEntities.Default(new EntityIdAllocator());
            Assert.Null(off.ByKey(key));

            WorldEntityRegistry on = WorldEntities.Default(
                new EntityIdAllocator(), includeProductionSecondIsland: true);
            WorldEntity island = on.ByKey(key)!;
            long entityId = on.EntityIdFor(island);

            Assert.Equal("1206286558@Island", island.AssetName);
            Assert.Equal(SeededEntityKind.Island, on.KindOf(entityId));
            Assert.Equal(SpawnOrder.AfterPlayer, island.Order);
        }

        [Fact]
        public void The_proof_island_is_Haven_instance_six_from_the_studios_own_world_map()
        {
            // Entry 6 of the twelve 1431299145.json placements in
            // docs/research/world-data/wamap-islands.json: (17003.416,
            // -212.325027, 1826.00183) m. A real position, not an invented
            // offset from instance #5.
            WorldEntity second = WorldEntities.ProofIsland();

            Assert.Equal(new FixedPointPosition(69645991, -869683, 7479303), second.Position);
            Assert.Equal(SpawnPolicy.IslandAssetName, second.AssetName);
        }

        [Fact]
        public void The_proof_island_needs_no_component_branch_that_does_not_already_exist()
        {
            // The point of choosing a second island over a tree: it seeds
            // nothing, so it exercises the seam and only the seam. If it fails in
            // front of a client, the seam is at fault - not a missing seed.
            Assert.Empty(WorldEntities.ProofIsland().SeedComponents);
        }

        [Fact]
        public void The_proof_island_is_somewhere_else_but_still_the_same_asset()
        {
            // Same bundle, different world position - which is exactly what Haven
            // is: ONE asset placed at TWELVE world positions. If the position
            // were shared the whole exercise would prove nothing.
            Assert.Equal(WorldEntities.Island().AssetName, WorldEntities.ProofIsland().AssetName);
            Assert.NotEqual(WorldEntities.Island().Position, WorldEntities.ProofIsland().Position);
            Assert.NotEqual(WorldEntities.Island().Key, WorldEntities.ProofIsland().Key);
        }

        [Fact]
        public void The_proof_island_does_not_land_on_top_of_the_player()
        {
            // A second island dropped through the spawn point would be a very
            // loud way to prove a very quiet mechanism.
            double dz = WorldEntities.ProofIsland().Position.MetresZ - SpawnPolicy.PlayerSpawnPosition.MetresZ;
            Assert.True(Math.Abs(dz) > 1000, "the second island is within 1 km of the player spawn");
        }
    }
}
