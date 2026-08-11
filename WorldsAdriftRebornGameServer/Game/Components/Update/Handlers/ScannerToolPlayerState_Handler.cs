using System.Collections.Generic;
using Bossa.Travellers.Items;
using Bossa.Travellers.Scanning;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Knowledge;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 2107 ScannerToolPlayerState - "my Scanner tool just scanned this entity".
     *
     * The GAIN half of the KNOWLEDGE loop, and the exact analogue of the 2106 salvage
     * handler: an EVENT component the player owns. The client's PlayerScannerTool
     * raycasts a scannable and publishes ScanEntityEvent(target, coords, assetGuid) on
     * its OWN authoritative 2107 writer; each event is one discrete scan, so the
     * server only has to decide "is that a databank, and has this player scanned it
     * before" and pay knowledge on the first time.
     *
     * VERIFIED shapes (Generated.Code.dll):
     *   ScanEntityEvent { EntityId entityId; Coordinates scanCoordinates; string assetGuid }.
     *   1332 KnowledgeServerState.Update: SetKnowledge/SetLifetimeKnowledge +
     *   AddKnowledgeGainScanResponse(KnowledgeGainScanResponse{scanData,knowledgeGained})
     *   / AddRepeatedScanResponse(RepeatedScanResponse{scanData}).
     *
     * 2107 is granted authoritative on and injected into the player entity
     * (MirrorSendPolicy), so it is in the player's ComponentMap and dispatched here.
     *
     * MULTIPLAYER SAFETY: event-driven and per-player. The scan is a trigger, not a
     * per-frame stream (the client rate-limits its own tool). 1332 is pushed only to
     * the scanning player's own peer; nothing is relayed to other players.
     */
    [RegisterComponentUpdateHandler]
    internal class ScannerToolPlayerState_Handler : IComponentUpdateHandler<ScannerToolPlayerState, ScannerToolPlayerState.Update, ScannerToolPlayerState.Data>
    {
        public ScannerToolPlayerState_Handler() { Init(2107); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            ScannerToolPlayerState.Update clientComponentUpdate, ScannerToolPlayerState.Data serverComponentData)
        {
            // Only the sender's OWN entity: 2107 rides the player's own scanner, so
            // entityId is the scanner. Without this a modified client could publish a
            // 2107 on someone else's avatar and credit their knowledge.
            ulong peerId = PeerIdentity.IdOf(player);
            if (!WorldsAdriftRebornGameServer.Players.Owns(peerId, entityId))
            {
                Console.WriteLine("[warning] 2107 update for entity " + entityId + " from a peer that owns "
                    + WorldsAdriftRebornGameServer.Players.EntityOf(peerId) + ", ignoring.");
                return;
            }

            // A DELTA: most 2107 packets carry no events (the component's fields are
            // empty), so the scan list is routinely null/empty. A ScanEntityEvent is
            // transient, read straight off the incoming update.
            Improbable.Collections.List<ScanEntityEvent>? scans = clientComponentUpdate.scanEntityEvent;
            if (scans == null || scans.Count == 0)
            {
                return;
            }

            PlayerProgression prog = ProgressionStore.For(entityId);
            bool mutated = false;

            foreach (ScanEntityEvent scan in scans)
            {
                long target = scan.entityId.Id;
                string key = target.ToString();
                string scanData = scan.assetGuid ?? "";

                ScanGrant grant = KnowledgeScanPolicy.Evaluate(
                    targetIsScannableDatabank: DatabankLedger.IsDatabank(target),
                    alreadyScanned: prog.AlreadyScanned.Contains(key),
                    knowledge: prog.Knowledge,
                    lifetimeKnowledge: prog.LifetimeKnowledge,
                    grantAmount: DatabankLedger.GrantFor(target));

                switch (grant.Outcome)
                {
                    case ScanGrantOutcome.NotScannable:
                        // The beam rests on trees, hulls, players and loot too; a scan
                        // naming any of them simply owes no response.
                        Console.WriteLine("[info] 2107: entity " + entityId + " scanned non-databank " + target + ", no knowledge.");
                        break;

                    case ScanGrantOutcome.Repeated:
                    {
                        Console.WriteLine("[info] 2107: entity " + entityId + " re-scanned databank " + target + ", no points.");
                        KnowledgeServerState.Update update = new KnowledgeServerState.Update();
                        update.AddRepeatedScanResponse(new RepeatedScanResponse(scanData));
                        PushKnowledge(player, entityId, update);
                        break;
                    }

                    case ScanGrantOutcome.Granted:
                    {
                        mutated = true;
                        prog.Knowledge = grant.NewKnowledge;
                        prog.LifetimeKnowledge = grant.NewLifetimeKnowledge;
                        prog.AlreadyScanned.Add(key);

                        Console.WriteLine("[info] 2107: entity " + entityId + " scanned databank " + target
                            + " for +" + grant.KnowledgeGained + " knowledge (now " + prog.Knowledge + ").");

                        KnowledgeServerState.Update update = new KnowledgeServerState.Update();
                        update.SetKnowledge(prog.Knowledge);
                        update.SetLifetimeKnowledge(prog.LifetimeKnowledge);
                        update.AddKnowledgeGainScanResponse(new KnowledgeGainScanResponse(scanData, grant.KnowledgeGained));
                        PushKnowledge(player, entityId, update);
                        break;
                    }
                }
            }

            // Write-through: a first-time databank scan granted knowledge and
            // recorded the databank in the dedup ledger, so persist under the
            // character key. Re-scans and non-databank hits change nothing.
            if (mutated)
            {
                Game.Knowledge.ProgressionService.Save(entityId);
            }
        }

        /// <summary>Push a 1332 update to the scanning player's own peer.</summary>
        private static void PushKnowledge(ENetPeerHandle player, long entityId, KnowledgeServerState.Update update)
        {
            SendOPHelper.SendComponentUpdateOp(player, entityId,
                new List<uint> { 1332 }, new List<object> { update });
        }
    }
}
