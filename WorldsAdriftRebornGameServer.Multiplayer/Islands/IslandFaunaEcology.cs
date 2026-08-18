namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One analytic BLOOM: a moving maximum in an island's ecological field, and
    /// the thing a school orbits instead of the island itself.
    ///
    /// Every number here is island-local and precomputed by
    /// <see cref="IslandFaunaEcology.BloomsFor"/> from (worldSeed, islandId,
    /// species, bloomIndex), so a second evaluator - the admin map - is handed
    /// PARAMETERS and restates only the time part, exactly the split
    /// <see cref="FaunaIslandMotion"/> already uses.
    /// </summary>
    /// <param name="Amplitude">Relative bloom strength, in (0,1]. Field shape only;
    /// no creature count hangs off it.</param>
    /// <param name="SigmaMetres">The Gaussian's width. Also the scale of the orbit
    /// a group flies around the bloom.</param>
    /// <param name="AnnulusRadiusMetres">The centre of the ring the bloom wanders,
    /// measured from the island's lateral centre.</param>
    /// <param name="RadialDriftMetres">How far the bloom breathes in and out of
    /// that ring.</param>
    /// <param name="AngularDriftRadians">How far the bloom swings around the ring,
    /// either side of <paramref name="BaseAngleRadians"/>.</param>
    /// <param name="OmegaRadial">Angular frequency of the radial breath, rad/s.</param>
    /// <param name="OmegaAngular">Angular frequency of the angular swing, rad/s.</param>
    /// <param name="OmegaMigration">The slow steady migration of the bloom around
    /// the island, rad/s. THIS is what stops the epicycle from being a disguised
    /// fixed circle: the whole system creeps around the island and never retraces.</param>
    /// <param name="PhaseRadial">Phase of the radial breath, from the seed.</param>
    /// <param name="PhaseAngular">Phase of the angular swing, from the seed.</param>
    /// <param name="BaseAngleRadians">Where on the ring the bloom starts.</param>
    public readonly record struct FaunaBloom(
        double Amplitude,
        double SigmaMetres,
        double AnnulusRadiusMetres,
        double RadialDriftMetres,
        double AngularDriftRadians,
        double OmegaRadial,
        double OmegaAngular,
        double OmegaMigration,
        double PhaseRadial,
        double PhaseAngular,
        double BaseAngleRadians);

    /// <summary>
    /// THE ECOLOGICAL FIELD: analytic Gaussian blooms whose maxima MOVE, and the
    /// closed-form paths groups take around them.
    ///
    /// THE TARGET ARCHITECTURE THIS IMPLEMENTS (plan-fauna-liveness.md 4b,
    /// principle 2): a per-island scalar field
    /// <c>F(x,t) = sum a_i * exp(-|x - p_i(t)|^2 / 2 sigma_i^2)</c> with slowly
    /// moving centres, and creature motion that follows
    /// <c>v = alpha*grad(F) + beta*(y_hat x grad(F))</c> - attraction plus
    /// circulation - so a group orbits MAXIMA IN THE ECOLOGY rather than the
    /// island's geometric centre.
    ///
    /// WHY THE GROUP PATH IS AN ORBIT RATHER THAN AN INTEGRATION, stated
    /// honestly because it is the one deliberate departure from the formula
    /// above. Following v exactly is an ODE, and an ODE breaks every property
    /// this feature is built on (closed-form evaluation, restart replay, the
    /// admin map's browser evaluator, the 1e-9 parity test). For a single
    /// Gaussian, the v above has a well-known limit behaviour: attraction pulls
    /// the follower onto the maximum while circulation carries it around it, and
    /// the two settle into a bounded orbit around the (moving) peak. The closed
    /// form below IS that limit orbit - centre follows the bloom, the group
    /// circulates at a radius set by the bloom's width and the species'
    /// circulation coefficient - so the LOOK of the field-following is kept and
    /// the state is not. This is the same reconstruction stance
    /// <see cref="IslandFaunaSchool"/> takes to the boid rules, and it is
    /// labelled the same way: the FIELD is WAREBORN TUNING in shape; the
    /// CHARACTER it reconstructs - motion that is irregular because it chases
    /// something - is the recovered character of retail's reached-waypoint
    /// patrol (PatrolVisualiser advanced the target only when the creature
    /// arrived, so retail paths were irregular by construction).
    ///
    /// WHY BLOOMS LIVE ON AN ANNULUS AND NOT OVER THE ROCK. This server has no
    /// terrain query - stated in <see cref="IslandLocationPolicy"/> and again
    /// here because it shapes the geometry. A bloom placed over the island's
    /// footprint would march a school straight through the rock. So a MANTA
    /// bloom (a thermal) wanders a ring OUTSIDE the island's lateral extent, in
    /// the same standoff regime as the recovered patrol
    /// (<see cref="IslandFaunaMovement.MantaOrbitRadiusOf"/>), and a JELLY bloom
    /// (food) wanders a ring at the island's rim, where the recovered day
    /// station already put the shoal. Altitude is NOT bloom-driven: the manta
    /// keeps its recovered midpoint-to-top band and the jelly keeps its
    /// recovered day/night blend, because both of those are recoveries and the
    /// field is not.
    ///
    /// CONTINUITY IS LOAD-BEARING. Every path here is a sum of sines of time
    /// with INCOMMENSURATE periods - C-infinity, never repeating, no day-index
    /// reseed. The architecture sketch seeded blooms from
    /// hash(seed, islandId, day); that reseed would snap every bloom at
    /// midnight, and on the wire a snap is a despawn
    /// (<see cref="IslandFaunaMovement"/>'s own rule). Incommensurate periods
    /// buy the same "different every day" without the discontinuity, and the
    /// deviation is recorded here deliberately.
    ///
    /// EVERYTHING IS A PURE FUNCTION of (worldSeed, islandId, species, index,
    /// t). No Random, no state, no integration - the same contract as the rest
    /// of the fauna maths, for the same reasons.
    ///
    /// NOT WIRED YET. Phase 2 stages this module and its tests; switching the
    /// live motion onto it is gated on the admin-map mirror being editable
    /// (another workstream holds AdminPage.cs) and is designed in
    /// docs/research/design-fauna-ecology-wiring.md.
    /// </summary>
    public static class IslandFaunaEcology
    {
        /// <summary>
        /// The default world seed. WAREBORN TUNING by definition - it selects
        /// among equally-plausible ecologies, nothing more. Overridable at the
        /// wiring step (design doc) so operators can reroll a world's blooms.
        /// </summary>
        public const int DefaultWorldSeed = 1;

        /// <summary>
        /// How many blooms an island's field carries per species: one, plus one
        /// more for islands whose lateral radius clears 250 m. WAREBORN TUNING;
        /// the DIRECTION (bigger islands have more going on) is the same
        /// size-scaling stance <see cref="IslandFaunaCapacity"/> takes, and 250 m
        /// is roughly the tier-1 median so about half the world gets two.
        /// </summary>
        public static int BloomCountFor(IslandTerrainEnvelope envelope) =>
            IslandFaunaMovement.LateralRadiusOf(envelope) >= 250.0 ? 2 : 1;

        /// <summary>
        /// Species coefficients for the circulation term - the beta in
        /// v = alpha*grad(F) + beta*(y_hat x grad(F)), expressed as the orbit
        /// radius in units of the bloom's sigma. WAREBORN TUNING. Jellies follow
        /// food closely (tight orbit, well inside the bloom); mantas ride
        /// thermals wide (orbit out at the bloom's shoulder). The numbers keep
        /// every group inside two sigma of its maximum, which is what "orbits
        /// the maximum" means once it must be checkable.
        /// </summary>
        public static double CirculationSigmaRatioFor(FaunaSpecies species) =>
            species == FaunaSpecies.MantaRay ? 1.2 : 0.6;

        /// <summary>
        /// How fast a group travels around its bloom, in metres per second.
        /// The manta keeps its recovered-corroborated 8 m/s
        /// (<see cref="IslandFaunaMovement.MantaMetresPerSecond"/>); the jelly
        /// drifts an order of magnitude slower, matching the character of its
        /// existing 600-second revolution. WAREBORN TUNING for the jelly figure.
        /// </summary>
        public static double OrbitMetresPerSecondFor(FaunaSpecies species) =>
            species == FaunaSpecies.MantaRay ? IslandFaunaMovement.MantaMetresPerSecond : 1.2;

        // ---- Bloom construction ------------------------------------------------

        /// <summary>
        /// The maximum group-spread multiplier <see cref="GroupOrbitRadius"/> can
        /// apply. Named so the clearance arithmetic below and the radius function
        /// cannot silently disagree.
        /// </summary>
        public const double MaxGroupSpread = 1.25;

        /// <summary>
        /// The clearance FLOOR: the closest to the island's lateral centre any
        /// group centre may ever come, per species. For the manta it is the
        /// recovered patrol radius (half-diagonal + 10 m); for the jelly it is
        /// just past the rim, the same 1.05x the recovered night station uses.
        /// This server has no terrain query, so clearance must hold by
        /// CONSTRUCTION, not by checking.
        /// </summary>
        public static double ClearanceFloorMetres(
            FaunaSpecies species, IslandTerrainEnvelope envelope) =>
            species == FaunaSpecies.MantaRay
                ? IslandFaunaMovement.MantaOrbitRadiusOf(envelope)
                : IslandFaunaMovement.LateralRadiusOf(envelope)
                    * IslandFaunaMovement.JellyNightRadiusRatio;

        /// <summary>
        /// Every bloom of one species' field on one island, fully parameterised.
        /// Deterministic in all arguments; allocation is the returned array only.
        ///
        /// THE CLEARANCE ARITHMETIC, because it is the safety property. The ring
        /// is placed at <c>floor + radialDrift + maxGroupOrbit</c>, so the
        /// closest possible approach - ring minus its own breath minus the widest
        /// group orbit - is exactly the species' clearance floor. A school
        /// following its bloom can therefore never dip inside the recovered
        /// patrol standoff (manta) or the rim station (jelly), which is what
        /// "cannot fly through the rock" means on a server with no terrain
        /// query. The outermost reach is floor + 2*(drift + maxGroupOrbit),
        /// bounded by the sigma and drift fractions below; the tests hold it
        /// against the recovered proportions.
        /// </summary>
        public static FaunaBloom[] BloomsFor(
            int worldSeed, IslandId islandId, FaunaSpecies species,
            IslandTerrainEnvelope envelope)
        {
            int count = BloomCountFor(envelope);
            FaunaBloom[] blooms = new FaunaBloom[count];

            double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);
            double floor = ClearanceFloorMetres(species, envelope);

            for (int i = 0; i < count; i++)
            {
                double u1 = Unit(worldSeed, islandId, species, i, 1);
                double u2 = Unit(worldSeed, islandId, species, i, 2);
                double u3 = Unit(worldSeed, islandId, species, i, 3);
                double u4 = Unit(worldSeed, islandId, species, i, 4);
                double u5 = Unit(worldSeed, islandId, species, i, 5);

                // Width scales with the ISLAND, drift with the FLOOR, so a small
                // island's bloom keeps the same proportions a big one does.
                double sigma = lateral * (species == FaunaSpecies.MantaRay
                    ? 0.12 + (0.08 * u1)
                    : 0.10 + (0.08 * u1));
                double maxGroupOrbit = sigma * CirculationSigmaRatioFor(species) * MaxGroupSpread;
                double radialDrift = floor * (0.02 + (0.03 * u2));
                double angularDrift = 0.25 + (0.20 * u3);

                // Incommensurate periods, minutes long. The golden-ratio
                // multiplier between the two fast terms keeps them from ever
                // phase-locking; the migration term is slower still - one lap of
                // the island in roughly half an hour to an hour by seed - and it
                // is what stops the epicycle being a disguised fixed circle.
                double omegaRadial = 2.0 * Math.PI / (420.0 + (240.0 * u4));
                double omegaAngular = omegaRadial * IslandFaunaSchool.GoldenRatioFraction;
                double omegaMigration = 2.0 * Math.PI / (1800.0 + (1500.0 * u5));

                blooms[i] = new FaunaBloom(
                    Amplitude: 0.5 + (0.5 * u2),
                    SigmaMetres: sigma,
                    AnnulusRadiusMetres: floor + radialDrift + maxGroupOrbit,
                    RadialDriftMetres: radialDrift,
                    AngularDriftRadians: angularDrift,
                    OmegaRadial: omegaRadial,
                    OmegaAngular: omegaAngular,
                    OmegaMigration: omegaMigration,
                    PhaseRadial: 2.0 * Math.PI * u3,
                    PhaseAngular: 2.0 * Math.PI * u4,
                    BaseAngleRadians: 2.0 * Math.PI
                        * IslandFaunaSchool.Fraction(u5 + (i * IslandFaunaSchool.GoldenRatioFraction)));
            }
            return blooms;
        }

        /// <summary>
        /// Where one bloom's maximum is at <paramref name="elapsedSeconds"/>, in
        /// island-local XZ metres relative to the envelope's lateral centre.
        /// A sum of three sinusoidal terms; C-infinity in time.
        /// </summary>
        public static (double X, double Z) BloomCentreAt(FaunaBloom bloom, double elapsedSeconds)
        {
            double radius = bloom.AnnulusRadiusMetres
                + (bloom.RadialDriftMetres
                    * Math.Sin((bloom.OmegaRadial * elapsedSeconds) + bloom.PhaseRadial));
            double angle = bloom.BaseAngleRadians
                + (bloom.OmegaMigration * elapsedSeconds)
                + (bloom.AngularDriftRadians
                    * Math.Sin((bloom.OmegaAngular * elapsedSeconds) + bloom.PhaseAngular));
            return (radius * Math.Sin(angle), radius * Math.Cos(angle));
        }

        // ---- The field itself --------------------------------------------------

        /// <summary>
        /// The field value F(x,t) at an island-local lateral point (relative to
        /// the envelope's lateral centre). The admin map renders this; group
        /// motion uses the limit orbit rather than sampling it (type remarks).
        /// </summary>
        public static double FieldAt(
            IReadOnlyList<FaunaBloom> blooms, double x, double z, double elapsedSeconds)
        {
            if (blooms == null) throw new ArgumentNullException(nameof(blooms));
            double sum = 0.0;
            for (int i = 0; i < blooms.Count; i++)
            {
                (double cx, double cz) = BloomCentreAt(blooms[i], elapsedSeconds);
                double dx = x - cx;
                double dz = z - cz;
                double sigma2 = blooms[i].SigmaMetres * blooms[i].SigmaMetres;
                sum += blooms[i].Amplitude * Math.Exp(-((dx * dx) + (dz * dz)) / (2.0 * sigma2));
            }
            return sum;
        }

        /// <summary>The lateral gradient of <see cref="FieldAt"/>, analytically.</summary>
        public static (double X, double Z) FieldGradientAt(
            IReadOnlyList<FaunaBloom> blooms, double x, double z, double elapsedSeconds)
        {
            if (blooms == null) throw new ArgumentNullException(nameof(blooms));
            double gx = 0.0, gz = 0.0;
            for (int i = 0; i < blooms.Count; i++)
            {
                (double cx, double cz) = BloomCentreAt(blooms[i], elapsedSeconds);
                double dx = x - cx;
                double dz = z - cz;
                double sigma2 = blooms[i].SigmaMetres * blooms[i].SigmaMetres;
                double f = blooms[i].Amplitude
                    * Math.Exp(-((dx * dx) + (dz * dz)) / (2.0 * sigma2));
                gx += -f * dx / sigma2;
                gz += -f * dz / sigma2;
            }
            return (gx, gz);
        }

        // ---- Group motion around the field's maxima ---------------------------

        /// <summary>Which of the island's blooms a group circulates. Round-robin.</summary>
        public static int BloomIndexFor(int groupIndex, int bloomCount) =>
            bloomCount <= 0 ? 0 : ((groupIndex % bloomCount) + bloomCount) % bloomCount;

        /// <summary>
        /// A group's orbit radius around its bloom, in metres: the species'
        /// circulation ratio times the bloom's width, with a golden-ratio spread
        /// per group so two groups on one bloom fly different circles.
        /// </summary>
        public static double GroupOrbitRadius(
            FaunaBloom bloom, FaunaSpecies species, int groupIndex)
        {
            // The spread multiplier stays inside [1, MaxGroupSpread]: the
            // clearance floor in BloomsFor is computed against MaxGroupSpread,
            // so a spread past it would be a school inside the rock.
            double spread = 1.0 + ((MaxGroupSpread - 1.0) * IslandFaunaSchool.Fraction(
                (groupIndex + 1) * IslandFaunaSchool.GoldenRatioFraction));
            return bloom.SigmaMetres * CirculationSigmaRatioFor(species) * spread;
        }

        /// <summary>
        /// WHERE A GROUP'S CENTRE IS, laterally: the bloom's moving maximum plus
        /// the circulation orbit around it, in island-local metres relative to
        /// the envelope's lateral centre.
        ///
        /// Constant LINEAR speed along the orbit - the recovered character
        /// (<see cref="IslandFaunaMovement.MantaMetresPerSecond"/>'s own rule) -
        /// so the angular rate is speed over radius. Groups are phase-spread by
        /// <see cref="IslandFaunaSchool.SchoolPhaseFraction"/>, and each SPECIES
        /// advances its own phase independently, which is RECOVERED:
        /// HabitatPatrolState (4332) carried a separate orbit phase per species
        /// on the same island, with the debug visualiser colouring Beetle yellow
        /// and MantaRay red - retail explicitly ran two patrols around one
        /// island at once. The species separation here comes free, because each
        /// species has its own bloom set and its own speed.
        /// </summary>
        public static (double X, double Z) GroupCentreAt(
            FaunaBloom bloom, FaunaSpecies species, int groupIndex, double elapsedSeconds)
        {
            (double bx, double bz) = BloomCentreAt(bloom, elapsedSeconds);
            double radius = GroupOrbitRadius(bloom, species, groupIndex);
            double angularRate = OrbitMetresPerSecondFor(species) / Math.Max(radius, 1.0);
            double angle = (angularRate * elapsedSeconds)
                + (2.0 * Math.PI * IslandFaunaSchool.SchoolPhaseFraction(groupIndex));
            return (bx + (radius * Math.Sin(angle)), bz + (radius * Math.Cos(angle)));
        }

        /// <summary>
        /// The farthest from the island's lateral centre a group's centre can
        /// EVER reach under these parameters: ring + breath + orbit. The bound
        /// the tests hold against the recovered day-station ratio, so the
        /// ecology cannot stand wildlife farther out than the old geometry did.
        /// </summary>
        public static double MaxLateralReach(
            FaunaBloom bloom, FaunaSpecies species, int groupIndex) =>
            bloom.AnnulusRadiusMetres + bloom.RadialDriftMetres
                + GroupOrbitRadius(bloom, species, groupIndex);

        /// <summary>
        /// The closest to the island's lateral centre the same group can ever
        /// come. By construction this is at least
        /// <see cref="ClearanceFloorMetres"/> - the tests assert it for every
        /// real catalogue island rather than trusting the algebra.
        /// </summary>
        public static double MinLateralReach(
            FaunaBloom bloom, FaunaSpecies species, int groupIndex) =>
            bloom.AnnulusRadiusMetres - bloom.RadialDriftMetres
                - GroupOrbitRadius(bloom, species, groupIndex);

        // ---- Seeded uniforms ---------------------------------------------------

        /// <summary>
        /// A deterministic uniform in [0,1) from the seed tuple. FNV-1a over the
        /// textual tuple - the same stable-across-processes reasoning as
        /// <see cref="IslandFaunaPolicy.JellySpeciesFor"/>, and NOT
        /// string.GetHashCode, which .NET randomises per process.
        /// </summary>
        public static double Unit(
            int worldSeed, IslandId islandId, FaunaSpecies species, int bloomIndex, int channel)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            uint hash = OffsetBasis;
            void Mix(string s)
            {
                for (int i = 0; i < s.Length; i++)
                {
                    hash = (hash ^ s[i]) * Prime;
                }
                hash = (hash ^ '|') * Prime;
            }
            Mix(worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(islandId.ToString());
            Mix(((int)species).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(bloomIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(channel.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return hash / 4294967296.0;
        }
    }
}
