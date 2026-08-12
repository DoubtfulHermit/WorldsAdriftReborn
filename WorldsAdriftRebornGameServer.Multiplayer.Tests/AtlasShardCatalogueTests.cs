using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The static atlas-shard facts: the VERIFIED wire prefab name, the deposit
    /// pairing, the slot/placement, and the ONE value that is deliberately still
    /// pending refdata recovery. Pure - no ENet, no game types - so the constants are
    /// pinned here rather than in front of a running client.
    /// </summary>
    public class AtlasShardCatalogueTests
    {
        [Fact]
        public void The_prefab_name_is_the_bare_MetalDepositAtlas_the_client_can_resolve()
        {
            // VERIFIED: MetalDepositAtlas is line 158 of prefab-names.tsv (client AND
            // worker "yes"). Bare, because the client appends the worker suffix itself.
            Assert.Equal("MetalDepositAtlas", AtlasShardCatalogue.AssetName);
            Assert.DoesNotContain("_unity", AtlasShardCatalogue.AssetName);
        }

        [Fact]
        public void The_item_type_id_is_the_reconstructed_atlasShard_row()
        {
            // The retail id is unrecoverable (findings-atlas-refdata #1), so the row was
            // DEFINED for the revival: atlasShard is a real itemData.json row, so Grant
            // accepts it and the pickup completes rather than rolling back.
            Assert.Equal("atlasShard", AtlasShardCatalogue.ItemTypeId);
            Assert.False(AtlasShardCatalogue.IsItemIdPending);
            // It must NOT be any of the near-miss ids the findings warn against.
            Assert.NotEqual("iron", AtlasShardCatalogue.ItemTypeId);
            Assert.NotEqual("scrapItem-atlashod", AtlasShardCatalogue.ItemTypeId);
            Assert.NotEqual("1305", AtlasShardCatalogue.ItemTypeId);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(21)]
        public void A_shard_key_round_trips_to_its_index_and_names_its_deposit(int index)
        {
            string key = AtlasShardCatalogue.KeyFor(index);
            Assert.True(AtlasShardCatalogue.IsShardKey(key));
            Assert.Equal(index, AtlasShardCatalogue.IndexOf(key));
            // atlas-shard-N pairs with deposit-N, so the host key is the deposit's.
            Assert.Equal(MetalDeposits.KeyFor(index), AtlasShardCatalogue.HostDepositKeyFor(index));
        }

        [Fact]
        public void A_non_shard_key_is_rejected()
        {
            Assert.False(AtlasShardCatalogue.IsShardKey("deposit-0"));
            Assert.False(AtlasShardCatalogue.IsShardKey(null));
            Assert.Null(AtlasShardCatalogue.IndexOf("deposit-0"));
            Assert.Null(AtlasShardCatalogue.IndexOf("atlas-shard-notanumber"));
        }

        [Fact]
        public void The_lodged_position_is_the_deposit_raised_by_the_offset()
        {
            FixedPointPosition deposit = FixedPointPosition.FromMetres(100.0, 5.0, -20.0);
            FixedPointPosition shard = AtlasShardCatalogue.LodgedPositionFor(deposit);

            // Same X/Z, Y raised by exactly the offset in fixed-point units.
            Assert.Equal(deposit.X, shard.X);
            Assert.Equal(deposit.Z, shard.Z);
            long expectedRaise = (long)(AtlasShardCatalogue.LodgedHeightOffsetMetres * FixedPointPosition.UnitsPerMetre);
            Assert.Equal(deposit.Y + expectedRaise, shard.Y);
            Assert.True(shard.Y > deposit.Y);
        }

        [Fact]
        public void The_pickup_sizing_reuses_the_nugget_pickup_values()
        {
            Assert.Equal(MetalNodes.PickUpRadius, AtlasShardCatalogue.PickUpRadius);
            Assert.Equal(MetalNodes.PickUpTimeToUse, AtlasShardCatalogue.PickUpTimeToUse);
            Assert.True(AtlasShardCatalogue.PickUpRadius > 0f);
        }

        [Fact]
        public void The_default_slot_is_zero()
        {
            Assert.Equal(0, AtlasShardCatalogue.DefaultSlotId);
        }
    }
}
