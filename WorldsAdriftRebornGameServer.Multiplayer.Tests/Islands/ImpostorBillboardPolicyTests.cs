using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public class ImpostorBillboardPolicyTests
    {
        [Fact]
        public void Retail_lets_a_late_rebake_swing_twelve_times_further_than_a_timely_one()
        {
            ImpostorBillboardSettings retail = ImpostorBillboardPolicy.RetailIslandSettings();

            Assert.Equal(2.5f, ImpostorBillboardPolicy.SteadyStateSwingDegrees(retail), precision: 5);
            Assert.Equal(30f, ImpostorBillboardPolicy.StaleSwingDegrees(retail), precision: 5);
            Assert.Equal(12f, ImpostorBillboardPolicy.SwingToleranceRatio(retail), precision: 5);
            Assert.False(ImpostorBillboardPolicy.IsSwingBounded(retail));
        }

        [Fact]
        public void Correcting_retail_bounds_the_swing_to_the_rebake_trigger()
        {
            ImpostorBillboardSettings corrected = ImpostorBillboardPolicy.Correct(
                ImpostorBillboardPolicy.RetailIslandSettings(), 0f, 0f, 0f);

            Assert.True(ImpostorBillboardPolicy.IsSwingBounded(corrected));
            Assert.Equal(2.5f, corrected.FollowAngleDegrees, precision: 5);
            Assert.Equal(1f, ImpostorBillboardPolicy.SwingToleranceRatio(corrected), precision: 5);
        }

        [Fact]
        public void Correcting_leaves_the_rebake_policy_alone_when_nothing_is_overridden()
        {
            ImpostorBillboardSettings retail = ImpostorBillboardPolicy.RetailIslandSettings();
            ImpostorBillboardSettings corrected =
                ImpostorBillboardPolicy.Correct(retail, 0f, 0f, 0f);

            Assert.Equal(retail.RebakeAngleDegrees, corrected.RebakeAngleDegrees, precision: 5);
            Assert.Equal(retail.RebakeSeconds, corrected.RebakeSeconds, precision: 5);
            Assert.Equal(retail.RebakeOnTime, corrected.RebakeOnTime);
        }

        [Fact]
        public void A_tightened_rebake_angle_drags_the_follow_angle_with_it()
        {
            ImpostorBillboardSettings corrected = ImpostorBillboardPolicy.Correct(
                ImpostorBillboardPolicy.RetailIslandSettings(),
                followOverride: 0f, rebakeAngleOverride: 1f, rebakeSecondsOverride: 0f);

            Assert.Equal(1f, corrected.RebakeAngleDegrees, precision: 5);
            Assert.Equal(1f, corrected.FollowAngleDegrees, precision: 5);
            Assert.True(ImpostorBillboardPolicy.IsSwingBounded(corrected));
        }

        [Fact]
        public void An_explicit_follow_override_wins_over_the_rebake_angle()
        {
            ImpostorBillboardSettings corrected = ImpostorBillboardPolicy.Correct(
                ImpostorBillboardPolicy.RetailIslandSettings(),
                followOverride: 8f, rebakeAngleOverride: 0f, rebakeSecondsOverride: 0f);

            Assert.Equal(8f, corrected.FollowAngleDegrees, precision: 5);
            Assert.False(ImpostorBillboardPolicy.IsSwingBounded(corrected));
        }

        [Theory]
        [InlineData(0.01f, ImpostorBillboardPolicy.MinFollowAngleDegrees)]
        [InlineData(90f, ImpostorBillboardPolicy.MaxFollowAngleDegrees)]
        [InlineData(12f, 12f)]
        public void Follow_angle_stays_inside_the_range_retail_declares(
            float requested, float expected)
        {
            Assert.Equal(expected,
                ImpostorBillboardPolicy.FollowAngleFor(2.5f, requested), precision: 5);
        }

        [Fact]
        public void A_rebake_angle_of_zero_would_never_be_written_to_the_controller()
        {
            // Zero means "no override"; it must not silently become a 0-degree
            // trigger, which would ask for a bake on every single frame.
            Assert.Equal(2.5f, ImpostorBillboardPolicy.RebakeAngleFor(2.5f, 0f), precision: 5);
            Assert.Equal(ImpostorBillboardPolicy.MinRebakeAngleDegrees,
                ImpostorBillboardPolicy.RebakeAngleFor(2.5f, 0.001f), precision: 5);
        }

        [Fact]
        public void A_rebake_timer_override_never_goes_below_a_quarter_second()
        {
            Assert.Equal(10f, ImpostorBillboardPolicy.RebakeSecondsFor(10f, 0f), precision: 5);
            Assert.Equal(2f, ImpostorBillboardPolicy.RebakeSecondsFor(10f, 2f), precision: 5);
            Assert.Equal(0.25f, ImpostorBillboardPolicy.RebakeSecondsFor(10f, 0.01f), precision: 5);
        }

        [Fact]
        public void Ship_impostors_share_the_defect_with_a_shorter_timer()
        {
            // ShipImposter.InitShipImposter: useUpdateByTime, timeInterval 5.
            // The follow angle is a single global on ImpostersHandler, so a fix
            // there covers ships too - worth knowing before changing it.
            ImpostorBillboardSettings ship = new ImpostorBillboardSettings();
            ship.RebakeAngleDegrees = ImpostorBillboardPolicy.RetailRebakeAngleDegrees;
            ship.FollowAngleDegrees = ImpostorBillboardPolicy.RetailFollowAngleDegrees;
            ship.RebakeSeconds = 5f;
            ship.RebakeOnTime = true;

            Assert.False(ImpostorBillboardPolicy.IsSwingBounded(ship));
            Assert.True(ImpostorBillboardPolicy.IsSwingBounded(
                ImpostorBillboardPolicy.Correct(ship, 0f, 0f, 0f)));
        }
    }
}
