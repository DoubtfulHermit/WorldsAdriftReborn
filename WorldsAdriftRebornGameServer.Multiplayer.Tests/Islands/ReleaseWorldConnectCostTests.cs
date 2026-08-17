using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// What one fresh connect costs at Wilderness scale.
    ///
    /// The immutable spawn plan is a PROCESS-WIDE list built once, so its length is
    /// not per-peer cost: every release terrain root is AfterPlayer (and therefore
    /// IslandTerrainConnectPolicy.IsManaged) and every release deposit, shard and
    /// databank is a streamed resource key (and therefore
    /// ConnectInterestPolicy.IsGateable), and the server fast-forwards a gated,
    /// out-of-range step in one turn without sending anything. These tests assert
    /// the property that makes that true - registering 47 terrains must not put 47
    /// terrains or hundreds of resources into what a joining peer actually walks.
    ///
    /// The three radii deliberately stay separate here, exactly as the runtime keeps
    /// them: the CONNECT bubble (WAREBORN_INTEREST_INITIAL_RADIUS_M, 45 m), the LIVE
    /// resource bubble (WAREBORN_INTEREST_RADIUS_M, 120 m in production) and the
    /// TERRAIN radius (WAREBORN_TERRAIN_LOAD_RADIUS_M, 4000 m in production).
    /// Conflating them is the recorded cause of a past crash.
    /// </summary>
    public sealed class ReleaseWorldConnectCostTests
    {
        private const double ConnectRadiusMetres = InterestPolicy.DefaultInitialRadiusMetres; // 45
        private const double ShipRadiusMetres = 400;
        private const double ProductionTerrainRadiusMetres = 4000;

        private static WorldEntityRegistry TierOneWorld() =>
            WorldEntities.Default(new EntityIdAllocator(), releaseWorldDistricts: "tier1");

        /// <summary>
        /// Mirrors WorldsAdriftRebornGameServer's BarrierInitial for a world with no
        /// ships: ConnectGatePosition is the entity's own position when it is not a
        /// mounted part of a hull.
        /// </summary>
        private static bool StreamsAtConnect(
            IslandRegistry islands, WorldEntity entity, double terrainRadiusMetres) =>
            IslandTerrainConnectPolicy.IsInitial(
                ConnectInterestPolicy.IsInitial(
                    entity.Key,
                    isMountedPart: false,
                    baseInitial: LoadBarrierPolicy.IsInitialKey(entity.Key),
                    resourceInterestEnabled: true,
                    SpawnPolicy.PlayerSpawnPosition,
                    entity.Position,
                    ConnectRadiusMetres,
                    ShipRadiusMetres),
                IslandTerrainConnectPolicy.IsManaged(
                    terrainInterestEnabled: true, islands.ByWorldEntityKey(entity.Key)),
                SpawnPolicy.PlayerSpawnPosition,
                islands.ByWorldEntityKey(entity.Key),
                terrainRadiusMetres);

        [Fact]
        public void Every_release_terrain_and_resource_is_gateable_so_none_is_forced_into_the_plan()
        {
            WorldEntityRegistry world = TierOneWorld();
            IslandRegistry islands = IslandRegistry.CreateReleaseWorld("tier1");

            IReadOnlyList<WorldEntity> release = world.Registrations
                .Where(entity => entity.Key.Contains("-release-", StringComparison.Ordinal)
                    || entity.Key.StartsWith("island-", StringComparison.Ordinal))
                .ToArray();
            Assert.NotEmpty(release);

            Assert.All(release, entity =>
            {
                Assert.Equal(SpawnOrder.AfterPlayer, entity.Order);
                bool gateable = ConnectInterestPolicy.IsGateable(
                        entity.Key, isMountedPart: false, resourceInterestEnabled: true)
                    || IslandTerrainConnectPolicy.IsManaged(
                        terrainInterestEnabled: true, islands.ByWorldEntityKey(entity.Key));
                Assert.True(gateable, entity.Key + " cannot be skipped at connect");
            });
        }

        /// <summary>
        /// The measured fact: the nearest tier-1 island is 9.33 km from the Haven
        /// spawn and production loads terrain at 4 km, so a fresh Haven connect
        /// streams NOTHING from the Wilderness. Every step it would otherwise walk
        /// is gated out in one turn.
        /// </summary>
        [Fact]
        public void A_fresh_haven_connect_streams_no_tier_one_terrain_and_no_tier_one_resource()
        {
            WorldEntityRegistry world = TierOneWorld();
            IslandRegistry islands = IslandRegistry.CreateReleaseWorld("tier1");

            IReadOnlyList<WorldEntity> streamed = world.Registrations
                .Where(entity => StreamsAtConnect(islands, entity, ProductionTerrainRadiusMetres))
                .ToArray();

            Assert.DoesNotContain(streamed, entity =>
                entity.Key.Contains("-release-", StringComparison.Ordinal));
            Assert.DoesNotContain(streamed, entity =>
                islands.ByWorldEntityKey(entity.Key) is { } island
                && island.Id != IslandCatalog.HavenId);
            // Haven's own ground still streams before the player, unconditionally.
            Assert.Contains(streamed, entity =>
                islands.ByWorldEntityKey(entity.Key)?.Id == IslandCatalog.HavenId);
        }

        /// <summary>
        /// The connect cost must not move when the Wilderness is added. Same spawn
        /// point, same radii, and the same streamed set as a Haven-only world apart
        /// from ONE entity: the world-wide biome lookup table, which every deposit
        /// needs and which a deposit-less Haven baseline therefore does not
        /// register. It is a single global, not per island - production Haven with
        /// its own deposits enabled already carries it. The 46 islands and their
        /// 307 other entities are pure additions behind the gate.
        /// </summary>
        [Fact]
        public void Adding_the_whole_wilderness_does_not_change_what_a_joining_peer_walks()
        {
            IslandRegistry havenOnly = IslandRegistry.CreateDefault();
            IReadOnlyList<string> baseline = WorldEntities.Default(new EntityIdAllocator())
                .Registrations
                .Where(entity => StreamsAtConnect(havenOnly, entity, ProductionTerrainRadiusMetres))
                .Select(entity => entity.Key)
                .ToArray();

            IslandRegistry tier1 = IslandRegistry.CreateReleaseWorld("tier1");
            IReadOnlyList<string> wilderness = TierOneWorld().Registrations
                .Where(entity => StreamsAtConnect(tier1, entity, ProductionTerrainRadiusMetres))
                .Select(entity => entity.Key)
                .ToArray();

            Assert.Empty(baseline.Except(wilderness, StringComparer.Ordinal));
            Assert.Equal(new[] { WorldEntities.GlobalEntityKey },
                wilderness.Except(baseline, StringComparer.Ordinal));
            Assert.True(wilderness.Count < 40,
                "a fresh connect streamed " + wilderness.Count + " entities; the connect plan is ballooning");
        }

        /// <summary>
        /// Terrain fidelity for the complete rollout is the v2 compact outline, and
        /// that choice must come from the rollout being active rather than from an
        /// island happening to have a catalogue record - re-asserted here because
        /// 47 v1 bundle prefetches per peer is the cost this whole design avoids.
        /// </summary>
        [Fact]
        public void Wilderness_islands_use_the_scalable_shell_only_because_the_rollout_is_active()
        {
            ReleaseIslandRecord island = ReleaseWorldRolloutPolicy.Select("tier1")[0];

            Assert.Equal(IslandShellFidelity.CompactOutline,
                IslandShellFidelityPolicy.Choose(island, releaseWorldRolloutActive: true));
            Assert.Equal(IslandShellFidelity.RetailLod,
                IslandShellFidelityPolicy.Choose(island, releaseWorldRolloutActive: false));
        }
    }
}
