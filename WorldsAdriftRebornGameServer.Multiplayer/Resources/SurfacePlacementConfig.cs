namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// Every TUNABLE KNOB the <see cref="SurfacePlacementGenerator"/> reads, in one
    /// reviewed place. Changing the density or spread of a resource field is a
    /// change to these numbers, not to the algorithm - and because the generator is
    /// deterministic, the same config over the same surface always yields the same
    /// layout (so persistence / mining state stays consistent across restarts).
    ///
    /// The defaults here encode WA's own acceptance rules where they are known and
    /// this project's reachability caveat where they are not:
    ///  - <see cref="MinUpwardNormal"/> is the flatness gate WA tested
    ///    (dot(up, normal) &gt; threshold in IslandProxyVisualizer / IslandSurfaceData).
    ///  - <see cref="MinReachableHeightMetres"/>/<see cref="MaxReachableHeightMetres"/>
    ///    is the reachable-ground band: Haven's flattest points sit on the metal
    ///    camp's elevated platforms (local y ~ 40-57 m) a player cannot walk to, so
    ///    "flattest" alone would place resources where nobody can reach them
    ///    (findings-resource-placement.md).
    ///  - <see cref="MinSpacingMetres"/> is deterministic Poisson/farthest-point
    ///    thinning; WA's live sampler did not prove spacing, but spacing avoids
    ///    pile-ups and makes the revival stable.
    ///  - <see cref="TargetCount"/> caps the field size.
    ///  - <see cref="Exclusions"/> keep resources off the spawn, the ship and props.
    /// </summary>
    public sealed class SurfacePlacementConfig
    {
        public SurfacePlacementConfig(
            double minUpwardNormal,
            double minReachableHeightMetres,
            double maxReachableHeightMetres,
            double minSpacingMetres,
            int targetCount,
            IReadOnlyList<PlacementExclusion>? exclusions = null)
        {
            MinUpwardNormal = minUpwardNormal;
            MinReachableHeightMetres = minReachableHeightMetres;
            MaxReachableHeightMetres = maxReachableHeightMetres;
            MinSpacingMetres = minSpacingMetres;
            TargetCount = targetCount;
            Exclusions = exclusions ?? System.Array.Empty<PlacementExclusion>();
        }

        /// <summary>
        /// A surface is accepted only if its normal Y (dot(up, normal)) is at least
        /// this. WA tested <c>&gt; 0.4</c> for loose scatter; the research suggests a
        /// stricter <c>~0.90</c> for a stable deposit that will not clip a slope.
        /// </summary>
        public double MinUpwardNormal { get; }

        /// <summary>Lower bound of the reachable island-local height band, metres.</summary>
        public double MinReachableHeightMetres { get; }

        /// <summary>
        /// Upper bound of the reachable island-local height band, metres. Excludes
        /// the elevated camp platforms a player cannot walk to.
        /// </summary>
        public double MaxReachableHeightMetres { get; }

        /// <summary>
        /// Minimum 3-D distance between any two accepted placements, metres. The
        /// primary density knob: smaller packs the field denser, larger spreads it.
        /// </summary>
        public double MinSpacingMetres { get; }

        /// <summary>
        /// The maximum number of placements to emit, INCLUDING any anchors passed to
        /// <see cref="SurfacePlacementGenerator.Generate"/>. A cap, not a quota: if
        /// the reachable, spaced surface supports fewer, fewer are emitted.
        /// </summary>
        public int TargetCount { get; }

        /// <summary>Lateral keep-out discs (spawn, ship, props). May be empty.</summary>
        public IReadOnlyList<PlacementExclusion> Exclusions { get; }

        /// <summary>Whether a lateral point is inside any exclusion disc.</summary>
        public bool IsExcluded(double x, double z)
        {
            for (int i = 0; i < Exclusions.Count; i++)
            {
                if (Exclusions[i].Contains(x, z))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
