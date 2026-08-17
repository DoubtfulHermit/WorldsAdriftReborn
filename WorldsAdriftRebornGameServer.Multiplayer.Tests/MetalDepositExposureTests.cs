using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// WHEN a deposit's core reads as exposed. The threshold is the client's own first
    /// cracked core-damage variant (half health), so these tests pin the arithmetic that
    /// turns "half health" into a shot count - the number the shard's pickable seam and
    /// the metal payout schedule both hang off.
    /// </summary>
    public class MetalDepositExposureTests
    {
        private const double Half = MetalDepositExposure.DefaultExposureHealthFraction;

        [Fact]
        public void The_default_threshold_is_half_health()
        {
            // The core's damage model flips to its first cracked variant at
            // round((1 - h) * (variants - 1)) >= 1, i.e. h <= 0.5 for a 3-variant core.
            Assert.Equal(0.5, MetalDepositExposure.DefaultExposureHealthFraction);
        }

        [Fact]
        public void A_ten_shot_deposit_exposes_half_way()
        {
            Assert.Equal(5, MetalDepositExposure.ShotsToExpose(10, Half));

            Assert.False(MetalDepositExposure.IsExposed(0, 10, Half));
            Assert.False(MetalDepositExposure.IsExposed(4, 10, Half));
            Assert.True(MetalDepositExposure.IsExposed(5, 10, Half));
            Assert.True(MetalDepositExposure.IsExposed(9, 10, Half));
            Assert.True(MetalDepositExposure.IsExposed(10, 10, Half));
        }

        [Fact]
        public void Exposure_is_monotone_so_the_caller_sees_one_edge()
        {
            // Once true it stays true - the registry's Lodged -> Exposed step is
            // therefore a genuine once-only transition, not something that can flap.
            bool seen = false;
            for (int hits = 0; hits <= 10; hits++)
            {
                bool now = MetalDepositExposure.IsExposed(hits, 10, Half);
                Assert.False(seen && !now);
                seen |= now;
            }
            Assert.True(seen);
        }

        [Fact]
        public void An_unhit_deposit_is_never_exposed_whatever_the_fraction()
        {
            // A fraction of 1.0 rounds "shots needed" to zero; the floor of one shot is
            // what stops a shard being handed out to a player who never fired.
            Assert.Equal(1, MetalDepositExposure.ShotsToExpose(10, 1.0));
            Assert.False(MetalDepositExposure.IsExposed(0, 10, 1.0));
            Assert.True(MetalDepositExposure.IsExposed(1, 10, 1.0));
        }

        [Fact]
        public void Exposure_never_needs_more_shots_than_the_deposit_has()
        {
            // A tiny fraction would otherwise ask for more shots than exist and make the
            // core impossible to expose before it is already destroyed.
            Assert.Equal(10, MetalDepositExposure.ShotsToExpose(10, 0.0001));
            Assert.True(MetalDepositExposure.IsExposed(10, 10, 0.0001));
        }

        [Fact]
        public void A_one_shot_deposit_exposes_on_its_only_shot()
        {
            Assert.Equal(1, MetalDepositExposure.ShotsToExpose(1, Half));
            Assert.True(MetalDepositExposure.IsExposed(1, 1, Half));
        }

        [Fact]
        public void A_nonsense_shot_count_still_yields_a_usable_threshold()
        {
            Assert.Equal(1, MetalDepositExposure.ShotsToExpose(0, Half));
            Assert.Equal(1, MetalDepositExposure.ShotsToExpose(-5, Half));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-number")]
        [InlineData("0")]
        [InlineData("-0.5")]
        [InlineData("1.5")]
        public void A_garbled_or_out_of_range_fraction_falls_back_to_the_default(string? env)
        {
            Assert.Equal(MetalDepositExposure.DefaultExposureHealthFraction,
                MetalDepositExposure.ExposureHealthFraction(env));
        }

        [Fact]
        public void A_valid_fraction_is_honoured_and_is_culture_invariant()
        {
            Assert.Equal(0.25, MetalDepositExposure.ExposureHealthFraction("0.25"));
            Assert.Equal(1.0, MetalDepositExposure.ExposureHealthFraction("1"));
            // A quarter-health threshold means three quarters of the shots first.
            Assert.Equal(8, MetalDepositExposure.ShotsToExpose(10, 0.25));
        }
    }
}
