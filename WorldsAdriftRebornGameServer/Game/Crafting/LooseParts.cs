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
    /// The live index is in-memory, while <see cref="Multiplayer.Persistence.LoosePartRecord"/>
    /// is its restart-durable form. Restore allocates fresh entity ids and repopulates this
    /// index before any peer connects.
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and the craft
    /// completion is drained on it, like every other writer here.
    /// </summary>
    internal static class LooseParts
    {
        private static readonly Dictionary<long, LoosePartDefinition> ByEntityId =
            new Dictionary<long, LoosePartDefinition>();

        /// <summary>
        /// The stable, cross-restart <c>PartUid</c> each live part was spawned with, keyed
        /// by entity id. It is what the persistence layer files a loose part's
        /// <c>LoosePartRecord</c> / <c>MountedPartRecord</c> under, so a loose part that
        /// later becomes mounted can have its loose record removed and re-expressed as a
        /// mount record without guessing which record is which.
        /// </summary>
        private static readonly Dictionary<long, string> PartUidByEntityId =
            new Dictionary<long, string>();

        /// <summary>
        /// The 1013 CraftableSpawningState a loose part is currently served with, per entity.
        /// Absent = the settled <see cref="CraftableSpawnPolicy.Done"/> value (not spawning,
        /// liftable). A FRESH craft records a <see cref="CraftableSpawnPolicy.Materializing"/>
        /// value here so its first checkout plays the dissolve; the materialize flip then
        /// removes the entry (back to Done) so a later checkout sees the finished part.
        /// </summary>
        private static readonly Dictionary<long, CraftableSpawnState> SpawnStateByEntityId =
            new Dictionary<long, CraftableSpawnState>();

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
        internal static void Register(long entityId, LoosePartDefinition definition, string partUid)
        {
            ByEntityId[entityId] = definition;
            PartUidByEntityId[entityId] = partUid ?? "";
        }

        /// <summary>The stable PartUid this part was spawned with, or null if the id is not a loose part.</summary>
        internal static string? PartUidFor(long entityId)
        {
            return PartUidByEntityId.TryGetValue(entityId, out string? uid) ? uid : null;
        }

        /// <summary>Whether this entity id is a crafted loose ship part.</summary>
        internal static bool Is(long entityId)
        {
            return ByEntityId.ContainsKey(entityId);
        }

        /// <summary>Forgets a part that was permanently dismantled.</summary>
        internal static bool Unregister(long entityId)
        {
            SpawnStateByEntityId.Remove(entityId);
            PartUidByEntityId.Remove(entityId);
            return ByEntityId.Remove(entityId);
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

        // -- MATERIALIZE (1013 CraftableSpawningState) dissolve, per entity --------------

        /// <summary>
        /// Record that a freshly-crafted part is DISSOLVING IN: its 1013 is served
        /// spawning=true for <paramref name="totalTime"/> seconds so the client plays the
        /// materialize. Called by the spawner BEFORE it broadcasts, so the first checkout
        /// already sees spawning=true. The mandatory flip to spawning=false (making the part
        /// liftable) is <see cref="MarkSpawned"/>.
        /// </summary>
        internal static void MarkSpawning(long entityId, float totalTime)
        {
            SpawnStateByEntityId[entityId] = CraftableSpawnPolicy.Materializing(totalTime);
        }

        /// <summary>
        /// Flip a part to the settled state (spawning=false, no timers) after its dissolve, so
        /// it becomes non-kinematic and liftable and a later checkout does not re-dissolve.
        /// </summary>
        internal static void MarkSpawned(long entityId)
        {
            SpawnStateByEntityId.Remove(entityId);
        }

        /// <summary>
        /// The 1013 CraftableSpawningState the serializer serves for this part: the recorded
        /// in-progress dissolve while it is materializing, else the settled
        /// <see cref="CraftableSpawnPolicy.Done"/>.
        /// </summary>
        internal static CraftableSpawnState SpawnStateFor(long entityId)
        {
            return SpawnStateByEntityId.TryGetValue(entityId, out CraftableSpawnState state)
                ? state
                : CraftableSpawnPolicy.Done;
        }
    }
}
