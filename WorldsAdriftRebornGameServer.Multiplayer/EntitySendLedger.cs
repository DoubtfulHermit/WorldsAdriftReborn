using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Records that an AddEntityOp was successfully queued for a peer. This is
    /// separate from component interest: AddEntity can be sent while a peer is still
    /// loading, before any component request exists, and resending it can recreate or
    /// corrupt a live client entity.
    /// </summary>
    public sealed class EntitySendLedger<TPeer> where TPeer : notnull
    {
        private readonly Dictionary<TPeer, HashSet<long>> _sent = new Dictionary<TPeer, HashSet<long>>();

        public bool WasSent(TPeer peer, long entityId)
        {
            return _sent.TryGetValue(peer, out HashSet<long>? entities) && entities.Contains(entityId);
        }

        public void MarkSent(TPeer peer, long entityId)
        {
            if (!_sent.TryGetValue(peer, out HashSet<long>? entities))
            {
                entities = new HashSet<long>();
                _sent.Add(peer, entities);
            }
            entities.Add(entityId);
        }

        public void ForgetPeer(TPeer peer) => _sent.Remove(peer);

        public void ForgetEntity(TPeer peer, long entityId)
        {
            if (_sent.TryGetValue(peer, out HashSet<long>? entities))
            {
                entities.Remove(entityId);
                if (entities.Count == 0) _sent.Remove(peer);
            }
        }
    }
}
