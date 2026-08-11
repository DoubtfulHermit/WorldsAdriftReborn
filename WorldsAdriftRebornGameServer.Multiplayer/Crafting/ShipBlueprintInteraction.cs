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
        /// Whether an inbound 1270 update warrants RE-SEEDING THE BLUEPRINT LIST on the
        /// 1274 reply. That is true only for an actual refresh (the panel open): the
        /// list is reset to empty then. A non-refresh command (SetBlueprintId, add/return
        /// item, ...) still gets a Busy=false reply (<see cref="ShouldReplyBusyFalse"/>)
        /// but must NOT churn the list model, so it does not re-seed the list.
        /// </summary>
        public static bool ShouldReplyToRefresh(int refreshBlueprintsEventCount)
        {
            return refreshBlueprintsEventCount > 0;
        }

        /// <summary>
        /// The per-kind counts of the eleven 1270 commands in one update. EVERY one of
        /// them is wrapped by the client in <c>LockOnBusyState</c>
        /// (PlayerShipBlueprintInteractionBehaviour), which sets the client-local
        /// <c>BusyModel</c> TRUE and then waits for a 1274 <c>BusyUpdated</c> event to
        /// clear it. Both LoadingInputBlockers (the left SHIP BLUEPRINTS list and the
        /// centre overlay) are bound to that one <c>BusyModel</c>.
        /// </summary>
        public readonly struct BlueprintCommandCounts
        {
            public BlueprintCommandCounts(
                int addItem, int returnItem, int startCrafting, int setBlueprintId,
                int refreshBlueprints, int saveBlueprint, int renameBlueprint,
                int deleteBlueprint, int autofillBlueprint, int returnAllItems,
                int setSchematicEnabled)
            {
                AddItem = addItem;
                ReturnItem = returnItem;
                StartCrafting = startCrafting;
                SetBlueprintId = setBlueprintId;
                RefreshBlueprints = refreshBlueprints;
                SaveBlueprint = saveBlueprint;
                RenameBlueprint = renameBlueprint;
                DeleteBlueprint = deleteBlueprint;
                AutofillBlueprint = autofillBlueprint;
                ReturnAllItems = returnAllItems;
                SetSchematicEnabled = setSchematicEnabled;
            }

            public int AddItem { get; }
            public int ReturnItem { get; }
            public int StartCrafting { get; }
            public int SetBlueprintId { get; }
            public int RefreshBlueprints { get; }
            public int SaveBlueprint { get; }
            public int RenameBlueprint { get; }
            public int DeleteBlueprint { get; }
            public int AutofillBlueprint { get; }
            public int ReturnAllItems { get; }
            public int SetSchematicEnabled { get; }

            /// <summary>Total LockOnBusyState-wrapped commands in the update.</summary>
            public int Locking =>
                AddItem + ReturnItem + StartCrafting + SetBlueprintId + RefreshBlueprints +
                SaveBlueprint + RenameBlueprint + DeleteBlueprint + AutofillBlueprint +
                ReturnAllItems + SetSchematicEnabled;
        }

        /// <summary>
        /// Whether an inbound 1270 update must be answered with a 1274 Busy=false. It
        /// must whenever it carries ANY of the eleven commands, because the client locked
        /// BusyModel on ALL of them and only a 1274 BusyUpdated clears it. Answering only
        /// RefreshBlueprints (the old behaviour) left the SetBlueprintId that fires when a
        /// hull frame is selected - hulls and blueprints are mutually exclusive, so
        /// selecting a hull clears the blueprint id - with BusyModel stuck true, so both
        /// blockers stayed up and EDIT/SAVE/everything was eaten. An empty update (no
        /// command) needs no reply.
        /// </summary>
        public static bool ShouldReplyBusyFalse(BlueprintCommandCounts counts)
        {
            return counts.Locking > 0;
        }
    }
}
