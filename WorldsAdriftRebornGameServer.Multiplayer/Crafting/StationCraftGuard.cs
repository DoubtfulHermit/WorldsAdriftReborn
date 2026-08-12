using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// The pure at-most-one guard for a TIMED station craft (6.1): once a craft is accepted on
    /// a (station, player) it holds the aperture open for the recipe's craft time before
    /// completing, so a second StartCrafting arriving during that window must NOT consume
    /// materials or spawn a second part. One entry per in-flight craft.
    ///
    /// THE REGRESSION THIS GUARDS AGAINST (fix/craft-not-blocked): the guard is a real GATE -
    /// a second craft is refused while an entry is held. So the entry MUST be released on
    /// EVERY exit of a craft: normal completion, a consume failure, AND an exception thrown
    /// mid-completion (the spawn/push). A single leaked entry rejects every later craft at that
    /// station forever - "craft one part, then everything is blocked". The lifecycle here is
    /// therefore deliberately symmetric (<see cref="TryBegin"/> once, then exactly one of
    /// <see cref="Complete"/> / <see cref="Abandon"/>), and both terminal calls are idempotent
    /// so a belt-and-braces double release (finally + catch) can never throw or wedge.
    ///
    /// Element-agnostic and dependency-free: just longs and a set, so it unit-tests natively -
    /// no game install, no wire, no GameServer assembly. The process-wide instance lives in the
    /// GameServer StationCraftTracker; this type carries the whole state machine so the tests
    /// can drive begin -> reject-duplicate -> complete -> begin-again exactly as the server does.
    ///
    /// NOT thread-safe, deliberately: every writer runs on the single server poll loop (the
    /// 1003 handler drains there and the deferred completion fires there too).
    /// </summary>
    public sealed class StationCraftGuard
    {
        private readonly HashSet<(long station, long player)> _inProgress =
            new HashSet<(long, long)>();

        /// <summary>
        /// Try to begin a craft on (<paramref name="stationEntityId"/>, <paramref name="playerEntityId"/>).
        /// Returns false if one is already running there (the caller must then reject the
        /// duplicate WITHOUT consuming), true if this call reserved the slot.
        /// </summary>
        public bool TryBegin(long stationEntityId, long playerEntityId)
        {
            return _inProgress.Add((stationEntityId, playerEntityId));
        }

        /// <summary>
        /// Reserve (<paramref name="stationEntityId"/>, <paramref name="playerEntityId"/>) then run
        /// <paramref name="craftStart"/> with the reservation HELD. This is the release-on-every-exit
        /// doctrine expressed ONCE so no craft-start path can forget it: if <paramref name="craftStart"/>
        /// THROWS, the reservation is released before the exception propagates, so a throwing craft-start
        /// (a failed owner-uid resolve, a scheduling failure, anything between reserve and the deferred
        /// completion) can never leak the guard - the "craft one part, then everything is blocked forever"
        /// regression. Returns:
        ///   * <c>false</c> when a craft is already in flight there - nothing is run, nothing reserved;
        ///   * <c>true</c> when <paramref name="craftStart"/> ran to completion - the reservation is STILL
        ///     HELD and the caller owns its release (normally the deferred completion's finally). On a
        ///     synchronous rejection inside <paramref name="craftStart"/> (e.g. a consume failure that
        ///     returns without throwing) the reservation is likewise still held, so the caller must
        ///     release it on that path too.
        /// A throw still propagates after the release, so the failure stays visible.
        /// </summary>
        public bool BeginGuarded(long stationEntityId, long playerEntityId, System.Action craftStart)
        {
            if (!TryBegin(stationEntityId, playerEntityId))
            {
                return false;
            }

            try
            {
                craftStart();
            }
            catch
            {
                Abandon(stationEntityId, playerEntityId);
                throw;
            }

            return true;
        }

        /// <summary>Whether a craft is currently in flight on this (station, player).</summary>
        public bool IsInProgress(long stationEntityId, long playerEntityId)
        {
            return _inProgress.Contains((stationEntityId, playerEntityId));
        }

        /// <summary>
        /// Release a (station, player) when its craft COMPLETES. Idempotent, so a
        /// belt-and-braces release path can call it more than once safely.
        /// </summary>
        public void Complete(long stationEntityId, long playerEntityId)
        {
            _inProgress.Remove((stationEntityId, playerEntityId));
        }

        /// <summary>
        /// Release a (station, player) when its craft is ABANDONED (a consume failure, or an
        /// exception thrown before/mid-completion). Idempotent, and identical in effect to
        /// <see cref="Complete"/> - named apart only so the call sites read their intent.
        /// </summary>
        public void Abandon(long stationEntityId, long playerEntityId)
        {
            _inProgress.Remove((stationEntityId, playerEntityId));
        }

        /// <summary>Forget every in-flight craft a leaving player had, so a re-use of that id is clean.</summary>
        public void ForgetPlayer(long playerEntityId)
        {
            _inProgress.RemoveWhere(k => k.player == playerEntityId);
        }

        /// <summary>How many crafts are in flight across all stations/players.</summary>
        public int Count => _inProgress.Count;
    }
}
