namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>A surveyed metal type and quality; this is not a deposit count.</summary>
    public sealed class SurveyedMetal
    {
        public SurveyedMetal(string name, int quality)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("a surveyed metal name must not be empty", nameof(name));
            if (quality < 1 || quality > 10)
                throw new ArgumentOutOfRangeException(nameof(quality));

            Name = name;
            Quality = quality;
        }

        public string Name { get; }
        public int Quality { get; }
    }

    /// <summary>
    /// Final Cardinal survey facts joined to one release-MapFile island. Dynamic
    /// resource positions are deliberately absent: they are generated separately
    /// from the preserved collision surface and retail density rules.
    /// </summary>
    public sealed class IslandSurveyProfile
    {
        public IslandSurveyProfile(
            IslandId islandId,
            string workshopId,
            int tier,
            string culture,
            string district,
            int databankCount,
            bool hasRevivalChamber,
            bool dangerous,
            bool hasTurrets,
            IEnumerable<string> trees,
            IEnumerable<SurveyedMetal>? pveMetals = null,
            IEnumerable<SurveyedMetal>? pvpMetals = null)
        {
            if (string.IsNullOrWhiteSpace(islandId.Value))
                throw new ArgumentException("a survey profile must name an island", nameof(islandId));
            if (string.IsNullOrWhiteSpace(workshopId))
                throw new ArgumentException("a workshop id must not be empty", nameof(workshopId));
            if (tier < 1 || tier > 4)
                throw new ArgumentOutOfRangeException(nameof(tier));
            if (string.IsNullOrWhiteSpace(culture))
                throw new ArgumentException("a culture must not be empty", nameof(culture));
            if (string.IsNullOrWhiteSpace(district))
                throw new ArgumentException("a district must not be empty", nameof(district));
            if (databankCount < 0)
                throw new ArgumentOutOfRangeException(nameof(databankCount));

            IslandId = islandId;
            WorkshopId = workshopId;
            Tier = tier;
            Culture = culture;
            District = district;
            DatabankCount = databankCount;
            HasRevivalChamber = hasRevivalChamber;
            Dangerous = dangerous;
            HasTurrets = hasTurrets;
            Trees = Array.AsReadOnly((trees ?? throw new ArgumentNullException(nameof(trees))).ToArray());
            PveMetals = Array.AsReadOnly((pveMetals ?? Array.Empty<SurveyedMetal>()).ToArray());
            PvpMetals = Array.AsReadOnly((pvpMetals ?? Array.Empty<SurveyedMetal>()).ToArray());
        }

        public IslandId IslandId { get; }
        public string WorkshopId { get; }
        public int Tier { get; }
        public string Culture { get; }
        public string District { get; }
        public int DatabankCount { get; }
        public bool HasRevivalChamber { get; }
        public bool Dangerous { get; }
        public bool HasTurrets { get; }
        public IReadOnlyList<string> Trees { get; }
        public IReadOnlyList<SurveyedMetal> PveMetals { get; }
        public IReadOnlyList<SurveyedMetal> PvpMetals { get; }
    }
}
