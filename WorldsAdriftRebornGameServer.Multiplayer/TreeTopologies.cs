namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// EVERY SHIPPED TREE PREFAB'S SKELETON - the section graph that decides which
    /// sections come away when one is cut, recovered per species rather than
    /// assumed to be `Tree`'s.
    ///
    /// WHY THIS HAD TO EXIST BEFORE A SECOND SPECIES COULD BE PLANTED. The mask is
    /// the protocol (see <see cref="TreeTopology"/>): the server computes which
    /// bits fall and the client renders whatever it is handed. So cutting a palm
    /// with birch's arithmetic is not an error anybody would see in a log - it is a
    /// tree that visibly comes apart wrongly, sections vanishing that should have
    /// stayed and staying that should have gone. The skeletons genuinely differ:
    /// section counts across the 65 prefabs run 9, 10, 11, 12, 13 and 14, and the
    /// branch shapes differ too.
    ///
    /// PROVENANCE, and why this is a recovery rather than a decode that happened to
    /// terminate. <c>docs/research/loop/data/tree_topology.py</c> hand-parses the
    /// serialized MonoBehaviour layout (resources.assets ships no MonoBehaviour
    /// typetrees) and was run over the whole client asset set - 309,415 objects,
    /// 130 TreeBase instances, which is the 65 prefabs twice because each ships a
    /// <c>_unityclient</c> and a <c>_unityworker</c> copy. Five checks all pass at
    /// 130/130:
    ///
    ///   * the three fixed <c>[ExposedMethod]</c> strings decode as "Auto Fill
    ///     Sections" / "Deparent All" / "Debug Initialize Tree" at the offsets the
    ///     layout predicts;
    ///   * every parse ends exactly on the object boundary (0 trailing bytes);
    ///   * every section PPtr resolves to a MonoBehaviour whose script is
    ///     <c>TreeSection</c>, with <c>id</c> equal to its index;
    ///   * every branch root and section index is inside 0..sectionCount-1, and no
    ///     section is a child of two branches;
    ///   * and the <c>_unityclient</c> and <c>_unityworker</c> copies - separately
    ///     serialized objects that the parser never compares while reading - agree
    ///     on the topology for 65/65 prefabs.
    ///
    /// `Tree`'s entry here reproduces <see cref="Trees.Branches"/> and
    /// <see cref="Trees.Harvestable"/> exactly, which is the regression lock tying
    /// this table to the hand-recovered constants that predate it.
    ///
    /// TWO AUTHORING QUIRKS ARE PRESERVED, not corrected, because the client reads
    /// the same authored data and any "fix" here would be a disagreement:
    ///
    ///   * <c>TreePalmStubby</c> has 13 sections but an authored <c>sectionMask</c>
    ///     of 16383 (a 14-bit mask). The authored mask is not used by either side:
    ///     the client derives it as <c>2^treeSections.Length - 1</c>
    ///     (<c>TreeBase.SetTreeDefaults</c>, <c>TreeFsimVisualizer.OnEnable</c>) and
    ///     so does <see cref="TreeTopology.FullMask"/>. The stale value is ignored
    ///     by construction.
    ///   * <c>TreePalmStubby02</c> has 14 sections but its branches only cover
    ///     0..12; section 13 is in no branch, and section 1 is non-harvestable where
    ///     every other prefab's only unfellable section is the stump at 0. Both are
    ///     carried through verbatim - <see cref="TreeTopology"/> handles an orphan
    ///     section and a mid-branch non-harvestable already.
    ///
    /// Pure data: no game types, no I/O. Instances are shared between trees of the
    /// same species, which is safe because <see cref="TreeTopology"/> holds no
    /// mutable state - a tree's changing mask lives in <see cref="TreeHarvest"/>.
    /// </summary>
    public static class TreeTopologies
    {
        private static readonly Dictionary<string, TreeTopology> ByPrefab =
            new Dictionary<string, TreeTopology>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Shorthand for one authored branch, to keep the table scannable.</summary>
        private static TreeBranch B(int root, params int[] sections) => new TreeBranch(root, sections);

        /// <summary>
        /// One prefab's skeleton. <paramref name="harvestable"/> is one character
        /// per section, '1' harvestable and '0' not, so the stump is visible as the
        /// leading '0' at a glance and a wrong-length row throws at startup rather
        /// than silently mis-indexing.
        /// </summary>
        private static void Add(string prefab, int sectionCount, string harvestable, params TreeBranch[] branches)
        {
            if (harvestable.Length != sectionCount)
            {
                throw new ArgumentException(
                    "harvestable mask for " + prefab + " is " + harvestable.Length
                    + " characters but the prefab has " + sectionCount + " sections", nameof(harvestable));
            }

            bool[] flags = new bool[sectionCount];
            for (int i = 0; i < sectionCount; i++)
            {
                flags[i] = harvestable[i] == '1';
            }

            ByPrefab.Add(prefab, new TreeTopology(sectionCount, branches, flags));
        }

        static TreeTopologies()
        {

            // ---- ash ----
            Add("TreeStraightBlue", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));
            Add("TreeStraightPink", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));
            Add("TreeWonky1Leaf1", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky1Leaf2", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky1Leaf7", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("treewonky2Leaf1", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky2Leaf2", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky2Leaf7", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky3Leaf1", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky3Leaf2", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky3Leaf7", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky4Leaf1", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));
            Add("TreeWonky4Leaf2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));
            Add("TreeWonky4Leaf7", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- birch ----
            Add("Tree", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));
            Add("TreeOrange", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));
            Add("TreeStraightDarkGreen", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));

            // ---- cedar ----
            Add("TreeWonky1LongLeaf2", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky1LongLeaf7", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky2LongLeaf2", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky2LongLeaf7", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky3LongLeaf2", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky3LongLeaf7", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky4LongLeaf2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));
            Add("TreeWonky4LongLeaf7", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- chestnut ----
            Add("TreeStraightRed", 12, "011111111111",
                B(0, 1, 2, 3, 4, 6, 8, 9, 10), B(4, 5), B(6, 7), B(9, 11));
            Add("TreeWonky1Leaf4", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky1Leaf5", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky2Leaf4", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky2Leaf5", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky3Leaf4", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky3Leaf5", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky4Leaf4", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));
            Add("TreeWonky4Leaf5", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- elm ----
            Add("TreeWonky1Leaf3", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky2Leaf3", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky3Leaf3", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky4Leaf3", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- hemlock ----
            Add("TreeDessert2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));
            Add("TreeDessert3", 10, "0111111111",
                B(0, 1, 2, 5, 6, 7), B(2, 3, 4), B(6, 8), B(6, 9));
            Add("TreeDessertLeaf1", 10, "0111111111",
                B(0, 1, 2, 5, 6, 7), B(2, 3, 4), B(6, 8), B(6, 9));
            Add("TreeDessertLeaf2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- oak ----
            Add("TreeWonky1Leaf6", 11, "01111111111",
                B(0, 1, 2, 3, 5, 6, 10), B(3, 4), B(6, 7), B(6, 8, 9));
            Add("TreeWonky2Leaf6", 10, "0111111111",
                B(0, 1, 2, 5, 6, 8), B(2, 3, 4), B(6, 9), B(6, 7));
            Add("TreeWonky3Leaf6", 11, "01111111111",
                B(0, 1, 2, 3, 5, 7, 8), B(3, 4), B(5, 6), B(7, 9), B(7, 10));
            Add("TreeWonky4Leaf6", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6), B(4, 7), B(4, 8));

            // ---- palm ----
            Add("TreePalm1", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalm2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalm3", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalm4", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalm5", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBlue1", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBlue2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBlue3", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBlue4", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBlue5", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmBranchesGreen", 14, "01111111111111",
                B(0, 1, 2, 4, 5, 7, 9, 12, 13), B(2, 3), B(5, 6), B(7, 8), B(9, 10), B(9, 11));
            Add("TreePalmBranchesPink", 14, "01111111111111",
                B(0, 1, 2, 4, 5, 7, 9, 12, 13), B(2, 3), B(5, 6), B(7, 8), B(9, 10), B(9, 11));
            Add("TreePalmShortLeaves1", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmShortLeaves2", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmShortLeaves3", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmShortLeaves4", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmShortLeaves5", 9, "011111111",
                B(0, 1, 2, 3, 4, 5, 6, 7, 8));
            Add("TreePalmStubby", 13, "0111111111111",
                B(0, 1, 2, 3, 4), B(0, 5), B(0, 6), B(0, 7), B(0, 8), B(0, 9), B(0, 10), B(0, 11), B(0, 12));
            Add("TreePalmStubby02", 14, "00111111111111",
                B(0, 1, 2, 3, 4), B(0, 5), B(0, 6), B(0, 7), B(0, 8), B(0, 9), B(0, 10), B(0, 11), B(0, 12));        }

        /// <summary>How many prefabs have a recovered skeleton. 65.</summary>
        public static int Count => ByPrefab.Count;

        /// <summary>Every bare prefab name with a recovered skeleton.</summary>
        public static IEnumerable<string> Prefabs => ByPrefab.Keys;

        /// <summary>Whether this prefab's own skeleton is known.</summary>
        public static bool Has(string? assetName) => For(assetName) != null;

        /// <summary>
        /// A prefab's own skeleton, or null if it is not a known tree.
        ///
        /// Null rather than falling back to `Tree` here on purpose: the fallback is
        /// a decision with a visible consequence, so it belongs at the call site
        /// where it can be logged, not hidden in a lookup. Tolerates a worker
        /// suffix for the same reason <see cref="TreeSpecies.WoodFor"/> does.
        /// </summary>
        public static TreeTopology? For(string? assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return null;
            }

            return ByPrefab.TryGetValue(TreeSpecies.StripWorkerSuffix(assetName), out TreeTopology? topology)
                ? topology
                : null;
        }
    }
}
