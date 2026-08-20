namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The two things the storm service needs from the outside world, and nothing
    /// else. Implemented for real in the game server (where ENet and the Improbable
    /// types live) and by a recording fake in the tests, which is what lets a whole
    /// 105-minute cycle - telegraph, storm, reset - be asserted on without a socket.
    /// </summary>
    public interface IIslandStormWire
    {
        /// <summary>
        /// The entity id this island's terrain is known by on every client, or null
        /// if its AddEntityOp has not run yet. MUST NOT allocate an id: asking "is
        /// there an island" must never be what creates one.
        /// </summary>
        long? IslandEntityId(string islandId);

        /// <summary>
        /// Sends one 1254 update to every peer that currently holds this island's
        /// 1254, and returns how many got it. Peers without it are skipped, not
        /// served: an update for a component a client does not hold is ignored at
        /// best.
        /// </summary>
        int PushTimer(long islandEntityId, IslandStormUpdate update);

        /// <summary>
        /// Restores every damaged tree, metal node and fuel canister in the world
        /// and tells the peers holding them. Returns a one-line summary for the log.
        /// This is the SAME body the authenticated operator command already ran -
        /// the storm is a second caller, not a second implementation.
        /// </summary>
        string ResetWorldResources();
    }

    /// <summary>
    /// THE UNDERSTORM. Drives each island's 1254 timer so the shipped client
    /// announces, renders and ends a storm, and refreshes the world's resources
    /// when the last one is over.
    ///
    /// This is S1 of §14.10 and it adds NOTHING to the wire's surface: 1254 is
    /// already seeded on every island, already read by a visualiser baked onto all
    /// 255 shipped island bundles (PROVED 2026-08-20, UnityPy), and already the
    /// component whose comment in the serializer reads "must be 0 or you will set
    /// the island into a storm". All this does is stop pinning it.
    ///
    /// WHAT A PLAYER GETS, none of which needs a client change:
    ///   * a rumble and camera shake that ramps over the last 30 s, within 300 m of
    ///     the island (the client's own numbers);
    ///   * the audio loop Bossa named <c>Play_IslandRespawn_Start</c> - their own
    ///     name for the understorm is "island respawn", which is the strongest
    ///     single piece of evidence that this event IS the resource refresh;
    ///   * bolts drawn from the death clouds UP into the island's own surface,
    ///     roughly one every half second (the prefab's shipped 0..1 s roll,
    ///     RECOVERED);
    ///   * mined nodes, chopped trees and drained fuel canisters back, when it ends.
    ///
    /// MEASURED IN SECONDS, NEVER IN MAIN-LOOP TURNS. Like
    /// <see cref="TreeHarvest"/>, and for the reason its doc gives: this server's
    /// loop turns once per ENet EVENT, so counting iterations means a busy server
    /// storms hundreds of times a minute. Every deadline here comes off the
    /// injected <see cref="IClock"/>.
    ///
    /// OFF BY DEFAULT (<c>WAREBORN_STORMS</c>). With it off, <see cref="Tick"/>
    /// returns on a bool and this server is byte-identical on the wire to one built
    /// without the feature.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly.
    /// </summary>
    public sealed class IslandStormService
    {
        private sealed class IslandState
        {
            public IslandState(string id, TimeSpan phaseOffset)
            {
                Id = id;
                PhaseOffset = phaseOffset;
            }

            public string Id { get; }
            public TimeSpan PhaseOffset { get; }
            public IslandStormUpdate? LastSent { get; set; }
            public TimeSpan LastSentAt { get; set; }
        }

        private readonly IClock _clock;
        private readonly IIslandStormWire _wire;
        private readonly List<IslandState> _islands = new List<IslandState>();
        private readonly TimeSpan _lastPhaseOffset;

        /// <summary>
        /// The last generation whose world reset has been performed.
        ///
        /// Seeded on the FIRST tick to whatever is already due rather than to zero,
        /// so a server that has been up for six hours before an operator enables
        /// storms does not immediately fire five backdated resets. The first storm a
        /// player sees is the first storm this service scheduled.
        /// </summary>
        private long _lastResetGeneration = -1;

        public IslandStormService(IClock clock, IIslandStormWire wire,
            IReadOnlyList<string> islandIds, bool enabled, TimeSpan cadence, TimeSpan duration,
            double jitterFraction, TimeSpan countdownRefresh)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _wire = wire ?? throw new ArgumentNullException(nameof(wire));
            if (islandIds == null) throw new ArgumentNullException(nameof(islandIds));
            if (cadence <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cadence),
                    "a non-positive cadence would storm every main-loop turn");
            if (duration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration),
                    "a zero-length storm sets estimatedMilliTillLightningEnd to 0, which is not a storm");
            if (countdownRefresh <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(countdownRefresh));

            Enabled = enabled;
            Cadence = cadence;
            Duration = duration;
            JitterFraction = IslandStormPolicy.ClampJitter(jitterFraction);
            CountdownRefresh = countdownRefresh;

            TimeSpan latest = TimeSpan.Zero;
            foreach (string id in islandIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                TimeSpan offset = IslandStormPolicy.PhaseOffsetFor(id, cadence, JitterFraction);
                if (offset > latest) latest = offset;
                _islands.Add(new IslandState(id, offset));
            }
            _lastPhaseOffset = latest;
        }

        public bool Enabled { get; }
        public TimeSpan Cadence { get; }
        public TimeSpan Duration { get; }
        public double JitterFraction { get; }
        public TimeSpan CountdownRefresh { get; }

        /// <summary>How many islands are on the storm schedule.</summary>
        public int IslandCount => _islands.Count;

        /// <summary>This island's offset into each cadence, for the boot log and the tests.</summary>
        public TimeSpan PhaseOffsetOf(string islandId)
        {
            foreach (IslandState island in _islands)
                if (string.Equals(island.Id, islandId, StringComparison.Ordinal))
                    return island.PhaseOffset;
            return TimeSpan.Zero;
        }

        /// <summary>Where one island is in its cycle right now. Sends nothing.</summary>
        public IslandStormSample SampleOf(string islandId) =>
            IslandStormPolicy.Sample(_clock.Elapsed, Cadence, Duration, PhaseOffsetOf(islandId));

        /// <summary>What this island was last told, for the stats snapshot and the tests.</summary>
        public IslandStormUpdate? LastSentTo(string islandId)
        {
            foreach (IslandState island in _islands)
                if (string.Equals(island.Id, islandId, StringComparison.Ordinal))
                    return island.LastSent;
            return null;
        }

        /// <summary>
        /// One call per main-loop turn. Cheap when off (one bool) and cheap when
        /// idle: a walk of a handful of islands doing integer arithmetic, which
        /// almost always decides to send nothing.
        ///
        /// ORDER. Pushes first, reset second, so the update that ENDS a storm is on
        /// the wire before the resources it refreshed change under the player. They
        /// land on the same loop turn either way; this is ordering, not atomicity.
        /// </summary>
        public void Tick()
        {
            if (!Enabled || _islands.Count == 0) return;

            TimeSpan now = _clock.Elapsed;

            foreach (IslandState island in _islands)
            {
                IslandStormSample sample =
                    IslandStormPolicy.Sample(now, Cadence, Duration, island.PhaseOffset);

                IslandStormUpdate? next = IslandStormPush.Next(island.LastSent, sample,
                    now - island.LastSentAt, CountdownRefresh);
                if (next == null) continue;

                long? entityId = _wire.IslandEntityId(island.Id);
                if (entityId == null) continue;   // not spawned yet; try again next turn

                _wire.PushTimer(entityId.Value, next.Value);
                island.LastSent = next.Value;
                island.LastSentAt = now;
            }

            TickWorldReset(now);
        }

        /// <summary>
        /// THE RESET, and it fires at the END of a storm, not the start.
        ///
        /// The wiki has loose objects surfacing DURING the storm, so an
        /// end-of-storm reset is a simplification and is declared one (WAREBORN
        /// TUNING). It is the honest simplification rather than the convenient one:
        /// resetting at the start would put every mined node back while the bolts
        /// were still falling, so the storm would be an announcement of something
        /// that had already happened.
        ///
        /// Once per generation, at the last island's storm end - see
        /// <see cref="IslandStormPolicy.WorldResetAt"/> for why it is the last one
        /// and what S2 does about it.
        /// </summary>
        private void TickWorldReset(TimeSpan now)
        {
            long due = IslandStormPolicy.DueWorldResetGeneration(now, Cadence, Duration, _lastPhaseOffset);

            if (_lastResetGeneration < 0)
            {
                // First tick: adopt whatever is already behind us rather than
                // replaying it.
                _lastResetGeneration = due;
                return;
            }

            if (due <= _lastResetGeneration) return;

            _lastResetGeneration = due;
            _wire.ResetWorldResources();
        }
    }
}
