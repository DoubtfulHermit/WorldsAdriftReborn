using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The DOCUMENTED, deterministic shard-spawn knob (the retail rarity rule is lost).
    /// Index 0 always carries a shard so a tester reliably has one; the rate is a
    /// tunable "one per N deposits"; and the selection never uses randomness, so every
    /// client walks the identical spawn plan.
    /// </summary>
    public class AtlasSpawnPolicyTests
    {
        [Fact]
        public void The_default_rate_puts_a_shard_in_every_deposit()
        {
            // Default (WAREBORN_ATLAS_RATE unset) = every deposit, which with the default
            // single-deposit session means exactly one shard - testable, not faithful.
            int rate = AtlasSpawnPolicy.OneInDeposits(null);
            Assert.Equal(AtlasSpawnPolicy.DefaultOneInDeposits, rate);
            for (int i = 0; i < 5; i++)
            {
                Assert.True(AtlasSpawnPolicy.DepositCarriesShard(i, rate));
            }
        }

        [Fact]
        public void The_proven_deposit_index_zero_always_carries_a_shard_at_any_rate()
        {
            foreach (int rate in new[] { 1, 2, 3, 4, 10, 100 })
            {
                Assert.True(AtlasSpawnPolicy.DepositCarriesShard(0, rate),
                    "index 0 must always carry a shard so a tester reliably has one");
            }
        }

        [Theory]
        [InlineData("4", 4)]
        [InlineData("  3 ", 3)]
        [InlineData("1", 1)]
        [InlineData("100", 100)]
        public void A_valid_rate_env_is_parsed(string env, int expected)
        {
            Assert.Equal(expected, AtlasSpawnPolicy.OneInDeposits(env));
        }

        [Theory]
        [InlineData("0")]        // a shard every zero deposits is meaningless
        [InlineData("-2")]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("lots")]     // fat-fingered
        [InlineData(null)]
        public void A_bad_rate_env_falls_back_to_the_default(string? env)
        {
            Assert.Equal(AtlasSpawnPolicy.DefaultOneInDeposits, AtlasSpawnPolicy.OneInDeposits(env));
        }

        [Fact]
        public void A_rate_of_four_selects_every_fourth_deposit_deterministically()
        {
            // one in four: indices 0,4,8 carry; 1,2,3,5 do not. Deterministic - the same
            // indices every time, on every client.
            Assert.True(AtlasSpawnPolicy.DepositCarriesShard(0, 4));
            Assert.False(AtlasSpawnPolicy.DepositCarriesShard(1, 4));
            Assert.False(AtlasSpawnPolicy.DepositCarriesShard(2, 4));
            Assert.False(AtlasSpawnPolicy.DepositCarriesShard(3, 4));
            Assert.True(AtlasSpawnPolicy.DepositCarriesShard(4, 4));
            Assert.True(AtlasSpawnPolicy.DepositCarriesShard(8, 4));
        }

        [Fact]
        public void A_rate_of_one_or_less_means_every_deposit()
        {
            for (int i = 0; i < 4; i++)
            {
                Assert.True(AtlasSpawnPolicy.DepositCarriesShard(i, 1));
                Assert.True(AtlasSpawnPolicy.DepositCarriesShard(i, 0));
            }
        }

        [Fact]
        public void A_negative_deposit_index_never_carries_a_shard()
        {
            Assert.False(AtlasSpawnPolicy.DepositCarriesShard(-1, 1));
        }
    }
}
