namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Which half of the fauna day a creature is currently living in.</summary>
    public enum FaunaDayPhase
    {
        /// <summary>Jellies push outward and sink toward the bounds floor.</summary>
        Day,

        /// <summary>Jellies orbit inside and are drawn back toward the island centre.</summary>
        Night,
    }

    /// <summary>
    /// WHERE A CREATURE IS, as a closed-form function of (creature, elapsed seconds).
    ///
    /// Retail's fauna motion belonged to the UnityWorker: real steering, real
    /// physics, real terrain queries (acs/Assets.Scripts.PrefabExporting.Preprocessors/
    /// CreaturePreprocessor.cs and JellyFishPreprocessor.cs install movement conduct
    /// only on that worker). None of that is preserved and none of it can run here,
    /// so the shapes below are ANALYTICAL reconstructions of the paths the decompiled
    /// visualisers describe - not a simulation of them.
    ///
    /// EVERYTHING IS A PURE FUNCTION, and that is the load-bearing property rather
    /// than a stylistic one. There is no Random, no DateTime, no integration and no
    /// remembered previous pose, so:
    /// <list type="bullet">
    /// <item>every peer watching the same creature is told the same position, because
    ///   the position is derived rather than sampled;</item>
    /// <item>a RESTARTED server replays the identical path for the identical elapsed
    ///   sequence, so a reconnecting player does not see the world's wildlife jump;</item>
    /// <item>a creature that nobody watched for ten minutes is exactly where it would
    ///   have been had somebody watched the whole time - no drift, no catch-up.</item>
    /// </list>
    ///
    /// GEOMETRY IS DERIVED AS RATIOS OF THE ISLAND'S OWN ENVELOPE, never as absolute
    /// metres. An orbit radius of "320 m" would put a manta INSIDE the rock on a
    /// 600 m island and a kilometre off the perimeter of a 40 m one. Taking the
    /// radius from the envelope's own lateral extent means a tiny island, a huge
    /// island and a long thin anisotropic island each get an orbit that clears their
    /// own geometry by the same proportion.
    /// </summary>
    public static class IslandFaunaMovement
    {
        /// <summary>
        /// Degrees of orbit advanced per step. RECOVERED from acs/PatrolVisualiser.cs,
        /// which advanced a manta's patrol target around the island in roughly
        /// ten-degree increments.
        /// </summary>
        public const double MantaOrbitStepDegrees = 10.0;

        /// <summary>
        /// Seconds a manta spends per <see cref="MantaOrbitStepDegrees"/> step.
        /// WAREBORN TUNING: retail's patrol advanced when the previous target was
        /// reached, which depended on physics this server does not have. Four seconds
        /// per ten degrees makes one lap take 144 s, which reads as a slow patrol
        /// rather than a fairground ride.
        /// </summary>
        public const double MantaSecondsPerOrbitStep = 4.0;

        /// <summary>
        /// How far outside the island's lateral bounding radius a manta flies, as a
        /// RATIO of that radius. RECOVERED direction (acs/PatrolVisualiser.cs targeted
        /// a point JUST BEYOND the lateral bounding radius); the 1.15 magnitude is
        /// WAREBORN TUNING, since the retail margin depended on a runtime bounds query.
        /// </summary>
        public const double MantaRadiusRatio = 1.15;

        /// <summary>
        /// The smallest clearance, in metres, between the island's lateral radius and
        /// the manta's orbit. WAREBORN TUNING. A pure ratio collapses on a tiny
        /// island - 15% of 8 m is barely over a metre - so a floor keeps the creature
        /// visibly off the rock at every island size.
        /// </summary>
        public const double MantaMinimumClearanceMetres = 12.0;

        /// <summary>
        /// How much of the island's half-height the manta's sinusoidal vertical offset
        /// spans. RECOVERED from acs/PatrolVisualiser.cs, whose patrol height varied
        /// sinusoidally across the island half-height; the 1.0 span is that rule taken
        /// literally rather than tuned.
        /// </summary>
        public const double MantaVerticalSpanRatio = 1.0;

        /// <summary>
        /// Vertical cycles per full lateral orbit. WAREBORN TUNING: retail's vertical
        /// term was a sine of patrol progress with no recoverable frequency. Two rises
        /// and two falls per lap keeps the path obviously non-planar.
        /// </summary>
        public const double MantaVerticalCyclesPerOrbit = 2.0;

        /// <summary>
        /// Length of a full fauna day/night cycle, in seconds. WAREBORN TUNING: retail
        /// read a shared world time-of-day service that is not preserved, so the cycle
        /// is stated here instead of guessed at each call site. Twenty minutes gives a
        /// player a chance to see both jelly behaviours in one session.
        /// </summary>
        public const double DayNightCycleSeconds = 1200.0;

        /// <summary>
        /// How far a jelly drifts OUT past the lateral radius by day, as a ratio of
        /// that radius. RECOVERED direction (acs/JellyFishMovement.cs moved laterally
        /// AWAY from the island centre during daytime); the magnitude is WAREBORN TUNING.
        /// </summary>
        public const double JellyDayRadiusRatio = 1.35;

        /// <summary>
        /// How far IN a jelly is drawn at night, as a ratio of the lateral radius.
        /// RECOVERED direction (acs/JellyFishMovement.cs returned toward the centre at
        /// night when outside, and orbited while inside); magnitude WAREBORN TUNING.
        /// </summary>
        public const double JellyNightRadiusRatio = 0.55;

        /// <summary>
        /// Seconds a jelly takes to complete one lateral revolution. WAREBORN TUNING;
        /// deliberately slower than a manta's lap, because a jelly drifts.
        /// </summary>
        public const double JellySecondsPerRevolution = 300.0;

        /// <summary>
        /// Which half of the day/night cycle <paramref name="elapsedSeconds"/> falls in.
        ///
        /// Pure and total, including for negative input, so a test can drive both
        /// phases directly and a caller cannot produce an undefined phase.
        /// </summary>
        public static FaunaDayPhase PhaseAt(double elapsedSeconds)
        {
            double cycle = elapsedSeconds % DayNightCycleSeconds;
            if (cycle < 0.0)
            {
                cycle += DayNightCycleSeconds;
            }
            return cycle < (DayNightCycleSeconds / 2.0) ? FaunaDayPhase.Day : FaunaDayPhase.Night;
        }

        /// <summary>
        /// The island's lateral bounding radius in metres: half the LONGER of the two
        /// horizontal extents, so a long thin island is enclosed rather than clipped.
        /// </summary>
        public static double LateralRadiusOf(IslandTerrainEnvelope envelope)
        {
            double halfX = (envelope.MaxX - envelope.MinX) / 2.0;
            double halfZ = (envelope.MaxZ - envelope.MinZ) / 2.0;
            double half = halfX > halfZ ? halfX : halfZ;
            return half > 0.0 ? half : 1.0;
        }

        /// <summary>Half the island's vertical extent, in metres. Always positive.</summary>
        public static double HalfHeightOf(IslandTerrainEnvelope envelope)
        {
            double half = (envelope.MaxY - envelope.MinY) / 2.0;
            return half > 0.0 ? half : 1.0;
        }

        /// <summary>The lateral centre of the envelope, in island-local metres.</summary>
        public static double CentreXOf(IslandTerrainEnvelope envelope) =>
            (envelope.MinX + envelope.MaxX) / 2.0;

        /// <summary>The lateral centre of the envelope, in island-local metres.</summary>
        public static double CentreZOf(IslandTerrainEnvelope envelope) =>
            (envelope.MinZ + envelope.MaxZ) / 2.0;

        /// <summary>The radius a manta orbits at: ratio-derived, with a floor clearance.</summary>
        public static double MantaOrbitRadiusOf(IslandTerrainEnvelope envelope)
        {
            double lateral = LateralRadiusOf(envelope);
            double scaled = lateral * MantaRadiusRatio;
            double floored = lateral + MantaMinimumClearanceMetres;
            return scaled > floored ? scaled : floored;
        }

        /// <summary>
        /// A creature's island-LOCAL pose in metres at <paramref name="elapsedSeconds"/>.
        /// The whole geometry lives here so it can be asserted on without a definition.
        /// </summary>
        public static (double X, double Y, double Z) LocalPoseAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            return creature.Species == FaunaSpecies.MantaRay
                ? MantaLocalPoseAt(creature, envelope, elapsedSeconds)
                : JellyLocalPoseAt(creature, envelope, elapsedSeconds);
        }

        /// <summary>
        /// The same pose in WORLD coordinates, converted with
        /// <see cref="IslandDefinition.LocalToGlobal"/> so a creature uses the exact
        /// island-local-to-global arithmetic every other placement on this server uses.
        /// </summary>
        public static FixedPointPosition WorldPoseAt(
            FaunaCreature creature, IslandDefinition island,
            IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }
            (double x, double y, double z) = LocalPoseAt(creature, envelope, elapsedSeconds);
            return island.LocalToGlobal(x, y, z);
        }

        /// <summary>
        /// Manta perimeter orbit. RECOVERED shape (acs/PatrolVisualiser.cs): a target
        /// just beyond the lateral bounding radius, advanced in ~10 degree steps, with
        /// a sinusoidal vertical offset spanning the island half-height. Each creature
        /// is phase-offset by its index so a population does not fly as one stack.
        /// </summary>
        private static (double X, double Y, double Z) MantaLocalPoseAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            double steps = elapsedSeconds / MantaSecondsPerOrbitStep;
            double phaseSteps = creature.Index * (360.0 / MantaOrbitStepDegrees / 4.0);
            double degrees = (steps + phaseSteps) * MantaOrbitStepDegrees;
            double radians = degrees * Math.PI / 180.0;

            double radius = MantaOrbitRadiusOf(envelope);
            double x = CentreXOf(envelope) + (radius * Math.Cos(radians));
            double z = CentreZOf(envelope) + (radius * Math.Sin(radians));

            double midY = (envelope.MinY + envelope.MaxY) / 2.0;
            double amplitude = HalfHeightOf(envelope) * MantaVerticalSpanRatio;
            double y = midY + (amplitude * Math.Sin(radians * MantaVerticalCyclesPerOrbit));

            return (x, y, z);
        }

        /// <summary>
        /// Jellyfish day/night drift. RECOVERED rules (acs/JellyFishMovement.cs): by
        /// DAY move laterally away from the island centre and seek the bounds-MIN
        /// altitude when outside the bounds; by NIGHT orbit while inside and return
        /// toward the centre when outside. Expressed as a closed form rather than as
        /// steering, so the day radius sits outside the island and the night radius
        /// inside it, and the day altitude is the envelope's own floor.
        /// </summary>
        private static (double X, double Y, double Z) JellyLocalPoseAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            double lateral = LateralRadiusOf(envelope);
            FaunaDayPhase phase = PhaseAt(elapsedSeconds);

            double revolutions = elapsedSeconds / JellySecondsPerRevolution;
            double phaseOffset = creature.Index * 0.37;
            double radians = ((revolutions + phaseOffset) * 2.0 * Math.PI)
                % (2.0 * Math.PI);

            double radius = phase == FaunaDayPhase.Day
                ? lateral * JellyDayRadiusRatio
                : lateral * JellyNightRadiusRatio;

            double x = CentreXOf(envelope) + (radius * Math.Cos(radians));
            double z = CentreZOf(envelope) + (radius * Math.Sin(radians));

            // By day the jelly is outside the bounds and sinks to the bounds MIN
            // altitude; at night, back inside, it holds the vertical midpoint.
            double y = phase == FaunaDayPhase.Day
                ? envelope.MinY
                : (envelope.MinY + envelope.MaxY) / 2.0;

            return (x, y, z);
        }
    }
}
