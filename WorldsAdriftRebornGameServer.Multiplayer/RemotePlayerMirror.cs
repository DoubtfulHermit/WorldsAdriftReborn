namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Decides which ops go to which peers so that players can see each other.
    /// Returns intents and sends nothing itself, which keeps every join, relay and
    /// disconnect rule verifiable without a packet or a running game.
    /// </summary>
    public sealed class RemotePlayerMirror
    {
        private readonly PlayerRegistry _registry;

        public RemotePlayerMirror(PlayerRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// A player finished spawning. Registers them, then mirrors in both
        /// directions: existing players to the newcomer, and the newcomer to
        /// existing players.
        ///
        /// The first player to join produces no intents, which is correct: there
        /// is nobody to show them and nobody to show them to.
        /// </summary>
        public IReadOnlyList<MirrorIntent> OnJoin(ulong peerId, long entityId)
        {
            List<MirrorIntent> intents = new();

            // Existing players -> newcomer. Computed before registering so the
            // newcomer is never mirrored to itself.
            foreach ((ulong otherPeer, long otherEntity) in _registry.Others(peerId))
            {
                intents.Add(new MirrorIntent(peerId, MirrorOp.AddEntity, otherEntity));
                intents.Add(new MirrorIntent(peerId, MirrorOp.AddComponents, otherEntity));

                // Newcomer -> this existing player.
                intents.Add(new MirrorIntent(otherPeer, MirrorOp.AddEntity, entityId));
                intents.Add(new MirrorIntent(otherPeer, MirrorOp.AddComponents, entityId));
            }

            _registry.Register(peerId, entityId);
            return intents;
        }

        /// <summary>
        /// A player sent a component update on their own entity. Forwards it
        /// verbatim to everyone else.
        ///
        /// An update from an unregistered peer yields no intents rather than
        /// throwing: packets arriving during join and teardown races are normal,
        /// and one player's bad state must never abort the packet loop.
        /// </summary>
        public IReadOnlyList<MirrorIntent> OnComponentUpdate(ulong peerId, uint componentId, byte[] payload)
        {
            long? entityId = _registry.EntityOf(peerId);
            if (entityId is null)
            {
                return Array.Empty<MirrorIntent>();
            }

            List<MirrorIntent> intents = new();
            foreach (ulong target in _registry.PeersExcept(peerId))
            {
                intents.Add(new MirrorIntent(target, MirrorOp.RelayComponentUpdate, entityId.Value, componentId, payload));
            }
            return intents;
        }

        /// <summary>
        /// A player disconnected. Unregisters them and tells everyone still
        /// connected to despawn their avatar. Without this a departed player
        /// leaves a frozen body standing in the world forever.
        /// </summary>
        public IReadOnlyList<MirrorIntent> OnLeave(ulong peerId)
        {
            long? entityId = _registry.Unregister(peerId);
            if (entityId is null)
            {
                return Array.Empty<MirrorIntent>();
            }

            List<MirrorIntent> intents = new();
            foreach (ulong target in _registry.PeersExcept(peerId))
            {
                intents.Add(new MirrorIntent(target, MirrorOp.RemoveEntity, entityId.Value));
            }
            return intents;
        }
    }
}
