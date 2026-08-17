namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHICH deposits carry an atlas shard - a DOCUMENTED, tunable, DETERMINISTIC
    /// reconstruction of the retail spawn rule.
    ///
    /// RECONSTRUCTED, not retail. The real rule (which deposits, at what probability,
    /// keyed on seed/biome/island) lived entirely in the lost retail worker and is
    /// unrecoverable: the client-authored <c>MetalRocksSpawnStateData</c> carries only
    /// a seed and scrap counts, no atlas field, and no on-disk resource table names a
    /// shard (docs/research/findings-atlas-refdata.md #2). So this is a knob, not a
    /// fact - a "one shard per N deposits" rate that defaults to a value that keeps the
    /// acquisition loop testable NOW and can be tuned toward real rarity once (if ever)
    /// a preserved capture surfaces.
    ///
    /// DETERMINISTIC by construction: the selection is <c>depositIndex % N</c>, not a
    /// random roll (Math.random is unavailable on this server and would break the
    /// every-client-walks-the-same-plan invariant - a shard that exists for one client
    /// and not another is an un-collectable ghost). Index 0 (the proven near-spawn
    /// deposit) ALWAYS carries a shard, so a tester always has one to mine regardless of
    /// the rate.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class AtlasSpawnPolicy
    {
        /// <summary>
        /// The default rate: one shard per this many deposits. 1 = EVERY deposit
        /// carries a shard, which - with the default single-deposit session
        /// (WAREBORN_DEPOSIT_COUNT defaults to 1) - means exactly one shard to mine and
        /// pick up. Chosen for testability, NOT fidelity: real atlas shards were rare,
        /// but that rarity is unrecoverable, so the honest default is "reliably present
        /// for the test" with a knob (<see cref="OneInDeposits"/>) to make it rarer.
        /// </summary>
        public const int DefaultOneInDeposits = 1;

        /// <summary>
        /// The rate from <c>WAREBORN_ATLAS_RATE</c> ("one shard per N deposits"), or
        /// <see cref="DefaultOneInDeposits"/>. Clamped to at least 1 - a rate of 0 or
        /// less would mean "a shard every zero deposits", which is meaningless; it
        /// falls back to the default. A non-integer or empty value likewise falls back,
        /// so a fat-fingered env var degrades to the testable default rather than
        /// crashing the spawn.
        /// </summary>
        public static int OneInDeposits(string? env)
        {
            if (!string.IsNullOrWhiteSpace(env)
                && int.TryParse(env.Trim(), out int n)
                && n >= 1)
            {
                return n;
            }
            return DefaultOneInDeposits;
        }

        /// <summary>
        /// Whether the deposit at <paramref name="depositIndex"/> carries a shard, given
        /// the "one in <paramref name="oneInDeposits"/>" rate. Deterministic: index 0
        /// always carries one (<c>0 % anything == 0</c>), and thereafter every
        /// <paramref name="oneInDeposits"/>-th deposit does. A rate of 1 or less means
        /// every deposit carries one.
        /// </summary>
        public static bool DepositCarriesShard(int depositIndex, int oneInDeposits)
        {
            if (depositIndex < 0)
            {
                return false;
            }
            if (oneInDeposits <= 1)
            {
                return true;
            }
            return depositIndex % oneInDeposits == 0;
        }
    }
}
