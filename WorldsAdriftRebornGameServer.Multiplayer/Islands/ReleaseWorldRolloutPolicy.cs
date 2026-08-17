namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Opt-in district rollout for the complete release world. "all" selects all
    /// 254 ordinary islands; otherwise a comma-separated list selects exact Bossa
    /// district ids (for example B3,C6), the two stable unassigned cell ids, or a
    /// NAMED TIER (tier1/t1/wilderness, tier2..tier4).
    ///
    /// A tier selector resolves from each record's own MapFile <c>cellTier</c>, so
    /// naming a region cannot drift if a future catalogue regeneration moves an
    /// island between cells. It is the map's geography - which cell a player is
    /// standing in - not the community survey's per-island tier, which disagrees
    /// for exactly one island (Holy Ruins: surveyed Tier 3, placed in Bossa's
    /// Tier-2 A4 cell). At the time of writing tier1 is exactly A2,A3,B2,B3 and
    /// those four cells contain nothing else; ReleaseWorldTierSelectionTests pins
    /// both halves of that so the equivalence cannot silently break.
    ///
    /// Selectors compose: "tier1,C6" is the Wilderness plus one tier-3 cell.
    /// </summary>
    public static class ReleaseWorldRolloutPolicy
    {
        public const string EnvVar = "WAREBORN_RELEASE_WORLD_DISTRICTS";

        /// <summary>The lowest and highest MapFile cell tier a tier selector may name.</summary>
        public const int MinTier = 1;
        public const int MaxTier = 4;

        /// <summary>
        /// The MapFile cell tier a selector names, or null when it is not a tier
        /// selector. Accepts "tierN", "tN" and the one authored alias the world
        /// actually uses in conversation, "wilderness" (tier 1).
        /// </summary>
        public static int? TierOf(string? selector)
        {
            if (string.IsNullOrWhiteSpace(selector)) return null;
            string value = selector.Trim();
            if (value.Equals("wilderness", StringComparison.OrdinalIgnoreCase)) return 1;

            string digits =
                value.StartsWith("tier", StringComparison.OrdinalIgnoreCase) ? value.Substring(4)
                : value.StartsWith("t", StringComparison.OrdinalIgnoreCase) ? value.Substring(1)
                : string.Empty;
            if (digits.Length == 0 || !int.TryParse(digits, out int tier)) return null;
            return tier >= MinTier && tier <= MaxTier ? tier : null;
        }

        public static IReadOnlyList<ReleaseIslandRecord> Select(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("off",
                    StringComparison.OrdinalIgnoreCase))
                return Array.Empty<ReleaseIslandRecord>();
            string[] selectors = raw.Split(',', StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries);
            bool all = selectors.Any(value => value.Equals("all", StringComparison.OrdinalIgnoreCase));
            HashSet<string> selected = new(selectors, StringComparer.OrdinalIgnoreCase);
            HashSet<int> tiers = new(selectors.Select(TierOf).Where(tier => tier != null)
                .Select(tier => tier!.Value));
            return ReleaseWorldCatalog.All.Where(record => all
                    || selected.Contains(record.CellId)
                    || tiers.Contains(record.CellTier))
                .OrderBy(record => record.Definition.Id).ToArray();
        }
    }
}
