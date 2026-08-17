using Bossa.Travellers.Crew;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Crew;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * 6901 CrewClientInterfaceState - every crew ACTION a player can take.
     *
     * This is why crews needed no command channel. The schema presents crew
     * actions as SpatialOS commands, and this server cannot carry a command in
     * either direction - SendCommandRequest, SendCommandResponse and both
     * dispatcher callbacks are TODO stubs in the shim, and OpList has no command
     * ops at all. But 6901 also exposes them as EVENTS:
     *
     *   crewInterface.Update.TriggerInvitePlayer(id, name).FinishAndSend()
     *
     * and events ride component updates, which is the one transport that works.
     * 6901 has no data fields whatsoever - it exists ONLY to carry these events -
     * which is the giveaway that this was the intended client-to-server path.
     *
     * The events are transient, exactly like 1011's spawn reply and 2106's shot:
     * they are read straight off the incoming update with no ApplyTo/merge.
     *
     * ALL TRUST LIVES BEHIND CrewPolicy. Nothing here decides anything: a client
     * that hand-crafts a BootPlayer for a crew it does not lead reaches
     * CrewService.Boot exactly like an honest one, and is refused by the same
     * rule. That matters more here than in most handlers, because crew actions
     * affect OTHER players and so are a griefing surface.
     */
    [RegisterComponentUpdateHandler]
    internal class CrewClientInterfaceState_Handler
        : IComponentUpdateHandler<CrewClientInterfaceState, CrewClientInterfaceState.Update, CrewClientInterfaceState.Data>
    {
        public CrewClientInterfaceState_Handler() { Init(6901); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            CrewClientInterfaceState.Update clientComponentUpdate,
            CrewClientInterfaceState.Data serverComponentData)
        {
            // The actor is whoever owns this entity, NOT whoever the packet says.
            // A crew action names its target but never its author.
            string actorUid = ActorOf(entityId);
            if (actorUid.Length == 0)
            {
                // Their character uid has not arrived yet (it comes in 1088, after
                // checkout). They can be shown a crew but must never be written
                // into one, so this is refused rather than keyed on something
                // volatile that would vanish on relog.
                Console.WriteLine("[warning] crew: ignoring an action from entity " + entityId
                    + " because its character identity has not arrived yet.");
                return;
            }

            foreach (InvitePlayer invite in clientComponentUpdate.invitePlayer)
                Apply(actorUid, CrewService.Invite(actorUid, invite.playerId, invite.displayName, null), "invite");

            foreach (InvitePlayerWithSlot invite in clientComponentUpdate.invitePlayerWithSlot)
                Apply(actorUid, CrewService.Invite(actorUid, invite.playerId, invite.displayName, invite.slot), "invite-with-slot");

            foreach (AcceptInvite _ in clientComponentUpdate.acceptInvite)
                Apply(actorUid, CrewService.Accept(actorUid), "accept");

            foreach (RejectInvite _ in clientComponentUpdate.rejectInvite)
                Apply(actorUid, CrewService.Reject(actorUid), "reject");

            foreach (BootPlayer boot in clientComponentUpdate.bootPlayer)
                Apply(actorUid, CrewService.Boot(actorUid, boot.playerId), "boot");

            foreach (LeaveCrew _ in clientComponentUpdate.leaveCrew)
                Apply(actorUid, CrewService.Leave(actorUid), "leave");

            foreach (SearchPlayer search in clientComponentUpdate.searchPlayer)
                CrewSearch.Answer(actorUid, search.playerName, search.searchRequestId);

            // UseCrewBeacon is deliberately not handled yet. It is a teleport, and
            // teleports belong with the graduation work rather than being bolted
            // on here; a beacon that silently did nothing would be worse than one
            // the UI never offers.
            if (clientComponentUpdate.useCrewBeacon.Count > 0)
            {
                Console.WriteLine("[info] crew: " + actorUid
                    + " used a crew beacon; beacons are not implemented yet.");
                CrewPush.Feedback(actorUid, "Crew beacons are not available yet.", false);
            }
        }

        /// <summary>
        /// Pushes the result to everyone it changed, or tells the actor why not.
        ///
        /// A refusal pushes NO state, because none changed - but it must still
        /// send feedback, or the crew panel sits waiting for an answer that never
        /// comes, exactly as the inventory panel used to.
        /// </summary>
        private static void Apply(string actorUid, CrewOutcome outcome, string what)
        {
            if (outcome.Ok)
            {
                CrewPush.PushAll(outcome.Affected, what);
                CrewPush.Feedback(actorUid, outcome.Message, true);
                return;
            }

            Console.WriteLine("[info] crew: refused " + what + " from " + actorUid
                + ": " + outcome.Message);
            CrewPush.Feedback(actorUid, outcome.Message, false);
        }

        private static string ActorOf(long entityId)
        {
            string uid = CharacterOwnership.UidForEntity(entityId);
            return uid.Length == 0 ? string.Empty : CrewPersistence.Key(Guid.Parse(uid));
        }
    }
}
