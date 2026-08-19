using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Loot
{
    /// <summary>
    /// THE TESTS THAT FAIL WHEN CONTAINERS STOP BEING SPAWNED, OR STOP BEING
    /// VISIBLE.
    ///
    /// This file exists because of a specific failure mode this project has already
    /// paid for once. The tree work shipped with a green suite while the feature was
    /// invisible in production, because every test covered the pure model and none
    /// covered the path that actually runs. Trees were built and shown to nobody for
    /// days.
    ///
    /// So the assertions here are deliberately about the LIVE PATH, not the model:
    ///
    ///   * <see cref="EnablingLootPlacesContainersOnHaven"/> and
    ///     <see cref="EnablingLootPlacesContainersOnEveryReleaseIsland"/> go through
    ///     <c>WorldEntities.Default</c> - the same call the running server makes -
    ///     so deleting the registration block breaks them.
    ///   * <see cref="EveryContainerKeyIsStreamedAndActivated"/> asserts the key
    ///     prefix is in <c>ResourceInterestPolicy.IsStreamedResourceKey</c>. A key
    ///     outside that allowlist is broadcast eagerly instead of streamed AND is
    ///     skipped by <c>ActivateBoundResources</c>, which is exactly the "renders
    ///     but does nothing" bug the handover records. Nothing else in the suite
    ///     would notice.
    ///   * <see cref="EveryContainerUsesAClientResolvablePrefab"/> asserts the asset
    ///     name is in the runtime-validated client census. A prefab the client cannot
    ///     load is an invisible entity carrying an E prompt.
    ///   * <see cref="ContainersAreSpawnedAfterThePlayer"/> keeps a chest off the
    ///     loading screen's critical path.
    /// </summary>
    public class LootContainerSpawnTests
    {
        private static WorldEntityRegistry Build(bool includeLoot, string? lootCount = null,
            string? districts = null)
        {
            return WorldEntities.Default(
                new EntityIdAllocator(),
                includeMetal: false,
                includeTree: false,
                releaseWorldDistricts: districts,
                includeLootContainers: includeLoot,
                lootCountEnv: lootCount);
        }

        private static WorldEntity[] ContainersIn(WorldEntityRegistry registry) =>
            registry.Registrations.Where(e => LootContainers.IsLootKey(e.Key)).ToArray();

        [Fact]
        public void LootIsOffByDefaultSoNoExistingSessionChanges()
        {
            Assert.Empty(ContainersIn(Build(includeLoot: false)));
        }

        [Fact]
        public void EnablingLootPlacesContainersOnHaven()
        {
            WorldEntity[] containers = ContainersIn(Build(includeLoot: true));

            // The number Haven is hand-tuned to, not a number this test invented.
            Assert.Equal(HavenSurface.LootTargetCount, containers.Length);
            Assert.All(containers, c => Assert.Equal(LootContainers.AssetName, c.AssetName));

            // Keys are dense from zero, because the interest service, the ledger and
            // the loot roll all key off them and a gap would be an unrollable chest.
            for (int i = 0; i < containers.Length; i++)
            {
                Assert.Contains(containers, c => c.Key == LootContainers.KeyFor(i));
            }
        }

        [Fact]
        public void TheHavenCountKnobClampsWithoutLosingTheFirstSeat()
        {
            WorldEntity[] one = ContainersIn(Build(includeLoot: true, lootCount: "1"));
            Assert.Single(one);
            Assert.Equal(LootContainers.KeyFor(0), one[0].Key);

            // Over the table size clamps to the table, never throws.
            Assert.Equal(HavenSurface.LootTargetCount,
                ContainersIn(Build(includeLoot: true, lootCount: "9999")).Length);
        }

        [Fact]
        public void EnablingLootPlacesContainersOnEveryReleaseIsland()
        {
            // One real tier-1 cell rather than the whole world: enough to prove the
            // release branch runs, fast enough to stay in the fast suite.
            WorldEntity[] containers = ContainersIn(Build(includeLoot: true, districts: "tier1"))
                .Where(c => c.Key.StartsWith(ReleaseWorldLoot.KeyPrefix)).ToArray();

            Assert.NotEmpty(containers);

            int expected = 0;
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                ReleaseLootIsland? seats = ReleaseLootCatalog.ForWorkshopId(island.Survey.WorkshopId);
                expected += seats?.Points.Count ?? 0;
            }

            Assert.Equal(expected, containers.Length);
        }

        [Fact]
        public void TheReleaseBranchIsGatedTooSoTurningLootOffTurnsItOffEverywhere()
        {
            Assert.Empty(ContainersIn(Build(includeLoot: false, districts: "tier1")));
        }

        [Fact]
        public void EveryContainerKeyIsStreamedAndActivated()
        {
            // The single line in ResourceInterestPolicy this depends on. Both the
            // Haven form and the release form, because they are different strings.
            Assert.True(ResourceInterestPolicy.IsStreamedResourceKey(LootContainers.KeyFor(0)));
            Assert.True(ResourceInterestPolicy.IsStreamedResourceKey(
                ReleaseWorldLoot.KeyFor("650186469", 3)));

            foreach (WorldEntity container in ContainersIn(Build(includeLoot: true, districts: "tier1")))
            {
                Assert.True(ResourceInterestPolicy.IsStreamedResourceKey(container.Key),
                    "container key '" + container.Key + "' is not streamed, so it would be "
                    + "broadcast to every peer at once and never activated");
            }
        }

        [Fact]
        public void EveryContainerUsesAClientResolvablePrefab()
        {
            Assert.True(Multiplayer.Ship.ClientEntityPrefabs.CanResolve(LootContainers.AssetName),
                LootContainers.AssetName + " is not in the client prefab census, so the "
                + "client could never load it and the chest would be invisible");

            // And BARE - the client appends the worker suffix itself, so a name that
            // already carries it resolves to nothing.
            Assert.DoesNotContain("_unityclient", LootContainers.AssetName);
        }

        [Fact]
        public void ContainersAreSpawnedAfterThePlayer()
        {
            Assert.All(ContainersIn(Build(includeLoot: true)),
                c => Assert.Equal(SpawnOrder.AfterPlayer, c.Order));
        }

        [Fact]
        public void ContainersCarryNoSeedComponentsSoTheInterestServeAnswersThem()
        {
            // A seed batch is all-or-nothing; the interest serve is best-effort. A
            // container needs 1210 AND 1081 and it asks for both itself.
            Assert.All(ContainersIn(Build(includeLoot: true)),
                c => Assert.Empty(c.SeedComponents));
        }

        [Fact]
        public void EveryReleaseContainerKeyResolvesBackToItsIslandTier()
        {
            foreach (WorldEntity container in ContainersIn(Build(includeLoot: true, districts: "tier1"))
                         .Where(c => c.Key.StartsWith(ReleaseWorldLoot.KeyPrefix)))
            {
                int? tier = ReleaseWorldLoot.TierForKey(container.Key);
                Assert.NotNull(tier);
                Assert.InRange(tier!.Value, LootScrapTable.MinTier, LootScrapTable.MaxTier);
            }

            // A Haven key belongs to no release island and must say so rather than
            // resolving to a wrong tier.
            Assert.Null(ReleaseWorldLoot.TierForKey(LootContainers.KeyFor(0)));
            Assert.Null(ReleaseWorldLoot.TierForKey("tree-4"));
            Assert.Null(ReleaseWorldLoot.TierForKey(null));
        }
    }
}
