namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The closed-form pose maths this registry drives: where one creature is at
    /// one instant, in ISLAND-LOCAL metres converted to world by the island.
    ///
    /// It is a delegate rather than a hard call so the registry owns only WHEN a
    /// pose is sent and never HOW it is shaped - the analytical manta orbit and
    /// jellyfish day/night drift live in <c>IslandFaunaMovement</c>, which is pure,
    /// allocation-free and stateless. The contract this registry depends on is
    /// exactly that purity: the function must be a total function of
    /// (creature, island, envelope, elapsedSeconds) with no stored physics state,
    /// no clock of its own and no entropy. Everything below is built on it.
    /// </summary>
    public delegate FixedPointPosition FaunaPoseFunction(
        FaunaCreature creature,
        IslandDefinition island,
        IslandTerrainEnvelope envelope,
        double elapsedSeconds);

    /// <summary>
    /// One creature's pose at one instant: everything a transform push needs.
    ///
    /// The position is COMPLETE AND ABSOLUTE, never a delta. That is not a
    /// stylistic choice: fauna transforms travel unreliably, exactly as ship and
    /// log poses do, so a peer will lose some of them. An absolute pose means a
    /// lost update costs one frame of smoothness and the very next pose puts the
    /// creature exactly where the server says it is; a delta stream would make
    /// every lost packet a permanent divergence that only a reconnect could fix.
    /// </summary>
    public readonly struct FaunaPose
    {
        public FaunaPose(long entityId, FaunaSpecies species, IslandId islandId,
            FixedPointPosition position)
        {
            EntityId = entityId;
            Species = species;
            IslandId = islandId;
            Position = position;
        }

        /// <summary>The creature entity this pose addresses.</summary>
        public long EntityId { get; }

        /// <summary>What the creature is, so a caller can pick the prefab without a second lookup.</summary>
        public FaunaSpecies Species { get; }

        /// <summary>The island that owns the creature, for interest scoping.</summary>
        public IslandId IslandId { get; }

        /// <summary>Where the creature is, absolutely. See the type remarks.</summary>
        public FixedPointPosition Position { get; }

        public override string ToString() =>
            Species + " " + EntityId + " on " + IslandId + " at " + Position;
    }

    /// <summary>
    /// Every live creature, and WHEN each one is due a transform push.
    ///
    /// The clock-driven half of island fauna, shaped deliberately like
    /// <c>FallingLogs</c> in TreeFall.cs, because it is the same kind of hazard:
    /// a NEW HIGH-RATE SENDER. The standing rule in docs/multiplayer.md is that no
    /// new feature may add an unbounded relayed sender, and this type is where that
    /// rule is actually enforced rather than merely intended:
    /// <list type="bullet">
    /// <item>a WORLD-WIDE CAP (<see cref="MaxConcurrent"/>) past which
    ///   <see cref="Add"/> REFUSES by returning false. A refusal is not an error
    ///   and never an exception - the island simply carries fewer creatures, which
    ///   is the state the world was in before this feature existed;</item>
    /// <item>a pose interval measured in SECONDS off an injected clock, never in
    ///   main-loop turns. The loop turns once per ENet EVENT, so counting turns
    ///   would push hundreds of updates a second on a busy server - a mistake this
    ///   project has already paid for once;</item>
    /// <item>SILENCE when nothing is due: <see cref="DuePoses"/> allocates nothing
    ///   and returns an empty array.</item>
    /// </list>
    ///
    /// IT HOLDS NO PHYSICS STATE, AND THAT IS THE WHOLE DESIGN. A creature's pose
    /// is not integrated, accumulated or remembered; it is recomputed from the
    /// clock's absolute elapsed time through <see cref="FaunaPoseFunction"/> every
    /// time it is asked for. So a server that RESTARTS and rebuilds this registry
    /// on a fresh clock replays the identical pose sequence for the identical
    /// elapsed times, with the identical entity ids from
    /// <see cref="IslandFaunaPolicy.PopulationFor"/> - nothing has to be persisted,
    /// and a reconnecting player cannot be shown a creature that has teleported.
    ///
    /// Not thread-safe: the server is a single poll loop.
    /// </summary>
    public sealed class IslandFaunaRegistry
    {
        /// <summary>
        /// How often one creature's transform is pushed while it is live. 250 ms -
        /// 4 Hz - and the number is chosen against the 20 Hz cadence every other
        /// moving thing on this server uses, not plucked from the air.
        ///
        /// FAUNA DRIFTS, IT DOES NOT FALL. A ship under a player's hand and a log
        /// toppling under gravity both change direction fast enough that the
        /// client's interpolation needs frequent correction; a manta on a perimeter
        /// orbit and a jelly on a day/night drift move slowly along smooth analytic
        /// paths, so intermediate updates buy nothing a client cannot interpolate
        /// for itself. Five times slower costs nothing visible and divides the
        /// bandwidth by five.
        ///
        /// THE RESULTING BUDGET, stated so it can be checked rather than trusted:
        /// 4 updates per second per creature, and at most
        /// <see cref="IslandFaunaPolicy.DefaultMaxConcurrent"/> (24) creatures live
        /// world-wide, so a peer that could somehow see every creature at once
        /// receives at most 96 fauna transform updates a second. The same 24
        /// creatures at the 20 Hz ship cadence would be 480 - roughly a flying
        /// ship's worth of traffic added for scenery, which is exactly the kind of
        /// unbounded growth the multiplayer-safety rule exists to stop.
        /// </summary>
        public static readonly TimeSpan DefaultPoseInterval = TimeSpan.FromMilliseconds(250);

        private sealed class Entry
        {
            public FaunaCreature Creature;
            public IslandDefinition Island = null!;
            public IslandTerrainEnvelope Envelope;
            public TimeSpan NextPoseAt;
        }

        private readonly IClock _clock;
        private readonly FaunaPoseFunction _pose;
        private readonly TimeSpan _poseInterval;
        private readonly int _maxConcurrent;
        private readonly Dictionary<long, Entry> _creatures = new Dictionary<long, Entry>();
        private long _nextEntityId = IslandFaunaPolicy.FirstFaunaEntityId;

        public IslandFaunaRegistry(IClock clock, FaunaPoseFunction pose,
            int? maxConcurrent = null, TimeSpan? poseInterval = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _pose = pose ?? throw new ArgumentNullException(nameof(pose));
            _maxConcurrent = maxConcurrent ?? IslandFaunaPolicy.DefaultMaxConcurrent;
            _poseInterval = poseInterval ?? DefaultPoseInterval;

            if (_maxConcurrent < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrent),
                    "a negative creature budget is not a budget");
            }
            if (_poseInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(poseInterval),
                    "a non-positive pose interval would push a transform per main-loop turn");
            }
        }

        /// <summary>How often a live creature is pushed. See <see cref="DefaultPoseInterval"/>.</summary>
        public TimeSpan PoseInterval => _poseInterval;

        /// <summary>How many creatures may be live at once, world-wide.</summary>
        public int MaxConcurrent => _maxConcurrent;

        /// <summary>How many are live now.</summary>
        public int Count => _creatures.Count;

        /// <summary>Whether another creature would fit inside the budget.</summary>
        public bool HasCapacity => _creatures.Count < _maxConcurrent;

        /// <summary>Whether an entity id names a creature this registry owns.</summary>
        public bool IsFauna(long entityId) => _creatures.ContainsKey(entityId);

        /// <summary>Every live creature id, so a caller can retire them on shutdown.</summary>
        public IReadOnlyCollection<long> Live => _creatures.Keys.ToList();

        /// <summary>
        /// The next fauna entity id. Monotonic from
        /// <see cref="IslandFaunaPolicy.FirstFaunaEntityId"/> and NEVER reused, even
        /// after the creature it named has gone - the rule every id allocator on
        /// this server keeps, because a packet still in flight for a retired entity
        /// must not be able to name a live one. The band sits a hundred million ids
        /// above <c>TreeFall.FirstLogEntityId</c> so fauna and felled logs can never
        /// collide.
        /// </summary>
        public long NextEntityId() => _nextEntityId++;

        /// <summary>
        /// Takes one creature live, or REFUSES.
        ///
        /// Returns false - never throws - when the budget is full, when the id is
        /// already live, or when the id is outside the fauna band. All three are
        /// "no creature this time", which is a quieter island and nothing worse.
        /// The first pose is due immediately so a creature is never checked out at
        /// the island origin and then snapped onto its path.
        /// </summary>
        public bool Add(FaunaCreature creature, IslandDefinition island,
            IslandTerrainEnvelope envelope)
        {
            if (island == null)
            {
                throw new ArgumentNullException(nameof(island));
            }
            if (!HasCapacity
                || creature.EntityId < IslandFaunaPolicy.FirstFaunaEntityId
                || _creatures.ContainsKey(creature.EntityId))
            {
                return false;
            }

            _creatures.Add(creature.EntityId, new Entry
            {
                Creature = creature,
                Island = island,
                Envelope = envelope,
                NextPoseAt = _clock.Elapsed,
            });
            return true;
        }

        /// <summary>Retires one creature. False if it was not live.</summary>
        public bool Remove(long entityId) => _creatures.Remove(entityId);

        /// <summary>
        /// Where a creature is RIGHT NOW, or null if it is not live - the seed a
        /// joiner is given at checkout. Clock-derived rather than remembered, for
        /// the reason in the type remarks: there is nothing to remember.
        /// </summary>
        public FixedPointPosition? PositionOf(long entityId) =>
            _creatures.TryGetValue(entityId, out Entry? entry)
                ? PoseOf(entry, _clock.Elapsed) : (FixedPointPosition?)null;

        /// <summary>
        /// Every creature whose next pose is due, advanced. Call once per main-loop
        /// turn; it allocates NOTHING when nothing is due, which is the common case
        /// between intervals and the whole case on a world with no fauna.
        /// </summary>
        public IReadOnlyList<FaunaPose> DuePoses() => DuePoses(null);

        /// <summary>
        /// The same, but only for creatures <paramref name="isWatched"/> accepts.
        ///
        /// THE SCHEDULE STILL ADVANCES FOR EVERYONE, and that is the whole subtlety.
        /// An unwatched creature is skipped for the COMPUTATION and for the result,
        /// but its <c>NextPoseAt</c> moves on exactly as if it had been sent. Anything
        /// else would let an unwatched creature accumulate an overdue schedule and
        /// then fire a burst the instant somebody walked into range - which is the
        /// same shape as the desync spiral this feature is audited against. It costs
        /// nothing to keep correct because a pose is a closed form of absolute elapsed
        /// time: skipping the computation cannot make the creature drift.
        ///
        /// This exists because the world-wide population is now allowed to be the
        /// whole catalogue while a peer holds at most a couple of dozen creatures.
        /// Computing several thousand trigonometric poses four times a second to
        /// discard almost all of them would be pure waste, and the filter is the one
        /// line that avoids it.
        /// </summary>
        public IReadOnlyList<FaunaPose> DuePoses(Func<long, bool>? isWatched)
        {
            if (_creatures.Count == 0)
            {
                return Array.Empty<FaunaPose>();
            }

            TimeSpan now = _clock.Elapsed;
            List<FaunaPose>? poses = null;

            foreach (KeyValuePair<long, Entry> pair in _creatures)
            {
                Entry entry = pair.Value;
                if (now < entry.NextPoseAt)
                {
                    continue;
                }

                entry.NextPoseAt = now + _poseInterval;
                if (isWatched != null && !isWatched(pair.Key))
                {
                    continue;
                }

                (poses ??= new List<FaunaPose>()).Add(new FaunaPose(pair.Key,
                    entry.Creature.Species, entry.Creature.IslandId, PoseOf(entry, now)));
            }

            return poses ?? (IReadOnlyList<FaunaPose>)Array.Empty<FaunaPose>();
        }

        /// <summary>Drops every creature without reporting it, for a caller tearing the world down.</summary>
        public void Clear() => _creatures.Clear();

        /// <summary>
        /// The pose maths, fed the clock's ABSOLUTE elapsed seconds rather than a
        /// per-creature age. Absolute time is what makes a restart replayable: an
        /// age would depend on when the creature happened to be added, so two
        /// servers that seeded the same island at different points in their boot
        /// would disagree about where the same creature is.
        /// </summary>
        private FixedPointPosition PoseOf(Entry entry, TimeSpan now) =>
            _pose(entry.Creature, entry.Island, entry.Envelope, now.TotalSeconds);
    }
}
