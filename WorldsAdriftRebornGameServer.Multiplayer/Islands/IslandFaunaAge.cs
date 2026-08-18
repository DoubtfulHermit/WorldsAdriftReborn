namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The five fields of component 1166 <c>AgeState</c>, as this server chooses
    /// to fill them. A value object rather than five loose numbers because the
    /// ONE property that matters about them is a relationship between two of
    /// them - <see cref="SecondsOld"/> against
    /// <see cref="SecondsTillFullyGrown"/> - and a type is where a relationship
    /// can be guarded.
    ///
    /// The wire shape is RECOVERED verbatim from
    /// <c>gencode/Bossa.Travellers.Creatures/AgeStateData.cs</c>: three int32
    /// seconds counters and two float kilogrammes, in this order.
    /// </summary>
    /// <param name="SecondsOld">How long this animal has been alive. The ONLY
    /// field with an update callback the client's <c>AgeVisualizer</c>
    /// subscribes to.</param>
    /// <param name="SecondsTillFullyGrown">The denominator of the growth ratio.
    /// NEVER ZERO - see <see cref="IslandFaunaAge"/>'s remarks on the NaN
    /// hazard.</param>
    /// <param name="SecondsTillNaturalDeath">A beetle-only blend-shape driver;
    /// inert on a manta. Non-zero only because <c>RayAging.GetLifeLivedRatio</c>
    /// divides by it.</param>
    /// <param name="MaxMassKilograms">Client-side only a divisor in
    /// <c>MovementController</c>, so it must not be zero.</param>
    /// <param name="MinMassKilograms">As above.</param>
    public readonly record struct FaunaAgeState(
        int SecondsOld,
        int SecondsTillFullyGrown,
        int SecondsTillNaturalDeath,
        float MaxMassKilograms,
        float MinMassKilograms)
    {
        /// <summary>
        /// The growth ratio the client will compute from these numbers -
        /// <c>Clamp01(secondsOld / secondsTillFullyGrown)</c>, RECOVERED verbatim
        /// from <c>AgeVisualizer.AgeUpdated</c>. Exposed so a test can assert on
        /// what the client will DO rather than on what we sent.
        /// </summary>
        public double FullyGrownRatio =>
            SecondsTillFullyGrown <= 0
                ? 1.0
                : Math.Clamp((double)SecondsOld / SecondsTillFullyGrown, 0.0, 1.0);

        /// <summary>
        /// The LOCAL SCALE the client's <c>AgeVisualizer</c> will assign to the
        /// entity root from these numbers:
        /// <c>Lerp(birthScale, fullyGrownScale, ratio)</c>. The endpoints are
        /// RECOVERED off the shipped prefab (see
        /// <see cref="IslandFaunaAge.RecoveredBirthScale"/>), so this is not a
        /// prediction - it is the client's own arithmetic, restated where a test
        /// can reach it.
        /// </summary>
        public double RenderedScale =>
            IslandFaunaAge.RecoveredBirthScale
            + ((IslandFaunaAge.RecoveredFullyGrownScale - IslandFaunaAge.RecoveredBirthScale)
                * FullyGrownRatio);
    }

    /// <summary>
    /// HOW OLD A CREATURE IS, and the total policy that keeps every OTHER
    /// creature full-sized.
    ///
    /// THE HAZARD THIS TYPE EXISTS FOR, stated first because it is the whole
    /// reason the policy is total. Component 1166 is not opt-in per creature.
    /// The manta prefab carries <c>AgeVisualizer</c>, whose only
    /// <c>[Require]</c> is an <c>AgeStateReader</c>; it is inert today ONLY
    /// because nobody answers 1166 and the visualiser therefore never activates.
    /// The instant a 1166 branch exists in <c>ComponentsSerializer</c>, the
    /// visualiser activates on EVERY manta that is served it and unconditionally
    /// assigns
    /// <c>localScale = Vector3.one * Lerp(0.25, 1.0, secondsOld/secondsTillFullyGrown)</c>.
    /// There is no "leave it alone" value. An adult must therefore be sent an
    /// explicit <c>secondsOld &gt;= secondsTillFullyGrown</c> or the whole
    /// world's mantas shrink to a quarter at once.
    ///
    /// So: <see cref="For"/> is TOTAL. Its default branch is
    /// <see cref="Adult"/>, every degenerate input falls into that branch, and
    /// the juvenile case is the narrow exception that has to argue for itself.
    /// A bug in the birth arithmetic can only ever produce a full-sized manta,
    /// which is what the world looks like today.
    ///
    /// PROVENANCE.
    /// <list type="bullet">
    /// <item><b>RECOVERED</b>: the wire shape (five fields, three int seconds and
    ///   two float kilogrammes); that the server is the sole author (there is no
    ///   <c>AgeStateWriter</c> anywhere in the decompiled client); the growth
    ///   formula; and the scale endpoints 0.25 and 1.0 read off the shipped
    ///   <c>MantaRay_unityclient</c> prefab's <c>AgeVisualizer</c>, applied to
    ///   the ENTITY ROOT.</item>
    /// <item><b>WAREBORN TUNING</b>: every DURATION.
    ///   <see cref="SecondsTillFullyGrown"/>,
    ///   <see cref="SecondsTillNaturalDeath"/> and both masses are ours. Retail's
    ///   were GSim's and GSim is not preserved.</item>
    /// </list>
    ///
    /// ONE CORRECTION TO THE PHASE 5 PLAN, recorded because it changes what a
    /// later reader may assume: <c>secondsTillFullyGrown</c> is NOT
    /// prefab-serialized. <c>AgeVisualizer.Settings</c> is exactly
    /// <c>{ Transform transformToScale; float birthScale; float fullyGrownScale; }</c>
    /// - three fields, 52 bytes, no slack - and every consumer in the client
    /// (<c>AgeVisualizer</c>, <c>RayAging</c>, <c>CreatureAudioExpressionClient</c>,
    /// <c>AgeBehaviour</c>) reads the duration off <c>AgeStateReader</c>, i.e.
    /// off the WIRE. There is nothing to read; the number is ours to choose, and
    /// it is chosen below.
    /// </summary>
    public static class IslandFaunaAge
    {
        /// <summary>
        /// The operator switch for juveniles - proposals B and C together.
        /// Named after <see cref="IslandFaunaPolicy.EnabledEnvVar"/> and
        /// <c>WAREBORN_ISLAND_FAUNA_ECOLOGY</c>, and it accepts the same tokens
        /// through the same <see cref="IslandFaunaPolicy.EnabledFrom"/>.
        ///
        /// OFF must be BYTE-IDENTICAL ON THE WIRE, and that is why the flag is
        /// consulted in exactly one place per effect: with it off, the service
        /// hands the serializer no age at all (so 1166 falls through to the
        /// unhandled path it takes today) and the evaluator passes no family (so
        /// <see cref="IslandFaunaSchool.MemberOffset"/> is the function it has
        /// always been).
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_ISLAND_FAUNA_JUVENILES";

        /// <summary>
        /// The newborn scale on the shipped <c>MantaRay_unityclient</c> prefab.
        /// RECOVERED - read out of the MonoBehaviour body in
        /// <c>resources.assets</c>, where <c>AgeVisualizer.Settings</c> occupies
        /// 52 bytes with no trailing slack. A newborn manta is a QUARTER of an
        /// adult's linear size, which is 1/64 of its volume, and because
        /// <c>transformToScale</c> is the entity ROOT its colliders and
        /// <c>CreatureBounds</c> sphere shrink with it.
        ///
        /// This server cannot change it and does not want to: it is the smallest
        /// a manta can be drawn, authored by Bossa.
        /// </summary>
        public const double RecoveredBirthScale = 0.25;

        /// <summary>The adult scale on the same prefab. RECOVERED; see <see cref="RecoveredBirthScale"/>.</summary>
        public const double RecoveredFullyGrownScale = 1.0;

        /// <summary>
        /// HOW LONG A MANTA TAKES TO GROW UP: one full nominal population cycle.
        ///
        /// WAREBORN TUNING, but not a free number - it is TIED to the rhythm
        /// rather than picked, and the tie is what makes it defensible. A calf
        /// slot is expressed for roughly half a cycle (the plan's measurement:
        /// 46-49%), so a maturation of one whole cycle means a calf is still
        /// visibly a calf for the entire window in which a player can see it -
        /// it enters at a quarter scale and leaves at about six tenths - and
        /// then the slot withdraws. Maturation faster than that would have
        /// calves reaching adult size while still expressed, which is a manta
        /// that grew in front of you for no reason; slower would make the size
        /// difference a permanent property of a slot, which is proposal B's
        /// explicitly rejected "mesh variant" reading.
        ///
        /// Read off <see cref="IslandFaunaRhythm.NominalCycleSeconds"/> rather
        /// than written as 1530, so retuning a phase duration moves this with it.
        /// </summary>
        public static int SecondsTillFullyGrown { get; } =
            (int)Math.Round(IslandFaunaRhythm.NominalCycleSeconds);

        /// <summary>
        /// WAREBORN TUNING, and inert: nothing on a manta consumes it. It exists
        /// as a non-zero number because <c>RayAging.GetLifeLivedRatio</c> divides
        /// by it and a zero denominator is the NaN this file is careful about.
        /// A hundred maturations away, which is this server's way of saying it
        /// does not model mortality (proposal H is declined for Phase 5).
        /// </summary>
        public static int SecondsTillNaturalDeath { get; } = SecondsTillFullyGrown * 100;

        /// <summary>
        /// An adult manta's mass. WAREBORN TUNING - no retail mass survives -
        /// and client-side only a divisor in the UnityWorker's
        /// <c>MovementController</c>, which this server does not run. Non-zero
        /// for that divisor's sake.
        /// </summary>
        public const float MaxMassKilograms = 400.0f;

        /// <summary>
        /// A newborn's mass: the adult's, scaled by the CUBE of the RECOVERED
        /// birth scale, because a quarter as long is a sixty-fourth the volume.
        /// The number is tuning; the RATIO is the prefab's.
        /// </summary>
        public const float MinMassKilograms =
            (float)(MaxMassKilograms * RecoveredBirthScale
                * RecoveredBirthScale * RecoveredBirthScale);

        /// <summary>
        /// THE SAFE ANSWER, and the default branch of <see cref="For"/>: an
        /// animal that is exactly, explicitly fully grown. The client computes
        /// <c>Clamp01(1) = 1</c> and renders at
        /// <see cref="RecoveredFullyGrownScale"/> - the size every manta in the
        /// world is today.
        /// </summary>
        public static FaunaAgeState Adult { get; } = new FaunaAgeState(
            SecondsOld: SecondsTillFullyGrown,
            SecondsTillFullyGrown: SecondsTillFullyGrown,
            SecondsTillNaturalDeath: SecondsTillNaturalDeath,
            MaxMassKilograms: MaxMassKilograms,
            MinMassKilograms: MinMassKilograms);

        /// <summary>
        /// THE TOTAL POLICY. An age in seconds becomes a wire state, and every
        /// input that is not a plain finite juvenile age becomes
        /// <see cref="Adult"/>.
        ///
        /// The guards are listed rather than implied because each one is a bug
        /// somebody could otherwise ship:
        /// <list type="bullet">
        /// <item><b>NaN</b> - <c>0/0</c> in the client is <c>NaN</c>,
        ///   <c>Clamp01(NaN)</c> is <c>NaN</c>, and a <c>NaN</c> localScale makes
        ///   the renderer misbehave in ways that do not name themselves.</item>
        /// <item><b>Infinity</b> - clamps to adult on the client, which is
        ///   merely wrong rather than broken, and is caught here anyway.</item>
        /// <item><b>Negative</b> - an animal born in the future. The arithmetic
        ///   that produced it is wrong; a full-sized manta is the failure a
        ///   player cannot see.</item>
        /// <item><b>Absurd</b> - anything at or past maturity is an adult by
        ///   definition, and is capped so the wire never carries a growth ratio
        ///   above one.</item>
        /// </list>
        /// </summary>
        public static FaunaAgeState For(double ageSeconds)
        {
            if (double.IsNaN(ageSeconds) || double.IsInfinity(ageSeconds)
                || ageSeconds < 0.0 || ageSeconds >= SecondsTillFullyGrown)
            {
                return Adult;
            }
            int seconds = (int)Math.Floor(ageSeconds);
            if (seconds < 0 || seconds >= SecondsTillFullyGrown)
            {
                return Adult;
            }
            return Adult with { SecondsOld = seconds };
        }

        /// <summary>
        /// THE WHOLE AGE POLICY, in one total function: what component 1166
        /// should carry for one creature at one instant.
        ///
        /// It is stated here rather than in the service so that the world-wide
        /// claim - "every manta that is not a calf renders at full size, on every
        /// island, at every instant" - can be swept by a test rather than
        /// reasoned about. The service is three lines of glue around it, and the
        /// glue is what tells this function WHICH slot is a calf; everything else
        /// is decided here.
        ///
        /// EVERY PATH BUT ONE RETURNS <see cref="Adult"/>. A non-manta, a slot
        /// that is not a calf slot, a slot with no birth instant (inside the
        /// population floor, not currently expressed, or born before this process
        /// started), and any degenerate arithmetic all land on the adult branch.
        /// This is Hazard 0's containment: a bug anywhere in the birth inversion
        /// can only produce a full-sized manta, which is what the world looks
        /// like today.
        /// </summary>
        public static FaunaAgeState StateFor(
            int worldSeed, IslandId islandId, FaunaSpecies species,
            int capacity, int speciesRank, bool isCalfSlot, double elapsedSeconds)
        {
            if (species != FaunaSpecies.MantaRay || !isCalfSlot)
            {
                return Adult;
            }
            double? age = AgeSeconds(
                worldSeed, islandId, species, capacity, speciesRank, elapsedSeconds);
            return age == null ? Adult : For(age.Value);
        }

        /// <summary>
        /// WHEN MEMBER <paramref name="speciesRank"/> OF A SPECIES ON AN ISLAND
        /// LAST ENTERED THE EXPRESSED PREFIX, in the same elapsed-seconds clock
        /// every other fauna function uses - or null for "it has always been
        /// here", "it is not here now", or "the walk could not find it".
        ///
        /// THIS IS PROPOSAL A, AND IT IS AN EXACT INVERSE RATHER THAN A SEARCH.
        /// The checkout layer shows a peer a PREFIX of a species' stable id list,
        /// of length <see cref="IslandFaunaRhythm.ExpressedCount"/>. During a
        /// rising phase the expressed fraction climbs a smoothstep, so the count
        /// crosses each integer EXACTLY ONCE, and that crossing is - mechanically,
        /// already, today - a birth: one animal appears alone in a school that was
        /// smaller a moment ago. The smoothstep <c>S(f) = f^2(3-2f)</c> has a
        /// closed-form inverse
        /// <c>S^-1(y) = 1/2 - sin(asin(1-2y)/3)</c>, exact and total on [0,1], so
        /// the instant of that crossing needs NO NEW STATE and NO NEW HASH
        /// CHANNEL. Nothing here is stored; nothing here is integrated.
        ///
        /// A BIRTH IS AN INCREASE IN EXPRESSION, NEVER A NEW ID. Appending an id
        /// per birth would shift every later id and destroy the prefix-stability
        /// property that stops population swings reshuffling which animal is
        /// which. This function READS the prefix rule rather than replacing it,
        /// so the property is preserved exactly.
        ///
        /// TWO THINGS IT MUST GET RIGHT, both of them collisions with the Phase 3
        /// rhythm fix:
        /// <list type="number">
        /// <item><b>Read the floor, never re-derive it.</b> The floor is
        ///   proportional (<c>max(min(2,cap), round(cap * TroughLevel))</c>), so
        ///   on a twelve-animal island the permanent resident core is four, not
        ///   two. Members inside the floor NEVER LEAVE and therefore have no
        ///   birth instant at all; this function returns null for them and the
        ///   caller reads that as "adult". The threshold below is computed from
        ///   <see cref="IslandFaunaRhythm.ExpressedCount"/>'s own arithmetic.</item>
        /// <item><b>Apply the per-island start offset.</b> Every island's walk is
        ///   advanced by <see cref="IslandFaunaRhythm.StartOffsetSeconds"/>. An
        ///   inversion that ignored it would be wrong by up to a full cycle -
        ///   1530 s - which is longer than a calf's whole visible life. The walk
        ///   below adds it, exactly as <see cref="IslandFaunaRhythm.At"/> does.</item>
        /// </list>
        ///
        /// AND THE PREDATOR LAG. A manta's expression is the PREY's rhythm
        /// evaluated a hashed lag behind, so the walk runs on the species' own
        /// clock. The lag is a constant offset, so a DIFFERENCE of two instants
        /// on that clock is the same number of seconds on the world clock, which
        /// is why an age can be computed there and used here without conversion.
        ///
        /// THE HONEST WEAKNESS, stated rather than hidden: under this scheme
        /// NOBODY IS ANYONE'S OFFSPRING, and the same animal is born again every
        /// cycle. Member 7 enters the prefix during Growing, leaves during
        /// Collapse, and re-enters next cycle. It is a level wearing a life. A
        /// player cannot perceive the difference - nobody watches one island for
        /// twenty-five minutes - but the code should say so rather than claim a
        /// lineage it does not have.
        /// </summary>
        /// <param name="capacity">The island's seeded slot count for this
        /// species - the length of the id list the prefix is taken from.</param>
        /// <param name="speciesRank">The creature's zero-based position in that
        /// list. It enters the prefix when the expressed count reaches
        /// <c>speciesRank + 1</c>.</param>
        public static double? BirthElapsedSeconds(
            int worldSeed, IslandId islandId, FaunaSpecies species,
            int capacity, int speciesRank, double elapsedSeconds)
        {
            if (capacity <= 0 || speciesRank < 0 || speciesRank >= capacity
                || double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds))
            {
                return null;
            }

            // Members inside the floor are the permanent resident core: the
            // expressed count is clamped up to the floor, so they are never
            // withdrawn and were never born. Read the floor off the rhythm.
            if (speciesRank < FloorOf(capacity))
            {
                return null;
            }

            // Not expressed right now means there is nothing to be the age OF.
            if (IslandFaunaRhythm.ExpressedCount(capacity,
                    IslandFaunaRhythm.ExpressionAt(worldSeed, islandId, species, elapsedSeconds))
                <= speciesRank)
            {
                return null;
            }

            // The species' OWN clock - the predator's is the prey's, lagged - and
            // the same negative-clamp-then-offset order IslandFaunaRhythm.At uses,
            // because an island's identity must still decide where it is.
            double speciesSeconds = SpeciesSeconds(worldSeed, islandId, species, elapsedSeconds);

            // BEFORE THE SPECIES' OWN CLOCK STARTS THERE IS NO HISTORY TO READ.
            // A manta's rhythm is the prey's evaluated a hashed lag (120-360 s)
            // behind, and IslandFaunaRhythm.At clamps a negative input to zero -
            // so for the first lag seconds of a process every instant reports the
            // same phase position and an inversion there would answer with the
            // clamp rather than with a crossing. Null, which the caller reads as
            // "adult", is the only honest answer in that window.
            if (speciesSeconds < 0.0) return null;
            double tau = speciesSeconds
                + IslandFaunaRhythm.StartOffsetSeconds(worldSeed, islandId);

            // The expressed count reaches speciesRank+1 when round(capacity * e)
            // does, i.e. when e crosses (speciesRank + 0.5) / capacity. The exact
            // midpoint is measure zero in continuous time; .NET rounds it to even
            // and the instant returned is the mathematical crossing either way.
            double threshold = (speciesRank + 0.5) / capacity;

            return CrossingElapsedSeconds(worldSeed, islandId, tau, threshold, speciesSeconds,
                elapsedSeconds);
        }

        /// <summary>
        /// HOW OLD IS THAT ANIMAL RIGHT NOW - <see cref="BirthElapsedSeconds"/>
        /// subtracted from the clock, or null when it has no birth instant.
        /// Never negative: a clock that ran backwards is a bug, and zero is the
        /// answer that cannot render wrongly.
        /// </summary>
        public static double? AgeSeconds(
            int worldSeed, IslandId islandId, FaunaSpecies species,
            int capacity, int speciesRank, double elapsedSeconds)
        {
            double? born = BirthElapsedSeconds(
                worldSeed, islandId, species, capacity, speciesRank, elapsedSeconds);
            if (born == null) return null;
            double age = elapsedSeconds - born.Value;
            return age < 0.0 ? 0.0 : age;
        }

        /// <summary>
        /// The exact inverse of the rhythm's smoothstep,
        /// <c>S^-1(y) = 1/2 - sin(asin(1-2y)/3)</c> - total on [0,1] and verified
        /// against <c>S</c> to 1e-12 across the interval. Outside [0,1] it
        /// saturates rather than returning a complex number.
        /// </summary>
        public static double InverseSmoothStep(double y)
        {
            if (double.IsNaN(y)) return 0.0;
            if (y <= 0.0) return 0.0;
            if (y >= 1.0) return 1.0;
            return 0.5 - Math.Sin(Math.Asin(1.0 - (2.0 * y)) / 3.0);
        }

        /// <summary>
        /// The floor <see cref="IslandFaunaRhythm.ExpressedCount"/> clamps up to,
        /// stated once so nothing re-derives it. Kept here rather than in the
        /// rhythm because it is this file that must not get it wrong; the
        /// arithmetic is copied from, and pinned to, the rhythm by test.
        /// </summary>
        public static int FloorOf(int capacity) =>
            capacity <= 0
                ? 0
                : Math.Max(Math.Min(2, capacity),
                    (int)Math.Round(capacity * IslandFaunaRhythm.TroughLevel));

        /// <summary>
        /// The clock the species' own rhythm runs on: the world clock for the
        /// prey, and the world clock minus the hashed predator lag for the manta.
        /// </summary>
        private static double SpeciesSeconds(
            int worldSeed, IslandId islandId, FaunaSpecies species, double elapsedSeconds) =>
            species == FaunaSpecies.JellyFish
                ? elapsedSeconds
                : elapsedSeconds - IslandFaunaRhythm.PredatorLagSeconds(worldSeed, islandId);

        /// <summary>
        /// How far back the walk will look before giving up: three nominal
        /// cycles' worth of phases. Beyond that the animal has been present
        /// since before anything a player could have watched, and "adult" is the
        /// honest and safe answer. The bound also stops a pathological seed
        /// turning an age lookup into an unbounded loop on the main thread.
        /// </summary>
        private const int MaxPhasesWalkedBack = 15;

        /// <summary>
        /// Walks the rhythm's phase sequence forward to <paramref name="tau"/>
        /// (which is exactly what <see cref="IslandFaunaRhythm.At"/> does, so the
        /// two cannot disagree about where a phase begins), remembering the last
        /// <see cref="MaxPhasesWalkedBack"/> phases, then scans them BACKWARDS
        /// for the most recent instant at which the expressed fraction crossed
        /// <paramref name="threshold"/> upwards.
        ///
        /// Only Growing and Recovery rise, so only they can carry a crossing.
        /// Dormant and Bloom hold and Collapse falls, so a crossing inside them
        /// is arithmetically impossible and the walk simply steps past.
        /// </summary>
        private static double? CrossingElapsedSeconds(
            int worldSeed, IslandId islandId, double tau, double threshold,
            double speciesSeconds, double elapsedSeconds)
        {
            int phaseCount = IslandFaunaRhythm.BasePhaseSeconds.Count;

            // Forward walk, remembering a bounded tail. Each entry is the phase's
            // ordinal and the tau at which it began.
            Span<int> phases = stackalloc int[MaxPhasesWalkedBack];
            Span<double> starts = stackalloc double[MaxPhasesWalkedBack];
            Span<double> durations = stackalloc double[MaxPhasesWalkedBack];
            int count = 0;

            double start = 0.0;
            int cycle = 0;
            bool found = false;
            int currentPhase = 0;
            double currentStart = 0.0;
            double currentDuration = 1.0;
            while (!found)
            {
                for (int phase = 0; phase < phaseCount; phase++)
                {
                    double duration = IslandFaunaRhythm.PhaseDuration(
                        worldSeed, islandId, cycle, phase);
                    if (tau - start < duration)
                    {
                        currentPhase = phase;
                        currentStart = start;
                        currentDuration = duration;
                        found = true;
                        break;
                    }
                    Remember(phases, starts, durations, ref count, phase, start, duration);
                    start += duration;
                }
                if (!found) cycle++;
            }

            // The partial phase we are standing in, then the remembered tail,
            // newest first.
            double? hit = CrossingIn(currentPhase, currentStart, currentDuration,
                (tau - currentStart) / currentDuration, threshold);
            if (hit == null)
            {
                for (int i = count - 1; i >= 0 && hit == null; i--)
                {
                    hit = CrossingIn(phases[i], starts[i], durations[i], 1.0, threshold);
                }
            }
            if (hit == null) return null;

            // tau -> species clock -> world clock. The species clock is the world
            // clock shifted by a constant lag, so the shift cancels in the age;
            // it is undone here anyway so the returned instant is a real world
            // time a log line can print.
            double offset = IslandFaunaRhythm.StartOffsetSeconds(worldSeed, islandId);
            double birthSpeciesSeconds = hit.Value - offset;

            // A CROSSING BEFORE THE SPECIES' CLOCK STARTED IS NOT A BIRTH. The
            // island's hashed start offset drops it anywhere in its cycle at
            // t=0, so the most recent crossing of a slot that booted already
            // expressed can sit before elapsed zero. That animal was not born
            // here, it was SEEDED here, and null - read by the caller as "adult"
            // - is the honest answer. The practical effect is that for up to one
            // cycle after a restart the calf slots that booted expressed render
            // full size until they withdraw and come back, which is a truer
            // statement than declaring the world full of newborns on every
            // restart.
            if (birthSpeciesSeconds < 0.0) return null;

            double birth = elapsedSeconds - (speciesSeconds - birthSpeciesSeconds);
            if (double.IsNaN(birth) || double.IsInfinity(birth)) return null;
            if (birth < 0.0 || birth > elapsedSeconds + 1e-9) return null;
            return birth;
        }

        private static void Remember(Span<int> phases, Span<double> starts, Span<double> durations,
            ref int count, int phase, double start, double duration)
        {
            if (count == MaxPhasesWalkedBack)
            {
                for (int i = 1; i < MaxPhasesWalkedBack; i++)
                {
                    phases[i - 1] = phases[i];
                    starts[i - 1] = starts[i];
                    durations[i - 1] = durations[i];
                }
                count--;
            }
            phases[count] = phase;
            starts[count] = start;
            durations[count] = duration;
            count++;
        }

        /// <summary>
        /// The tau at which one phase's expressed fraction crossed
        /// <paramref name="threshold"/> upwards, considering only the part of the
        /// phase up to <paramref name="endFraction"/>, or null if it did not.
        /// </summary>
        private static double? CrossingIn(
            int phase, double start, double duration, double endFraction, double threshold)
        {
            if (duration <= 0.0) return null;
            if (endFraction < 0.0) return null;
            if (endFraction > 1.0) endFraction = 1.0;

            (double from, double to) = RampOf((FaunaPopulationPhase)phase);
            if (to <= from) return null;                 // a hold or a collapse
            if (threshold <= from) return null;          // already expressed at the start
            if (threshold > to) return null;             // never reached in this phase

            double y = (threshold - from) / (to - from);
            double f = InverseSmoothStep(y);
            return f > endFraction ? (double?)null : start + (f * duration);
        }

        /// <summary>
        /// A phase's expressed fraction at its start and at its end - the same
        /// levels <see cref="IslandFaunaRhythm.PreyExpressionAt"/> joins with
        /// smoothsteps, restated as endpoints so a crossing test can bracket
        /// them. A phase whose end is not above its start cannot carry a birth.
        /// </summary>
        private static (double From, double To) RampOf(FaunaPopulationPhase phase) => phase switch
        {
            FaunaPopulationPhase.Dormant =>
                (IslandFaunaRhythm.DormantLevel, IslandFaunaRhythm.DormantLevel),
            FaunaPopulationPhase.Growing => (IslandFaunaRhythm.DormantLevel, 1.0),
            FaunaPopulationPhase.Bloom => (1.0, 1.0),
            FaunaPopulationPhase.Collapse => (1.0, IslandFaunaRhythm.TroughLevel),
            _ => (IslandFaunaRhythm.TroughLevel, IslandFaunaRhythm.DormantLevel),
        };
    }
}
