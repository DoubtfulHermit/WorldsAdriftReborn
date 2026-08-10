using System;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The switch from "always yank a player below the island home" to "only
    /// catch a fall through the WORLD; recovery is the manual F10". Two things
    /// are asserted here that no amount of flying off an island demonstrates
    /// quickly: which environment values mean "on", and that with the automatic
    /// rescue off a ship below the island is left alone while a true world-fall
    /// is still caught.
    /// </summary>
    public class AutoFallRescuePolicyTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long Player = 3;

        private static FixedPointPosition AtHeight(double metresY)
        {
            return FixedPointPosition.FromMetres(
                SpawnPolicy.PlayerSpawnPosition.MetresX,
                metresY,
                SpawnPolicy.PlayerSpawnPosition.MetresZ);
        }

        // ------------------------------------------------------------------
        // ENV PARSING
        // ------------------------------------------------------------------

        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData(" yes ")]
        [InlineData("On")]
        public void TruthyValuesEnableTheLegacyYank(string value)
        {
            Assert.True(AutoFallRescuePolicy.ParseEnabled(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("no")]
        [InlineData("off")]
        [InlineData("nonsense")]
        public void EverythingElseIsOff(string? value)
        {
            Assert.False(AutoFallRescuePolicy.ParseEnabled(value));
        }

        [Fact]
        public void DefaultIsOff()
        {
            // The whole point of the change: absent config, the automatic yank
            // does not fire. F10 is the recovery now.
            Assert.False(AutoFallRescuePolicy.ParseEnabled(null));
        }

        // ------------------------------------------------------------------
        // WHICH FLOOR EACH MODE USES
        // ------------------------------------------------------------------

        [Fact]
        public void EnabledUsesTheOrdinaryIslandFloor()
        {
            Assert.Equal(FallPolicy.FloorY, AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: true));
        }

        [Fact]
        public void DisabledUsesTheDeepWorldFallNet()
        {
            Assert.Equal(FallPolicy.DeepFloorY, AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: false));
        }

        [Fact]
        public void TheDeepNetIsWellBelowTheOrdinaryFloor()
        {
            // Sanity: the deep net must be strictly deeper than the island floor,
            // or "off" would rescue MORE than "on".
            Assert.True(FallPolicy.DeepFloorY < FallPolicy.FloorY);
        }

        [Fact]
        public void TheDeepNetIsBelowTheDeepestAuthoredIsland()
        {
            // Shattered Mausoleum's underside is -828.3 m. The deep net must sit
            // below it so that nothing a ship does near real ground trips it.
            Assert.True(FallPolicy.DeepFloorMetres < -828.3);
        }

        // ------------------------------------------------------------------
        // BEHAVIOUR: OFF MODE (the new default)
        // ------------------------------------------------------------------

        [Fact]
        public void OffMode_DoesNotYankAShipFlyingBelowTheIsland()
        {
            // A ship (or a player on one) sitting well below the island but above
            // the deep net: with the old floor this was an instant rescue; now it
            // must be left alone.
            FallWatch watch = new FallWatch(new FakeClock(),
                AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: false));

            // -600 m is below the ordinary floor (-504.7) but far above the deep
            // net (-2000), i.e. exactly the "flying below the island" case.
            Assert.Equal(FallVerdict.Descending, watch.Observe(Player, AtHeight(-600)));
            Assert.False(watch.IsFalling(Player));
        }

        [Fact]
        public void OffMode_StillCatchesAFallThroughTheWorld()
        {
            FallWatch watch = new FallWatch(new FakeClock(),
                AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: false));

            // Past the deep net: this has fallen out of the world and is still
            // rescued, using the very same one-rescue-per-fall machinery.
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-2500)));
            Assert.True(watch.IsFalling(Player));
        }

        [Fact]
        public void OffMode_KeepsOneRescuePerFallMachinery()
        {
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock,
                AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: false));

            // First packet past the deep net rescues; the next, moments later,
            // does not - the retry interval still governs, unchanged.
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-2500)));
            Assert.Equal(FallVerdict.RescueInFlight, watch.Observe(Player, AtHeight(-2600)));
        }

        // ------------------------------------------------------------------
        // BEHAVIOUR: ON MODE (legacy, restored by the env var)
        // ------------------------------------------------------------------

        [Fact]
        public void OnMode_YanksBelowTheOrdinaryFloorJustLikeBefore()
        {
            FallWatch watch = new FallWatch(new FakeClock(),
                AutoFallRescuePolicy.FloorYFor(autoRescueEnabled: true));

            // -600 m, below the island floor: the legacy rescue fires.
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-600)));
        }

        [Fact]
        public void DefaultConstructedWatchStillUsesTheIslandFloor()
        {
            // The parameterless-floor overload must be identical to the old
            // behaviour, so the 13 existing FallWatch tests keep meaning what they
            // meant.
            FallWatch watch = new FallWatch(new FakeClock());
            Assert.Equal(FallPolicy.FloorY, watch.FloorY);
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-600)));
        }
    }
}
