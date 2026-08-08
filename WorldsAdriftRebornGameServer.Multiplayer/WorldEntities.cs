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
        public static WorldEntityRegistry Default(EntityIdAllocator ids, bool includeProofIsland = false, bool includeTree = true)
        {
            WorldEntityRegistry registry = new WorldEntityRegistry(ids);

            registry.Register(Island());

            if (includeProofIsland)
            {
                registry.Register(ProofIsland());
            }

            if (includeTree)
            {
                registry.Register(HavenTree());
            }

            return registry;
        }
    }
}
