namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// The one decision behind the ship-build UI's loading spinner, kept out of the
    /// component handler so it can be asserted natively.
    ///
    /// THE SPINNER. The FRAME DESIGNS + SHIP BLUEPRINTS panel sits under a full-panel
    /// LoadingInputBlocker (<c>ShipSchematicsList._loadingInputBlocker</c>) bound to
    /// the 1274 <c>GsimShipBlueprintInteractionState.Busy</c> property. On open,
    /// <c>ShipCraftingUI.Activate()</c> -&gt; <c>TriggerShipBlueprintsRefresh()</c>
    /// sets Busy true LOCALLY and publishes a <c>RefreshBlueprints</c> event on the
    /// player's 1270 <c>PlayerShipBlueprintInteractionState</c> writer. Real
    /// SpatialOS answered with a 1274 update carrying the catalogue and Busy=false;
    /// this server has no such reply, so the blocker never lifts. The fix is a
    /// per-player, event-driven reply: when a 1270 update carries a RefreshBlueprints
    /// event, send the sender ONE 1274 update with Busy=false and an empty blueprint
    /// list (empty is the correct catalogue for a new player).
    ///
    /// MULTIPLAYER SAFETY: this is a reply to a client COMMAND, not a stream. It is
    /// sent only to the peer that asked, only about that peer's OWN player entity,
    /// and only when a RefreshBlueprints event actually arrives - never per frame and
    /// never relayed to another player's mirror. 1270/1208 are filtered out of the
    /// raw cross-entity relay (<see cref="MirrorSendPolicy.IsRelayedToOtherPlayers"/>).
    /// </summary>
    public static class ShipBlueprintInteraction
    {
        /// <summary>
        /// The Busy value the server writes back on 1274. Always false: the reply
        /// EXISTS to clear the spinner, and there is no server-side work to wait on -
        /// the blueprint list is produced synchronously (empty, this milestone).
        /// </summary>
        public const bool RepliedBusy = false;

        /// <summary>
        /// Whether an inbound 1270 update warrants a 1274 reply. It does exactly when
        /// the client asked for a refresh. Everything else on 1270 (add/return item,
        /// save/rename/delete blueprint, autofill, ...) is a later milestone and
        /// produces no reply here, so an unrelated 1270 update never triggers a send.
        /// </summary>
        public static bool ShouldReplyToRefresh(int refreshBlueprintsEventCount)
        {
            return refreshBlueprintsEventCount > 0;
        }
    }
}
