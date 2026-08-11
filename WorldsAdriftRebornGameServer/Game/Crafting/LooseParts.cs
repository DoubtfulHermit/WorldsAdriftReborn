using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The server's record of every LOOSE ship part crafted this session, keyed by
    /// the world-entity id it was spawned as. It is the part counterpart of
    /// <see cref="BuiltShips"/>: <see cref="LoosePartSpawner"/> writes one entry when
    /// a craft completes, and <c>ComponentsSerializer</c> reads it back to serve the
    /// per-entity truth the generic branches cannot -
    ///
    ///   * 1120 ShipPartState: this part's prefabName / attachmentType / itemType /
    ///     title (attached=false), so the client loads the right prefab and knows how
    ///     it would mount;
    ///   * 8066 ShipRootState: served as "no ship" (shipRoot absent) for a loose part,
    ///     rather than the bolted-part "points at the hull" value;
    ///   * 1108/1236/1013: the lamp is on and functional and done spawning;
    ///   * 1099: the part's own itemType with no salvage flow.
    ///
    /// In-memory only, exactly like the built-ship / node / shipyard ledgers:
    /// "persistent" for this milestone means "visible to every connected client until
    /// the server restarts". A restart-durable loose-part ledger is the documented
    /// follow-on (the built-ship ledger's persistence work is the template).
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and the craft
    /// completion is drained on it, like every other writer here.
    /// </summary>
    internal static class LooseParts
    {
        private static readonly Dictionary<long, LoosePartDefinition> ByEntityId =
            new Dictionary<long, LoosePartDefinition>();
        private static int _sequence;

        /// <summary>
        /// A never-reused suffix for a loose part's registration key, so the shared
        /// entity id it is allocated from cannot collide across crafts. Monotonic,
        /// the same contract as <see cref="BuiltShips.NextSequence"/>.
        /// </summary>
        internal static int NextSequence()
        {
            return _sequence++;
        }

        /// <summary>
        /// Records a newly spawned loose part's entity id and the definition its
        /// serve branches read. Called by the spawner BEFORE it broadcasts, so the
        /// first peer to check the part out already sees per-entity truth.
        /// </summary>
        internal static void Register(long entityId, LoosePartDefinition definition)
        {
            ByEntityId[entityId] = definition;
        }

        /// <summary>Whether this entity id is a crafted loose ship part.</summary>
        internal static bool Is(long entityId)
        {
            return ByEntityId.ContainsKey(entityId);
        }

        /// <summary>
        /// The definition a loose part's serve branches read, or null when the id is
        /// not a loose part (the branch then serves nothing and best-effort interest
        /// skips it - only a loose part ever requests 1120/1108/1236).
        /// </summary>
        internal static LoosePartDefinition? DefFor(long entityId)
        {
            return ByEntityId.TryGetValue(entityId, out LoosePartDefinition? def) ? def : null;
        }

        /// <summary>How many loose parts have been crafted this session.</summary>
        internal static int Count => ByEntityId.Count;
    }
}
