namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHERE ONE MEMBER SITS RELATIVE TO ITS SCHOOL, as a pure function of
    /// (member index, elapsed seconds).
    ///
    /// WHAT RETAIL ACTUALLY DID, so this file's departures are visible rather than
    /// implied. A retail flock was NOT a formation and NOT one networked transform
    /// with client-side offsets. It was a separate SpatialOS entity - component 1199
    /// <c>FlockState</c>, carrying only <c>females</c>/<c>males</c> member lists, a
    /// <c>speciesType</c>, origin/target habitat ids and a <c>flockPhase</c> - whose
    /// position acted as an ATTRACTOR. Each member was a full, independently
    /// networked creature entity that received one point
    /// (<c>InhabitantState.flockEntityPosition</c>, component 1197) plus one
    /// direction (<c>targetHabitatVector</c>), and solved its own position on the
    /// UnityWorker with a five-rule boid steerer
    /// (<c>acs/Assets.Scripts.Visualisers.Creatures/MovementController.cs</c>:
    /// cohesion 1.5, separation 1.5, alignment 1.5, seek-target 15, wander 10 - all
    /// PROVED weights). Spacing was emergent from separation acting on live
    /// neighbour transforms refreshed every 10 s, never from a formation table.
    ///
    /// THIS SERVER CANNOT RUN THAT. Boids are an integrator: they carry velocity,
    /// they need every neighbour's live transform, and they are not a function of
    /// elapsed time. <see cref="IslandFaunaRegistry"/>'s whole restart-reproducible
    /// design - no <c>Random</c>, no accumulated physics, a pose that is a closed
    /// form of (creature, seconds) - would have to be abandoned to host one. So the
    /// STRUCTURE is RECOVERED (a school is a moving attractor point; members are
    /// clustered around it and each is still its own networked entity, exactly as
    /// retail wired it) and the CLUSTER SHAPE is a WAREBORN reconstruction of what
    /// the boid rules settle into rather than a simulation of them.
    ///
    /// THE CLUSTER SCALE IS ANCHORED, not invented. Two retail constants survive and
    /// both describe how tightly a flock gathers:
    /// <list type="bullet">
    /// <item>a member declares itself ready to flock inside <c>sqrMagnitude &lt; 100f</c>
    ///   of the flock entity - 10 m
    ///   (<c>acs/Assets.Scripts.Visualisers.Creatures/FlockingConductVisualiser.cs</c>);</item>
    /// <item>a flock counts itself caught up inside <c>Mathf.Pow(15f, 2f)</c> - 15 m
    ///   (<c>acs/Assets.Scripts.Visualisers.Creatures.Flock/FlockVisualiser.cs</c>).</item>
    /// </list>
    /// <see cref="MantaSchoolRadiusMetres"/> sits between those two. That is the
    /// strongest claim the surviving client supports about how big a flock is in
    /// metres, and it is stated as an anchor rather than as a recovered value.
    ///
    /// GROUP SIZE IS NOT RECOVERABLE AND IS NOT CLAIMED HERE. <c>FlockStateData</c>'s
    /// membership is two unbounded <c>List&lt;EntityId&gt;</c>; <c>BasicCreatureSpawnerState</c>
    /// (4321) carries only an inhabitants map and a <c>hasDoneGenesisSpawn</c> flag;
    /// <c>PopulationManagementState</c> carries a "critically low" TIMER and no
    /// threshold. An exhaustive sweep of the decompiled client - every
    /// <c>Bossa.Travellers.Creatures*</c> generated struct, every visualiser, both
    /// data files it ships - finds no min, max, count, density or capacity for a
    /// flock anywhere. Those numbers lived in GSim/FSIM, which is not preserved.
    /// Every count in <see cref="IslandFaunaPolicy"/> is therefore WAREBORN TUNING.
    ///
    /// JELLYFISH DID NOT FLOCK, and this file is used for them anyway - deliberately.
    /// Three independent proofs that retail jellies had no flock:
    /// <c>JellyFishMovement.cs</c> contains no flock reference at all;
    /// <c>JellyFishPreprocessor.cs</c> installs <c>BasicMovementController</c> and
    /// never <c>FlockingConductVisualiser</c>, so a jelly did not even carry the boid
    /// solver; and <c>FlockStateData.speciesType</c> is a <c>SpeciesType</c>
    /// (<c>None,Tree,Beetle,MantaRay,Stalker</c>) while a jelly is a
    /// <c>BasicSpeciesType</c>, so a jelly could not be typed into a flock. What
    /// retail jellies had instead was DENSITY: each one picked its own
    /// <c>Random.Range(0f, 360f)</c> heading offset and its own
    /// <c>Random.onUnitSphere</c> orbit axis and drifted independently about the same
    /// island centre. A player reads a dozen of those as a shoal even though no
    /// component says so. So a jelly "shoal" here is a LOOSE, WIDE cluster - see
    /// <see cref="JellyShoalRadiusMetres"/> - reproducing that emergent look with the
    /// per-member independence made deterministic, not a claim that jellies flocked.
    /// </summary>
    public static class IslandFaunaSchool
    {
        /// <summary>
        /// The golden angle in radians, 2*pi*(1 - 1/phi).
        ///
        /// Used to place members around the school because it is the one spacing
        /// that never lets two members share an angle for ANY count - a school of
        /// three, seven or twenty spreads evenly with the same expression and no
        /// per-size table. Sunflower seeds pack this way for the same reason.
        /// </summary>
        public const double GoldenAngleRadians = 2.399963229728653;

        /// <summary>
        /// A low-discrepancy step used to spread members radially and vertically.
        /// The fractional part of the golden ratio; consecutive multiples of it fill
        /// [0,1) about as evenly as anything can without remembering what it already
        /// placed. This is what stands in for retail's per-creature <c>Random</c>
        /// while staying a pure function of the index.
        /// </summary>
        public const double GoldenRatioFraction = 0.6180339887498949;

        /// <summary>
        /// How far a manta sits from its school's centre, in metres. ANCHORED, not
        /// invented: retail's members declared themselves ready inside 10 m of the
        /// flock entity and a flock counted itself caught up inside 15 m, so a
        /// congregated flock is a cluster on that order. Twelve is between the two.
        /// </summary>
        public const double MantaSchoolRadiusMetres = 12.0;

        /// <summary>
        /// Vertical half-spread of a manta school, in metres. WAREBORN TUNING, and
        /// deliberately much smaller than <see cref="MantaSchoolRadiusMetres"/>: rays
        /// travel as a broad flat sheet, so a school that was as tall as it is wide
        /// would read as a ball of fish rather than as mantas.
        /// </summary>
        public const double MantaSchoolVerticalRadiusMetres = 4.0;

        /// <summary>
        /// How far a jelly sits from its shoal's centre, in metres. WAREBORN TUNING.
        /// Twice a manta school's radius because retail jellies were INDEPENDENT
        /// drifters, not flock members - the look being reproduced is a diffuse
        /// cloud, and a tight one would assert a cohesion rule the decompile proves
        /// jellies never had.
        /// </summary>
        public const double JellyShoalRadiusMetres = 26.0;

        /// <summary>Vertical half-spread of a jelly shoal, in metres. WAREBORN TUNING; jellies bob, so it is generous.</summary>
        public const double JellyShoalVerticalRadiusMetres = 14.0;

        /// <summary>
        /// How fast the cluster turns over, in radians per second. WAREBORN TUNING
        /// standing in for the PROVED wander rule (weight 10) that kept retail's
        /// boids from freezing into a lattice. Slow on purpose: about one revolution
        /// every two minutes, so the school reads as alive rather than as a spinning
        /// carousel, and so a 4 Hz pose stream carries it without visible stepping.
        /// </summary>
        public const double WeaveRadiansPerSecond = 0.05;

        /// <summary>
        /// A school's own phase around its island's orbit, as a fraction of one lap.
        ///
        /// Golden-ratio spaced for the same reason as the member angles: two schools
        /// land on opposite sides, three on thirds, and no count ever stacks two
        /// schools on top of each other. Total for any non-negative index.
        /// </summary>
        public static double SchoolPhaseFraction(int schoolIndex) =>
            Fraction(schoolIndex * GoldenRatioFraction);

        /// <summary>
        /// One member's offset from its school's centre, in metres, at
        /// <paramref name="elapsedSeconds"/>.
        ///
        /// PURE AND TOTAL, which is what lets it sit inside
        /// <see cref="IslandFaunaMovement"/>'s closed form: no state, no entropy, no
        /// integration, so a restarted server puts every member back exactly where it
        /// was and two peers watching the same school are told the same thing.
        ///
        /// Member 0 is NOT the centre. Every member is offset, including the first,
        /// because a school whose lead animal sits exactly on the mathematical
        /// attractor looks like a formation with a leader - which is precisely what
        /// <c>FlockStateData</c> proves retail did not have (it carries member lists
        /// and no leader field at all).
        /// </summary>
        public static (double X, double Y, double Z) MemberOffset(
            int memberIndex, double radius, double verticalRadius,
            double elapsedSeconds, double weaveRadiansPerSecond)
        {
            if (memberIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(memberIndex),
                    "a school member index is a position in a list and cannot be negative");
            }

            double weave = elapsedSeconds * weaveRadiansPerSecond;

            // Angle: golden-angle spaced, plus a slow common rotation so the cluster
            // turns over instead of holding a fixed shape.
            double angle = (memberIndex * GoldenAngleRadians) + weave;

            // Radial: sqrt of a low-discrepancy fraction, which fills a DISC evenly.
            // Without the square root the members bunch toward the centre, because
            // area grows with the square of the radius.
            double radial = radius * Math.Sqrt(Fraction((memberIndex + 1) * GoldenRatioFraction));

            // Vertical: its own low-discrepancy sequence and its own, slower weave,
            // so a member's height is not a function of where it is horizontally.
            // Sharing one phase would flatten the whole school onto a tilted plate.
            double verticalPhase = ((memberIndex + 1) * GoldenAngleRadians * 0.5) + (weave * 0.6);
            double vertical = verticalRadius * Math.Sin(verticalPhase);

            return (radial * Math.Cos(angle), vertical, radial * Math.Sin(angle));
        }

        /// <summary>
        /// The cluster radii for a species: tight and flat for a manta school, wide
        /// and loose for a jelly shoal. See the type remarks for why those differ in
        /// kind rather than in degree.
        /// </summary>
        public static (double Radius, double VerticalRadius) ClusterFor(FaunaSpecies species) =>
            species == FaunaSpecies.MantaRay
                ? (MantaSchoolRadiusMetres, MantaSchoolVerticalRadiusMetres)
                : (JellyShoalRadiusMetres, JellyShoalVerticalRadiusMetres);

        /// <summary>
        /// The fractional part, always in [0,1) including for negative input, so a
        /// caller cannot produce a phase outside one turn.
        /// </summary>
        public static double Fraction(double value)
        {
            double fraction = value - Math.Floor(value);
            return fraction < 0.0 ? fraction + 1.0 : fraction >= 1.0 ? 0.0 : fraction;
        }
    }
}
