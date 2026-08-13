using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHEN the spawn chain may stop waiting for a client's ack and advance to the
    /// next step anyway - the pure policy half of the spawn-chain stall fix.
    ///
    /// WHY THIS EXISTS. A joining client is walked into the world step by step
    /// (see <see cref="SpawnPlan"/>), and each step waits for the client's ack of
    /// the previous op: a RequestAsset waits for the asset-loaded reply, an
    /// AddEntity for the entity-added reply. That lockstep is the only throttle on
    /// client-side loading - but it had NO safety net: one ack that never arrives
    /// (an asset whose async load coroutine dies in the loading-screen handover, a
    /// reply lost to a client bug, an op the client never received) parked the
    /// whole chain FOREVER, and every world entity behind the stuck step was
    /// silently never delivered. Live case 2026-08-12: the chain stopped at the
    /// 'global' entity and the restored shipyard + assembly station behind it never
    /// reached the client.
    ///
    /// WHY ADVANCING IS SAFE. Both acks are cheap-to-miss:
    /// * An AddEntity ack is sent by the modded CoreSdk the moment the AddEntityOp
    ///   packet is DESERIALIZED (before the game even dispatches it), so a missing
    ///   one means the packet or reply is gone, not that the client is still busy.
    /// * Advancing past a RequestAsset sends the AddEntity for a prefab the client
    ///   may not have loaded - which the client mod's synchronous rescue path
    ///   (WorkerSpecificAssetDatabaseTemplateProvider_Patch) handles by loading the
    ///   same Resources asset on the spot and compiling the template, after which
    ///   the entity renders normally. Worst case (an island bundle the rescue
    ///   cannot load) the entity is dropped by the client with the chain still
    ///   advancing - a degraded world instead of a frozen handshake.
    ///
    /// ALWAYS ON. Unlike the opt-in perf knobs, the timeout cannot be disabled:
    /// a chain that can stall forever on one lost packet is a correctness bug, not
    /// a tuning choice. The env var only moves the timeout inside a sane band.
    ///
    /// PURE. No ENet, no wall clock: the caller supplies elapsed times from its
    /// own monotonic clock.
    /// </summary>
    public static class SpawnAckTimeoutPolicy
    {
        /// <summary>The environment variable that overrides the ack timeout, in milliseconds.</summary>
        public const string TimeoutEnvVar = "WAREBORN_SPAWN_ACK_TIMEOUT_MS";

        /// <summary>
        /// How long a performed step waits for its ack before the chain advances
        /// anyway. Generous against real load times (the slowest observed in-chain
        /// load, the island bundle, acks within ~2 s on a cold cache) yet short
        /// enough that a lost ack costs one pause, not a session.
        /// </summary>
        public const int DefaultTimeoutMs = 5000;

        /// <summary>
        /// Lower clamp. Below this a slow-but-healthy client (cold cache, big
        /// bundle) would be overtaken by its own spawn chain, reintroducing the
        /// AddEntity-races-the-load bug the ack gating exists to prevent.
        /// </summary>
        public const int MinTimeoutMs = 1000;

        /// <summary>
        /// Upper clamp. A typo of a colossal value would turn the safety net back
        /// into a near-infinite stall; a minute is already far beyond any real
        /// client-side load.
        /// </summary>
        public const int MaxTimeoutMs = 60000;

        /// <summary>
        /// The ack timeout for an env value, clamped to
        /// [<see cref="MinTimeoutMs"/>, <see cref="MaxTimeoutMs"/>]. Unset, empty,
        /// unparsable, zero or negative all fall back to the default: the safety
        /// net can be tuned but never removed.
        /// </summary>
        public static TimeSpan TimeoutFrom(string? env)
        {
            if (!int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) || ms <= 0)
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
        /// Whether the chain should stop waiting and advance past the current step.
        ///
        /// Only a step that was actually PERFORMED can time out - a step still held
        /// back (by the pacer, or not yet reached) has not asked for an ack. The
        /// LAST step never advances: parking there is the plan's normal "done"
        /// state, exactly as the ack path treats it.
        /// </summary>
        public static bool ShouldAdvance(
            bool performed,
            bool isLastStep,
            TimeSpan performedAt,
            TimeSpan now,
            TimeSpan timeout)
        {
            return performed
                && !isLastStep
                && now - performedAt >= timeout;
        }
    }
}
