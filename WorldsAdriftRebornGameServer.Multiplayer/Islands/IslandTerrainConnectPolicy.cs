namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Connect-plan partition for terrain owned by continuous interest.</summary>
    public static class IslandTerrainConnectPolicy
    {
        /// <summary>
        /// Haven remains the mandatory pre-player ground. Only optional AfterPlayer
        /// island roots move from the immutable connect plan to continuous interest.
        /// Disabled mode returns false for every island and therefore preserves the
        /// legacy plan byte-for-byte.
        /// </summary>
        public static bool IsManaged(bool terrainInterestEnabled, IslandDefinition? island) =>
            terrainInterestEnabled
            && island != null
            && island.Id != IslandCatalog.HavenId
            && island.SpawnOrder == SpawnOrder.AfterPlayer;

        public static bool IsInitial(
            bool baseInitial,
            bool managedTerrain,
            FixedPointPosition spawnPosition,
            IslandDefinition? island,
            double loadRadiusMetres) =>
            managedTerrain
                ? island != null
                    && IslandTerrainEnvelopes.Require(island.Id)
                        .DistanceSquaredTo(spawnPosition, island)
                        <= loadRadiusMetres * loadRadiusMetres
                : baseInitial;
    }
}
