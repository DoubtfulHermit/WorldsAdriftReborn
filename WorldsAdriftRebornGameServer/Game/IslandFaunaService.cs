using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// ISLAND FAUNA ON THE WIRE. The impure half of
    /// <see cref="IslandFaunaPolicy"/>/<see cref="IslandFaunaRegistry"/>: it seeds
    /// the planned creatures at boot, streams each one to the peers close enough to
    /// care, and pushes the pose the registry says is due.
    ///
    /// A CREATURE IS NOT A WORLD REGISTRATION, and that decision is what shapes
    /// this whole file. It is stated in <see cref="IslandFaunaPolicy.FirstFaunaEntityId"/>
    /// and it is not a preference: <see cref="IslandFaunaRegistry.Add"/> REFUSES any
    /// id below the fauna band, while a <c>WorldEntityRegistry</c> registration is
    /// numbered by <c>EntityIdAllocator</c> counting up from 1. The two id schemes
    /// are mutually exclusive, so fauna cannot go through the registry - and
    /// therefore cannot ride <c>ResourceInterestService</c>, whose entire input is
    /// the registration list. Adding a "fauna-" prefix to
    /// <c>ResourceInterestPolicy.IsStreamedResourceKey</c> would be dead code: a
    /// creature has no registration key for it to match. The per-peer checkout below
    /// is the price of the disjoint id band, and it is deliberately the same shape
    /// as <see cref="ResourceInterestService"/>'s so the two behave alike.
    ///
    /// INTEREST IS KEYED ON THE ISLAND, NOT ON THE ANIMAL, and that is the fix for
    /// the reported despawn. The reasoning, the measurement and the rejected
    /// alternatives are all in <see cref="IslandFaunaInterestPolicy"/>; the short
    /// version is that a creature's distance to a standing player oscillates by
    /// design, so checking out on it made every orbit a remove/re-add cycle. An
    /// island's distance does not oscillate, so keying on it cannot flicker.
    ///
    /// WIRE SHAPE, per creature, per peer (the multiplayer-safety contract):
    /// <list type="bullet">
    /// <item>OUT, once per checkout: an AssetLoadRequest one cadence before an
    ///   AddEntity, to peers inside the fauna radius of the creature's ISLAND, whose
    ///   island terrain is already checked out, and which can receive RemoveEntity.
    ///   Nothing else is seeded; the client asks for the components its own prefab
    ///   wants over SEND_COMPONENT_INTEREST.</item>
    /// <item>OUT, 4/s while checked out: one 190602 TransformState carrying the
    ///   complete absolute position. 190602 is UNRELIABLE by
    ///   <c>MirrorSendPolicy.RelayReliabilityFor</c> and this stream supersedes -
    ///   every update is the whole pose - so a loss costs one frame of smoothness.</item>
    /// <item>OUT, once: RemoveEntity on channel 5 once the ISLAND leaves the unload
    ///   radius. A creature is never removed while its island is held, so a school
    ///   arrives and departs as one thing.</item>
    /// <item>IN: nothing. No client sends anything about a creature; there is no
    ///   update handler, and there is nothing to interact with yet.</item>
    /// </list>
    ///
    /// THE WORST CASE, stated so it can be checked rather than trusted, and now
    /// INDEPENDENT OF HOW BIG THE WORLD IS. A peer may hold at most
    /// <see cref="IslandFaunaInterestPolicy.DefaultPerPeerCreatures"/> (24) creatures,
    /// each pushed at <see cref="IslandFaunaRegistry.DefaultPoseInterval"/> (250 ms),
    /// so the ceiling is 24 x 4 = 96 fauna transform updates a second - under a fifth
    /// of one 20 Hz avatar relay, and the same ceiling the soak gate already measured
    /// FLAT. <see cref="BudgetEnv"/> now bounds only how much wildlife EXISTS, which
    /// costs a dictionary entry and a closed-form pose; <see cref="PeerBudgetEnv"/> is
    /// the knob that moves the wire, and it is the one to be careful with.
    ///
    /// ONLY PEERS THAT CAN RECEIVE RemoveEntity ARE EVER SHOWN A CREATURE. Channel 5
    /// is a negotiated capability, and a peer that lacks it could never unload the
    /// animal again. That is the same guard <c>FallingLogService</c> uses, and for
    /// the same reason: it is what lets this feature exist without a litter story.
    /// </summary>
    internal sealed class IslandFaunaService
    {
        /// <summary>The opt-in gate. See <see cref="IslandFaunaPolicy.EnabledEnvVar"/>.</summary>
        internal const string EnableEnv = IslandFaunaPolicy.EnabledEnvVar;

        /// <summary>
        /// How many creatures may EXIST world-wide. 0 is a second kill switch.
        /// Named like <c>WAREBORN_TREE_FALL_MAX</c> because it does the same job.
        /// This is no longer the wire bound; see <see cref="PeerBudgetEnv"/>.
        /// </summary>
        internal const string BudgetEnv = "WAREBORN_ISLAND_FAUNA_MAX";

        /// <summary>
        /// How many creatures ONE PEER may hold at once. THE wire bound, and the
        /// number the multiplayer-safety rule is about.
        /// </summary>
        internal const string PeerBudgetEnv = IslandFaunaInterestPolicy.PerPeerBudgetEnvVar;

        /// <summary>How near an island a peer must be for its fauna to check out.</summary>
        internal const string RadiusEnv = IslandFaunaInterestPolicy.LoadRadiusEnvVar;

        /// <summary>
        /// The ecology switch: capacity-driven populations, quiet islands,
        /// multiple groups, and field-following motion. OFF by default and a
        /// typo fails safe to the classic motion, like every fauna flag. It is
        /// read ONCE at boot - the pose function and the population plan are
        /// chosen in the constructor and at Seed - so it cannot flip mid-process
        /// and re-lay entity ids under a live session.
        /// </summary>
        internal const string EcologyEnv = "WAREBORN_ISLAND_FAUNA_ECOLOGY";

        /// <summary>
        /// The world seed the blooms are derived from. An integer; anything else
        /// falls back to <see cref="IslandFaunaEcology.DefaultWorldSeed"/>.
        /// Rerolling it re-lays the ecology's motion, never its entity ids -
        /// the id blocks are a pure function of the catalogue.
        /// </summary>
        internal const string SeedEnv = "WAREBORN_ISLAND_FAUNA_SEED";

        /// <summary>
        /// The JUVENILES switch (Phase 5): mother-and-calf offsets and
        /// quarter-scale calves. OFF by default and a typo fails safe, like every
        /// fauna flag, and with it off the wire is BYTE-IDENTICAL - the serializer
        /// is handed no age (so 1166 falls through to the unhandled path it takes
        /// today) and the evaluator is handed no family (so every member offset is
        /// the function it has always been).
        ///
        /// IT REQUIRES THE ECOLOGY, and that is a design statement rather than an
        /// implementation limit. A calf's age is the inverse of the population
        /// rhythm's expression ramp; with no ecology there is no rhythm, no
        /// expression and therefore no birth. A calf slot that is ALWAYS present
        /// at a frozen size is a mesh variant, which is exactly the reading the
        /// juvenile proposal exists to avoid. So this flag is ANDed with
        /// <see cref="EcologyEnv"/> and says so at boot when an operator sets one
        /// without the other.
        /// </summary>
        internal const string JuvenilesEnv = IslandFaunaAge.EnabledEnvVar;

        private const uint TransformStateComponentId = 190602;
        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(120);
        private const int MaxQueuedPerPeer = 128;

        private sealed class PeerState
        {
            public readonly HashSet<long> Loaded = new();
            public readonly HashSet<IslandId> Islands = new();
            public readonly Queue<ResourceStreamAction> Pending = new();
            public TimeSpan NextReconcile;
            public TimeSpan NextSend;
            public TimeSpan ContinuousAfter;
            public long AssetRequestedFor;
            public bool RemoveSupported;
            public bool ConnectPlanComplete;
        }

        /// <summary>
        /// One island's fauna, grouped so interest can be decided per island.
        /// The per-species lists are kept in SEEDING ORDER (school-major,
        /// contiguous ids) because the rhythm expresses a PREFIX of each: growth
        /// appends at the tail and decline removes from it, so a change of
        /// expression can never reshuffle which animals exist, only extend or
        /// trim the same stable sequence.
        /// </summary>
        private sealed class IslandPopulation
        {
            public IslandDefinition Island = null!;
            public IslandTerrainEnvelope Envelope;
            public readonly List<long> EntityIds = new();
            public readonly List<long> MantaIds = new();
            public readonly List<long> JellyIds = new();
        }

        private readonly IClock _clock;
        private readonly bool _enabled;
        private readonly double _loadRadius;
        private readonly double _unloadRadius;
        private readonly int _peerBudget;
        private readonly IslandFaunaRegistry _registry;
        private readonly Dictionary<long, FaunaPlacement> _planned = new();
        private readonly Dictionary<IslandId, IslandPopulation> _byIsland = new();
        private readonly Dictionary<ENetPeerHandle, PeerState> _peers = new();
        private readonly HashSet<long> _held = new();
        private Func<ENetPeerHandle, IslandId, bool>? _terrainReady;
        private readonly FaunaEcologyEvaluator? _ecology;
        private readonly bool _juveniles;

        /// <summary>
        /// The records the world was seeded from, kept ONLY when the ecology is
        /// on: its telemetry lists quiet islands as deliberate zeros, and a quiet
        /// island has no creatures, so it cannot be recovered from _byIsland.
        /// </summary>
        private IReadOnlyList<ReleaseIslandRecord> _seededFrom =
            Array.Empty<ReleaseIslandRecord>();
        private long _sample;

        /// <summary>
        /// How many creatures the world wanted at <see cref="Seed"/> time, before
        /// the budget was applied. Remembered rather than recomputed because the
        /// operator console reports it, and because it is the one number that says
        /// whether the budget covered the world or a corner of it.
        /// </summary>
        private int _demand;

        internal IslandFaunaService(IClock clock)
            : this(clock,
                IslandFaunaPolicy.EnabledFrom(Environment.GetEnvironmentVariable(EnableEnv)),
                IslandFaunaPolicy.ParseBudget(Environment.GetEnvironmentVariable(BudgetEnv)),
                IslandFaunaInterestPolicy.LoadRadiusFrom(Environment.GetEnvironmentVariable(RadiusEnv)),
                IslandFaunaInterestPolicy.ParsePerPeerBudget(
                    Environment.GetEnvironmentVariable(PeerBudgetEnv)),
                IslandFaunaPolicy.EnabledFrom(Environment.GetEnvironmentVariable(EcologyEnv)),
                ParseSeed(Environment.GetEnvironmentVariable(SeedEnv)),
                IslandFaunaPolicy.EnabledFrom(Environment.GetEnvironmentVariable(JuvenilesEnv)))
        {
        }

        internal IslandFaunaService(IClock clock, bool enabled, int? maxConcurrent,
            double? loadRadius = null, int? perPeerBudget = null,
            bool ecologyEnabled = false, int? worldSeed = null,
            bool juvenilesEnabled = false)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _enabled = enabled;
            _loadRadius = loadRadius ?? IslandFaunaInterestPolicy.DefaultLoadRadiusMetres;
            _unloadRadius = IslandFaunaInterestPolicy.UnloadRadiusFor(_loadRadius);
            _peerBudget = perPeerBudget ?? IslandFaunaInterestPolicy.DefaultPerPeerCreatures;
            // The pose function is chosen ONCE, here: the registry cannot tell
            // the ecology from the classic patrol, and nothing downstream
            // branches on the flag again.
            // Juveniles ride on the ecology's rhythm, so they cannot exist without
            // it; see JuvenilesEnv. Decided ONCE, here, like the ecology itself.
            _juveniles = juvenilesEnabled && ecologyEnabled;
            _ecology = ecologyEnabled
                ? new FaunaEcologyEvaluator(worldSeed ?? IslandFaunaEcology.DefaultWorldSeed)
                : null;
            if (juvenilesEnabled && !ecologyEnabled)
            {
                Console.WriteLine("[island-fauna] " + JuvenilesEnv + " is set but " + EcologyEnv
                    + " is not; juveniles need the population rhythm to have a birth to be aged"
                    + " from, so they stay OFF this run.");
            }
            _registry = new IslandFaunaRegistry(clock,
                _ecology != null ? _ecology.WorldTransformAt : IslandFaunaMovement.WorldTransformAt,
                maxConcurrent);
        }

        /// <summary>An integer seed, or null for the default. A typo must not stop a boot.</summary>
        internal static int? ParseSeed(string? raw) =>
            int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int seed)
                ? seed : (int?)null;

        /// <summary>Whether the ecology layer drives populations and motion.</summary>
        internal bool EcologyEnabled => _ecology != null;

        /// <summary>Whether island fauna is switched on.</summary>
        internal bool Enabled => _enabled;

        /// <summary>The parsed load radius this boot decides with, for telemetry.</summary>
        internal double LoadRadiusMetres => _loadRadius;

        /// <summary>The derived unload radius, for telemetry.</summary>
        internal double UnloadRadiusMetres => _unloadRadius;

        /// <summary>
        /// How many creatures this peer currently holds, for the interest
        /// section of the stats snapshot. Zero for an untracked peer - which is
        /// the truth: nothing has been streamed to them.
        /// </summary>
        internal int CheckedOutFor(ENetPeerHandle peer) =>
            _peers.TryGetValue(peer, out PeerState? state) ? state.Loaded.Count : 0;

        /// <summary>How many creatures are live. Zero whenever the feature is off.</summary>
        internal int Count => _registry.Count;

        /// <summary>
        /// The operator console's view of the world's wildlife: what is live, on
        /// which islands, and AT WHAT CLOCK.
        ///
        /// The clock is the point. Every pose this service sends is a closed form
        /// of the same <c>_clock.Elapsed</c> that is reported here, so a console
        /// holding this number and an island's envelope can place every creature
        /// exactly where this server has it - without anybody streaming 460
        /// positions three times a minute and calling the result live.
        ///
        /// READ-ONLY and allocating only the island list. It is called from the
        /// same authoritative poll thread that owns every field it touches, on the
        /// stats writer's few-second cadence, so it needs no lock and costs
        /// nothing measurable.
        /// </summary>
        internal FaunaRuntimeStat Telemetry()
        {
            if (!_enabled)
            {
                return FaunaRuntimeStat.Off;
            }

            // EXPRESSED counts, not seeded ones: the roster is what the map
            // draws and what a player can actually be shown, and under the
            // rhythm those are the same number by construction.
            double now = _clock.Elapsed.TotalSeconds;
            List<FaunaIslandStat> islands = new List<FaunaIslandStat>(_byIsland.Count);
            foreach (KeyValuePair<IslandId, IslandPopulation> pair in _byIsland)
            {
                (int mantas, int jellies) = ExpressedFor(pair.Key, pair.Value, now);
                islands.Add(new FaunaIslandStat(pair.Key.ToString(), mantas, jellies));
            }
            // Sorted by id so the file diffs readably and the console's island
            // order does not depend on dictionary iteration.
            islands.Sort((left, right) =>
                string.CompareOrdinal(left.IslandId, right.IslandId));

            return new FaunaRuntimeStat(
                enabled: true,
                clockSeconds: _clock.Elapsed.TotalSeconds,
                liveCount: _registry.Count,
                budget: _registry.MaxConcurrent,
                demand: _demand,
                perPeerBudget: _peerBudget,
                poseIntervalMs: (int)Math.Round(_registry.PoseInterval.TotalMilliseconds),
                islands: islands,
                ecology: EcologyTelemetry());
        }

        /// <summary>
        /// The ecology section: every seeded-from island (INCLUDING the quiet
        /// ones - a deliberate zero must be visible), its capacities, what is
        /// expressed now, its group structure and its bloom parameters. All of
        /// it re-read from the same pure functions and the same memoised blooms
        /// the pose path uses, so the map's numbers cannot be a second
        /// derivation.
        /// </summary>
        private FaunaEcologyStat EcologyTelemetry()
        {
            if (_ecology == null)
            {
                return FaunaEcologyStat.Off;
            }

            List<FaunaEcologyIslandStat> islands =
                new List<FaunaEcologyIslandStat>(_seededFrom.Count);
            foreach (ReleaseIslandRecord record in _seededFrom)
            {
                IslandId id = record.Definition.Id;
                int tier = record.Survey.Tier;
                (int mantas, int jellies) = IslandFaunaCapacity.ClampedToPeerBudget(
                    IslandFaunaCapacity.CapacityFor(
                        FaunaSpecies.MantaRay, tier, record.Envelope, id),
                    IslandFaunaCapacity.CapacityFor(
                        FaunaSpecies.JellyFish, tier, record.Envelope, id),
                    _peerBudget);

                // What is EXPRESSED right now - the rhythm's fraction of the
                // seeded capacity, counted over the same prefix Reconcile
                // desires, so the map's group sizes are exactly what a player
                // standing there would be shown.
                double nowSeconds = _clock.Elapsed.TotalSeconds;
                int liveMantas = 0, liveJellies = 0;
                List<FaunaGroupStat> groups = new List<FaunaGroupStat>();
                if (_byIsland.TryGetValue(id, out IslandPopulation? population))
                {
                    (liveMantas, liveJellies) = ExpressedFor(id, population, nowSeconds);
                    Dictionary<(FaunaSpecies Species, int Index), int> members =
                        new Dictionary<(FaunaSpecies Species, int Index), int>();
                    foreach (long entityId in population.MantaIds.Take(liveMantas)
                        .Concat(population.JellyIds.Take(liveJellies)))
                    {
                        if (!_planned.TryGetValue(entityId, out FaunaPlacement placement)) continue;
                        FaunaCreature creature = placement.Creature;
                        members.TryGetValue((creature.Species, creature.SchoolIndex), out int n);
                        members[(creature.Species, creature.SchoolIndex)] = n + 1;
                    }
                    foreach (KeyValuePair<(FaunaSpecies Species, int Index), int> pair in members
                        .OrderBy(p => p.Key.Species).ThenBy(p => p.Key.Index))
                    {
                        // The LIVE descriptor: the same schedule the pose path
                        // and the streaming filter read, so a map animating from
                        // this pair is animating what the wire is doing.
                        FaunaGroupBehaviour segment = _ecology.SegmentFor(
                            id, pair.Key.Species, record.Envelope, pair.Key.Index, nowSeconds);
                        groups.Add(new FaunaGroupStat(
                            pair.Key.Species == FaunaSpecies.MantaRay ? "manta" : "jelly",
                            pair.Key.Index,
                            segment.FromBloom,
                            pair.Value,
                            segment.Behaviour.ToString(),
                            segment.EpochSeconds,
                            segment.DurationSeconds,
                            segment.ToBloom));
                    }
                }

                (FaunaPopulationPhase mantaPhase, double mantaFraction) =
                    IslandFaunaRhythm.PhaseFor(_ecology.WorldSeed, id,
                        FaunaSpecies.MantaRay, nowSeconds);
                (FaunaPopulationPhase jellyPhase, double jellyFraction) =
                    IslandFaunaRhythm.PhaseFor(_ecology.WorldSeed, id,
                        FaunaSpecies.JellyFish, nowSeconds);

                List<FaunaBloomStat> bloomStats = new List<FaunaBloomStat>();
                foreach (FaunaSpecies species in new[]
                    { FaunaSpecies.MantaRay, FaunaSpecies.JellyFish })
                {
                    FaunaBloom[] blooms = _ecology.BloomsFor(id, species, record.Envelope);
                    for (int i = 0; i < blooms.Length; i++)
                    {
                        bloomStats.Add(FaunaBloomStat.From(species, i, blooms[i]));
                    }
                }

                islands.Add(new FaunaEcologyIslandStat(
                    id.ToString(),
                    IslandFaunaCapacity.QuietFactorFor(id),
                    mantas, jellies,
                    liveMantas, liveJellies,
                    groups, bloomStats,
                    mantaPhase.ToString(), mantaFraction,
                    jellyPhase.ToString(), jellyFraction));
            }
            islands.Sort((left, right) =>
                string.CompareOrdinal(left.IslandId, right.IslandId));

            return new FaunaEcologyStat(enabled: true, _ecology.WorldSeed, islands);
        }

        /// <summary>
        /// Takes the world's creatures live, once, at boot.
        ///
        /// The population is derived rather than persisted (see
        /// <see cref="IslandFaunaPolicy.PopulationFor"/>), so this is a pure
        /// function of the selected island set and runs before any peer can connect.
        /// It reports the DEMAND alongside the seeded count: at release-world scale
        /// those two numbers disagree by a lot, and an operator has to be told that
        /// rather than left to notice empty islands.
        /// </summary>
        internal void Seed(IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (!_enabled)
            {
                return;
            }
            if (islands == null)
            {
                throw new ArgumentNullException(nameof(islands));
            }

            int demand = _ecology != null
                ? IslandFaunaPlan.EcologyDemand(islands, _peerBudget)
                : IslandFaunaPlan.Demand(islands);
            _demand = demand;
            _seededFrom = _ecology != null ? islands : Array.Empty<ReleaseIslandRecord>();
            IReadOnlyList<FaunaPlacement> plan = _ecology != null
                ? IslandFaunaPlan.BuildEcology(islands, _registry.MaxConcurrent, _peerBudget)
                : IslandFaunaPlan.Build(islands, _registry.MaxConcurrent);
            if (_ecology != null)
            {
                Console.WriteLine("[island-fauna] ECOLOGY ON (" + EcologyEnv + "): capacity-driven"
                    + " populations from each island's own AABB, quiet islands deliberate, groups"
                    + " circulating field maxima; world seed " + _ecology.WorldSeed
                    + " (" + SeedEnv + ").");
            }

            foreach (FaunaPlacement placement in plan)
            {
                if (!_registry.Add(placement.Creature, placement.Island, placement.Envelope))
                {
                    // The plan is built against the registry's own budget, so this
                    // is unreachable rather than routine - say so instead of letting
                    // an island quietly lose an animal.
                    Console.WriteLine("[island-fauna] the registry refused planned creature "
                        + placement.Creature.EntityId + " on " + placement.Creature.IslandId
                        + "; it will not exist this run.");
                    continue;
                }
                _planned[placement.Creature.EntityId] = placement;

                if (!_byIsland.TryGetValue(placement.Creature.IslandId, out IslandPopulation? population))
                {
                    population = new IslandPopulation
                    {
                        Island = placement.Island,
                        Envelope = placement.Envelope,
                    };
                    _byIsland[placement.Creature.IslandId] = population;
                }
                population.EntityIds.Add(placement.Creature.EntityId);
                (placement.Creature.Species == FaunaSpecies.MantaRay
                    ? population.MantaIds : population.JellyIds)
                    .Add(placement.Creature.EntityId);
            }

            Console.WriteLine("[island-fauna] ON: seeded " + _registry.Count + " creature(s) across "
                + IslandFaunaPlan.IslandCount(plan) + " of " + islands.Count + " island(s); the world"
                + " wanted " + demand + " and the world-wide budget is " + _registry.MaxConcurrent
                + " (" + BudgetEnv + ").");
            Console.WriteLine("[island-fauna] interest is ISLAND-KEYED at "
                + _loadRadius.ToString("0") + " m load / " + _unloadRadius.ToString("0")
                + " m unload to the island envelope (" + RadiusEnv + "), capped at "
                + _peerBudget + " creature(s) per peer (" + PeerBudgetEnv + "). At a "
                + _registry.PoseInterval.TotalMilliseconds.ToString("0")
                + " ms pose cadence the worst case ONE peer can receive is "
                + IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond(
                    _peerBudget, _registry.PoseInterval).ToString("0")
                + " fauna transform update(s) a second, whatever the world's population.");

            // NAME the populated islands. "8 of 46" tells an operator the budget bit;
            // it does not tell a player where to go to see the feature at all, and a
            // feature nobody can find is indistinguishable from one that is broken.
            List<string> populated = new List<string>();
            foreach (FaunaPlacement placement in plan)
            {
                string id = placement.Creature.IslandId.ToString();
                if (!populated.Contains(id)) populated.Add(id);
            }
            if (populated.Count > 0)
            {
                // Elided past a dozen: naming every island was useful when eight of
                // forty-six carried anything and is noise now that all of them do.
                const int NameLimit = 12;
                Console.WriteLine("[island-fauna] populated island(s): "
                    + string.Join(", ", populated.Take(NameLimit))
                    + (populated.Count > NameLimit
                        ? " and " + (populated.Count - NameLimit) + " more." : "."));
            }

            if (demand > _registry.MaxConcurrent)
            {
                Console.WriteLine("[island-fauna] " + (demand - _registry.Count)
                    + " planned creature(s) were dropped for budget: the remaining islands carry NO"
                    + " fauna. Raise " + BudgetEnv + " to cover more of the world, and expect the"
                    + " worst-case update rate above to rise in proportion.");
            }
        }

        /// <summary>
        /// Binds the terrain lifecycle, so no creature can be added to a peer that
        /// does not yet hold its island. Unbound is the fail-open legacy path, which
        /// is what a Haven-only world without terrain interest needs.
        /// </summary>
        internal void AttachTerrainReadiness(Func<ENetPeerHandle, IslandId, bool> terrainReady) =>
            _terrainReady = terrainReady ?? throw new ArgumentNullException(nameof(terrainReady));

        /// <summary>
        /// Warns if the fauna radius reaches past the terrain radius, in which case
        /// the wildlife beyond it is invisible for a reason nothing else reports.
        ///
        /// A CREATURE MUST NOT OUTRUN ITS ISLAND - <see cref="Execute"/> drops any add
        /// for an island the peer does not hold. That guard is correct and stays, but
        /// it makes the terrain radius a silent CEILING on the fauna radius: an
        /// operator who raises <see cref="RadiusEnv"/> past
        /// <c>WAREBORN_TERRAIN_LOAD_RADIUS_M</c> gets creatures that are admitted,
        /// queued, dropped and requeued forever, and NO log line anywhere says why the
        /// animals never arrive. It cost a debugging round to find once; the boot line
        /// exists so it cannot cost a second.
        ///
        /// It warns rather than clamping: the terrain radius is read by another
        /// service and a fauna service that silently rewrote another subsystem's
        /// configuration would be a worse surprise than the one it is reporting.
        /// </summary>
        internal void WarnIfPastTerrainRadius(double terrainLoadRadiusMetres)
        {
            if (!_enabled || _loadRadius <= 0.0 || terrainLoadRadiusMetres <= 0.0
                || _loadRadius <= terrainLoadRadiusMetres)
            {
                return;
            }

            Console.WriteLine("[island-fauna] WARNING: the fauna radius ("
                + _loadRadius.ToString("0") + " m, " + RadiusEnv + ") reaches PAST the island"
                + " terrain radius (" + terrainLoadRadiusMetres.ToString("0") + " m, "
                + IslandTerrainInterestPolicy.LoadRadiusEnvVar + "). A creature is never sent to a"
                + " peer that does not hold its island, so every creature between the two radii"
                + " will be silently withheld. Raise the terrain radius or lower the fauna one.");
        }

        /// <summary>
        /// Hands lifecycle over from the connect spawn plan. A creature is never in
        /// that plan, but a joiner instantiating the world on its main thread must
        /// not also be handed wildlife: this is the same settle window the resource
        /// stream waits out, for the same OOM reason.
        /// </summary>
        internal void NoteConnectPlanComplete(ENetPeerHandle peer)
        {
            if (!_enabled) return;
            PeerState state = StateFor(peer);
            if (state.ConnectPlanComplete) return;
            state.ConnectPlanComplete = true;
            state.ContinuousAfter = _clock.Elapsed
                + WorldsAdriftRebornGameServer.ResourceInterest.SettleDelay;
            state.NextReconcile = state.ContinuousAfter;
        }

        /// <summary>Whether an entity id names one of this server's creatures.</summary>
        internal bool IsFauna(long entityId) => _enabled && _registry.IsFauna(entityId);

        /// <summary>
        /// Where a creature is RIGHT NOW, or null for anything that is not one.
        /// <c>ComponentsSerializer</c>'s 190602 branch asks this: a creature is not
        /// in the world registry, so <c>TransformSeedFor</c> would hand it the
        /// PLAYER SPAWN and a checking-out peer would find a manta on its head.
        /// </summary>
        internal FixedPointPosition? PositionOf(long entityId) =>
            _enabled ? _registry.PositionOf(entityId) : null;

        /// <summary>
        /// What a creature is, or null for anything that is not one. The 1182/4322
        /// species branches ask this; nothing else knows a creature exists.
        /// </summary>
        internal FaunaSpecies? SpeciesOf(long entityId) =>
            _enabled && _planned.TryGetValue(entityId, out FaunaPlacement placement)
                ? placement.Creature.Species : (FaunaSpecies?)null;

        /// <summary>
        /// The whole creature record, or null for anything that is not one. The
        /// identity-component branches (1177 gender, 4326 manta variant, 4322
        /// jelly species) ask this: gender is a function of the member index and
        /// the variant's biome and the jelly's species are functions of the
        /// island, so the serializer needs the creature rather than three
        /// separate questions.
        /// </summary>
        /// <summary>Whether calf slots and their offsets are live this run.</summary>
        internal bool JuvenilesEnabled => _juveniles;

        /// <summary>
        /// THE AGE ONE CREATURE IS SERVED IN COMPONENT 1166, or null for anything
        /// that must not receive the component at all.
        ///
        /// NULL IS THE FLAG-OFF PATH AND IT MUST STAY THAT WAY. The serializer's
        /// 1166 branch is guarded on this returning a value, so with juveniles off
        /// - or on a jelly, or on an entity that is not a creature - the component
        /// falls through to the same unhandled path it takes today and the wire is
        /// byte-identical.
        ///
        /// EVERY OTHER ANSWER IS AN ADULT UNLESS IT ARGUES OTHERWISE. Serving 1166
        /// activates <c>AgeVisualizer</c> on EVERY manta it reaches, and the
        /// visualiser assigns a scale unconditionally, so the policy is total by
        /// construction: <see cref="IslandFaunaAge.Adult"/> is the default and the
        /// juvenile case is the narrow exception. See
        /// <see cref="IslandFaunaAge"/>'s remarks - this is Hazard 0.
        /// </summary>
        internal FaunaAgeState? AgeStateFor(long entityId)
        {
            if (!_enabled || !_juveniles) return null;
            if (!_planned.TryGetValue(entityId, out FaunaPlacement placement)) return null;

            // Jellies have NO scale path at all - no AgeVisualizer and no size
            // field on any component in the basic-creature stack - so 1166 on a
            // jelly has no consumer. Serving it would be traffic that draws
            // nothing, and a claim the client cannot honour.
            if (placement.Creature.Species != FaunaSpecies.MantaRay) return null;

            return AgeOf(placement.Creature, _clock.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// The age policy applied to one planned creature at one instant. Split
        /// out so a test can drive it without a peer, a socket or a clock.
        /// </summary>
        private FaunaAgeState AgeOf(FaunaCreature creature, double nowSeconds)
        {
            if (_ecology == null
                || !_byIsland.TryGetValue(creature.IslandId, out IslandPopulation? population))
            {
                return IslandFaunaAge.Adult;
            }

            // The species' OWN capacity and this creature's OWN rank in it, READ
            // off the seeded id list rather than re-derived. A second derivation
            // of either would be a second thing that can disagree with the prefix
            // the checkout layer actually walks.
            int rank = population.MantaIds.IndexOf(creature.EntityId);
            if (rank < 0) return IslandFaunaAge.Adult;

            // NO CALF SLOTS EXIST AT THIS COMMIT. Turning 1166 on is a change to
            // the whole species - the visualiser activates on every manta the
            // moment the component is answered - so the adult case ships and is
            // proved on its own first. The juvenile commit passes the family's
            // answer here instead of false.
            return IslandFaunaAge.StateFor(
                _ecology.WorldSeed, creature.IslandId, FaunaSpecies.MantaRay,
                population.MantaIds.Count, rank, isCalfSlot: false, nowSeconds);
        }

        internal FaunaCreature? CreatureOf(long entityId) =>
            _enabled && _planned.TryGetValue(entityId, out FaunaPlacement placement)
                ? placement.Creature : (FaunaCreature?)null;

        /// <summary>
        /// Guards component interest against the cross-channel unload race. Channel 5
        /// RemoveEntity and channel 2 interest are independent, so a request may
        /// arrive after the creature was unloaded; re-seeding it would leave native
        /// components on an entity the client no longer holds. Non-fauna ids and the
        /// disabled feature both fail open.
        /// </summary>
        internal bool MayServe(ENetPeerHandle peer, long entityId) =>
            !IsFauna(entityId)
            || (_peers.TryGetValue(peer, out PeerState? state) && state.Loaded.Contains(entityId));

        /// <summary>
        /// One call per main-loop turn: per-peer checkout, then every due pose.
        /// Cheap when the feature is off (one bool) and when nothing is due (an
        /// empty-dictionary walk that allocates nothing).
        /// </summary>
        internal void Tick()
        {
            if (!_enabled)
            {
                return;
            }

            TickCheckout();
            TickPoses();
        }

        internal void Forget(ENetPeerHandle peer) => _peers.Remove(peer);

        private void TickCheckout()
        {
            TimeSpan now = _clock.Elapsed;
            foreach ((ENetPeerHandle peer, PeerState state) in _peers.ToArray())
            {
                if (!state.ConnectPlanComplete || now < state.ContinuousAfter) continue;

                if (now >= state.NextReconcile)
                {
                    state.NextReconcile = now + ReconcileInterval;
                    Reconcile(peer, state);
                }
                if (now < state.NextSend || state.Pending.Count == 0) continue;
                state.NextSend = now + SendInterval;
                Execute(peer, state);
            }
        }

        /// <summary>
        /// Rebuilds this peer's pending work from WHICH ISLANDS it is near.
        ///
        /// Two pure steps and no geometry of its own:
        /// <see cref="IslandFaunaInterestPolicy.Admit"/> decides which islands'
        /// populations the peer should hold, and
        /// <see cref="IslandFaunaInterestPolicy.Reconcile"/> turns that into the
        /// add/remove work. The creature positions the previous version of this method
        /// sampled every 500 ms are not consulted at all any more - that sampling WAS
        /// the despawn bug, because it made a creature's checkout a function of where
        /// it happened to be in its orbit.
        /// </summary>
        private void Reconcile(ENetPeerHandle peer, PeerState state)
        {
            FixedPointPosition center = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);
            double now = _clock.Elapsed.TotalSeconds;

            List<FaunaIslandCandidate> candidates = new List<FaunaIslandCandidate>(_byIsland.Count);
            foreach ((IslandId islandId, IslandPopulation population) in _byIsland)
            {
                (int mantas, int jellies) = ExpressedFor(islandId, population, now);
                candidates.Add(new FaunaIslandCandidate(islandId,
                    population.Envelope.DistanceSquaredTo(center, population.Island),
                    mantas + jellies));
            }

            IReadOnlyList<IslandId> admitted = IslandFaunaInterestPolicy.Admit(
                candidates, state.Islands, _loadRadius,
                // A peer with no channel 5 can never unload, so it must never be told
                // to: an infinite unload radius means "retain what you have".
                state.RemoveSupported ? _unloadRadius : double.PositiveInfinity,
                _peerBudget);

            state.Islands.Clear();
            List<long> desired = new List<long>();
            foreach (IslandId islandId in admitted)
            {
                state.Islands.Add(islandId);
                // The EXPRESSED prefix, not the seeded roll: under the rhythm an
                // island shows the fraction of its capacity the cycle allows,
                // and the prefix rule means the same animals extend or trim -
                // the checkout layer streams the difference at its own pace,
                // which is the closed-form version of gradual convergence.
                // A deep-dived group is then filtered OUT whole: a school under
                // the island needs no members on any peer's wire (the Dive
                // behaviour's streaming-LOD half), and it departs and returns as
                // one thing through the ordinary remove/add machinery.
                IslandPopulation population = _byIsland[islandId];
                (int mantas, int jellies) = ExpressedFor(islandId, population, now);
                AddStreamed(desired, population, population.MantaIds, mantas,
                    FaunaSpecies.MantaRay, islandId, now);
                AddStreamed(desired, population, population.JellyIds, jellies,
                    FaunaSpecies.JellyFish, islandId, now);
            }

            ResourceInterestPolicy.ReplacePending(
                state.Pending,
                IslandFaunaInterestPolicy.Reconcile(desired, state.Loaded),
                MaxQueuedPerPeer);
        }

        private void Execute(ENetPeerHandle peer, PeerState state)
        {
            ResourceStreamAction action = state.Pending.Peek();

            // Revalidate at the last boundary. Nothing else adds a creature to a
            // peer, but a reconcile can queue work that a previous send already
            // satisfied, and duplicate AddEntity corrupts the client's entity map.
            if (!ResourceInterestPolicy.ShouldExecute(
                    state.ConnectPlanComplete, action, state.Loaded))
            {
                state.Pending.Dequeue();
                if (state.AssetRequestedFor == action.EntityId) state.AssetRequestedFor = 0;
                return;
            }

            if (action.Kind == ResourceStreamActionKind.Remove)
            {
                state.Pending.Dequeue();
                if (SendOPHelper.SendRemoveEntityOP(peer, action.EntityId))
                {
                    state.Loaded.Remove(action.EntityId);
                    PeerCheckoutCleanup.RemoveEntity(peer, action.EntityId);
                }
                else
                {
                    // Channel 5 was not negotiated after all. Retain the checkout so
                    // a later re-add cannot produce a second, inert copy.
                    state.RemoveSupported = false;
                }
                return;
            }

            if (!_planned.TryGetValue(action.EntityId, out FaunaPlacement placement))
            {
                state.Pending.Dequeue();
                return;
            }

            // A creature must not outrun its island: a peer that does not hold the
            // terrain has not loaded that part of the world at all. Drop rather than
            // park, so removals and other islands keep progressing; the next
            // reconcile requeues it once the terrain lands.
            if (_terrainReady != null && !_terrainReady(peer, placement.Creature.IslandId))
            {
                state.Pending.Dequeue();
                if (state.AssetRequestedFor == action.EntityId) state.AssetRequestedFor = 0;
                return;
            }

            // Creature-aware, not species-aware: a jelly's prefab is its ISLAND's
            // jelly species (four retail prefabs, IslandFaunaPolicy.JellySpeciesFor),
            // while a manta is always the one manta prefab.
            string prefab = IslandFaunaPolicy.PrefabNameFor(placement.Creature);
            if (state.AssetRequestedFor != action.EntityId)
            {
                // "notNeeded?" is the assetTYPE every other caller passes; the
                // context is separately the same literal via IslandCatalog.
                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                    prefab, IslandCatalog.DefaultTerrainAssetContext);
                state.AssetRequestedFor = action.EntityId;
                return; // a full cadence for the asset callback before AddEntity
            }

            state.Pending.Dequeue();
            state.AssetRequestedFor = 0;
            if (SendOPHelper.SendAddEntityOP(peer, action.EntityId, prefab,
                    IslandCatalog.DefaultTerrainAssetContext))
            {
                state.Loaded.Add(action.EntityId);
                WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, action.EntityId);
                Console.WriteLine("[island-fauna] added " + placement.Creature.Species + " "
                    + action.EntityId + " on " + placement.Creature.IslandId + " to "
                    + peer.DangerousGetHandle() + ".");
            }
        }

        /// <summary>
        /// Pushes every pose the registry says is due, to the peers holding that
        /// creature's 190602.
        ///
        /// Sent to each peer DIRECTLY, never through <c>RelayToOtherPlayers</c> -
        /// that method re-addresses an update to the SENDER's own avatar, so a
        /// creature's pose routed through it would teleport whoever received it. The
        /// same trap is documented on the falling-log pose push and on the nugget
        /// depletion sink; it has already cost this project a debugging round.
        /// </summary>
        private void TickPoses()
        {
            // NOBODY IS WATCHING, SO NOTHING IS SENT - and nothing is even computed.
            // The registry is silent when no pose is DUE, but with a full world the
            // due set is 24 creatures four times a second forever, whether or not a
            // single player is connected. Asking first whether any peer holds any
            // creature keeps an empty server's fauna cost at one integer compare per
            // loop turn. Skipping does not desynchronise anything: a pose is a
            // closed-form function of absolute elapsed time, so the first push after
            // somebody arrives is exactly where the creature would have been.
            if (!AnyPeerHoldsFauna())
            {
                return;
            }

            // Only the creatures SOMEBODY is holding are worth the trigonometry. The
            // world may now carry the whole catalogue while a peer holds a couple of
            // dozen, so this union is what keeps a fully populated world's per-turn
            // cost proportional to what is actually being watched.
            _held.Clear();
            foreach (PeerState state in _peers.Values)
            {
                _held.UnionWith(state.Loaded);
            }

            IReadOnlyList<FaunaPose> poses = _registry.DuePoses(_held.Contains);
            if (poses.Count == 0)
            {
                return;
            }

            // ONE sample index per turn, shared by every creature moving in it -
            // exactly what ShipPartMotionService and FallingLogService do. Per-
            // creature increments would make each animal's stamps climb at a rate
            // unrelated to the interval they are actually sent at, and the client's
            // interpolator plays back on the stamps.
            long sample = ++_sample;
            float stamp = ShipPartMotionPolicy.StampFor(sample, _registry.PoseInterval.TotalSeconds);

            foreach (FaunaPose pose in poses)
            {
                foreach (ENetPeerHandle peer in ConnectedPeers())
                {
                    if (!TryGetStoredRef(peer, pose.EntityId, TransformStateComponentId,
                            out ulong refId))
                    {
                        continue;
                    }

                    // THE ROTATION RIDES THIS SAME UPDATE. 190602 already carries a
                    // localRotation, so facing costs no extra packet, no extra
                    // component and no extra send - the per-peer ceiling of
                    // IslandFaunaInterestPolicy.DefaultPerPeerCreatures x the pose
                    // cadence is untouched by it.
                    //
                    // Sending the identity SENTINEL here, as this did until now, was
                    // not a neutral default: the client's AbstractLerpTransformBehaviour
                    // applies position and rotation TOGETHER whenever the position
                    // moved, so identity actively re-slammed every creature to "nose
                    // along world +Z" four times a second regardless of travel.
                    TransformState.Update update = ShipPartTransform.BuildParentlessWakeUpdate(
                        pose.Position,
                        new Improbable.Corelibrary.Math.Quaternion32(
                            Multiplayer.Placement.Quaternion32Packing.Encode(
                                pose.Rotation.W, pose.Rotation.X,
                                pose.Rotation.Y, pose.Rotation.Z)),
                        stamp);

                    // Keep this peer's stored 190602 in step with what it has just
                    // been told, so a re-serve cannot resurrect the seed pose.
                    if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId)
                        is TransformState.Data stored)
                    {
                        update.ApplyTo(stored);
                    }

                    SendOPHelper.SendComponentUpdateOp(peer, pose.EntityId,
                        new List<uint> { TransformStateComponentId },
                        new List<object> { update });
                }
            }
        }

        /// <summary>
        /// How many of each species this island EXPRESSES right now: the whole
        /// seeded roll without the ecology, the rhythm's fraction of it with.
        /// The seeded per-species counts ARE the island's clamped capacities -
        /// the plan built them from exactly that arithmetic - so no second
        /// capacity derivation exists to disagree with the first.
        /// </summary>
        private (int Mantas, int Jellies) ExpressedFor(
            IslandId islandId, IslandPopulation population, double nowSeconds)
        {
            if (_ecology == null)
            {
                return (population.MantaIds.Count, population.JellyIds.Count);
            }
            return (
                IslandFaunaRhythm.ExpressedCount(population.MantaIds.Count,
                    IslandFaunaRhythm.ExpressionAt(_ecology.WorldSeed, islandId,
                        FaunaSpecies.MantaRay, nowSeconds)),
                IslandFaunaRhythm.ExpressedCount(population.JellyIds.Count,
                    IslandFaunaRhythm.ExpressionAt(_ecology.WorldSeed, islandId,
                        FaunaSpecies.JellyFish, nowSeconds)));
        }

        /// <summary>
        /// Appends the expressed prefix of one species' ids, minus any group the
        /// behaviour schedule has deep-dived. The streamed decision is computed
        /// once per group per call - the same SegmentFor the pose path and the
        /// telemetry read, so what a peer holds is what the maps say exists.
        /// </summary>
        private void AddStreamed(List<long> desired, IslandPopulation population,
            List<long> ids, int expressed, FaunaSpecies species, IslandId islandId, double now)
        {
            if (_ecology == null)
            {
                desired.AddRange(ids.Take(expressed));
                return;
            }
            Dictionary<int, bool>? streamedByGroup = null;
            for (int i = 0; i < expressed && i < ids.Count; i++)
            {
                if (!_planned.TryGetValue(ids[i], out FaunaPlacement placement)) continue;
                int group = placement.Creature.SchoolIndex;
                streamedByGroup ??= new Dictionary<int, bool>();
                if (!streamedByGroup.TryGetValue(group, out bool streamed))
                {
                    streamed = IslandFaunaBehaviour.IsStreamed(
                        _ecology.SegmentFor(islandId, species, population.Envelope, group, now),
                        now);
                    streamedByGroup[group] = streamed;
                }
                if (streamed)
                {
                    desired.Add(ids[i]);
                }
            }
        }

        private bool AnyPeerHoldsFauna()
        {
            foreach (PeerState state in _peers.Values)
            {
                if (state.Loaded.Count > 0) return true;
            }
            return false;
        }

        private PeerState StateFor(ENetPeerHandle peer)
        {
            if (_peers.TryGetValue(peer, out PeerState? state)) return state;

            state = new PeerState
            {
                // RemoveEntity is channel 5, and a peer that cannot receive it would
                // keep every creature it ever saw for the rest of its session.
                RemoveSupported = EnetLayer.ENet_PeerChannelCount(peer)
                    > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP,
            };
            _peers[peer] = state;
            if (!state.RemoveSupported)
            {
                Console.WriteLine("[island-fauna] peer " + peer.DangerousGetHandle()
                    + " cannot receive RemoveEntity; it will retain every creature it is shown.");
            }
            return state;
        }

        private static bool TryGetStoredRef(ENetPeerHandle peer, long entityId, uint componentId,
            out ulong refId)
        {
            refId = 0;
            return GameState.Instance.ComponentMap.TryGetValue(peer,
                       out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                && byEntity.TryGetValue(entityId, out Dictionary<uint, ulong>? byComponent)
                && byComponent.TryGetValue(componentId, out refId);
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers() =>
            PeerManager.Instance.playerState.Keys.ToList();
    }
}
