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
        /// The three SHIP-RECOGNITION components, appended to the hull's proactive
        /// seed when <see cref="HullSeedComponents"/> is asked with recognition ON
        /// (the runtime default; kill switch WAREBORN_SHIP_RECOGNISE=0):
        ///
        ///   8062 ShipOwnersDeprecatedState
        ///   8071 ShipPartCountState
        ///   4349 ShipRegisteredCharactersState
        ///
        /// They are the complete [Require] set of the client's own ShipVisualizer
        /// (VERIFIED off ShipFrame_unityclient's [Require] map and the decompiled
        /// ShipVisualizer fields). With them present the injector ENABLES
        /// ShipVisualizer, so GetComponentInParent&lt;ShipVisualizer&gt; on the hull
        /// succeeds and the client TAGS the surface as a ship. See
        /// <see cref="Multiplayer.ShipRecognition"/> for the values and for the
        /// carry-chain caveat (the physical carry is the hull's PathFollower, not
        /// this visualizer).
        ///
        /// KEPT SEPARATE from <see cref="ShipFrameSeedComponents"/> because they
        /// carry DIFFERENT risk. The base four are proven against a running client;
        /// these three have never been, so they sit behind a kill switch defaulting
        /// ON, exactly like the deck and the trees. And even with the switch OFF the
        /// client still gets them: it REQUESTS all three over interest (that is how
        /// we learned they were missing - "unhandled component id 8062/8071/4349"),
        /// and ComponentsSerializer now answers best-effort. The switch only chooses
        /// whether they ALSO ride the hull's proactive, all-or-nothing batch.
        /// </summary>
        public static readonly IReadOnlyList<uint> ShipRecognitionSeedComponents =
            Multiplayer.ShipRecognition.SeedComponents;

        /// <summary>
        /// The hull's full proactive seed set: <see cref="ShipFrameSeedComponents"/>
        /// alone, or with <see cref="ShipRecognitionSeedComponents"/> appended when
        /// <paramref name="recogniseShip"/> is set. 190602 stays first (the position
        /// every other behaviour reads back); the recognition ids go last so a
        /// recognition serialize failure can never come before the geometry in the
        /// all-or-nothing batch.
        /// </summary>
        public static IReadOnlyList<uint> HullSeedComponents(bool recogniseShip)
        {
            if (!recogniseShip)
            {
                return ShipFrameSeedComponents;
            }

            List<uint> all = new List<uint>(ShipFrameSeedComponents);
            all.AddRange(ShipRecognitionSeedComponents);
            return all;
        }

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
        /// <summary>
        /// The calibrated on-ground default hull position: island-local
        /// (208.00, 7.70, 16.00). Y raised from -1283561 (deck buried ~1.4 m under
        /// the surface) to -1273730 so the deck plane sits ~1 m ABOVE the player
        /// spawn floor (feet at -1277826), clear of the real collision terrain.
        /// </summary>
        public static readonly FixedPointPosition ShipFrameDefaultPosition =
            new FixedPointPosition(70502113, -1273730, -4580013);

        /// <summary>
        /// Where the hull spawns. Overridable at runtime with
        /// <c>WAREBORN_SHIP_POS="x,y,z"</c> (WORLD metres) so the ship can be moved
        /// anywhere for testing WITHOUT a rebuild or a test change - the deck, helm
        /// and every other part derive their position from this, so they follow it.
        /// A malformed value falls back to <see cref="ShipFrameDefaultPosition"/>.
        /// </summary>
        public static FixedPointPosition ShipFramePosition()
        {
            string? env = Environment.GetEnvironmentVariable("WAREBORN_SHIP_POS");
            if (!string.IsNullOrWhiteSpace(env))
            {
                string[] p = env.Split(',');
                if (p.Length == 3
                    && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)
                    && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y)
                    && double.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double z))
                {
                    return FixedPointPosition.FromMetres(x, y, z);
                }
            }
            return ShipFrameDefaultPosition;
        }

        /// <param name="recogniseShip">
        /// Whether to append <see cref="ShipRecognitionSeedComponents"/> so the
        /// client's ShipVisualizer enables and the hull is a recognised ship. ON by
        /// default - it reflects the runtime default (WAREBORN_SHIP_RECOGNISE != 0) -
        /// and the no-arg callers that only read <see cref="WorldEntity.Position"/>
        /// (the parts' position derivation and the tests) are unaffected by it.
        /// </param>
        public static WorldEntity ShipFrame(bool recogniseShip = true)
        {
            return new WorldEntity(
                ShipFrameKey,
                ShipFrameAssetName,
                DefaultAssetContext,
                ShipFramePosition(),
                HullSeedComponents(recogniseShip),
                SpawnOrder.AfterPlayer);
        }

        /// <summary>The helm part's registration key. See <see cref="Helm"/>.</summary>
        public const string HelmKey = Multiplayer.Helm.Key;

        /// <summary>
        /// The single interactable part bolted onto the static hull: a Helm01
        /// carrying the "Man" verb, sat on the deck.
        ///
        /// A ship is N+1 entities linked by 8066 ShipRootState (findings-first-ship,
        /// "Many entities, not one"). This is the +1: its OWN entity, whose 190602 is
        /// seeded hull-RELATIVE (parent = Parent(hullId, "~"), see
        /// <see cref="Multiplayer.BoltedPartTransform"/>) with the offset from
        /// <see cref="Multiplayer.Helm.OnDeckOf"/>, so it MOVES with the hull rather
        /// than drifting. The 8066 seed pointing this part's shipRoot at the
        /// hull, and the 1210 InteractiveState carrying InteractVerb.Man, are served
        /// by ComponentsSerializer when the client requests them - the same
        /// best-effort, interest-driven path the MetalNugget's PickUp prompt uses,
        /// so like the nugget this registration seeds NOTHING unprompted. The helm
        /// renders from its baked prefab geometry and the Man prompt appears when a
        /// player walks within <see cref="Multiplayer.Helm.ManRadius"/>.
        ///
        /// AfterPlayer, and registered AFTER the hull so the hull's entity id is
        /// already allocated by the time the helm's 8066 needs to name it.
        /// </summary>
        public static WorldEntity Helm()
        {
            return new WorldEntity(
                HelmKey,
                Multiplayer.Helm.AssetName,
                DefaultAssetContext,
                Multiplayer.Helm.OnDeckOf(ShipFrame().Position),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>The walkable deck part's registration key. See <see cref="Deck01"/>.</summary>
        public const string DeckKey = Multiplayer.Deck.Key;

        /// <summary>The aft engine part's registration key. See <see cref="ModularEngine"/>.</summary>
        public const string EngineKey = ShipParts.EngineKey;

        /// <summary>The amidships sail part's registration key. See <see cref="Sail01"/>.</summary>
        public const string SailKey = ShipParts.SailKey;

        /// <summary>
        /// Whether <paramref name="key"/> names a part BOLTED onto the hull - a
        /// helm, deck, engine or sail - as opposed to the hull itself. The 8066
        /// branch in ComponentsSerializer asks this to decide isRoot: a bolted part
        /// points its shipRoot at the hull (isRoot=false); the hull is its own root.
        /// Centralised here so adding a part is one registration plus one line,
        /// never a scattered set of <c>==</c> checks that fall out of step.
        /// </summary>
        public static bool IsBoltedPartKey(string? key)
        {
            return key == HelmKey || key == DeckKey || key == EngineKey || key == SailKey;
        }

        /// <summary>
        /// The single walkable FLOOR bolted onto the static hull: a Deck01 whose
        /// 1518 vertices the client turns into a SOLID collider a player can stand
        /// on while the hull moves. THE primary deliverable of the full-ship work.
        ///
        /// A ship is N+1 entities linked by 8066 (findings-first-ship). This is the
        /// floor +1: its OWN entity, CENTRED on the hull by
        /// <see cref="Multiplayer.Deck.OnHull"/> (its vertices are origin-centred, so
        /// the deck centre coincides with the hull centre) and seeded hull-RELATIVE -
        /// its 190602 carries parent = Parent(hullId, "~") and the local offset from
        /// the hull (see <see cref="Multiplayer.BoltedPartTransform"/>), so the client
        /// composes it with the hull's live position every FixedUpdate and the solid
        /// deck can no longer drift out from under a player standing on it.
        ///
        /// Seeds NOTHING unprompted, exactly like the helm, the tree and the nugget:
        /// the client checks the deck out and asks for what it wants over
        /// SEND_COMPONENT_INTEREST, and ComponentsSerializer answers best-effort. The
        /// two that make the floor solid - 1518 ShipDeckState (the polygon) and 1099
        /// SalvageAndRepairState (one Wood material, so ShipDeckVisualizer.OnEnable's
        /// OriginalMaterials[0] does not throw) - both have branches, so the client's
        /// ShipDeckVisualizer enables and builds a solid BoxCollider deck. 8066 links
        /// it to the hull. Everything else the prefab asks for is skipped, so
        /// ShipPartVisualizer stays dormant - the same rule-7-safe state the helm's
        /// 8066 sits in.
        ///
        /// AfterPlayer, and registered AFTER the hull so the hull's entity id is
        /// already allocated when the deck's 8066 and its ship-surface membership
        /// need to name it.
        /// </summary>
        public static WorldEntity Deck01()
        {
            return new WorldEntity(
                DeckKey,
                Multiplayer.Deck.AssetName,
                DefaultAssetContext,
                Multiplayer.Deck.OnHull(ShipFrame().Position),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// The aft engine part - cosmetic, so the hull reads as a whole ship. Baked
        /// geometry + an 8066 link to the hull, exactly like the helm; its special
        /// 12281 visualizer is left dormant (no exhaust VFX). See
        /// <see cref="ShipParts"/> for the ASSUMPTION this rests on. AfterPlayer,
        /// after the hull.
        /// </summary>
        public static WorldEntity ModularEngine()
        {
            return new WorldEntity(
                EngineKey,
                ShipParts.EngineAssetName,
                DefaultAssetContext,
                ShipParts.EngineOnHull(ShipFrame().Position),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// The amidships sail part - cosmetic, same footing as
        /// <see cref="ModularEngine"/>; its 1303 SailState visualizer is left dormant
        /// (no cloth physics). AfterPlayer, after the hull.
        /// </summary>
        public static WorldEntity Sail01()
        {
            return new WorldEntity(
                SailKey,
                ShipParts.SailAssetName,
                DefaultAssetContext,
                ShipParts.SailOnHull(ShipFrame().Position),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
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
        /// An anchored metal DEPOSIT as a <see cref="WorldEntity"/>: the
        /// <c>metal_deposit_entity</c> prefab at one measured Haven surface vertex.
        ///
        /// NO SEEDED COMPONENTS, exactly like the nugget, the tree and the island. The
        /// deposit is NOT the sender's own player entity, so its interest requests are
        /// answered BEST-EFFORT rather than all-or-nothing - the client checks it out,
        /// asks for its 1255/2103/12283/1016/190602 (+ 1099) over
        /// SEND_COMPONENT_INTEREST, and ComponentsSerializer answers each. Pushing an
        /// unprompted seed batch would only add an all-or-nothing failure mode whose
        /// contents are our guess at the prefab's needs rather than its own statement
        /// of them. Unlike the nugget, the deposit's geometry is IMPORTED at runtime
        /// from the variant named by 1255, so a missing/invalid variantId leaves it
        /// invisible even though the entity exists (see <see cref="MetalDeposits.VariantId"/>).
        ///
        /// AfterPlayer: nobody stands on a deposit, so it never delays the loading
        /// screen, and a misbehaving deposit can never break a player's own spawn.
        /// </summary>
        public static WorldEntity DepositEntity(MetalNode node)
        {
            return new WorldEntity(
                node.Key,
                MetalDeposits.AssetName,
                DefaultAssetContext,
                node.Position,
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// An ATLAS SHARD lodged in the deposit at placement <paramref name="index"/> -
        /// the real retail acquisition object, a SEPARATE <c>MetalDepositAtlas</c>
        /// entity from its host deposit. Keyed <c>atlas-shard-N</c> to pair with
        /// <c>deposit-N</c>, positioned at the deposit raised by
        /// <see cref="AtlasShardCatalogue.LodgedHeightOffsetMetres"/> so it reads as
        /// sitting in the core.
        ///
        /// NO SEEDED COMPONENTS, exactly like the deposit and the nugget: the client
        /// checks the shard out and asks for its 1305/2102/1210/190602 over
        /// SEND_COMPONENT_INTEREST, which ComponentsSerializer answers best-effort. Its
        /// 1305 rockCoreId and lodged/released/collected state are wired from the
        /// AtlasShards ledger (populated in AddWorldEntity from the host deposit's id).
        ///
        /// AfterPlayer, and it MUST be registered AFTER its host deposit so the
        /// deposit's shared entity id is already bound when the shard's spawn step
        /// resolves its host (see <see cref="Default"/>).
        /// </summary>
        public static WorldEntity AtlasShardEntity(int index, FixedPointPosition depositPosition)
        {
            return new WorldEntity(
                AtlasShardCatalogue.KeyFor(index),
                AtlasShardCatalogue.AssetName,
                DefaultAssetContext,
                AtlasShardCatalogue.LodgedPositionFor(depositPosition),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// A scannable DATABANK world entity at a placement index - the KNOWLEDGE
        /// analogue of <see cref="DepositEntity"/>. Same shape: no seedComponents (its
        /// 190602 TransformState and 8073 ScannableRuinState are served best-effort
        /// over interest, so a missing reader cannot abort an AddComponent batch), and
        /// AfterPlayer, because nobody stands on a databank and a misbehaving one must
        /// never delay a player's spawn.
        /// </summary>
        public static WorldEntity DatabankEntity(int index)
        {
            return new WorldEntity(
                Databanks.KeyFor(index),
                Databanks.AssetName,
                DefaultAssetContext,
                Databanks.PositionAt(index),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// A FUEL POD as a <see cref="WorldEntity"/>: the fuel-egg prefab at a measured
        /// Haven surface vertex, keyed <c>fuel-pod-N</c>. The FUEL analogue of the
        /// atlas shard, but HOST-LESS - a pod carries only 2102 LodgeableState (no 1305
        /// rock-core link), so it needs no host deposit and is registered already
        /// released (directly pickable). See <see cref="FuelPods"/>.
        ///
        /// NO SEEDED COMPONENTS, exactly like the nugget, the tree and the shard: the
        /// client checks the pod out and asks for its 2102/190602/1210 over
        /// SEND_COMPONENT_INTEREST, which ComponentsSerializer answers best-effort. Its
        /// lodged/released/collected state and the PickUp availability are wired from
        /// the fuel-pod ledger. AfterPlayer, so a misbehaving pod can never delay or
        /// break a player's own spawn.
        /// </summary>
        public static WorldEntity FuelPodEntity(int index)
        {
            return new WorldEntity(
                FuelPods.KeyFor(index),
                FuelPods.AssetName,
                DefaultAssetContext,
                FuelPods.PositionAt(index),
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>The global entity's registration key. See <see cref="GlobalEntity"/>.</summary>
        public const string GlobalEntityKey = "global";

        /// <summary>
        /// The prefab name of the SpatialOS GLOBAL entity - the one singleton the
        /// client hangs its world-wide data visualisers on (GlobalEntityPreprocessor
        /// adds GlobalWeatherDataVisualizer, WorldBoundsDataVisualizer,
        /// GlobalBiomeDataVisualizer, GlobalKnowledgeGraphDataVisualizer and
        /// GameDBVisualizer to it). BARE, client-resolvable (prefab-names.tsv line 78,
        /// client + worker "yes"; shipped as GlobalEntity_unityclient).
        /// </summary>
        public const string GlobalEntityAssetName = "GlobalEntity";

        /// <summary>
        /// The SpatialOS GLOBAL entity, spawned ONLY as the metal deposit's biome
        /// dependency (see <see cref="Default"/>, includeDeposit).
        ///
        /// WHY A DEPOSIT NEEDS IT. MetalDepositVisualiser.InitRoutine blocks on
        /// FindBiomeAsync, which polls GlobalBiomeDataVisualizer.GetBiomeAt(pos) until
        /// it returns a biome; GetBiomeAt reads the static zone table
        /// GlobalBiomeDataVisualizer builds in OnEnable from its 1253
        /// GlobalBiomeVoronoiCentresState. GlobalBiomeDataVisualizer only exists on
        /// THIS entity, and only enables once its two [Require]s - 1253 and 8064
        /// DevBiome - are checked out. With no global entity the deposit waits forever
        /// and never draws its rock (the crust/core loop works, but on an invisible
        /// entity). Trees and nuggets are not biome-keyed, so they never hit this.
        ///
        /// Only GlobalBiomeDataVisualizer wakes up: the server serves 1253 + 8064 and
        /// nothing else the prefab's other global visualisers [Require] (weather,
        /// world-bounds, knowledge-graph, GameDB), so those stay dormant on best-effort
        /// interest - no weather, no bounds behaviour, just the biome table. Its own
        /// transform position is irrelevant to the biome math (the Voronoi centres are
        /// absolute coordinates in the component, compared in X/Z only), so it is parked
        /// on the island origin.
        ///
        /// AfterPlayer, and registered BEFORE the deposits so its AddEntity is queued
        /// first and the biome resolves as early as possible - though the deposit's
        /// poll is patient, so even a late checkout resolves it.
        /// </summary>
        public static WorldEntity GlobalEntity()
        {
            return new WorldEntity(
                GlobalEntityKey,
                GlobalEntityAssetName,
                DefaultAssetContext,
                SpawnPolicy.IslandPosition,
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
        /// <param name="includeDeck">
        /// Whether to bolt the walkable <see cref="Deck01"/> onto the hull. ON by
        /// default - it is THE point of the full-ship work - with WAREBORN_SHIP_DECK=0
        /// as the kill switch, on the same footing as the tree and the helm: it is
        /// AfterPlayer, so a misbehaving deck can never delay or break a player's own
        /// spawn, and its solid-floor path has never been in front of a running
        /// client, so a switch to turn it off without a rebuild is worth its one line.
        /// </param>
        /// <param name="includeExtraParts">
        /// Whether to add the cosmetic <see cref="ModularEngine"/> and
        /// <see cref="Sail01"/>. OFF by default (WAREBORN_SHIP_PARTS=1 to add them):
        /// unlike the deck and the helm they rest on an unverified assumption that
        /// they render from baked geometry without their special visualizer, so they
        /// are opt-in until a live client confirms it. Safe to enable regardless -
        /// best-effort interest leaves an unrenderable part inert, never the ship.
        /// </param>
        /// <param name="recogniseShip">
        /// Whether to append the ship-recognition components (8062/8071/4349) to the
        /// hull's proactive seed so the client's ShipVisualizer enables. ON by
        /// default with WAREBORN_SHIP_RECOGNISE=0 as the kill switch. Off falls back
        /// to the proven base-four seed; the client still receives the three over
        /// interest, so recognition degrades to best-effort rather than vanishing.
        /// See <see cref="ShipRecognitionSeedComponents"/>.
        /// </param>
        /// <param name="includeDeposit">
        /// Whether to place the <see cref="MetalDeposits.Haven"/> anchored deposit(s) -
        /// the real ore mining loop. OFF by default (WAREBORN_SPAWN_DEPOSIT=1 to turn
        /// it on), unlike the nugget: the deposit's coordinate AND its runtime-imported
        /// variant chain have never been in front of a running client, and its geometry
        /// is imported rather than baked, so an invalid variant is an invisible entity.
        /// AfterPlayer, so a misbehaving deposit cannot delay or break a player's spawn.
        /// </param>
        /// <param name="depositCountEnv">
        /// The raw WAREBORN_DEPOSIT_COUNT value, or null for one (the proven deposit).
        /// Clamped to [1, all placed]; index 0 is the proven deposit, so any count keeps
        /// it. Defaults to a single deposit - the cautious first-live count.
        /// </param>
        /// <param name="includeAtlasShard">
        /// Whether to lodge an ATLAS SHARD in the proven deposit (index 0) - the real
        /// retail acquisition object. Only meaningful alongside
        /// <paramref name="includeDeposit"/> (a shard needs a live host core to render
        /// and be mined loose). ON by default when deposits are on, with
        /// WAREBORN_SPAWN_ATLAS=0 as the kill switch: it is AfterPlayer and inert until
        /// its core is destroyed, so a misbehaving shard can never delay or break a
        /// spawn, and the grant is a no-op until the pending retail itemTypeId is
        /// recovered (AtlasShardCatalogue.ItemTypeId), so it cannot mis-grant.
        /// </param>
        /// <param name="atlasRateEnv">
        /// The raw WAREBORN_ATLAS_RATE value, or null for the default. "One shard per N
        /// deposits" - the documented, deterministic <see cref="AtlasSpawnPolicy"/>
        /// reconstruction of the lost retail rarity rule. Defaults to every deposit
        /// (index 0, the proven deposit, always carries one).
        /// </param>
        /// <param name="includeFuelPods">
        /// Whether to place the <see cref="FuelPods.HavenPlacements"/> fuel pods - the
        /// gatherable FUEL crafting material. ON by default with WAREBORN_SPAWN_FUELPODS=0
        /// as the kill switch, on the same AfterPlayer footing as the tree and node: a
        /// misbehaving pod cannot delay or break a player's spawn, and it grants the
        /// real, already-shipping <c>"fuel"</c> item so it cannot mis-grant. Independent
        /// of the deposit/atlas spawns - a fuel pod is host-less and needs no deposit.
        /// </param>
        /// <param name="fuelPodCountEnv">
        /// The raw WAREBORN_FUELPOD_COUNT value, or null for the full starter set.
        /// Clamped to [1, all placed]; index 0 is the nearest-spawn pod, so any count
        /// keeps it.
        /// </param>
        public static WorldEntityRegistry Default(EntityIdAllocator ids, bool includeProofIsland = false, bool includeTree = true, bool includeMetal = true, bool metalOnlyProven = false, string? treeCountEnv = null, string? oreCountEnv = null, bool includeDeck = true, bool includeExtraParts = false, bool recogniseShip = true, bool includeDeposit = false, string? depositCountEnv = null, bool includeDatabank = false, string? databankCountEnv = null, bool includeAtlasShard = true, string? atlasRateEnv = null, bool includeFuelPods = true, string? fuelPodCountEnv = null)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);

            registry.Register(Island());

            if (includeProofIsland)
            {
                registry.Register(ProofIsland());
            }

            registry.Register(ShipFrame(recogniseShip));
            // The helm goes in right after the hull so the hull's shared entity id
            // is allocated first: the helm's 8066 seed names the hull by that id,
            // and ByEntityId/BoundEntityIdFor must be able to find it without
            // allocating. Gated by the same WAREBORN_SPAWN_SHIP-adjacent tree/metal
            // philosophy - AfterPlayer, so a misbehaving helm cannot delay a spawn -
            // but always on: it is inert scenery until the client asks for its 1210,
            // and the whole point is to have it there to walk up to.
            registry.Register(Helm());

            // The walkable floor, then the cosmetic parts - all AFTER the hull so
            // its shared entity id is allocated first: each part's 8066 seed and (for
            // the deck) its ship-surface membership name the hull by that id. Same
            // always-on-with-a-kill-switch philosophy as the helm; the deck defaults
            // on because it is the deliverable, the engine/sail off because they are
            // unverified. AfterPlayer throughout, so none can delay a spawn.
            if (includeDeck)
            {
                registry.Register(Deck01());
            }
            if (includeExtraParts)
            {
                registry.Register(ModularEngine());
                registry.Register(Sail01());
            }

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

            if (includeDeposit)
            {
                // The GLOBAL entity FIRST: it carries the biome table the deposit's
                // visualiser blocks on (GetBiomeAt), so without it the rock never draws.
                // Registered before the deposits so its AddEntity - and its 1253/8064 -
                // are in flight first. Only spawned alongside deposits: nothing else in
                // a session needs it, so existing tree/nugget/ship sessions are unchanged.
                registry.Register(GlobalEntity());

                // Default to ONE (the proven deposit) - the coordinate and the
                // runtime-imported variant have never been validated live, so a single
                // anchored rock before the whole table. A WAREBORN_DEPOSIT_COUNT is
                // clamped to [1, full]; a null (unset) means the cautious one, NOT the
                // full set. Index 0 (the proven deposit) is always kept.
                int depositCount = depositCountEnv == null
                    ? 1
                    : SpawnCountPolicy.CountFrom(depositCountEnv, MetalDeposits.HavenPlacements.Count);
                IReadOnlyList<MetalNode> deposits = MetalDeposits.Haven(depositCount);
                foreach (MetalNode node in deposits)
                {
                    registry.Register(DepositEntity(node));
                }

                // ATLAS SHARDS, one lodged in each deposit the spawn rule selects. ALL
                // deposits are registered first (above), so every deposit's shared entity
                // id is already bound when a shard's spawn step resolves its host (the
                // shard's 1305 rockCoreId and the deposit's 2103 attachedEntities are
                // wired from it). Which deposits carry a shard is the DOCUMENTED,
                // deterministic AtlasSpawnPolicy knob (WAREBORN_ATLAS_RATE = one shard per
                // N deposits, default every deposit) - the retail rarity rule is lost, so
                // this is a reconstruction to tune, and index 0 (the proven deposit)
                // always carries one so a tester reliably has a shard. Killable wholesale
                // with WAREBORN_SPAWN_ATLAS=0.
                if (includeAtlasShard)
                {
                    int oneInDeposits = AtlasSpawnPolicy.OneInDeposits(atlasRateEnv);
                    for (int i = 0; i < deposits.Count; i++)
                    {
                        if (AtlasSpawnPolicy.DepositCarriesShard(i, oneInDeposits))
                        {
                            registry.Register(AtlasShardEntity(i, deposits[i].Position));
                        }
                    }
                }
            }

            if (includeFuelPods)
            {
                // FUEL PODS - the gatherable fuel crafting material. Host-less (a pod
                // carries only 2102, no host deposit), so this block is INDEPENDENT of
                // the deposit/atlas spawn above and each pod is its own standalone
                // pickable entity, registered like a tree or a nugget. AfterPlayer,
                // so none can delay a spawn; index 0 (nearest spawn) is always kept.
                int fuelPodCount = FuelPods.CountFrom(fuelPodCountEnv);
                for (int i = 0; i < fuelPodCount; i++)
                {
                    registry.Register(FuelPodEntity(i));
                }
            }

            if (includeDatabank)
            {
                // The scannable databank(s) that feed the KNOWLEDGE loop. Opt-in and
                // AfterPlayer, the same cautious philosophy as the deposit: default to
                // ONE bank at the proven near-spawn vertex, a WAREBORN_DATABANK_COUNT
                // clamps to [1, full]. Nothing else in a session needs it, so existing
                // sessions are unchanged.
                int databankCount = Databanks.CountFrom(databankCountEnv);
                for (int i = 0; i < databankCount; i++)
                {
                    registry.Register(DatabankEntity(i));
                }
            }

            return registry;
        }
    }
}
