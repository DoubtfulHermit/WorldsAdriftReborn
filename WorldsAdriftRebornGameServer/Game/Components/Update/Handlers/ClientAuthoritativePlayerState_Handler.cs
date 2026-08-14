using System.Collections.Generic;
using Bossa.Travellers.Player;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Components.Update.Handlers
{
    /*
     * Watches 1073 ClientAuthoritativePlayerState for two customers: the
     * TELEPORT ACK, and the relay's movement ingest (RelayEmitter), which this
     * handler feeds with the already-deserialized update so 1073 is never
     * deserialized twice.
     *
     * TeleportTransformVisualizer is the only 190607 consumer that can enable on
     * this server, and when it applies a teleport it writes the executed request
     * number into this component's lastExecutedRequest field. That write is the
     * server's only evidence a teleport actually happened - there is no other
     * channel, and the server never sees the client's transform except as
     * opaque relayed bytes.
     *
     * It is also why the parentless path is the cheap one: the ack lands on a
     * component the client is ALREADY granted authority over
     * (MirrorSendPolicy.AuthoritativeComponents), so teleport needs no new
     * authority grant at all. The expensive path acks on 190606, which we do not
     * grant and would have to.
     *
     * HOT PATH. ClientAuthoritativePlayerMovement republishes 1073 every tick
     * with the player's bone and relative-position data. The teleport-ack
     * branch stays free on those ticks (its Option is empty); the relay ingest
     * is the stream's actual consumer now and is a few comparisons plus a
     * dictionary hit per update, no allocation on the accept path beyond the
     * pending-update merge. The reflection work in
     * ComponentUpdateManager.HandleComponentUpdate already ran for every 1073
     * update before this existed.
     *
     * Deliberately does NOT call ApplyTo: the server's stored 1073 is only ever
     * used to re-seed the component, where the client's live bone bytes and
     * relative position would be stale noise.
     */
    [RegisterComponentUpdateHandler]
    internal class ClientAuthoritativePlayerState_Handler : IComponentUpdateHandler<ClientAuthoritativePlayerState, ClientAuthoritativePlayerState.Update, ClientAuthoritativePlayerState.Data>
    {
        public ClientAuthoritativePlayerState_Handler() { Init(1073); }

        protected override void Init( uint ComponentId )
        {
            this.ComponentId = ComponentId;
        }

        public override void HandleUpdate( ENetPeerHandle player, long entityId,
            ClientAuthoritativePlayerState.Update clientComponentUpdate, ClientAuthoritativePlayerState.Data serverComponentData )
        {
            // Ownership gate FIRST now (docs/multiplayer.md rule 6): a client
            // may only speak for its OWN entity. It used to sit after the
            // teleport-ack check because the ack was this handler's only
            // customer; the relay ingest below consumes EVERY 1073, so the gate
            // has to come before anything reads the update. Without it, a peer
            // could ack another player's teleport and push that player's request
            // counter forward - or feed movement into another player's relay
            // stream.
            if (!WorldsAdriftRebornGameServer.Players.Owns(PeerIdentity.IdOf(player), entityId))
            {
                return;
            }

            // The relay's ingest: timestamp/position judged (drops, staleness
            // metric), accepted state merged for the cadence emitter. This is
            // the deserialization the manager already did, used a second time
            // rather than done a second time - RelayToOtherPlayers no longer
            // touches 1073 under relay v2.
            WorldsAdriftRebornGameServer.Relay.ObservePlayerState(PeerIdentity.IdOf(player), clientComponentUpdate);

            // Aboard-detection. A player on a deck is not parented; the client
            // reports which entity they stand on via 1073 relativeTo (VERIFIED:
            // ClientAuthoritativePlayerMovement.CollectDataHighFrequency sets
            // relativeTo = the ground object's entity id and relativeBias = 1 when
            // attached, InvalidEntityId / 0 when free). Those two fields arrive only
            // when they CHANGE, so the tracker accumulates them and decides "aboard
            // ship X" against the ships this server spawned. Nothing here depends on
            // how a ship moves, which is why it can exist ahead of the flight work.
            Multiplayer.AboardSample aboardSample = new Multiplayer.AboardSample(
                clientComponentUpdate.relativeTo.HasValue,
                clientComponentUpdate.relativeTo.HasValue ? clientComponentUpdate.relativeTo.Value.Id : 0L,
                clientComponentUpdate.relativeBias.HasValue,
                clientComponentUpdate.relativeBias.HasValue ? clientComponentUpdate.relativeBias.Value : 0f,
                clientComponentUpdate.isRelativeToShip.HasValue,
                clientComponentUpdate.isRelativeToShip.HasValue
                    && clientComponentUpdate.isRelativeToShip.Value.HasValue
                    && clientComponentUpdate.isRelativeToShip.Value.Value);

            Multiplayer.AboardTransition aboard =
                WorldsAdriftRebornGameServer.Aboard.Observe(PeerIdentity.IdOf(player), aboardSample);
            if (aboard.Change != Multiplayer.AboardChange.None)
            {
                Console.WriteLine("[info] player entity " + entityId + " " + aboard + ".");
            }

            // A terrain relativeTo is the authoritative coordinate-frame label for
            // positionRelative. The field is sparse (only sent when it changes), so
            // ResourceInterest remembers the last island until another terrain id is
            // observed. Ship/deck ids are ignored here and handled as global poses below.
            if (clientComponentUpdate.relativeTo.HasValue)
            {
                WorldsAdriftRebornGameServer.ResourceInterest.ObserveRelativeTo(
                    player, clientComponentUpdate.relativeTo.Value.Id);
            }

            if (clientComponentUpdate.positionRelative.HasValue)
            {
                Improbable.Math.Vector3f p = clientComponentUpdate.positionRelative.Value;
                long? ship = WorldsAdriftRebornGameServer.Aboard.ShipOf(PeerIdentity.IdOf(player));
                if (ship.HasValue)
                {
                    Multiplayer.FixedPointPosition basePos;
                    if (!WorldsAdriftRebornGameServer.Flight.TryGetFlownPose(ship.Value, out basePos, out _))
                    {
                        basePos = WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(ship.Value);
                    }
                    WorldsAdriftRebornGameServer.ResourceInterest.ObserveGlobalPosition(
                        player,
                        Multiplayer.FixedPointPosition.FromMetres(
                            basePos.MetresX + p.X,
                            basePos.MetresY + p.Y,
                            basePos.MetresZ + p.Z),
                        "aboard ship " + ship.Value);
                }
                else
                {
                    WorldsAdriftRebornGameServer.ResourceInterest.ObserveIslandLocalPosition(player, p.X, p.Y, p.Z);
                }
            }

            // Carry echo. The client-side ship carry
            // (ClientAuthoritativePlayerMovement.RepositionRelativeToGroundedObject)
            // arms only when RelativePathFollower != null, which is set ONLY by
            // HandleRelativeToUpdate - and that fires ONLY on a RECEIVED 1073
            // relativeTo. The client's own Send() never fires it locally, and this
            // custom server otherwise never echoes a worker its own authoritative
            // update, so the owner never receives its own relativeTo and the carry
            // never arms. Echo it back when the exact contact object changes, and
            // disarm only when the canonical aboard tracker confirms a real leave.
            //
            // MINIMAL ON PURPOSE: only relativeTo, and only when it CHANGES. The
            // player is authoritative over 1073's position/bone fields and
            // republishes them every tick; echoing those would fight its own
            // prediction and rubber-band it. relativeTo alone carries no position -
            // the repositioner recomputes the relative offset locally from live
            // transforms - so a bare relativeTo echo arms the carry without moving
            // anyone. The exact reported id is echoed (never a ship root): the
            // client's RelativeGameObject setter only arms when the echoed id
            // resolves to the SAME ground object it already chose.
            if (WorldsAdriftRebornGameServer.CarryEchoEnabled)
            {
                Multiplayer.CarryEchoDecision echo = WorldsAdriftRebornGameServer.CarryEcho.Observe(
                    PeerIdentity.IdOf(player),
                    aboard,
                    clientComponentUpdate.relativeTo.HasValue,
                    clientComponentUpdate.relativeTo.HasValue ? clientComponentUpdate.relativeTo.Value.Id : 0L,
                    WorldsAdriftRebornGameServer.Aboard.ShipOf(PeerIdentity.IdOf(player)).HasValue);

                if (echo.ShouldEcho)
                {
                    ClientAuthoritativePlayerState.Update carryEcho = new ClientAuthoritativePlayerState.Update();
                    carryEcho.SetRelativeTo(new EntityId(echo.RelativeTo));

                    SendOPHelper.SendComponentUpdateOp(
                        player, entityId,
                        new List<uint> { 1073 },
                        new List<object> { carryEcho });

                    Console.WriteLine("[info] carry-echo: sent owner entity " + entityId
                        + " its own relativeTo " + echo.RelativeTo
                        + " back to update the moving-ground carry.");
                }
            }

            if (!clientComponentUpdate.lastExecutedRequest.HasValue)
            {
                return;
            }

            WorldsAdriftRebornGameServer.Teleports.OnAck(
                player, entityId, clientComponentUpdate.lastExecutedRequest.Value);
        }
    }
}
