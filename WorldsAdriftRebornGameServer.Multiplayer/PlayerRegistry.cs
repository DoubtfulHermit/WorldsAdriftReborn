namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Owns the peer-to-entity relationship and nothing else.
    ///
    /// A peer is identified by an opaque ulong (in production, the ENetPeer*
    /// pointer value). No ENet type appears in this API on purpose: it keeps the
    /// registry testable, and it is the seam a server-authoritative design would
    /// later extend.
    /// </summary>
    public sealed class PlayerRegistry
    {
        private readonly Dictionary<ulong, long> _entityByPeer = new();

        /// <summary>Number of players currently registered.</summary>
        public int Count => _entityByPeer.Count;

        /// <summary>
        /// Associates a peer with its player entity. Re-registering an existing
        /// peer overwrites the mapping rather than throwing: a client that
        /// reconnects into the same peer slot must not take the server down.
        /// </summary>
        public void Register(ulong peerId, long entityId)
        {
            _entityByPeer[peerId] = entityId;
        }

        /// <summary>
        /// Removes a peer. Returns the entity it owned so the caller can emit a
        /// RemoveEntityOp, or null if the peer was never registered.
        /// </summary>
        public long? Unregister(ulong peerId)
        {
            if (!_entityByPeer.TryGetValue(peerId, out long entityId))
            {
                return null;
            }

            _entityByPeer.Remove(peerId);
            return entityId;
        }

        /// <summary>The entity owned by this peer, or null if unknown.</summary>
        public long? EntityOf(ulong peerId)
        {
            return _entityByPeer.TryGetValue(peerId, out long entityId) ? entityId : null;
        }

        /// <summary>
        /// Whether this peer owns this entity. The gate for first-time setup,
        /// AUTHORITY grants and any state a client publishes about "its" entity.
        ///
        /// The old check was "is this ANY player entity", which ran the full
        /// setup - authority included - against SOMEONE ELSE'S avatar whenever a
        /// client happened to request the mirrored remote entity's components
        /// first. Request ordering had merely been lucky.
        ///
        /// An unregistered peer owns nothing, including entity 0.
        /// </summary>
        public bool Owns(ulong peerId, long entityId)
        {
            return _entityByPeer.TryGetValue(peerId, out long owned) && owned == entityId;
        }

        /// <summary>
        /// Every registered peer except the one given: the relay target set.
        /// Excludes the origin so a player never receives an echo of their own
        /// update. An unregistered peer still yields all known peers, which is
        /// what a join needs before the joiner is registered.
        /// </summary>
        public IReadOnlyList<ulong> PeersExcept(ulong peerId)
        {
            List<ulong> result = new();
            foreach (ulong peer in _entityByPeer.Keys)
            {
                if (peer != peerId)
                {
                    result.Add(peer);
                }
            }
            return result;
        }

        /// <summary>
        /// Every other player as a (peer, entity) pair, for mirroring existing
        /// players to a newcomer.
        /// </summary>
        public IReadOnlyList<(ulong PeerId, long EntityId)> Others(ulong peerId)
        {
            List<(ulong, long)> result = new();
            foreach (KeyValuePair<ulong, long> entry in _entityByPeer)
            {
                if (entry.Key != peerId)
                {
                    result.Add((entry.Key, entry.Value));
                }
            }
            return result;
        }
    }
}
