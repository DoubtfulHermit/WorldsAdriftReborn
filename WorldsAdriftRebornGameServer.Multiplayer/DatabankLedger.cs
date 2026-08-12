using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server-side "which entities are scannable databanks" ledger, populated
    /// when a databank world entity is given its entity id (the same spawn seam the
    /// deposit uses for <c>NodeRegistry</c>). The 2107 scan handler consults it to
    /// decide whether a ScanEntityEvent's target is worth knowledge and how much, and
    /// to build the scan-note text (<see cref="ScanDataFor"/>).
    ///
    /// Pure and process-global, mirroring <see cref="NodeRegistry"/>: a databank is a
    /// fixed world fact, not per-player state, so one registration serves every
    /// player who scans it.
    /// </summary>
    public static class DatabankLedger
    {
        private readonly struct Entry
        {
            internal Entry(long grant, string title, string description)
            {
                Grant = grant;
                Title = title ?? "";
                Description = description ?? "";
            }

            internal long Grant { get; }
            internal string Title { get; }
            internal string Description { get; }
        }

        private static readonly Dictionary<long, Entry> ByEntity = new Dictionary<long, Entry>();

        /// <summary>
        /// Register a placed databank and the knowledge a first scan grants, with no
        /// scan-note text (kept for callers/tests that only care about the grant).
        /// Idempotent: re-registration returns false and changes nothing.
        /// </summary>
        public static bool Register(long entityId, long grantAmount) =>
            Register(entityId, grantAmount, "", "");

        /// <summary>
        /// Register a placed databank with its first-scan knowledge grant AND the scan
        /// note's <paramref name="title"/>/<paramref name="description"/> (served as
        /// ScannableData JSON by <see cref="ScanDataFor"/> so the client's scan note
        /// prints text instead of a blank). Idempotent: re-registration (a second
        /// joiner walking the same spawn step) returns false and does not change it.
        /// </summary>
        public static bool Register(long entityId, long grantAmount, string title, string description)
        {
            if (ByEntity.ContainsKey(entityId))
            {
                return false;
            }
            ByEntity[entityId] = new Entry(grantAmount, title, description);
            return true;
        }

        /// <summary>True if the entity is a registered scannable databank.</summary>
        public static bool IsDatabank(long entityId) => ByEntity.ContainsKey(entityId);

        /// <summary>The knowledge a first scan of this databank grants, or 0 if not a databank.</summary>
        public static long GrantFor(long entityId) =>
            ByEntity.TryGetValue(entityId, out Entry entry) ? entry.Grant : 0;

        /// <summary>
        /// The <c>scanData</c> JSON a scan of this databank carries so the client's scan
        /// note prints its title/body - a minimal ScannableData object built by
        /// <see cref="ScannableNote.Json"/>. Empty string for a non-databank (the caller
        /// then owes no note). A databank registered without note text still yields a
        /// well-formed (empty-fields) ScannableData object, which parses non-null.
        /// </summary>
        public static string ScanDataFor(long entityId) =>
            ByEntity.TryGetValue(entityId, out Entry entry)
                ? ScannableNote.Json(entry.Title, entry.Description)
                : "";

        /// <summary>The number of registered databanks.</summary>
        public static int Count => ByEntity.Count;

        /// <summary>Test seam only: forget every registered databank.</summary>
        public static void Clear() => ByEntity.Clear();
    }
}
