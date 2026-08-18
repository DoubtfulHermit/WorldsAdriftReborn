namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Every NUMBER a second evaluator of <see cref="IslandFaunaMovement"/> needs,
    /// read from the movement itself rather than restated.
    ///
    /// This type exists because the operator console draws the wildlife MOVING, and
    /// a 3-second stats file cannot carry motion. The browser therefore evaluates
    /// the same closed form the game server does. That is only honest if the two
    /// agree, and the cheapest way to make them agree is to give the second
    /// evaluator no numbers of its own: every field below is a direct read of a
    /// constant in <see cref="IslandFaunaMovement"/> or
    /// <see cref="IslandFaunaSchool"/>, so retuning a manta's speed or a jelly's
    /// day length moves the console with it and cannot be forgotten.
    ///
    /// What is NOT solved here is the SHAPE of the formulas, which the second
    /// evaluator does have to restate. That is what the console's parity test
    /// guards: it evaluates <see cref="IslandFaunaMovement.LocalPoseAt"/> at fixed
    /// timestamps and asserts the browser's mirror returns the same metres.
    /// </summary>
    public readonly record struct FaunaMapConstants(
        double DayNightCycleSeconds,
        double DayBeginsAtCycleFraction,
        double DayEndsAtCycleFraction,
        double PhaseTransitionFraction,
        double JellyDayRadiusRatio,
        double JellyNightRadiusRatio,
        double JellySecondsPerRevolution,
        double IslandWalkableHeightFraction,
        double MantaVerticalSpanRatio,
        double MantaMetresPerSecond,
        double MantaSchoolRadiusMetres,
        double MantaSchoolVerticalRadiusMetres,
        double JellyShoalRadiusMetres,
        double JellyShoalVerticalRadiusMetres,
        double WeaveRadiansPerSecond,
        double GoldenAngleRadians,
        double GoldenRatioFraction,
        int SchoolsPerIsland,
        // The ecology's compile-time constants (the per-island bloom PARAMETERS
        // travel in the live feed instead - they depend on the game server's
        // seed, which this projection cannot know across the process boundary).
        double MantaCirculationSigmaRatio,
        double JellyCirculationSigmaRatio,
        double MantaOrbitMetresPerSecond,
        double JellyOrbitMetresPerSecond,
        double MaxGroupSpread,
        // The behaviour excursions' shape constants (Phase 4). The per-group
        // (behaviour, epoch) descriptors travel in the live feed.
        double ExcursionRampFraction,
        double FeedRadiusPinch,
        double DiveBelowFloorFraction,
        // The family's geometry (Phase 5). WHICH slots are calves and WHICH
        // adult each trails are seed-derived and travel in the live feed; these
        // two lengths are compile-time and must be read, never restated.
        double CalfTrailMetres,
        double CalfDropMetres);

    /// <summary>
    /// One island's motion geometry, in ISLAND-LOCAL metres, precomputed from its
    /// envelope.
    ///
    /// The split is deliberate: everything that depends on the island's own shape
    /// is derived HERE, by calling <see cref="IslandFaunaMovement"/>'s own accessors,
    /// so a second evaluator never re-derives a half-diagonal or a lap time and
    /// cannot get one subtly wrong. What is left for it is the part that depends on
    /// TIME, which is the part that cannot be precomputed.
    ///
    /// Local rather than world, because that is the frame the console already draws
    /// an island's preserved coastline in: shell points are local metres added to
    /// the MapFile placement. Publishing fauna in the same frame means a creature
    /// is drawn in the correct relationship to the coastline beneath it even where
    /// the running server's own island origin differs from the MapFile's.
    /// </summary>
    public readonly record struct FaunaIslandMotion(
        double CentreX,
        double CentreY,
        double CentreZ,
        double MinY,
        double MaxY,
        double HalfHeightMetres,
        double MantaOrbitRadiusMetres,
        double MantaLapSeconds,
        double JellyLateralRadiusMetres);

    /// <summary>
    /// What lives on one island, by species. Counts are WAREBORN TUNING - the
    /// decompiled client carries no group size anywhere - and every caller that
    /// shows them is expected to say so in words.
    /// </summary>
    public readonly record struct FaunaIslandPopulation(
        int MantaRays,
        int JellyFish,
        int Schools,
        int MantaSchoolSize,
        int JellyShoalSize)
    {
        /// <summary>Every creature on the island, whatever its species.</summary>
        public int Total => MantaRays + JellyFish;
    }

    /// <summary>
    /// The fauna movement model, flattened so something other than this process can
    /// evaluate it.
    ///
    /// Pure, engine-free and total. Nothing here decides anything; it only
    /// re-presents what <see cref="IslandFaunaMovement"/>,
    /// <see cref="IslandFaunaSchool"/> and <see cref="IslandFaunaPolicy"/> already
    /// say, in the one shape a wire and a second evaluator can both use.
    /// </summary>
    public static class IslandFaunaMapModel
    {
        /// <summary>
        /// The model's constants, read straight off the movement. Deliberately a
        /// property built from the real fields rather than a literal table: a
        /// literal would be a second place to change a tuning value, which is
        /// exactly the drift this type exists to prevent.
        /// </summary>
        public static FaunaMapConstants Constants { get; } = new FaunaMapConstants(
            DayNightCycleSeconds: IslandFaunaMovement.DayNightCycleSeconds,
            DayBeginsAtCycleFraction: IslandFaunaMovement.DayBeginsAtCycleFraction,
            DayEndsAtCycleFraction: IslandFaunaMovement.DayEndsAtCycleFraction,
            PhaseTransitionFraction: IslandFaunaMovement.PhaseTransitionFraction,
            JellyDayRadiusRatio: IslandFaunaMovement.JellyDayRadiusRatio,
            JellyNightRadiusRatio: IslandFaunaMovement.JellyNightRadiusRatio,
            JellySecondsPerRevolution: IslandFaunaMovement.JellySecondsPerRevolution,
            IslandWalkableHeightFraction: IslandFaunaMovement.IslandWalkableHeightFraction,
            MantaVerticalSpanRatio: IslandFaunaMovement.MantaVerticalSpanRatio,
            MantaMetresPerSecond: IslandFaunaMovement.MantaMetresPerSecond,
            MantaSchoolRadiusMetres: IslandFaunaSchool.MantaSchoolRadiusMetres,
            MantaSchoolVerticalRadiusMetres: IslandFaunaSchool.MantaSchoolVerticalRadiusMetres,
            JellyShoalRadiusMetres: IslandFaunaSchool.JellyShoalRadiusMetres,
            JellyShoalVerticalRadiusMetres: IslandFaunaSchool.JellyShoalVerticalRadiusMetres,
            WeaveRadiansPerSecond: IslandFaunaSchool.WeaveRadiansPerSecond,
            GoldenAngleRadians: IslandFaunaSchool.GoldenAngleRadians,
            GoldenRatioFraction: IslandFaunaSchool.GoldenRatioFraction,
            SchoolsPerIsland: IslandFaunaPolicy.SchoolsPerIsland,
            MantaCirculationSigmaRatio:
                IslandFaunaEcology.CirculationSigmaRatioFor(FaunaSpecies.MantaRay),
            JellyCirculationSigmaRatio:
                IslandFaunaEcology.CirculationSigmaRatioFor(FaunaSpecies.JellyFish),
            MantaOrbitMetresPerSecond:
                IslandFaunaEcology.OrbitMetresPerSecondFor(FaunaSpecies.MantaRay),
            JellyOrbitMetresPerSecond:
                IslandFaunaEcology.OrbitMetresPerSecondFor(FaunaSpecies.JellyFish),
            MaxGroupSpread: IslandFaunaEcology.MaxGroupSpread,
            ExcursionRampFraction: IslandFaunaBehaviour.ExcursionRampFraction,
            FeedRadiusPinch: IslandFaunaBehaviour.FeedRadiusPinch,
            DiveBelowFloorFraction: IslandFaunaBehaviour.DiveBelowFloorFraction,
            CalfTrailMetres: IslandFaunaFamily.CalfTrailMetres,
            CalfDropMetres: IslandFaunaFamily.CalfDropMetres);

        /// <summary>
        /// One island's precomputed motion geometry. Every field is the movement's
        /// own accessor, called - never a re-derivation of it.
        /// </summary>
        public static FaunaIslandMotion MotionFor(IslandTerrainEnvelope envelope) =>
            new FaunaIslandMotion(
                CentreX: IslandFaunaMovement.CentreXOf(envelope),
                CentreY: IslandFaunaMovement.CentreYOf(envelope),
                CentreZ: IslandFaunaMovement.CentreZOf(envelope),
                MinY: envelope.MinY,
                MaxY: envelope.MaxY,
                HalfHeightMetres: IslandFaunaMovement.HalfHeightOf(envelope),
                MantaOrbitRadiusMetres: IslandFaunaMovement.MantaOrbitRadiusOf(envelope),
                MantaLapSeconds: IslandFaunaMovement.MantaLapSecondsOf(envelope),
                JellyLateralRadiusMetres: IslandFaunaMovement.LateralRadiusOf(envelope));

        /// <summary>
        /// What a tier-<paramref name="tier"/> island carries, by species, from
        /// <see cref="IslandFaunaPolicy"/>. Total for any integer, because the
        /// policy clamps the tier itself.
        /// </summary>
        public static FaunaIslandPopulation PopulationFor(int tier) =>
            new FaunaIslandPopulation(
                MantaRays: IslandFaunaPolicy.MantaCountFor(tier),
                JellyFish: IslandFaunaPolicy.JellyFishCountFor(tier),
                Schools: IslandFaunaPolicy.SchoolsPerIsland,
                MantaSchoolSize: IslandFaunaPolicy.SchoolSizeFor(FaunaSpecies.MantaRay, tier),
                JellyShoalSize: IslandFaunaPolicy.SchoolSizeFor(FaunaSpecies.JellyFish, tier));
    }
}
