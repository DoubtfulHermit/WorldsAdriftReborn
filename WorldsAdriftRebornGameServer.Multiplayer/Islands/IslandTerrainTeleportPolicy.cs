namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    public enum TerrainTeleportDecision { Send, Wait, Refuse }

    /// <summary>Pure safety decision for teleporting onto optional terrain.</summary>
    public static class IslandTerrainTeleportPolicy
    {
        public static TerrainTeleportDecision Decide(
            bool terrainManaged,
            bool destinationKnown,
            bool terrainReady,
            bool waitExpired)
        {
            if (!terrainManaged) return TerrainTeleportDecision.Send;
            if (!destinationKnown || waitExpired) return TerrainTeleportDecision.Refuse;
            return terrainReady ? TerrainTeleportDecision.Send : TerrainTeleportDecision.Wait;
        }

        public static bool WaitExpired(TimeSpan now, TimeSpan deadline) => now >= deadline;
    }
}
