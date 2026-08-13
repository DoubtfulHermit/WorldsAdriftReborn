namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHETHER the shipped loading-barrier is armed, HOW LONG a joining client may
    /// hold the loading screen before the server gives up waiting, and WHICH world
    /// entities belong to the initial "world ready" set - and nothing else.
    ///
    /// WHY THIS EXISTS. The custom server currently seeds WA's own
    /// <c>190000 EntityLoadingControl</c> as <c>Idle</c> with an EMPTY entity list
    /// and <c>190002 Activated</c> as already true, so the shipped
    /// <c>190001 EntityLoadingResponse</c> readiness protocol never runs: the
    /// loading screen fades the moment the player's activation component arrives,
    /// while dozens of AfterPlayer world entities are still compiling and
    /// instantiating on the client's main thread. That is the spawn hitch.
    ///
    /// The proper fix (docs/research/findings-spawn-lag.md, Rank 1) is to seed
    /// <c>190000</c> as <c>Requested</c> naming an INITIAL set of entities, hold
    /// <c>190002</c> false, and only publish <c>Activated=true</c> once the client
    /// signals the initial set is ready via <c>190001</c>. This type is the pure
    /// policy half of that: the env-to-config decisions and the initial/distant
    /// PARTITION, with no ENet, no Improbable types, and no wall clock, so a test
    /// can pin every rule. The ENet wiring (seeding the components, granting the
    /// 190001 writer, the 190001 handler, the timeout push) is the server's glue.
    ///
    /// EVERY RULE FAILS SAFE. A loading barrier that stops the server booting, or
    /// that strands a player on a loading screen forever, is worse than no barrier
    /// at all - so a bad env var falls back to a safe default rather than throwing,
    /// and the whole feature is OFF unless explicitly enabled.
    /// </summary>
    public static class LoadBarrierPolicy
    {
        /// <summary>
        /// The environment variable that arms the barrier. OFF unless it is exactly
        /// "1": an unset, empty, or any-other value keeps the current behaviour
        /// (190000 Idle/empty, 190002 immediately true), so an operator who has
        /// never heard of this flag gets exactly what they had before.
        /// </summary>
        public const string EnableEnvVar = "WAREBORN_LOAD_BARRIER";

        /// <summary>The environment variable that overrides the readiness timeout, in milliseconds.</summary>
        public const string TimeoutEnvVar = "WAREBORN_LOAD_BARRIER_TIMEOUT_MS";

        /// <summary>
        /// How long the server waits for a peer's <c>190001 Loaded=true</c> before
        /// activating it anyway. Long enough for a cold-cache client to instantiate
        /// the initial set behind the loading screen; short enough that a client
        /// that will NEVER signal ready (an old mod build with no checker, a stuck
        /// prefab) is not trapped on the loading screen for more than this.
        /// </summary>
        public const int DefaultTimeoutMs = 15000;

        /// <summary>
        /// Lower clamp on the timeout. Below this the barrier would routinely fire
        /// its fallback before a cold-cache client could finish the initial set,
        /// which would put the hitch back exactly where it was.
        /// </summary>
        public const int MinTimeoutMs = 1000;

        /// <summary>
        /// Upper clamp on the timeout. A typo of a very large value would leave a
        /// genuinely stuck client on the loading screen for minutes; the fallback
        /// exists precisely so that cannot happen.
        /// </summary>
        public const int MaxTimeoutMs = 120000;

        /// <summary>
        /// Whether the barrier is armed for an env value. Only the exact string "1"
        /// enables it; everything else - unset, empty, "0", "true", nonsense -
        /// leaves it off. Deliberately stricter than <see cref="TimeoutFrom"/>: a
        /// feature that reshapes the spawn pipeline must be turned on on purpose,
        /// never by an accidental non-empty value.
        /// </summary>
        public static bool IsEnabled(string? env) => env == "1";

        /// <summary>
        /// The readiness timeout for an env value, clamped to
        /// [<see cref="MinTimeoutMs"/>, <see cref="MaxTimeoutMs"/>]. Unset, empty,
        /// unparsable, zero, or negative all fall back to
        /// <see cref="DefaultTimeoutMs"/> - a perf/safety knob must never disable
        /// its own safety net or take the server down.
        /// </summary>
        public static TimeSpan TimeoutFrom(string? env)
        {
            if (!int.TryParse(env, out int ms) || ms <= 0)
            {
                return TimeSpan.FromMilliseconds(DefaultTimeoutMs);
            }

            if (ms < MinTimeoutMs)
            {
                return TimeSpan.FromMilliseconds(MinTimeoutMs);
            }

            return TimeSpan.FromMilliseconds(ms > MaxTimeoutMs ? MaxTimeoutMs : ms);
        }

        /// <summary>
        /// Whether a registered world entity belongs to the INITIAL set: the things
        /// that must exist on the client before the loading screen is allowed to
        /// fade. That is the ground the player stands on and the player's own ship -
        /// the island, the ship hull, and every bolted part (deck, helm, engine,
        /// sail). Everything else (trees, ore, databanks, the diagnostic proof
        /// island) is DISTANT scenery: it still streams in, but it does not gate
        /// activation, so 21 trees and 21 ore no longer sit on the critical path of
        /// a join.
        ///
        /// Deliberately key-based rather than radius-based. A radius around the
        /// spawn point is the more general rule (findings Rank 1 suggests ~120 m),
        /// but it needs validating against the real Haven placements and it makes
        /// the initial set depend on positions that can move; "the island and the
        /// player's ship" is the load-bearing subset by construction and is stable.
        /// Radius refinement is a follow-up tuning, not a correctness prerequisite.
        /// </summary>
        public static bool IsInitialKey(string? key)
        {
            return key == WorldEntities.IslandKey
                || key == WorldEntities.ShipFrameKey
                || WorldEntities.IsBoltedPartKey(key)
                // Every entity of a BUILT ship - its hull and each derived deck panel.
                // A built ship's hull mesh and the client's per-panel MakeDeck collider
                // generation are the heaviest work a joiner does, so they belong behind
                // the loading screen (frozen, out of view). Left out of the initial set
                // they stream in-view after the barrier lifts and freeze/crash the
                // second player - the observed regression. See BuiltShipPlacement.
                || Ship.BuiltShipPlacement.IsBuiltShipEntityKey(key)
                // THE WHOLE STATIC WORLD. This client instantiates entities
                // SYNCHRONOUSLY on the main thread (~100 ms/frame budget), so every
                // deposit/tree/shard/canister that streams in AFTER the screen lifts is
                // a visible hitch - the "game stutters when it starts rendering" both
                // players reported, on Windows and Linux alike (platform-independent,
                // so not a wine problem). Retail hid exactly this behind its loading
                // screen. A single small island's statics are a few seconds of extra
                // loading screen instead of half a minute of in-view stutter.
                || KeyHasPrefix(key, "deposit-")
                || KeyHasPrefix(key, "atlas-shard-")
                || KeyHasPrefix(key, "fuel-pod-")
                || KeyHasPrefix(key, "tree-")
                || KeyHasPrefix(key, "databank-")
                || KeyHasPrefix(key, "metal-");
        }

        private static bool KeyHasPrefix(string? key, string prefix)
        {
            return key != null && key.StartsWith(prefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The registered world entities in the initial set, in registration order.
        /// The counterpart to <see cref="DistantEntities"/>; together they partition
        /// every registration exactly once.
        /// </summary>
        public static IReadOnlyList<WorldEntity> InitialEntities(WorldEntityRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            List<WorldEntity> initial = new List<WorldEntity>();
            foreach (WorldEntity entity in registry.Registrations)
            {
                if (IsInitialKey(entity.Key))
                {
                    initial.Add(entity);
                }
            }
            return initial;
        }

        /// <summary>
        /// The registered world entities that are NOT in the initial set, in
        /// registration order - the distant scenery that streams in after the
        /// loading screen has already lifted.
        /// </summary>
        public static IReadOnlyList<WorldEntity> DistantEntities(WorldEntityRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            List<WorldEntity> distant = new List<WorldEntity>();
            foreach (WorldEntity entity in registry.Registrations)
            {
                if (!IsInitialKey(entity.Key))
                {
                    distant.Add(entity);
                }
            }
            return distant;
        }
    }
}
