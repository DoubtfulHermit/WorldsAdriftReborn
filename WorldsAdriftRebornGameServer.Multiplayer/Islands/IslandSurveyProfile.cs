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
    /// Where an island's EFFECTIVE metal table came from. Only
    /// <see cref="SurveyPve"/> is a reading of what retail's PvE shard actually
    /// had; the other two are weaker and are labelled so nothing downstream can
    /// quietly promote them to evidence.
    /// </summary>
    public enum MetalTableSource
    {
        /// <summary>The island's own recorded PvE table. 38 of 254 islands.</summary>
        SurveyPve,

        /// <summary>
        /// No PvE table, but the same physical island WAS read on the PvP shard,
        /// so this is still an observation of that island one ruleset removed.
        /// 23 of 254 islands.
        /// </summary>
        SurveyPvp,

        /// <summary>
        /// Neither table was ever recorded. Composed by
        /// <c>tools/world-import/metal_inference.py</c> from the tier cohort that
        /// WAS surveyed. NOT Bossa data. 193 of 254 islands.
        /// </summary>
        InferredTier,
    }

    /// <summary>
    /// Final Cardinal survey facts joined to one release-MapFile island. Dynamic
    /// resource positions are deliberately absent: they are generated separately
    /// from the preserved collision surface and the catalogue's density rule.
    ///
    /// <see cref="PveMetals"/> and <see cref="PvpMetals"/> are the survey verbatim,
    /// empty lists included. <see cref="Metals"/> is the table the island's
    /// deposits were actually stamped from and <see cref="MetalSource"/> says
    /// which of the two it is, or that it is neither. Keeping all three means the
    /// inference never overwrites the evidence it was derived from.
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
            IEnumerable<SurveyedMetal>? pvpMetals = null,
            IEnumerable<SurveyedMetal>? metals = null,
            MetalTableSource metalSource = MetalTableSource.SurveyPve)
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
            // Defaulting Metals to the PvE survey keeps every hand-built profile in
            // the tests and the Haven catalogue meaning exactly what it meant before
            // the effective table became a separate concept.
            Metals = metals == null
                ? PveMetals
                : Array.AsReadOnly(metals.ToArray());
            MetalSource = metalSource;
            if (!Enum.IsDefined(typeof(MetalTableSource), metalSource))
                throw new ArgumentOutOfRangeException(nameof(metalSource));
            // A source claim that contradicts the evidence it names is worse than no
            // claim: it would let an inferred island read as surveyed.
            if (metalSource == MetalTableSource.SurveyPve && Metals.Count > 0 && PveMetals.Count == 0)
                throw new ArgumentException(
                    "an island claiming a surveyed PvE metal table has no PvE survey", nameof(metalSource));
            if (metalSource == MetalTableSource.SurveyPvp && PvpMetals.Count == 0)
                throw new ArgumentException(
                    "an island claiming a surveyed PvP metal table has no PvP survey", nameof(metalSource));
            if (metalSource == MetalTableSource.InferredTier
                && (PveMetals.Count > 0 || PvpMetals.Count > 0))
                throw new ArgumentException(
                    "an island with a surveyed metal table must not be marked inferred", nameof(metalSource));
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

        /// <summary>The table this island's deposits were stamped from.</summary>
        public IReadOnlyList<SurveyedMetal> Metals { get; }

        /// <summary>Where <see cref="Metals"/> came from. Never inferred silently.</summary>
        public MetalTableSource MetalSource { get; }

        /// <summary>
        /// True when this island's metals were composed from its tier cohort rather
        /// than observed. The one predicate anything reporting on the world should
        /// use before presenting an island's metals as fact.
        /// </summary>
        public bool MetalsAreInferred => MetalSource == MetalTableSource.InferredTier;
    }
}
