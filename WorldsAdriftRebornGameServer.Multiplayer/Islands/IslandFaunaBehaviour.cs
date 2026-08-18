namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>What a school is DOING. The vocabulary of Phase 4.</summary>
    public enum FaunaBehaviour
    {
        /// <summary>The neutral state: the Phase 2 field-following orbit, unmodified.</summary>
        Cruise,

        /// <summary>A tight pass over the bloom's centre - the orbit pinches in and relaxes.</summary>
        Feed,

        /// <summary>
        /// The school sinks below the island's underside, holds, and climbs
        /// back. While deep it is NOT STREAMED - disappearance is both the
        /// believability ("they went under the rock") and the streaming LOD the
        /// architecture prescribes: a school under the island needs no members
        /// on any peer's wire, which is also what makes big populations
        /// affordable against the 333 ms/creature arrival cost.
        /// </summary>
        Dive,

        /// <summary>The school crosses from its bloom to the island's other one.</summary>
        Migrate,
    }

    /// <summary>
    /// One group's CURRENT behaviour segment - the published (behaviour, epoch)
    /// pair of the target architecture, plus the two numbers that make it
    /// self-describing on the wire: how long the segment runs and which bloom a
    /// migration is heading to. This is the whole interface between the
    /// schedule and every consumer; the browser never re-derives the schedule
    /// (it would need the admin-only world seed), it evaluates exactly this.
    /// </summary>
    public readonly record struct FaunaGroupBehaviour(
        FaunaBehaviour Behaviour,
        double EpochSeconds,
        double DurationSeconds,
        int FromBloom,
        int ToBloom);

    /// <summary>
    /// SCHOOL BEHAVIOURS AS A DETERMINISTIC SCHEDULE, and the modifiers each one
    /// applies to the Phase 2 motion.
    ///
    /// THE ARCHITECTURE'S ONE PERMITTED PIECE OF STATE is a published
    /// (behaviour, epoch) pair per group. Phase 4 spends LESS than that budget:
    /// the schedule below is a pure hash-walk of segments - like the population
    /// rhythm, computable at any instant by anyone with the seed - and the
    /// published pair merely REPORTS it. The budget stays banked for the phase
    /// that genuinely needs state: reaction to players
    /// (behaviour=Scatter, epoch=now), which is deferred and not built here.
    ///
    /// EVERY EXCURSION IS NEUTRAL AT ITS EDGES, and that one property carries
    /// most of the design. Each segment starts and ends in the Cruise pose (the
    /// in-hold-out <see cref="Bump"/> envelope is zero-valued and
    /// zero-derivative at both ends; a migration ends fully AT its destination
    /// bloom), which buys three things at once: the motion is C1 across every
    /// segment boundary; a map poll that is up to three seconds stale shows a
    /// group briefly cruising instead of anything discontinuous, because both
    /// descriptions agree at the boundary by construction; and the wire never
    /// carries a teleport, which a player reads as a despawn.
    ///
    /// THE ORBIT ANGLE NEVER CHANGES RATE. A feed pinches the orbit RADIUS while
    /// the angle stays the same linear function of the clock - scaling the
    /// angular rate would make the angle an integral of history, which is the
    /// state this whole feature refuses to hold.
    ///
    /// PROVENANCE: the schedule and its numbers are WAREBORN TUNING. The
    /// VOCABULARY is the recovered one - retail's ConductType carries Feeding
    /// and Patrolling, its flocks migrated between habitats
    /// (HabitatState.incoming/outgoingFlocks), and the recovered jelly day rule
    /// already had wildlife spending hours under the island's rock, which is
    /// what makes Dive read as native rather than invented. The conduct PICKER
    /// is lost (plan 2.6); a time-driven picker is tuning wearing a recovered
    /// vocabulary, and says so.
    /// </summary>
    public static class IslandFaunaBehaviour
    {
        /// <summary>How deep below the island's floor a dive holds, as a fraction of its height.</summary>
        public const double DiveBelowFloorFraction = 0.15;

        /// <summary>How far a feed pinches the orbit radius (1 -> 0.5 at the deepest).</summary>
        public const double FeedRadiusPinch = 0.5;

        /// <summary>The dive fraction above which a group stops being streamed.</summary>
        public const double UnstreamedAboveDiveFraction = 0.9;

        /// <summary>The in/out ramp of every excursion, as a fraction of its segment.</summary>
        public const double ExcursionRampFraction = 0.25;

        /// <summary>
        /// The segment a group is in at <paramref name="elapsedSeconds"/>. A
        /// hash-walk exactly like the rhythm's: segment kinds and durations come
        /// from the seed, and a group's CURRENT bloom is its round-robin start
        /// plus every migration completed before now - so "which bloom" is
        /// itself a pure function of the clock. Single-bloom islands never
        /// migrate; their draw becomes a cruise.
        /// </summary>
        public static FaunaGroupBehaviour SegmentAt(
            int worldSeed, IslandId islandId, FaunaSpecies species, int groupIndex,
            IslandTerrainEnvelope envelope, int bloomCount, double elapsedSeconds)
        {
            double remaining = elapsedSeconds < 0.0 ? 0.0 : elapsedSeconds;
            double start = 0.0;
            int bloom = IslandFaunaEcology.BloomIndexFor(groupIndex, bloomCount);
            int segment = 0;
            while (true)
            {
                FaunaBehaviour behaviour = KindOf(
                    Unit(worldSeed, islandId, species, groupIndex, segment, channel: 1),
                    bloomCount);
                double duration = DurationOf(behaviour,
                    Unit(worldSeed, islandId, species, groupIndex, segment, channel: 2));
                if (behaviour == FaunaBehaviour.Migrate)
                {
                    // A MIGRATION TAKES AS LONG AS THE CROSSING DEMANDS. The
                    // blend's peak speed is 1.5 x separation / duration
                    // (smoothstep's centre slope), and the separation between two
                    // blooms is bounded by their shared ring; flooring the
                    // duration at 7.2 x floor / speed holds that peak to about
                    // HALF the species' cruise speed, so a migrating school
                    // reads as travelling, never as launched. Retail agrees in
                    // spirit: a flock's travel time was emergent from its flight
                    // speed, never a constant (findings, migration section).
                    duration = Math.Max(duration, MinimumMigrateSeconds(species, envelope));
                }
                int toBloom = behaviour == FaunaBehaviour.Migrate && bloomCount > 1
                    ? (bloom + 1) % bloomCount
                    : bloom;

                if (remaining < duration)
                {
                    return new FaunaGroupBehaviour(behaviour, start, duration, bloom, toBloom);
                }
                remaining -= duration;
                start += duration;
                bloom = toBloom;
                segment++;
            }
        }

        /// <summary>The distance-honest migration floor. See the comment at its use.</summary>
        public static double MinimumMigrateSeconds(
            FaunaSpecies species, IslandTerrainEnvelope envelope) =>
            7.2 * IslandFaunaEcology.ClearanceFloorMetres(species, envelope)
                / IslandFaunaEcology.OrbitMetresPerSecondFor(species);

        /// <summary>
        /// The draw: half the time a school just cruises, because behaviour that
        /// never rests reads as a slot machine. WAREBORN TUNING.
        /// </summary>
        private static FaunaBehaviour KindOf(double u, int bloomCount)
        {
            if (u < 0.50) return FaunaBehaviour.Cruise;
            if (u < 0.75) return FaunaBehaviour.Feed;
            if (u < 0.90) return FaunaBehaviour.Dive;
            return bloomCount > 1 ? FaunaBehaviour.Migrate : FaunaBehaviour.Cruise;
        }

        /// <summary>Minutes-scale segments; long cruises, shorter excursions.</summary>
        private static double DurationOf(FaunaBehaviour behaviour, double u) => behaviour switch
        {
            FaunaBehaviour.Feed => 120.0 + (120.0 * u),
            FaunaBehaviour.Dive => 240.0 + (240.0 * u),
            FaunaBehaviour.Migrate => 150.0 + (90.0 * u),
            _ => 240.0 + (240.0 * u),
        };

        /// <summary>
        /// How far through its segment a descriptor is at <paramref name="elapsedSeconds"/>,
        /// clamped to [0,1] - past the end is 1, which for every excursion is
        /// the neutral pose, so a stale descriptor degrades to Cruise instead of
        /// extrapolating.
        /// </summary>
        public static double SegmentFraction(FaunaGroupBehaviour segment, double elapsedSeconds)
        {
            if (segment.DurationSeconds <= 0.0) return 1.0;
            double f = (elapsedSeconds - segment.EpochSeconds) / segment.DurationSeconds;
            return f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
        }

        /// <summary>
        /// The in-hold-out envelope: smoothsteps up over the first quarter of the
        /// segment, holds at 1, smoothsteps back down over the last quarter.
        /// Zero value AND zero derivative at both ends - the neutral-edges
        /// property everything above leans on.
        /// </summary>
        public static double Bump(double f)
        {
            double rise = SmoothStep(f / ExcursionRampFraction);
            double fall = SmoothStep((1.0 - f) / ExcursionRampFraction);
            return Math.Min(rise, fall);
        }

        /// <summary>The orbit radius multiplier: a feed pinches in, everything else is neutral.</summary>
        public static double RadiusMultiplier(FaunaGroupBehaviour segment, double elapsedSeconds) =>
            segment.Behaviour == FaunaBehaviour.Feed
                ? 1.0 - (FeedRadiusPinch * Bump(SegmentFraction(segment, elapsedSeconds)))
                : 1.0;

        /// <summary>How deep into its dive a group is, 0..1. Zero for everything but Dive.</summary>
        public static double DiveFraction(FaunaGroupBehaviour segment, double elapsedSeconds) =>
            segment.Behaviour == FaunaBehaviour.Dive
                ? Bump(SegmentFraction(segment, elapsedSeconds))
                : 0.0;

        /// <summary>How far across its migration a group is, 0..1 monotone. Zero unless migrating.</summary>
        public static double MigrationBlend(FaunaGroupBehaviour segment, double elapsedSeconds) =>
            segment.Behaviour == FaunaBehaviour.Migrate && segment.FromBloom != segment.ToBloom
                ? SmoothStep(SegmentFraction(segment, elapsedSeconds))
                : 0.0;

        /// <summary>
        /// The altitude a dive holds at: below the island's own floor by a
        /// fraction of its height, so "under the rock" scales with the rock.
        /// </summary>
        public static double DivedAltitude(IslandTerrainEnvelope envelope) =>
            envelope.MinY - ((envelope.MaxY - envelope.MinY) * DiveBelowFloorFraction);

        /// <summary>
        /// Whether this group's members are streamed to peers right now. False
        /// only while a dive is deep - the LOD half of Dive's double duty.
        /// Streaming flips at the two Bump crossings of the threshold per dive:
        /// a whole-group departure and a whole-group return, at the checkout
        /// layer's own pace, never a flicker (Bump is monotone on each side of
        /// its hold).
        /// </summary>
        public static bool IsStreamed(FaunaGroupBehaviour segment, double elapsedSeconds) =>
            DiveFraction(segment, elapsedSeconds) <= UnstreamedAboveDiveFraction;

        private static double SmoothStep(double t)
        {
            if (t <= 0.0) return 0.0;
            if (t >= 1.0) return 1.0;
            return t * t * (3.0 - (2.0 * t));
        }

        /// <summary>The FNV-1a uniform, tagged "behaviour" so no other fauna hash can collide.</summary>
        public static double Unit(int worldSeed, IslandId islandId, FaunaSpecies species,
            int groupIndex, int segment, int channel)
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
            Mix("behaviour");
            Mix(worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(islandId.ToString());
            Mix(((int)species).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(groupIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(segment.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mix(channel.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return hash / 4294967296.0;
        }
    }
}
