using System;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Restores the retail LODGING of an atlas shard on a client that has no
    /// UnityWorker.
    ///
    /// <c>MetalDepositAtlasVisualiser_client.OnVisualiserInit</c> is, in full:
    ///
    ///     _view = GetComponentInChildren&lt;MetalDepositAtlasView&gt;();
    ///     _view.ReloadModel();
    ///
    /// It shows the crystal and stops. Everything that made the crystal sit IN the
    /// rock - resolving the host core, indexing its authored <c>ScrapSlots</c> by the
    /// 1305 <c>slotId</c>, aligning to that slot's transform, putting the object on
    /// <c>Layers.Interactive</c> so the interaction raycast can see it, and letting go
    /// when the core explodes - lives in <c>MetalDepositAtlasVisualiser_fsim</c>, which
    /// is <c>[WorkerType(WorkerPlatform.UnityWorker)]</c> and therefore absent from the
    /// client build entirely (<c>MetalDepositAtlasPreprocessor.ExportProcess</c>).
    ///
    /// WAReborn is a server + stock clients; there is no worker to run it. So this
    /// postfix bolts <see cref="AtlasShardLodgeAligner"/> - a direct port of that
    /// worker behaviour - onto the shard as it initialises. Without it the shard is a
    /// loose crystal hanging wherever the server's 190602 put it, which is exactly the
    /// "just a shard on the floor" the players saw.
    ///
    /// This is a client-side VISUAL/INTERACTION repair only. Whether the shard may be
    /// taken is still entirely the server's call (1210 available), and the aligner
    /// never sends anything.
    /// </summary>
    [HarmonyPatch(typeof(MetalDepositAtlasVisualiser_client), "OnVisualiserInit")]
    internal static class AtlasShardLodging_Patch
    {
        [HarmonyPostfix]
        public static void OnVisualiserInit_Postfix(MetalDepositAtlasVisualiser_client __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null)
                {
                    return;
                }

                // Already aligned (a re-init after a checkout bounce): the aligner
                // reattaches itself, so never stack a second one.
                if (__instance.gameObject.GetComponent<AtlasShardLodgeAligner>() != null)
                {
                    return;
                }

                // The visualiser's own 1305 reader - the SAME source the retail fsim used
                // for rockCoreId/slotId. Private, so read it reflectively rather than
                // re-deriving the host from anything else.
                object state = AccessTools
                    .Field(typeof(MetalDepositAtlasVisualiser_client), "_state")
                    ?.GetValue(__instance);

                if (!(state is Bossa.Travellers.Materials.MetalDepositAtlasShardStateReader reader))
                {
                    Debug.LogWarning("[WAR][atlas] could not read MetalDepositAtlasShardState off "
                        + "the shard visualiser; leaving the shard unlodged.");
                    return;
                }

                long shardEntityId = SafeEntityId(__instance);
                long rockCoreId = reader.RockCoreId.Id;
                int slotId = reader.SlotId;

                Debug.Log("[WAR][atlas] shard entity " + shardEntityId
                    + " init: rockCoreId=" + rockCoreId + " slotId=" + slotId + ".");

                MetalDepositAtlasView view =
                    __instance.gameObject.GetComponentInChildren<MetalDepositAtlasView>();

                AtlasShardLodgeAligner aligner =
                    __instance.gameObject.AddComponent<AtlasShardLodgeAligner>();
                aligner.Begin(view == null ? null : view.transform, shardEntityId, rockCoreId, slotId);
            }
            catch (Exception ex)
            {
                // Never throw out of a postfix: the worst acceptable outcome is the old
                // behaviour (a loose shard), not a broken visualiser chain.
                Debug.LogWarning("[WAR][atlas] lodging postfix failed: " + ex);
            }
        }

        private static long SafeEntityId(MonoBehaviour behaviour)
        {
            try
            {
                return behaviour.gameObject.EntityId().Id;
            }
            catch (Exception)
            {
                return 0L;
            }
        }
    }
}
