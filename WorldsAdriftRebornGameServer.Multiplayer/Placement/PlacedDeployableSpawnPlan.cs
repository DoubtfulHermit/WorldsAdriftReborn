using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// The one place a placed deployable's <see cref="WorldEntity"/> is constructed,
    /// so the runtime confirm-spawn path and the boot restore path CANNOT diverge.
    ///
    /// This is what makes "a restored shipyard is byte-identical to a freshly placed
    /// one" a property of the code rather than a hope: both
    /// <c>Game.Placement.PlacementService.SpawnPlacedDeployable</c> (runtime) and the
    /// boot restore build their registration HERE, from the same
    /// <see cref="DeployableDef"/>, so the asset name, the all-or-nothing seed
    /// component set, the asset context and the AfterPlayer spawn order are the same
    /// on both paths. If they built two WorldEntities by hand, a change to one would
    /// silently drop the other's interest batch on a live client.
    ///
    /// Pure and engine-free, so the seed-set parity is asserted natively.
    /// </summary>
    public static class PlacedDeployableSpawnPlan
    {
        /// <summary>
        /// The registration for one placed instance of <paramref name="def"/> at
        /// <paramref name="position"/>/<paramref name="packedRotation"/>, keyed by
        /// <paramref name="sequence"/>. The key is <c>def.KeyPrefix + ":" + sequence</c>,
        /// unique for the life of the process (the caller owns the monotonic counter);
        /// the seed set is the deployable's own, verbatim.
        /// </summary>
        public static WorldEntity WorldEntityFor(
            DeployableDef def,
            int sequence,
            FixedPointPosition position,
            uint packedRotation)
        {
            if (def == null)
            {
                throw new System.ArgumentNullException(nameof(def));
            }

            return new WorldEntity(
                def.KeyPrefix + ":" + sequence,
                def.AssetName,
                WorldEntities.DefaultAssetContext,
                position,
                seedComponents: def.SeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer,
                packedRotation: packedRotation);
        }
    }
}
