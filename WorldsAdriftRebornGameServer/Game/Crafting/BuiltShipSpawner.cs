using System;
using System.Collections.Generic;
using System.Linq;
using Bossa.Travellers.Items;
using Improbable;
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

            BuiltRegistration reg = RegisterBuiltShip(hullPos, hullBytes);
            long hullEntityId = reg.HullEntityId;
            WorldEntity hull = reg.Hull;

            // Persist the built ship so it reappears next boot - re-spawned as a world
            // entity in the connect-time spawn plan, exactly like this runtime spawn. The
            // returned persistent index is the durable handle a mounted part references its
            // ship by; record it against this hull so a mount committed on it persists too.
            // Persist the EFFECTIVE bytes (the min-hull fallback if the design was bad), so
            // a restore regenerates the SAME panels and keys from the SAME geometry.
            // OWNER = the shipyard's owner (the player who built this ship), threaded so
            // the ship's persisted record is owned like its yard and survives restart owned.
            string shipOwner = Placement.PlacedShipyards.SeedFor(shipyardEntityId).OwnerCharacterUid;
            int persistentIndex = WorldStatePersistence.RecordBuiltShip(hullPos, reg.EffectiveHullBytes, shipOwner, shipyardPos);
            BuiltShips.SetPersistentIndex(hullEntityId, persistentIndex);

            // GATE B (ship ownership): record the built hull's owner so its 8062/4349
            // serve branches seed the owner's character uid and the client's
            // HostileItemPlacingPredicate treats the ship as the builder's (green/placeable)
            // rather than inaccessible. The owner is the shipyard's owner - the player who
            // built it - the same value persisted above.
            BuiltShips.SetOwner(hullEntityId, shipOwner);

            // ONE SHIP PER SHIPYARD: record which yard produced this hull, so its 1205
            // ShipyardState.DockedShipId reports it and a further CRAFT on that yard is
            // refused until it is cleared (see the 1270 StartCrafting gate + the undock
            // trigger). Recorded BEFORE the 1205 push below so the serve branch agrees.
            BuiltShips.SetDocked(shipyardEntityId, hullEntityId);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                bool hullOk = BroadcastToPeer(peer, hullEntityId, hull);
                bool decksOk = true;
                foreach ((long deckId, WorldEntity deckEntity) in reg.Decks)
                {
                    decksOk &= BroadcastToPeer(peer, deckId, deckEntity);
                }
                if (hullOk && decksOk)
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
                + " as hull entity " + hullEntityId + " + " + reg.Decks.Count + " deck panel entity(ies)"
                + " at " + hullPos + " (" + reg.EffectiveHullBytes.Length + "-byte hull, "
                + BuiltShipPlacement.HullSeedComponents.Count + " hull seeds + "
                + BuiltShipPlacement.DeckSeedComponents.Count + " deck seeds each), sent to "
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

            BuiltRegistration reg = RegisterBuiltShip(hullPos, hullBytes);

            // GATE B (ship ownership): re-establish the built hull's owner from the
            // persisted record so a restored ship comes back OWNED by the same character,
            // and the owner's client can place parts on it after relog (the regression
            // this fixes). Empty for a legacy record written before ownership threading -
            // that hull restores unowned, exactly as it was persisted.
            BuiltShips.SetOwner(reg.HullEntityId, record.OwnerCharacterUid);

            Console.WriteLine("[info] built-ship spawn: RESTORED ship as hull entity " + reg.HullEntityId
                + " + " + reg.Decks.Count + " deck panel entity(ies) at " + reg.Hull.Position + " ("
                + reg.EffectiveHullBytes.Length + "-byte hull); it will be served to every joining client"
                + " via the spawn plan.");

            return reg.HullEntityId;
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
        /// <summary>The result of registering a built ship: its hull, its deck panels, and the bytes actually served.</summary>
        private readonly struct BuiltRegistration
        {
            public BuiltRegistration(long hullEntityId, WorldEntity hull,
                IReadOnlyList<(long Id, WorldEntity Entity)> decks, byte[] effectiveHullBytes)
            {
                HullEntityId = hullEntityId;
                Hull = hull;
                Decks = decks;
                EffectiveHullBytes = effectiveHullBytes;
            }

            public long HullEntityId { get; }
            public WorldEntity Hull { get; }
            public IReadOnlyList<(long Id, WorldEntity Entity)> Decks { get; }

            /// <summary>The hull bytes actually registered for 1209 and used to derive the decks (the fallback if the design was bad).</summary>
            public byte[] EffectiveHullBytes { get; }
        }

        private static BuiltRegistration RegisterBuiltShip(FixedPointPosition hullPos, byte[] hullBytes)
        {
            // Derive the deck panels from the SAME validated bytes 1209 will serve, so the
            // visible hull and its floors can never come from different geometry. If the
            // design yields no panels, fall back to the minimum hull for BOTH 1209 and the
            // decks; if even that yields none, last-resort the single static rectangle.
            byte[] effectiveBytes = hullBytes;
            IReadOnlyList<DeckPanel> panels = TryGeneratePanels(effectiveBytes, out string? firstError);
            if (panels.Count == 0)
            {
                Console.WriteLine("[warn] built-ship spawn: deck derivation from the " + effectiveBytes.Length
                    + "-byte hull yielded no panels" + (firstError == null ? "" : " (" + firstError + ")")
                    + "; regenerating hull + decks from the minimum hull.");
                effectiveBytes = ShipHull.MinimumHullData();
                panels = TryGeneratePanels(effectiveBytes, out _);
            }
            if (panels.Count == 0)
            {
                Console.WriteLine("[warn] built-ship spawn: even the minimum hull derived no deck panels;"
                    + " last-resort spawning the single static deck rectangle.");
                panels = new[] { StaticFallbackPanel() };
            }

            LogHullOrientation(effectiveBytes, panels.Count);

            int sequence = BuiltShips.NextSequence();
            BuiltShipSpawnPlan.HullAndDecks plan = BuiltShipSpawnPlan.For(sequence, hullPos, panels);

            WorldsAdriftRebornGameServer.WorldEntities.Register(plan.Hull);
            long hullEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(plan.Hull);
            BuiltShips.RegisterHull(hullEntityId, effectiveBytes);

            var decks = new List<(long Id, WorldEntity Entity)>(plan.Decks.Count);
            for (int i = 0; i < plan.Decks.Count; i++)
            {
                WorldEntity deckEntity = plan.Decks[i];
                WorldsAdriftRebornGameServer.WorldEntities.Register(deckEntity);
                long deckEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(deckEntity);
                BuiltShips.RegisterDeck(deckEntityId, panels[i].LocalVertices);
                decks.Add((deckEntityId, deckEntity));
            }

            return new BuiltRegistration(hullEntityId, plan.Hull, decks, effectiveBytes);
        }

        /// <summary>
        /// Logs the hull's real dimensions and its bow axis at spawn. Cheap, once per
        /// ship, and it is the line that answers a "my ship is rotated / it flies
        /// sideways" report without another round of yaw guessing: a stock cell is 12 m
        /// of BEAM by 4 m of KEEL, so a short hull is genuinely wider than it is long
        /// and its bow (+Z, where the pilot camera looks and where the ship flies) is
        /// its SHORT axis. Never throws - a metrics failure must not cost a spawn.
        /// </summary>
        private static void LogHullOrientation(byte[] effectiveBytes, int panelCount)
        {
            try
            {
                if (!ShipPlanModel.TryDecode(effectiveBytes, out ShipPlanModel? model, out _) || model == null)
                {
                    return;
                }
                ShipHullMetrics metrics = ShipHullMetrics.Measure(model);
                // A beam-dominant hull is logged at WARN, not INFO: it is the single
                // most-reported live confusion ("my ship flies sideways"), the answer
                // is a hull change and not a server change, and a line buried at info
                // level among the spawn chatter has twice now failed to be the thing
                // anyone found. WideHullAdvice() is the shared wording - the man-the-
                // helm log (ShipFlightService.StartPiloting) prints the same sentence.
                string level = metrics.KeelIsLongestAxis ? "[info]" : "[warn]";
                Console.WriteLine(level + " built-ship spawn: hull geometry - "
                    + metrics.Describe()
                    + " " + panelCount + " deck panel(s).");
            }
            catch (System.Exception e)
            {
                Console.WriteLine("[warn] built-ship spawn: could not measure hull geometry: " + e.Message);
            }
        }

        /// <summary>
        /// Decodes <paramref name="bytes"/> and derives the deck panels, never throwing:
        /// a bad blob or a generation error returns an empty list (and the reason), which
        /// the caller treats as a fallback trigger.
        /// </summary>
        private static IReadOnlyList<DeckPanel> TryGeneratePanels(byte[] bytes, out string? error)
        {
            if (!ShipPlanModel.TryDecode(bytes, out ShipPlanModel? model, out error) || model == null)
            {
                return System.Array.Empty<DeckPanel>();
            }
            try
            {
                return DeckGenerator.Generate(model);
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return System.Array.Empty<DeckPanel>();
            }
        }

        /// <summary>
        /// The last-resort single deck panel: the legacy static rectangle
        /// (<see cref="Multiplayer.Deck.LocalVertices"/>) as a <see cref="DeckPanel"/>,
        /// centred on the hull (its centroid is the origin, so no offset). Registered like
        /// any other panel so it is a built deck (a placeable, hull-parented surface) and
        /// its 1518 serves the rectangle.
        /// </summary>
        private static DeckPanel StaticFallbackPanel()
        {
            var verts = new List<ShipVector3>();
            foreach ((double x, double y, double z) in Multiplayer.Deck.LocalVertices)
            {
                verts.Add(new ShipVector3((float)x, (float)y, (float)z));
            }
            return new DeckPanel(new ShipVector3(0f, 0f, 0f), verts, sourceDeckNumber: 0, sourceQuadIndex: 0);
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

            List<uint> seedServed = new List<uint>();
            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true, seedServed))
            {
                Console.WriteLine("[error] built-ship spawn: entity " + entityId
                    + " was created on a peer but its seed components were dropped; it will render inert.");
                return false;
            }

            // Ledger the seeds (same pattern as PlacementService): without the
            // mark, the client's later re-declared interest for this entity
            // re-ADDs everything - including 1518/1099 (deck collider reset,
            // player falls through the deck) and 190602 (TransformState re-seed).
            WorldsAdriftRebornGameServer.ServedComponents.MarkServed(peer, entityId, seedServed);

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
