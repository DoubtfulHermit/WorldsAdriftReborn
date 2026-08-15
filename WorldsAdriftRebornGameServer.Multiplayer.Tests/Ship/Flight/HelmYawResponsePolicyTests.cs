using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class HelmYawResponsePolicyTests
    {
        [Fact]
        public void Opposite_full_lock_crosses_centre_in_under_one_tenth_second()
        {
            float yaw = 1f;
            for (int frame = 0; frame < 6; frame++)
            {
                float retail = yaw - 3f / 60f;
                yaw = HelmYawResponsePolicy.ApplyReversal(yaw, retail, -1f, 1f / 60f);
            }

            Assert.True(yaw < 0f, "opposing steering should have crossed centre within 100 ms");
        }

        [Theory]
        [InlineData(0.4f, 0.45f, 1f)]
        [InlineData(-0.4f, -0.45f, -1f)]
        [InlineData(0.4f, 0.39f, 0f)]
        [InlineData(0.1f, 0.05f, -1f)]
        public void Same_direction_neutral_and_deadzone_keep_retail_precision(
            float before, float retail, float raw)
        {
            Assert.Equal(retail,
                HelmYawResponsePolicy.ApplyReversal(before, retail, raw, 1f / 60f),
                precision: 5);
        }

        [Fact]
        public void Reversal_is_clamped_to_wire_axis_range()
        {
            Assert.Equal(-1f,
                HelmYawResponsePolicy.ApplyReversal(1f, 0.95f, -1f, 1f),
                precision: 5);
        }
    }
}
