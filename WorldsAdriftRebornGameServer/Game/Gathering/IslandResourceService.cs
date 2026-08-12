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
        internal const int MaxRequestSends = 10;

        /// <summary>The per-island ledger, created on first use with the env-configured count.</summary>
        private static IslandResourceLedger LedgerFor(long islandEntityId)
        {
            if (!Ledgers.TryGetValue(islandEntityId, out IslandResourceLedger? ledger))
            {
                ledger = new IslandResourceLedger(IslandResourceHandshake.MetalCount());
                Ledgers.Add(islandEntityId, ledger);
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
                }
            }

            return served;
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
            IReadOnlyList<AdmittedDeposit> admitted = ledger.Admit(items);

            if (admitted.Count == 0)
            {
                System.Console.WriteLine("[info] resource-handshake: 1011 reply on island " + islandEntityId
                    + " carried " + items.Count + " placement(s), none admitted (duplicate/over-count/non-metal); "
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
        }
    }
}
