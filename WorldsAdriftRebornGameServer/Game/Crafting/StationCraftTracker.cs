using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The at-most-one guard for a TIMED station craft (6.1): once a craft is accepted on a
    /// (station, player) it holds the aperture open for the recipe's craft time before
    /// completing, so a second StartCrafting arriving during that window must NOT consume
    /// materials or spawn a second part again. One entry per in-flight craft; the deferred
    /// completion clears it. Keyed by (station, player) so two players can craft at one bench
    /// and one player can craft at two benches independently.
    ///
    /// Single-loop only, like the other crafting ledgers: every writer runs on the poll loop
    /// (the 1003 handler drains there and the deferred completion fires there too).
    /// </summary>
    internal static class StationCraftTracker
    {
        private static readonly HashSet<(long station, long player)> InProgress =
            new HashSet<(long, long)>();

        /// <summary>
        /// Try to begin a craft on (<paramref name="stationEntityId"/>, <paramref name="playerEntityId"/>).
        /// Returns false if one is already running there (the caller must then reject the
        /// duplicate WITHOUT consuming), true if this call reserved the slot.
        /// </summary>
        internal static bool TryBegin(long stationEntityId, long playerEntityId)
        {
            return InProgress.Add((stationEntityId, playerEntityId));
        }

        /// <summary>Release a (station, player) when its craft completes (or is abandoned).</summary>
        internal static void End(long stationEntityId, long playerEntityId)
        {
            InProgress.Remove((stationEntityId, playerEntityId));
        }

        /// <summary>Forget every in-flight craft a leaving player had, so a re-use of that id is clean.</summary>
        internal static void ForgetPlayer(long playerEntityId)
        {
            InProgress.RemoveWhere(k => k.player == playerEntityId);
        }
    }
}
