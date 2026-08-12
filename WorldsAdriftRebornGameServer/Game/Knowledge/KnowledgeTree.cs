using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;

namespace WorldsAdriftRebornGameServer.Game.Knowledge
{
    /// <summary>
    /// The server's copy of the knowledge tree, loaded from
    /// Game/Knowledge/Config/knowledge-tree.json next to the assembly exactly the way
    /// <see cref="Items.SchematicHelper"/> loads schematicData.json: read once,
    /// cached. It exists ONLY so the 1334 spend handler can re-check the cost and
    /// prerequisites the client already checked - the CLIENT owns the authoritative
    /// tree (baked into its prefab), and this file is the export of that same prefab,
    /// so the two agree on cost by construction.
    ///
    /// We deliberately do NOT serve 1307 GlobalKnowledgeGraphDataState: it is only a
    /// cost/lifetime OVERRIDE string layered on the client's baked tree, and shipping
    /// no override leaves the baked costs (which this file mirrors) in force.
    /// </summary>
    public static class KnowledgeTree
    {
        private static readonly string TreePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "Game/Knowledge/Config/knowledge-tree.json");

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private static IReadOnlyDictionary<string, KnowledgeNodeInfo>? _nodes;

        /// <summary>Every node the server can price, keyed by node id (== schematic id).</summary>
        public static IReadOnlyDictionary<string, KnowledgeNodeInfo> Nodes
        {
            get
            {
                EnsureLoaded();
                return _nodes!;
            }
        }

        /// <summary>The node for an id, or null when the tree has never heard of it.</summary>
        public static KnowledgeNodeInfo? Get(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return null;
            }
            return Nodes.TryGetValue(nodeId, out KnowledgeNodeInfo? node) ? node : null;
        }

        private static void EnsureLoaded()
        {
            if (_nodes != null)
            {
                return;
            }

            string raw = System.IO.File.ReadAllText(TreePath);
            TreeDocument? doc = JsonSerializer.Deserialize<TreeDocument>(raw, ReadOptions);

            Dictionary<string, KnowledgeNodeInfo> nodes = new Dictionary<string, KnowledgeNodeInfo>();

            if (doc?.Nodes != null)
            {
                foreach (NodeDto dto in doc.Nodes)
                {
                    if (string.IsNullOrEmpty(dto.Id) || nodes.ContainsKey(dto.Id))
                    {
                        // The export lists a node id more than once (228 entries, 198
                        // unique); first occurrence wins - the cost is the same on each.
                        continue;
                    }

                    List<string> parents = new List<string>();
                    if (dto.Parents != null)
                    {
                        foreach (string? p in dto.Parents)
                        {
                            if (!string.IsNullOrEmpty(p))
                            {
                                parents.Add(p!);
                            }
                        }
                    }

                    nodes[dto.Id] = new KnowledgeNodeInfo(
                        dto.Id,
                        dto.KnowledgeCost,
                        parents,
                        dto.NodeType ?? "",
                        dto.MaxUses,
                        dto.IsRoot);
                }
            }

            _nodes = nodes;

            System.Console.WriteLine("[info] loaded " + _nodes.Count + " knowledge node(s) from knowledge-tree.json");
        }

        private sealed class TreeDocument
        {
            [JsonPropertyName("nodes")]
            public List<NodeDto>? Nodes { get; set; }
        }

        private sealed class NodeDto
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("nodeType")]
            public string? NodeType { get; set; }

            [JsonPropertyName("knowledgeCost")]
            public int KnowledgeCost { get; set; }

            [JsonPropertyName("maxUses")]
            public int MaxUses { get; set; }

            [JsonPropertyName("isRoot")]
            public bool IsRoot { get; set; }

            [JsonPropertyName("parents")]
            public List<string?>? Parents { get; set; }
        }
    }
}
