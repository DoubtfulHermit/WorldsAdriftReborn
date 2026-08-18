using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about the scannable DATABANK this server can place - the KNOWLEDGE
    /// analogue of <see cref="MetalDeposits"/>. A databank is a single SpatialOS
    /// entity spawned from the <c>DataBank_001</c> prefab, seeded with 190602
    /// TransformState (so it has a place in the world) and 8073 ScannableRuinState
    /// (the marker the client's <c>DatabankIslandVisualiser</c> reads to draw and
    /// make it scannable). A player who scans it with the Scanner tool earns
    /// <see cref="GrantAmount"/> knowledge once (2107 ScanEntityEvent -> the scan
    /// handler -> 1332).
    ///
    /// Placement mirrors the deposit's: one anchored bank at a MEASURED near-spawn
    /// LOD0 surface vertex, so a tester walks a few paces and scans. Kept off the
    /// single default deposit's vertex so the two opt-in spawns never interpenetrate.
    ///
    /// CAVEAT (only a live client can close it): whether DataBank_001 draws and is
    /// scannable when spawned as its OWN entity - rather than instantiated by the
    /// island's IslandDataBankAndLootableSpawner (1243), which needs a client writer
    /// we cannot host - is unverified. The scan-GRANT path does not depend on it: the
    /// client sends ScanEntityEvent for whatever it targets and the handler checks the
    /// target against <see cref="DatabankLedger"/>.
    /// </summary>
    public static class Databanks
    {
        /// <summary>
        /// The wire prefab name. VERIFIED: <c>DataBank_001</c> is in
        /// docs/research/loop/data/prefab-names.tsv with both the client and worker
        /// columns "yes". Bare, because the client appends the worker suffix itself
        /// (the same rule as metal_deposit_entity / Tree / MetalNugget); a
        /// "_unityclient" suffix would be doubled and resolve to nothing.
        /// </summary>
        public const string AssetName = "DataBank_001";

        /// <summary>Registration-key prefix for a placed databank. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "databank-";

        /// <summary>
        /// Knowledge a first scan of a databank grants.
        ///
        /// 25 is retail's figure, from the Worlds Adrift wiki's Knowledge page.
        /// It replaces a 10,000 marked "TESTING: big grant so many nodes can be
        /// unlocked from one scan" - which did exactly that: against a tree whose
        /// costs run 1..5000, a single scan bought most of it, and four databanks
        /// on one island handed out 40,000. The loop was provable but meaningless.
        ///
        /// The tree's own costs are left alone: their spread (126 nodes at 1, then
        /// bands at 60/120/150/180/240, up to 5000) is authored data, not something
        /// to rescale around this number. The one exception is "Shipbuilding",
        /// corrected from 20 to the wiki's 50 in the same pass - it was the single
        /// outlier, and 50 is a band the tree already uses.
        ///
        /// So two scans buy Shipbuilding, which is the pacing the wiki describes.
        /// WIKI-SOURCED, not decompiled: no surviving client table carries it.
        /// </summary>
        public const long GrantAmount = 25;

        /// <summary>
        /// The scan NOTE heading the client prints when a databank is scanned. Served as
        /// ScannableData JSON (title/description) so the scan note reads as real text
        /// instead of the old blank (which happened because we sent a raw asset GUID that
        /// <c>ScannableData.Parse</c> could not parse). Flavour, not gameplay - any
        /// non-empty text makes the note render.
        /// </summary>
        public const string NoteTitle = "Ancient Databank";

        /// <summary>The scan note body the client prints under <see cref="NoteTitle"/>.</summary>
        public const string NoteDescription =
            "A cache of pre-Collapse knowledge. Scanning it adds to your understanding of the world.";

        /// <summary>The island every player spawns on; databanks are placed island-local against it.</summary>
        public static readonly FixedPointPosition IslandOrigin = IslandCatalog.Haven.GlobalOrigin;

        /// <summary>One island-local databank placement on Haven (island-local metres).</summary>
        public readonly struct Placement
        {
            public Placement(double localX, double localY, double localZ)
            {
                LocalX = localX;
                LocalY = localY;
                LocalZ = localZ;
            }

            public double LocalX { get; }
            public double LocalY { get; }
            public double LocalZ { get; }
        }

        /// <summary>
        /// The databank placements on Haven, island-local metres.
        ///
        /// Index 0 is a MEASURED near-spawn LOD0 surface vertex (island-local
        /// (192.0, 7.13, 8.0)) - the same measured vertex the deposit table lists at
        /// its index 1, ~16 m from the spawn point at (208.0, 6.70, 4.00) and well
        /// clear of the single default deposit's proven vertex (216.0, 4.57, 8.0), so
        /// the two opt-in spawns cannot interpenetrate. A reachable flat spot, not a
        /// placement study.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = new[]
        {
            new Placement(208.0, 4.99, 8.0), // flat on-surface spawn-zone vertex from the Haven surface table (index 0)
        };

        /// <summary>The registration key for the databank at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>True if a registration key is a databank's.</summary>
        public static bool IsDatabankKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>The world position of the databank at a placement index.</summary>
        public static FixedPointPosition PositionAt(int index)
        {
            Placement p = HavenPlacements[index];
            return IslandCatalog.Haven.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ);
        }

        /// <summary>The number of databanks to place, clamped to [1, full table].</summary>
        public static int CountFrom(string? countEnv) =>
            SpawnCountPolicy.CountFrom(countEnv, HavenPlacements.Count);
    }
}
