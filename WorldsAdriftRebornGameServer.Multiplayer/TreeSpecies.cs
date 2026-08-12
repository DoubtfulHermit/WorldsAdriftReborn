namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHICH WOOD EACH TREE PREFAB DROPS - Bossa's authored species table,
    /// recovered rather than invented.
    ///
    /// Retail's rule (worldsadrift.fandom.com/wiki/Trees, /wiki/Wood) is that each
    /// tree type has its own look and yields a DIFFERENT wood, and the eight woods
    /// differ in weight and strength - which is why the item rows this maps onto
    /// carry exactly that flavour ("Extremely light and soft, cannot withstand much
    /// damage" for cedar, "The heaviest but also most useful wood" for palm). All
    /// eight are real rows in <c>itemData.json</c>, so every value here resolves to
    /// an item the client's database can look up. That check matters: the client
    /// falls back to <c>placeholder_icon</c> on an unknown icon
    /// (<c>InventoryIconManager.GetIconTexture</c>) but an unknown itemTypeId is a
    /// harder failure, so no wood is named here that the catalogue lacks.
    ///
    /// PROVENANCE. <c>TreePreprocessor.woodType</c> is copied onto
    /// <c>TreeFsimVisualizer.woodType</c> at export, so the species survives on
    /// every shipped <c>_unityworker</c> prefab. All 65 were parsed
    /// (<c>docs/research/loop/data/tree_woodtypes.json</c>) and 65/65 landed on one
    /// of the eight known woods - no prefab needed a guess. The keys here are those
    /// 65 names with the worker suffix stripped, because the name that goes on the
    /// wire is BARE: the client appends the worker suffix itself in
    /// <c>WorkerSpecificPrefabName.GetWorkerSpecificPrefabName</c>. See
    /// <see cref="Trees.AssetName"/>.
    ///
    /// <c>treewonky2Leaf1</c> is lower-cased in the shipped data where its
    /// thirteen siblings are not. That is Bossa's typo, preserved rather than
    /// tidied, and it is why the lookup is case-INSENSITIVE - the entity prefab
    /// container is all-lowercase anyway
    /// (<c>entityprefabs/treewonky2leaf1_unityclient</c>).
    ///
    /// WHAT THIS DOES NOT GIVE YOU: a species' TOPOLOGY. The section count,
    /// branches and per-section harvestable flags in <see cref="Trees"/> were
    /// recovered for `Tree` alone; a palm has a different skeleton, and cutting it
    /// with `Tree`'s arithmetic would produce a mask the client disagrees with -
    /// which renders as a tree falling apart wrongly rather than as an error (see
    /// <see cref="TreeTopology"/>). So placing a non-`Tree` species needs that
    /// prefab's TreeBase parsed first (<c>tree_topology.py &lt;prefab&gt;</c>).
    /// Until then this table is the yield half only, and the world places `Tree`.
    ///
    /// Pure: a dictionary and string handling. No game types, no I/O.
    /// </summary>
    public static class TreeSpecies
    {
        /// <summary>
        /// The eight woods, and the only values this table may map onto. Each is a
        /// real <c>itemTypeID</c> in <c>itemData.json</c> with category "Wood".
        /// </summary>
        public static readonly IReadOnlyList<string> Woods = new[]
        {
            "ash", "birch", "cedar", "chestnut", "elm", "hemlock", "oak", "palm",
        };

        /// <summary>
        /// Bare prefab name -> authored wood species, for all 65 shipped tree
        /// prefabs. Case-insensitive; see the class remarks for why.
        /// </summary>
        private static readonly Dictionary<string, string> ByPrefab =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // ash
            { "TreeStraightBlue", "ash" },
            { "TreeStraightPink", "ash" },
            { "TreeWonky1Leaf1", "ash" },
            { "TreeWonky1Leaf2", "ash" },
            { "TreeWonky1Leaf7", "ash" },
            { "treewonky2Leaf1", "ash" },
            { "TreeWonky2Leaf2", "ash" },
            { "TreeWonky2Leaf7", "ash" },
            { "TreeWonky3Leaf1", "ash" },
            { "TreeWonky3Leaf2", "ash" },
            { "TreeWonky3Leaf7", "ash" },
            { "TreeWonky4Leaf1", "ash" },
            { "TreeWonky4Leaf2", "ash" },
            { "TreeWonky4Leaf7", "ash" },

            // birch
            { "Tree", "birch" },
            { "TreeOrange", "birch" },
            { "TreeStraightDarkGreen", "birch" },

            // cedar
            { "TreeWonky1LongLeaf2", "cedar" },
            { "TreeWonky1LongLeaf7", "cedar" },
            { "TreeWonky2LongLeaf2", "cedar" },
            { "TreeWonky2LongLeaf7", "cedar" },
            { "TreeWonky3LongLeaf2", "cedar" },
            { "TreeWonky3LongLeaf7", "cedar" },
            { "TreeWonky4LongLeaf2", "cedar" },
            { "TreeWonky4LongLeaf7", "cedar" },

            // chestnut
            { "TreeStraightRed", "chestnut" },
            { "TreeWonky1Leaf4", "chestnut" },
            { "TreeWonky1Leaf5", "chestnut" },
            { "TreeWonky2Leaf4", "chestnut" },
            { "TreeWonky2Leaf5", "chestnut" },
            { "TreeWonky3Leaf4", "chestnut" },
            { "TreeWonky3Leaf5", "chestnut" },
            { "TreeWonky4Leaf4", "chestnut" },
            { "TreeWonky4Leaf5", "chestnut" },

            // elm
            { "TreeWonky1Leaf3", "elm" },
            { "TreeWonky2Leaf3", "elm" },
            { "TreeWonky3Leaf3", "elm" },
            { "TreeWonky4Leaf3", "elm" },

            // hemlock
            { "TreeDessert2", "hemlock" },
            { "TreeDessert3", "hemlock" },
            { "TreeDessertLeaf1", "hemlock" },
            { "TreeDessertLeaf2", "hemlock" },

            // oak
            { "TreeWonky1Leaf6", "oak" },
            { "TreeWonky2Leaf6", "oak" },
            { "TreeWonky3Leaf6", "oak" },
            { "TreeWonky4Leaf6", "oak" },

            // palm
            { "TreePalm1", "palm" },
            { "TreePalm2", "palm" },
            { "TreePalm3", "palm" },
            { "TreePalm4", "palm" },
            { "TreePalm5", "palm" },
            { "TreePalmBlue1", "palm" },
            { "TreePalmBlue2", "palm" },
            { "TreePalmBlue3", "palm" },
            { "TreePalmBlue4", "palm" },
            { "TreePalmBlue5", "palm" },
            { "TreePalmBranchesGreen", "palm" },
            { "TreePalmBranchesPink", "palm" },
            { "TreePalmShortLeaves1", "palm" },
            { "TreePalmShortLeaves2", "palm" },
            { "TreePalmShortLeaves3", "palm" },
            { "TreePalmShortLeaves4", "palm" },
            { "TreePalmShortLeaves5", "palm" },
            { "TreePalmStubby", "palm" },
            { "TreePalmStubby02", "palm" },
        };

        /// <summary>How many tree prefabs have a recovered species. 65.</summary>
        public static int Count => ByPrefab.Count;

        /// <summary>Every bare prefab name with a recovered species.</summary>
        public static IEnumerable<string> Prefabs => ByPrefab.Keys;

        /// <summary>
        /// Whether an asset name is one of the shipped tree prefabs. This is what
        /// decides that a spawned entity should be PLANTED as harvestable, and it
        /// replaces the old equality test against <see cref="Trees.AssetName"/> -
        /// which was correct only while exactly one species existed.
        ///
        /// Tolerates a worker suffix so a caller that has the full
        /// <c>Tree_unityclient</c> form still gets an answer.
        /// </summary>
        public static bool IsTree(string? assetName) => WoodFor(assetName) != null;

        /// <summary>
        /// The wood a tree prefab drops, or null if the name is not a known tree.
        ///
        /// Null rather than a birch default on purpose: a silent default is how a
        /// newly placed species would quietly pay out the wrong wood forever. The
        /// caller decides what to do about an unknown, and logs it.
        /// </summary>
        public static string? WoodFor(string? assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return null;
            }

            return ByPrefab.TryGetValue(StripWorkerSuffix(assetName), out string? wood) ? wood : null;
        }

        /// <summary>
        /// Drops a <c>_unityclient</c> / <c>_unityworker</c> suffix if present. The
        /// wire name is bare, but the recovered table was built from worker prefabs
        /// and a caller may hold either form.
        /// </summary>
        public static string StripWorkerSuffix(string assetName)
        {
            const string worker = "_unityworker";
            const string client = "_unityclient";

            if (assetName.EndsWith(worker, StringComparison.OrdinalIgnoreCase))
            {
                return assetName.Substring(0, assetName.Length - worker.Length);
            }
            if (assetName.EndsWith(client, StringComparison.OrdinalIgnoreCase))
            {
                return assetName.Substring(0, assetName.Length - client.Length);
            }

            return assetName;
        }
    }
}
