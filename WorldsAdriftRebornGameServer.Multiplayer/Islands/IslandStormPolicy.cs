using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>Where one island is in its understorm cycle right now.</summary>
    public enum IslandStormPhase
    {
        /// <summary>No storm soon. The client shows and hears nothing.</summary>
        Quiet = 0,

        /// <summary>
        /// Inside the client's own 30 s warning window: a player within 300 m gets
        /// a rumble and camera shake that ramps as the countdown falls. PROVED,
        /// <c>acs/IslandLightningTimerVisualizer.cs:161-167</c>.
        /// </summary>
        Telegraph = 1,

        /// <summary>
        /// The storm is running: bolts strike upward from the death clouds into the
        /// island's own surface. Driven ENTIRELY by
        /// <c>estimatedMilliTillLightningEnd &gt; 0</c>. PROVED, <c>:226</c>.
        /// </summary>
        Active = 2,
    }

    /// <summary>One island's cycle state at one instant.</summary>
    public readonly struct IslandStormSample
    {
        public IslandStormSample(IslandStormPhase phase, int millisTillNextLightning,
            int millisTillLightningEnd, long generation)
        {
            Phase = phase;
            MillisTillNextLightning = millisTillNextLightning;
            MillisTillLightningEnd = millisTillLightningEnd;
            Generation = generation;
        }

        public IslandStormPhase Phase { get; }

        /// <summary>
        /// 1254 <c>estimatedMilliTillNextLightning</c>: milliseconds until this
        /// island's next storm STARTS, or 0 while one is running.
        /// </summary>
        public int MillisTillNextLightning { get; }

        /// <summary>
        /// 1254 <c>estimatedMilliTillLightningEnd</c>: milliseconds until the
        /// running storm ends, or 0 when none is running. THIS IS THE STORM SWITCH -
        /// the client's <c>IsLightningActive</c> is literally
        /// <c>EstimatedMilliTillLightningEnd &gt; 0</c>.
        /// </summary>
        public int MillisTillLightningEnd { get; }

        /// <summary>
        /// 1254 <c>generation</c>: which storm cycle this is, counting from 1.
        ///
        /// It has ZERO client readers (PROVED - a whole-decompile sweep finds
        /// exactly two consumers of 1254, <c>IslandLightningTimerVisualizer</c> and
        /// <c>IslandLocalTransformBehaviour</c>, and neither touches
        /// <c>Generation</c>). It is carried anyway because it is the only field on
        /// the component that says WHICH storm a client is looking at, which is what
        /// makes a server log and a player report line up.
        /// </summary>
        public long Generation { get; }

        public override string ToString() =>
            "storm gen " + Generation + " " + Phase
            + " (next=" + MillisTillNextLightning + "ms end=" + MillisTillLightningEnd + "ms)";
    }

    /// <summary>
    /// WHEN each island's understorm happens, and what the two integers on 1254 say
    /// at any instant. Pure: no ENet, no Improbable types, no game install, no
    /// clock of its own - every entry point takes the elapsed time it should answer
    /// for, so a whole 105-minute cycle is asserted on in microseconds.
    ///
    /// WHAT AN UNDERSTORM IS. Lightning from the death clouds below strikes the
    /// island's underside, resets its resources, and lasts under a minute. It does
    /// NOT move and it has no radius: 1254 carries no position and the visualiser
    /// that renders it lives ON the island entity and samples ITS OWN surface
    /// (PROVED, <c>acs/IslandLightningTimerVisualizer.cs:239-240</c>). So an
    /// understorm is a per-island timer and nothing else, which is why this file is
    /// arithmetic rather than a simulation.
    ///
    /// THE ONE THING THIS MUST NEVER DO. <c>isLightningActive</c> is not written -
    /// not here, not by the service, not by the wire. Nothing in this assembly can
    /// even express it. See <see cref="IslandStormPush"/> for why that matters and
    /// what it costs (nothing: the visualiser reads the INT, not the bool).
    ///
    /// DETERMINISTIC. Given the same island ids, cadence, jitter and duration, two
    /// servers storm at the same instants for ever. The per-island offset is a
    /// stable hash of the island's ID STRING, not of its registration order, so
    /// adding a thirteenth island does not reshuffle the other twelve's schedules.
    ///
    /// NOT THREAD-SAFE and it does not need to be: it holds no state.
    /// </summary>
    public static class IslandStormPolicy
    {
        // ------------------------------------------------------------------------
        // OPERATOR KNOBS
        // ------------------------------------------------------------------------

        /// <summary>
        /// The master switch, and it arrives OFF.
        ///
        /// Every prior feature in this server that pushes an unsolicited component
        /// update to an already-checked-out entity landed behind its own flag
        /// (WAREBORN_SKY_WHALE, WAREBORN_ISLAND_FAUNA, WAREBORN_HELM_FLIGHT), and
        /// this one moves an entity type nobody has ever seen move: the ISLAND. With
        /// it off, this server is byte-identical on the wire to one built without
        /// this feature - 1254 is still seeded exactly as before and never updated.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_STORMS";

        /// <summary>How long between one island's storms, in seconds.</summary>
        public const string CadenceEnvVar = "WAREBORN_STORM_CADENCE_SECONDS";

        /// <summary>
        /// How far apart, as a fraction of the cadence, different islands' storms are
        /// spread. 0 makes the whole world storm in lockstep.
        /// </summary>
        public const string JitterEnvVar = "WAREBORN_STORM_JITTER_FRACTION";

        /// <summary>How long one storm runs, in seconds.</summary>
        public const string DurationEnvVar = "WAREBORN_STORM_DURATION_SECONDS";

        /// <summary>
        /// How often the countdown is re-sent during the 30 s warning window, in
        /// seconds. Floored at <see cref="MinCountdownRefreshSeconds"/> - see there
        /// for the client bug that makes a SHORTER refresh do nothing at all.
        /// </summary>
        public const string CountdownRefreshEnvVar = "WAREBORN_STORM_COUNTDOWN_REFRESH_SECONDS";

        // ------------------------------------------------------------------------
        // RECOVERED / PROVED CLIENT CONSTANTS - do not invent alternatives
        // ------------------------------------------------------------------------

        /// <summary>
        /// The client's warning window, in seconds. PROVED:
        /// <c>if (visualizer.IsLightningActive || visualizer.EstimatedTimeUntilLightningStarts &lt; 30f)</c>
        /// (<c>acs/IslandLightningTimerVisualizer.cs:161</c>), and the shake magnitude
        /// then ramps <c>InverseLerp(30f, 0f, t)</c>.
        /// </summary>
        public const double TelegraphSeconds = 30.0;

        /// <summary>
        /// How near the island a player's CAMERA must be to get the warning, in
        /// metres. PROVED: the client tests <c>sqrDistanceToBounds &lt; 90000f</c>,
        /// and 90000 = 300². Distance to the island's BOUNDS, not its origin, so it
        /// is 300 m from the rock rather than from the centre.
        ///
        /// Nothing on this server enforces it - it is the client's own test - but it
        /// is the number a maintainer needs in order to stand in the right place.
        /// </summary>
        public const double TelegraphRadiusMetres = 300.0;

        /// <summary>
        /// ⚠ THE CLIENT DOES NOT COUNT DOWN ON ITS OWN, AND THIS CONSTANT IS WHY
        /// THE COUNTDOWN HAS TO BE RE-SENT.
        ///
        /// <c>TimeEstimationSmoother.StepAndSmooth()</c> decrements a local
        /// <c>estimated</c>, computes a new smoothed value - and RETURNS it without
        /// ever assigning it (PROVED, read in the decompile:
        /// <c>smoothed</c> is a <c>{ get; private set; }</c> written in exactly ONE
        /// place, <c>OnUpdatedValue</c>, and only when <c>warp</c> is true; the
        /// caller <c>IslandLightningTimerVisualizer.Update()</c> discards the return
        /// value). It is a shipped bug.
        ///
        /// Consequence, and it is the whole shape of this feature:
        /// <c>EstimatedTimeUntilLightningStarts</c> is a STAIRCASE that only moves
        /// when the SERVER pushes a new value, and only then if
        /// <c>Mathf.Abs(newValue - smoothed) &gt; 7f</c>. A push that moves the
        /// countdown by five seconds is silently discarded and the warning never
        /// ramps. A server that pushes once and waits gets no warning at all,
        /// because the seeded 50 s never decays past 30.
        ///
        /// So: every countdown push must move the value by MORE than seven seconds,
        /// which is a floor on the refresh interval, not a ceiling on it.
        /// </summary>
        public const double ClientWarpThresholdSeconds = 7.0;

        /// <summary>
        /// The smallest countdown refresh interval that can do anything, in seconds.
        /// One second of headroom over <see cref="ClientWarpThresholdSeconds"/>, since
        /// the interval is also how far the value moves.
        /// </summary>
        public const double MinCountdownRefreshSeconds = 8.0;

        /// <summary>
        /// The strike cadence the shipped island prefab carries, in seconds:
        /// <c>_minTimeBetweenLightningSeconds = 0.0</c>,
        /// <c>_maxTimeBetweenLightningSeconds = 1.0</c>, and the client rolls
        /// <c>Lerp(min, max, Random.value)</c> afresh after each bolt.
        ///
        /// RECOVERED 2026-08-20 by reading the bundles' type trees with UnityPy
        /// (grep cannot see them - the bundles are compressed). Identical across
        /// every island sampled. A 45 s storm is therefore about 90 bolts.
        ///
        /// Recorded, not used: the server cannot change it. It is on the prefab, and
        /// changing it would be a client mod.
        /// </summary>
        public const double PrefabMinSecondsBetweenStrikes = 0.0;

        /// <inheritdoc cref="PrefabMinSecondsBetweenStrikes"/>
        public const double PrefabMaxSecondsBetweenStrikes = 1.0;

        // ------------------------------------------------------------------------
        // DEFAULTS
        // ------------------------------------------------------------------------

        /// <summary>
        /// RECOVERED, and it is the same number <see cref="TreeHarvest.UnderstormCadence"/>
        /// has been carrying unused since tree regrowth shipped: 105 minutes, the
        /// midpoint of the wiki's "resources reset every 1.5 to 2 hours".
        ///
        /// Retail's last content patch halved it ("Understorms now happen twice as
        /// often", Update 31), so ~52 min is arguably the truer late-retail number.
        /// 105 is kept because it is the value this repo already recorded and
        /// already tests, and because it is one env var away.
        /// </summary>
        public static readonly TimeSpan DefaultCadence = TreeHarvest.UnderstormCadence;

        /// <summary>
        /// WAREBORN TUNING. The wiki says "less than a minute" and nothing more
        /// precise survives, so 45 s is chosen inside that bound: long enough that a
        /// player who runs toward the island still sees bolts when they arrive,
        /// short enough that it does not outstay the 30 s that led up to it.
        /// </summary>
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(45);

        /// <summary>
        /// WAREBORN TUNING. Spreads islands' storms over the first fifth of each
        /// cadence, so they do not all fire at the same instant - the wiki's "these
        /// storms occur with varying frequency all across the map".
        /// </summary>
        public const double DefaultJitterFraction = 0.2;

        /// <summary>
        /// WAREBORN TUNING, but its FLOOR is not ours - see
        /// <see cref="ClientWarpThresholdSeconds"/>. Eight seconds puts roughly four
        /// steps into the 30 s warning (30 -> 22 -> 14 -> 6 -> storm), each one big
        /// enough to warp the client's smoother, and the client's own
        /// <c>TimeLerp(_curMag, target, dt, 0.25f)</c> turns that staircase back into
        /// a ramp on the way to the camera.
        /// </summary>
        public static readonly TimeSpan DefaultCountdownRefresh = TimeSpan.FromSeconds(8);

        /// <summary>
        /// The largest jitter fraction accepted. Above a half, one island's storm can
        /// overlap the next generation of another's and the "one world reset per
        /// cadence" rule stops being true.
        /// </summary>
        public const double MaxJitterFraction = 0.5;

        // ------------------------------------------------------------------------
        // ENV PARSING - a typo must never stop a server booting
        // ------------------------------------------------------------------------

        /// <summary>
        /// Whether storms run at all. Default OFF: anything other than a recognised
        /// truthy value leaves this feature inert.
        /// </summary>
        public static bool Enabled(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string v = raw.Trim();
            return v.Equals("1", StringComparison.Ordinal)
                || v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || v.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Reads <see cref="EnabledEnvVar"/> from the process environment.</summary>
        public static bool EnabledFromEnvironment() =>
            Enabled(Environment.GetEnvironmentVariable(EnabledEnvVar));

        /// <summary>
        /// The cadence in force. Unparseable, non-positive, or shorter than one
        /// storm plus its warning falls back to <see cref="DefaultCadence"/>: a
        /// cadence below that would mean a storm was always either running or being
        /// announced, which is not a cadence.
        /// </summary>
        public static TimeSpan CadenceFrom(string? raw, TimeSpan duration)
        {
            TimeSpan? parsed = PositiveSeconds(raw);
            if (parsed == null) return DefaultCadence;
            TimeSpan floor = duration + TimeSpan.FromSeconds(TelegraphSeconds);
            return parsed.Value <= floor ? DefaultCadence : parsed.Value;
        }

        /// <summary>
        /// The storm duration in force. Anything unparseable or non-positive falls
        /// back to <see cref="DefaultDuration"/>; a zero-length storm would set
        /// <c>estimatedMilliTillLightningEnd</c> to 0 and therefore never be a storm
        /// at all.
        /// </summary>
        public static TimeSpan DurationFrom(string? raw) =>
            PositiveSeconds(raw) ?? DefaultDuration;

        /// <summary>The jitter fraction in force, clamped to [0, <see cref="MaxJitterFraction"/>].</summary>
        public static double JitterFrom(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return DefaultJitterFraction;
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double value)) return DefaultJitterFraction;
            if (double.IsNaN(value) || double.IsInfinity(value)) return DefaultJitterFraction;
            return ClampJitter(value);
        }

        /// <summary>
        /// The countdown refresh interval in force, FLOORED at
        /// <see cref="MinCountdownRefreshSeconds"/>. The floor is not a nicety: a
        /// refresh below the client's 7 s warp threshold produces pushes the client
        /// discards, i.e. a warning that never ramps and a feature that looks
        /// unplugged. An operator who asks for 2 s gets 8 and is right to.
        /// </summary>
        public static TimeSpan CountdownRefreshFrom(string? raw)
        {
            TimeSpan? parsed = PositiveSeconds(raw);
            if (parsed == null) return DefaultCountdownRefresh;
            TimeSpan floor = TimeSpan.FromSeconds(MinCountdownRefreshSeconds);
            return parsed.Value < floor ? floor : parsed.Value;
        }

        /// <summary>
        /// WHETHER TREES STILL REGROW ON THEIR OWN FIVE-MINUTE TIMERS, once storms
        /// exist to regrow them instead.
        ///
        /// <see cref="TreeHarvest"/>'s own doc has been asking for this since tree
        /// regrowth shipped: "If an understorm is ever built, tree regrowth should
        /// STOP using its own delay and ride that event instead: the seam is exactly
        /// <c>DueRespawns</c>". This is that switch. Retail did not heal each tree
        /// quietly on its own schedule - you could strip an area bare and it STAYED
        /// bare until the storm, and that difference is the whole reason the
        /// understorm was a gameplay loop rather than weather.
        ///
        /// So: storms on and no explicit tree knob -> per-tree regrowth is OFF and
        /// the forest comes back with the storm. An operator who has SET
        /// <c>WAREBORN_TREE_RESPAWN_SECONDS</c> has said something explicit about
        /// trees and keeps exactly the behaviour they asked for, storms or not -
        /// that is the revert path, and it is why this takes the raw string rather
        /// than the parsed delay (an unparseable value is still an operator saying
        /// "leave my trees alone", and it should not silently mean the opposite).
        /// </summary>
        public static bool PerTreeRegrowthEnabled(bool stormsEnabled, string? treeRespawnSecondsRaw)
        {
            if (!stormsEnabled) return true;
            return !string.IsNullOrWhiteSpace(treeRespawnSecondsRaw);
        }

        public static double ClampJitter(double value) =>
            value <= 0 ? 0 : value >= MaxJitterFraction ? MaxJitterFraction : value;

        private static TimeSpan? PositiveSeconds(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double seconds)) return null;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0) return null;
            return TimeSpan.FromSeconds(seconds);
        }

        // ------------------------------------------------------------------------
        // THE SCHEDULE
        // ------------------------------------------------------------------------

        /// <summary>
        /// How far into each cadence THIS island's storm sits, derived from its id
        /// string rather than its position in a list.
        ///
        /// Order-independence is the point. A registration-order offset would give
        /// every island a new schedule the day an operator enables one more district,
        /// and "the storm times all changed" would be indistinguishable from a bug.
        /// A stable hash of the id means Haven's phase is Haven's phase for ever.
        /// </summary>
        public static TimeSpan PhaseOffsetFor(string? islandId, TimeSpan cadence, double jitterFraction)
        {
            double jitter = ClampJitter(jitterFraction);
            if (jitter <= 0 || cadence <= TimeSpan.Zero) return TimeSpan.Zero;
            double unit = StableUnitInterval(islandId);
            return TimeSpan.FromTicks((long)(cadence.Ticks * jitter * unit));
        }

        /// <summary>
        /// A stable value in [0,1) for an island id. FNV-1a over the id's UTF-8
        /// bytes; deliberately NOT <c>string.GetHashCode</c>, which .NET randomises
        /// per process, so that would have reshuffled every island's schedule on
        /// every restart.
        /// </summary>
        public static double StableUnitInterval(string? islandId)
        {
            if (string.IsNullOrEmpty(islandId)) return 0.0;
            uint hash = 2166136261u;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(islandId))
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return hash / 4294967296.0;
        }

        /// <summary>
        /// When the storm of a given generation starts for an island with this phase
        /// offset. Generations count from ONE, so a freshly booted server does not
        /// storm at t=0 - it waits a full cadence, which is also what an operator
        /// flipping the switch expects.
        /// </summary>
        public static TimeSpan StartOf(long generation, TimeSpan cadence, TimeSpan phaseOffset) =>
            TimeSpan.FromTicks(cadence.Ticks * generation) + phaseOffset;

        /// <summary>
        /// One island's cycle state at <paramref name="now"/>.
        ///
        /// Never returns <c>MillisTillLightningEnd &gt; 0</c> unless a storm is
        /// genuinely running, because that integer IS the client's storm switch.
        /// </summary>
        public static IslandStormSample Sample(TimeSpan now, TimeSpan cadence, TimeSpan duration,
            TimeSpan phaseOffset)
        {
            if (cadence <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cadence));
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));

            long generation = (now - phaseOffset).Ticks / cadence.Ticks;
            if (generation < 1) generation = 1;

            TimeSpan start = StartOf(generation, cadence, phaseOffset);
            TimeSpan end = start + duration;
            if (now >= end)
            {
                generation += 1;
                start = StartOf(generation, cadence, phaseOffset);
                end = start + duration;
            }

            if (now < start)
            {
                TimeSpan untilStart = start - now;
                IslandStormPhase phase = untilStart.TotalSeconds < TelegraphSeconds
                    ? IslandStormPhase.Telegraph
                    : IslandStormPhase.Quiet;
                return new IslandStormSample(phase, ClampMillis(untilStart), 0, generation);
            }

            return new IslandStormSample(IslandStormPhase.Active, 0, ClampMillis(end - now), generation);
        }

        /// <summary>
        /// WHEN THE WORLD RESET FOR A GENERATION FIRES, and why it is the LAST
        /// island's storm end rather than each island's own.
        ///
        /// S1 reuses the server's existing reset, which is WORLD-WIDE
        /// (<c>ResetHarvestResources()</c> walks every tree, node and canister in
        /// the world). With jittered per-island storms and a world-wide reset, firing
        /// at each island's own storm end would reset the whole world once per island
        /// per cadence - twelve islands would refresh Haven's ore twelve times an
        /// hour, eleven of them while Haven was perfectly calm. So the reset fires
        /// ONCE per generation, at the instant the last island's storm ends, which is
        /// the only moment at which "every island has just been struck" is true.
        ///
        /// This is a KNOWN DIVERGENCE, stated rather than hidden: retail reset each
        /// island when ITS storm ended. Closing it is S2's job and needs exactly one
        /// thing this class does not have - a resource-to-island map (§14.10 S2).
        /// </summary>
        public static TimeSpan WorldResetAt(long generation, TimeSpan cadence, TimeSpan duration,
            TimeSpan lastPhaseOffset) =>
            StartOf(generation, cadence, lastPhaseOffset) + duration;

        /// <summary>
        /// The highest generation whose world reset is already due at
        /// <paramref name="now"/>, or 0 if none is. Monotonic in <paramref name="now"/>,
        /// which is what lets the caller fire each reset exactly once by remembering
        /// the last generation it acted on.
        /// </summary>
        public static long DueWorldResetGeneration(TimeSpan now, TimeSpan cadence, TimeSpan duration,
            TimeSpan lastPhaseOffset)
        {
            if (cadence <= TimeSpan.Zero) return 0;
            long generation = (now - lastPhaseOffset - duration).Ticks / cadence.Ticks;
            return generation < 1 ? 0 : generation;
        }

        private static int ClampMillis(TimeSpan span)
        {
            double ms = span.TotalMilliseconds;
            if (ms <= 0) return 0;
            return ms >= int.MaxValue ? int.MaxValue : (int)ms;
        }
    }
}
