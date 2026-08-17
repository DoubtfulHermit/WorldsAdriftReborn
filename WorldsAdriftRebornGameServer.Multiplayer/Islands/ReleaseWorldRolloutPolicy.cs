namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Opt-in district rollout for the complete release world. "all" selects all
    /// 254 ordinary islands; otherwise a comma-separated list selects exact Bossa
    /// district ids (for example B3,C6) or the two stable unassigned cell ids.
    /// </summary>
    public static class ReleaseWorldRolloutPolicy
    {
        public const string EnvVar = "WAREBORN_RELEASE_WORLD_DISTRICTS";

        public static IReadOnlyList<ReleaseIslandRecord> Select(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("off",
                    StringComparison.OrdinalIgnoreCase))
                return Array.Empty<ReleaseIslandRecord>();
            string[] selectors = raw.Split(',', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
            bool all = selectors.Any(value => value.Equals("all", StringComparison.OrdinalIgnoreCase));
            HashSet<string> selected = new(selectors, StringComparer.OrdinalIgnoreCase);
            return ReleaseWorldCatalog.All.Where(record => all
                    || selected.Contains(record.CellId))
                .OrderBy(record => record.Definition.Id).ToArray();
        }
    }
}
