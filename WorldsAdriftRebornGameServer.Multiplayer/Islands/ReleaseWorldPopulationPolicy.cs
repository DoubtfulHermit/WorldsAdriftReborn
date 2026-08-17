namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// What a release-world selector actually costs, answered from the embedded
    /// catalogue alone. Terrains includes Haven, which is always registered and is
    /// never a catalogue record.
    /// </summary>
    public readonly record struct ReleaseWorldPopulation(
        int Islands,
        int Terrains,
        int Cells,
        int Deposits,
        int Databanks,
        int AtlasShards,
        int IslandsWithoutMetal,
        int IslandsWithRevivalChambers,
        int IslandsWithTreeSpecies,
        int IslandsWithSurveyedMetal,
        int IslandsWithInferredMetal,
        int InferredDeposits)
    {
        /// <summary>Every world entity this selector adds beyond the Haven baseline.</summary>
        public int ReleaseEntities => Islands + Deposits + Databanks + AtlasShards;
    }

    /// <summary>
    /// Pure accounting for a release-world rollout: how many terrains, deposits,
    /// databanks and atlas shards a selector produces, and how much of the
    /// selection has no surveyed metal at all.
    ///
    /// WHY THIS EXISTS. The counts were previously re-derived by hand in tests, in
    /// the handover and in the boot banner, which is exactly how three numbers
    /// drift apart. More importantly it keeps one uncomfortable fact impossible to
    /// overlook, now stated as
    /// <see cref="ReleaseWorldPopulation.IslandsWithInferredMetal"/>.
    ///
    /// The final Cardinal Guild survey recorded a PvE metal table for only 38 of
    /// the 254 ordinary islands, and a PvP one for 33. It visited all 254 - every
    /// island carries a surveyor name and an exact databank count - so the missing
    /// tables are a coverage gap in the survey, not islands retail shipped barren.
    /// Retail's own island spawner state (component 1010) carries a
    /// `minMetalRockDeposits` floor and a per-island `metalDepositQualities` map,
    /// which is precisely the map the survey was reading off.
    ///
    /// The 193 islands with neither table therefore get a metal table composed
    /// from their tier cohort by tools/world-import/metal_inference.py. That is
    /// INFERENCE, it is not Bossa data, and it is counted separately here for
    /// exactly that reason. <see cref="ReleaseWorldPopulation.IslandsWithoutMetal"/>
    /// is retained and should now be zero for any selector.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class ReleaseWorldPopulationPolicy
    {
        public static ReleaseWorldPopulation For(string? selector) =>
            For(ReleaseWorldRolloutPolicy.Select(selector), atlasShardsEnabled: true,
                oneInDeposits: AtlasSpawnPolicy.DefaultOneInDeposits);

        public static ReleaseWorldPopulation For(
            IReadOnlyList<ReleaseIslandRecord> selection,
            bool atlasShardsEnabled,
            int oneInDeposits)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            int deposits = 0;
            int shards = 0;
            int databanks = 0;
            int withoutMetal = 0;
            int surveyed = 0;
            int inferred = 0;
            int inferredDeposits = 0;
            foreach (ReleaseIslandRecord island in selection)
            {
                deposits += island.Deposits.Count;
                databanks += island.Databanks.Count;
                if (island.Deposits.Count == 0) withoutMetal++;
                if (island.Survey.MetalsAreInferred)
                {
                    inferred++;
                    inferredDeposits += island.Deposits.Count;
                }
                else surveyed++;
                if (atlasShardsEnabled) shards += ShardCountFor(island, oneInDeposits);
            }
            return new ReleaseWorldPopulation(
                Islands: selection.Count,
                // Haven is registered unconditionally and is not a catalogue record.
                Terrains: selection.Count + 1,
                Cells: selection.Select(island => island.CellId)
                    .Distinct(StringComparer.Ordinal).Count(),
                Deposits: deposits,
                Databanks: databanks,
                AtlasShards: shards,
                IslandsWithoutMetal: withoutMetal,
                IslandsWithRevivalChambers: selection.Count(
                    island => island.Survey.HasRevivalChamber),
                IslandsWithTreeSpecies: selection.Count(
                    island => island.Survey.Trees.Count > 0),
                IslandsWithSurveyedMetal: surveyed,
                IslandsWithInferredMetal: inferred,
                InferredDeposits: inferredDeposits);
        }

        /// <summary>
        /// How many of one island's deposits carry an atlas shard. The rate is
        /// applied to that island's OWN deposit index, so every island with any
        /// metal at all reliably has at least one shard to mine whatever the rate
        /// is - the same guarantee <see cref="AtlasSpawnPolicy"/> gives Haven's
        /// index 0, extended per island rather than once for the whole world.
        /// </summary>
        public static int ShardCountFor(ReleaseIslandRecord island, int oneInDeposits)
        {
            if (island == null) throw new ArgumentNullException(nameof(island));
            int count = 0;
            for (int i = 0; i < island.Deposits.Count; i++)
                if (AtlasSpawnPolicy.DepositCarriesShard(i, oneInDeposits)) count++;
            return count;
        }
    }
}
