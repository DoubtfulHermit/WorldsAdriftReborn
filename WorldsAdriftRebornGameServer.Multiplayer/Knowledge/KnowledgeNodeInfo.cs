using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// The server's trimmed view of ONE knowledge-tree node, as
    /// <see cref="KnowledgeSpendPolicy"/> acts on it. Pure data - no game types, no
    /// IO - so the spend rules are unit-tested natively rather than in front of a
    /// running client.
    ///
    /// The node NAME (the dictionary key that owns this value) IS the schematic id
    /// the client's baked prefab uses; see docs/research/loop/data/knowledge-tree.json.
    /// Costs live on the CLIENT (KnowledgeNode.knowledgeCost, baked into the prefab):
    /// the client only sends UseNode after it has already checked affordability, so
    /// the server keeps its own copy of the same numbers purely to re-check
    /// authoritatively. 1307 GlobalKnowledgeGraphDataState is NOT needed for this -
    /// it is only a cost/lifetime OVERRIDE string, and we ship no override.
    /// </summary>
    public sealed class KnowledgeNodeInfo
    {
        public KnowledgeNodeInfo(
            string id,
            int knowledgeCost,
            IReadOnlyList<string> parents,
            string nodeType,
            int maxUses,
            bool isRoot)
        {
            Id = id;
            KnowledgeCost = knowledgeCost;
            Parents = parents ?? new List<string>();
            NodeType = nodeType ?? "";
            MaxUses = maxUses;
            IsRoot = isRoot;
        }

        /// <summary>The node id, which is also the schematic id for SCHEMATIC_* nodes.</summary>
        public string Id { get; }

        /// <summary>Knowledge points this node costs to purchase once.</summary>
        public int KnowledgeCost { get; }

        /// <summary>
        /// The node ids that must be purchased first. A <c>null</c> entry (the tree
        /// JSON carries these) is a root sentinel and is ignored by the prerequisite
        /// check.
        /// </summary>
        public IReadOnlyList<string> Parents { get; }

        /// <summary>
        /// SCHEMATIC_LIST / SCHEMATIC_FIXED / SCHEMATIC_RANDOM / SLOT / CIPHERSLOT /
        /// TECHNOLOGY. Only SCHEMATIC_* nodes learn a craftable schematic; SLOT and
        /// CIPHERSLOT raise caps and are out of scope for the first loop.
        /// </summary>
        public string NodeType { get; }

        /// <summary>
        /// How many times the node may be purchased. -1 means unlimited (the tree's
        /// convention). A one-shot schematic unlock has already been "used" once it
        /// appears in the uses map with a count at or above this.
        /// </summary>
        public int MaxUses { get; }

        /// <summary>A root node has no purchasable parent gate of its own.</summary>
        public bool IsRoot { get; }

        /// <summary>True for the three SCHEMATIC_* node types that learn a recipe.</summary>
        public bool LearnsSchematic =>
            NodeType == "SCHEMATIC_LIST"
            || NodeType == "SCHEMATIC_FIXED"
            || NodeType == "SCHEMATIC_RANDOM";
    }
}
