using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The helm mount-rotation lock: identity composed with the
    /// WAREBORN_HELM_MOUNT_YAW offset. These tests pin the composition law, the
    /// env parse, and - most load-bearing - the EXACT packed Quaternion32 values
    /// the deploy one-liner writes into an existing save's MountedParts record,
    /// so the number in the runbook is asserted here rather than hand-derived.
    /// </summary>
    public class HelmMountLockTests
    {
        // The exact packed uints for the two live-plausible signs of the 90-degree
        // offset. THE deploy one-liner values: a saved helm's PackedRotation must
        // be set to the value matching the WAREBORN_HELM_MOUNT_YAW in force.
        private const uint PackedYawPlus90 = 536869375u;
        private const uint PackedYawMinus90 = 535822847u;

        [Fact]
        public void Identity_composed_with_a_rotation_is_that_rotation()
        {
            (float W, float X, float Y, float Z) q = HelmMountLock.YawQuaternion(37.5);
            (float W, float X, float Y, float Z) composed = HelmMountLock.Compose((1f, 0f, 0f, 0f), q);
            Assert.Equal(q.W, composed.W, 6);
            Assert.Equal(q.X, composed.X, 6);
            Assert.Equal(q.Y, composed.Y, 6);
            Assert.Equal(q.Z, composed.Z, 6);
        }

        [Fact]
        public void The_lock_rotation_is_the_identity_composed_yaw()
        {
            (float W, float X, float Y, float Z) locked = HelmMountLock.LockRotation(90.0);
            // yaw +90 about +Y: (cos45, 0, sin45, 0)
            Assert.Equal(Math.Cos(Math.PI / 4.0), locked.W, 6);
            Assert.Equal(0f, locked.X, 6);
            Assert.Equal(Math.Sin(Math.PI / 4.0), locked.Y, 6);
            Assert.Equal(0f, locked.Z, 6);
        }

        [Fact]
        public void A_zero_offset_packs_to_the_identity_sentinel()
        {
            // Byte-identical to the old raw-identity lock: 0 degrees is 1023.
            Assert.Equal(Quaternion32Packing.Identity, HelmMountLock.PackedLockRotation(0.0));
        }

        [Fact]
        public void The_plus_90_lock_packs_to_the_deploy_one_liner_value()
        {
            Assert.Equal(PackedYawPlus90, HelmMountLock.PackedLockRotation(90.0));
        }

        [Fact]
        public void The_minus_90_lock_packs_to_the_alternate_one_liner_value()
        {
            Assert.Equal(PackedYawMinus90, HelmMountLock.PackedLockRotation(-90.0));
        }

        [Fact]
        public void The_packed_lock_round_trips_through_the_client_decoder()
        {
            foreach (double degrees in new[] { 90.0, -90.0, 45.0, 180.0 })
            {
                uint packed = HelmMountLock.PackedLockRotation(degrees);
                (float w, float x, float y, float z) = Quaternion32Packing.Decode(packed);
                (float W, float X, float Y, float Z) expected = HelmMountLock.LockRotation(degrees);
                // Sign canonicalisation: q and -q are the same rotation, and the
                // encoder flips signs so its largest component is positive. Align
                // hemispheres by the dot product, not any single component.
                float dot = expected.W * w + expected.X * x + expected.Y * y + expected.Z * z;
                float sign = dot >= 0f ? 1f : -1f;
                // 10-bit quantization: ~0.0014 per component worst case.
                Assert.Equal(expected.W, sign * w, 2);
                Assert.Equal(expected.X, sign * x, 2);
                Assert.Equal(expected.Y, sign * y, 2);
                Assert.Equal(expected.Z, sign * z, 2);
            }
        }

        [Fact]
        public void The_default_offset_is_the_live_reported_90()
        {
            Assert.Equal(90.0, HelmMountLock.DefaultYawDegrees);
            Assert.Equal(HelmMountLock.DefaultYawDegrees, HelmMountLock.ParseYawDegrees(null));
        }

        [Theory]
        [InlineData("90", 90.0)]
        [InlineData("-90", -90.0)]
        [InlineData("0", 0.0)]
        [InlineData("22.5", 22.5)]
        [InlineData("  180 ", 180.0)]
        public void The_env_knob_parses_invariant_degrees(string raw, double expected)
        {
            Assert.Equal(expected, HelmMountLock.ParseYawDegrees(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("east")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void A_malformed_knob_falls_back_to_the_default(string raw)
        {
            Assert.Equal(HelmMountLock.DefaultYawDegrees, HelmMountLock.ParseYawDegrees(raw));
        }
    }
}
