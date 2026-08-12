using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The process-wide at-most-one guard for a TIMED station craft (6.1): once a craft is
    /// accepted on a (station, player) it holds the aperture open for the recipe's craft time
    /// before completing, so a second StartCrafting arriving during that window must NOT
    /// consume materials or spawn a second part again. Keyed by (station, player) so two
    /// players can craft at one bench and one player can craft at two benches independently.
    ///
    /// This is now a THIN wrapper over the pure, dependency-free <see cref="StationCraftGuard"/>
    /// in the Multiplayer assembly, so the whole begin -> reject-duplicate -> complete ->
    /// begin-again state machine unit-tests natively (StationCraftGuardTests). The
    /// process-wide guard is the single instance held here.
    ///
    /// LEAK = PERMANENT BLOCK. A held entry that is never released rejects every later craft at
    /// that station ("craft one part, then all blocked"), so the caller MUST release on EVERY
    /// exit - normal completion, consume failure, AND an exception thrown mid-completion. Both
    /// release calls are idempotent to make a belt-and-braces (finally + catch) release safe.
    ///
    /// Single-loop only, like the other crafting ledgers: every writer runs on the poll loop
    /// (the 1003 handler drains there and the deferred completion fires there too).
    /// </summary>
    internal static class StationCraftTracker
    {
        private static readonly StationCraftGuard Guard = new StationCraftGuard();

        /// <summary>
        /// Try to begin a craft on (<paramref name="stationEntityId"/>, <paramref name="playerEntityId"/>).
        /// Returns false if one is already running there (the caller must then reject the
        /// duplicate WITHOUT consuming), true if this call reserved the slot.
        /// </summary>
        internal static bool TryBegin(long stationEntityId, long playerEntityId)
        {
            return Guard.TryBegin(stationEntityId, playerEntityId);
        }

        /// <summary>
        /// Reserve (station, player) and run <paramref name="craftStart"/> with the reservation held,
        /// releasing it automatically if <paramref name="craftStart"/> throws (release-on-every-exit).
        /// Returns false when a craft is already in flight there (nothing run), true when
        /// <paramref name="craftStart"/> completed with the reservation still held for the caller's
        /// later completion. See <see cref="StationCraftGuard.BeginGuarded"/>.
        /// </summary>
        internal static bool BeginGuarded(long stationEntityId, long playerEntityId, System.Action craftStart)
        {
            return Guard.BeginGuarded(stationEntityId, playerEntityId, craftStart);
        }

        /// <summary>Whether a craft is currently in flight on this (station, player).</summary>
        internal static bool IsInProgress(long stationEntityId, long playerEntityId)
        {
            return Guard.IsInProgress(stationEntityId, playerEntityId);
        }

        /// <summary>
        /// Release a (station, player) when its craft completes (or is abandoned). Idempotent,
        /// so the deferred completion's finally + catch can both fire without harm.
        /// </summary>
        internal static void End(long stationEntityId, long playerEntityId)
        {
            Guard.Complete(stationEntityId, playerEntityId);
        }

        /// <summary>Forget every in-flight craft a leaving player had, so a re-use of that id is clean.</summary>
        internal static void ForgetPlayer(long playerEntityId)
        {
            Guard.ForgetPlayer(playerEntityId);
        }
    }
}
