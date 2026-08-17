namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// HOW MANY TREES AN ISLAND GETS - and an honest label on the one number in
    /// the tree pipeline that is not recovered evidence.
    ///
    /// THE COUNT IS NOT RECOVERABLE, AND THIS SAYS SO. Retail budgeted content by
    /// island surface area through <c>LootablePerAreaDataState</c> (component
    /// 1244), and that component's eighteen fields cover exactly three things:
    /// databanks, loot containers and loot chests. There is no tree field, in that
    /// component or in any other. Nor is there a tree spawner to infer one from:
    /// <c>IslandResourceType</c> declares a <c>Value2Tree</c> member, but
    /// <c>IslandProxyVisualizer.OnSpawnResources</c> has branches for <c>Metal</c>
    /// and <c>Egg</c> only, and its <c>ResourceNames</c> lists just those two. Every
    /// tree in Worlds Adrift came from a GSim-side list built at island-upload time
    /// from <c>IslandProps/Trees</c> editor markers, and that list did not ship -
    /// zero of the 465,571 extracted prop placements is a tree
    /// (docs/research/loop/findings-harvestable-world.md).
    ///
    /// So this is a CALIBRATION, not a reconstruction, and it is anchored to the
    /// only tree population this server has ever run in a live session: Haven's 80
    /// distributed trees over a 90-cell LOD0 surface. Using Haven's own density
    /// means the first island a player visits after Haven feels like Haven, which
    /// is the most defensible property available when the true number is gone.
    ///
    /// WHY IT IS CLAMPED AT BOTH ENDS.
    ///
    /// The floor exists because density alone strands the small islands: a 9-cell
    /// islet earns 8 trees, and an island the survey says is wooded should be worth
    /// landing on for wood. Twelve is roughly one full wood run.
    ///
    /// The ceiling exists because boot-time entity registration is a real cost and
    /// a past bug had a joining second player run the main thread out of memory
    /// instantiating too much world at once (docs/HANDOVER.md). Ungated, the
    /// largest tree island (734 cells) would ask for 652 trees by itself. In
    /// practice the ceiling, not the density, is what sets the world's tree budget:
    /// 49 of the 72 wooded islands clamp to it. That is deliberate - it makes the
    /// total predictable and bounded rather than a function of one huge island.
    ///
    /// The resulting world is 3,767 trees over 72 islands, and it is scoped further
    /// by the same WAREBORN_RELEASE_WORLD_DISTRICTS dial that already gates
    /// deposits and databanks, so enabling only the tier-1 cells yields 727 trees.
    ///
    /// This formula is duplicated in tools/world-import/generate-release-tree-placements.py.
    /// <c>ReleaseTreeCatalogTests</c> asserts that every shipped island's point
    /// count equals what this class says, so the two cannot drift apart silently.
    ///
    /// Pure: arithmetic only. No I/O, no game types.
    /// </summary>
    public static class ReleaseTreeBudget
    {
        /// <summary>
        /// Haven's proven density: 80 distributed trees across a 90-cell surface.
        /// </summary>
        public const double TreesPerCell = 80.0 / 90.0;

        /// <summary>Floor, so a wooded islet still supports a wood run.</summary>
        public const int MinTrees = 12;

        /// <summary>Ceiling, so no single island dominates boot registration.</summary>
        public const int MaxTrees = 60;

        /// <summary>
        /// Trees for an island whose extracted surface has this many LOD0 cells.
        /// Non-positive input yields the floor rather than throwing: a malformed
        /// surface should give a thin island, not a dead server.
        /// </summary>
        public static int CountFor(int lod0Cells)
        {
            if (lod0Cells <= 0)
            {
                return MinTrees;
            }

            int scaled = (int)Math.Floor(lod0Cells * TreesPerCell + 0.5);
            return Math.Max(MinTrees, Math.Min(MaxTrees, scaled));
        }
    }
}
