using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The server's record of every ship a player has BUILT this session, keyed by the
    /// world-entity ids its hull and deck were spawned as. It is the ship-craft
    /// counterpart of <c>Placement.PlacedShipyards</c>: <see cref="BuiltShipSpawner"/>
    /// writes one hull entry (with the design's hull bytes) and one deck entry when a
    /// build completes, and <c>ComponentsSerializer</c> reads them back to serve
    /// per-entity truth the single global test ship's branches cannot:
    ///
    ///   * the 1209 branch serves a BUILT hull its OWN <see cref="HullBytesFor"/> (the
    ///     player's saved design) instead of the global minimum hull, so different
    ///     builds render as different ships;
    ///   * the 1099 branch treats a built hull like the test hull (empty materials,
    ///     not salvageable) and a built deck like the test deck (one Wood material, so
    ///     ShipDeckVisualizer.OnEnable does not IndexOutOfRange on an empty list).
    ///
    /// In-memory only, exactly like the node/shipyard/databank ledgers: "persistent"
    /// for this milestone means "visible to every connected client until the server
    /// restarts". A restart-durable built-ship ledger is the documented follow-on.
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and the build
    /// timer's completion callback is drained on it, like every other writer here.
    /// </summary>
    internal static class BuiltShips
    {
        private static readonly Dictionary<long, byte[]> HullBytesByEntityId = new Dictionary<long, byte[]>();
        private static readonly HashSet<long> DeckEntityIds = new HashSet<long>();
        private static int _sequence;

        /// <summary>
        /// A never-reused suffix for a built ship's registration keys. The key is what
        /// the world-entity registry allocates a shared entity id from, so it must be
        /// unique for the life of the process; a monotonic counter is the simplest
        /// thing that cannot collide across builds.
        /// </summary>
        internal static int NextSequence()
        {
            return _sequence++;
        }

        /// <summary>
        /// Records a newly built hull's entity id and the hull bytes its 1209 must
        /// serve. The bytes are stored as given (the spawner has already validated and,
        /// if needed, fallen back to the minimum hull), so a serve is a straight lookup.
        /// </summary>
        internal static void RegisterHull(long hullEntityId, byte[] hullBytes)
        {
            HullBytesByEntityId[hullEntityId] = hullBytes;
        }

        /// <summary>Records a newly built deck's entity id so the 1099 branch gives it the deck material.</summary>
        internal static void RegisterDeck(long deckEntityId)
        {
            DeckEntityIds.Add(deckEntityId);
        }

        /// <summary>Whether this entity id is a built ship's hull.</summary>
        internal static bool IsBuiltHull(long entityId)
        {
            return HullBytesByEntityId.ContainsKey(entityId);
        }

        /// <summary>Whether this entity id is a built ship's deck.</summary>
        internal static bool IsBuiltDeck(long entityId)
        {
            return DeckEntityIds.Contains(entityId);
        }

        /// <summary>
        /// The hull bytes a built hull's 1209 CustomShipHullState must serve, or null if
        /// the id is not a built hull (the caller then serves the global minimum hull).
        /// </summary>
        internal static byte[]? HullBytesFor(long entityId)
        {
            return HullBytesByEntityId.TryGetValue(entityId, out byte[]? bytes) ? bytes : null;
        }

        /// <summary>How many ships have been built this session.</summary>
        internal static int Count => HullBytesByEntityId.Count;
    }
}
