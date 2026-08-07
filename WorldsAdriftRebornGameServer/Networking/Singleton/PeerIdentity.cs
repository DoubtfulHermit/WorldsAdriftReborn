using WorldsAdriftRebornGameServer.DLLCommunication;

namespace WorldsAdriftRebornGameServer.Networking.Singleton
{
    /// <summary>
    /// Resolves the raw ENetPeer* carried on an inbound packet back to the single
    /// canonical <see cref="ENetPeerHandle"/> created when that client connected.
    ///
    /// This exists because ENetPeerHandle is a SafeHandle and therefore compares
    /// by reference: constructing a new handle around the same pointer would not
    /// match any dictionary key, and would additionally arm a second finalizer
    /// that disconnects the peer when collected. Every lookup must go through
    /// here so exactly one handle per peer is ever alive.
    /// </summary>
    internal sealed class PeerIdentity
    {
        private static PeerIdentity? instance;
        public static PeerIdentity Instance => instance ??= new PeerIdentity();

        private readonly Dictionary<IntPtr, ENetPeerHandle> _byPointer = new();

        private PeerIdentity()
        {
        }

        /// <summary>Opaque, stable id for a peer: its pointer value.</summary>
        public static ulong IdOf(ENetPeerHandle peer)
        {
            return (ulong)peer.DangerousGetHandle().ToInt64();
        }

        /// <summary>
        /// Records the handle for a newly connected peer. If the pointer is
        /// already known (ENet reuses peer slots), the existing handle is kept so
        /// no duplicate ever exists.
        /// </summary>
        public ENetPeerHandle Track(IntPtr rawPeer, ENetPeerHandle handle)
        {
            if (_byPointer.TryGetValue(rawPeer, out ENetPeerHandle? existing))
            {
                return existing;
            }

            _byPointer[rawPeer] = handle;
            return handle;
        }

        /// <summary>
        /// The handle for this pointer, or null if the peer is unknown. Null is a
        /// normal outcome during connect and teardown races and must never be
        /// treated as fatal.
        /// </summary>
        public ENetPeerHandle? Resolve(IntPtr rawPeer)
        {
            if (rawPeer == IntPtr.Zero)
            {
                return null;
            }

            return _byPointer.TryGetValue(rawPeer, out ENetPeerHandle? handle) ? handle : null;
        }

        /// <summary>Drops a peer on disconnect and returns the handle it had.</summary>
        public ENetPeerHandle? Forget(IntPtr rawPeer)
        {
            if (!_byPointer.TryGetValue(rawPeer, out ENetPeerHandle? handle))
            {
                return null;
            }

            _byPointer.Remove(rawPeer);
            return handle;
        }

        public int Count => _byPointer.Count;
    }
}
