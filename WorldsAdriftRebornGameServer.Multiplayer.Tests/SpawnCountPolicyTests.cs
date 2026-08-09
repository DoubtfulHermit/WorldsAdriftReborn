using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The env caps that dial the test-populated world (trees, ore) up and down
    /// without a rebuild. The load-bearing property is the FLOOR: the count can
    /// never drop below 1, because the near-spawn HavenTree and the proven metal
    /// node sit first in their sets and must survive any cap.
    /// </summary>
    public class SpawnCountPolicyTests
    {
        [Fact]
        public void ClampKeepsAValueInsideOneToFull()
        {
            Assert.Equal(5, SpawnCountPolicy.Clamp(5, 21));
            Assert.Equal(1, SpawnCountPolicy.Clamp(1, 21));
            Assert.Equal(21, SpawnCountPolicy.Clamp(21, 21));
        }

        [Fact]
        public void ClampNeverDropsBelowOne()
        {
            // 0 or negative would delete the near-spawn anchor entity; refuse it.
            Assert.Equal(1, SpawnCountPolicy.Clamp(0, 21));
            Assert.Equal(1, SpawnCountPolicy.Clamp(-4, 21));
        }

        [Fact]
        public void ClampNeverExceedsWhatIsPlaced()
        {
            Assert.Equal(21, SpawnCountPolicy.Clamp(1000, 21));
        }

        [Fact]
        public void UnsetOrGarbageIsTheFullSet()
        {
            Assert.Equal(21, SpawnCountPolicy.CountFrom(null, 21));
            Assert.Equal(21, SpawnCountPolicy.CountFrom("", 21));
            Assert.Equal(21, SpawnCountPolicy.CountFrom("lots", 21));
        }

        [Fact]
        public void AValidCountParsesAndClamps()
        {
            Assert.Equal(3, SpawnCountPolicy.CountFrom("3", 21));
            Assert.Equal(1, SpawnCountPolicy.CountFrom("0", 21));
            Assert.Equal(21, SpawnCountPolicy.CountFrom("99", 21));
        }
    }
}
