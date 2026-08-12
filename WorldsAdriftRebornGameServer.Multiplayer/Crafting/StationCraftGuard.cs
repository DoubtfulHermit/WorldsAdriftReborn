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
