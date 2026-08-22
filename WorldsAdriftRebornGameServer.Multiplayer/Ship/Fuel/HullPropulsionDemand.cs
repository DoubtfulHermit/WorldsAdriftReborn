namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>
    /// The one authoritative answer to "how much combustion propulsion is this
    /// hull commanding right now?" Fuel consumes this value; it never reconstructs
    /// a second control state from player packets.
    ///
    /// <see cref="Throttle"/> is the physical hull lever held by the flight session,
    /// including its deliberately latched value after a clean dismount. An abandoned
    /// helm is neutralised by flight before this value is read. <see cref="EngineCount"/>
    /// is the current mounted combustion-engine count. Sails and lift are absent on
    /// purpose: neither consumes fuel.
    /// </summary>
    public readonly struct HullPropulsionDemand : System.IEquatable<HullPropulsionDemand>
    {
        public HullPropulsionDemand(double throttle, int engineCount)
        {
            Throttle = double.IsFinite(throttle)
                ? System.Math.Clamp(throttle, -1.0, 1.0)
                : 0.0;
            EngineCount = engineCount < 0 ? 0 : engineCount;
        }

        public double Throttle { get; }
        public int EngineCount { get; }
        public bool IsPowered => EngineCount > 0 && Throttle != 0.0;
        public static HullPropulsionDemand None => default;

        public bool Equals(HullPropulsionDemand other) =>
            Throttle.Equals(other.Throttle) && EngineCount == other.EngineCount;
        public override bool Equals(object? obj) => obj is HullPropulsionDemand other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Throttle, EngineCount);
        public override string ToString() => Throttle.ToString("0.###",
            System.Globalization.CultureInfo.InvariantCulture) + " throttle x " + EngineCount + " engine(s)";
    }
}
