using System;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The invariant tying the ENet host's channel cap to the number of ENet
    /// op-types, so a sixth op-type can never be added while the host still opens
    /// only five channels.
    ///
    /// THE TRAP THIS CLOSES. The host is created with a bare literal
    /// <c>maxChannels = 5</c>, exactly equal to the <c>EnetLayer.ENetChannel</c>
    /// op-type count. ENet channels are INDICES: a connection negotiates
    /// <c>maxChannels</c> channels (0..maxChannels-1) and silently caps away any
    /// send on a higher index. So two things have to stay true, and nothing was
    /// asserting either:
    ///
    ///   1. The op-type enum values are a contiguous 0..N-1 block. If someone
    ///      added <c>SOME_OP = 9</c>, the enum would have six members but its
    ///      highest value would be 9; code sending on channel 9 would be capped
    ///      away by a host that only opened channels 0..5.
    ///   2. The host's <c>maxChannels</c> equals that op-type count. Add a sixth
    ///      op-type (channel index 5) without bumping <c>maxChannels</c> and every
    ///      send on channel 5 is silently dropped by the negotiated connection.
    ///
    /// Both are latent: nothing breaks today, and the failure when it does break
    /// is invisible (a channel that "sends" but never arrives). This is the
    /// compile-nothing / startup assert that turns that silent cap into a loud
    /// throw the first time either invariant is violated. Pure so it is tested in
    /// isolation; the caller feeds it the values it read off the real enum.
    /// </summary>
    public static class EnetChannelContract
    {
        /// <summary>
        /// True when the op-type values form a contiguous 0..N-1 block, i.e. the
        /// number of op-types is exactly the highest op-type value plus one. ENet
        /// channels are indices, so a gap means a channel index with no channel.
        /// </summary>
        public static bool IsContiguous(int channelCount, int highestChannelValue)
        {
            return channelCount == highestChannelValue + 1;
        }

        /// <summary>
        /// True when the host's channel cap matches the op-type count, so every
        /// op-type has a channel and no channel is negotiated away.
        /// </summary>
        public static bool CapMatchesOpTypeCount(int maxChannels, int channelCount)
        {
            return maxChannels == channelCount;
        }

        /// <summary>
        /// The startup assert. Throws <see cref="InvalidOperationException"/> with
        /// a message naming which invariant broke, so adding a sixth op-type (or a
        /// non-contiguous op-type value) fails loudly at boot instead of sending on
        /// a channel the negotiated connection caps away. A no-op when both
        /// invariants hold.
        /// </summary>
        /// <param name="maxChannels">The literal passed to ENet_Create_Host.</param>
        /// <param name="channelCount">Enum.GetValues(typeof(ENetChannel)).Length.</param>
        /// <param name="highestChannelValue">(int) of the highest ENetChannel member.</param>
        public static void Validate(int maxChannels, int channelCount, int highestChannelValue)
        {
            if (!IsContiguous(channelCount, highestChannelValue))
            {
                throw new InvalidOperationException(
                    "[enet-channels] ENetChannel op-type values are not a contiguous 0.."
                    + (channelCount - 1) + " block: " + channelCount + " op-types but the highest"
                    + " value is " + highestChannelValue + ". ENet channels are indices, so channel "
                    + highestChannelValue + " would be sent on a host that only opened "
                    + channelCount + " channels. Give the op-types contiguous values.");
            }

            if (!CapMatchesOpTypeCount(maxChannels, channelCount))
            {
                throw new InvalidOperationException(
                    "[enet-channels] the ENet host opens maxChannels=" + maxChannels
                    + " but there are now " + channelCount + " ENetChannel op-types. A send on"
                    + " channel " + (channelCount - 1) + " would be silently capped away by the"
                    + " negotiated connection. Set maxChannels to " + channelCount
                    + " (one channel per op-type).");
            }
        }
    }
}
