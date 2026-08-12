using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bossa.Travellers.Player;
using Bossa.Travellers.Refdata;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Items;
using WorldsAdriftRebornGameServer.Game.Knowledge;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    [RegisterComponentUpdateHandler]
    internal class ReferenceDataRequestState_Handler : IComponentUpdateHandler<ReferenceDataRequestState, ReferenceDataRequestState.Update, ReferenceDataRequestState.Data>
    {
        public ReferenceDataRequestState_Handler() { Init(6908); }
        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }
        private static byte[] Compress(string input)
        {
            byte[] data = Encoding.ASCII.GetBytes(input);
            using MemoryStream mStream = new();
            using (GZipStream gStream = new(mStream, CompressionMode.Compress, true))
            {
                gStream.Write(data, 0, data.Length);
            }
            return mStream.ToArray();
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId,
            ReferenceDataRequestState.Update clientComponentUpdate, ReferenceDataRequestState.Data serverComponentData )
        {
            clientComponentUpdate.ApplyTo(serverComponentData);
            ReferenceDataRequestState.Update serverComponentUpdate = (ReferenceDataRequestState.Update)serverComponentData.ToUpdate();

            for (int j = 0; j < clientComponentUpdate.requestReferenceData.Count; j++)
            {
                bool doComp = clientComponentUpdate.requestReferenceData[j].compress;
                Console.WriteLine("[info] game requests reference data, compress: " + doComp);

                ReferenceDataState.Update newRefData = (ReferenceDataState.Update)((ReferenceDataState.Data)ClientObjects.Instance.Dereference(GameState.Instance.ComponentMap[player][entityId][1097])).ToUpdate();
                
                var invData = ItemHelper.GetReferenceItems();
                var resDesc = ItemHelper.GetDescriptions(true);
                var scrapDesc = ItemHelper.GetDescriptions();
                var bundleDesc = ItemHelper.BundleDescriptions();
                newRefData.SetInventoryData(invData);
                newRefData.AddInventoryDataSent(new SendInventoryData(invData, doComp ? Compress(invData) : null));
                newRefData.SetResourcesDescriptions(resDesc);
                newRefData.AddResourceDescriptionsSent(new SendResourceDescriptions(resDesc, doComp ? Compress(JsonSerializer.Serialize(resDesc)) : null));
                newRefData.SetScrapItemsDescriptions(scrapDesc);
                newRefData.AddScrapItemDescriptionsSent(new SendScrapItemsDescriptions(scrapDesc, doComp ? Compress(JsonSerializer.Serialize(scrapDesc)) : null));
                newRefData.AddSteamInvBundlesDescriptionsSent(
                    new SendSteamInventoryBundlesDescriptions(bundleDesc, doComp ? Compress(JsonSerializer.Serialize(bundleDesc)) : null));
                // The recipe catalogue now lives in a file (Game/Items/Config/
                // schematicData.json), loaded like itemData.json. It is served
                // verbatim so the client parses the full SchematicData field set,
                // and the compressed path is preserved unchanged.
                var schematicData = SchematicHelper.RawJson;
                newRefData.SetSchematicsData(schematicData);
                newRefData.AddSchematicDataSent(new SendSchematicData(schematicData, doComp ? Compress(schematicData) : null));

                SendOPHelper.SendComponentUpdateOp(player, entityId, new List<uint> { 1097 }, new List<object> { newRefData });

                // CATALOGUE-INIT DETERMINISM (server-only workaround). The BepInEx mod's
                // ReferenceDataFakeLoad fake-marks Schematics loaded and injects a fake
                // one-record 'glider' catalogue BEFORE this real 1097 arrives; the learned
                // library can then resolve the 60 raw 1079 ids against the fake dictionary
                // and drop most of them, and it is NOT rebuilt when the real catalogue
                // replaces the dictionary UNLESS the 1079 learnedSchematics field is touched
                // again (InventoryVisualiser listens only to that field). So immediately
                // after the real catalogue, re-send 1079 with the player's CURRENT complete
                // learnedSchematics to force one more resolve against the now-real 1097 data.
                // Touch learnedSchematics specifically - defaultSchematics has no client
                // callback. This is a compatibility shim to be removed once the client mod
                // stops fake-initialising Schematics (client-mod commit 2e8ca35).
                PlayerProgression prog = ProgressionStore.For(entityId);
                Improbable.Collections.List<string> learned = new Improbable.Collections.List<string>();
                foreach (string s in prog.LearnedSchematics)
                {
                    learned.Add(s);
                }

                SchematicsLearnerClientState.Update learnedRefresh = new SchematicsLearnerClientState.Update();
                learnedRefresh.SetLearnedSchematics(learned);
                SendOPHelper.SendComponentUpdateOp(player, entityId, new List<uint> { 1079 }, new List<object> { learnedRefresh });
            }

            SendOPHelper.SendComponentUpdateOp(player, entityId, new List<uint> { ComponentId }, new List<object> { serverComponentUpdate });
        }
    }
}
