using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The channel-cap invariant: the ENet host must open exactly one channel per
    /// op-type, and the op-types must be a contiguous 0..N-1 block. The whole
    /// point is that a SIXTH op-type added later fails LOUDLY here instead of
    /// silently sending on a channel the negotiated connection caps away.
    /// </summary>
    public class EnetChannelContractTests
    {
        // The live shape: 5 op-types (ASSET_LOAD_REQUEST_OP..COMPONENT_UPDATE_OP),
        // highest value 4, host opens 5 channels. Mirrors EnetLayer today.
        private const int LiveMaxChannels = 5;
        private const int LiveChannelCount = 5;
        private const int LiveHighestValue = 4;

        [Fact]
        public void The_current_five_channel_shape_validates()
        {
            // No throw. This is the shape EnetLayer wires today.
            EnetChannelContract.Validate(LiveMaxChannels, LiveChannelCount, LiveHighestValue);
            Assert.True(EnetChannelContract.IsContiguous(LiveChannelCount, LiveHighestValue));
            Assert.True(EnetChannelContract.CapMatchesOpTypeCount(LiveMaxChannels, LiveChannelCount));
        }

        [Fact]
        public void A_sixth_op_type_without_bumping_the_cap_throws_loudly()
        {
            // Someone adds a 6th op-type (channel index 5) but leaves the host at
            // maxChannels=5. Every send on channel 5 would be silently dropped;
            // the assert must turn that into a boot-time throw instead.
            int channelCount = 6;
            int highestValue = 5;
            int maxChannels = 5; // not bumped

            Assert.False(EnetChannelContract.CapMatchesOpTypeCount(maxChannels, channelCount));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EnetChannelContract.Validate(maxChannels, channelCount, highestValue));
            Assert.Contains("channel 5", ex.Message);
        }

        [Fact]
        public void Bumping_the_cap_alongside_a_sixth_op_type_validates()
        {
            // The correct edit: 6 op-types AND maxChannels=6. No throw.
            EnetChannelContract.Validate(6, 6, 5);
        }

        [Fact]
        public void A_non_contiguous_op_type_value_throws_loudly()
        {
            // Someone gives an op-type a value of 9 (a gap). Now there are 6
            // op-types but the highest index is 9, so channel 9 rides a host that
            // only opened 6 channels. Contiguity must be asserted first.
            int channelCount = 6;
            int highestValue = 9;
            int maxChannels = 6;

            Assert.False(EnetChannelContract.IsContiguous(channelCount, highestValue));
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EnetChannelContract.Validate(maxChannels, channelCount, highestValue));
            Assert.Contains("contiguous", ex.Message);
        }

        [Fact]
        public void Contiguity_is_checked_before_the_cap_so_the_root_cause_is_named()
        {
            // When both are wrong, the non-contiguity is the root cause and must be
            // the message, not the downstream cap mismatch.
            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => EnetChannelContract.Validate(maxChannels: 5, channelCount: 6, highestChannelValue: 9));
            Assert.Contains("contiguous", ex.Message);
        }
    }
}
