using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// The server's answer to a node purchase. Mirrors the generated
    /// <c>KnowledgeUseResponseType</c> (Bossa.Travellers.Scanning) but is a pure
    /// local enum so this policy carries no game-assembly dependency; the handler
    /// maps it one-to-one. FullInventory is never produced here (no inventory gate
    /// on a knowledge purchase).
    /// </summary>
    public enum NodeSpendResponse
    {
        Success,
        NotEnoughKnowledge,
        NodeLocked,
        InexistentNode,
        PastMaxUses,
    }

    /// <summary>The pure result of one node purchase; the handler applies it.</summary>
    public readonly struct NodeSpend
    {
        public NodeSpend(
            NodeSpendResponse response,
            int newKnowledge,
            int newNodeUseCount,
            string? learnedSchematicId)
        {
            Response = response;
            NewKnowledge = newKnowledge;
            NewNodeUseCount = newNodeUseCount;
            LearnedSchematicId = learnedSchematicId;
        }

        public NodeSpendResponse Response { get; }

        /// <summary>Knowledge after the deduction (unchanged on any failure).</summary>
        public int NewKnowledge { get; }

        /// <summary>The node's use count after this purchase (unchanged on failure).</summary>
        public int NewNodeUseCount { get; }

        /// <summary>
        /// The schematic id to append to 1079 learnedSchematics, or null when the
        /// node learns no schematic (SLOT/CIPHERSLOT/TECHNOLOGY) or the purchase
        /// failed. Only set on Success.
        /// </summary>
        public string? LearnedSchematicId { get; }

        public bool Ok => Response == NodeSpendResponse.Success;
    }

    /// <summary>
    /// SPEND half of the KNOWLEDGE loop: a player clicks a tree node and, if they can
    /// afford it and its parents are purchased, pays the cost and learns the node's
    /// schematic. Pure - the tree, the wallet and the uses map are passed in as plain
    /// values; the handler writes the result to 1332/1079 and answers on 1332.
    ///
    /// Server-AUTHORITATIVE: the client checks affordability locally before it sends
    /// UseNode, but this re-checks everything (cost, prerequisites, use cap) so a
    /// spoofed or stale client cannot unlock for free. The client and this policy
    /// agree on cost by construction because both read the SAME numbers - the client
    /// from its baked prefab, the server from a copy of that prefab's export
    /// (knowledge-tree.json).
    /// </summary>
    public static class KnowledgeSpendPolicy
    {
        /// <summary>
        /// Node ids whose baked display name differs from the catalogue's schematic
        /// key, so the learned id resolves against the recovered recipe catalogue.
        /// The tree's node names are display-ish ("Head Torch", "Storage Container",
        /// "Atlas Core Enhancer") while the recovered recipe ids are camelCase-ish
        /// ("headTorch", "storageContainer", "skyCoreAtlasEnhancer"); this table
        /// bridges the two. A node with no entry learns under its own id - harmless
        /// when no catalogue recipe matches (a learned id that does not resolve is
        /// silently dropped by the client, not an error), which is the case for the
        /// procedural weapon/slot/cipher nodes and the tree nodes with no recovered
        /// recipe (Compass, Paint Can, Bread, ...). Every mapping below points at a
        /// key that exists in Game/Items/Config/schematicData.json.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> SchematicAliases =
            new Dictionary<string, string>
            {
                // Shipbuilding is the cheapest tree root (cost 20, reachable from a
                // single databank scan) and the tree has no literal "Shipyard" node,
                // so this root grants the Shipyard recipe - the milestone proof path.
                { "Shipbuilding", "shipyard" },

                // Wings/Engines roots are SCHEMATIC_RANDOM procedural branches; only
                // the wing has a recovered concrete recipe (the Bossa procedural wing
                // seed), so the Wings root learns it. Engines has no recovered recipe.
                { "WingsRootSchematic", "proceduralWingDefault" },

                // Explorer branch.
                { "Fuel Gauge", "fuelGauge" },
                { "Hip Lamp", "hipLamp" },
                { "Head Torch", "headTorch" },
                { "Glider", "glider" },
                { "Artificial Horizon", "artificialHorizon" },

                // SkyshipBuilder branch (root "Stairs").
                { "Stairs", "stairs" },
                { "Medium Panel", "mediumPanel" },
                { "Window Panel", "window" },
                { "Large Panel", "largePanel" },
                { "Ship Railing", "railing" },
                { "Railing Corner", "railingCorner" },

                // Tradesman branch (root "Trunk").
                { "Trunk", "trunk" },
                { "Mounted Box", "mountedBox" },
                { "Storage Container", "storageContainer" },
                { "Loom", "loom" },

                // Cooking branch (root "Campfire").
                { "Campfire", "campFire" },
                { "Thuntomite Steak", "thuntomiteSteak" },
                { "Manta Steak", "mantaSteak" },
                { "Stove", "stove" },

                // AtlasEngineer branch (root "Atlas Core Enhancer"). The wiki recovered
                // these as "Sky Core X" recipe ids.
                { "Atlas Core Enhancer", "skyCoreAtlasEnhancer" },
                { "Atlas Core Generator", "skyCoreGenerator" },
                { "Atlas Core Air Filter", "skyCoreAirFilter" },
                { "Atlas Core Coolant System", "skyCoreCoolantSystem" },
                { "Atlas Core Stabiliser", "skyCoreStabiliser" },
                { "Atlas Core Computer", "skyCoreComputer" },
                { "Atlas Core Circuitry Network", "skyCoreCircuitryNetwork" },
                { "Atlas Core Efficiency Module", "skyCoreEfficiencyModule" },
                { "Lifter", "atlasLifter" },
            };

        public static NodeSpend Evaluate(
            IReadOnlyDictionary<string, KnowledgeNodeInfo> tree,
            int knowledge,
            IReadOnlyDictionary<string, int> nodeUses,
            string nodeId)
        {
            if (nodeId == null || !tree.TryGetValue(nodeId, out KnowledgeNodeInfo? node) || node == null)
            {
                return new NodeSpend(NodeSpendResponse.InexistentNode, knowledge, 0, null);
            }

            int currentUses = UsesOf(nodeUses, nodeId);

            // A one-shot schematic node (maxUses -1 = unlimited by the tree's
            // convention, but a SCHEMATIC unlock is meaningful only ONCE) is spent
            // when it already sits in the uses map. Guard both the explicit cap and
            // the learn-once semantics.
            if (node.MaxUses >= 0 && currentUses >= node.MaxUses)
            {
                return new NodeSpend(NodeSpendResponse.PastMaxUses, knowledge, currentUses, null);
            }
            if (node.LearnsSchematic && currentUses >= 1)
            {
                return new NodeSpend(NodeSpendResponse.PastMaxUses, knowledge, currentUses, null);
            }

            if (!PrerequisitesMet(tree, nodeUses, node))
            {
                return new NodeSpend(NodeSpendResponse.NodeLocked, knowledge, currentUses, null);
            }

            if (knowledge < node.KnowledgeCost)
            {
                return new NodeSpend(NodeSpendResponse.NotEnoughKnowledge, knowledge, currentUses, null);
            }

            int remaining = knowledge - node.KnowledgeCost;
            int newUses = currentUses + 1;
            string? learned = node.LearnsSchematic ? SchematicIdFor(node.Id) : null;

            return new NodeSpend(NodeSpendResponse.Success, remaining, newUses, learned);
        }

        /// <summary>
        /// A node is unlocked when EVERY non-null parent has been purchased at least
        /// once. Root nodes and null parent sentinels have no gate. A parent named in
        /// the tree that we do not carry is treated as satisfied rather than a
        /// permanent lock (the export lists a few cross-branch meta-parents).
        /// </summary>
        private static bool PrerequisitesMet(
            IReadOnlyDictionary<string, KnowledgeNodeInfo> tree,
            IReadOnlyDictionary<string, int> nodeUses,
            KnowledgeNodeInfo node)
        {
            if (node.IsRoot)
            {
                return true;
            }

            foreach (string parent in node.Parents)
            {
                if (string.IsNullOrEmpty(parent))
                {
                    continue;
                }
                if (!tree.ContainsKey(parent))
                {
                    continue;
                }
                if (UsesOf(nodeUses, parent) <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The catalogue-resolving schematic id for a node id.</summary>
        public static string SchematicIdFor(string nodeId)
        {
            return SchematicAliases.TryGetValue(nodeId, out string? alias) ? alias : nodeId;
        }

        private static int UsesOf(IReadOnlyDictionary<string, int> nodeUses, string id)
        {
            return nodeUses.TryGetValue(id, out int uses) ? uses : 0;
        }
    }
}
