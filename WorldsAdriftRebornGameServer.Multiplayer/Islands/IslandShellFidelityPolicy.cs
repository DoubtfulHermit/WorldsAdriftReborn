namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Which distant-shell construction one island is asked for.</summary>
    public enum IslandShellFidelity
    {
        /// <summary>
        /// v1 <see cref="IslandDistantShellProtocol.Request"/>. The client clones the
        /// island's own lowest retail terrain LOD out of the cached island bundle, so
        /// the silhouette is real island geometry carrying the retail
        /// <c>GenerateDynamicMaterial</c>. It costs one bundle prefetch per island.
        /// </summary>
        RetailLod,

        /// <summary>
        /// v2 <see cref="IslandDistantShellProtocol.ProceduralRequest"/>. The client
        /// builds a prism from the catalogue's radial outline and loads no island
        /// bundle. It scales to every registered island and carries no terrain
        /// silhouette detail or retail material.
        /// </summary>
        CompactOutline,
    }

    /// <summary>
    /// Chooses the distant-shell fidelity for one island.
    ///
    /// v1 retail LOD is the PREFERENCE. v2 compact outline is a scalability
    /// fallback: it exists because the complete release-world rollout registers 254
    /// islands and 254 island-bundle prefetches per peer are not affordable. Where
    /// the managed terrain set is bounded, the prefetch is affordable and the
    /// island-shaped shell is the better answer.
    ///
    /// Release-catalogue membership alone must NOT select v2. Every bounded-rollout
    /// island is a record in the same 254-island catalogue, so keying the decision
    /// on membership would downgrade the bounded configuration as an incidental
    /// side effect of the catalogue being embedded. The rollout flag is therefore
    /// an explicit input.
    ///
    /// A near-band fidelity upgrade (replacing an already-built v2 shell with a v1
    /// mesh as a viewer approaches) is deliberately NOT modelled here: the client
    /// dedups shells by terrain entity id and both entry points early-return with
    /// <c>SendReadyAgain</c>, so an upgrade needs a client teardown/rebuild path
    /// that does not exist yet. When it does, that decision belongs in this type as
    /// a distance-aware overload; nothing else needs to move.
    /// </summary>
    public static class IslandShellFidelityPolicy
    {
        /// <summary>
        /// The outline sizes <see cref="IslandDistantShellProtocol.ProceduralRequest"/>
        /// will encode and <c>TryParseProceduralRequest</c> will accept.
        /// </summary>
        public const int MinimumOutlinePoints = 3;
        public const int MaximumOutlinePoints = 32;

        /// <summary>
        /// <paramref name="release"/> is null when the island has no release-world
        /// record, which means there is no outline to encode and v1 is the only
        /// possible answer.
        /// </summary>
        public static IslandShellFidelity Choose(
            ReleaseIslandRecord? release, bool releaseWorldRolloutActive) =>
            Choose(release == null ? 0 : release.Shell.Count, releaseWorldRolloutActive);

        /// <summary>
        /// The whole decision, expressed over the only two facts it depends on.
        /// <paramref name="outlinePointCount"/> is zero when no catalogue record
        /// exists.
        /// </summary>
        public static IslandShellFidelity Choose(
            int outlinePointCount, bool releaseWorldRolloutActive)
        {
            // No encodable outline: v2 is not merely unpreferred, it is impossible.
            if (!IsEncodableOutline(outlinePointCount))
                return IslandShellFidelity.RetailLod;

            // Bounded managed terrain: prefer the island's real retail LOD.
            if (!releaseWorldRolloutActive)
                return IslandShellFidelity.RetailLod;

            // Complete rollout: 254 bundle prefetches are not affordable.
            return IslandShellFidelity.CompactOutline;
        }

        public static bool IsEncodableOutline(int outlinePointCount) =>
            outlinePointCount >= MinimumOutlinePoints
            && outlinePointCount <= MaximumOutlinePoints;

        /// <summary>
        /// The record a <see cref="IslandShellFidelity.CompactOutline"/> choice must
        /// encode. It throws rather than let a caller emit a v2 marker the client
        /// cannot build; <see cref="Choose(ReleaseIslandRecord?, bool)"/> can never
        /// reach that state, and this makes the invariant enforced rather than
        /// merely documented.
        /// </summary>
        public static ReleaseIslandRecord RequireOutline(ReleaseIslandRecord? release)
        {
            if (!IsEncodableOutline(release == null ? 0 : release.Shell.Count))
                throw new InvalidOperationException(
                    "a compact-outline shell needs a release-world record carrying "
                    + MinimumOutlinePoints + ".." + MaximumOutlinePoints
                    + " outline points");
            return release!;
        }
    }
}
