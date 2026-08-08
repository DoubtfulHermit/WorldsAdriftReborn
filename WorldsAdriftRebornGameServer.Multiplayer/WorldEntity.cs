namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Where a world entity is spawned relative to the joining player's own
    /// avatar.
    /// </summary>
    public enum SpawnOrder
    {
        /// <summary>
        /// Before the player entity exists. Use this ONLY for something the
        /// player must be able to stand on, because it costs the player a bundle
        /// load's worth of loading screen: the island's colliders have to be
        /// there before the player's 190602 is published, or the player is placed
        /// over geometry that has not streamed in and falls forever (no fall
        /// damage on this server, no WorldEdgePushback).
        /// </summary>
        BeforePlayer,

        /// <summary>
        /// After the player entity exists. The default, and what a tree, a
        /// decoration or a not-yet-boarded ship wants: nothing about them is
        /// load-bearing for the player's footing, and making the player wait on
        /// their bundles just lengthens the loading screen.
        /// </summary>
        AfterPlayer,
    }

    /// <summary>
    /// ONE thing this server puts in the world that is not a player: the island,
    /// a tree, a ship hull, a crafting station. It is a registration - a
    /// description of an entity - not the entity itself; the entity id is
    /// allocated later, once, by <see cref="WorldEntityRegistry"/>.
    ///
    /// WHY THIS TYPE EXISTS. Before it, "which entity is this and where does it
    /// go" was answered by a single boolean question - is this id the island? -
    /// asked inside the component serializer, and the answer to "what asset is
    /// it" was a bare string constant. That works for exactly two kinds of thing.
    /// Everything queued behind this seam (a choppable tree, a static ship frame,
    /// a shipyard) is a third, fourth and fifth kind, each with its own prefab,
    /// its own position and its own set of components that must be seeded before
    /// the client will treat it as anything but scenery.
    ///
    /// Nothing here talks to ENet or to the game's assemblies, so a registration
    /// can be asserted on in a unit test rather than by staring at a game client.
    /// </summary>
    public sealed class WorldEntity
    {
        /// <param name="key">
        /// Stable identity for the registration, used as the shared-entity-id key
        /// (see <see cref="EntityIdAllocator.SharedEntityId"/>). It must be stable
        /// for the life of the process and unique within a registry; it is never
        /// sent on the wire.
        /// </param>
        /// <param name="assetName">
        /// The prefab/bundle name. It goes on the wire TWICE - in the
        /// AssetLoadRequestOp and in the AddEntityOp - and for an island a third
        /// time inside 1041 IslandState. A mismatch between those means the client
        /// is told to place something it never loaded, so they read one field.
        ///
        /// Send the BARE name. The client appends the worker suffix itself
        /// (WorkerSpecificPrefabName.GetWorkerSpecificPrefabName), so "Tree" is
        /// correct and "tree_unityclient" is not.
        /// </param>
        /// <param name="assetContext">
        /// The prefab context. "notNeeded?" is what the island has always sent and
        /// is the right answer for anything with a single variant; a prefab with
        /// per-worker variants (the Traveller's "Default" vs "Player") needs the
        /// real one.
        /// </param>
        /// <param name="position">
        /// The 190602 TransformState.localPosition seed. This is the ONLY thing
        /// that places anything in this world, it is consumed once at OnEnable,
        /// and it must never be re-sent to a live entity - for an island
        /// IslandLocalTransformVisualizer does not teleport, it starts a 5-second
        /// smoothstep slide that drags the terrain out from under everyone on it.
        /// </param>
        /// <param name="seedComponents">
        /// Components the server PUSHES immediately after the AddEntityOp, without
        /// waiting to be asked. Empty is a perfectly good answer and is what the
        /// island uses: the client checks the entity out and asks for what it
        /// wants over SEND_COMPONENT_INTEREST.
        ///
        /// If you do list ids: the push goes out with failOnComponentInitError
        /// TRUE, so a single id with no branch in ComponentsSerializer drops the
        /// ENTIRE batch and yields a fully-rendered, completely inert entity. The
        /// only warning is one "[error] failed to initialize component NNNN" line
        /// in the server log - which is why SendOPHelper now prints the whole
        /// requested list next to it.
        /// </param>
        /// <param name="order">See <see cref="SpawnOrder"/>. Defaults to AfterPlayer.</param>
        public WorldEntity(
            string key,
            string assetName,
            string assetContext,
            FixedPointPosition position,
            IReadOnlyList<uint>? seedComponents = null,
            SpawnOrder order = SpawnOrder.AfterPlayer)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("a world entity needs a stable key", nameof(key));
            }
            if (string.IsNullOrWhiteSpace(assetName))
            {
                throw new ArgumentException("a world entity needs an asset name; it is sent on the wire", nameof(assetName));
            }
            if (assetContext == null)
            {
                throw new ArgumentNullException(nameof(assetContext));
            }

            Key = key;
            AssetName = assetName;
            AssetContext = assetContext;
            Position = position;
            SeedComponents = seedComponents ?? Array.Empty<uint>();
            Order = order;
        }

        /// <summary>Stable registration identity. Never sent on the wire.</summary>
        public string Key { get; }

        /// <summary>The bare prefab/bundle name, sent in both the asset request and the AddEntityOp.</summary>
        public string AssetName { get; }

        /// <summary>The prefab context sent alongside <see cref="AssetName"/>.</summary>
        public string AssetContext { get; }

        /// <summary>The 190602 localPosition seed. Sent once, never re-sent.</summary>
        public FixedPointPosition Position { get; }

        /// <summary>Components pushed unprompted right after the AddEntityOp. Usually empty.</summary>
        public IReadOnlyList<uint> SeedComponents { get; }

        /// <summary>Whether this is spawned before or after the joining player's own avatar.</summary>
        public SpawnOrder Order { get; }

        public override string ToString()
        {
            return Key + " (" + AssetName + "@" + AssetContext + ") at " + Position
                + (SeedComponents.Count > 0 ? ", " + SeedComponents.Count + " seeded component(s)" : "");
        }
    }
}
