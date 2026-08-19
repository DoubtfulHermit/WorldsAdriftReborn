using Bossa.Travellers.Interact;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Loot
{
    /// <summary>
    /// OPENS A CHEST. One event, echoed back to one player, in response to one
    /// interaction.
    ///
    /// THE THING THAT IS NOT OBVIOUS: the container's inventory panel does not open
    /// because the client holds the container's 1081, and it does not open because
    /// the server sets <c>inUseBy</c>. It opens because an <c>Interact</c> EVENT
    /// arrives on the container's own <c>1210 InteractiveState</c>. From the shipped
    /// client, <c>InWorldInventoryVisualiser.OnEnable</c>:
    ///
    /// <code>
    ///   _interactState.InteractTriggered.Add(OnObjectInteractionTriggered);
    ///   ...
    ///   if (interactEvent.playerEntityId == MyPlayerIdVisualizer.GetEntityId()
    ///       &amp;&amp; interactEvent.verb == InteractVerb.Inventory)
    ///   { SyncInventory(); DisplayInventoryUI(); }
    /// </code>
    ///
    /// So the round trip is: player presses E -&gt; client fires
    /// <c>TriggerInteractWithObject(container, Inventory)</c> on its OWN 1211 -&gt;
    /// <c>InteractAgentState_Handler</c> lands here -&gt; this echoes
    /// <c>Interact{Inventory, thatPlayer}</c> on the CONTAINER's 1210 -&gt; the
    /// visualiser opens <c>MainInventoryUIState.Storage</c>. It is the same shape as
    /// <c>PlacementService.OpenShipyardConsole</c>, which echoes 1005 on the
    /// shipyard; only the component and the event differ.
    ///
    /// WHY THE EVENT IS ADDRESSED TO ONE PEER. The echo carries the opening player's
    /// entity id and the client compares it against its own, so a broadcast would be
    /// ignored by everyone else anyway - but it would still cost every peer holding
    /// that chest a component update. One player opening a chest is not news.
    ///
    /// WHY <c>inUseBy</c> IS DELIBERATELY NOT SET. It is tempting, and it is a trap.
    /// <c>OnObjectInUseByUpdated</c> pops <c>MainInventoryUIState</c> whenever the
    /// PREVIOUS holder was the local player - so setting the field on open and
    /// clearing it on close would need a matching close signal this server does not
    /// have, and getting the pairing wrong closes the panel under the player's
    /// cursor. The field's only other job is the open/close Wwise one-shot. Phase 2
    /// can add the handshake when there is a close event to hang it on; until then
    /// nothing is lost but a sound.
    ///
    /// MULTIPLAYER-SAFE: event-driven, one component update per E press, no per-frame
    /// state, no relay, nothing broadcast. A no-op for any target that is not a
    /// registered container.
    /// </summary>
    internal static class LootService
    {
        private const uint InteractiveStateComponentId = 1210;

        /// <summary>
        /// Answers a completed <c>Inventory</c> interaction on a loot container by
        /// echoing the event the client needs to open the panel. Returns true when
        /// the echo was sent.
        /// </summary>
        internal static bool OpenContainer(ENetPeerHandle peer, long playerEntityId, long containerEntityId)
        {
            if (!LootContainerLedger.IsContainer(containerEntityId))
            {
                // Something else entirely - a ship trunk, a prop, a mistake. Not our
                // event to answer, and answering it would put an Interact event on a
                // component that may not even be there.
                return false;
            }

            // Belt and braces: the 1081 serve stocks a container on checkout, but a
            // client that asked for 1210 and not 1081 would otherwise open an empty
            // chest. Idempotent, so this cannot re-roll anything.
            LootStock.Ensure(containerEntityId);

            InteractiveState.Update update = new InteractiveState.Update();
            update.AddInteract(new Interact(
                InteractVerb.Inventory,
                new EntityId(playerEntityId),
                // characterUid: the client's handler reads only verb and
                // playerEntityId, and this server has no uid at this seam, so an
                // empty string is honest rather than a fabricated identity.
                ""));

            bool sent = SendOPHelper.SendComponentUpdateOp(
                peer, containerEntityId,
                new List<uint> { InteractiveStateComponentId },
                new List<object> { update });

            if (sent)
            {
                Console.WriteLine("[loot] player " + playerEntityId + " opened container "
                    + containerEntityId + " ('" + (LootContainerLedger.KeyOf(containerEntityId) ?? "?")
                    + "'); echoed Interact(Inventory) on its 1210.");
            }
            else
            {
                Console.WriteLine("[warning] [loot] player " + playerEntityId
                    + " interacted with container " + containerEntityId
                    + " but the 1210 Interact echo failed to send; their panel will not open."
                    + " The prompt is not a lie - the echo is - so this is a send failure,"
                    + " not a missing component.");
            }

            return sent;
        }
    }
}
