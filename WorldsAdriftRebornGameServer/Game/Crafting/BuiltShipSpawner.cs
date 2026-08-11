using System;
using System.Collections.Generic;
using System.Linq;
using Bossa.Travellers.Items;
using Improbable;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// PHASE 3, THE SPAWN. Turns a completed ship-blueprint build into a real,
    /// boardable ship hull+deck standing next to the shipyard that built it - the
    /// SAME kind of world entity as the proven static test ship
    /// (<see cref="Multiplayer.WorldEntities.ShipFrame"/> + <c>Deck01</c>), but
    /// triggered by build completion, positioned next to the shipyard, and with the
    /// hull geometry taken from the player's saved design.
    ///
    /// HOW IT REUSES THE PROVEN MACHINERY. It follows the runtime-spawn path the
    /// placed shipyard already proves (<c>Placement.PlacementService.SpawnPlacedDeployable</c>):
    /// register a <see cref="WorldEntity"/>, allocate its shared entity id once, then
    /// broadcast AssetLoadRequest -> AddEntity -> the all-or-nothing seed batch to every
    /// connected peer. The hull's seed set is EXACTLY the test hull's
    /// (<see cref="BuiltShipPlacement.HullSeedComponents"/>); the deck's is the test
    /// deck's readers (<see cref="BuiltShipPlacement.DeckSeedComponents"/>). The only
    /// per-build difference is the 1209 hull bytes, which the serializer resolves from
    /// <see cref="BuiltShips"/> keyed by the hull's entity id.
    ///
    /// MULTIPLAYER CLASSIFICATION: a ONE-TIME AddEntity + static seeds per built ship,
    /// then ordinary interest serving - NOT a per-frame re-seed, NOT a high-rate relay.
    /// The at-rest 1130 is a single seed (served by the serializer from the hull's own
    /// registered position), not a stream; no motion is published here (flight is a
    /// later phase). A built ship is a SHARED world entity every peer sees - correct for
    /// a ship, unlike the per-player blueprint state - so the same id is broadcast to
    /// all peers and every peer checks the same hull out.
    /// </summary>
    internal static class BuiltShipSpawner
    {
        /// <summary>
        /// Spawns the built ship for a completed build on <paramref name="shipyardEntityId"/>
        /// using the player's <paramref name="savedHullBytes"/> for the hull shape.
        /// Returns the allocated hull entity id, or null if nothing could be spawned.
        ///
        /// The hull bytes are validated with <see cref="ShipPlanModel.TryDecode"/> first;
        /// an invalid or empty blob falls back to <see cref="ShipHull.MinimumHullData"/>
        /// (and logs), because 1209's client-side <c>ShipPlan.Load</c> THROWS on a bad
        /// blob - into the client's log, where we cannot see it - and the visible result
        /// is a hull that renders nothing.
        /// </summary>
        internal static long? Spawn(long shipyardEntityId, byte[] savedHullBytes)
        {
            byte[] hullBytes = ResolveValidHullBytes(savedHullBytes);

            FixedPointPosition shipyardPos;
            WorldEntity? shipyard = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(shipyardEntityId);
            if (shipyard != null)
            {
                shipyardPos = shipyard.Position;
            }
            else
            {
                // A build whose shipyard is not a registered world entity (should not
                // happen for a placed shipyard, which IS registered) - spawn next to the
                // default static-ship spot rather than at the origin, and say so.
                shipyardPos = Multiplayer.WorldEntities.ShipFramePosition();
                Console.WriteLine("[warn] built-ship spawn: shipyard entity " + shipyardEntityId
                    + " is not a registered world entity; placing the built ship next to the default"
                    + " ship spot " + shipyardPos + " instead of next to the console.");
            }

            int sequence = BuiltShips.NextSequence();
            FixedPointPosition hullPos = BuiltShipPlacement.HullNextTo(shipyardPos);
            FixedPointPosition deckPos = BuiltShipPlacement.DeckOn(hullPos);

            WorldEntity hull = new WorldEntity(
                BuiltShipPlacement.HullKey(sequence),
                Multiplayer.WorldEntities.ShipFrameAssetName,
                Multiplayer.WorldEntities.DefaultAssetContext,
                hullPos,
                seedComponents: BuiltShipPlacement.HullSeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer);

            WorldEntity deck = new WorldEntity(
                BuiltShipPlacement.DeckKey(sequence),
                Multiplayer.Deck.AssetName,
                Multiplayer.WorldEntities.DefaultAssetContext,
                deckPos,
                seedComponents: BuiltShipPlacement.DeckSeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer);

            WorldsAdriftRebornGameServer.WorldEntities.Register(hull);
            WorldsAdriftRebornGameServer.WorldEntities.Register(deck);

            // Allocate the shared ids once (EntityIdFor is what allocates), then record
            // the per-ship truth the serializer reads back BEFORE any peer can check the
            // entities out: the hull's own bytes (1209) and that the deck is a deck (1099).
            long hullEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(hull);
            long deckEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(deck);
            BuiltShips.RegisterHull(hullEntityId, hullBytes);
            BuiltShips.RegisterDeck(deckEntityId);

            // ONE SHIP PER SHIPYARD: record which yard produced this hull, so its 1205
            // ShipyardState.DockedShipId reports it and a further CRAFT on that yard is
            // refused until it is cleared (see the 1270 StartCrafting gate + the undock
            // trigger). Recorded BEFORE the 1205 push below so the serve branch agrees.
            BuiltShips.SetDocked(shipyardEntityId, hullEntityId);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                bool hullOk = BroadcastToPeer(peer, hullEntityId, hull);
                bool deckOk = BroadcastToPeer(peer, deckEntityId, deck);
                if (hullOk && deckOk)
                {
                    reached++;
                }
                // Tell every peer holding the shipyard in interest that it is now docked
                // to this hull, so ShipyardVisualizer.OnDockedShipChanged fires live
                // rather than only on a fresh checkout. dockedShipId is SHARED world
                // truth (the shipyard is a shared entity), so this goes to all peers -
                // a one-time event, not a stream. Best-effort: a drop is corrected by the
                // serve branch on the next checkout.
                PushDockedShipId(peer, shipyardEntityId, hullEntityId);
            }

            Console.WriteLine("[info] built-ship spawn: BUILT ship for shipyard " + shipyardEntityId
                + " as hull entity " + hullEntityId + " + deck entity " + deckEntityId
                + " at " + hullPos + " (" + hullBytes.Length + "-byte hull, "
                + BuiltShipPlacement.HullSeedComponents.Count + " hull seeds + "
                + BuiltShipPlacement.DeckSeedComponents.Count + " deck seeds), sent to "
                + reached + " peer(s). This is build #" + BuiltShips.Count + " this session.");

            if (reached == 0)
            {
                Console.WriteLine("[warn] built-ship spawn: hull " + hullEntityId
                    + " was registered but reached no fully-connected peer; late joiners will still"
                    + " get it via the connect-time spawn plan.");
            }

            return hullEntityId;
        }

        private static byte[] ResolveValidHullBytes(byte[] savedHullBytes)
        {
            byte[] bytes = BuiltShipPlacement.ResolveHullBytes(savedHullBytes, out bool usedFallback);
            if (usedFallback)
            {
                Console.WriteLine("[warn] built-ship spawn: the player's saved hull bytes ("
                    + (savedHullBytes?.Length ?? 0) + " byte(s)) did not decode as a ShipPlan;"
                    + " falling back to the " + bytes.Length
                    + "-byte minimum hull so the ship still renders.");
            }
            return bytes;
        }

        /// <summary>
        /// Sends one entity to one peer the proven way: AssetLoadRequest (so the client
        /// has the prefab loaded), AddEntity (create it), then the all-or-nothing seed
        /// push with failOnComponentInitError TRUE. Mirrors
        /// <c>PlacementService.BroadcastToPeer</c>, fanned out to the live peer set.
        /// </summary>
        private static bool BroadcastToPeer(ENetPeerHandle peer, long entityId, WorldEntity registration)
        {
            SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", registration.AssetName, registration.AssetContext);

            if (!SendOPHelper.SendAddEntityOP(peer, entityId, registration.AssetName, registration.AssetContext))
            {
                Console.WriteLine("[error] built-ship spawn: failed to send AddEntityOp for entity " + entityId + " to a peer.");
                return false;
            }

            List<Structs.Structs.InterestOverride> seeds = registration.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1))
                .ToList();

            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true))
            {
                Console.WriteLine("[error] built-ship spawn: entity " + entityId
                    + " was created on a peer but its seed components were dropped; it will render inert.");
                return false;
            }

            return true;
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers()
        {
            return PeerManager.Instance.playerState.Keys.ToList();
        }

        /// <summary>
        /// Pushes a live 1205 ShipyardState update carrying only DockedShipId to one
        /// peer, so its ShipyardVisualizer learns the yard is docked without waiting for
        /// a re-checkout. Shared by the spawn path (dockedShipId = the new hull) and,
        /// via <see cref="PushUndocked"/>, the undock trigger (dockedShipId = invalid 0).
        /// </summary>
        internal static void PushDockedShipId(ENetPeerHandle peer, long shipyardEntityId, long dockedHullEntityId)
        {
            ShipyardState.Update update = new ShipyardState.Update();
            update.SetDockedShipId(new EntityId(dockedHullEntityId));
            SendOPHelper.SendComponentUpdateOp(peer, shipyardEntityId,
                new List<uint> { 1205 }, new List<object> { update });
        }

        /// <summary>
        /// Broadcasts "this shipyard no longer has a docked ship" (1205 DockedShipId =
        /// invalid) to every connected peer. Called by the undock trigger after clearing
        /// the ledger association, so the client drops the docked ship and CRAFT is
        /// allowed again.
        /// </summary>
        internal static void PushUndocked(long shipyardEntityId)
        {
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                PushDockedShipId(peer, shipyardEntityId, 0);
            }
        }
    }
}
