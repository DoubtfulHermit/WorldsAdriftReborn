using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * Watches 190602 TransformState for a player who has fallen out of the world,
     * and nothing else.
     *
     * THIS IS THE TYPED PATH, AND IT ALREADY EXISTED. The packet loop hands every
     * inbound ComponentUpdate to ComponentUpdateManager.HandleComponentUpdate,
     * which finds the component's client vtable, deserializes it with the game's
     * own code, and looks for a registered handler. Until now no handler for
     * 190602 existed, so that work was done and the result thrown away - which is
     * why adding this costs nothing measurable. The alternative, hand-parsing the
     * bytes that RelayToOtherPlayers forwards, would have meant reimplementing
     * Improbable's wire format to learn something the game's own deserializer was
     * already telling us; see TransformSampleLogger for what that path looks like
     * when it is only a diagnostic.
     *
     * ONE PRECONDITION, AND IT IS SATISFIED: HandleComponentUpdate silently drops
     * any update it has no STORED component for
     * (ComponentMap[peer][entity][component]). 190602 is seeded on every player
     * entity by ComponentsSerializer, so the lookup hits. If that seed is ever
     * removed this handler stops being called and the fall floor quietly stops
     * working - which is exactly the failure mode the 1037 seed comment in
     * ComponentsSerializer warns about.
     *
     * DELIBERATELY DOES NOT CALL ApplyTo. The server's stored 190602 is only ever
     * used to re-seed the component, and findings-spawn.md is emphatic that a
     * re-seed of a live player's transform is an out-of-world drop. Keeping the
     * stored copy at the spawn value is the safer of the two wrong answers, and
     * changing it is a different piece of work.
     *
     * WHAT IT DOES NOT DO: send. It FEEDS the relay (RelayEmitter's ingest,
     * first thing after the ownership gate) but never talks to ENet itself;
     * under relay v2 the emitter puts coalesced 190602 on the wire at a fixed
     * cadence, and with WAREBORN_RELAY_V2=0 RelayToOtherPlayers forwards the
     * raw bytes exactly as before. Reading a component and mirroring it are
     * separate jobs and this is only the reading one.
     */
    [RegisterComponentUpdateHandler]
    internal class TransformState_Handler : IComponentUpdateHandler<TransformState, TransformState.Update, TransformState.Data>
    {
        /// <summary>
        /// Whether the "this player is parented" complaint has been made. It
        /// cannot happen on today's server and would arrive at transform rate if
        /// it ever did, so it is said once and then never again.
        /// </summary>
        private static bool _parentWarningSaid;

        public TransformState_Handler() { Init(MirrorSendPolicy.TransformStateComponentId); }

        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId,
            TransformState.Update clientComponentUpdate, TransformState.Data serverComponentData )
        {
            // Ownership gate FIRST (docs/multiplayer.md rule 6): a client may only
            // speak for its OWN entity. Without it a peer could publish a transform
            // for somebody else and have THAT player teleported home - or feed
            // movement into another player's relay stream. It moved above the
            // localPosition check when the relay ingest below arrived, because a
            // position-less update (a parent change, a reset event) is exactly
            // what the ingest must still see.
            if (!WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId))
            {
                return;
            }

            // The relay's ingest: judged (duplicate/jump drops), accepted state
            // merged for the cadence emitter, edge fields (parent, onReset)
            // preserved unconditionally. Same deserialization the fall floor
            // below reads - never a second pass. RelayToOtherPlayers no longer
            // touches 190602 under relay v2.
            WorldsAdriftRebornGameServer.Relay.ObserveTransform(PeerIdentity.IdOf(player), clientComponentUpdate);

            if (!clientComponentUpdate.localPosition.HasValue)
            {
                return;
            }

            // A PARENTED transform is expressed in its parent's LOCAL space, not
            // the world's - the client's own writer branches on exactly this
            // (LocalTransformUpdaterBehaviour publishes transform.localPosition
            // when Parent.HasValue and
            // transform.position.RemapUnityVectorToGlobalVector() when it does
            // not). Comparing a local y against a world floor would teleport
            // somebody who is standing on something.
            //
            // The generated writer only puts `parent` on the wire when it
            // CHANGES, so what this can report is an EDGE, not a state. Passing
            // the edge on and letting FallWatch remember it is the whole reason
            // this is a nullable rather than a bool: null means "this update said
            // nothing about a parent", which is what nearly every update says.
            //
            // Players are seeded parentless and nothing on this server ever gives
            // one a parent, so the transition below should never happen - hence
            // it is said out loud, once.
            bool? parentPresent = clientComponentUpdate.parent.HasValue
                ? clientComponentUpdate.parent.Value.HasValue
                : (bool?)null;

            if (parentPresent == true && !_parentWarningSaid)
            {
                _parentWarningSaid = true;
                Console.WriteLine("[warning] fall floor: entity " + entityId
                    + " published a PARENTED 190602. Its positions are local to its parent, not the "
                    + "world, so it cannot be checked against the floor and will not be rescued "
                    + "until it is unparented.");
            }

            Improbable.Collections.List<long> fixedPoint = clientComponentUpdate.localPosition.Value.fixedPointValues;
            if (fixedPoint == null || fixedPoint.Count < 3)
            {
                return;
            }

            // Q52.12 straight off the wire, no arithmetic: FixedPointPosition IS
            // this encoding, and FallPolicy's thresholds are in it too, so the
            // hot path never converts to metres at all.
            WorldsAdriftRebornGameServer.Falls.OnPlayerTransform(
                entityId,
                new FixedPointPosition(fixedPoint[0], fixedPoint[1], fixedPoint[2]),
                parentPresent);
        }
    }
}
