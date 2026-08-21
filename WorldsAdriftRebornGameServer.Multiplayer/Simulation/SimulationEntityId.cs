using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// Runtime-level identity of one thing the shadow model can talk about.
    ///
    /// Deliberately NOT the same type as a Wareborn boot-time entity id: those are
    /// <c>long</c>s that get recycled across sessions, and three different id spaces
    /// (peer id, entity id, character uid) already exist in this server. The shadow
    /// model needs one stable, deterministically formatted key it can sort and print,
    /// so the adapter is the only place that knows how a hull id becomes "ship:893".
    ///
    /// This type is part of the engine-agnostic core: it knows nothing about ships,
    /// islands, ENet or Unity, and SimulationCorePurityTests keeps it that way.
    /// </summary>
    public readonly struct SimulationEntityId
        : IEquatable<SimulationEntityId>, IComparable<SimulationEntityId>
    {
        public SimulationEntityId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("entity id is required", nameof(value));
            Value = value.Trim();
        }

        public string Value { get; }

        public bool Equals(SimulationEntityId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SimulationEntityId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? "");
        public int CompareTo(SimulationEntityId other) => StringComparer.Ordinal.Compare(Value, other.Value);
        public static bool operator ==(SimulationEntityId left, SimulationEntityId right) => left.Equals(right);
        public static bool operator !=(SimulationEntityId left, SimulationEntityId right) => !left.Equals(right);
        public override string ToString() => Value ?? "";
    }
}
