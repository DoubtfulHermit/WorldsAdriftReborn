namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// A snapshot of the live-ness counters ENet keeps per peer, read straight
    /// out of the native ENetPeer struct (the impure read lives in
    /// DLLCommunication.EnetPeerProbe; this type and its policy are the pure,
    /// testable half).
    ///
    /// WHY THESE FIELDS. The working theory for the 73-second silent drop is:
    /// reliable relay traffic outruns a peer's ACKs, fills ENet's window, RTT
    /// blows out, ENet times the peer out. Each stage has a counter:
    /// <see cref="ReliableDataInTransit"/> is the un-ACKed reliable byte count
    /// (the filling window), <see cref="RoundTripTimeMs"/> the blow-out, and
    /// <see cref="PacketsLost"/>/<see cref="PacketsSent"/> the retransmission
    /// pressure. Logged next to the traffic rates, the chain becomes visible
    /// instead of inferred.
    /// </summary>
    public readonly struct EnetPeerHealth
    {
        /// <summary>ENetPeerState; 5 is CONNECTED.</summary>
        public uint State { get; }

        /// <summary>Mean RTT of reliable packets, milliseconds.</summary>
        public uint RoundTripTimeMs { get; }

        /// <summary>RTT variance, milliseconds.</summary>
        public uint RoundTripTimeVarianceMs { get; }

        /// <summary>Reliable packets sent to the peer (lifetime).</summary>
        public uint PacketsSent { get; }

        /// <summary>Reliable packets ENet had to consider lost (lifetime).</summary>
        public uint PacketsLost { get; }

        /// <summary>Bytes of reliable data sent and not yet ACKed - the window fill.</summary>
        public uint ReliableDataInTransit { get; }

        /// <summary>Negotiated MTU; used as a layout sanity check (576..4096).</summary>
        public uint Mtu { get; }

        public EnetPeerHealth(uint state, uint roundTripTimeMs, uint roundTripTimeVarianceMs,
            uint packetsSent, uint packetsLost, uint reliableDataInTransit, uint mtu)
        {
            State = state;
            RoundTripTimeMs = roundTripTimeMs;
            RoundTripTimeVarianceMs = roundTripTimeVarianceMs;
            PacketsSent = packetsSent;
            PacketsLost = packetsLost;
            ReliableDataInTransit = reliableDataInTransit;
            Mtu = mtu;
        }
    }

    /// <summary>
    /// The judgement calls around <see cref="EnetPeerHealth"/>, kept pure so
    /// they are testable: is a snapshot believable, and how does it print.
    /// </summary>
    public static class EnetPeerHealthPolicy
    {
        /// <summary>ENET_PEER_STATE_CONNECTED in enet 1.3.17's ENetPeerState enum.</summary>
        public const uint StateConnected = 5;

        /// <summary>Highest ENetPeerState value (ZOMBIE) in enet 1.3.17.</summary>
        public const uint MaxState = 9;

        /// <summary>ENET_PROTOCOL_MINIMUM_MTU / _MAXIMUM_MTU in enet 1.3.17.</summary>
        public const uint MinMtu = 576;
        public const uint MaxMtu = 4096;

        /// <summary>
        /// Whether a snapshot looks like it was read from a real, current-layout
        /// ENetPeer. The struct offsets are hardcoded against the vendored enet
        /// 1.3.17 built for the x64 Windows ABI; if the DLL is ever rebuilt from
        /// a different enet, reads land on the wrong fields and produce garbage
        /// SILENTLY. Two fields with narrow legal ranges make that loud instead:
        /// state is a 0..9 enum and MTU is clamped by the protocol to 576..4096.
        /// A wrong layout has to hit both windows by chance to slip through.
        /// </summary>
        public static bool IsPlausible(in EnetPeerHealth health)
        {
            return health.State <= MaxState
                && health.Mtu >= MinMtu
                && health.Mtu <= MaxMtu;
        }

        /// <summary>
        /// E.g. <c>rtt 48+/-12ms, lost 3/1290, 1448B in-flight</c>. Appends the
        /// state only when it is not the boring CONNECTED, so the common case
        /// stays short.
        /// </summary>
        public static string Describe(in EnetPeerHealth health)
        {
            string line = "rtt " + health.RoundTripTimeMs + "+/-" + health.RoundTripTimeVarianceMs
                + "ms, lost " + health.PacketsLost + "/" + health.PacketsSent
                + ", " + health.ReliableDataInTransit + "B in-flight";

            return health.State == StateConnected ? line : line + ", state " + health.State;
        }
    }
}
