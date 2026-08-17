using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>Stable lookup seam for compact release-world resource placements.</summary>
    public static class ReleaseWorldResources
    {
        public static MetalNode? DepositByKey(string? key) =>
            ReleaseWorldCatalog.DepositByKey(key);

        public static string DatabankKeyFor(ReleaseIslandRecord island, int index) =>
            "databank-release-" + island.Survey.WorkshopId + "-" + index;
    }
}
