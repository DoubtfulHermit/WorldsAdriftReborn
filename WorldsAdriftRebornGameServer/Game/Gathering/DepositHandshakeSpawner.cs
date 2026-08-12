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
    /// branch), then broadcast AssetLoadRequest -> AddEntity to every connected peer.
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
        /// The metal type a handshake deposit represents. RECONSTRUCTED: the retail
        /// biome material table (which metal, which quality) did not survive, so a
        /// handshake deposit is a plain starter metal until refdata is recovered. The
        /// VISUALS come from the client-chosen <c>variant</c>, not from this - this only
        /// names the future salvage grant's item type (a real itemData.json row).
        /// </summary>
        internal const string DefaultMetalType = "iron";

        /// <summary>Reconstructed quality for the future grant; unused by the deposit's rendering.</summary>
        internal const int DefaultQuality = 5;

        private static bool _globalEntityEnsured;

        /// <summary>
        /// Spawns one admitted deposit on the given island and returns its entity id,
        /// or null if nothing could be spawned. Registers the biome GLOBAL entity first
        /// (once) so the client's MetalDepositVisualiser can resolve the deposit's biome
        /// and actually draw the rock - see <see cref="Multiplayer.WorldEntities.GlobalEntity"/>.
        /// </summary>
        internal static long? Spawn(long islandEntityId, AdmittedDeposit admitted)
        {
            EnsureGlobalEntity();

            string key = KeyFor(islandEntityId, admitted.Index);

            // A re-run of the same (island, index) - should not happen, the ledger
            // hands out monotonic indices - would collide on the registry key. Guard so
            // a defensive double call is a no-op rather than a throw.
            if (WorldsAdriftRebornGameServer.WorldEntities.ByKey(key) != null)
            {
                return WorldsAdriftRebornGameServer.WorldEntities.BoundEntityIdFor(key);
            }

            MetalNode node = new MetalNode(
                key,
                DefaultMetalType,
                DefaultQuality,
                admitted.Position,
                isDeposit: true,
                variantId: admitted.Variant);

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
                    node.MetalType,
                    new Multiplayer.Gathering.YieldRule(node.MetalType, amountPerUnit: 1));

                WorldsAdriftRebornGameServer.MetalHarvest.Place(
                    entityId,
                    Multiplayer.MetalDeposits.YieldUnits,
                    shotsToDeplete: Multiplayer.MetalDeposits.ShotsToDeplete);
            }

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastEntity(peer, entityId, registration))
                {
                    reached++;
                }
            }

            System.Console.WriteLine("[info] resource-handshake: spawned DEPOSIT '" + key + "' as entity "
                + entityId + " on island " + islandEntityId + " at " + node.Position
                + " variant '" + node.VariantId + "' (" + Multiplayer.MetalDeposits.ShotsToDeplete
                + " shots -> " + Multiplayer.MetalDeposits.YieldUnits + " units), broadcast to "
                + reached + " peer(s). Late joiners get it via the connect-time spawn plan.");

            return entityId;
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
