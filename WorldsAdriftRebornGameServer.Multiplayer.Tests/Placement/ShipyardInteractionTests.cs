using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The values that shape the placed shipyard console's "Craft" prompt. The one that
    /// fails SILENTLY if wrong is the radius: InteractiveObjectVisualizer.OnEnable finds
    /// the Craft entry but with radius 0 the prompt never appears - the exact trap the
    /// helm and metal-node interaction seeds document. Pin it non-zero.
    /// </summary>
    public class ShipyardInteractionTests
    {
        [Fact]
        public void Craft_radius_is_non_zero_or_no_prompt_appears()
        {
            Assert.True(ShipyardInteraction.CraftRadius > 0f,
                "a zero radius makes the console's Craft prompt invisible");
        }

        [Fact]
        public void Craft_time_to_use_is_non_negative()
        {
            Assert.True(ShipyardInteraction.CraftTimeToUse >= 0f);
        }
    }
}
