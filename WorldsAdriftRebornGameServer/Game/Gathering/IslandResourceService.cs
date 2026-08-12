using System.Collections.Generic;
using Bossa.Travellers.Islands;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// The server half of the island resource-placement handshake, glue side. Owns one
    /// <see cref="IslandResourceLedger"/> per island (the clamp/dedup/idempotency state)
    /// and drives the two wire moments the pure policy cannot:
    ///
    ///  1. <see cref="OnIslandInterest"/> - when a peer checks the island out, serve it
    ///     1010 + 1011 (so the stock IslandProxyVisualizer's reader+writer bind and it
    ///     enables), GRANT the peer authority over 1011 (so its reply WRITER works), then
    ///     raise the 1010 <c>SpawnResources</c> request event. One-time per (peer, island).
    ///
    ///  2. <see cref="OnReply"/> - when the client's 1011 SpawnResourcesReply arrives,
    ///     admit it through the island's ledger (metal-only, on-ground positions
    ///     converted to fixed point, deduped, clamped to the requested count) and spawn a
    ///     real deposit at each admitted position via <see cref="DepositHandshakeSpawner"/>.
    ///
    /// Sending the request to EVERY peer and letting the ledger clamp/dedup is what makes
    /// it multiplayer-safe: two clients sampling the same island cannot over-spawn or
    /// double up. The spawned deposits are shared world entities, so every peer (and every
    /// late joiner, via the connect-time spawn plan) sees them.
    /// </summary>
    internal static class IslandResourceService
    {
        private static readonly Dictionary<long, IslandResourceLedger> Ledgers = new Dictionary<long, IslandResourceLedger>();

        // Which (peer, island) pairs have had 1010/1011 served + 1011 authority granted.
        // Done exactly once - re-serving 1010/1011 would re-add live components.
        private static readonly HashSet<(ulong Peer, long Island)> ServedAndGranted = new HashSet<(ulong, long)>();

        // (peer, island) pairs whose client has sent at least one 1011 reply - proof its
        // IslandProxyVisualizer enabled and subscribed, so the request event no longer
        // needs re-sending to it.
        private static readonly HashSet<(ulong Peer, long Island)> Replied = new HashSet<(ulong, long)>();

        // How many times the SpawnResources request has been (re-)sent to each
        // (peer, island). The request is re-sent on the client's periodic interest
        // re-declaration until it replies - a timer-free retry that survives the
        // cross-ENet-channel race where the request event (channel 4) could otherwise
        // arrive before the AddComponent (channel 2) that enables the visualizer. Capped
        // so a client that never replies cannot be asked forever.
        private static readonly Dictionary<(ulong Peer, long Island), int> RequestSends = new Dictionary<(ulong, long), int>();

        /// <summary>The most times the request is re-sent to one peer before giving up waiting for a reply.</summary>
        internal const int MaxRequestSends = IslandResourceHandshake.MaxRequestSends;

        /// <summary>
        /// The one start-up line that says how ore will be placed this run. Written before
        /// any peer connects, so it is at the very top of the log the operator tails.
        /// </summary>
        internal static void ReportConfiguration()
        {
            if (!IslandResourceHandshake.Enabled())
            {
                System.Console.WriteLine("[warning] resource-handshake: DISABLED ("
                    + IslandResourceHandshake.EnabledEnvVar
                    + "=0). Ore comes from the hand-placed table only, via the boot-time"
                    + " WAREBORN_SPAWN_DEPOSIT path.");
                return;
            }

            System.Console.WriteLine("[info] resource-handshake: ENABLED - the CLIENT chooses every deposit"
                + " position by surface-sampling its own island mesh (physics-checked), and this server"
                + " spawns what it replies with. Requesting " + IslandResourceHandshake.MetalCount()
                + " metal deposit(s) per island (" + IslandResourceHandshake.CountEnvVar + ", default "
                + IslandResourceHandshake.DefaultMetalCount + ", clamped " + IslandResourceHandshake.MinMetalCount
                + ".." + IslandResourceHandshake.MaxMetalCount + ").");

            System.Console.WriteLine("[info] resource-handshake: accepting client placements inside "
                + IslandBounds.Haven() + " (Haven's measured AABB + "
                + IslandBounds.DefaultMarginMetres + " m); anything outside is refused and logged.");

            if (IslandResourceFallback.Enabled())
            {
                System.Console.WriteLine("[info] resource-handshake: static fallback ARMED at "
                    + IslandResourceFallback.Seconds() + "s (" + IslandResourceFallback.SecondsEnvVar
                    + ") - if no usable 1011 placement arrives in that time the "
                    + Multiplayer.MetalDeposits.HavenPlacements.Count
                    + " hand-placed deposits are spawned instead, so the world is never left empty.");
            }
            else
            {
                System.Console.WriteLine("[warning] resource-handshake: static fallback DISABLED ("
                    + IslandResourceFallback.EnabledEnvVar
                    + "=0) - if the client never replies there will be NO ore at all.");
            }

            if (System.Environment.GetEnvironmentVariable("WAREBORN_SPAWN_DEPOSIT") == "1")
            {
                System.Console.WriteLine("[warning] resource-handshake: WAREBORN_SPAWN_DEPOSIT=1 is IGNORED"
                    + " while the handshake is on - the hand-placed table would otherwise appear ALONGSIDE"
                    + " the client-placed deposits. It is still used, at runtime, as the fallback.");
            }
        }

        /// <summary>The per-island ledger, created on first use with the env-configured count.</summary>
        private static IslandResourceLedger LedgerFor(long islandEntityId)
        {
            if (!Ledgers.TryGetValue(islandEntityId, out IslandResourceLedger? ledger))
            {
                // The COORDINATE-FRAME GUARD travels with the ledger: every replied
                // position is checked against Haven's own (generously widened) AABB
                // before it can become an entity. See Multiplayer.IslandBounds.
                ledger = new IslandResourceLedger(IslandResourceHandshake.MetalCount(), IslandBounds.Haven());
                Ledgers.Add(islandEntityId, ledger);

                System.Console.WriteLine("[info] resource-handshake: island " + islandEntityId
                    + " configured for " + ledger.RequestedCount + " metal deposit(s) ("
                    + IslandResourceHandshake.CountEnvVar + ", default "
                    + IslandResourceHandshake.DefaultMetalCount + ", clamped to "
                    + IslandResourceHandshake.MinMetalCount + ".." + IslandResourceHandshake.MaxMetalCount
                    + "); accepting client placements inside " + IslandBounds.Haven() + "; static fallback "
                    + (IslandResourceFallback.Enabled()
                        ? "armed at " + IslandResourceFallback.Seconds() + "s ("
                            + IslandResourceFallback.SecondsEnvVar + ")"
                        : "DISABLED (" + IslandResourceFallback.EnabledEnvVar + ")")
                    + ".");
            }
            return ledger;
        }

        /// <summary>Whether an entity id is an island this server registered.</summary>
        private static bool IsIsland(long entityId)
        {
            WorldEntity? reg = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId);
            return reg != null && reg.Key == Multiplayer.WorldEntities.IslandKey;
        }

        /// <summary>
        /// Called from the interest-serve path for any non-own entity. If the handshake is
        /// on and this is an island this peer has not yet been set up for, serve 1010+1011,
        /// grant 1011 authority, and raise the SpawnResources request - IN THAT ORDER, so
        /// the client has enabled its visualizer and bound its reply writer before the
        /// request event is delivered. The served ids are returned so the caller can mark
        /// them delivered and skip the best-effort re-add that would double-serve them.
        /// </summary>
        internal static IReadOnlyList<uint> OnIslandInterest(ENetPeerHandle peer, long entityId)
        {
            if (!IslandResourceHandshake.Enabled() || !IsIsland(entityId))
            {
                return System.Array.Empty<uint>();
            }

            ulong peerId = PeerIdentity.IdOf(peer);
            (ulong, long) pk = (peerId, entityId);

            // Ensure the ledger (and thus the requested count) exists for this island.
            IslandResourceLedger ledger = LedgerFor(entityId);

            IReadOnlyList<uint> served = System.Array.Empty<uint>();

            // Serve + grant ONCE. On later island-interest declarations this is skipped
            // (re-adding live 1010/1011 would cycle the client's readers/writer), but the
            // request re-send below still runs until the peer replies.
            if (!ServedAndGranted.Contains(pk))
            {
                served = ServeAndGrant(peer, entityId);
                if (served.Count == 0)
                {
                    return served; // serve failed; try again on the next interest declaration
                }
                ServedAndGranted.Add(pk);

                // ARM THE EXPLICIT RE-SENDS. The client's periodic interest
                // re-declaration is a free retry, but nothing on this side GUARANTEES it
                // happens - and if the very first request lost the cross-channel race with
                // the AddComponent that enables the visualizer, no retry means no ore at
                // all. So the re-sends are scheduled outright, on the main loop, and
                // cancelled the moment this peer replies. See
                // IslandResourceHandshake.RequestRetrySeconds.
                ScheduleRetries(peer, entityId);
            }

            // (Re-)send the SpawnResources request until this peer replies or the cap is
            // hit. The first send may lose the cross-channel race with the AddComponent
            // that enables the visualizer; the client's periodic interest re-declaration
            // gives a free, low-rate retry with no timer. Repeated requests are safe: the
            // ledger clamps/dedups whatever comes back.
            if (!Replied.Contains(pk) && ledger.RequestedCount > 0)
            {
                RequestSends.TryGetValue(pk, out int sends);
                if (sends < MaxRequestSends)
                {
                    SendRequest(peer, entityId, ledger.RequestedCount);
                    RequestSends[pk] = sends + 1;
                    ledger.MarkRequestSent();

                    System.Console.WriteLine("[info] resource-handshake: island " + entityId
                        + " request " + ledger.RequestedCount + " metal deposit(s) via 1010 SpawnResources"
                        + " to a peer (send #" + (sends + 1) + "/" + MaxRequestSends + "); awaiting its 1011 reply. ("
                        + ledger.SpawnedCount + "/" + ledger.RequestedCount + " spawned so far)");

                    // ARM THE SAFE FAILURE MODE, once, on the FIRST request for this
                    // island. If the deadline passes with nothing spawned from client
                    // replies, the hand-placed table goes down instead - the world is
                    // never left with no ore because a live unknown did not go our way.
                    ArmFallback(entityId);
                }
            }

            return served;
        }

        /// <summary>
        /// Schedules the explicit SpawnResources re-sends for one (peer, island), on the
        /// MAIN POLL LOOP via <see cref="DeferredActions"/>. Each fire re-checks that the
        /// peer is still connected, has still not replied, and that the island has not
        /// fallen back - so a disconnected or already-served peer is never written to.
        /// Keyed on (peer, island) so <see cref="CancelRetries"/> can drop the rest the
        /// instant a reply lands.
        ///
        /// MULTIPLAYER CLASSIFICATION: at most <c>RequestRetrySeconds.Length</c> one-shot
        /// events per peer per island for the life of a session - not a stream, not a
        /// per-frame relay, and self-cancelling on success.
        /// </summary>
        private static void ScheduleRetries(ENetPeerHandle peer, long islandEntityId)
        {
            ulong peerId = PeerIdentity.IdOf(peer);
            (ulong, long) pk = (peerId, islandEntityId);

            foreach (double seconds in IslandResourceHandshake.RequestRetrySeconds)
            {
                DeferredActions.AfterKeyed(RetryKey(pk), seconds, () => RetrySend(peer, pk, seconds));
            }
        }

        /// <summary>The DeferredActions cancellation key for one (peer, island)'s pending re-sends.</summary>
        private static string RetryKey((ulong Peer, long Island) pk)
        {
            return "resource-handshake-retry-" + pk.Peer + "-" + pk.Island;
        }

        /// <summary>Drops every pending re-send for a (peer, island) - called when it replies.</summary>
        private static void CancelRetries((ulong Peer, long Island) pk)
        {
            DeferredActions.Cancel(RetryKey(pk));
        }

        /// <summary>
        /// One scheduled re-send. Every reason NOT to send is re-checked here rather than
        /// at schedule time, because all of them can become true in the seconds between.
        /// </summary>
        private static void RetrySend(ENetPeerHandle peer, (ulong Peer, long Island) pk, double seconds)
        {
            if (Replied.Contains(pk))
            {
                return; // it answered; nothing to chase
            }
            if (!PeerManager.Instance.playerState.ContainsKey(peer))
            {
                return; // gone
            }
            if (PeerIdentity.IdOf(peer) != pk.Peer)
            {
                return; // the handle was recycled for a different peer between schedule and fire
            }

            IslandResourceLedger ledger = LedgerFor(pk.Island);
            if (ledger.FallbackFired || ledger.Satisfied || ledger.RequestedCount <= 0)
            {
                return;
            }

            RequestSends.TryGetValue(pk, out int sends);
            if (sends >= MaxRequestSends)
            {
                return;
            }

            SendRequest(peer, pk.Island, ledger.RequestedCount);
            RequestSends[pk] = sends + 1;

            System.Console.WriteLine("[info] resource-handshake: island " + pk.Island
                + " RE-SENT the 1010 SpawnResources request (+" + seconds + "s, send #" + (sends + 1)
                + "/" + MaxRequestSends + ") - the peer has still not replied on 1011.");
        }

        /// <summary>
        /// Schedules the one-shot fallback deadline for an island, the first time only.
        /// Uses <see cref="DeferredActions"/>, so it fires on the MAIN POLL LOOP - the same
        /// thread that owns the peer set and the world-entity registry - not a background
        /// timer. A no-op when the fallback is disabled.
        /// </summary>
        private static void ArmFallback(long islandEntityId)
        {
            if (!IslandResourceFallback.Enabled())
            {
                return;
            }
            IslandResourceLedger ledger = LedgerFor(islandEntityId);
            if (!ledger.MarkDeadlineArmed())
            {
                return;
            }

            double seconds = IslandResourceFallback.Seconds();
            DeferredActions.After(seconds, () => ResolveDeadline(islandEntityId, seconds));

            System.Console.WriteLine("[info] resource-handshake: island " + islandEntityId
                + " fallback deadline armed - if no usable 1011 placement arrives within "
                + seconds + "s the hand-placed deposits are spawned instead.");
        }

        /// <summary>
        /// The deadline. Either the handshake produced deposits - in which case the
        /// fallback stands down and one <see cref="IslandResourceFallback.HandshakeMarker"/>
        /// line says so - or it did not, and the static table is placed under one
        /// <see cref="IslandResourceFallback.FallbackMarker"/> line. Exactly one of the two
        /// markers is written per island, which is what makes "which path is live" a
        /// single grep rather than an inference.
        /// </summary>
        internal static void ResolveDeadline(long islandEntityId, double seconds)
        {
            IslandResourceLedger ledger = LedgerFor(islandEntityId);

            // THE CLOCK ONLY RUNS WHILE SOMEONE IS PLAYING. If every peer has left, the
            // handshake never got a fair chance - the client that would have replied is
            // gone - and latching the hand-placed table into an EMPTY world would hand the
            // next joiner the placements they already rejected. Re-arm instead. One
            // deferred action per deadline on an idle server; it stops the moment someone
            // connects and either path resolves.
            if (PeerManager.Instance.playerState.Count == 0 && !ledger.FallbackFired)
            {
                DeferredActions.After(seconds, () => ResolveDeadline(islandEntityId, seconds));
                System.Console.WriteLine("[info] resource-handshake: island " + islandEntityId
                    + " fallback deadline reached with NO peers connected; re-armed for another "
                    + seconds + "s rather than placing the static table into an empty world.");
                return;
            }

            if (!IslandResourceFallback.ShouldFallBack(ledger.SpawnedCount, ledger.FallbackFired))
            {
                System.Console.WriteLine("[info] " + IslandResourceFallback.StoodDownLine(
                    islandEntityId, ledger.SpawnedCount, ledger.RequestedCount));
                return;
            }

            // Latch BEFORE spawning: from here on the ledger refuses client replies, so a
            // reply that lands mid-spawn cannot stack a second set on top.
            ledger.MarkFallbackFired();

            int spawned = DepositFallbackSpawner.SpawnStaticPlacements();

            System.Console.WriteLine("[warning] " + IslandResourceFallback.FallbackLine(
                islandEntityId, seconds, spawned));
        }

        /// <summary>
        /// Serves 1010 (reader) + 1011 (reader/writer) on the island and grants the peer
        /// authority over 1011. Returns the ids served (so the caller marks them delivered
        /// and the best-effort path does not re-add them), or empty on failure.
        /// </summary>
        private static IReadOnlyList<uint> ServeAndGrant(ENetPeerHandle peer, long entityId)
        {
            // failOnComponentInitError false: an island is a shared entity, best-effort
            // like every other world-entity serve - a serializer miss must not tear down
            // anything, only leave the visualizer unenabled (visible in the log).
            List<Structs.Structs.InterestOverride> resourceComponents = new List<Structs.Structs.InterestOverride>
            {
                new Structs.Structs.InterestOverride(1010, 1),
                new Structs.Structs.InterestOverride(1011, 1),
            };
            List<uint> served = new List<uint>();
            if (!SendOPHelper.SendAddComponentOp(peer, entityId, resourceComponents, false, served))
            {
                System.Console.WriteLine("[warning] resource-handshake: could not serve 1010/1011 on island "
                    + entityId + " to a peer; its IslandProxyVisualizer will not enable this checkout.");
                return System.Array.Empty<uint>();
            }

            // Grant the client authority over 1011 so its reply WRITER binds. 1010 stays a
            // server-owned reader on the client.
            if (!SendOPHelper.SendAuthorityChangeOp(peer, entityId, new List<uint> { 1011 }))
            {
                System.Console.WriteLine("[warning] resource-handshake: served 1010/1011 on island " + entityId
                    + " but the 1011 authority grant failed; the client's reply writer will not bind.");
            }
            return served;
        }

        /// <summary>Raises the 1010 SpawnResources request event on the island for one peer.</summary>
        private static void SendRequest(ENetPeerHandle peer, long entityId, int number)
        {
            IslandResourceSpawnerState.Update request = new IslandResourceSpawnerState.Update();
            request.AddSpawnResources(new SpawnResources(number, IslandResourceType.Metal));
            SendOPHelper.SendComponentUpdateOp(peer, entityId,
                new List<uint> { 1010 }, new List<object> { request });
        }

        /// <summary>
        /// Consumes a client's 1011 SpawnResourcesReply for an island: flatten every
        /// request to a plain <see cref="ResourceReplyItem"/>, admit through the ledger,
        /// and spawn a deposit at each admitted position. Ignores replies on non-islands
        /// or from a peer with no spawned player entity (trust: only a real player drives
        /// placement).
        /// </summary>
        internal static void OnReply(ENetPeerHandle peer, long islandEntityId,
            IslandResourceSpawnerClientState.Update update)
        {
            if (update == null || update.spawnResourcesReply == null || update.spawnResourcesReply.Count == 0)
            {
                return; // an ordinary 1011 data update (initialized/meshCount), no reply events
            }
            if (!IslandResourceHandshake.Enabled() || !IsIsland(islandEntityId))
            {
                return;
            }

            // Trust: the reply must come from a peer that has a spawned player entity.
            ulong peerId = PeerIdentity.IdOf(peer);
            if (WorldsAdriftRebornGameServer.Players.EntityOf(peerId) == null)
            {
                System.Console.WriteLine("[warning] resource-handshake: 1011 reply on island " + islandEntityId
                    + " from a peer with no player entity; ignoring.");
                return;
            }

            // The client replied at all, so its visualizer enabled and subscribed: stop
            // re-sending the request to this peer (even if this particular batch admits
            // nothing - it is proof the request landed).
            Replied.Add((peerId, islandEntityId));
            CancelRetries((peerId, islandEntityId));

            List<ResourceReplyItem> items = new List<ResourceReplyItem>();
            foreach (SpawnResourcesReply reply in update.spawnResourcesReply)
            {
                if (reply.requests == null)
                {
                    continue;
                }
                foreach (SpawnResourceRequest req in reply.requests)
                {
                    items.Add(new ResourceReplyItem(
                        req.resource.position.X,
                        req.resource.position.Y,
                        req.resource.position.Z,
                        req.resource.metadata,
                        req.variant));
                }
            }

            if (items.Count == 0)
            {
                return;
            }

            IslandResourceLedger ledger = LedgerFor(islandEntityId);
            LedgerAdmission admission = ledger.AdmitDetailed(items);
            IReadOnlyList<AdmittedDeposit> admitted = admission.Admitted;

            if (admission.RefusedBecauseFallbackFired)
            {
                System.Console.WriteLine("[warning] resource-handshake: 1011 reply on island " + islandEntityId
                    + " carried " + items.Count + " placement(s) but arrived AFTER the fallback deadline; "
                    + "this island is already served by the hand-placed table (" + IslandResourceFallback.FallbackMarker
                    + "). Raise " + IslandResourceFallback.SecondsEnvVar + " to give the client longer.");
                return;
            }

            // THE COORDINATE-FRAME ALARM. A refused placement means the client's global
            // frame is not the one we seed the island in - a floating-origin or scale
            // error - which is exactly how deposits ended up in the sky before. Logged
            // with the raw metres and the box, so one line identifies the bug.
            if (admission.Outcome.OutOfBounds > 0)
            {
                ResourceReplyItem bad = admission.Outcome.FirstOutOfBounds!.Value;
                System.Console.WriteLine("[warning] resource-handshake: REJECTED " + admission.Outcome.OutOfBounds
                    + " of " + items.Count + " placement(s) on island " + islandEntityId
                    + " as OUT OF BOUNDS - first was ("
                    + bad.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ", "
                    + bad.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ", "
                    + bad.Z.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                    + ") m, outside " + IslandBounds.Haven()
                    + ". The client's RemapUnityVectorToGlobalCoordinates frame does not match the island's"
                    + " 190602 seed; NOTHING was spawned there.");
            }

            if (admitted.Count == 0)
            {
                System.Console.WriteLine("[info] resource-handshake: 1011 reply on island " + islandEntityId
                    + " carried " + items.Count + " placement(s), none admitted ("
                    + admission.Outcome.NonMetal + " non-metal, "
                    + admission.Outcome.Duplicate + " duplicate, "
                    + admission.Outcome.OutOfBounds + " out-of-bounds, rest over-count); "
                    + ledger.SpawnedCount + "/" + ledger.RequestedCount + " already spawned.");
                return;
            }

            foreach (AdmittedDeposit deposit in admitted)
            {
                DepositHandshakeSpawner.Spawn(islandEntityId, deposit);
            }

            System.Console.WriteLine("[info] resource-handshake: 1011 reply on island " + islandEntityId
                + " admitted " + admitted.Count + " of " + items.Count + " placement(s); now "
                + ledger.SpawnedCount + "/" + ledger.RequestedCount + " deposit(s) spawned"
                + (ledger.Satisfied ? " (island satisfied)." : "."));

            // The success marker, paired with the fallback's. Written on the FIRST batch
            // that actually spawns something (that is the moment the live unknown closed)
            // and again when the island is satisfied, so a short grep finds it either way.
            if (ledger.SpawnedCount == admitted.Count || ledger.Satisfied)
            {
                System.Console.WriteLine("[info] " + IslandResourceFallback.HandshakeLine(
                    islandEntityId, ledger.SpawnedCount, ledger.RequestedCount));
            }
        }
    }
}
