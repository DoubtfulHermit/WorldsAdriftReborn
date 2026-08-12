using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// THE SAFE FAILURE MODE. Places the existing hand-placed <see cref="MetalDeposits"/>
    /// table at RUNTIME when the island resource handshake produced nothing before its
    /// deadline (see <see cref="IslandResourceFallback"/>).
    ///
    /// WHY RUNTIME AND NOT THE BOOT REGISTRY. The static deposits used to be registered at
    /// boot behind <c>WAREBORN_SPAWN_DEPOSIT=1</c>, so every joiner's connect-time spawn
    /// plan walked them. That cannot express "only if the handshake failed" - at boot
    /// nobody has connected, so nothing has been asked and nothing has replied. Spawning
    /// them here, through the SAME <see cref="DepositHandshakeSpawner.SpawnDepositNode"/>
    /// the handshake uses, means:
    ///   - a live session gets them broadcast immediately (AssetLoadRequest -> AddEntity);
    ///   - they enter the live <c>WorldEntityRegistry</c>, so LATE joiners get them from
    ///     the connect-time spawn plan for free, exactly like a handshake deposit;
    ///   - they land in the same Nodes / HarvestReward / MetalHarvest ledgers, so the
    ///     crust-core mining loop is identical on both paths;
    ///   - and the boot registry stays clean, so the two sets can never both appear.
    ///
    /// ATLAS SHARDS. The static path's shards are spawned here too, by CALLING the
    /// existing shard factories and ledger (<see cref="WorldEntities.AtlasShardEntity"/>,
    /// <c>AtlasShards.Register</c>) - no shard logic is duplicated or modified. Shards are
    /// paired to deposits by INDEX (<c>atlas-shard-N</c> lodges in <c>deposit-N</c>), which
    /// is why the fallback keeps the static table's own <c>deposit-N</c> keys.
    ///
    /// MULTIPLAYER CLASSIFICATION: a one-shot burst of AddEntity ops on the main poll
    /// loop, then ordinary interest serving. No per-frame stream, no relay. It fires at
    /// most ONCE per island for the life of the process - the latch lives in
    /// <see cref="IslandResourceLedger.MarkFallbackFired"/>, upstream of here.
    /// </summary>
    internal static class DepositFallbackSpawner
    {
        /// <summary>
        /// How many hand-placed deposits the fallback lays down. Reuses the SAME
        /// <c>WAREBORN_DEPOSIT_COUNT</c> knob the static boot path used, clamped to
        /// [1, the full table], so an operator who had tuned that knob keeps its meaning.
        /// </summary>
        internal static int Count()
        {
            return SpawnCountPolicy.CountFrom(
                Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_COUNT"),
                MetalDeposits.HavenPlacements.Count);
        }

        /// <summary>
        /// Whether the fallback should also lodge atlas shards. Same
        /// <c>WAREBORN_SPAWN_ATLAS</c> kill switch as the boot path (default ON) - dropping
        /// them silently would make the fallback a quiet regression of the atlas vertical
        /// rather than a like-for-like substitute.
        /// </summary>
        internal static bool IncludeShards()
        {
            return Environment.GetEnvironmentVariable("WAREBORN_SPAWN_ATLAS") != "0";
        }

        /// <summary>
        /// Spawns the hand-placed deposits (and their shards) and returns how many
        /// DEPOSITS were spawned. Never throws into the poll loop: a failure on one
        /// deposit is logged and the rest still go down, because the entire point of this
        /// path is that the world is not left empty.
        /// </summary>
        internal static int SpawnStaticPlacements()
        {
            IReadOnlyList<MetalNode> deposits = MetalDeposits.Haven(Count());
            List<long> depositIds = new List<long>(deposits.Count);
            int spawned = 0;

            foreach (MetalNode node in deposits)
            {
                long? id = null;
                try
                {
                    id = DepositHandshakeSpawner.SpawnDepositNode(node, "the static fallback table");
                }
                catch (Exception e)
                {
                    Console.WriteLine("[error] resource-handshake: fallback deposit '" + node.Key
                        + "' failed to spawn: " + e.Message);
                }

                depositIds.Add(id ?? 0);
                if (id != null)
                {
                    spawned++;
                }
            }

            if (IncludeShards())
            {
                SpawnShards(deposits, depositIds);
            }

            return spawned;
        }

        /// <summary>
        /// Lodges one atlas shard in each fallback deposit the deterministic
        /// <see cref="AtlasSpawnPolicy"/> rule selects, using the existing shard entity
        /// factory and ledger. Runs AFTER every deposit is registered, so each shard's host
        /// entity id is already bound - the same ordering the boot registry relies on.
        /// </summary>
        private static void SpawnShards(IReadOnlyList<MetalNode> deposits, IReadOnlyList<long> depositIds)
        {
            int oneIn = AtlasSpawnPolicy.OneInDeposits(Environment.GetEnvironmentVariable("WAREBORN_ATLAS_RATE"));
            int lodged = 0;

            for (int i = 0; i < deposits.Count; i++)
            {
                if (!AtlasSpawnPolicy.DepositCarriesShard(i, oneIn) || depositIds[i] == 0)
                {
                    continue;
                }

                try
                {
                    WorldEntity shard = Multiplayer.WorldEntities.AtlasShardEntity(i, deposits[i].Position);
                    if (WorldsAdriftRebornGameServer.WorldEntities.ByKey(shard.Key) != null)
                    {
                        continue; // already placed (a defensive second call)
                    }

                    WorldsAdriftRebornGameServer.WorldEntities.Register(shard);
                    long shardId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(shard);

                    // Same ledger call AddWorldEntity makes for a boot-registered shard, so
                    // its 1305 rockCoreId and the host's 2103 attachedEntities wire up
                    // identically. Idempotent by shard entity id.
                    WorldsAdriftRebornGameServer.AtlasShards.Register(
                        shardId, depositIds[i], AtlasShardCatalogue.DefaultSlotId);

                    DepositHandshakeSpawner.BroadcastToAll(shardId, shard);
                    lodged++;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[error] resource-handshake: fallback atlas shard for deposit-" + i
                        + " failed to spawn: " + e.Message);
                }
            }

            if (lodged > 0)
            {
                Console.WriteLine("[info] resource-handshake: fallback lodged " + lodged
                    + " atlas shard(s) in the hand-placed deposits (one per " + oneIn + ").");
            }
        }
    }
}
