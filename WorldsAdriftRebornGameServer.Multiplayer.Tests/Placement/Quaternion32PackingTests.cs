using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The smallest-three packing must produce exactly what the client's
    /// Quaternion32Util decodes, or a placed structure faces the wrong way (or,
    /// with the naive "0" rotation, decodes to NaN and is rejected). Identity maps
    /// to the client's sentinel 1023; a real yaw survives a round-trip.
    /// </summary>
    public class Quaternion32PackingTests
    {
        [Fact]
        public void Identity_encodes_to_the_client_sentinel()
        {
            Assert.Equal(1023u, Quaternion32Packing.Encode(1f, 0f, 0f, 0f));
            Assert.Equal(1023u, Quaternion32Packing.Encode(-1f, 0f, 0f, 0f));
        }

        [Fact]
        public void The_sentinel_decodes_back_to_identity()
        {
            (float w, float x, float y, float z) = Quaternion32Packing.Decode(1023u);
            Assert.Equal(1f, w, 5);
            Assert.Equal(0f, x, 5);
            Assert.Equal(0f, y, 5);
            Assert.Equal(0f, z, 5);
        }

        [Fact]
        public void A_non_finite_rotation_falls_back_to_identity_rather_than_throwing()
        {
            Assert.Equal(Quaternion32Packing.Identity, Quaternion32Packing.Encode(float.NaN, 0f, 0f, 0f));
            Assert.Equal(Quaternion32Packing.Identity, Quaternion32Packing.Encode(0f, float.PositiveInfinity, 0f, 0f));
        }

        [Fact]
        public void A_zero_magnitude_rotation_falls_back_to_identity()
        {
            Assert.Equal(Quaternion32Packing.Identity, Quaternion32Packing.Encode(0f, 0f, 0f, 0f));
        }

        [Theory]
        [InlineData(45f)]
        [InlineData(90f)]
        [InlineData(135f)]
        [InlineData(200f)]
        [InlineData(315f)]
        public void A_yaw_survives_a_round_trip(float degrees)
        {
            // Yaw about +Y: q = (cos(a/2), 0, sin(a/2), 0).
            double half = degrees * Math.PI / 180.0 / 2.0;
            float w = (float)Math.Cos(half);
            float y = (float)Math.Sin(half);

            uint packed = Quaternion32Packing.Encode(w, 0f, y, 0f);
            (float dw, float dx, float dy, float dz) = Quaternion32Packing.Decode(packed);

            // Quaternion q and -q are the same rotation; the encoder canonicalises
            // the sign of the largest component, so compare the reconstructed
            // rotation up to global sign.
            AssertSameRotation(w, 0f, y, 0f, dw, dx, dy, dz);
        }

        [Fact]
        public void An_unnormalised_input_is_normalised_before_packing()
        {
            // Same rotation as the 90-degree yaw above, scaled x3; must pack the same.
            double half = 90.0 * Math.PI / 180.0 / 2.0;
            float w = (float)Math.Cos(half);
            float y = (float)Math.Sin(half);

            uint normalised = Quaternion32Packing.Encode(w, 0f, y, 0f);
            uint scaled = Quaternion32Packing.Encode(w * 3f, 0f, y * 3f, 0f);
            Assert.Equal(normalised, scaled);
        }

        private static void AssertSameRotation(
            float aw, float ax, float ay, float az,
            float bw, float bx, float by, float bz)
        {
            // Dot product magnitude near 1 means the two unit quaternions represent
            // the same orientation (allowing for the q ≡ -q double cover). The 10-bit
            // components give ~1e-3 resolution, so tolerate that.
            float dot = (aw * bw) + (ax * bx) + (ay * by) + (az * bz);
            Assert.True(Math.Abs(dot) > 0.999f, $"dot={dot} not ~±1");
        }
    }
}
