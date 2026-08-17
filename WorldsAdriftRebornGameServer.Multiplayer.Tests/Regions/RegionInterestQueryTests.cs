using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Regions
{
    public sealed class RegionInterestQueryTests
    {
        [Fact]
        public void Region_candidates_preserve_offered_order_and_retained_previous_region_entities()
        {
            WorldEntity havenA = Entity("tree-haven-a", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity trades = Entity("tree-trades", IslandCatalog.TradesChallenge.GlobalOrigin);
            WorldEntity havenB = Entity("tree-haven-b", IslandCatalog.Haven.GlobalOrigin);
            RegionInterestQuery query = Query(havenA, trades, havenB);

            IReadOnlyList<WorldEntity> candidates = query.Candidates(
                RegionCatalog.HavenRegionId,
                new[] { havenA, trades, havenB },
                new HashSet<string>(StringComparer.Ordinal) { trades.Key });

            Assert.Equal(new[] { havenA.Key, trades.Key, havenB.Key },
                candidates.Select(entity => entity.Key));
        }

        [Fact]
        public void Other_region_entities_are_not_candidates_when_not_retained()
        {
            WorldEntity haven = Entity("tree-haven", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity trades = Entity("tree-trades", IslandCatalog.TradesChallenge.GlobalOrigin);

            Assert.Equal(new[] { haven.Key }, Query(haven, trades)
                .Candidates(RegionCatalog.HavenRegionId, new[] { haven, trades })
                .Select(entity => entity.Key));
        }

        [Fact]
        public void Runtime_registration_joins_the_same_region_query()
        {
            WorldEntity boot = Entity("tree-boot", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity runtime = Entity("tree-runtime", IslandCatalog.Haven.GlobalOrigin);
            RegionInterestQuery query = Query(boot);

            query.Register(runtime, RegionCatalog.HavenRegionId);

            Assert.Equal(new[] { boot.Key, runtime.Key }, query
                .Candidates(RegionCatalog.HavenRegionId, new[] { boot, runtime })
                .Select(entity => entity.Key));
        }

        [Fact]
        public void Directory_routing_preserves_the_existing_island_reconcile_result()
        {
            WorldEntity havenA = Entity("tree-haven-a", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity trades = Entity("tree-trades", IslandCatalog.TradesChallenge.GlobalOrigin);
            WorldEntity havenB = Entity("tree-haven-b", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity[] offered = { havenA, trades, havenB };
            Dictionary<string, long> ids = new(StringComparer.Ordinal)
            {
                [havenA.Key] = 11,
                [trades.Key] = 12,
                [havenB.Key] = 13,
            };
            HashSet<long> loaded = new() { 12 };
            IslandResource[] oldInput = offered.Select(entity => new IslandResource(
                ids[entity.Key], entity.Position,
                entity == trades ? IslandCatalog.TradesChallengeId : IslandCatalog.HavenId))
                .ToArray();

            IReadOnlyList<(long Id, FixedPointPosition Position)> before =
                IslandResourceInterestPolicy.ReconcileSet(
                    IslandCatalog.HavenId, oldInput, loaded);
            HashSet<string> retainedKeys = loaded
                .Select(id => ids.Single(pair => pair.Value == id).Key)
                .ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<WorldEntity> routed = Query(offered).Candidates(
                RegionCatalog.HavenRegionId, offered, retainedKeys);
            IReadOnlyList<(long Id, FixedPointPosition Position)> after =
                IslandResourceInterestPolicy.ReconcileSet(
                    IslandCatalog.HavenId,
                    routed.Select(entity => oldInput.Single(resource =>
                        resource.EntityId == ids[entity.Key])),
                    loaded);

            Assert.Equal(before, after);
        }

        [Fact]
        public void Shared_region_still_reconciles_only_the_active_island_and_loaded_carryovers()
        {
            WorldEntity haven = Entity("tree-haven", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity trades = Entity("tree-trades", IslandCatalog.TradesChallenge.GlobalOrigin);
            WorldEntityRegistry registry = new(new EntityIdAllocator());
            registry.Register(haven);
            registry.Register(trades);
            IslandRegistry islands = IslandRegistry.CreateDefault();
            RegionId sharedId = new("shared-test-region");
            RegionRegistry regions = new(islands);
            regions.Register(new RegionDefinition(sharedId, "Shared test region", new[]
            {
                IslandCatalog.HavenId,
                IslandCatalog.TradesChallengeId,
            }));
            RegionInterestQuery query = new(WorldDirectory.Build(registry, islands, regions));

            // Region routing intentionally offers both islands. Exact island
            // lifecycle remains the old IslandResourceInterestPolicy's job.
            IReadOnlyList<WorldEntity> offered = query.Candidates(
                sharedId, new[] { haven, trades });
            IReadOnlyList<(long Id, FixedPointPosition Position)> result =
                IslandResourceInterestPolicy.ReconcileSet(
                    IslandCatalog.HavenId,
                    offered.Select(entity => new IslandResource(
                        entity == haven ? 21 : 22,
                        entity.Position,
                        entity == haven ? IslandCatalog.HavenId : IslandCatalog.TradesChallengeId)),
                    new HashSet<long>());

            Assert.Equal(new[] { 21L }, result.Select(candidate => candidate.Id));
        }

        [Fact]
        public void Unclassified_candidate_fails_instead_of_silently_disappearing()
        {
            WorldEntity known = Entity("tree-known", IslandCatalog.Haven.GlobalOrigin);
            WorldEntity unknown = Entity("tree-unknown", IslandCatalog.Haven.GlobalOrigin);
            RegionInterestQuery query = Query(known);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                query.Candidates(RegionCatalog.HavenRegionId, new[] { known, unknown }));

            Assert.Contains(unknown.Key, error.Message);
        }

        private static RegionInterestQuery Query(params WorldEntity[] entities)
        {
            WorldEntityRegistry registry = new(new EntityIdAllocator());
            foreach (WorldEntity entity in entities) registry.Register(entity);
            IslandRegistry islands = IslandRegistry.CreateDefault();
            WorldDirectory directory = WorldDirectory.Build(
                registry, islands, RegionRegistry.CreateDefault(islands));
            return new RegionInterestQuery(directory);
        }

        private static WorldEntity Entity(string key, FixedPointPosition position) =>
            new(key, "asset", WorldEntities.DefaultAssetContext, position);
    }
}
