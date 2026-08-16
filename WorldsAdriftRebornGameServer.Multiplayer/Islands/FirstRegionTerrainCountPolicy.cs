namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Bounds the optional, after-player prefix of
    /// <see cref="IslandCatalog.FirstRegionTerrain"/> without changing production
    /// startup by itself. Missing or invalid configuration safely selects none.
    /// </summary>
    public static class FirstRegionTerrainCountPolicy
    {
        public const int MaximumOptionalTerrain = 4;

        public static int Clamp(int count)
        {
            if (count < 0)
                return 0;
            return count > MaximumOptionalTerrain ? MaximumOptionalTerrain : count;
        }

        public static int CountFrom(string? configuredCount)
        {
            return int.TryParse(configuredCount, out int count) ? Clamp(count) : 0;
        }
    }
}
