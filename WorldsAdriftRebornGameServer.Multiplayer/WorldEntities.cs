namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The world entities this server knows how to spawn, and the registry it
    /// builds from them.
    ///
    /// This is the file a caller edits to put a new thing in the world. Adding a
    /// tree or a ship frame is: write one <see cref="WorldEntity"/> here, add it
    /// to <see cref="Default"/>, and add whatever component branches its
    /// <see cref="WorldEntity.SeedComponents"/> name to ComponentsSerializer.
    /// Nothing in the spawn state machine, the entity-id allocator or the
    /// component serializer's dispatch needs to change - that is the whole point
    /// of the seam.
    ///
    /// Positions come from `docs/research/world-data/wamap-islands.json`, a
    /// preserved copy of the studio's own world map (266 islands). They are
    /// written in METRES here and converted, rather than pasted as fixed-point
    /// literals, so the number in the file can be diffed against the number in
    /// the map data by eye.
    /// </summary>
    public static class WorldEntities
    {
        /// <summary>
        /// Prefab context for an entity with a single variant. The island has
        /// always sent this literal; it is not a placeholder to be filled in
        /// later, it is what the client's dispatch expects when there is nothing
        /// to disambiguate.
        /// </summary>
        public const string DefaultAssetContext = "notNeeded?";

        /// <summary>The island's registration key. See <see cref="EntityIdAllocator.IslandKey"/>.</summary>
        public const string IslandKey = EntityIdAllocator.IslandKey;

        /// <summary>
        /// Haven instance #5, the island every player spawns on.
        ///
        /// BeforePlayer, and it is the only thing that has earned that: its
        /// colliders are the ground the player's seed position is measured
        /// against. See <see cref="SpawnOrder"/>.
        ///
        /// No seeded components. The client checks the island out and asks for
        /// what it wants over SEND_COMPONENT_INTEREST; that path has worked since
        /// before this seam existed and pushing components at it unprompted would
        /// only add a way to fail.
        /// </summary>
        public static WorldEntity Island()
        {
            return new WorldEntity(
                IslandKey,
                SpawnPolicy.IslandAssetName,
                DefaultAssetContext,
                SpawnPolicy.IslandPosition,
                seedComponents: null,
                order: SpawnOrder.BeforePlayer);
        }

        /// <summary>The proof entity's registration key. See <see cref="ProofIsland"/>.</summary>
        public const string ProofIslandKey = "island-north";

        /// <summary>
        /// A SECOND Haven, ~2.96 km north, at world (17003.416, -212.325027,
        /// 1826.00183) m.
        ///
        /// WHAT IT IS FOR. It is the trivial third entity that proves this seam
        /// end to end without dragging in any downstream work. It exercises every
        /// part of the mechanism - its own registration key, its own entity id
        /// allocated once and shared by every client, its own asset request, its
        /// own AddEntityOp, and its own 190602 position seeded from the registry
        /// rather than from a constant - while needing ZERO new branches in
        /// ComponentsSerializer, because every component the client will ask an
        /// island for already has one.
        ///
        /// WHY THIS AND NOT A TREE. A tree needs eight new serializer branches
        /// before it is anything but inert scenery, and a ship needs four. Either
        /// would have proven the seam and the new branches at the same time, which
        /// is exactly the entanglement this registration avoids: if a tree fails,
        /// this tells you whether the seam or the seeds are at fault.
        ///
        /// It is entry 6 of the twelve `1431299145.json` placements in
        /// `docs/research/world-data/wamap-islands.json` - a real position from
        /// the studio's world map, the neighbour of the instance #5 we already
        /// spawn, not an invented offset. Haven ships as ONE asset placed at
        /// TWELVE world positions in a north-south column.
        ///
        /// AfterPlayer: nobody stands on it, so making the loading screen wait on
        /// a second 4.31 MiB bundle would be pure cost.
        ///
        /// OFF BY DEFAULT. It is enabled by WAREBORN_SPAWN_PROOF_ISLAND=1 (see
        /// <see cref="Default"/>) because it has never been in front of a running
        /// client - no game was launched for this change - and a second island is
        /// a visible change to what players see. The policy is tested; the pixels
        /// are not.
        /// </summary>
        public static WorldEntity ProofIsland()
        {
            return new WorldEntity(
                ProofIslandKey,
                SpawnPolicy.IslandAssetName,
                DefaultAssetContext,
                FixedPointPosition.FromMetres(17003.416, -212.325027, 1826.00183),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>The ship hull's registration key. See <see cref="ShipFrame"/>.</summary>
        public const string ShipFrameKey = "ship-haven";

        /// <summary>
        /// The prefab name of the procedural ship hull root.
        ///
        /// BARE, and not one of the `ShipFrame01`/`ShipFrame02` variants. Two
        /// reasons: `ShipFrame` is the one whose geometry comes from 1209 rather
        /// than being baked, and `ShipFrame01_unityclient` has no root Rigidbody,
        /// which `PathFollower.Awake` fetches unconditionally - so the baked
        /// variants are the ones that can never be made to move.
        ///
        /// It resolves even though no island manifest mentions it: ship prefabs
        /// are baked into `resources.assets`, which is always resident, and the
        /// client's dispatch ignores prefab CONTEXT for every name that does not
        /// start with Traveller, ModalErrorPopup or Spectator
        /// (DispatchEventHandler.cs:342-344).
        /// </summary>
        public const string ShipFrameAssetName = "ShipFrame";

        /// <summary>
        /// The components seeded on the hull, and the entire reason a ship can be
        /// spawned rather than built. FOUR, measured off the shipped client
        /// prefab's `[Require]` map (`docs/research/loop/data/req_shipframe.tsv`):
        ///
        ///   190602 TransformState        - position, and half of what
        ///                                  SSPDeadReckoningVisualizer requires
        ///   1209   CustomShipHullState   - the hull blob; drives
        ///                                  CustomShipFrameVisualizer -> mesh + colliders
        ///   1099   SalvageAndRepairState - the OTHER half of what
        ///                                  CustomShipFrameVisualizer requires
        ///   1130   SSPPredictedMotionState - the other half of
        ///                                  SSPDeadReckoningVisualizer -> PathFollower
        ///
        /// Everything else on the prefab stays disabled. That is not a
        /// simplification, it is the SHIPPED DEFAULT: every `*Visualizer` on
        /// `ShipFrame_unityclient` ships at `m_Enabled = 0` and is switched on by
        /// the injector only once all of its `[Require]` readers can be
        /// satisfied. Seeding a fifth component would switch on a fifth
        /// behaviour, which is a way to lose, not a way to gain.
        ///
        /// ORDER: 190602 first. The batch is applied in the order given and the
        /// position is the thing every other behaviour reads back.
        ///
        /// ALL-OR-NOTHING. This list goes out with failOnComponentInitError TRUE,
        /// so one id with no branch in ComponentsSerializer drops all four and
        /// leaves a fully-rendered inert hull. All four have branches; the
        /// `[interest]` line in SendOPHelper is what proves it stayed that way.
        /// </summary>
        public static readonly IReadOnlyList<uint> ShipFrameSeedComponents =
            new uint[] { 190602, 1209, 1099, 1130 };

        /// <summary>
        /// A single static ship hull on Haven, 12 m north of where the player
        /// wakes up.
        ///
        /// WHAT IT IS FOR. It is step 2 of `docs/research/loop/findings-first-ship.md`
        /// - the first thing this server has ever put in the world that is not
        /// terrain or a person, and the first test of the claim that a ship is
        /// `AddEntity("ShipFrame")` plus four components rather than a shipyard, a
        /// blueprint flow and weeks of crafting. It does not move; the path
        /// publisher that would make it a ferry is deliberately not here, because
        /// it is blocked on a carry test that needs a running client.
        ///
        /// WHERE IT IS. Island-local (208.00, 5.30, 16.00) on Haven instance #5,
        /// i.e. the island's own fixed point plus (208*4096, (long)(5.30*4096),
        /// 16*4096) = (69650145 + 851968, -1305269 + 21708, -4645549 + 65536).
        /// In metres that is world (17212.4300, -313.3694, -1118.1675).
        ///
        /// It shares the player's X exactly and sits 12.00 m further north, so it
        /// is a straight walk from the spawn point with nothing between. The
        /// point is entry (208.00, 4.80, 16.00) of the TRS-corrected LOD0 surface
        /// table `docs/research/world-data/island-surfaces/1431299145.json`, whose
        /// normal is (0.02, 1.00, -0.02) - the flattest ground within 30 m of the
        /// spawn - and whose four sampled neighbours at 8 m span only 0.71 m of
        /// height (4.80, 4.98, 4.99, 5.16, 5.51), which is what a 12 m x 4 m
        /// footprint needs.
        ///
        /// The 0.50 m above that surface vertex is a STAND-OFF, and a smaller one
        /// than the player's 2.00 m for a different reason: the hull's deck plane
        /// is at the entity's own local y = 0 and nothing on a one-cell plan
        /// hangs below it, so this is the height of the step up onto the ship. A
        /// metre would clear the terrain more comfortably and might be too tall
        /// to walk up; sinking it to zero would z-fight with the ground.
        ///
        /// AfterPlayer: the player must not be standing on it when they spawn -
        /// they spawn 12 m away, on the island - so making the loading screen
        /// wait on it would be pure cost.
        /// </summary>
        public static WorldEntity ShipFrame()
        {
            return new WorldEntity(
                ShipFrameKey,
                ShipFrameAssetName,
                DefaultAssetContext,
                new FixedPointPosition(70502113, -1283561, -4580013),
                ShipFrameSeedComponents,
                SpawnOrder.AfterPlayer);
        }

        /// <summary>The tree's registration key. See <see cref="HavenTree"/>.</summary>
        public const string HavenTreeKey = "tree-haven";

        /// <summary>
        /// Island-local (208.00, 4.99, 8.00) on Haven instance #5, i.e. world
        /// (17212.4300, -313.6793, -1126.1675) m.
        ///
        /// FOUR METRES from the player's own spawn point, on the same island-local
        /// x. Derived exactly the way <see cref="SpawnPolicy.PlayerSpawnPosition"/>
        /// was, from the same two sources, so the two numbers can be checked
        /// against each other rather than each taken on trust:
        ///
        ///   island   (69650145, -1305269, -4645549)   fixed point, = Haven #5
        /// + local    (208.00, 4.99, 8.00) m           x 4096, truncated
        /// = tree     (70502113, -1284830, -4612781)
        ///
        /// <c>WorldEntitiesTests</c> asserts that arithmetic, so the literals
        /// cannot drift from the derivation.
        ///
        /// (208, 4.99, 8) IS A MEASURED LOD0 SURFACE VERTEX, entry of the 2,139
        /// candidates in <c>docs/research/world-data/island-surfaces/1431299145.json</c>
        /// nearest the spawn point in the 2.5-12 m band. Its normal is ny = 0.999
        /// (dead flat) and the nearest prop of any kind is 12.71 m away in 3D. It
        /// is NOT the spawn coordinate with an offset added: an invented offset
        /// could land in a rock or in mid-air, and this island's pre-TRS surface
        /// tables were once wrong by a mean of 47.7 m, so a coordinate that was not
        /// measured is a coordinate that is probably underground.
        ///
        /// The Y is the SURFACE, with no stand-off. The player's spawn Y carries
        /// +2.00 m because a player dropped 0.15 m underground interpenetrates the
        /// ground; a tree wants its base exactly on it, and a tree floating two
        /// metres up would be unmistakable.
        ///
        /// "In front of" is approximate and honestly so. The spawn rotation is the
        /// identity sentinel, so which way the player faces on arrival is not
        /// something this server decides - what is guaranteed is four metres away,
        /// flat, clear, and well inside the 40 m the aim raycast reaches.
        /// </summary>
        public static readonly FixedPointPosition HavenTreePosition =
            new FixedPointPosition(70502113, -1284830, -4612781);

        /// <summary>
        /// ONE CHOPPABLE TREE, four metres from where the player wakes up.
        ///
        /// WHY A TREE AND NOT SCENERY. There is no choice: not one of the 465,571
        /// props baked into the 255 island bundles is a tree, on any island,
        /// including this one. The prop channel cannot produce an entity at all -
        /// <c>PopulateStaticPrefabs.InitObjectFromData</c> sets position, rotation
        /// and scale and attaches no <c>EntityObjectStorage</c> - so a scenery tree
        /// has no entity id, cannot be aimed at
        /// (<c>PlayerLookingAt.GetInteractiveObject</c> returns null for it), and
        /// publishes <c>EntityId(0)</c> if something did cut it. The 65 tree
        /// prefabs in the prop library are editor markers that were exported into a
        /// server-side spawn list and stripped; the list did not survive. Every
        /// tree in this world is one we place. See
        /// docs/research/loop/findings-harvestable-world.md.
        ///
        /// NO SEEDED COMPONENTS, exactly like the island, and this is the single
        /// most important line in the registration. The client checks the entity
        /// out and asks for what it wants over SEND_COMPONENT_INTEREST, and that
        /// request is served all-or-nothing. Pushing an unprompted seed list would
        /// add a SECOND all-or-nothing batch whose contents are our guess at the
        /// client's needs rather than the client's own statement of them - pure
        /// downside, since a guess that is too small fails and a guess that is
        /// right is redundant.
        ///
        /// What the client will ask for was derived from the shipped prefab rather
        /// than assumed: 148 nodes, 40 MonoBehaviours, of which 13 are visualizers
        /// (independently confirmed by the <c>m_Enabled = 0</c> markers
        /// <c>PrefabCompiler.DisableVisualizers</c> leaves on exactly the
        /// visualizers), and <c>VisualizerMetadataLookup</c> walks the whole
        /// hierarchy with <c>includeInactive: true</c> and collects the READER ids
        /// of their <c>[Require]</c> fields, base classes included. That yields ten
        /// ids - 190601, 190602, 1035, 1036, 1016, 1099, 1183, 1232, 4333, 4400 -
        /// and every one now has a branch in ComponentsSerializer. An eleventh,
        /// 190604, can arrive in a LATER full-set resend if the transform hierarchy
        /// falls back to Global mode; it already had a branch, which is the only
        /// reason that is not a landmine.
        ///
        /// AfterPlayer. Nobody stands on a tree, and every step before the player
        /// is a step the loading screen waits on.
        /// </summary>
        public static WorldEntity HavenTree()
        {
            return new WorldEntity(
                HavenTreeKey,
                Trees.AssetName,
                DefaultAssetContext,
                HavenTreePosition,
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// Trees distributed across the whole island (island-local metres),
        /// farthest-point sampled from the 1431299145 surface table (ny&gt;0.90),
        /// so harvesting can be tested away from spawn too. Each spawns as its own
        /// choppable Tree entity (unique key), planted by AssetName exactly like
        /// <see cref="HavenTree"/>. AfterPlayer, so none can delay a player spawn.
        /// </summary>
        public static readonly IReadOnlyList<(double X, double Y, double Z)> DistributedTreeLocals =
            new (double, double, double)[]
        {
            (-59.7, 12.00, 88.0), (116.0, 7.65, 24.0),  (168.0, 4.43, -40.0),
            (142.1, 4.00, 68.0),  (224.0, 2.87, 32.0),  (168.0, 4.46, 32.0),
            (216.0, 2.69, -8.0),  (144.0, 4.35, 24.0),  (168.0, 3.74, 56.0),
            (208.0, 2.06, 48.0),  (152.0, 3.96, -20.0), (32.0, 11.11, -112.0),
            (184.0, 2.80, 48.0),  (160.0, 4.72, 16.0),  (240.0, 3.58, 16.0),
            (224.0, 8.83, 8.0),   (136.0, 3.86, -16.0), (168.0, 5.06, 0.0),
            (176.0, 4.90, 16.0),  (128.0, 4.80, 16.0),
        };

        /// <summary>
        /// The distributed trees as spawnable entities, keyed tree-0..N.
        /// </summary>
        public static IEnumerable<WorldEntity> DistributedTrees()
        {
            int i = 0;
            foreach ((double x, double y, double z) in DistributedTreeLocals)
            {
                yield return new WorldEntity(
                    "tree-" + i++,
                    Trees.AssetName,
                    DefaultAssetContext,
                    MetalNodes.IslandLocalToWorldFixed(MetalNodes.IslandOrigin, x, y, z),
                    seedComponents: null,
                    order: SpawnOrder.AfterPlayer);
            }
        }

        /// <summary>
        /// A placed metal resource node as a <see cref="WorldEntity"/>: the
        /// <c>MetalNugget</c> prefab at one measured Haven surface vertex.
        ///
        /// NO SEEDED COMPONENTS, exactly like the island and the tree. The client
        /// checks the node out and asks for what it wants over
        /// SEND_COMPONENT_INTEREST, which the server answers BEST-EFFORT (a node is
        /// not the sender's own player entity, so it never takes the all-or-nothing
        /// path) - so an id the nugget's prefab asks for that has no branch yet is
        /// skipped, and the nugget still renders from its BAKED geometry. That is
        /// Phase 0.3: one unhandled component id does not leave the node inert.
        ///
        /// AfterPlayer: nobody stands on a node, so it never delays the loading
        /// screen.
        /// </summary>
        public static WorldEntity MetalNodeEntity(MetalNode node)
        {
            return new WorldEntity(
                node.Key,
                MetalNodes.AssetName,
                DefaultAssetContext,
                node.Position,
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// The registry the server runs with.
        /// </summary>
        /// <param name="ids">The id source. Shared with player entity ids.</param>
        /// <param name="includeProofIsland">
        /// Whether to include <see cref="ProofIsland"/>. The server passes the
        /// WAREBORN_SPAWN_PROOF_ISLAND environment variable; tests pass both.
        /// </param>
        /// <param name="includeTree">
        /// Whether to include <see cref="HavenTree"/>. ON by default - it is the
        /// point of the change - with WAREBORN_SPAWN_TREE=0 as the kill switch,
        /// because no game was launched for this and a tree that misbehaves should
        /// be switchable off without a rebuild. It is safe to leave on even if it
        /// does misbehave: the tree is AfterPlayer, so nothing about it can delay
        /// or break the player's own spawn.
        /// </param>
        /// <param name="includeMetal">
        /// Whether to place the <see cref="MetalNodes.Haven"/> nugget nodes. ON by
        /// default with WAREBORN_SPAWN_METAL=0 as the kill switch, same footing as
        /// the tree: they are AfterPlayer, so a misbehaving node cannot delay or
        /// break a player's own spawn.
        /// </param>
        /// <param name="metalOnlyProven">
        /// When true, places ONLY the single proven node. The cautious first-live
        /// mode the standing caveat calls for: the coordinate chain has never been
        /// validated against a running client, so one node before the whole table.
        /// The server passes WAREBORN_SPAWN_METAL=proven. Wins over
        /// <paramref name="oreCountEnv"/>: proven-only is the safest count there is.
        /// </param>
        /// <param name="treeCountEnv">
        /// The raw WAREBORN_TREE_COUNT value, or null for the full set. Caps the
        /// TOTAL number of trees (the near-spawn HavenTree plus distributed ones),
        /// clamped to [1, all placed]; the HavenTree is first, so any count keeps
        /// it. Parsed by <see cref="SpawnCountPolicy"/> so a bad value is the full
        /// set rather than a crash.
        /// </param>
        /// <param name="oreCountEnv">
        /// The raw WAREBORN_ORE_COUNT value, or null for the full set. Caps the
        /// number of metal nodes, clamped to [1, all placed]; placement index 0 is
        /// the proven node, so any count keeps it. Ignored when
        /// <paramref name="metalOnlyProven"/> is set.
        /// </param>
        public static WorldEntityRegistry Default(EntityIdAllocator ids, bool includeProofIsland = false, bool includeTree = true, bool includeMetal = true, bool metalOnlyProven = false, string? treeCountEnv = null, string? oreCountEnv = null)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);

            registry.Register(Island());

            if (includeProofIsland)
            {
                registry.Register(ProofIsland());
            }

            registry.Register(ShipFrame());
            if (includeTree)
            {
                // Total trees = HavenTree (always, index 0 of the set) + the first
                // (N-1) distributed trees. Clamped to [1, full] so the near-spawn
                // tree can never be dropped and an over-large count cannot overrun.
                int fullTrees = 1 + DistributedTreeLocals.Count;
                int treeTotal = SpawnCountPolicy.CountFrom(treeCountEnv, fullTrees);

                registry.Register(HavenTree());
                foreach (WorldEntity tree in DistributedTrees().Take(treeTotal - 1))
                {
                    registry.Register(tree);
                }
            }

            if (includeMetal)
            {
                // Proven-only wins; otherwise the env cap, defaulting to the full
                // table. Either way index 0 (the proven node) is included.
                int oreCount = metalOnlyProven
                    ? 1
                    : SpawnCountPolicy.CountFrom(oreCountEnv, MetalNodes.HavenPlacements.Count);

                foreach (MetalNode node in MetalNodes.Haven(oreCount))
                {
                    registry.Register(MetalNodeEntity(node));
                }
            }

            return registry;
        }
    }
}
