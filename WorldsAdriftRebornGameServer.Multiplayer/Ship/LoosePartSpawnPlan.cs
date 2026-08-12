namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A loose part's world-entity registration, built in ONE pure place - the
    /// counterpart of <see cref="BuiltShipSpawnPlan"/> for parts. The impure spawner
    /// (<c>Game.Crafting.LoosePartSpawner</c>) allocates the entity id and
    /// broadcasts; this half only assembles the <see cref="WorldEntity"/> from the
    /// part definition and position, so its asset name and its all-or-nothing seed
    /// set can be asserted natively.
    ///
    /// AfterPlayer: nobody stands on a loose part, so it never delays a player's
    /// loading screen; and being AfterPlayer means a misbehaving part can never break
    /// a player's own spawn.
    /// </summary>
    public static class LoosePartSpawnPlan
    {
        /// <summary>
        /// The registration for a loose <paramref name="part"/> at
        /// <paramref name="position"/>, keyed by <paramref name="sequence"/>. Asset
        /// name is the part's prefab; the seed set is the part's own
        /// <see cref="LoosePartDefinition.SeedComponents"/> (ShipPartVisualizer's
        /// requires + the part-specific functional ids). <paramref name="packedRotation"/>
        /// is the 190602 localRotation seed - identity for a freshly-crafted loose part
        /// (it chose no facing), threaded through only so a RESTORED part reproduces the
        /// exact rotation it was persisted with.
        /// </summary>
        public static WorldEntity For(int sequence, FixedPointPosition position, LoosePartDefinition part,
            uint packedRotation = Placement.Quaternion32Packing.Identity)
        {
            return new WorldEntity(
                LoosePartPlacement.Key(sequence, part.SchematicId),
                part.PrefabName,
                WorldEntities.DefaultAssetContext,
                position,
                seedComponents: part.SeedComponents,
                order: SpawnOrder.AfterPlayer,
                packedRotation: packedRotation);
        }
    }
}
