using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The one decision behind lifting the ship-build UI's loading spinner: a 1270
    /// RefreshBlueprints event earns a 1274 Busy=false reply, and nothing else on 1270
    /// does. See <see cref="ShipBlueprintInteraction"/>.
    /// </summary>
    public class ShipBlueprintInteractionTests
    {
        [Fact]
        public void A_refresh_event_earns_a_reply()
        {
            // The client publishes RefreshBlueprints when the panel opens; that is the
            // one event this milestone answers, so the spinner can lift.
            Assert.True(ShipBlueprintInteraction.ShouldReplyToRefresh(1));
        }

        [Fact]
        public void Multiple_refresh_events_in_one_update_still_reply()
        {
            // One reply is enough (it is idempotent - Busy=false + empty list), but a
            // batch carrying several refreshes must still be answered, not dropped.
            Assert.True(ShipBlueprintInteraction.ShouldReplyToRefresh(3));
        }

        [Fact]
        public void An_update_with_no_refresh_event_is_not_answered()
        {
            // A 1270 update carrying only later-milestone events (item add/return,
            // save/rename/delete blueprint, ...) produces no 1274 reply here.
            Assert.False(ShipBlueprintInteraction.ShouldReplyToRefresh(0));
        }

        [Fact]
        public void The_reply_always_clears_busy()
        {
            // The reply EXISTS to clear the blocker; there is no server-side work to
            // wait on, so Busy is always false.
            Assert.False(ShipBlueprintInteraction.RepliedBusy);
        }
    }
}
