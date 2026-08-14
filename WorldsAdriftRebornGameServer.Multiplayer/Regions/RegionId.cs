namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>
    /// Stable world-region identity, independent of process, boot order and
    /// runtime entity ids.
    /// </summary>
    public readonly struct RegionId : IEquatable<RegionId>, IComparable<RegionId>
    {
        public RegionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("a region id must not be empty", nameof(value));

            Value = value;
        }

        public string Value { get; }

        public bool Equals(RegionId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is RegionId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public int CompareTo(RegionId other) => StringComparer.Ordinal.Compare(Value, other.Value);
        public static bool operator ==(RegionId left, RegionId right) => left.Equals(right);
        public static bool operator !=(RegionId left, RegionId right) => !left.Equals(right);
        public override string ToString() => Value ?? string.Empty;
    }
}
