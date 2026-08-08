using System.Runtime.InteropServices;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.DLLCommunication
{
    /// <summary>
    /// Reads ENet's per-peer health counters (RTT, packet loss, reliable bytes
    /// in flight) directly out of the native ENetPeer struct behind an
    /// <see cref="ENetPeerHandle"/>.
    ///
    /// WHY DIRECT READS. The C++ shim exports no accessor for these fields, and
    /// the shim cannot be changed in this task: the same CoreSdkDll ships to
    /// clients, so any C++ change is a separate, batched decision. The peer
    /// POINTER, however, is already on this side of the boundary - it is the
    /// dictionary key the whole server uses - and ENetPeer is a plain C struct,
    /// so the fields are one Marshal read away at fixed offsets.
    ///
    /// WHERE THE OFFSETS COME FROM. Computed with offsetof() by
    /// x86_64-w64-mingw32-gcc against the VENDORED enet (submodule
    /// WorldsAdriftRebornCoreSdk/enet @ bfbb35e, version 1.3.17) with the same
    /// -DWIN32 define build-mingw.sh uses - i.e. the exact ABI CoreSdkDll.dll
    /// is built with (MSVC x64 lays these plain C structs out identically).
    ///
    /// THE RISK, PLAINLY. If the submodule is ever bumped to an enet whose
    /// ENetPeer layout differs, these reads silently return other fields.
    /// Mitigated, not removed, by EnetPeerHealthPolicy.IsPlausible: state
    /// (0..9 enum) and MTU (protocol-clamped 576..4096) are checked before a
    /// snapshot is trusted, and an implausible read reports failure instead of
    /// numbers. The clean fix is a tiny C++ export
    /// (ENet_EXP_Peer_Health(ENetPeer*, struct out*)) next time the DLL is
    /// rebuilt for other reasons.
    ///
    /// Only ever called from the main loop for CONNECTED peers, whose ENetPeer
    /// storage is owned by the host and stable for the connection's lifetime.
    /// </summary>
    internal static class EnetPeerProbe
    {
        // offsetof(ENetPeer, ...) for enet 1.3.17, x64 Windows ABI. See class
        // remarks for the derivation; do not edit without re-deriving.
        private const int OffState = 56;
        private const int OffPacketsSent = 124;
        private const int OffPacketsLost = 128;
        private const int OffRoundTripTime = 200;
        private const int OffRoundTripTimeVariance = 204;
        private const int OffMtu = 208;
        private const int OffReliableDataInTransit = 216;

        /// <summary>
        /// Snapshot of a live peer's health counters. False when the pointer is
        /// null or the read fails the layout sanity check - callers must treat
        /// that as "unavailable", never as zeros.
        /// </summary>
        public static bool TryRead(IntPtr peer, out EnetPeerHealth health)
        {
            if (peer == IntPtr.Zero)
            {
                health = default;
                return false;
            }

            health = new EnetPeerHealth(
                state: ReadU32(peer, OffState),
                roundTripTimeMs: ReadU32(peer, OffRoundTripTime),
                roundTripTimeVarianceMs: ReadU32(peer, OffRoundTripTimeVariance),
                packetsSent: ReadU32(peer, OffPacketsSent),
                packetsLost: ReadU32(peer, OffPacketsLost),
                reliableDataInTransit: ReadU32(peer, OffReliableDataInTransit),
                mtu: ReadU32(peer, OffMtu));

            return EnetPeerHealthPolicy.IsPlausible(health);
        }

        private static uint ReadU32(IntPtr basePtr, int offset)
        {
            return unchecked((uint)Marshal.ReadInt32(basePtr, offset));
        }
    }
}
