using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
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

            FixedPointPosition hullPos = BuiltShipPlacement.HullNextTo(shipyardPos);

            (long hullEntityId, WorldEntity hull, WorldEntity deck, long deckEntityId) =
                RegisterBuiltShip(hullPos, hullBytes);

            // Persist the built ship so it reappears next boot - re-spawned as a world
            // entity in the connect-time spawn plan, exactly like this runtime spawn.
            WorldStatePersistence.RecordBuiltShip(hullPos, hullBytes);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                bool hullOk = BroadcastToPeer(peer, hullEntityId, hull);
                bool deckOk = BroadcastToPeer(peer, deckEntityId, deck);
                if (hullOk && deckOk)
                {
                    reached++;
                }
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

        /// <summary>
        /// Re-creates ONE persisted ship at boot from its stored hull position and hull
        /// bytes, via the SAME <see cref="RegisterBuiltShip"/> core the runtime build
        /// uses, so a restored ship is byte-identical and the spawn plan serves it to
        /// every joining client. Returns the allocated hull entity id, or null if the
        /// stored bytes could not be resolved. Does not broadcast (no peers at boot) and
        /// does not re-persist (the record it came from is already on disk).
        /// </summary>
        internal static long? Restore(BuiltShipRecord record)
        {
            byte[] hullBytes = ResolveValidHullBytes(record.HullBytes);
            FixedPointPosition hullPos = record.HullPosition();

            (long hullEntityId, WorldEntity hull, WorldEntity _, long deckEntityId) =
                RegisterBuiltShip(hullPos, hullBytes);

            Console.WriteLine("[info] built-ship spawn: RESTORED ship as hull entity " + hullEntityId
                + " + deck entity " + deckEntityId + " at " + hull.Position + " (" + hullBytes.Length
                + "-byte hull); it will be served to every joining client via the spawn plan.");

            return hullEntityId;
        }

        /// <summary>
        /// Registers a built ship's hull and deck and seeds the built-ship ledger,
        /// allocating their shared entity ids - the part common to a runtime build and a
        /// boot restore. The two WorldEntities are built by
        /// <see cref="BuiltShipSpawnPlan"/>, the single source of truth for a built
        /// ship's asset + seed sets, so the two paths cannot diverge. Does NOT broadcast.
        ///
        /// The ledger is seeded BEFORE any peer can check the entities out: the hull's
        /// own bytes (1209) and that the deck is a deck (1099).
        /// </summary>
        private static (long HullEntityId, WorldEntity Hull, WorldEntity Deck, long DeckEntityId)
            RegisterBuiltShip(FixedPointPosition hullPos, byte[] hullBytes)
        {
            int sequence = BuiltShips.NextSequence();
            BuiltShipSpawnPlan.HullAndDeck plan = BuiltShipSpawnPlan.For(sequence, hullPos);

            WorldsAdriftRebornGameServer.WorldEntities.Register(plan.Hull);
            WorldsAdriftRebornGameServer.WorldEntities.Register(plan.Deck);

            long hullEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(plan.Hull);
            long deckEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(plan.Deck);
            BuiltShips.RegisterHull(hullEntityId, hullBytes);
            BuiltShips.RegisterDeck(deckEntityId);

            return (hullEntityId, plan.Hull, plan.Deck, deckEntityId);
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
    }
}
