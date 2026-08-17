namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHICH PREFAB A SURVEYED SPECIES BECOMES - the join between what the survey
    /// says grew on an island and what this server can safely put there.
    ///
    /// The Cardinal Guild survey records species per island as ordinary English
    /// names ("Palm", "Hemlock", ...), and the vocabulary across all 72 wooded
    /// islands is exactly the eight woods Bossa authored - no ninth name, no
    /// synonym. That is what makes this table a lookup rather than a guess.
    ///
    /// THE SPECIES DATA IS RECOVERED, NOT INVENTED. A tree's wood is a serialized
    /// field on its prefab: <c>TreePreprocessor.woodType</c> is copied onto
    /// <c>TreeFsimVisualizer.woodType</c> at export and written to component 1036
    /// on first enable. There is no biome table and no runtime species selection
    /// anywhere in the client. All 65 shipped tree prefabs were parsed
    /// (docs/research/loop/data/tree_woodtypes.json) and 65/65 landed on one of the
    /// eight woods, so <see cref="TreeSpecies"/> is complete.
    ///
    /// WHY THIS DOES NOT PICK FROM ALL 65. Choosing a prefab is a client-safety
    /// judgement, not a data lookup, and that judgement already exists in
    /// <see cref="WorldEntities.VerifiedSpecies"/>: one prefab per wood, each of
    /// which has its own recovered topology (so the cut mask is the species' own
    /// arithmetic and the tree does not fall apart wrongly), its own recovered
    /// wood, and a MonoBehaviour set identical to <c>Tree</c>'s - which matters
    /// because the client's component batch is <c>failOnComponentInitError: true</c>,
    /// so a single reader id this server does not serve aborts the batch and the
    /// tree comes up broken. <c>TreePalmBlue2</c> is the one prefab excluded, for
    /// requiring <c>TeleportRequestState (190607)</c>.
    ///
    /// So this class deliberately owns no prefab names of its own. It inverts
    /// <c>VerifiedSpecies</c> through <see cref="TreeSpecies.WoodFor"/>, leaving
    /// exactly one source of truth for "is this prefab safe to spawn". Adding a
    /// ninth verified species there is automatically picked up here.
    ///
    /// Pure: dictionaries and string handling. No I/O, no game types.
    /// </summary>
    public static class ReleaseTreeSpecies
    {
        /// <summary>
        /// Wood id -> the client-verified prefab that drops it, derived from
        /// <see cref="WorldEntities.VerifiedSpecies"/> rather than restated.
        /// First entry wins if a wood ever gains a second verified prefab, which
        /// keeps <c>Tree</c> (birch) canonical.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> PrefabByWood = BuildPrefabByWood();

        private static IReadOnlyDictionary<string, string> BuildPrefabByWood()
        {
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (string prefab in WorldEntities.VerifiedSpecies)
            {
                string? wood = TreeSpecies.WoodFor(prefab);
                if (wood != null && !map.ContainsKey(wood))
                {
                    map[wood] = prefab;
                }
            }

            return map;
        }

        /// <summary>The woods this server can actually place a tree for.</summary>
        public static IReadOnlyCollection<string> PlaceableWoods => (IReadOnlyCollection<string>)PrefabByWood.Keys;

        /// <summary>
        /// The verified prefab for a wood, or null if no verified species drops it.
        ///
        /// Null rather than a birch fallback, for the same reason
        /// <see cref="TreeSpecies.WoodFor"/> returns null: a silent substitution is
        /// how an island quietly grows the wrong wood forever. The caller decides.
        /// </summary>
        public static string? PrefabForWood(string? wood)
        {
            if (string.IsNullOrWhiteSpace(wood))
            {
                return null;
            }

            return PrefabByWood.TryGetValue(wood.Trim(), out string? prefab) ? prefab : null;
        }

        /// <summary>
        /// The prefab for tree <paramref name="index"/> on an island whose surveyed
        /// woods are <paramref name="woods"/>.
        ///
        /// Round-robin rather than random: an island the survey says had cedar, elm,
        /// birch and oak gets all four in even measure and gets them in the same
        /// place on every boot, so a player who learns where the oak is keeps being
        /// right. Determinism here is the same property the placement generator
        /// buys with its hash ordering.
        /// </summary>
        public static string? PrefabAt(IReadOnlyList<string> woods, int index)
        {
            if (woods == null || woods.Count == 0 || index < 0)
            {
                return null;
            }

            return PrefabForWood(woods[index % woods.Count]);
        }
    }
}
