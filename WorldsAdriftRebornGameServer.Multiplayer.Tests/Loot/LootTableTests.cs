using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Loot
{
    /// <summary>
    /// The loot roll and the tier table it draws from.
    ///
    /// The property that matters most here is DETERMINISM, and it is not a
    /// performance concern. Contents are not stored anywhere yet, so they are
    /// rederived on every 1081 serve - once per peer per checkout. A random roll
    /// would mean two players at the same chest see different loot, a re-checkout
    /// rerolls it, and pacing away and back is a farm.
    /// </summary>
    public class LootTableTests
    {
        [Fact]
        public void TheSameContainerAlwaysRollsTheSameItems()
        {
            IReadOnlyList<LootDrop> first = LootTable.Roll("loot-release-650186469-2", 3);
            IReadOnlyList<LootDrop> second = LootTable.Roll("loot-release-650186469-2", 3);

            Assert.Equal(first.Select(d => d.ItemTypeId), second.Select(d => d.ItemTypeId));
            Assert.NotEmpty(first);
        }

        [Fact]
        public void DifferentContainersRollDifferentItems()
        {
            // Not a guarantee for any one pair - it is a hash - so this asserts the
            // population is varied rather than that two named keys differ.
            HashSet<string> seen = new();
            for (int i = 0; i < 40; i++)
            {
                foreach (LootDrop drop in LootTable.Roll(LootContainers.KeyFor(i), 1))
                {
                    seen.Add(drop.ItemTypeId);
                }
            }

            Assert.True(seen.Count > 10,
                "40 containers produced only " + seen.Count + " distinct items; the roll is not spreading");
        }

        [Fact]
        public void EveryRollIsWithinTheTunedItemCount()
        {
            for (int tier = LootScrapTable.MinTier; tier <= LootScrapTable.MaxTier; tier++)
            {
                for (int i = 0; i < 200; i++)
                {
                    IReadOnlyList<LootDrop> drops = LootTable.Roll("loot-" + i, tier);
                    Assert.InRange(drops.Count, LootTable.MinItems, LootTable.MaxItems);
                }
            }
        }

        [Fact]
        public void AContainerNeverHoldsTheSameItemTwice()
        {
            for (int i = 0; i < 300; i++)
            {
                IReadOnlyList<LootDrop> drops = LootTable.Roll("loot-" + i, 4);
                Assert.Equal(drops.Count, drops.Select(d => d.ItemTypeId).Distinct().Count());
            }
        }

        [Fact]
        public void EveryRolledItemBelongsToTheIslandTierItWasRolledFor()
        {
            for (int tier = LootScrapTable.MinTier; tier <= LootScrapTable.MaxTier; tier++)
            {
                for (int i = 0; i < 120; i++)
                {
                    foreach (LootDrop drop in LootTable.Roll("loot-" + i, tier))
                    {
                        LootScrapEntry? entry = LootScrapTable.ById(drop.ItemTypeId);
                        Assert.NotNull(entry);
                        Assert.Contains(tier, entry!.Tiers);
                    }
                }
            }
        }

        [Fact]
        public void AnOutOfRangeTierIsClampedRatherThanEmptyingTheChest()
        {
            // Survey tier and MapFile cell tier disagree on at least one real island
            // (Holy Ruins), and both are preserved facts. A disagreement must never
            // be able to empty a container.
            Assert.NotEmpty(LootTable.Roll("loot-0", 0));
            Assert.NotEmpty(LootTable.Roll("loot-0", 99));
            Assert.NotEmpty(LootTable.Roll("loot-0", -3));
        }

        [Fact]
        public void AnUnnamedContainerRollsNothingRatherThanThrowing()
        {
            Assert.Empty(LootTable.Roll(null, 1));
            Assert.Empty(LootTable.Roll("", 1));
        }

        [Fact]
        public void ScrapIsNotStackableSoEveryDropIsOneItem()
        {
            foreach (LootDrop drop in LootTable.Roll("loot-7", 2))
            {
                Assert.Equal(1, drop.Amount);
                Assert.Equal(0, drop.Quality);
            }
        }

        [Fact]
        public void EveryTierHasEnoughScrapToFillAContainerWithoutRepeating()
        {
            for (int tier = LootScrapTable.MinTier; tier <= LootScrapTable.MaxTier; tier++)
            {
                Assert.True(LootScrapTable.ForTier(tier).Count > LootTable.MaxItems,
                    "tier " + tier + " has only " + LootScrapTable.ForTier(tier).Count
                    + " eligible scrap rows, which is not enough for a draw without replacement");
            }
        }

        [Fact]
        public void TheScrapTableLoadedAndEveryRowLooksLikeRetailData()
        {
            Assert.NotEmpty(LootScrapTable.All);

            foreach (LootScrapEntry entry in LootScrapTable.All)
            {
                Assert.StartsWith("scrapItem-", entry.ItemTypeId);
                Assert.InRange(entry.Width, 1, 8);
                Assert.InRange(entry.Height, 1, 8);
                Assert.NotEmpty(entry.Tiers);
                Assert.All(entry.Tiers,
                    t => Assert.InRange(t, LootScrapTable.MinTier, LootScrapTable.MaxTier));
            }
        }

        [Fact]
        public void NoScrapRowIsWiderOrTallerThanTheContainerGrid()
        {
            // A row that cannot fit even an empty container would be silently dropped
            // by the stocking pass forever.
            foreach (LootScrapEntry entry in LootScrapTable.All)
            {
                Assert.True(entry.Width <= LootContainers.GridWidth
                            && entry.Height <= LootContainers.GridHeight,
                    entry.ItemTypeId + " is " + entry.Width + "x" + entry.Height
                    + ", which does not fit a " + LootContainers.GridWidth + "x"
                    + LootContainers.GridHeight + " container");
            }
        }
    }
}
