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

        /// <summary>
        /// The registry the server runs with.
        /// </summary>
        /// <param name="ids">The id source. Shared with player entity ids.</param>
        /// <param name="includeProofIsland">
        /// Whether to include <see cref="ProofIsland"/>. The server passes the
        /// WAREBORN_SPAWN_PROOF_ISLAND environment variable; tests pass both.
        /// </param>
        public static WorldEntityRegistry Default(EntityIdAllocator ids, bool includeProofIsland = false)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);

            registry.Register(Island());

            if (includeProofIsland)
            {
                registry.Register(ProofIsland());
            }

            registry.Register(ShipFrame());

            return registry;
        }
    }
}
