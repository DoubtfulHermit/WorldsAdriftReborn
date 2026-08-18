namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Which half of the fauna day a creature is currently living in.</summary>
    public enum FaunaDayPhase
    {
        /// <summary>Jellies push outward and sink toward the bounds floor.</summary>
        Day,

        /// <summary>Jellies are drawn back in and rise toward the island's rim.</summary>
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
    /// AND EVERYTHING IS CONTINUOUS IN TIME, which is a separate promise and an
    /// equally load-bearing one. A closed form is free to be discontinuous, and this
    /// file's first version WAS: a jelly's radius and altitude both switched
    /// instantly at the day/night boundary, teleporting it hundreds of metres. On the
    /// wire a teleport is indistinguishable from a despawn-and-respawn, which is the
    /// exact complaint this feature was reported for. Every phase term below is
    /// therefore blended rather than switched.
    ///
    /// GEOMETRY IS DERIVED AS RATIOS OF THE ISLAND'S OWN ENVELOPE, never as absolute
    /// metres. An orbit radius of "320 m" would put a manta INSIDE the rock on a
    /// 600 m island and a kilometre off the perimeter of a 40 m one. Taking the
    /// radius from the envelope's own lateral extent means a tiny island, a huge
    /// island and a long thin anisotropic island each get an orbit that clears their
    /// own geometry by the same proportion.
    ///
    /// A SCHOOL MOVES; A MEMBER IS AN OFFSET FROM IT. Every path below positions a
    /// SCHOOL, and <see cref="IslandFaunaSchool"/> then displaces each member around
    /// that point. That split is RECOVERED: retail's flock was a separate entity
    /// acting as an attractor, and members steered toward it, so "one moving point
    /// plus per-member displacement" is the shape the surviving client describes.
    /// </summary>
    public static class IslandFaunaMovement
    {
        /// <summary>
        /// Degrees of orbit advanced per patrol step. RECOVERED from
        /// acs/PatrolVisualiser.cs, whose <c>CreatureReachedPatrol</c> adds or
        /// subtracts exactly <c>10f</c> and wraps at 0/360 - so a lap is 36 waypoints.
        /// Kept as the unit the lap is expressed in even though this server advances
        /// continuously rather than on arrival.
        /// </summary>
        public const double MantaOrbitStepDegrees = 10.0;

        /// <summary>
        /// How far outside the island's lateral extent a manta flies, in METRES.
        /// RECOVERED EXACTLY from acs/PatrolVisualiser.cs, which computed its patrol
        /// target as <c>new Vector2(BoundsExtents.x, BoundsExtents.z).magnitude + 10f</c>
        /// - the horizontal half-DIAGONAL of the island's bounding box plus a flat ten
        /// metre standoff.
        ///
        /// This replaces a "1.15x the larger lateral extent" ratio that was WAREBORN
        /// TUNING. The recovered form is both more faithful and better behaved: the
        /// half-diagonal already scales with the island, and on a square island it is
        /// about 1.41x the half-width, so the manta clears the CORNERS rather than
        /// clipping through them the way a radius taken from one axis does.
        /// </summary>
        public const double MantaLateralStandoffMetres = 10.0;

        /// <summary>
        /// How fast a school of mantas travels along its patrol, in metres per second.
        ///
        /// WAREBORN TUNING for the magnitude; CONSTANT SPEED rather than constant lap
        /// time is RECOVERED. Retail advanced the patrol target by ten degrees when the
        /// creature REACHED the previous one, so lap time was a consequence of how big
        /// the island was and how fast the animal swam - never a fixed number. The
        /// previous "144 seconds per lap regardless of island" got this backwards and
        /// it showed at the extremes: on the catalogue's largest island that works out
        /// to 23 m/s, a manta ray moving at 84 km/h.
        ///
        /// Eight metres a second is a brisk glide. It gives the smallest tier-1 island
        /// about a minute a lap and the largest about eight, which is the spread a
        /// fixed speed is supposed to produce.
        /// </summary>
        public const double MantaMetresPerSecond = 8.0;

        /// <summary>
        /// How much of the island's half-height the manta's vertical term spans.
        /// RECOVERED: acs/PatrolVisualiser.cs multiplies its vertical sine by
        /// <c>islandSurfaceData.BoundsExtents.y</c> - the half-height exactly.
        /// </summary>
        public const double MantaVerticalSpanRatio = 1.0;

        /// <summary>
        /// Where inside an island's bounding box the ground a player stands on
        /// actually is, as a fraction from the box's floor to its ceiling.
        ///
        /// MEASURED, not assumed, and it is the number that explains why the wildlife
        /// was invisible. Across all 254 islands in the release runtime catalogue, the
        /// island's own reviewed landing point sits at a MEDIAN 0.755 of its AABB
        /// height. A floating island is mostly the rock hanging underneath it, so the
        /// box's MIDPOINT - which is what retail's patrol maths is anchored on, and
        /// what this server's jellies used to hold at night - is typically fifty to a
        /// hundred and fifty metres BELOW the player's feet.
        ///
        /// Retail dealt with that by only ever offsetting UPWARD from the midpoint
        /// (see <see cref="MantaVerticalOffsetRatioAt"/>). This constant is what the
        /// jelly reconstruction uses to reach the same band.
        /// </summary>
        public const double IslandWalkableHeightFraction = 0.75;

        /// <summary>
        /// Length of a full fauna day/night cycle, in seconds. WAREBORN TUNING: retail
        /// read <c>TimeKeeperVisualizer</c>'s shared world time, which is not
        /// preserved, so the cycle length is stated here instead of guessed at each
        /// call site. Twenty minutes gives a player a chance to see both jelly
        /// behaviours in one session.
        /// </summary>
        public const double DayNightCycleSeconds = 1200.0;

        /// <summary>
        /// Where in the normalised cycle day begins. RECOVERED EXACTLY from
        /// acs/Assets.Scripts.Visualisers.Creatures/JellyFishMovement.cs:
        /// <c>_isDayTime = num &gt; 0.2f &amp;&amp; num &lt; 0.8f</c>. Day is therefore
        /// the middle 60% of the cycle and night the 40% around the wrap - which is
        /// not a half-and-half split, and was worth recovering rather than assuming.
        /// </summary>
        public const double DayBeginsAtCycleFraction = 0.2;

        /// <summary>Where in the normalised cycle day ends. RECOVERED; see <see cref="DayBeginsAtCycleFraction"/>.</summary>
        public const double DayEndsAtCycleFraction = 0.8;

        /// <summary>
        /// How much of the cycle each dawn and dusk takes to cross, as a fraction.
        ///
        /// WAREBORN TUNING, and it exists purely to make the boundary CONTINUOUS.
        /// Retail's threshold was a hard boolean, but retail creatures were steered:
        /// flipping the desired direction made a jelly turn around over some seconds,
        /// it did not move the jelly. A closed form has no such inertia, so without a
        /// ramp the same boolean becomes a teleport. Six percent of twenty minutes is
        /// about seventy seconds of dawn, which is a drift rather than a snap.
        /// </summary>
        public const double PhaseTransitionFraction = 0.06;

        /// <summary>
        /// How far OUT past the lateral extent a jelly shoal drifts by day, as a ratio
        /// of that extent. RECOVERED direction: acs/JellyFishMovement.cs steers
        /// <c>(-toIsland.x, 0, -toIsland.z)</c> during daytime - laterally AWAY from
        /// the island centre with no vertical component. The magnitude is WAREBORN
        /// TUNING, since retail's was wherever the steering happened to settle.
        /// </summary>
        public const double JellyDayRadiusRatio = 1.35;

        /// <summary>
        /// How far out a jelly shoal sits at night, as a ratio of the lateral extent.
        ///
        /// Just past the rim, and the reasoning is a RECONSTRUCTION rather than a
        /// recovery - stated in full because it is the biggest inference in this file.
        /// Retail's night rule is <c>(BoundsCenter - position).normalized</c> when the
        /// jelly is outside the bounds: swim toward the island's CENTRE. A jelly
        /// approaching from the daytime station below the island is therefore steering
        /// up and inward - but the island's centre is solid rock, and retail jellies
        /// had colliders, so what that rule actually produces is a jelly that rises
        /// along the underside and gathers at the rim. A closed form has no collider
        /// and would fly the shoal into the middle of the island, so the rim is
        /// modelled directly.
        /// </summary>
        public const double JellyNightRadiusRatio = 1.05;

        /// <summary>
        /// Seconds a jelly shoal takes to complete one lateral revolution. WAREBORN
        /// TUNING. Retail's night orbit came from a random per-creature axis crossed
        /// with the vector to the island, at whatever rate the physics gave; a slow
        /// analytic circuit is the closed-form stand-in. Deliberately far slower than
        /// a manta's patrol, because a jelly drifts and a ray swims.
        /// </summary>
        public const double JellySecondsPerRevolution = 600.0;

        /// <summary>
        /// Which half of the day/night cycle <paramref name="elapsedSeconds"/> falls
        /// in, using the RECOVERED 0.2/0.8 thresholds.
        ///
        /// Pure and total, including for negative input, so a test can drive both
        /// phases directly and a caller cannot produce an undefined phase. This is the
        /// BOOLEAN view, kept because it is what a reader asks for; the geometry below
        /// uses <see cref="DaynessAt"/> instead, because it must not step.
        /// </summary>
        public static FaunaDayPhase PhaseAt(double elapsedSeconds)
        {
            double cycle = CycleFractionAt(elapsedSeconds);
            return cycle > DayBeginsAtCycleFraction && cycle < DayEndsAtCycleFraction
                ? FaunaDayPhase.Day : FaunaDayPhase.Night;
        }

        /// <summary>Where in the day/night cycle a moment falls, in [0,1).</summary>
        public static double CycleFractionAt(double elapsedSeconds) =>
            IslandFaunaSchool.Fraction(elapsedSeconds / DayNightCycleSeconds);

        /// <summary>
        /// HOW DAYTIME it is, from 0 (fully night) to 1 (fully day), ramping smoothly
        /// across dawn and dusk.
        ///
        /// This is the continuous replacement for the boolean, and it is what every
        /// jelly term is interpolated on. Total, periodic and equal to the boolean
        /// everywhere except inside the two <see cref="PhaseTransitionFraction"/>
        /// ramps.
        /// </summary>
        public static double DaynessAt(double elapsedSeconds)
        {
            double cycle = CycleFractionAt(elapsedSeconds);
            double ramp = PhaseTransitionFraction;

            // Rising through dawn, then falling through dusk. Distances are measured
            // on the cycle rather than on the raw fraction so the night that straddles
            // the wrap is one phase and not two.
            double rising = SmoothStep((cycle - DayBeginsAtCycleFraction) / ramp);
            double falling = SmoothStep((DayEndsAtCycleFraction - cycle) / ramp);
            return Math.Min(rising, falling);
        }

        /// <summary>
        /// The island's lateral bounding radius in metres: half the LONGER of the two
        /// horizontal extents, so a long thin island is enclosed rather than clipped.
        /// This is the scale the JELLY ratios are expressed against.
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

        /// <summary>The vertical centre of the envelope, in island-local metres.</summary>
        public static double CentreYOf(IslandTerrainEnvelope envelope) =>
            (envelope.MinY + envelope.MaxY) / 2.0;

        /// <summary>
        /// The radius a manta school orbits at, in metres. RECOVERED EXACTLY:
        /// the horizontal half-diagonal of the island's bounding box plus
        /// <see cref="MantaLateralStandoffMetres"/>.
        /// </summary>
        public static double MantaOrbitRadiusOf(IslandTerrainEnvelope envelope)
        {
            double halfX = (envelope.MaxX - envelope.MinX) / 2.0;
            double halfZ = (envelope.MaxZ - envelope.MinZ) / 2.0;
            double diagonal = Math.Sqrt((halfX * halfX) + (halfZ * halfZ));
            return (diagonal > 0.0 ? diagonal : 1.0) + MantaLateralStandoffMetres;
        }

        /// <summary>How long one manta lap takes on this island, in seconds. Always positive.</summary>
        public static double MantaLapSecondsOf(IslandTerrainEnvelope envelope) =>
            2.0 * Math.PI * MantaOrbitRadiusOf(envelope) / MantaMetresPerSecond;

        /// <summary>
        /// The manta's vertical offset above the island's MIDPOINT, as a fraction of
        /// its half-height, at <paramref name="lapFraction"/> through one lap.
        ///
        /// THE SINGLE MOST IMPORTANT RECOVERY IN THIS FILE, because getting it wrong
        /// is what put the wildlife where nobody could see it. Retail computes
        /// <c>Vector3.up * Mathf.Sin(orbitDegrees * (PI/180f) * 0.25f) * BoundsExtents.y</c>
        /// and <c>orbitDegrees</c> is WRAPPED INTO [0,360] by
        /// <c>CreatureReachedPatrol</c>. The argument to that sine therefore only ever
        /// covers [0, PI/2], so the sine only ever covers [0, 1]: the offset is
        /// ALWAYS NON-NEGATIVE and the patrol occupies the band from the island's
        /// vertical MIDPOINT up to its very TOP. It never goes below the midpoint.
        ///
        /// This server previously read the same line as a full <c>sin</c> over
        /// [-1, +1] and flew mantas symmetrically about the midpoint - so half of
        /// every lap was spent between the island's midpoint and the BOTTOM of its
        /// bounding box, which on a floating island is the tip of the rock spire, a
        /// couple of hundred metres under the player's feet. With the walkable surface
        /// measured at 0.75 of the box height (see
        /// <see cref="IslandWalkableHeightFraction"/>), the recovered band straddles
        /// the ground a player is standing on and the mistaken one mostly did not.
        ///
        /// Retail's term SNAPS from 1 back to 0 at the 360-degree wrap, and a steered
        /// creature simply glided down to the new target over the following seconds.
        /// A closed form has no glide, so the band is traversed up and back down
        /// instead - same band, same endpoints, no discontinuity. That closure is
        /// WAREBORN; the band is RECOVERED.
        /// </summary>
        public static double MantaVerticalOffsetRatioAt(double lapFraction) =>
            MantaVerticalSpanRatio * Math.Sin(IslandFaunaSchool.Fraction(lapFraction) * Math.PI);

        /// <summary>
        /// A creature's island-LOCAL pose in metres at <paramref name="elapsedSeconds"/>.
        /// The whole geometry lives here so it can be asserted on without a definition.
        /// </summary>
        public static (double X, double Y, double Z) LocalPoseAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            (double x, double y, double z) = creature.Species == FaunaSpecies.MantaRay
                ? MantaSchoolCentreAt(creature, envelope, elapsedSeconds)
                : JellyShoalCentreAt(creature, envelope, elapsedSeconds);

            (double radius, double verticalRadius) = IslandFaunaSchool.ClusterFor(creature.Species);
            (double ox, double oy, double oz) = IslandFaunaSchool.MemberOffset(
                creature.MemberIndex, radius, verticalRadius, elapsedSeconds,
                IslandFaunaSchool.WeaveRadiansPerSecond);

            return (x + ox, y + oy, z + oz);
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
        /// Where a MANTA SCHOOL's centre is: the recovered perimeter patrol.
        ///
        /// PUBLIC because the school's path is the thing worth asserting on. A single
        /// member's pose is the path plus a cluster offset, and on a small island that
        /// offset can be a large fraction of the island - so a test written against a
        /// member cannot distinguish "the patrol is wrong" from "the school is wide".
        ///
        /// A target on a circle of <see cref="MantaOrbitRadiusOf"/>, travelled at a
        /// constant <see cref="MantaMetresPerSecond"/>, with the recovered
        /// midpoint-to-top vertical band. Retail's own lateral term is
        /// <c>(sin(theta), 0, cos(theta))</c>, kept here rather than normalised to the
        /// usual (cos, sin) so the phase relationship between the lateral angle and
        /// the vertical term is the one the decompile shows.
        ///
        /// Schools are spread around the lap by <see cref="IslandFaunaSchool.SchoolPhaseFraction"/>,
        /// so an island carrying more than one never stacks them.
        /// </summary>
        public static (double X, double Y, double Z) MantaSchoolCentreAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            double radius = MantaOrbitRadiusOf(envelope);
            double lapSeconds = MantaLapSecondsOf(envelope);
            double lap = IslandFaunaSchool.Fraction(
                (elapsedSeconds / lapSeconds) + IslandFaunaSchool.SchoolPhaseFraction(creature.SchoolIndex));
            double theta = lap * 2.0 * Math.PI;

            double x = CentreXOf(envelope) + (radius * Math.Sin(theta));
            double z = CentreZOf(envelope) + (radius * Math.Cos(theta));
            double y = CentreYOf(envelope)
                + (HalfHeightOf(envelope) * MantaVerticalOffsetRatioAt(lap));

            return (x, y, z);
        }

        /// <summary>
        /// Where a JELLY SHOAL's centre is: the recovered day/night drift, blended.
        /// PUBLIC for the reason given on <see cref="MantaSchoolCentreAt"/>.
        ///
        /// RECOVERED rules (acs/JellyFishMovement.cs). By DAY a jelly steers laterally
        /// AWAY from the island centre and, once outside the bounds, holds the
        /// altitude of <c>BoundsMin.y</c> - the underside of the rock. At NIGHT it
        /// steers back toward <c>BoundsCenter</c>, orbiting once inside. Day is the
        /// middle 60% of the cycle, from the recovered 0.2/0.8 thresholds.
        ///
        /// The night STATION is a reconstruction rather than a recovery - see
        /// <see cref="JellyNightRadiusRatio"/> for why "toward the centre" plus a
        /// collider means "up to the rim" - and the altitude it rises to is the
        /// MEASURED walkable band (<see cref="IslandWalkableHeightFraction"/>), so the
        /// night shoal comes up level with a player standing on the island instead of
        /// hanging somewhere under the rock where nobody has ever seen one.
        ///
        /// Both the radius and the altitude are interpolated on
        /// <see cref="DaynessAt"/>, never switched. The first version of this file
        /// switched them, and a shoal that jumps from the island's underside to its
        /// rim in one frame is a despawn as far as a player is concerned.
        /// </summary>
        public static (double X, double Y, double Z) JellyShoalCentreAt(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double elapsedSeconds)
        {
            double lateral = LateralRadiusOf(envelope);
            double dayness = DaynessAt(elapsedSeconds);

            double revolutions = elapsedSeconds / JellySecondsPerRevolution;
            double theta = (IslandFaunaSchool.Fraction(revolutions
                + IslandFaunaSchool.SchoolPhaseFraction(creature.SchoolIndex))) * 2.0 * Math.PI;

            double radius = lateral * Lerp(JellyNightRadiusRatio, JellyDayRadiusRatio, dayness);

            double nightY = envelope.MinY
                + ((envelope.MaxY - envelope.MinY) * IslandWalkableHeightFraction);
            double y = Lerp(nightY, envelope.MinY, dayness);

            return (CentreXOf(envelope) + (radius * Math.Sin(theta)),
                y,
                CentreZOf(envelope) + (radius * Math.Cos(theta)));
        }

        /// <summary>Linear interpolation, with <paramref name="t"/> already in [0,1].</summary>
        private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

        /// <summary>
        /// The classic smootherstep-free cubic 3t^2 - 2t^3, clamped. Zero derivative at
        /// both ends, which is what stops a phase ramp from producing a visible kink in
        /// the shoal's motion where the blend starts and finishes.
        /// </summary>
        private static double SmoothStep(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - (2.0 * t));
        }
    }
}
