using WorldsAdriftRebornGameServer.Multiplayer.Islands;

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
        public const string DefaultAssetContext = IslandCatalog.DefaultTerrainAssetContext;

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
            return Island(IslandRegistry.CreateDefault().Require(IslandCatalog.HavenId));
        }

        /// <summary>Creates a terrain entity from one registered island definition.</summary>
        public static WorldEntity Island(IslandDefinition island)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }

            return new WorldEntity(
                island.WorldEntityKey,
                island.TerrainAssetName,
                island.TerrainAssetContext,
                island.GlobalOrigin,
                seedComponents: null,
                order: island.SpawnOrder);
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
            IslandCatalog.Haven.LocalToGlobal(208.0, 4.99, 8.0);

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
        /// Trees distributed across the whole island (island-local metres), generated
        /// deterministically from the complete 1431299145 terrain surface with no
        /// altitude band. The former hand table mostly occupied Haven's low eastern
        /// shelf; ridges and the western half were therefore barren. Each seat is
        /// flat, spaced and clear of spawn/ship/deposits by
        /// <see cref="Resources.HavenSurface"/>.
        /// </summary>
        public static readonly IReadOnlyList<(double X, double Y, double Z)> DistributedTreeLocals =
            BuildDistributedTreeLocals();

        private static IReadOnlyList<(double X, double Y, double Z)> BuildDistributedTreeLocals()
        {
            List<(double X, double Y, double Z)> result = new List<(double, double, double)>();
            foreach (Resources.GeneratedPlacement p in Resources.HavenSurface.TreeLocals())
            {
                result.Add((p.LocalX, p.LocalY, p.LocalZ));
            }
            return result;
        }

        /// <summary>
        /// ONE VERIFIED PREFAB PER WOOD - the species the distributed trees cycle
        /// through when species variety is switched on.
        ///
        /// Retail gave every tree type its own wood, and all eight woods are real
        /// items; this is what makes seven of them gatherable instead of one. Each
        /// entry cleared THREE gates before being listed, because a wrong pick is
        /// not a log line, it is a visibly broken tree:
        ///
        /// 1. Its skeleton is recovered and verified (<see cref="TreeTopologies"/>),
        ///    so the mask arithmetic is the species' own.
        /// 2. Its wood is recovered (<see cref="TreeSpecies"/>), so the yield is
        ///    Bossa's, and the eight cover every wood in the catalogue.
        /// 3. Its MonoBehaviour set is IDENTICAL to `Tree`'s - same 23 classes, no
        ///    extras - so its <c>[Require]</c> reader ids are exactly the ten this
        ///    server already serves branches for. That gate is not cosmetic: the
        ///    client's component batch is <c>failOnComponentInitError: true</c>, so
        ///    one uncovered id aborts the batch and the tree comes up broken with
        ///    its break VFX silent.
        ///
        /// 64 of the 65 shipped species pass gate 3. The one that does not is
        /// <c>TreePalmBlue2</c>, which carries <c>LocalTransformTeleportBehaviour</c>
        /// and therefore requires <c>TeleportRequestState (190607)</c> - a component
        /// this server never serves. It is deliberately absent from this list, and
        /// that is the reason.
        ///
        /// `Tree` itself stays first so the birch everyone has already chopped is
        /// still the tree nearest spawn's neighbour.
        /// </summary>
        public static readonly IReadOnlyList<string> VerifiedSpecies = new[]
        {
            Trees.AssetName,          // birch
            "TreePalm1",              // palm
            "TreeStraightBlue",       // ash
            "TreeStraightRed",        // chestnut
            "TreeWonky1Leaf6",        // oak
            "TreeWonky1Leaf3",        // elm
            "TreeDessert2",           // hemlock
            "TreeWonky1LongLeaf2",    // cedar
        };

        /// <summary>
        /// The distributed trees as spawnable entities, keyed tree-0..N.
        ///
        /// With <paramref name="varySpecies"/> false (the default) every one is
        /// `Tree`, exactly as before. With it true they cycle through
        /// <see cref="VerifiedSpecies"/> so all eight woods are gatherable on one
        /// island. The near-spawn <see cref="HavenTree"/> is birch either way - the
        /// one tree every session walks up to does not change behaviour behind a
        /// switch.
        /// </summary>
        public static IEnumerable<WorldEntity> DistributedTrees(bool varySpecies = false)
        {
            int i = 0;
            foreach ((double x, double y, double z) in DistributedTreeLocals)
            {
                string asset = varySpecies
                    ? VerifiedSpecies[i % VerifiedSpecies.Count]
                    : Trees.AssetName;

                yield return new WorldEntity(
                    "tree-" + i++,
                    asset,
                    DefaultAssetContext,
                    IslandCatalog.Haven.LocalToGlobal(x, y, z),
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
        /// invisible even though the entity exists (see <see cref="MetalDeposits.VariantIdFor"/>).
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
        /// <c>deposit-N</c>, and positioned ON its host deposit
        /// (<see cref="AtlasShardCatalogue.LodgedPositionFor(FixedPointPosition)"/>,
        /// offset 0 by default). The VISIBLE embedding in the core is done client-side
        /// by aligning the shard's view to the core's authored ScrapSlots - the retail
        /// alignment is UnityWorker-only, so no server position can stand in for it;
        /// see the remarks on <see cref="AtlasShardCatalogue.DefaultLodgedHeightOffsetMetres"/>.
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
        public static WorldEntity AtlasShardEntity(int index, FixedPointPosition depositPosition) =>
            AtlasShardEntity(MetalDeposits.KeyFor(index), depositPosition);

        /// <summary>
        /// An ATLAS SHARD lodged in the deposit registered under
        /// <paramref name="hostDepositKey"/> - the SOURCE-AGNOSTIC factory.
        ///
        /// Use this for any deposit that is not part of the static Haven table: the
        /// real resource-spawn handshake places deposits the client ground-checked and
        /// keys them <c>handshake-deposit-&lt;island&gt;-&lt;i&gt;</c>, and before this overload
        /// existed nothing could give one of those a shard (shard keys were built from a
        /// bare table index, so only <c>deposit-N</c> could ever be a host). The shard's
        /// key embeds the host's key, so <c>AtlasShardCatalogue.HostKeyOf</c> recovers
        /// the host at registration time without any index arithmetic.
        ///
        /// THE CALLER MUST REGISTER THE HOST DEPOSIT FIRST. Registration resolves the
        /// host by key through <c>BoundEntityIdFor</c>; a shard whose host is not yet
        /// bound is refused (with a warning) rather than given an invalid 1305
        /// rockCoreId, because an invalid host is a shard the client will not render and
        /// nobody can pick up. For a runtime spawner that means: register the deposit
        /// entity, then immediately register <c>AtlasShardEntity(hostKey, hostPosition)</c>.
        /// </summary>
        public static WorldEntity AtlasShardEntity(string hostDepositKey, FixedPointPosition depositPosition)
        {
            return new WorldEntity(
                AtlasShardCatalogue.KeyForHost(hostDepositKey),
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
            return DatabankEntity(Databanks.KeyFor(index), Databanks.PositionAt(index));
        }

        /// <summary>
        /// THE WILDERNESS SHRINE: the Revival Chamber prefab standing on Haven, the
        /// one interactable that takes a player off the tutorial island.
        ///
        /// <see cref="Wilderness.WildernessShrine"/> holds every value and every
        /// piece of provenance; this is only the registration.
        ///
        /// AfterPlayer, and that is not the usual caution - it is required. The
        /// shrine is on Haven's ground but nobody stands ON the shrine, so it has
        /// no claim on being spawned before the player, and anything BeforePlayer
        /// can delay a spawn if it misbehaves.
        ///
        /// It DOES carry a seed batch, unlike the databank next to it, because its
        /// 1210 is the whole point: a client only asks for interest in components
        /// it has a reason to want, and an object with no prompt gives it none.
        /// Both ids in the batch have a ComponentsSerializer branch - see
        /// <see cref="Wilderness.WildernessShrine.SeedComponents"/> on why that is a
        /// rule and not a coincidence.
        /// </summary>
        public static WorldEntity WildernessShrineEntity(IslandDefinition haven)
        {
            return new WorldEntity(
                Wilderness.WildernessShrine.WorldEntityKey,
                Wilderness.WildernessShrine.AssetName,
                DefaultAssetContext,
                Wilderness.WildernessShrine.PositionOn(haven),
                seedComponents: Wilderness.WildernessShrine.SeedComponents,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// THE REVIVAL CHAMBER as SCENERY: the 20 m landmark the shrine stands in.
        ///
        /// <see cref="Wilderness.WildernessChamber"/> holds every value and every
        /// piece of provenance; this is only the registration.
        ///
        /// It carries a ROTATION, which almost nothing else here does: the prefab
        /// has exactly one doorway, on its local +x, and the yaw is what points that
        /// doorway at ground the player can walk in on.
        ///
        /// 190602 ONLY. No 1210 - the prefab's own interaction visualizer is on a
        /// plate at the bottom of a sealed well 11 m under the floor, and seeding
        /// 1210 here would advertise a prompt nobody could ever reach. The one thing
        /// in this room that answers an interact is
        /// <see cref="WildernessShrineEntity"/>.
        ///
        /// AfterPlayer: it is a building, and a building must never be able to delay
        /// somebody's spawn.
        /// </summary>
        public static WorldEntity WildernessChamberEntity(IslandDefinition haven)
        {
            return new WorldEntity(
                Wilderness.WildernessChamber.WorldEntityKey,
                Wilderness.WildernessChamber.AssetName,
                DefaultAssetContext,
                Wilderness.WildernessChamber.PositionOn(haven),
                seedComponents: Wilderness.WildernessChamber.SeedComponents,
                order: SpawnOrder.AfterPlayer,
                packedRotation: Wilderness.WildernessChamber.PackedRotation);
        }

        /// <summary>A scannable databank with an island-specific stable key and pose.</summary>
        public static WorldEntity DatabankEntity(string key, FixedPointPosition position)
        {
            return new WorldEntity(
                key,
                Databanks.AssetName,
                DefaultAssetContext,
                position,
                seedComponents: null,
                order: SpawnOrder.AfterPlayer);
        }

        /// <summary>
        /// A FUEL CANISTER as a <see cref="WorldEntity"/>: the fuel-pod prefab at a
        /// measured Haven surface vertex, keyed <c>fuel-pod-N</c>. It is a SALVAGE
        /// TARGET worked with the gauntlet beam - the same flow as a metal node, NOT a
        /// pickup like the atlas shard. See <see cref="FuelPods"/> for the client
        /// evidence and <see cref="FuelCanisterYield"/> for the recovered 8/8/9 curve.
        ///
        /// NO SEEDED COMPONENTS, exactly like the nugget and the tree: the client checks
        /// the canister out and asks for its 190602/1099/2102/1016 over
        /// SEND_COMPONENT_INTEREST, which ComponentsSerializer answers best-effort. The
        /// 1099 isSalvageable flag (the beam's gate) and the sunk-when-emptied transform
        /// are wired from the fuel-canister ledger. AfterPlayer, so a misbehaving
        /// canister can never delay or break a player's own spawn.
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

        /// <summary>
        /// One LOOT CONTAINER on Haven: a searchable chest of retail scrap.
        ///
        /// NO SEEDED COMPONENTS, the same rule every other resource here follows. A
        /// container needs 1210 InteractiveState AND 1081 InventoryState - the
        /// client's InWorldInventoryVisualiser [Require]s both and will not enable
        /// with either missing - and it ASKS for both over SEND_COMPONENT_INTEREST,
        /// which ComponentsSerializer answers best-effort. A seed batch here would be
        /// our guess at that need in place of the client's own statement of it, and
        /// an all-or-nothing batch at that.
        ///
        /// AfterPlayer, so a misbehaving chest can never delay or break a spawn: it
        /// is inert scenery until the client asks for its 1210.
        /// </summary>
        public static WorldEntity LootContainerEntity(int index)
        {
            return new WorldEntity(
                LootContainers.KeyFor(index),
                LootContainers.AssetName,
                DefaultAssetContext,
                LootContainers.PositionAt(index),
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
                IslandCatalog.Haven.GlobalOrigin,
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
        /// Whether to place the <see cref="FuelPods.HavenPlacements"/> fuel canisters -
        /// the gatherable FUEL crafting material, salvaged with the gauntlet beam. ON by
        /// default with WAREBORN_SPAWN_FUELPODS=0 as the kill switch, on the same
        /// AfterPlayer footing as the tree and node: a misbehaving canister cannot delay
        /// or break a player's spawn, and it grants the real, already-shipping
        /// <c>"fuel"</c> item so it cannot mis-grant. Independent of the deposit/atlas
        /// spawns - a canister is free-standing and needs no deposit.
        /// </param>
        /// <param name="fuelPodCountEnv">
        /// The raw WAREBORN_FUELPOD_COUNT value, or null for the full starter set.
        /// Clamped to [1, all placed]; index 0 is the nearest-spawn pod, so any count
        /// keeps it.
        /// </param>
        /// <param name="includeLootContainers">
        /// Whether to place LOOT CONTAINERS - the searchable chests of retail scrap.
        /// OFF by default (WAREBORN_SPAWN_LOOT=1 to turn it on), for the same reason
        /// the deposit and the databank are: no loot prefab has ever been in front of
        /// a running client on this server, and a prefab that fails to resolve is an
        /// invisible entity with an E prompt on it. AfterPlayer throughout, so a
        /// misbehaving chest cannot delay or break a spawn. When on, this covers BOTH
        /// Haven's hand-tuned seats and every selected release island's generated
        /// ones - a world where only the tutorial island has loot would be worse than
        /// one with none.
        /// </param>
        /// <param name="lootCountEnv">
        /// The raw WAREBORN_LOOT_COUNT value, or null for Haven's full set. Clamped to
        /// [1, all placed]. Affects HAVEN ONLY: the release islands' counts come from
        /// their own surface area through <see cref="Loot.LootBudget"/>, which is the
        /// recovered rule and not something an operator should be able to overrule by
        /// accident.
        /// </param>
        /// <param name="varyTreeSpecies">
        /// Generic support for cycling the eight verified per-species tree prefabs.
        /// Production Haven deliberately passes false because its explicit starter
        /// biome profile is birch; this remains available for a future island whose
        /// recovered per-island data actually names several woods.
        /// </param>
        /// <param name="includeWeatherWalls">
        /// Whether to register the release map's 44 WEATHER WALLS as
        /// <c>WallSegment</c> entities carrying <c>1204 WallSegmentState</c> - the
        /// storm rifts, wind rifts, sand storms and world edge the shipped client
        /// already has every renderer for and has never been given the geometry of.
        /// OFF by default (<c>WAREBORN_WALLS=1</c>), and off is byte-identical on the
        /// wire because nothing is registered at all.
        ///
        /// VISUAL ONLY, structurally: the wall FORCE paths live in
        /// <c>ShipPreprocessor</c>'s <c>UnityWorker</c> branch and are not on our
        /// hulls, so serving 1204 applies zero newtons to anything. See
        /// <see cref="Walls.WallPolicy"/>.
        /// </param>
        /// <param name="wallTypesEnv">
        /// The raw WAREBORN_WALL_TYPES value, or null for every type. The cost lever:
        /// dropping type 1 drops the 11 storm rifts and with them the world-wide
        /// ambient-bolt spawn rate, which is the one part of this feature whose
        /// expense is derived from a formula rather than measured.
        /// </param>
        public static WorldEntityRegistry Default(EntityIdAllocator ids, bool includeProofIsland = false, bool includeTree = true, bool includeMetal = true, bool metalOnlyProven = false, string? treeCountEnv = null, string? oreCountEnv = null, bool includeDeck = true, bool includeExtraParts = false, bool recogniseShip = true, bool includeDeposit = false, string? depositCountEnv = null, bool includeDatabank = false, string? databankCountEnv = null, bool includeAtlasShard = true, string? atlasRateEnv = null, bool includeFuelPods = true, string? fuelPodCountEnv = null, bool varyTreeSpecies = false, bool includeStaticShip = true, bool includeProductionSecondIsland = false, int firstRegionTerrainCount = 0, string? releaseWorldDistricts = null, bool includeWildernessShrine = true, bool includeLootContainers = false, string? lootCountEnv = null, bool includeWeatherWalls = false, string? wallTypesEnv = null)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);
            int terrainCount = FirstRegionTerrainCountPolicy.Clamp(firstRegionTerrainCount);
            IReadOnlyList<ReleaseIslandRecord> releaseIslands =
                ReleaseWorldRolloutPolicy.Select(releaseWorldDistricts);
            IslandRegistry islands = releaseIslands.Count > 0
                ? IslandRegistry.CreateReleaseWorld(releaseWorldDistricts)
                : terrainCount > 0
                ? IslandRegistry.CreateWithFirstRegionTerrain(terrainCount)
                : IslandRegistry.CreateDefault();

            registry.Register(Island(islands.Require(IslandCatalog.HavenId)));

            // A BUILDING CLEARS ITS OWN GROUND. Everything this server scatters on
            // Haven - trees, nuggets, canisters, deposits, databanks - is drawn from
            // the SAME measured LOD0 surface table the Revival Chamber was sited on,
            // so without this a tree grows through its roof and a nugget sits in the
            // middle of its floor. Both happened, and the registration test named
            // them: "the shrine is inside tree-46", then "metal-12 stands inside the
            // Revival Chamber". SKIPPED, not moved: those tables are generated
            // fields and a hand-nudged entry in one would be a lie about where the
            // ground is. An atlas shard is registered straight after its host
            // deposit and shares its position, so skipping the deposit skips it too.
            IslandDefinition chamberHaven = islands.Require(IslandCatalog.HavenId);
            void RegisterClearOfChamber(WorldEntity entity)
            {
                if (includeWildernessShrine
                    && Wilderness.WildernessChamber.Covers(entity.Position, chamberHaven))
                {
                    return;
                }

                registry.Register(entity);
            }

            // Terrain expansion is deliberately independent from the older Trades
            // resource flag. It adds only a bounded, evidenced prefix of release-map
            // terrain; candidate resources remain disabled until each island profile
            // has its own acceptance pass.
            if (includeProductionSecondIsland
                && registry.ByKey(IslandCatalog.TradesChallenge.WorldEntityKey) == null)
                registry.Register(Island(IslandCatalog.TradesChallenge));

            foreach (ReleaseIslandRecord record in releaseIslands)
                if (registry.ByKey(record.Definition.WorldEntityKey) == null)
                    registry.Register(Island(record.Definition));

            int candidateIndex = 0;
            foreach (IslandDefinition candidate in IslandCatalog.FirstRegionTerrain.Skip(1))
            {
                bool selected = candidateIndex < terrainCount;
                if (selected && registry.ByKey(candidate.WorldEntityKey) == null)
                    registry.Register(Island(candidate));
                candidateIndex++;
            }

            if (includeProofIsland)
            {
                registry.Register(ProofIsland());
            }

            // The STATIC test ship (hull + helm + deck + optional cosmetic parts) -
            // the pre-shipbuilding development rig. Now that players build and fly
            // their own ships it is scenery that confuses the world (a second
            // hull+helm standing 50 m from the shipyard), so it is gated as a
            // whole. Every server-side consumer looks it up via nullable
            // ByKey/BoundEntityIdFor, so its absence serves nothing rather than
            // faulting.
            if (includeStaticShip)
            {
                registry.Register(ShipFrame(recogniseShip));
                // The helm goes in right after the hull so the hull's shared entity id
                // is allocated first: the helm's 8066 seed names the hull by that id,
                // and ByEntityId/BoundEntityIdFor must be able to find it without
                // allocating. AfterPlayer, so a misbehaving helm cannot delay a spawn -
                // it is inert scenery until the client asks for its 1210.
                registry.Register(Helm());

                // The walkable floor, then the cosmetic parts - all AFTER the hull so
                // its shared entity id is allocated first: each part's 8066 seed and (for
                // the deck) its ship-surface membership name the hull by that id.
                // AfterPlayer throughout, so none can delay a spawn.
                if (includeDeck)
                {
                    registry.Register(Deck01());
                }
                if (includeExtraParts)
                {
                    registry.Register(ModularEngine());
                    registry.Register(Sail01());
                }
            }

            if (includeTree)
            {
                // Total trees = HavenTree (always, index 0 of the set) + the first
                // (N-1) distributed trees. Clamped to [1, full] so the near-spawn
                // tree can never be dropped and an over-large count cannot overrun.
                int fullTrees = 1 + DistributedTreeLocals.Count;
                int treeTotal = SpawnCountPolicy.CountFrom(treeCountEnv, fullTrees);

                registry.Register(HavenTree());
                foreach (WorldEntity tree in DistributedTrees(varyTreeSpecies).Take(treeTotal - 1))
                {
                    RegisterClearOfChamber(tree);
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
                    RegisterClearOfChamber(MetalNodeEntity(node));
                }
            }

            // The biome lookup table is world-wide and required by deposits on
            // either island. Register it exactly once, before every deposit.
            if (includeDeposit || includeProductionSecondIsland
                || releaseIslands.Any(record => record.Deposits.Count > 0))
            {
                registry.Register(GlobalEntity());
            }

            if (includeDeposit)
            {
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
                    RegisterClearOfChamber(DepositEntity(node));
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
                            RegisterClearOfChamber(AtlasShardEntity(i, deposits[i].Position));
                        }
                    }
                }
            }

            if (includeProductionSecondIsland && releaseIslands.Count == 0)
            {
                // The Trades Challenge's recovered community row is unusually exact:
                // Aluminium quality 4, five deposits (98 cells * retail 0.05 density),
                // five databanks, and NO trees. Populate only that evidence-backed
                // profile; do not copy Haven's birch/iron/fuel starter biome across.
                IReadOnlyList<Resources.GeneratedPlacement> tradesDeposits =
                    Resources.TradesChallengeResources.DepositLocals();
                for (int i = 0; i < tradesDeposits.Count; i++)
                {
                    MetalNode node = Resources.TradesChallengeResources.DepositByKey(
                        Resources.TradesChallengeResources.DepositKeyFor(i))!;
                    RegisterClearOfChamber(DepositEntity(node));
                    if (includeAtlasShard)
                    {
                        RegisterClearOfChamber(AtlasShardEntity(node.Key, node.Position));
                    }
                }

                for (int i = 0; i < Resources.TradesChallengeResources.DatabankCount; i++)
                {
                    RegisterClearOfChamber(DatabankEntity(
                        Resources.TradesChallengeResources.DatabankKeyFor(i),
                        Resources.TradesChallengeResources.DatabankPositionAt(i)));
                }
            }

            // Complete release-world population. Counts and metal tables come from
            // the joined survey; positions come from each island's extracted surface.
            // Trees ARE included as of the 2026-08-18 pass: the species-specific
            // acceptance an earlier comment here deferred is done (all eight woods
            // have recovered topology), so the 72 islands the survey records as
            // wooded grow their own species. See ReleaseWorldTrees. Deposits keep
            // their retail 0.05-per-cell density and databanks their exact surveyed
            // count; trees came from a separate placement pass because the survey
            // records species but never a count.
            //
            // Each deposit's ATLAS SHARD is registered IMMEDIATELY AFTER its host, as
            // AtlasShardEntity requires: registration resolves the host by key through
            // BoundEntityIdFor, so a shard whose deposit is not yet bound is refused.
            // Without this a release-world deposit mined out to nothing - the metal
            // arrived but the shard that is the loop's payoff could never exist, the
            // same gap the source-agnostic overload was added to close for the
            // handshake deposits. The rate is applied to each ISLAND's own deposit
            // index, so every island with any metal reliably has at least one shard
            // whatever WAREBORN_ATLAS_RATE says, and WAREBORN_SPAWN_ATLAS=0 still
            // kills the whole population.
            int releaseOneInDeposits = AtlasSpawnPolicy.OneInDeposits(atlasRateEnv);
            foreach (ReleaseIslandRecord island in releaseIslands)
            {
                for (int i = 0; i < island.Deposits.Count; i++)
                {
                    MetalNode deposit = island.Deposits[i];
                    RegisterClearOfChamber(DepositEntity(deposit));
                    if (includeAtlasShard
                        && AtlasSpawnPolicy.DepositCarriesShard(i, releaseOneInDeposits))
                    {
                        RegisterClearOfChamber(AtlasShardEntity(deposit.Key, deposit.Position));
                    }
                }
                for (int i = 0; i < island.Databanks.Count; i++)
                    RegisterClearOfChamber(DatabankEntity(
                        Resources.ReleaseWorldResources.DatabankKeyFor(island, i),
                        island.Databanks[i]));
                foreach (WorldEntity tree in Islands.ReleaseWorldTrees.For(island))
                    registry.Register(tree);
                if (includeLootContainers)
                    foreach (WorldEntity container in Islands.ReleaseWorldLoot.For(island))
                        registry.Register(container);
            }

            if (includeFuelPods)
            {
                // FUEL CANISTERS - the gatherable fuel crafting material, salvaged with
                // the gauntlet beam. Free-standing, so this block is INDEPENDENT of the
                // deposit/atlas spawn above and each canister is its own standalone
                // salvage target, registered like a tree or a nugget. AfterPlayer, so
                // none can delay a spawn; index 0 (nearest spawn) is always kept.
                int fuelPodCount = FuelPods.CountFrom(fuelPodCountEnv);
                for (int i = 0; i < fuelPodCount; i++)
                {
                    RegisterClearOfChamber(FuelPodEntity(i));
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
                    RegisterClearOfChamber(DatabankEntity(i));
                }
            }

            if (includeLootContainers)
            {
                // LOOT CONTAINERS on Haven. Hand-tuned count, unlike the release
                // islands' - see HavenSurface.LootTargetCount. RegisterClearOfChamber
                // rather than registry.Register, because a chest inside the Revival
                // Chamber is the same bug that put a tree through its roof and a
                // nugget on its floor; a container is not exempt just because it is
                // small.
                int lootCount = LootContainers.CountFrom(lootCountEnv);
                for (int i = 0; i < lootCount; i++)
                {
                    RegisterClearOfChamber(LootContainerEntity(i));
                }
            }

            // The exit from Haven. Registered LAST and unconditionally on the island
            // itself, not behind the release-world flag: whether the Wilderness is
            // open tonight is a question the shrine ANSWERS, not one that decides
            // whether it exists. A door that says "not tonight" is better than a
            // missing door, because the second one reads as a bug.
            if (includeWildernessShrine)
            {
                // The CHAMBER first, then the shrine that stands inside it. Order is
                // cosmetic (both are AfterPlayer and neither depends on the other's
                // entity id) but it is the order they read in, and it keeps the
                // spawn plan legible.
                registry.Register(WildernessChamberEntity(islands.Require(IslandCatalog.HavenId)));
                registry.Register(WildernessShrineEntity(islands.Require(IslandCatalog.HavenId)));
            }

            // THE WEATHER WALLS. Registered LAST, and not through
            // RegisterClearOfChamber: a wall is a region boundary tens of kilometres
            // long with no collider, it sits on no ground and nothing can grow
            // through it. Nothing else in the plan depends on a wall's entity id, and
            // going last means every existing entity keeps the id it had, so a run
            // with walls and a run without are directly comparable.
            foreach (WorldEntity wall in Walls.WorldWalls.All(includeWeatherWalls, wallTypesEnv))
            {
                registry.Register(wall);
            }

            return registry;
        }
    }
}
