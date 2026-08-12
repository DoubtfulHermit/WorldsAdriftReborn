namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>What the server should send, decided by <see cref="RemotePlayerMirror"/>.</summary>
    public enum MirrorOp
    {
        /// <summary>Spawn a remote player's avatar on the target client.</summary>
        AddEntity,

        /// <summary>
        /// Send a remote player's components. Never carries authority: only a
        /// peer's own entity may be made authoritative, or the client would try
        /// to drive someone else's character.
        /// </summary>
        AddComponents,

        /// <summary>Forward a component update verbatim to another client.</summary>
        RelayComponentUpdate,

        /// <summary>Despawn a departed player's avatar.</summary>
        RemoveEntity,
    }

    /// <summary>
    /// A single "send this op to that peer" instruction. The mirror returns these
    /// instead of sending, so the decision logic stays a pure function of registry
    /// state and can be asserted on directly in tests.
    /// </summary>
    public readonly struct MirrorIntent
    {
        /// <summary>Peer that should receive this op.</summary>
        public ulong TargetPeer { get; }

        public MirrorOp Op { get; }

        /// <summary>Entity the op concerns (the subject, not the recipient).</summary>
        public long EntityId { get; }

        /// <summary>Component id; meaningful only for <see cref="MirrorOp.RelayComponentUpdate"/>.</summary>
        public uint ComponentId { get; }

        /// <summary>
        /// Raw component bytes, forwarded unchanged. The server does not
        /// deserialize: it cannot for most component ids, and re-serializing
        /// would add failure modes for no benefit.
        /// </summary>
        public byte[]? Payload { get; }

        public MirrorIntent(ulong targetPeer, MirrorOp op, long entityId, uint componentId = 0, byte[]? payload = null)
        {
            TargetPeer = targetPeer;
            Op = op;
            EntityId = entityId;
            ComponentId = componentId;
            Payload = payload;
        }

        public override string ToString()
        {
            return $"{Op} entity={EntityId} -> peer={TargetPeer}"
                 + (Op == MirrorOp.RelayComponentUpdate ? $" component={ComponentId}" : string.Empty);
        }
    }
}
