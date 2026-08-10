using System.Collections.Generic;
using Bossa.Travellers.Player;
using Bossa.Travellers.Scanning;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Knowledge;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 1334 KnowledgeClientState - "I clicked a knowledge-tree node".
     *
     * The SPEND half of the KNOWLEDGE loop. The client owns the tree (baked into its
     * prefab) and only sends UseNode(id) AFTER it has checked affordability locally,
     * so this handler re-checks everything authoritatively (cost, prerequisites, use
     * cap), deducts the cost from 1332, records the node in 1332 knowledgeNodeUses,
     * and LEARNS the node's schematic by appending it to 1079 learnedSchematics.
     * Client and server agree on cost by construction: both read the same numbers,
     * the client from its baked tree and the server from that tree's export
     * (knowledge-tree.json). That is why 1307 is unnecessary.
     *
     * VERIFIED shapes (Generated.Code.dll):
     *   UseNode { string id }.
     *   1332 KnowledgeServerState.Update: SetKnowledge / SetKnowledgeNodeUses +
     *   AddKnowledgeUseResponse(KnowledgeUseResponse{KnowledgeUseResponseType}).
     *   enum KnowledgeUseResponseType { Success, FullInventory, NotEnoughKnowledge,
     *   NodeLocked, InexistentNode, PastMaxUses }.
     *   1079 SchematicsLearnerClientState.Update: SetLearnedSchematics(List<string>) +
     *   AddSchematicLearnt(SchematicLearnt{title}) -> the "SCHEMATIC LEARNED" card.
     *
     * 1334 is granted authoritative on and injected into the player entity
     * (MirrorSendPolicy), so it is dispatched here.
     *
     * MULTIPLAYER SAFETY: event-driven and per-player. UseNode is a click, not a
     * per-frame stream. 1332/1079 are pushed only to the clicking player's own peer;
     * nothing is relayed to other players.
     */
    [RegisterComponentUpdateHandler]
    internal class KnowledgeClientState_Handler : IComponentUpdateHandler<KnowledgeClientState, KnowledgeClientState.Update, KnowledgeClientState.Data>
    {
        public KnowledgeClientState_Handler() { Init(1334); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            KnowledgeClientState.Update clientComponentUpdate, KnowledgeClientState.Data serverComponentData)
        {
            // Only the sender's OWN entity: 1334 is the player's own knowledge writer.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 1334 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            Improbable.Collections.List<UseNode>? uses = clientComponentUpdate.useNode;
            if (uses == null || uses.Count == 0)
            {
                return;
            }

            PlayerProgression prog = ProgressionStore.For(entityId);

            foreach (UseNode use in uses)
            {
                string nodeId = use.id ?? "";

                NodeSpend spend = KnowledgeSpendPolicy.Evaluate(
                    KnowledgeTree.Nodes, prog.Knowledge, prog.NodeUses, nodeId);

                KnowledgeServerState.Update knowledgeUpdate = new KnowledgeServerState.Update();
                knowledgeUpdate.AddKnowledgeUseResponse(new KnowledgeUseResponse(MapResponse(spend.Response)));

                List<uint> componentIds = new List<uint> { 1332 };
                List<object> updates = new List<object> { knowledgeUpdate };

                if (spend.Ok)
                {
                    prog.Knowledge = spend.NewKnowledge;
                    prog.NodeUses[nodeId] = spend.NewNodeUseCount;

                    knowledgeUpdate.SetKnowledge(prog.Knowledge);
                    knowledgeUpdate.SetKnowledgeNodeUses(ToMap(prog.NodeUses));

                    Console.WriteLine("[info] 1334: entity " + entityId + " purchased node '" + nodeId
                        + "', -" + (spend.Response == NodeSpendResponse.Success ? "cost" : "0")
                        + ", knowledge now " + prog.Knowledge + ".");

                    // LEARN the schematic: append to 1079 learnedSchematics (idempotent)
                    // and fire the "SCHEMATIC LEARNED" card. A node that learns nothing
                    // (SLOT/CIPHERSLOT/TECHNOLOGY) leaves 1079 untouched.
                    if (!string.IsNullOrEmpty(spend.LearnedSchematicId)
                        && !prog.LearnedSchematics.Contains(spend.LearnedSchematicId!))
                    {
                        prog.LearnedSchematics.Add(spend.LearnedSchematicId!);

                        SchematicsLearnerClientState.Update learnUpdate = new SchematicsLearnerClientState.Update();
                        learnUpdate.SetLearnedSchematics(ToList(prog.LearnedSchematics));
                        learnUpdate.AddSchematicLearnt(new SchematicLearnt(spend.LearnedSchematicId!));

                        componentIds.Add(1079);
                        updates.Add(learnUpdate);

                        Console.WriteLine("[info] 1334: entity " + entityId + " learned schematic '"
                            + spend.LearnedSchematicId + "'.");
                    }
                }
                else
                {
                    Console.WriteLine("[info] 1334: entity " + entityId + " could not purchase node '" + nodeId
                        + "': " + spend.Response + ".");
                }

                SendOPHelper.SendComponentUpdateOp(player, entityId, componentIds, updates);
            }
        }

        private static KnowledgeUseResponseType MapResponse(NodeSpendResponse response)
        {
            switch (response)
            {
                case NodeSpendResponse.Success: return KnowledgeUseResponseType.Success;
                case NodeSpendResponse.NotEnoughKnowledge: return KnowledgeUseResponseType.NotEnoughKnowledge;
                case NodeSpendResponse.NodeLocked: return KnowledgeUseResponseType.NodeLocked;
                case NodeSpendResponse.InexistentNode: return KnowledgeUseResponseType.InexistentNode;
                case NodeSpendResponse.PastMaxUses: return KnowledgeUseResponseType.PastMaxUses;
                default: return KnowledgeUseResponseType.InexistentNode;
            }
        }

        private static Improbable.Collections.Map<string, int> ToMap(IReadOnlyDictionary<string, int> source)
        {
            Improbable.Collections.Map<string, int> map = new Improbable.Collections.Map<string, int>();
            foreach (KeyValuePair<string, int> kv in source)
            {
                map.Add(kv.Key, kv.Value);
            }
            return map;
        }

        private static Improbable.Collections.List<string> ToList(IReadOnlyList<string> source)
        {
            Improbable.Collections.List<string> list = new Improbable.Collections.List<string>();
            foreach (string s in source)
            {
                list.Add(s);
            }
            return list;
        }
    }
}
