using System.Globalization;
using System.Reflection;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// Recovered biome resource profile for The Trades Challenge (1206286558).
    /// Cardinal Guild's preserved row names Aluminium at quality 4, five databanks
    /// and no trees. The deposit count is the retail density rule applied to its 98
    /// LOD0 terrain cells: ceil(98 * 0.05) = 5. Positions are deterministic samples
    /// of the extracted collision surface, not hand-authored scenery coordinates.
    /// </summary>
    public static class TradesChallengeResources
    {
        public const string SurfaceResourceName = "trades-challenge-surface-1206286558.txt";
        public const string DepositKeyPrefix = "deposit-the-trades-challenge-";
        public const string DatabankKeyPrefix = "databank-the-trades-challenge-";
        public const string MetalType = "aluminium";
        public const int MetalQuality = 4;
        public const int DepositCount = 5;
        public const int DatabankCount = 5;

        private static IReadOnlyList<SurfaceSample>? _samples;
        private static IReadOnlyList<GeneratedPlacement>? _deposits;
        private static IReadOnlyList<GeneratedPlacement>? _databanks;

        public static string DepositKeyFor(int index) => DepositKeyPrefix + index;
        public static string DatabankKeyFor(int index) => DatabankKeyPrefix + index;

        public static IReadOnlyList<GeneratedPlacement> DepositLocals()
        {
            if (_deposits == null)
            {
                _deposits = SurfacePlacementGenerator.Generate(
                    Samples,
                    new SurfacePlacementConfig(
                        minUpwardNormal: 0.9,
                        minReachableHeightMetres: -10,
                        maxReachableHeightMetres: 10,
                        minSpacingMetres: 45,
                        targetCount: DepositCount,
                        exclusions: new[] { new PlacementExclusion(-64, -64, 12) }));
            }
            return _deposits;
        }

        public static IReadOnlyList<GeneratedPlacement> DatabankLocals()
        {
            if (_databanks == null)
            {
                List<PlacementExclusion> exclusions = new()
                {
                    new PlacementExclusion(-64, -64, 8),
                };
                foreach (GeneratedPlacement deposit in DepositLocals())
                {
                    exclusions.Add(new PlacementExclusion(deposit.LocalX, deposit.LocalZ, 10));
                }
                _databanks = SurfacePlacementGenerator.Generate(
                    Samples,
                    new SurfacePlacementConfig(
                        minUpwardNormal: 0.94,
                        minReachableHeightMetres: -10,
                        maxReachableHeightMetres: 10,
                        minSpacingMetres: 35,
                        targetCount: DatabankCount,
                        exclusions: exclusions));
            }
            return _databanks;
        }

        public static MetalNode? DepositByKey(string? key)
        {
            if (key == null || !key.StartsWith(DepositKeyPrefix, StringComparison.Ordinal)
                || !int.TryParse(key.Substring(DepositKeyPrefix.Length), out int index)
                || index < 0 || index >= DepositLocals().Count)
            {
                return null;
            }
            GeneratedPlacement p = DepositLocals()[index];
            return new MetalNode(
                DepositKeyFor(index), MetalType, MetalQuality,
                IslandCatalog.TradesChallenge.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ),
                isDeposit: true,
                variantId: MetalDeposits.VariantIdFor(index));
        }

        public static FixedPointPosition DatabankPositionAt(int index)
        {
            GeneratedPlacement p = DatabankLocals()[index];
            return IslandCatalog.TradesChallenge.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ);
        }

        private static IReadOnlyList<SurfaceSample> Samples => _samples ??= LoadSamples();

        private static IReadOnlyList<SurfaceSample> LoadSamples()
        {
            Assembly assembly = typeof(TradesChallengeResources).Assembly;
            string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(
                x => x.EndsWith(SurfaceResourceName, StringComparison.Ordinal));
            if (resourceName == null)
                throw new FileNotFoundException("embedded Trades Challenge surface table is missing");

            List<SurfaceSample> samples = new();
            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using StreamReader reader = new(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 6) continue;
                samples.Add(new SurfaceSample(
                    Parse(fields[0]), Parse(fields[1]), Parse(fields[2]),
                    Parse(fields[3]), Parse(fields[4]), Parse(fields[5])));
            }
            return samples;
        }

        private static double Parse(string value) =>
            double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
