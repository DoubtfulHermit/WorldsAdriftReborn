using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The static facts about a FUEL POD as a world entity: prefab name, the real
    /// granted item, key helpers, and the starter placement set. The live per-pod
    /// state and the pickup gate are the SHARED core, tested in
    /// <see cref="LodgeablePickupRegistryTests"/> / <see cref="LodgeablePickupPolicyTests"/>.
    /// </summary>
    public class FuelPodsTests
    {
        [Fact]
        public void A_pod_grants_the_real_fuel_item_which_is_not_a_pending_placeholder()
        {
            Assert.Equal("fuel", FuelPods.ItemTypeId);
            Assert.DoesNotContain("PENDING", FuelPods.ItemTypeId, StringComparison.Ordinal);
            Assert.True(FuelPods.FuelPerPod >= 1);
        }

        [Fact]
        public void The_default_asset_name_is_the_egg_prefab_and_is_env_overridable()
        {
            Assert.Equal("Egg", FuelPods.DefaultAssetName);

            string? saved = Environment.GetEnvironmentVariable("WAREBORN_FUELPOD_ASSET");
            try
            {
                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", null);
                Assert.Equal(FuelPods.DefaultAssetName, FuelPods.AssetName);

                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", "  ");
                Assert.Equal(FuelPods.DefaultAssetName, FuelPods.AssetName); // blank falls back

                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", "FuelPodCustom");
                Assert.Equal("FuelPodCustom", FuelPods.AssetName);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", saved);
            }
        }

        [Theory]
        [InlineData(0, "fuel-pod-0")]
        [InlineData(3, "fuel-pod-3")]
        public void Keys_round_trip_through_index(int index, string expectedKey)
        {
            Assert.Equal(expectedKey, FuelPods.KeyFor(index));
            Assert.True(FuelPods.IsPodKey(expectedKey));
            Assert.Equal(index, FuelPods.IndexOf(expectedKey));
        }

        [Fact]
        public void A_non_pod_key_is_not_a_pod_and_has_no_index()
        {
            Assert.False(FuelPods.IsPodKey("atlas-shard-0"));
            Assert.False(FuelPods.IsPodKey("tree-3"));
            Assert.False(FuelPods.IsPodKey(null));
            Assert.Null(FuelPods.IndexOf("tree-3"));
        }

        [Fact]
        public void The_starter_set_places_several_pods_and_each_has_a_distinct_position()
        {
            Assert.True(FuelPods.HavenPlacements.Count >= 3);
            var seen = new System.Collections.Generic.HashSet<(long, long, long)>();
            for (int i = 0; i < FuelPods.HavenPlacements.Count; i++)
            {
                FixedPointPosition p = FuelPods.PositionAt(i);
                Assert.True(seen.Add((p.X, p.Y, p.Z)), $"pod {i} shares a position with another pod");
            }
        }

        [Fact]
        public void The_count_knob_clamps_to_the_table_and_never_drops_the_first_pod()
        {
            int full = FuelPods.HavenPlacements.Count;
            Assert.Equal(full, FuelPods.CountFrom(null));
            Assert.Equal(1, FuelPods.CountFrom("1"));
            Assert.Equal(1, FuelPods.CountFrom("0"));       // clamped up to 1
            Assert.Equal(full, FuelPods.CountFrom("9999")); // clamped down to full
            Assert.Equal(full, FuelPods.CountFrom("junk")); // bad value -> full
        }
    }
}
