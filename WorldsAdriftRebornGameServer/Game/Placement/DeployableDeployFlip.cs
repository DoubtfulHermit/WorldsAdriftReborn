using System;
using System.Collections.Generic;
using System.Linq;
using Bossa.Travellers.Items;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Placement
{
    /// <summary>
    /// The thin glue for the shipyard FOLD-OUT completion flip (3.1): after a live placement
    /// has seeded 1205 <c>deployed=false</c> (so the client plays the <c>Shipyard_Deploy</c>
    /// panel/leg fold-out), this schedules the deferred flip back to <c>deployed=true</c> so a
    /// LATER checkout snaps to the finished pose.
    ///
    /// It runs on the main poll loop via <see cref="DeferredActions"/> (NOT a background
    /// timer), so enumerating the peer set here is on the same thread that mutates it. The flip
    /// does two things, in order:
    ///   1. updates the placed-shipyard ledger to deployed=true, so any peer that checks the
    ///      yard out AFTER the fold reads the snapped value from the serializer; and
    ///   2. best-effort pushes a live 1205 <c>SetDeployed(true)</c> to connected peers, exactly
    ///      as <see cref="Crafting.BuiltShipSpawner.PushDockedShipId"/> pushes the shared 1205
    ///      DockedShipId. (The client's ShipyardVisualizer reads Deployed once at checkout and
    ///      does not re-animate on this update, so the push cannot cause a second fold-out; the
    ///      ledger update is what makes late checkouts correct.)
    /// One-shot and per-entity - not a stream.
    /// </summary>
    internal static class DeployableDeployFlip
    {
        internal static void ScheduleShipyardFoldOut(long shipyardEntityId)
        {
            float seconds = Multiplayer.Placement.ShipyardDeployPolicy.DeploySeconds(
                Environment.GetEnvironmentVariable("WAREBORN_SHIPYARD_DEPLOY_SECONDS"));

            DeferredActions.After(seconds, () => FlipDeployed(shipyardEntityId));

            Console.WriteLine("[info] placement: shipyard " + shipyardEntityId
                + " seeded deployed=false to play the fold-out; will flip to deployed=true in "
                + seconds + "s so later checkouts snap.");
        }

        private static void FlipDeployed(long shipyardEntityId)
        {
            // Ledger first: this is what a LATER checkout reads, and the reason the flip
            // matters at all (ShipyardVisualizer does not listen for DeployedUpdated).
            PlacedShipyards.MarkDeployed(shipyardEntityId);

            int pushed = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                try
                {
                    ShipyardState.Update update = new ShipyardState.Update();
                    update.SetDeployed(true);
                    if (SendOPHelper.SendComponentUpdateOp(peer, shipyardEntityId,
                            new List<uint> { 1205 }, new List<object> { update }))
                    {
                        pushed++;
                    }
                }
                catch (Exception e)
                {
                    // A disconnected peer must not take the loop down; the ledger flip already
                    // makes every future checkout correct.
                    Console.WriteLine("[warning] placement: could not push shipyard " + shipyardEntityId
                        + " deployed=true to a peer: " + e.Message);
                }
            }

            Console.WriteLine("[info] placement: shipyard " + shipyardEntityId
                + " fold-out complete; flipped 1205 deployed=true (ledger + " + pushed + " peer push(es)).");
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers()
        {
            return PeerManager.Instance.playerState.Keys.ToList();
        }
    }
}
