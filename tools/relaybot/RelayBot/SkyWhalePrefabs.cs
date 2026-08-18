namespace RelayBot
{
    /// <summary>
    /// Which prefab names the server serves as the SKY WHALE, so the harness can
    /// recognise a whale checkout without guessing. Mirrors
    /// <c>SkyWhalePolicy.PrefabName</c> and <c>SkyWhalePolicy.CallPrefabName</c>.
    ///
    /// A whale is not a creature and is deliberately not in
    /// <see cref="IslandFaunaPrefabs"/>: it is a separate feature behind a
    /// separate flag with a separate id band and a separate per-peer budget, and
    /// folding the two together in the harness would make the soak's fauna
    /// numbers stop meaning what they say.
    /// </summary>
    public static class SkyWhalePrefabs
    {
        /// <summary>
        /// The animal. Still named <c>DiscoWhale</c> on the wire because that is
        /// the shipped prefab's name and the client resolves by name - undoing the
        /// joke is the BepInEx mod's job, not the server's.
        /// </summary>
        public static bool IsWhale(string prefabName) => prefabName == "DiscoWhale";

        /// <summary>The invisible caller.</summary>
        public static bool IsCall(string prefabName) => prefabName == "BigCall";

        /// <summary>
        /// What each prefab's visualisers actually read.
        ///
        /// The whale wants exactly ONE component, 190602 TransformState, and that
        /// is the whole point of it: every always-on script on the prefab has no
        /// <c>[Require]</c>, and the inherited ship-part visualiser stack ships
        /// disabled. 190602 is not decoration - the root Rigidbody has gravity, so
        /// without the component the animal free-falls.
        ///
        /// The caller wants 190602 and 4347 BigCallState. Requesting 4347 here is
        /// what turns the soak into a wire-level check that the CALL serializes:
        /// its seeded <c>playAudio</c> is what makes the sound, so an unserved
        /// 4347 is a silent world that still looks correct at the op level.
        /// </summary>
        public static uint[] InterestSetFor(string prefabName) =>
            IsCall(prefabName) ? new[] { 190602u, 4347u } : new[] { 190602u };
    }
}
