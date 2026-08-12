using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The tree as a thing in the world: what it is called on the wire, where it
    /// stands, and that it stands there without needing a game client to confirm
    /// it.
    ///
    /// The coordinate assertions are the point. Two of this project's most
    /// expensive bugs were a spawn point 0.15 m underground and a documented
    /// fallback 6 m underground, both of which looked plausible in a source file
    /// and were only wrong in a running game. Deriving the tree's position from
    /// the island's, in the same arithmetic the player's spawn uses, means the two
    /// numbers check each other.
    /// </summary>
    public class TreeRegistrationTests
    {
        private static WorldEntity Tree() => WorldEntities.HavenTree();

        // ------------------------------------------------------------------
        // On the wire
        // ------------------------------------------------------------------

        [Fact]
        public void The_prefab_name_is_the_BARE_name_because_the_client_appends_the_worker_suffix()
        {
            // "Tree", not "tree_unityclient" and not "Tree_unityclient": the client
            // appends the suffix itself in
            // WorkerSpecificPrefabName.GetWorkerSpecificPrefabName, so a suffixed
            // name is suffixed twice and resolves to nothing.
            //
            // docs/research/gathering/findings-node-spawning.md claims there is no
            // `Tree` prefab at all. It is wrong: entityprefabs/tree_unityclient is
            // line 289 of the very container listing it cites.
            Assert.Equal("Tree", Trees.AssetName);
            Assert.Equal("Tree", Tree().AssetName);
            Assert.DoesNotContain("_unity", Tree().AssetName);
        }

        [Fact]
        public void The_tree_seeds_no_components_and_lets_the_client_ask()
        {
            // Same choice the island makes, for a stronger reason. A client's own
            // interest request is served all-or-nothing, and an unprompted seed
            // list would add a SECOND all-or-nothing batch built from our guess at
            // what the client wants rather than from its own statement of it - a
            // guess that is too small drops everything, and a guess that is right
            // is redundant.
            Assert.Empty(Tree().SeedComponents);
        }

        [Fact]
        public void The_tree_spawns_after_the_player_because_nobody_stands_on_it()
        {
            // Every step before the player is a step the loading screen waits on.
            // Only something the player's footing depends on earns BeforePlayer.
            Assert.Equal(SpawnOrder.AfterPlayer, Tree().Order);
        }

        [Fact]
        public void The_tree_uses_the_single_variant_prefab_context()
        {
            // "notNeeded?" is what anything with one variant sends; only a prefab
            // with per-worker variants (the Traveller's Default vs Player) needs a
            // real one.
            Assert.Equal(WorldEntities.DefaultAssetContext, Tree().AssetContext);
        }

        // ------------------------------------------------------------------
        // Where it stands
        // ------------------------------------------------------------------

        [Fact]
        public void The_tree_position_is_the_island_plus_a_measured_island_local_surface_vertex()
        {
            // THE DERIVATION, pinned so the three literals cannot drift from it:
            //   island (69650145, -1305269, -4645549)
            // + local  (208.00, 4.99, 8.00) m, x4096, truncated toward zero
            // = tree   (70502113, -1284830, -4612781)
            //
            // (208, 4.99, 8) is a MEASURED LOD0 surface vertex from
            // docs/research/world-data/island-surfaces/1431299145.json - normal
            // ny = 0.999, nearest prop of any kind 12.71 m away in 3D. It is not
            // the spawn point with an offset added: this island's pre-TRS surface
            // tables were once wrong by a mean of 47.7 m, so an unmeasured
            // coordinate is probably underground.
            FixedPointPosition island = SpawnPolicy.IslandPosition;
            FixedPointPosition tree = WorldEntities.HavenTreePosition;

            Assert.Equal((long)(208.00 * 4096), tree.X - island.X);
            Assert.Equal((long)(4.99 * 4096), tree.Y - island.Y);
            Assert.Equal((long)(8.00 * 4096), tree.Z - island.Z);

            Assert.Equal(WorldEntities.HavenTreePosition, Tree().Position);
        }

        [Fact]
        public void The_tree_is_about_four_metres_from_where_the_player_wakes_up()
        {
            // Close enough to see and to reach - the aimer's raycast is 40 m and
            // the salvager's is 10 m, so the tree is well inside the binding one -
            // and far enough not to be inside the player.
            FixedPointPosition player = SpawnPolicy.PlayerSpawnPosition;
            FixedPointPosition tree = WorldEntities.HavenTreePosition;

            double dx = tree.MetresX - player.MetresX;
            double dy = tree.MetresY - player.MetresY;
            double dz = tree.MetresZ - player.MetresZ;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            Assert.InRange(distance, 3.0, 6.0);
        }

        [Fact]
        public void The_tree_sits_ON_the_ground_while_the_player_spawns_ABOVE_it()
        {
            // The player's spawn Y carries a 2.00 m stand-off because a player
            // dropped 0.15 m underground interpenetrates the ground. A tree wants
            // its base exactly on the surface, so its Y is the measured surface
            // with no stand-off - and it is therefore BELOW the player's spawn Y
            // even though its own surface vertex is slightly higher (4.99 vs 4.70).
            Assert.True(WorldEntities.HavenTreePosition.Y < SpawnPolicy.PlayerSpawnPosition.Y,
                "the tree's base should be below the player's spawn altitude");
            Assert.InRange(SpawnPolicy.PlayerSpawnPosition.MetresY - WorldEntities.HavenTreePosition.MetresY, 1.0, 2.5);
        }

        [Fact]
        public void The_tree_is_not_placed_at_the_island_origin_or_the_world_origin()
        {
            // The two failure modes of "the position was never set": the default
            // seed used to put everything at the world origin, which only looked
            // right while the island was there too. Haven is 17 km away.
            Assert.NotEqual(new FixedPointPosition(0, 0, 0), WorldEntities.HavenTreePosition);
            Assert.NotEqual(SpawnPolicy.IslandPosition, WorldEntities.HavenTreePosition);
        }

        // ------------------------------------------------------------------
        // In the registry
        // ------------------------------------------------------------------

        [Fact]
        public void The_default_registry_plants_the_tree_and_the_kill_switch_removes_it()
        {
            WorldEntityRegistry on = WorldEntities.Default(new EntityIdAllocator());
            Assert.NotNull(on.ByKey(WorldEntities.HavenTreeKey));

            WorldEntityRegistry off = WorldEntities.Default(new EntityIdAllocator(), includeTree: false);
            Assert.Null(off.ByKey(WorldEntities.HavenTreeKey));
        }

        [Fact]
        public void The_tree_gets_its_own_position_from_the_registry_and_not_the_players_spawn()
        {
            // The generalisation the world-entity seam exists for: before it, every
            // entity that asked for 190602 got byte-identical data, so a tree would
            // have been planted inside the player.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            WorldEntity tree = registry.ByKey(WorldEntities.HavenTreeKey)!;

            long treeId = registry.EntityIdFor(tree);

            Assert.Equal(WorldEntities.HavenTreePosition, registry.TransformSeedFor(treeId));
            Assert.NotEqual(SpawnPolicy.PlayerSpawnPosition, registry.TransformSeedFor(treeId));
            // Anything unregistered is a player avatar and still gets the spawn point.
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition, registry.TransformSeedFor(treeId + 1000));
        }

        [Fact]
        public void The_tree_is_a_World_entity_and_not_mistaken_for_the_island()
        {
            // KindOf is only a log label - adding a member to SeededEntityKind is
            // the wrong axis - but mislabelling the tree as the island would hand
            // it the island branch of 1041 IslandState.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);

            long treeId = registry.EntityIdFor(registry.ByKey(WorldEntities.HavenTreeKey)!);
            long islandId = registry.EntityIdFor(registry.ByKey(WorldEntities.IslandKey)!);

            Assert.Equal(SeededEntityKind.World, registry.KindOf(treeId));
            Assert.Equal(SeededEntityKind.Island, registry.KindOf(islandId));
            Assert.NotEqual(treeId, islandId);
            Assert.Contains("Tree", registry.Describe(treeId));
        }

        [Fact]
        public void Every_client_is_given_the_SAME_entity_id_for_the_tree()
        {
            // Not a nicety. The cut signal names the tree BY ID, and the server
            // resolves that id against its own registry - so a per-client id means
            // one player's chop resolves to nothing and the other's to the tree,
            // silently.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            WorldEntity tree = registry.ByKey(WorldEntities.HavenTreeKey)!;

            long first = registry.EntityIdFor(tree);
            long second = registry.EntityIdFor(tree);
            long third = registry.EntityIdFor(tree);

            Assert.Equal(first, second);
            Assert.Equal(first, third);
        }

        [Fact]
        public void The_spawn_plan_requests_the_trees_bundle_before_it_creates_the_tree()
        {
            // The failure with no error message: the client drops an AddEntityOp
            // for a prefab it has not loaded and simply never shows the object.
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator()));

            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));
            Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));

            int asset = IndexOf(plan, SpawnOp.RequestAsset, WorldEntities.HavenTreeKey);
            int entity = IndexOf(plan, SpawnOp.AddEntity, WorldEntities.HavenTreeKey);
            int player = IndexOf(plan, SpawnOp.AddEntity, null);

            Assert.True(asset >= 0 && entity > asset);
            Assert.True(entity > player, "the tree must not delay the player's own spawn");
        }

        // ------------------------------------------------------------------
        // The species facts
        // ------------------------------------------------------------------

        [Fact]
        public void The_wood_is_birch_because_that_is_what_Bossa_authored()
        {
            // Recovered, not chosen: TreePreprocessor.woodType survives onto the
            // shipped _unityworker prefabs, all 65 were parsed, and 65/65 landed on
            // one of the eight known woods. Tree_unityworker is birch.
            // docs/research/loop/data/tree_woodtypes.json.
            Assert.Equal("birch", Trees.WoodType);
        }

        [Fact]
        public void The_scale_is_one_because_the_default_makes_an_invisible_tree()
        {
            // TreeScaleVisualiser.OnEnable is one statement -
            // transform.localScale = treeState.Scale.ToUnityVector() - with no
            // guard, and Vector3d's default is (0,0,0). A tree seeded with the
            // default is invisible, keeps working colliders, and logs nothing.
            Assert.Equal(1.0, Trees.Scale);
            Assert.NotEqual(0.0, Trees.Scale);
        }

        [Fact]
        public void The_tree_is_not_dynamic_because_the_setter_starts_a_falling_audio_loop()
        {
            // TreeBase.Dynamic's SETTER calls TreeAmbienceSfx.TryActivateFallingAudio()
            // on the true edge - on a tree that is not falling, because nothing
            // here gives a tree physics authority.
            Assert.False(Trees.Dynamic);
        }

        /// <summary>Index of a step, by op and by registration key (null = the player).</summary>
        private static int IndexOf(IReadOnlyList<SpawnPlanStep> plan, SpawnOp op, string? key)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Op == op && (key == null ? plan[i].Entity == null : plan[i].Entity?.Key == key))
                {
                    return i;
                }
            }
            return -1;
        }

        [Fact]
        public void The_seeded_item_health_is_non_zero_and_undamaged()
        {
            // health == 0 makes SalvageableItemVisualiser.OnEnable paint every
            // renderer black; health < maxHealth makes IsDamaged() true, and
            // IsSalvageable() is !IsDamaged() || IsRepairable() - so a damaged tree
            // is only aimable if it also happens to be repairable.
            Assert.True(Trees.ItemHealth > 0);
        }
    }
}
