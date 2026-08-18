namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHICH BIOME AN ISLAND IS IN, as the retail client understood the word.
    ///
    /// RECOVERED, and it matters exactly once. Retail assigned
    /// <c>BiomeType { Biome1..Biome4 }</c> by nearest Voronoi centre over X/Z
    /// (acs/GlobalBiomeDataVisualizer.GetBiomeAt, acs/IslandSurfaceData.cs:171),
    /// backed by component 1253 <c>GlobalBiomeVoronoiCentresState</c>. The twenty
    /// centres are Bossa's own data and live in this repo -
    /// docs/research/world-data/wamap-islands.json under <c>"Biomes"</c>, per its
    /// PROVENANCE.md - and running that lookup against all 254 catalogue islands
    /// agrees with the island's DISTRICT 254 times out of 254. So the biome is a
    /// pure table join on the district and no geometry needs to run here.
    ///
    /// The one place it matters: biome is the value the manta and beetle variant
    /// clients KEY THEIR MESHES ON. <c>MantaRayVariantClient</c> subscribes to
    /// <c>BiomeTypeUpdated</c> (never to the vestigial
    /// <c>MantaRayVariantType</c>) and picks a tail mesh whose settings entry
    /// matches the biome; without a served biome, <c>PickTail</c> never runs,
    /// <c>MyVariantSettings</c> stays null, and every rendered frame throws - the
    /// 383,632-NRE storm docs/research/plan-fauna-liveness.md section 1.5 measures.
    ///
    /// As a POPULATION driver the biome is deliberately NOT used: biome equals
    /// tier for 253 of 254 islands (the sole exception is Holy Ruins, district
    /// A4, catalogue tier 3, Voronoi Type 2), and all 46 tier-1 islands are
    /// Biome1 Saborian - zero discriminating power inside a tier. Island SIZE is
    /// the driver that varies (plan section 2.5).
    /// </summary>
    public static class IslandBiome
    {
        /// <summary>
        /// The biome the client renders when it is told nothing: BiomeType is a
        /// 1-based enum and Biome1 is both its first member and the value the
        /// beetle's variant client hardcodes at Awake. Used for a cell this table
        /// has never heard of, so an unknown district degrades to the default
        /// look instead of an invalid enum on the wire.
        /// </summary>
        public const int DefaultVoronoiType = 1;

        /// <summary>
        /// The Voronoi biome type (1..4) for a survey cell / district id.
        ///
        /// The table IS the recovered data: each row restates the <c>Type</c> of
        /// the Voronoi centre labelled with that district in Bossa's own
        /// wamap-islands.json. The two MapFile centres labelled district "None"
        /// are both Type 4 and cover the catalogue's two "unassigned-t4-*"
        /// cells, which is why the unassigned prefix maps to 4 rather than to
        /// the default.
        /// </summary>
        public static int VoronoiTypeForCell(string? cellId)
        {
            if (string.IsNullOrEmpty(cellId))
            {
                return DefaultVoronoiType;
            }
            if (cellId!.StartsWith("unassigned-t4", StringComparison.Ordinal))
            {
                return 4;
            }
            return cellId switch
            {
                "A2" or "A3" or "B2" or "B3" => 1,
                "A1" or "A4" or "B1" or "B4" => 2,
                "C1" or "C2" or "C3" or "C4" or "C5" or "C6" => 3,
                "D1" or "D2" or "D3" or "E3" => 4,
                _ => DefaultVoronoiType,
            };
        }
    }
}
