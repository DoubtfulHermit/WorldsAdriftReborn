using System;
using Bossa.Travellers.Utilityslot;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * Relays 6910 UtilitySlotActivatedState to other players as a low-rate ON/OFF
     * EVENT, so they can see each other's glider deploy and tool-in-hand - the
     * thing that regressed the instant the per-frame relay was filtered off.
     *
     * WHY A HANDLER AND NOT THE RAW PATH. RelayToOtherPlayers forwards raw bytes
     * on arrival; for 6910 it is deliberately switched OFF
     * (MirrorSendPolicy.IsRelayedToOtherPlayers), because the client republishes
     * 6910 at ~170/s while a utility is active and relaying that byte-for-byte
     * bufferbloated the link and dropped a peer (RTT 24 ms -> 5 s, 2026-08-09).
     * But the ~170/s is HEALTH floats draining - the glider/tool VISUAL rides the
     * head/body/feet BOOLS, which flip rarely (VERIFIED: the generated
     * FinishAndSend_ResolveDiff clears unchanged fields, so a health frame carries
     * no bool and a deploy carries only the flipped bool).
     *
     * This handler gets the SAME already-deserialized update the framework hands
     * every registered component (see TransformState_Handler for the mechanism -
     * ComponentUpdateManager deserializes 6910 with the game's own vtable, and
     * 6910 is stored on the owner's entity because it is in
     * MirrorSendPolicy.AuthoritativeComponents/InjectedComponents, so the
     * ComponentMap lookup hits and this fires). It feeds UtilitySlotRelayFilter,
     * which drops every health-only frame and passes a bool transition exactly
     * once, then re-emits a BOOLS-ONLY update to the other players - reliably,
     * because a dropped transition never comes back and would leave the glider
     * stuck open or closed. That collapses ~170/s to a handful of events.
     *
     * OWNERSHIP GATE FIRST (rule 6): a client may only speak for its own entity.
     * The relayed payload is three bools with no cross-entity reference, so
     * re-addressing it to the sender's own entity - what every relay does - is
     * correct here (unlike 1231/1037/1211).
     *
     * DELIBERATELY DOES NOT CALL ApplyTo: the server's stored 6910 is only read to
     * re-seed a late joiner, and the seed default (all-inactive) is a safe view
     * until the next transition arrives. Seeding the LIVE bool state to late
     * joiners is a follow-up, not this change.
     */
    [RegisterComponentUpdateHandler]
    internal class UtilitySlotActivatedState_Handler : IComponentUpdateHandler<UtilitySlotActivatedState, UtilitySlotActivatedState.Update, UtilitySlotActivatedState.Data>
    {
        /// <summary>
        /// Per-entity change detector. One instance for the process: the handler
        /// is a singleton created once by ComponentUpdateManager's registration.
        /// </summary>
        private static readonly UtilitySlotRelayFilter Filter = new UtilitySlotRelayFilter();

        public UtilitySlotActivatedState_Handler() { Init(MirrorSendPolicy.UtilitySlotActivatedStateComponentId); }

        protected override void Init(uint ComponentId)
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate(ENetPeerHandle player, long entityId,
            UtilitySlotActivatedState.Update clientComponentUpdate, UtilitySlotActivatedState.Data serverComponentData)
        {
            ulong senderId = PeerIdentity.IdOf(player);

            // Rule 6: only relay a component the sender actually owns.
            if (!WorldsAdriftRebornGameServer.Players.Owns(senderId, entityId))
            {
                return;
            }

            bool? head = clientComponentUpdate.head.HasValue ? clientComponentUpdate.head.Value : (bool?)null;
            bool? body = clientComponentUpdate.body.HasValue ? clientComponentUpdate.body.Value : (bool?)null;
            bool? feet = clientComponentUpdate.feet.HasValue ? clientComponentUpdate.feet.Value : (bool?)null;

            UtilitySlotRelayDecision decision = Filter.Decide(entityId, head, body, feet);
            if (!decision.Relay)
            {
                return; // health-only frame, or nothing changed.
            }

            // A self-contained bools-only update: no health fields, so nothing of
            // the per-frame stream rides along.
            UtilitySlotActivatedState.Update relay = new UtilitySlotActivatedState.Update();
            relay.SetHead(decision.Head);
            relay.SetBody(decision.Body);
            relay.SetFeet(decision.Feet);

            byte[]? payload = SendOPHelper.SerializeComponentUpdatePayload(
                MirrorSendPolicy.UtilitySlotActivatedStateComponentId, relay);
            if (payload == null)
            {
                return;
            }

            foreach ((ulong targetPeer, long _) in WorldsAdriftRebornGameServer.Players.Others(senderId))
            {
                ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)targetPeer));
                if (target == null)
                {
                    continue;
                }

                // Reliable: this is a rare on/off transition, not the per-frame
                // stream 6910's policy still guards against. forceReliable keeps
                // that policy (Unreliable) intact for the raw path.
                SendOPHelper.SendRawComponentUpdateOp(
                    target, entityId, MirrorSendPolicy.UtilitySlotActivatedStateComponentId, payload, forceReliable: true);
            }
        }
    }
}
