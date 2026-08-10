using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server-side "which entities are scannable databanks" ledger, populated
    /// when a databank world entity is given its entity id (the same spawn seam the
    /// deposit uses for <c>NodeRegistry</c>). The 2107 scan handler consults it to
    /// decide whether a ScanEntityEvent's target is worth knowledge and how much.
    ///
    /// Pure and process-global, mirroring <see cref="NodeRegistry"/>: a databank is a
    /// fixed world fact, not per-player state, so one registration serves every
    /// player who scans it.
    /// </summary>
    public static class DatabankLedger
    {
        private static readonly Dictionary<long, long> GrantByEntity = new Dictionary<long, long>();

        /// <summary>
        /// Register a placed databank and the knowledge a first scan grants.
        /// Idempotent: re-registration (a second joiner walking the same spawn step)
        /// returns false and does not change the grant.
        /// </summary>
        public static bool Register(long entityId, long grantAmount)
        {
            if (GrantByEntity.ContainsKey(entityId))
            {
                return false;
            }
            GrantByEntity[entityId] = grantAmount;
            return true;
        }

        /// <summary>True if the entity is a registered scannable databank.</summary>
        public static bool IsDatabank(long entityId) => GrantByEntity.ContainsKey(entityId);

        /// <summary>The knowledge a first scan of this databank grants, or 0 if not a databank.</summary>
        public static long GrantFor(long entityId) =>
            GrantByEntity.TryGetValue(entityId, out long grant) ? grant : 0;

        /// <summary>The number of registered databanks.</summary>
        public static int Count => GrantByEntity.Count;

        /// <summary>Test seam only: forget every registered databank.</summary>
        public static void Clear() => GrantByEntity.Clear();
    }
}
