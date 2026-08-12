

namespace WorldsAdriftRebornGameServer.DLLCommunication
{
    /// <summary>
    /// A NON-OWNING reference to an <c>ENetPeer*</c>. The peer belongs to the
    /// ENetHost and is freed by ENet itself; this type exists only so a peer can
    /// be a dictionary key and be passed to the native send helpers.
    ///
    /// <see cref="ReleaseHandle"/> therefore does nothing. It used to call
    /// <c>ENet_Disconnect</c>, which was harmless purely by accident: every
    /// handle was constructed with a throwaway, invalid <see cref="ENetHostHandle"/>,
    /// so the native side bailed out on its NULL-host guard
    /// (WorldsAdriftRebornCoreSdk/enetLayer.cpp:98-101). A <c>SetHostHandle</c>
    /// setter sat next to it waiting to arm the hazard: hand it the real host and
    /// the FINALIZER thread would enter <c>enet_host_service</c> for up to three
    /// seconds (enetLayer.cpp:107-117) concurrently with the main loop's own
    /// <c>enet_host_service</c>. An ENetHost is not thread-safe, and the peer
    /// being finalized is by definition already gone, so there was nothing to
    /// gain and a corrupted host to lose. The setter is gone with the call.
    ///
    /// Disconnecting a peer deliberately is the main loop's job, not the garbage
    /// collector's.
    /// </summary>
    internal class ENetPeerHandle : CptrHandle
    {
        public ENetPeerHandle(IntPtr peer)
        {
            handle = peer;
        }

        protected override bool ReleaseHandle()
        {
            return true;
        }
    }
}
