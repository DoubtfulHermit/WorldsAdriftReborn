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
        /// Node id -> the recipe id(s) that node LEARNS. The tree's node names are
        /// display-ish ("Head Torch", "Atlas Core Enhancer") while the recovered
        /// recipe ids are camelCase-ish ("headTorch", "skyCoreAtlasEnhancer"); this
        /// table bridges the two AND lets ONE node grant SEVERAL recipes, mirroring
        /// the real WA tree where a foundational node (Shipbuilding, the Atlas root)
        /// unlocked a whole schematicList at once. A node with no entry learns under
        /// its own id - harmless when no catalogue recipe matches (SchematicIdFor
        /// falls through to the node id, which is not a catalogue key and is dropped
        /// by the learn guard), which is the case for the procedural weapon / slot /
        /// cipher tiers and the tree nodes with no recovered recipe.
        ///
        /// FAITHFULNESS (post grant-all revert): each node maps to the recipe a
        /// player expects from that node's NAME/branch, and the recipe's own category
        /// routes it to the right UI. Every target below is a real key in
        /// Game/Items/Config/schematicData.json (asserted by
        /// ReferenceDataCrashSafetyTests.Every_alias_target_resolves_in_catalogue) and
        /// every catalogue recipe is reachable from some node OR is a starter
        /// (Every_recipe_is_reachable_from_a_knowledge_node).
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string[]> SchematicAliases =
            new Dictionary<string, string[]>
            {
                // === SHIPBUILDING ROOT: the functional-ship BASELINE ===
                // Cheapest root (cost 20). Learning Shipbuilding grants the parts a
                // player needs to get a FIRST ship flying: the shipyard to build at,
                // the deck to stand on, the helm to steer, a sail for lift. The richer
                // hull structure is earned in the SkyshipBuilder branch, cores in
                // Atlas Engineer, propulsion in Engines/Wings. (There is no dedicated
                // "Sail"/"Helm" node in the sparse tree export, so these foundational
                // parts ride the root the player unlocks first, exactly as WA gated
                // the starter hull low in the tree.)
                { "Shipbuilding", new[] { "shipyard", "deck", "helm", "sail" } },

                // === SKYSHIP BUILDER (root "Stairs"): ship STRUCTURE (Shipyard cat) ===
                { "Stairs", new[] { "stairs" } },
                { "Medium Panel", new[] { "mediumPanel" } },
                { "Large Panel", new[] { "largePanel" } },
                { "Window Panel", new[] { "window" } },
                // The two railing nodes each also carry a BAR PIPE - straight with
                // straight, bent with corner. Bar pipes are the instrument stands (WIKI:
                // "structural items ... used to attract lightning in a Stormwall or to
                // display Instruments"), they are real client prefabs we simply never
                // implemented, and the sparse tree export has no node named for them.
                // Same domain, same material, same Shipyard category - so they ride the
                // nearest-named structural nodes, exactly as the horn and the airspeed
                // indicator already do below.
                { "Ship Railing", new[] { "railing", "barPipe" } },
                { "Railing Corner", new[] { "railingCorner", "barPipeBent" } },
                { "Crows Nest", new[] { "smallPanel" } },
                // Two SkyshipBuilder structural nodes host the two ship fittings the
                // sparse export left with no faithfully-named node of their own (a ship
                // horn, the airspeed instrument). Same domain (ship, Shipyard cat), so
                // they route to the right UI; WHICH structural node hosts them is
                // cosmetic. Kept mapped so no catalogue recipe is dead content.
                { "Paint Drum", new[] { "horn" } },
                { "Paint Can", new[] { "airspeedIndicator" } },

                // === WINGS: procedural wing ===
                { "WingsRootSchematic", new[] { "proceduralWingDefault" } },

                // === ENGINES: procedural engine (Assembly Station) + power ===
                { "EnginesRootSchematic", new[] { "proceduralEngineDefault" } },
                { "EnginesSchematic2", new[] { "powerGenerator" } },
                { "EnginesSchematicBonus1", new[] { "powerGenerator01" } },

                // === ATLAS ENGINEER (root "Atlas Core Enhancer"): sky cores ===
                // The root grants the BASIC Atlas Sky Core (the lift core that makes a
                // ship fly) alongside its namesake enhancer; the branch nodes grant the
                // named variants. This puts the fundamental core under Atlas Engineer,
                // where a player looks for it, instead of on a spare engine tier.
                { "Atlas Core Enhancer", new[] { "atlasSkyCore", "skyCoreAtlasEnhancer" } },
                { "Atlas Core Generator", new[] { "skyCoreGenerator" } },
                { "Atlas Core Air Filter", new[] { "skyCoreAirFilter" } },
                { "Atlas Core Coolant System", new[] { "skyCoreCoolantSystem" } },
                { "Atlas Core Stabiliser", new[] { "skyCoreStabiliser" } },
                { "Atlas Core Computer", new[] { "skyCoreComputer" } },
                { "Atlas Core Circuitry Network", new[] { "skyCoreCircuitryNetwork" } },
                { "Atlas Core Efficiency Module", new[] { "skyCoreEfficiencyModule" } },
                { "Lifter", new[] { "atlasLifter" } },

                // === EXPLORER (root "Makeshift Bandages"): instruments + field kit ===
                { "Makeshift Bandages", new[] { "personalReviver" } },
                { "Nervure Bandages", new[] { "altimeter" } },
                { "Compass", new[] { "headingIndicator" } },
                { "Fuel Gauge", new[] { "fuelGauge" } },
                { "Artificial Horizon", new[] { "artificialHorizon" } },
                { "Head Torch", new[] { "headTorch" } },
                { "Hip Lamp", new[] { "hipLamp" } },
                { "Glider", new[] { "glider" } },

                // === TRADESMAN (root "Trunk"): furniture / storage / cloth ===
                { "Trunk", new[] { "trunk" } },
                { "Mounted Box", new[] { "mountedBox" } },
                { "Storage Container", new[] { "storageContainer" } },
                { "Shipping Container", new[] { "shippingContainer" } },
                { "Loom", new[] { "loom" } },
                { "Metal Chair", new[] { "cupboard" } },
                { "Long Wooden Table", new[] { "barrel" } },
                { "Long Metal Table", new[] { "assemblyStation" } },
                { "Wooden Stool", new[] { "makeshiftStorage" } },
                { "Dye", new[] { "clothMakeshift" } },

                // === COOKING (root "Campfire") ===
                { "Campfire", new[] { "campFire" } },
                { "Stove", new[] { "stove" } },
                { "Thuntomite Steak", new[] { "thuntomiteSteak" } },
                { "Manta Steak", new[] { "mantaSteak" } },
                { "Bread", new[] { "thuntomiteStew" } },
                // moonshine is a Cooking recipe; homed on a spare cooking node so it
                // surfaces in the Cooking tab, NOT on the engine bonus node whose baked
                // export id happens to be "moonshine" (an engine node granting a drink
                // was the clearest cross-domain mis-map).
                { "Manta Burger", new[] { "moonshine" } },

                // === WEAPONS: the roots grant the weapon's projectile; tiers add ammo ===
                { "PistolsRootSchematic", new[] { "pistol" } },
                { "PistolsSchematic2", new[] { "pistolBullets" } },
                { "CannonsRootSchematic", new[] { "cannonball" } },
                { "CannonsSchematic2", new[] { "cannonShell" } },
                { "CannonsSchematicBonus1", new[] { "cannonball" } },
                { "SwivelGunRootSchematic", new[] { "swivelGunShell" } },
                { "SwivelGunSchematicBonus1", new[] { "swivelGunShell" } },

                // === TERRITORY ===
                { "Territory Control Tower", new[] { "territory_control_beacon" } },
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

        /// <summary>
        /// The PRIMARY catalogue-resolving schematic id for a node id (the first of
        /// its learned set), or the node id itself when it has no alias. Used for the
        /// single-recipe "SCHEMATIC LEARNED" card path.
        /// </summary>
        public static string SchematicIdFor(string nodeId)
        {
            return SchematicAliases.TryGetValue(nodeId, out string[]? aliases) && aliases.Length > 0
                ? aliases[0]
                : nodeId;
        }

        /// <summary>
        /// ALL catalogue-resolving schematic ids a node learns. A node may learn
        /// several (a foundational root grants a whole schematicList); a node with no
        /// alias resolves to its own id (dropped by the catalogue guard when it is not
        /// a recipe key). This is the source of truth the purchase-time learn and the
        /// login reconcile both walk.
        /// </summary>
        public static IReadOnlyList<string> SchematicIdsFor(string nodeId)
        {
            return SchematicAliases.TryGetValue(nodeId, out string[]? aliases)
                ? aliases
                : new[] { nodeId };
        }

        private static int UsesOf(IReadOnlyDictionary<string, int> nodeUses, string id)
        {
            return nodeUses.TryGetValue(id, out int uses) ? uses : 0;
        }
    }
}
