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
        int IslandsWithTreeSpecies)
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
    /// drift apart. More importantly it makes one uncomfortable fact impossible to
    /// overlook: <see cref="ReleaseWorldPopulation.IslandsWithoutMetal"/>. The
    /// final Cardinal Guild survey recorded a PvE metal table for only 38 of the
    /// 254 ordinary islands, so most islands carry databanks and terrain but no
    /// mining loop. That is the evidence, not a registration failure, and an empty
    /// table is deliberately never backfilled with an invented population.
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
            foreach (ReleaseIslandRecord island in selection)
            {
                deposits += island.Deposits.Count;
                databanks += island.Databanks.Count;
                if (island.Deposits.Count == 0) withoutMetal++;
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
                    island => island.Survey.Trees.Count > 0));
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
