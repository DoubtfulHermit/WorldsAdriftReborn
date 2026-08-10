using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Bossa.Travellers.Refdata;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Items;
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
            }

            SendOPHelper.SendComponentUpdateOp(player, entityId, new List<uint> { ComponentId }, new List<object> { serverComponentUpdate });
        }
    }
}
