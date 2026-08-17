namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Evidenced axis-aligned terrain extent in island-local metres. Unlike an
    /// origin-distance approximation this treats a player beside a wide island as
    /// beside its geometry, not hundreds of metres from an arbitrary pivot.
    /// </summary>
    public readonly record struct IslandTerrainEnvelope(
        IslandId IslandId,
        double MinX, double MinY, double MinZ,
        double MaxX, double MaxY, double MaxZ)
    {
        public double DistanceSquaredTo(
            FixedPointPosition worldPosition,
            IslandDefinition island)
        {
            if (island == null) throw new ArgumentNullException(nameof(island));
            if (island.Id != IslandId)
                throw new ArgumentException("terrain envelope and island identity differ", nameof(island));

            double x = worldPosition.MetresX - island.GlobalOrigin.MetresX;
            double y = worldPosition.MetresY - island.GlobalOrigin.MetresY;
            double z = worldPosition.MetresZ - island.GlobalOrigin.MetresZ;
            double dx = AxisDistance(x, MinX, MaxX);
            double dy = AxisDistance(y, MinY, MaxY);
            double dz = AxisDistance(z, MinZ, MaxZ);
            return dx * dx + dy * dy + dz * dz;
        }

        public bool Contains(FixedPointPosition worldPosition, IslandDefinition island) =>
            DistanceSquaredTo(worldPosition, island) == 0.0;

        private static double AxisDistance(double value, double minimum, double maximum) =>
            value < minimum ? minimum - value : value > maximum ? value - maximum : 0.0;
    }

    /// <summary>
    /// Local AABBs extracted from the TRS-composed LOD0 collision surfaces in
    /// docs/research/world-data/island-surfaces. There is deliberately no guessed
    /// fallback: optional terrain without surface evidence is not stream-managed.
    /// </summary>
    public static class IslandTerrainEnvelopes
    {
        private static readonly IReadOnlyDictionary<IslandId, IslandTerrainEnvelope> Known =
            new Dictionary<IslandId, IslandTerrainEnvelope>
            {
                [IslandCatalog.HavenId] = new(IslandCatalog.HavenId,
                    -303.0, -86.0, -122.4, 256.5, 98.0, 169.1),
                [IslandCatalog.TradesChallengeId] = new(IslandCatalog.TradesChallengeId,
                    -201.5, -98.7, -201.5, 201.5, 5.4, 201.6),
                [IslandCatalog.MentalFacilityId] = new(IslandCatalog.MentalFacilityId,
                    -176.7, -92.4, -115.7, 176.8, 48.4, 104.9),
                [IslandCatalog.BetrayalCopperKingId] = new(IslandCatalog.BetrayalCopperKingId,
                    -296.6, -184.9, -130.8, 143.8, 30.9, 268.6),
                [IslandCatalog.HighlandsHillsId] = new(IslandCatalog.HighlandsHillsId,
                    -268.2, -120.3, -222.5, 319.8, 98.7, 362.6),
                [IslandCatalog.LandManForgotId] = new(IslandCatalog.LandManForgotId,
                    -320.3, -267.3, -313.0, 315.0, 210.3, 324.2),
                [IslandCatalog.DrunkRavenInnId] = new(IslandCatalog.DrunkRavenInnId,
                    -36.2, -240.2, -32.9, 72.5, 0.7, 69.1),
                [IslandCatalog.BeautifulWildlandsId] = new(IslandCatalog.BeautifulWildlandsId,
                    -82.4, -150.9, -101.9, 76.7, 92.0, 81.1),
                [IslandCatalog.TheThreeId] = new(IslandCatalog.TheThreeId,
                    -260.1, -84.4, -300.4, 75.1, 47.1, 86.3),
                [IslandCatalog.RoxboroughIsleId] = new(IslandCatalog.RoxboroughIsleId,
                    -276.3, -92.9, -200.7, 268.1, 35.7, 200.7),
                [IslandCatalog.CampsDauratsId] = new(IslandCatalog.CampsDauratsId,
                    -248.2, -85.5, -258.8, 257.6, 198.9, 347.2),
                [IslandCatalog.TriphalionCityId] = new(IslandCatalog.TriphalionCityId,
                    -284.1, -173.3, -295.9, 295.6, 133.1, 315.2),
                [IslandCatalog.SplitpeakPassId] = new(IslandCatalog.SplitpeakPassId,
                    -272.9, -82.5, -193.8, 302.0, 259.0, 193.7),
                [IslandCatalog.CrimsonParadiseId] = new(IslandCatalog.CrimsonParadiseId,
                    -314.8, -313.9, -312.9, 316.8, 109.7, 314.5),
            };

        public static IslandTerrainEnvelope? ByIsland(IslandId islandId) =>
            Known.TryGetValue(islandId, out IslandTerrainEnvelope envelope) ? envelope
                : ReleaseWorldCatalog.ByIsland(islandId)?.Envelope;

        public static IslandTerrainEnvelope Require(IslandId islandId) =>
            ByIsland(islandId) ?? throw new KeyNotFoundException(
                "no extracted terrain envelope is registered for island '" + islandId + "'");
    }
}
