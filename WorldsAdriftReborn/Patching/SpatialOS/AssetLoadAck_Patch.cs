using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Improbable.Worker;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.SpatialOS
{
    /// <summary>
    /// The asset-loaded ACK ledger: every AssetLoadRequestOp the server sends is
    /// answered EXACTLY ONCE, even when the synchronous prefab rescue beats the
    /// game's own async reply.
    ///
    /// THE NORMAL FLOW (retail, decompile Improbable.Unity.Core.DispatchEventHandler):
    /// OnAssetLoad(op) -> templateProvider.PrepareTemplate(name, onSuccess) ->
    /// OnAssetTemplatePrepared(op) -> SpatialOS.Connection.SendAssetLoadedResponse(
    /// op.AssetType, op.Name, op.Context). That response is the ONLY packet the
    /// server's spawn chain accepts to advance past a RequestAsset step - so a
    /// request whose async load never completes (the loader coroutine's host can
    /// die in the loading-screen handover) is a reply that never comes, and the
    /// server used to wait for it forever.
    ///
    /// WHAT THIS ADDS. OnAssetLoad records the request here (a count per prefab
    /// name: the server legitimately requests Deck01 eight times in a row for a
    /// restored ship). When the rescue in
    /// <see cref="WorkerSpecificAssetDatabaseTemplateProvider_Patch"/> loads a
    /// prefab synchronously, it calls <see cref="TryAckNow"/>: if a request for
    /// that prefab is still unanswered, the SAME SendAssetLoadedResponse call the
    /// normal path makes is made immediately - with the request's own
    /// AssetType/Context - and the pending count drops. When the game's own
    /// prepared-callback later fires for a request the rescue already answered,
    /// the prefix on OnAssetTemplatePrepared skips the duplicate send. Net: one
    /// request, one reply, always - just sometimes earlier.
    ///
    /// Everything here runs on Unity's main thread (dispatch callbacks and
    /// coroutines both), so plain collections are safe.
    /// </summary>
    internal static class AssetLoadAck
    {
        private sealed class PendingRequest
        {
            public string AssetType;
            public string Context;
        }

        private static readonly Dictionary<string, Queue<PendingRequest>> Pending =
            new Dictionary<string, Queue<PendingRequest>>();

        /// <summary>An AssetLoadRequestOp arrived; remember that it owes one reply.</summary>
        public static void RecordRequested(string name, string assetType, string context)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            Queue<PendingRequest> queue;
            if (!Pending.TryGetValue(name, out queue))
                Pending[name] = queue = new Queue<PendingRequest>();
            queue.Enqueue(new PendingRequest { AssetType = assetType, Context = context });
        }

        /// <summary>
        /// Whether the game's own prepared-callback should still send its reply
        /// for this prefab. True consumes one pending request (the original send
        /// follows); false means the rescue already answered it and the duplicate
        /// send must be skipped.
        /// </summary>
        public static bool ConsumeForOriginalAck(string name)
        {
            return ConsumePending(name);
        }

        /// <summary>
        /// Called by the synchronous rescue: if this prefab still owes the server
        /// a reply, send it NOW - the identical SendAssetLoadedResponse the
        /// unpatched flow sends - so the spawn chain advances instead of waiting
        /// on an async callback that may never run. Returns whether a reply went
        /// out.
        /// </summary>
        public static bool TryAckNow(string name)
        {
            Queue<PendingRequest> queue;
            if (string.IsNullOrEmpty(name) || !Pending.TryGetValue(name, out queue)
                || queue.Count == 0)
            {
                return false;
            }

            PendingRequest entry = queue.Peek();
            string assetType = entry.AssetType;
            string context = entry.Context;
            ConsumePending(name);

            try
            {
                Improbable.Unity.Core.SpatialOS.Connection.SendAssetLoadedResponse(assetType, name, context);
                Debug.Log("[WAReborn] sent the asset-loaded reply for '" + name
                    + "' from the synchronous rescue so the server's spawn chain advances.");
                return true;
            }
            catch (System.Exception e)
            {
                // Fail open: a reply we could not send leaves exactly the pre-fix
                // behaviour (the server's ack timeout now owns that case).
                Debug.LogWarning("[WAReborn] failed to send the rescue asset-loaded reply for '"
                    + name + "': " + e.Message);
                return false;
            }
        }

        private static bool ConsumePending(string name)
        {
            Queue<PendingRequest> queue;
            if (string.IsNullOrEmpty(name) || !Pending.TryGetValue(name, out queue)
                || queue.Count == 0)
            {
                return false;
            }

            queue.Dequeue();
            if (queue.Count == 0)
                Pending.Remove(name);
            return true;
        }
    }

    /// <summary>Records every incoming asset-load request in the ledger.</summary>
    [HarmonyPatch]
    internal class DispatchEventHandler_OnAssetLoad_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName("Improbable.Unity.Core.DispatchEventHandler"),
                "OnAssetLoad");
        }

        [HarmonyPrefix]
        public static void OnAssetLoad_Prefix( AssetLoadRequestOp assetLoad )
        {
            AssetLoadAck.RecordRequested(assetLoad.Name, assetLoad.AssetType, assetLoad.Context);
        }
    }

    /// <summary>
    /// Skips the game's own asset-loaded reply when the rescue already sent it,
    /// so the server never receives two replies for one request (a stale second
    /// reply would advance a LATER RequestAsset step early and reintroduce the
    /// AddEntity-races-the-load bug system-wide).
    /// </summary>
    [HarmonyPatch]
    internal class DispatchEventHandler_OnAssetTemplatePrepared_Patch
    {
        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName("Improbable.Unity.Core.DispatchEventHandler"),
                "OnAssetTemplatePrepared");
        }

        [HarmonyPrefix]
        public static bool OnAssetTemplatePrepared_Prefix(
            object __instance, AssetLoadRequestOp assetLoad )
        {
            // The callback proves the bundle is now cached. Shell construction
            // must happen here (never in OnAssetLoad) so it cannot race the
            // asynchronous island bundle loader.
            DistantIslandShells.TemplatePrepared(__instance, assetLoad);
            if (AssetLoadAck.ConsumeForOriginalAck(assetLoad.Name))
            {
                return true; // still owed - let the original send the reply.
            }

            Debug.Log("[WAReborn] skipping the duplicate asset-loaded reply for '"
                + assetLoad.Name + "' (the synchronous rescue already answered it).");
            return false;
        }
    }
}
