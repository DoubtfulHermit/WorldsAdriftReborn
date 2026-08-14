using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    /// <summary>Stable in-process identity of one indivisible simulation authority unit.</summary>
    public readonly struct SimulationDomainId : IEquatable<SimulationDomainId>, IComparable<SimulationDomainId>
    {
        public SimulationDomainId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("domain id is required", nameof(value));
            Value = value.Trim();
        }

        public string Value { get; }

        public static SimulationDomainId ForShip(long hullEntityId)
        {
            if (hullEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(hullEntityId));
            return new SimulationDomainId("ship:" + hullEntityId);
        }

        public bool Equals(SimulationDomainId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is SimulationDomainId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? "");
        public int CompareTo(SimulationDomainId other) => StringComparer.Ordinal.Compare(Value, other.Value);
        public static bool operator ==(SimulationDomainId left, SimulationDomainId right) => left.Equals(right);
        public static bool operator !=(SimulationDomainId left, SimulationDomainId right) => !left.Equals(right);
        public override string ToString() => Value;
    }
}
