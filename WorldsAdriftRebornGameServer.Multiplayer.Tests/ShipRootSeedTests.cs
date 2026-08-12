using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The 8066 ShipRootState seed DECISION - isRoot, and which hull shipRoot
    /// names - kept pure so it is asserted without the game's Option&lt;EntityId&gt;.
    /// VERIFIED shape: ShipRootStateData { Option&lt;EntityId&gt; shipRoot; bool isRoot }.
    /// </summary>
    public class ShipRootSeedTests
    {
        [Fact]
        public void A_bolted_on_part_points_its_ship_root_at_the_hull_and_is_not_the_root()
        {
            // The Helm's 8066: what makes it a member of the ship rather than a
            // free entity that happens to sit on the deck. ShipPartVisualizer
            // .ShipEntityId returns exactly this shipRoot value.
            ShipRootSeed part = ShipRootSeed.Part(42);

            Assert.False(part.IsRoot);
            Assert.True(part.HasShipRoot);
            Assert.Equal(42, part.ShipRootEntityId);
        }

        [Fact]
        public void The_hull_is_the_root_and_points_its_ship_root_at_itself()
        {
            // isRoot=true, and a self-pointing shipRoot so that resolving any
            // member's shipRoot lands on the hull whether it started from a part or
            // from the hull.
            ShipRootSeed root = ShipRootSeed.Root(7);

            Assert.True(root.IsRoot);
            Assert.True(root.HasShipRoot);
            Assert.Equal(7, root.ShipRootEntityId);
        }
    }
}
