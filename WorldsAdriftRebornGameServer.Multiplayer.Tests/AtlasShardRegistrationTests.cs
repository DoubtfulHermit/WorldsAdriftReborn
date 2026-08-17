using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The atlas shard's spawn wiring through <see cref="WorldEntities.Default"/>: it
    /// is lodged in the proven deposit only when deposits are on and the atlas switch
    /// is not killed, keyed to pair with its host, and registered AFTER that host so
    /// the deposit's shared entity id is bound when the shard resolves its rockCoreId.
    /// The lodged/released/collected state machine is pinned in
    /// <see cref="AtlasShardRegistryTests"/>; this pins that the right entity is placed.
    /// </summary>
    public class AtlasShardRegistrationTests
    {
        private static WorldEntityRegistry Build(bool includeDeposit, bool includeAtlasShard)
        {
            return WorldEntities.Default(
                new EntityIdAllocator(),
                includeMetal: false,
                includeTree: false,
                includeDeposit: includeDeposit,
                includeAtlasShard: includeAtlasShard);
        }

        private static int ShardCountIn(WorldEntityRegistry r) =>
            r.Registrations.Count(e => e.AssetName == AtlasShardCatalogue.AssetName);

        [Fact]
        public void NoShardWithoutADepositToLodgeItIn()
        {
            // A shard needs a live host core to render and be mined loose, so it never
            // spawns without deposits even with the atlas switch on.
            WorldEntityRegistry r = Build(includeDeposit: false, includeAtlasShard: true);
            Assert.Equal(0, ShardCountIn(r));
        }

        [Fact]
        public void TheAtlasKillSwitchSuppressesTheShard()
        {
            WorldEntityRegistry r = Build(includeDeposit: true, includeAtlasShard: false);
            Assert.Equal(0, ShardCountIn(r));
            // ...but the deposit itself is unaffected.
            Assert.NotNull(r.ByKey(MetalDeposits.KeyFor(0)));
        }

        [Fact]
        public void EnablingDepositsLodgesOneShardInTheProvenDeposit()
        {
            WorldEntityRegistry r = Build(includeDeposit: true, includeAtlasShard: true);
            Assert.Equal(1, ShardCountIn(r));

            WorldEntity? shard = r.ByKey(AtlasShardCatalogue.KeyFor(0));
            Assert.NotNull(shard);
            Assert.Equal("MetalDepositAtlas", shard!.AssetName);
        }

        [Fact]
        public void TheShardIsRegisteredAfterItsHostDepositSoTheHostIdIsBound()
        {
            WorldEntityRegistry r = Build(includeDeposit: true, includeAtlasShard: true);
            var keys = r.Registrations.Select(e => e.Key).ToList();
            int depositAt = keys.IndexOf(MetalDeposits.KeyFor(0));
            int shardAt = keys.IndexOf(AtlasShardCatalogue.KeyFor(0));
            Assert.True(depositAt >= 0 && shardAt >= 0);
            Assert.True(depositAt < shardAt,
                "the host deposit must be registered before the shard so its entity id is bound");
        }

        [Fact]
        public void TheShardSitsOnItsHostDeposit()
        {
            // The shard ENTITY is centred on its host rock - the visible embedding is the
            // client-side alignment to the core's authored ScrapSlots, not a server-side
            // offset. An invented offset here is what produced "a shard on the floor".
            WorldEntityRegistry r = Build(includeDeposit: true, includeAtlasShard: true);
            WorldEntity deposit = r.ByKey(MetalDeposits.KeyFor(0))!;
            WorldEntity shard = r.ByKey(AtlasShardCatalogue.KeyFor(0))!;

            Assert.Equal(AtlasShardCatalogue.LodgedPositionFor(deposit.Position), shard.Position);
            Assert.Equal(deposit.Position, shard.Position);
        }

        [Fact]
        public void AShardKeyNamesItsHostDepositSoAnySpawnerCanLodgeOne()
        {
            // The handshake spawner registers deposits the client ground-checked, keyed
            // "handshake-deposit-<island>-N". The shard key must carry that host verbatim
            // so registration can bind the rockCoreId without a table index.
            const string handshakeHost = "handshake-deposit-1431299145-7";

            string shardKey = AtlasShardCatalogue.KeyForHost(handshakeHost);
            Assert.True(AtlasShardCatalogue.IsShardKey(shardKey));
            Assert.Equal(handshakeHost, AtlasShardCatalogue.HostKeyOf(shardKey));
            // It has no static placement index, and that is fine.
            Assert.Null(AtlasShardCatalogue.IndexOf(shardKey));

            // The static path is the same function with a "deposit-N" host.
            Assert.Equal(MetalDeposits.KeyFor(3),
                AtlasShardCatalogue.HostKeyOf(AtlasShardCatalogue.KeyFor(3)));
            Assert.Equal(3, AtlasShardCatalogue.IndexOf(AtlasShardCatalogue.KeyFor(3)));
        }

        [Fact]
        public void AShardEntityCanBeBuiltForAnyHostKey()
        {
            const string handshakeHost = "handshake-deposit-1431299145-2";
            FixedPointPosition hostPos = FixedPointPosition.FromMetres(120.0, 8.0, -40.0);

            WorldEntity shard = WorldEntities.AtlasShardEntity(handshakeHost, hostPos);

            Assert.Equal(AtlasShardCatalogue.KeyForHost(handshakeHost), shard.Key);
            Assert.Equal(AtlasShardCatalogue.AssetName, shard.AssetName);
            Assert.Equal(hostPos, shard.Position);
            Assert.Empty(shard.SeedComponents);
        }

        [Fact]
        public void TheShardSeedsNothingUnprompted()
        {
            // Like the deposit and nugget: the client checks it out and asks for its
            // 1305/2102/1210/190602 over interest, answered best-effort.
            WorldEntityRegistry r = Build(includeDeposit: true, includeAtlasShard: true);
            WorldEntity shard = r.ByKey(AtlasShardCatalogue.KeyFor(0))!;
            Assert.Empty(shard.SeedComponents);
        }
    }
}
