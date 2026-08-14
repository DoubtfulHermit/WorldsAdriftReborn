using WorldsAdriftRebornGameServer.DLLCommunication;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>One authoritative cleanup seam for a successful per-peer entity unload.</summary>
    internal static class PeerCheckoutCleanup
    {
        public static void RemoveEntity(ENetPeerHandle peer, long entityId)
        {
            GameState.Instance.ComponentMap.TryGetValue(peer, out var peerSlice);
            IReadOnlyList<ulong> deadRefs = ComponentRefCleanup.TakeRefsForRemovedEntity(peerSlice, entityId);

            // TakeRefs removed the map entry first. If a native destroy unexpectedly
            // faults, no broadcast can target the unloaded peer and a retry cannot
            // double-destroy references already attempted here.
            foreach (ulong refId in deadRefs)
            {
                ClientObjects.Instance.DestroyReference(refId);
            }

            WorldsAdriftRebornGameServer.ServedComponents.ForgetEntity(peer, entityId);
            WorldsAdriftRebornGameServer.SentEntities.ForgetEntity(peer, entityId);
        }
    }
}
