namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The two opaque VALUES a server-spawned ship hull needs, kept out of the
    /// component serializer so both can be asserted on natively.
    ///
    /// 1. <see cref="MinimumHullDataBase64"/> - the 39 bytes of 1209
    ///    CustomShipHullState.hullData that make a hull exist at all.
    /// 2. <see cref="FsimIdHash"/> and <see cref="MillisecondsSinceEpoch"/> - the
    ///    two numbers in a 1130 SSPPredictedMotionState control point that are
    ///    not simply "where the ship is".
    ///
    /// Neither is a policy decision the serializer should be making inline, and
    /// the first one is the single highest-risk value in the whole ship path.
    /// </summary>
    public static class ShipHull
    {
        /// <summary>
        /// THE HULL. A whole ship's geometry, as a base64 blob, because that is
        /// genuinely what it is: 1209 is one field, <c>byte[] hullData</c>, and
        /// the client rebuilds the mesh and its colliders from it at runtime
        /// (CustomShipFrameVisualizer.cs:50-52 -> MeshGenerator.GenerateShipMesh).
        ///
        /// WHAT IT DECODES TO: <c>ShipPlan.MakeDefault()</c>, i.e. exactly one
        /// cell at (0,0) with the stock <c>ShipSection</c> geometry - half-width
        /// 3 m, no curve. At the client's fixed <c>ShipScale = 2</c> that is a
        /// hull frame roughly 12 m across, 4 m fore-to-aft and 3.4 m tall, with
        /// its deck plane at the entity's own local y = 0.
        ///
        /// WHERE IT CAME FROM, and why it is not retyped by hand:
        /// <c>docs/research/loop/data/make_hulldata.py</c> generates it from a
        /// transcription of the client's own writer, and its output is committed
        /// beside it in <c>hulldata-samples.txt</c>. Regenerate with
        /// <c>python3 make_hulldata.py</c> and compare the <c>one_cell</c> line;
        /// swapping this ship for the 81-byte 3x1 or the 160-byte 3x2 is then a
        /// one-line change here.
        ///
        /// WHY IT IS THE RISKIEST VALUE WE SEND. It has never been fed to
        /// <c>ShipPlan.Load</c>. That method does not fail quietly - it THROWS on
        /// a null or empty array, and a wrong byte anywhere else desynchronises
        /// the reader and throws inside BinaryReader instead. The failure lands
        /// in the CLIENT's log, not ours, and the visible result is a ship entity
        /// that renders nothing. If that is what you see, this constant is the
        /// first suspect, not the seeding path.
        ///
        /// The transcription itself was checked against the reader, not just the
        /// writer: <c>ShipPlan.Load</c> reads int16 cellCount, then per cell
        /// int16 cellNumber, int16 deckNumber, <c>ShipCell.Read</c> = Front
        /// section, a one-byte bool, and the Back section only when that bool is
        /// set. A section is Top[0], Top[1], Bottom[0], Bottom[1] as three sbytes
        /// each, then four curve sbytes: 16 bytes. 2 + 4 + 16 + 1 + 16 = 39.
        /// </summary>
        public const string MinimumHullDataBase64 = "AQAAAAAA6AAAGAAA6AAAGAAAAAAAAAHoAAAYAADoAAAYAAAAAAAA";

        /// <summary>
        /// The length <see cref="MinimumHullDataBase64"/> must decode to. Asserted
        /// in the test suite: a base64 constant is exactly the kind of literal
        /// that survives a bad edit and only fails on someone else's machine.
        /// </summary>
        public const int MinimumHullDataLength = 39;

        /// <summary>
        /// A FRESH copy of the minimum hull, every call.
        ///
        /// Not a cached <c>static readonly byte[]</c>, deliberately: an array is
        /// mutable however readonly the field is, and this one is handed to the
        /// game's serializer once per client. One accidental in-place edit would
        /// corrupt the hull for every player who connected afterwards, and the
        /// symptom would be "the ship stopped working after a while".
        /// </summary>
        public static byte[] MinimumHullData()
        {
            return Convert.FromBase64String(MinimumHullDataBase64);
        }

        /// <summary>
        /// The <c>fsimIdHash</c> stamped on every control point this server
        /// publishes. ASCII "WASH" - a marker, not a hash of anything.
        ///
        /// It only has to satisfy two rules, both read off
        /// SSPDeadReckoningVisualizer.AddControlPoint:
        ///
        /// 1. It must never equal the receiving client's own
        ///    <c>SpatialOS.Configuration.WorkerId.GetHashCode()</c>, or the client
        ///    treats our points as its own echo and DROPS them, silently. WorkerId
        ///    is a fresh GUID per process, so any fixed value is safe.
        /// 2. It must never CHANGE between consecutive points, or the client calls
        ///    <c>IgnoreControlPointsUntil(t + ServerBoundaryRejectionTime)</c> and
        ///    ignores half a second of motion. Hence a constant rather than
        ///    anything derived from the peer, the entity or the clock.
        ///
        /// Zero is not used because zero is also what an unset field decodes to,
        /// which would make "we stamped it" and "nobody stamped it" the same
        /// value on the wire.
        /// </summary>
        public const int FsimIdHash = 0x57415348;

        /// <summary>
        /// The epoch a 1130 control point's <c>timestamp</c> counts milliseconds
        /// from: 2018-03-01T00:00:00Z, the client's own
        /// <c>SynchronisedTime.EpochTime</c>.
        ///
        /// This is why no clock negotiation is needed to publish ship motion: the
        /// client converts with <c>FromMillisecondsSinceEpoch</c> against this
        /// same fixed instant. It is NTP wall-clock, not uptime.
        /// </summary>
        public static readonly DateTime ControlPointEpochUtc =
            new DateTime(2018, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// A UTC instant as a control-point timestamp. Round, not truncate: the
        /// client's own encoder is <c>round(t * 1000)</c>.
        /// </summary>
        public static long MillisecondsSinceEpoch(DateTime utc)
        {
            return (long)Math.Round((utc.ToUniversalTime() - ControlPointEpochUtc).TotalMilliseconds);
        }

        /// <summary>
        /// Now, as a control-point timestamp.
        ///
        /// For a STATIC ship the value barely matters - the seeded point carries
        /// zero velocity, and every extrapolation the client's PathFollower does
        /// from a zero-velocity point lands on the same position, whether it
        /// extrapolates forwards or backwards. It is a real timestamp anyway
        /// because <c>PathFollower.AddControlPoint</c> derives its server-latency
        /// estimate from it, and because the ferry that comes later must use real
        /// ones - having the static case already be honest means the ferry is not
        /// the first code to get this right.
        /// </summary>
        public static long NowMillisecondsSinceEpoch()
        {
            return MillisecondsSinceEpoch(DateTime.UtcNow);
        }
    }
}
