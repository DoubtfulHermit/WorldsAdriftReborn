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

        [Fact]
        public void An_empty_update_earns_no_busy_reply()
        {
            // No command => no BusyModel was locked => nothing to clear.
            var counts = new ShipBlueprintInteraction.BlueprintCommandCounts(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            Assert.Equal(0, counts.Locking);
            Assert.False(ShipBlueprintInteraction.ShouldReplyBusyFalse(counts));
        }

        [Theory]
        // one command of each kind, in constructor order:
        // addItem, returnItem, startCrafting, setBlueprintId, refreshBlueprints,
        // saveBlueprint, renameBlueprint, deleteBlueprint, autofillBlueprint,
        // returnAllItems, setSchematicEnabled
        [InlineData(1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)] // AddItem
        [InlineData(0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0)] // ReturnItem
        [InlineData(0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0)] // StartCrafting
        [InlineData(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0)] // SetBlueprintId  <- the stuck-busy bug
        [InlineData(0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0)] // RefreshBlueprints
        [InlineData(0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0)] // SaveBlueprint
        [InlineData(0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0)] // RenameBlueprint
        [InlineData(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0)] // DeleteBlueprint
        [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0)] // AutofillBlueprint
        [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0)] // ReturnAllItems
        [InlineData(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1)] // SetSchematicEnabled
        public void Every_lock_on_busy_command_earns_a_busy_false_reply(
            int addItem, int returnItem, int startCrafting, int setBlueprintId,
            int refreshBlueprints, int saveBlueprint, int renameBlueprint,
            int deleteBlueprint, int autofillBlueprint, int returnAllItems,
            int setSchematicEnabled)
        {
            // EVERY one of the eleven 1270 commands is LockOnBusyState-wrapped on the
            // client, so each on its own must earn a Busy=false reply. Missing any one
            // (as the old refresh-only handler missed SetBlueprintId) leaves BusyModel
            // stuck true and both LoadingInputBlockers eat all input.
            var counts = new ShipBlueprintInteraction.BlueprintCommandCounts(
                addItem, returnItem, startCrafting, setBlueprintId, refreshBlueprints,
                saveBlueprint, renameBlueprint, deleteBlueprint, autofillBlueprint,
                returnAllItems, setSchematicEnabled);
            Assert.Equal(1, counts.Locking);
            Assert.True(ShipBlueprintInteraction.ShouldReplyBusyFalse(counts));
        }

        [Fact]
        public void Set_blueprint_id_alone_clears_busy_but_is_not_a_refresh()
        {
            // The exact frame-select case: SetBlueprintId fires without a refresh. It must
            // clear Busy (ShouldReplyBusyFalse true) but must NOT re-seed the blueprint
            // list (ShouldReplyToRefresh false), so selecting a frame does not churn the list.
            var counts = new ShipBlueprintInteraction.BlueprintCommandCounts(
                0, 0, 0, setBlueprintId: 1, 0, 0, 0, 0, 0, 0, 0);
            Assert.True(ShipBlueprintInteraction.ShouldReplyBusyFalse(counts));
            Assert.False(ShipBlueprintInteraction.ShouldReplyToRefresh(counts.RefreshBlueprints));
        }

        [Fact]
        public void Locking_sums_all_kinds()
        {
            var counts = new ShipBlueprintInteraction.BlueprintCommandCounts(
                1, 2, 0, 3, 1, 0, 0, 0, 0, 0, 4);
            Assert.Equal(11, counts.Locking);
            Assert.True(ShipBlueprintInteraction.ShouldReplyBusyFalse(counts));
        }
    }
}
