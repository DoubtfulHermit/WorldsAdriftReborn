using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// THE CONSUMER the retail worker used to be. Turns one client-provided,
    /// on-ground placement from the 1011 SpawnResourcesReply into a real, mineable
    /// metal DEPOSIT entity - the SAME kind of world entity as the hand-placed
    /// <see cref="Multiplayer.MetalDeposits"/> test deposit, but positioned by the
    /// client's own surface sample instead of a measured or offline-guessed vertex.
    ///
    /// It reuses the PROVEN runtime-spawn machinery exactly as
    /// <see cref="Crafting.BuiltShipSpawner"/> does: register a
    /// <see cref="WorldEntity"/>, allocate its shared entity id once, seed the SAME
    /// ledgers the connect-time deposit path seeds (<c>Nodes</c> +
    /// <c>MetalHarvest</c> + <c>HarvestReward</c>, mirroring AddWorldEntity's deposit
    /// branch), then registers it with per-peer spatial interest (or preserves the
    /// old broadcast when spatial interest is disabled).
    /// A deposit carries NO seed components (its 1255/2103/12283/1016/190602 are
    /// served best-effort over interest, exactly like the static deposit), so there
    /// is no all-or-nothing batch to drop.
    ///
    /// MULTIPLAYER CLASSIFICATION: a ONE-TIME AddEntity per admitted deposit, then
    /// ordinary interest serving - not a per-frame re-seed, not a high-rate relay.
    /// The clamp/dedup/idempotency that stops a re-send or a second client from
    /// double-spawning lives in <see cref="IslandResourceLedger"/>, upstream of here.
    /// </summary>
    internal static class DepositHandshakeSpawner
    {
        /// <summary>
        /// The metal a handshake deposit falls back to when its island cannot be
        /// identified, or is identified but no catalogue holds a metal table for it.
        ///
        /// It used to be the metal for EVERY handshake deposit, on the stated
        /// grounds that "the retail biome material table did not survive". That was
        /// wrong, and it is the single reason every rock a live player could reach
        /// was iron: the per-island table DID survive, was imported into
        /// release-runtime-catalog.json for all 254 islands with its provenance
        /// recorded, and is what ReleaseWorldCatalog already stamps its own 1930
        /// deposits from. This path simply never asked it.
        ///
        /// The VISUALS still come from the client-chosen <c>variant</c>, not from
        /// here - this names the salvage grant's item type (a real itemData.json
        /// row) and nothing else.
        /// </summary>
        internal const string DefaultMetalType = Multiplayer.Gathering.IslandMetalTable.FallbackMetal;

        /// <summary>Fallback quality, used only when the island's own table cannot be found.</summary>
        internal const int DefaultQuality = 5;

        /// <summary>
        /// The metal and quality for the <paramref name="index"/>-th deposit on an
        /// island, drawn from that island's own table.
        ///
        /// The island is recovered from its entity id through the world registry
        /// key, because the handshake reply carries only the id. A miss at any step
        /// - unbound id, unknown key, island with no table - degrades to the
        /// fallback and SAYS SO, rather than quietly paying iron and looking like a
        /// deliberate choice, which is how the previous hardcode survived so long.
        /// </summary>
        private static (string Metal, int Quality) MetalFor(long islandEntityId, int index)
        {
            Multiplayer.WorldEntity? island =
                WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(islandEntityId);
            Multiplayer.Islands.IslandDefinition? definition = island == null
                ? null
                : WorldsAdriftRebornGameServer.IslandTopology.ByWorldEntityKey(island.Key);

            if (definition == null)
            {
                System.Console.WriteLine("[warning] resource-handshake: island entity " + islandEntityId
                    + " is not a known island, so deposit " + index + " falls back to "
                    + DefaultMetalType + " q" + DefaultQuality + ".");
                return (DefaultMetalType, DefaultQuality);
            }

            Multiplayer.Islands.SurveyedMetal? draw =
                Multiplayer.Gathering.IslandMetalTable.DrawFor(definition.Id, index);

            if (draw == null)
            {
                System.Console.WriteLine("[warning] resource-handshake: island '" + definition.Id
                    + "' has no metal table, so deposit " + index + " falls back to "
                    + DefaultMetalType + " q" + DefaultQuality + ".");
                return (DefaultMetalType, DefaultQuality);
            }

            return (Multiplayer.Gathering.IslandMetalTable.ItemTypeIdOf(draw), draw.Quality);
        }

        private static bool _globalEntityEnsured;

        /// <summary>
        /// Spawns one admitted deposit on the given island and returns its entity id,
        /// or null if nothing could be spawned. Registers the biome GLOBAL entity first
        /// (once) so the client's MetalDepositVisualiser can resolve the deposit's biome
        /// and actually draw the rock - see <see cref="Multiplayer.WorldEntities.GlobalEntity"/>.
        /// </summary>
        internal static long? Spawn(long islandEntityId, AdmittedDeposit admitted)
        {
            (string metal, int quality) = MetalFor(islandEntityId, admitted.Index);

            MetalNode node = new MetalNode(
                KeyFor(islandEntityId, admitted.Index),
                metal,
                quality,
                admitted.Position,
                isDeposit: true,
                variantId: admitted.Variant);

            long? depositId = SpawnDepositNode(node, "island " + islandEntityId);

            // LODGE AN ATLAS SHARD, exactly as the fallback does for a hand-placed
            // deposit. Without this a SUCCESSFUL handshake yields a world with no atlas
            // shards at all: shard creation used to be index-paired to the static
            // deposit-N table, so a client-placed deposit could never be a host. The
            // shard's key embeds the HOST'S key (AtlasShardCatalogue.KeyForHost), so the
            // registration resolves its 1305 rockCoreId from the deposit we just bound -
            // which is why this runs AFTER SpawnDepositNode, never before.
            if (depositId.HasValue && depositId.Value != 0)
            {
                LodgeShardIn(node, depositId.Value, admitted.Index);
            }

            return depositId;
        }

        /// <summary>
        /// Registers the atlas shard lodged in a just-spawned handshake deposit, when the
        /// spawn rate says this one carries a shard. Mirrors
        /// <c>DepositFallbackSpawner.SpawnShards</c> call-for-call (same factory, same
        /// ledger, same broadcast) so a handshake deposit and a fallback deposit are
        /// indistinguishable to the shard code. Never throws out of the spawn path: a
        /// shard that cannot be placed is logged and the deposit still stands.
        /// </summary>
        private static void LodgeShardIn(MetalNode node, long depositEntityId, int depositIndex)
        {
            try
            {
                int oneIn = AtlasSpawnPolicy.OneInDeposits(
                    System.Environment.GetEnvironmentVariable("WAREBORN_ATLAS_RATE"));
                if (!AtlasSpawnPolicy.DepositCarriesShard(depositIndex, oneIn))
                {
                    return;
                }

                WorldEntity shard = Multiplayer.WorldEntities.AtlasShardEntity(node.Key, node.Position);
                if (WorldsAdriftRebornGameServer.WorldEntities.ByKey(shard.Key) != null)
                {
                    return; // already placed (a defensive second call)
                }

                WorldsAdriftRebornGameServer.WorldEntities.Register(shard);
                long shardId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(shard);

                WorldsAdriftRebornGameServer.AtlasShards.Register(
                    shardId, depositEntityId, AtlasShardCatalogue.DefaultSlotId);

                BroadcastToAll(shardId, shard);

                System.Console.WriteLine("[info] resource-handshake: lodged ATLAS SHARD '" + shard.Key
                    + "' as entity " + shardId + " in deposit entity " + depositEntityId
                    + " (one shard per " + oneIn + " deposit(s)).");
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine("[error] resource-handshake: atlas shard for '" + node.Key
                    + "' failed to lodge: " + e.Message + ". The deposit itself is unaffected.");
            }
        }

        /// <summary>
        /// Spawns ONE deposit node at runtime and returns its entity id, or null if the
        /// AddEntity could not be built. The shared core of both live deposit paths - the
        /// handshake's client-chosen placements and the static-table FALLBACK - so a
        /// fallback deposit is registered, ledgered and broadcast through exactly the same
        /// code as a handshake one, and neither can drift from the other.
        ///
        /// Idempotent on the node's registry key: a second call for the same key returns
        /// the already-bound entity id and spawns nothing.
        /// </summary>
        internal static long? SpawnDepositNode(MetalNode node, string context)
        {
            EnsureGlobalEntity();

            string key = node.Key;

            // A re-run of the same key - should not happen, the ledger hands out
            // monotonic indices and the fallback fires once - would collide on the
            // registry key. Guard so a defensive double call is a no-op, not a throw.
            if (WorldsAdriftRebornGameServer.WorldEntities.ByKey(key) != null)
            {
                return WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(key);
            }

            WorldEntity registration = Multiplayer.WorldEntities.DepositEntity(node);
            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long entityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            // Seed the SAME ledgers the connect-time deposit branch seeds
            // (WorldsAdriftRebornGameServer.AddWorldEntity), so this deposit is a real
            // mineable node: NodeRegistry carries its facts + crust damage, HarvestReward
            // teaches the yield, MetalHarvest makes it shootable with the deposit's
            // ten-shot depletion. Idempotent by the fresh entity id.
            if (WorldsAdriftRebornGameServer.Nodes.Register(entityId, node))
            {
                HarvestReward.Register(
                    Multiplayer.Gathering.NodeYield.SourceKeyFor(node),
                    Multiplayer.Gathering.NodeYield.RuleFor(node));

                WorldsAdriftRebornGameServer.MetalHarvest.Place(
                    entityId,
                    Multiplayer.MetalDeposits.YieldUnits,
                    shotsToDeplete: Multiplayer.MetalDeposits.ShotsToDeplete);
            }

            int reached = 0;
            bool spatiallyStreamed = WorldsAdriftRebornGameServer.ResourceInterest.RegisterRuntime(entityId, registration);
            if (!spatiallyStreamed)
            {
                foreach (ENetPeerHandle peer in ConnectedPeers())
                {
                    if (BroadcastEntity(peer, entityId, registration)) reached++;
                }
            }

            System.Console.WriteLine("[info] resource-handshake: spawned DEPOSIT '" + key + "' as entity "
                + entityId + " on " + context + " at " + node.Position
                + " variant '" + node.VariantId + "' (" + Multiplayer.MetalDeposits.ShotsToDeplete
                + " shots -> " + Multiplayer.MetalDeposits.YieldUnits + " units), "
                + (spatiallyStreamed
                    ? "registered with per-peer spatial interest."
                    : "broadcast to " + reached + " peer(s).")
                + " Late joiners get it from continuous spatial interest.");

            return entityId;
        }

        /// <summary>
        /// Broadcasts an already-registered world entity to every connected peer - the
        /// shared broadcast the fallback needs for the atlas shards it spawns beside its
        /// deposits. Returns how many peers took it.
        /// </summary>
        internal static int BroadcastToAll(long entityId, WorldEntity registration)
        {
            if (WorldsAdriftRebornGameServer.ResourceInterest.RegisterRuntime(entityId, registration))
            {
                return 0;
            }
            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastEntity(peer, entityId, registration))
                {
                    reached++;
                }
            }
            return reached;
        }

        /// <summary>The registry key for a handshake deposit, namespaced by island so two islands never collide.</summary>
        internal static string KeyFor(long islandEntityId, int index)
        {
            return IslandResourceLedger.KeyPrefix + islandEntityId + "-" + index;
        }

        /// <summary>
        /// Registers and broadcasts the biome GLOBAL entity the first time a handshake
        /// deposit is spawned, unless something (the static deposit path) already
        /// registered it. Without it the deposit exists but its rock never draws
        /// (MetalDepositVisualiser blocks on the biome table this entity carries).
        /// </summary>
        private static void EnsureGlobalEntity()
        {
            if (_globalEntityEnsured)
            {
                return;
            }
            _globalEntityEnsured = true;

            if (WorldsAdriftRebornGameServer.WorldEntities.ByKey(Multiplayer.WorldEntities.GlobalEntityKey) != null)
            {
                return; // already registered (e.g. the static WAREBORN_SPAWN_DEPOSIT path)
            }

            WorldEntity global = Multiplayer.WorldEntities.GlobalEntity();
            WorldsAdriftRebornGameServer.WorldEntities.Register(global);
            long globalId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(global);
            WorldsAdriftRebornGameServer.DomainHost.MarkGlobal(globalId);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastEntity(peer, globalId, global))
                {
                    reached++;
                }
            }
            System.Console.WriteLine("[info] resource-handshake: registered biome GLOBAL entity " + globalId
                + " so handshake deposits can resolve their biome and draw; broadcast to " + reached + " peer(s).");
        }

        /// <summary>
        /// Sends one entity to one peer the proven way (AssetLoadRequest -> AddEntity).
        /// No AddComponent batch: a deposit and the global entity both carry no seed
        /// components and are served best-effort over interest, exactly like the static
        /// deposit and like every other world entity this server spawns.
        /// </summary>
        private static bool BroadcastEntity(ENetPeerHandle peer, long entityId, WorldEntity registration)
        {
            SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", registration.AssetName, registration.AssetContext);
            if (!SendOPHelper.SendAddEntityOP(peer, entityId, registration.AssetName, registration.AssetContext))
            {
                System.Console.WriteLine("[error] resource-handshake: failed to send AddEntityOp for entity "
                    + entityId + " ('" + registration.Key + "') to a peer.");
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
