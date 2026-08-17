namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>The small set of island facts the current Haven pipeline uses.</summary>
    public sealed class IslandDefinition
    {
        public IslandDefinition(
            IslandId id,
            string displayName,
            string worldEntityKey,
            FixedPointPosition globalOrigin,
            string terrainAssetName,
            string terrainAssetContext,
            SpawnOrder spawnOrder)
        {
            if (string.IsNullOrWhiteSpace(id.Value))
                throw new ArgumentException("an island definition must have a non-empty id", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("an island display name must not be empty", nameof(displayName));
            if (string.IsNullOrWhiteSpace(worldEntityKey))
                throw new ArgumentException("an island world-entity key must not be empty", nameof(worldEntityKey));
            if (string.IsNullOrWhiteSpace(terrainAssetName))
                throw new ArgumentException("an island terrain asset name must not be empty", nameof(terrainAssetName));
            if (string.IsNullOrWhiteSpace(terrainAssetContext))
                throw new ArgumentException("an island terrain asset context must not be empty", nameof(terrainAssetContext));

            Id = id;
            DisplayName = displayName;
            WorldEntityKey = worldEntityKey;
            GlobalOrigin = globalOrigin;
            TerrainAssetName = terrainAssetName;
            TerrainAssetContext = terrainAssetContext;
            SpawnOrder = spawnOrder;
        }

        public IslandId Id { get; }
        public string DisplayName { get; }
        public string WorldEntityKey { get; }
        public FixedPointPosition GlobalOrigin { get; }
        public string TerrainAssetName { get; }
        public string TerrainAssetContext { get; }
        public SpawnOrder SpawnOrder { get; }

        /// <summary>
        /// Converts local metres to global Q52.12. Each local axis is truncated
        /// before addition, exactly matching the client and pre-registry pipeline.
        /// </summary>
        public FixedPointPosition LocalToGlobal(double localX, double localY, double localZ)
        {
            return new FixedPointPosition(
                GlobalOrigin.X + (long)(localX * FixedPointPosition.UnitsPerMetre),
                GlobalOrigin.Y + (long)(localY * FixedPointPosition.UnitsPerMetre),
                GlobalOrigin.Z + (long)(localZ * FixedPointPosition.UnitsPerMetre));
        }
    }
}
