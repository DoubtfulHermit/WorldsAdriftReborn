using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The two 1005 pushes of a timed station craft. The regression these lock down is
    /// "stuck in the crafting animation forever": the COMPLETE push must (a) land on the
    /// SAME station the START opened the animation on, (b) serve itemReadyInSeconds=-1 and
    /// fire CraftingCompleted so the client closes the aperture / stops the atomizer, and
    /// (c) STILL carry one SlottedMaterial per requirement - an empty completion list throws
    /// IndexOutOfRange in the client's SyncCraftingItems and aborts OnCraftingCompleted
    /// before it can stop the animation and unlock the station.
    /// </summary>
    public class StationCraftPushPlanTests
    {
        private const long Station = 2;
        private const int Seconds = 10;

        [Fact]
        public void StartAndComplete_TargetTheSameStation()
        {
            // The START push opened the aperture/atomizer on the STATION; the COMPLETE push
            // must reach the SAME entity or the animation it opened never gets its stop.
            CraftingStatePush start = StationCraftPushPlan.Start(Station, requirementCount: 3, seconds: Seconds);
            CraftingStatePush done = StationCraftPushPlan.Complete(Station, requirementCount: 3);

            Assert.Equal(Station, start.Target);
            Assert.Equal(Station, done.Target);
            Assert.Equal(start.Target, done.Target);
        }

        [Fact]
        public void Start_HoldsApertureOpen_WithPositiveCountdownAndCraftingStarted()
        {
            CraftingStatePush start = StationCraftPushPlan.Start(Station, requirementCount: 3, seconds: Seconds);

            Assert.Equal(Seconds, start.ItemReadyInSeconds);
            Assert.True(start.ItemReadyInSeconds > 0, "START must serve a positive countdown so the aperture stays open");
            Assert.True(start.CraftingStarted);
            Assert.False(start.CraftingCompleted);
        }

        [Fact]
        public void Complete_ClosesTheAperture_AndFiresCraftingCompleted()
        {
            CraftingStatePush done = StationCraftPushPlan.Complete(Station, requirementCount: 3);

            // -1 (not 0): the client's stop condition is itemReadyInSeconds < 0, and a bare 0
            // is the protobuf int default that would drop off the wire.
            Assert.Equal(-1, done.ItemReadyInSeconds);
            Assert.Equal(StationCraftPushPlan.ClosedCountdown, done.ItemReadyInSeconds);
            Assert.True(done.ItemReadyInSeconds < 0);
            Assert.True(done.CraftingCompleted);
            Assert.False(done.CraftingStarted);
        }

        [Theory]
        [InlineData(1)]  // Helm: a single-requirement recipe still must not send an empty list
        [InlineData(3)]
        [InlineData(4)]  // Procedural Engine
        public void Complete_CarriesOneSlotPerRequirement_NotAnEmptyList(int requirementCount)
        {
            // THE fix: the completion list length must equal the client's CraftingSlotData.Count
            // (== the recipe's requirement count). A shorter/empty list throws IndexOutOfRange
            // in CraftingStationData.SyncCraftingItems and wedges the station mid-animation.
            CraftingStatePush done = StationCraftPushPlan.Complete(Station, requirementCount);

            Assert.Equal(requirementCount, done.SlotCount);
            Assert.True(done.SlotCount > 0, "an empty completion slot list is exactly the stuck-animation bug");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        public void StartAndComplete_CarryTheSameSlotCount(int requirementCount)
        {
            // The client's CraftingSlotData is built once from the loaded schematic and read
            // by BOTH pushes; the two must agree on how many slots it indexes.
            CraftingStatePush start = StationCraftPushPlan.Start(Station, requirementCount, Seconds);
            CraftingStatePush done = StationCraftPushPlan.Complete(Station, requirementCount);

            Assert.Equal(start.SlotCount, done.SlotCount);
            Assert.Equal(requirementCount, start.SlotCount);
        }
    }
}
