namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>The stable authority-shaped owner of a registered world entity.</summary>
    public enum WorldOwnerKind
    {
        Global,
        Region,
        Ship,
    }

    /// <summary>
    /// Read-only ownership identity. This is topology, not runtime authority:
    /// it deliberately contains no worker id, lease or authority generation.
    /// </summary>
    public readonly struct WorldOwner : IEquatable<WorldOwner>
    {
        private WorldOwner(WorldOwnerKind kind, string id)
        {
            Kind = kind;
            Id = id;
        }

        public WorldOwnerKind Kind { get; }
        public string Id { get; }

        public static WorldOwner Global => new(WorldOwnerKind.Global, "global");

        public static WorldOwner ForRegion(RegionId regionId) =>
            new(WorldOwnerKind.Region, RequireId(regionId.Value, "region"));

        public static WorldOwner ForShip(string shipRootKey) =>
            new(WorldOwnerKind.Ship, RequireId(shipRootKey, "ship root"));

        public bool Equals(WorldOwner other) =>
            Kind == other.Kind && string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is WorldOwner other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, StringComparer.Ordinal.GetHashCode(Id ?? ""));
        public static bool operator ==(WorldOwner left, WorldOwner right) => left.Equals(right);
        public static bool operator !=(WorldOwner left, WorldOwner right) => !left.Equals(right);
        public override string ToString() => Kind.ToString().ToLowerInvariant() + ":" + Id;

        private static string RequireId(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(label + " identity must not be empty", nameof(value));
            return value;
        }
    }
}
