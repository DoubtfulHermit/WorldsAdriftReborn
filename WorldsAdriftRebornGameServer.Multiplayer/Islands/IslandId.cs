namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Stable identity for an island, independent of boot-time entity ids and
    /// WAMap placement numbers.
    /// </summary>
    public readonly struct IslandId : IEquatable<IslandId>, IComparable<IslandId>
    {
        public IslandId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("an island id must not be empty", nameof(value));
            }
            Value = value;
        }

        public string Value { get; }

        public bool Equals(IslandId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is IslandId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public int CompareTo(IslandId other) => StringComparer.Ordinal.Compare(Value, other.Value);
        public static bool operator ==(IslandId left, IslandId right) => left.Equals(right);
        public static bool operator !=(IslandId left, IslandId right) => !left.Equals(right);
        public override string ToString() => Value ?? string.Empty;
    }
}
