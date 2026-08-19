using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// SALVAGING A PIECE OF SCRAP - the rules, on a real grid.
    ///
    /// The property that matters most here is ATOMICITY, and it is the one a
    /// "green suite, broken game" test set would miss: every refusal below asserts
    /// the model is BYTE-FOR-BYTE unchanged, not merely that the call returned
    /// false. A salvage that ate the scrap and paid nothing is silent, permanent
    /// item loss, and the client has no rollback to notice it with.
    /// </summary>
    public class ScrapSalvagePolicyTests
    {
        private const int StackMax = 99;

        // Real footprints and a real stack ceiling: iron/lead/chestnut are 3x2 and
        // stack to 99 in itemData.json, and a scrap relic is whatever its row says.
        private static readonly Dictionary<string, ItemFootprint> Sizes = new()
        {
            ["iron"] = new ItemFootprint(3, 2),
            ["lead"] = new ItemFootprint(3, 2),
            ["chestnut"] = new ItemFootprint(3, 2),
            ["fuel"] = new ItemFootprint(2, 2),
            ["scrapItem-relic"] = new ItemFootprint(2, 2),
            ["scrapItem-hugerelic"] = new ItemFootprint(5, 3),
            ["scrapItem-barren"] = new ItemFootprint(1, 1),
            ["glider"] = new ItemFootprint(3, 4),
        };

        private static bool Footprints(string itemTypeId, out ItemFootprint footprint) =>
            Sizes.TryGetValue(itemTypeId, out footprint);

        private static int StackMaxOf(string itemTypeId) =>
            Sizes.ContainsKey(itemTypeId) ? StackMax : -1;

        /// <summary>The reward table under test, in the shape InventoryWire hands over.</summary>
        private static ScrapRewardLookup Table(
            params (string Type, ScrapReward[] Rows)[] entries)
        {
            Dictionary<string, IReadOnlyList<ScrapReward>> map = new();
            foreach ((string type, ScrapReward[] rows) in entries)
            {
                map[type] = rows;
            }

            return (string itemTypeId, out IReadOnlyList<ScrapReward> rewards) =>
                map.TryGetValue(itemTypeId, out rewards!);
        }

        private static Func<int> Ids(int from = 5000)
        {
            int next = from;
            return () => next++;
        }

        private static InventoryModel GridWith(params InventoryItem[] items)
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            foreach (InventoryItem item in items) model.Add(item);
            return model;
        }

        private static InventoryItem Scrap(int id, string type = "scrapItem-relic",
            int x = 0, int y = 0, Dictionary<string, string>? meta = null,
            string slot = InventoryItem.NotWorn, bool lockBox = false)
        {
            return new InventoryItem(id, type, 1, slot, InventoryItem.NoSlot, x, y, false,
                InventoryItem.NoSlot, 0, 0, lockBox, meta ?? new Dictionary<string, string>(), null);
        }

        private static SalvageResult Salvage(
            InventoryModel model, int itemId, int tier, ScrapRewardLookup table, Func<int>? ids = null)
        {
            return ScrapSalvagePolicy.Salvage(
                model, itemId, tier, table, Footprints, StackMaxOf, ids ?? Ids());
        }

        // ---- tier keys ----------------------------------------------------

        [Theory]
        [InlineData("1", 1, 0)]
        [InlineData("4", 4, 0)]
        [InlineData("1.1", 1, 1)]
        [InlineData("4.2", 4, 2)]
        public void ATierKeyIsATierAndAnOrdinal(string key, int tier, int ordinal)
        {
            Assert.True(ScrapSalvagePolicy.TryParseTierKey(key, out int parsedTier, out int parsedOrdinal));
            Assert.Equal(tier, parsedTier);
            Assert.Equal(ordinal, parsedOrdinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("tier1")]
        [InlineData("1.2.3")]
        [InlineData("-1")]
        [InlineData("1,1")]
        public void AnUnrecognisedTierKeyIsRefusedRatherThanGuessedAt(string? key)
        {
            Assert.False(ScrapSalvagePolicy.TryParseTierKey(key, out _, out _));
        }

        /// <summary>
        /// The German-locale trap: a decimal parse of "4.1" on a de-DE host reads as
        /// forty-one, which would silently move every second yield to tier 41 and
        /// make it unreachable. The maintainer's own host is German-locale.
        /// </summary>
        [Fact]
        public void ATierKeyParsesTheSameUnderAGermanLocale()
        {
            System.Globalization.CultureInfo previous = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                Assert.True(ScrapSalvagePolicy.TryParseTierKey("4.1", out int tier, out int ordinal));
                Assert.Equal(4, tier);
                Assert.Equal(1, ordinal);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previous;
            }
        }

        // ---- which tier gets paid -----------------------------------------

        [Fact]
        public void ARequestedTierIsPaidExactlyWhenTheItemHasIt()
        {
            IReadOnlyList<ScrapReward> rows = new[]
            {
                new ScrapReward(2, 0, "iron", 10, 6),
                new ScrapReward(3, 0, "iron", 10, 5),
                new ScrapReward(4, 0, "iron", 10, 10),
            };

            Assert.Equal(3, ScrapSalvagePolicy.ResolveTier(rows, 3));
        }

        [Fact]
        public void ATierAboveEverythingAuthoredClampsDownToTheHighestAuthored()
        {
            IReadOnlyList<ScrapReward> rows = new[] { new ScrapReward(1, 0, "iron", 10, 4) };

            Assert.Equal(1, ScrapSalvagePolicy.ResolveTier(rows, 4));
        }

        /// <summary>
        /// The case that makes the clamp necessary: a tier-4-only relic in the bag of
        /// a player standing on Haven. Without the fallback it would be permanently
        /// unsalvageable, which is the exact failure this phase removes.
        /// </summary>
        [Fact]
        public void ATierBelowEverythingAuthoredClampsUpToTheLowestAuthored()
        {
            IReadOnlyList<ScrapReward> rows = new[] { new ScrapReward(4, 0, "palm", 140, 10) };

            Assert.Equal(4, ScrapSalvagePolicy.ResolveTier(rows, 1));
        }

        [Fact]
        public void AnEmptyTableResolvesToNoTierAtAll()
        {
            Assert.Null(ScrapSalvagePolicy.ResolveTier(Array.Empty<ScrapReward>(), 1));
            Assert.Null(ScrapSalvagePolicy.ResolveTier(null, 1));
        }

        // ---- second yields -------------------------------------------------

        /// <summary>
        /// THE CORRECTION TO THE PLAN. Its step 3 said to resolve tier n to "the
        /// highest key whose integer part is n", which pays the .1 row INSTEAD of the
        /// base row. All 23 rows that carry a sub-key carry its base key too, and the
        /// materials always differ, so that rule would have silently deleted a yield
        /// from every one of them - 125 bronze off a cracked mining drill, in exchange
        /// for 40 fuel.
        /// </summary>
        [Fact]
        public void ASecondYieldIsPaidInAdditionToTheFirstNotInsteadOfIt()
        {
            IReadOnlyList<ScrapReward> rows = new[]
            {
                new ScrapReward(3, 1, "fuel", 40, 0),
                new ScrapReward(3, 0, "iron", 125, 5),
            };

            IReadOnlyList<SalvageYield> yields = ScrapSalvagePolicy.YieldsFor(rows, 3);

            Assert.Equal(2, yields.Count);
            Assert.Equal(new SalvageYield("iron", 125, 5), yields[0]);
            Assert.Equal(new SalvageYield("fuel", 40, 0), yields[1]);
        }

        [Fact]
        public void YieldsForAnotherTierAreNotPaid()
        {
            IReadOnlyList<ScrapReward> rows = new[]
            {
                new ScrapReward(1, 0, "iron", 100, 7),
                new ScrapReward(1, 1, "chestnut", 50, 7),
                new ScrapReward(4, 0, "lead", 10, 9),
            };

            IReadOnlyList<SalvageYield> yields = ScrapSalvagePolicy.YieldsFor(rows, 1);

            Assert.Equal(new[] { "iron", "chestnut" }, yields.Select(y => y.ItemTypeId));
        }

        // ---- stack splitting ------------------------------------------------

        [Fact]
        public void AnAmountOverTheStackCeilingIsCutIntoPilesThatStillTotalTheRecoveredAmount()
        {
            IReadOnlyList<SalvageYield> stacks = ScrapSalvagePolicy.IntoStacks(
                new[] { new SalvageYield("lead", 250, 8) }, StackMaxOf);

            Assert.Equal(new[] { 99, 99, 52 }, stacks.Select(s => s.Amount));
            Assert.Equal(250, stacks.Sum(s => s.Amount));
            Assert.All(stacks, s => Assert.Equal(8, s.Quality));
        }

        [Fact]
        public void AnUnstackableTypeIsLeftAsOnePileRatherThanCutIntoOnes()
        {
            IReadOnlyList<SalvageYield> stacks = ScrapSalvagePolicy.IntoStacks(
                new[] { new SalvageYield("unknown-to-the-database", 7, 3) }, StackMaxOf);

            Assert.Single(stacks);
            Assert.Equal(7, stacks[0].Amount);
        }

        // ---- the whole click --------------------------------------------------

        [Fact]
        public void SalvagingConsumesTheScrapAndPaysItsMaterial()
        {
            InventoryModel model = GridWith(Scrap(70));
            ScrapRewardLookup table = Table(
                ("scrapItem-relic", new[] { new ScrapReward(1, 0, "iron", 30, 4) }));

            SalvageResult result = Salvage(model, 70, 1, table);

            Assert.True(result.Paid);
            Assert.Equal(1, result.Tier);
            Assert.Null(model.ById(70));

            InventoryItem paid = Assert.Single(model.Items);
            Assert.Equal("iron", paid.ItemTypeId);
            Assert.Equal(30, paid.Amount);
            Assert.Equal(4, paid.Quality);
        }

        /// <summary>
        /// Quality is not ours to choose: it is the <c>q</c> the row records for the
        /// tier the item came off, and the SAME relic pays a different quality on a
        /// different island. Getting this wrong is invisible until somebody crafts.
        /// </summary>
        [Fact]
        public void TheQualityPaidIsTheOneRecordedForThatTier()
        {
            ScrapRewardLookup table = Table((
                "scrapItem-relic",
                new[]
                {
                    new ScrapReward(2, 0, "iron", 45, 6),
                    new ScrapReward(4, 0, "iron", 45, 10),
                }));

            InventoryModel low = GridWith(Scrap(70));
            Assert.True(Salvage(low, 70, 2, table).Paid);
            Assert.Equal(6, low.Items.Single().Quality);

            InventoryModel high = GridWith(Scrap(71));
            Assert.True(Salvage(high, 71, 4, table).Paid);
            Assert.Equal(10, high.Items.Single().Quality);
        }

        [Fact]
        public void TheRecordedSourceTierBeatsTheCallersGuess()
        {
            ScrapRewardLookup table = Table((
                "scrapItem-relic",
                new[]
                {
                    new ScrapReward(1, 0, "iron", 45, 4),
                    new ScrapReward(4, 0, "iron", 45, 10),
                }));

            InventoryModel model = GridWith(Scrap(70, meta: new Dictionary<string, string>
            {
                [ScrapSalvagePolicy.SourceTierMetaKey] = "4",
            }));

            SalvageResult result = Salvage(model, 70, ScrapSalvagePolicy.DefaultTier, table);

            Assert.Equal(4, result.Tier);
            Assert.Equal(10, model.Items.Single().Quality);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not a number")]
        [InlineData("0")]
        [InlineData("-2")]
        public void AnUnusableSourceTierStampFallsBackRatherThanThrowing(string stamp)
        {
            Dictionary<string, string> meta = new() { [ScrapSalvagePolicy.SourceTierMetaKey] = stamp };

            Assert.Equal(3, ScrapSalvagePolicy.TierFromMeta(meta, 3));
        }

        [Fact]
        public void ASecondYieldLandsAsItsOwnPile()
        {
            InventoryModel model = GridWith(Scrap(70));
            ScrapRewardLookup table = Table((
                "scrapItem-relic",
                new[]
                {
                    new ScrapReward(3, 0, "iron", 60, 5),
                    new ScrapReward(3, 1, "fuel", 40, 0),
                }));

            SalvageResult result = Salvage(model, 70, 3, table);

            Assert.True(result.Paid);
            Assert.Equal(new[] { "iron", "fuel" }, result.Yields.Select(y => y.ItemTypeId));
            Assert.Equal(new[] { "iron", "fuel" }, model.Items.Select(i => i.ItemTypeId));
        }

        [Fact]
        public void ALargePayoutBecomesSeveralPilesButOneToastableYield()
        {
            InventoryModel model = GridWith(Scrap(70));
            ScrapRewardLookup table = Table((
                "scrapItem-relic", new[] { new ScrapReward(4, 0, "lead", 250, 8) }));

            SalvageResult result = Salvage(model, 70, 4, table);

            Assert.True(result.Paid);

            // The toast says 250 once...
            SalvageYield toast = Assert.Single(result.Yields);
            Assert.Equal(250, toast.Amount);

            // ...and the grid holds it as three legal piles totalling the same.
            Assert.Equal(3, model.Items.Count);
            Assert.Equal(250, model.Items.Sum(i => i.Amount));
            Assert.All(model.Items, i => Assert.True(i.Amount <= StackMax));
        }

        [Fact]
        public void APayoutMergesOntoAPileTheBagAlreadyHolds()
        {
            InventoryModel model = GridWith(
                Scrap(70),
                new InventoryItem(80, "iron", 20, InventoryItem.NotWorn, InventoryItem.NoSlot,
                    4, 0, false, InventoryItem.NoSlot, 0, 4, false,
                    new Dictionary<string, string>(), null));

            ScrapRewardLookup table = Table(
                ("scrapItem-relic", new[] { new ScrapReward(1, 0, "iron", 30, 4) }));

            Assert.True(Salvage(model, 70, 1, table).Paid);

            InventoryItem pile = Assert.Single(model.Items);
            Assert.Equal(80, pile.ItemId);
            Assert.Equal(50, pile.Amount);
        }

        // ---- refusals, all of which must change nothing --------------------

        [Fact]
        public void SalvagingSomethingTheBagDoesNotHoldChangesNothing()
        {
            InventoryModel model = GridWith(Scrap(70));
            IReadOnlyList<InventoryItem> before = model.Items.ToList();

            SalvageResult result = Salvage(model, 999, 1,
                Table(("scrapItem-relic", new[] { new ScrapReward(1, 0, "iron", 30, 4) })));

            Assert.Equal(SalvageOutcome.ItemNotHeld, result.Outcome);
            Assert.Equal(before, model.Items);
        }

        /// <summary>
        /// Two SALVAGE clicks in flight. The client greys its own panel while it
        /// waits, but a hand-built packet need not, so the server has to be
        /// idempotent on an id that is already gone.
        /// </summary>
        [Fact]
        public void ASecondSalvageOfTheSameItemPaysNothingMore()
        {
            InventoryModel model = GridWith(Scrap(70));
            ScrapRewardLookup table = Table(
                ("scrapItem-relic", new[] { new ScrapReward(1, 0, "iron", 30, 4) }));

            Assert.True(Salvage(model, 70, 1, table).Paid);
            IReadOnlyList<InventoryItem> afterFirst = model.Items.ToList();

            SalvageResult second = Salvage(model, 70, 1, table);

            Assert.Equal(SalvageOutcome.ItemNotHeld, second.Outcome);
            Assert.Equal(afterFirst, model.Items);
        }

        [Fact]
        public void SalvagingSomethingThatIsNotScrapChangesNothing()
        {
            InventoryModel model = GridWith(Scrap(70, "glider"));
            IReadOnlyList<InventoryItem> before = model.Items.ToList();

            SalvageResult result = Salvage(model, 70, 1,
                Table(("glider", new[] { new ScrapReward(1, 0, "iron", 30, 4) })));

            Assert.Equal(SalvageOutcome.NotScrap, result.Outcome);
            Assert.Equal(before, model.Items);
        }

        [Fact]
        public void SalvagingAWornOrStashedItemChangesNothing()
        {
            InventoryModel worn = GridWith(Scrap(70, slot: "Body"));
            InventoryModel stashed = GridWith(Scrap(71, lockBox: true));
            ScrapRewardLookup table = Table(
                ("scrapItem-relic", new[] { new ScrapReward(1, 0, "iron", 30, 4) }));

            Assert.Equal(SalvageOutcome.NotInGrid, Salvage(worn, 70, 1, table).Outcome);
            Assert.Equal(SalvageOutcome.NotInGrid, Salvage(stashed, 71, 1, table).Outcome);
            Assert.Single(worn.Items);
            Assert.Single(stashed.Items);
        }

        /// <summary>
        /// A Founder's Tome is a <c>scrapItem-*</c> with no reward block, so the
        /// client offers SALVAGE on it and there is nothing recovered to pay. It must
        /// survive the click.
        /// </summary>
        [Fact]
        public void ScrapWithNoRewardBlockIsNotConsumed()
        {
            InventoryModel model = GridWith(Scrap(70, "scrapItem-barren"));

            SalvageResult result = Salvage(model, 70, 1, Table());

            Assert.Equal(SalvageOutcome.NoRewardBlock, result.Outcome);
            Assert.NotNull(model.ById(70));
        }

        /// <summary>
        /// THE ITEM-LOSS CASE. A payout that will not fit must cost the player
        /// nothing at all - not the scrap, and not a partial pile of metal that makes
        /// them think it worked.
        /// </summary>
        [Fact]
        public void APayoutThatDoesNotFitConsumesNothingAndPaysNothing()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Scrap(70, "scrapItem-barren", 9, 17));

            // Fill every remaining cell of the 10x18 grid with 3x2 iron piles at a
            // quality that cannot merge with the payout's.
            int id = 100;
            for (int y = 0; y + 2 <= 18; y += 2)
            {
                for (int x = 0; x + 3 <= 9; x += 3)
                {
                    model.Add(new InventoryItem(id++, "iron", 99, InventoryItem.NotWorn,
                        InventoryItem.NoSlot, x, y, false, InventoryItem.NoSlot, 0, 1, false,
                        new Dictionary<string, string>(), null));
                }
            }

            IReadOnlyList<InventoryItem> before = model.Items.ToList();

            SalvageResult result = Salvage(model, 70, 1,
                Table(("scrapItem-barren", new[] { new ScrapReward(1, 0, "iron", 30, 4) })));

            Assert.Equal(SalvageOutcome.NoRoom, result.Outcome);
            Assert.Empty(result.Yields);
            Assert.Equal(before, model.Items);
            Assert.NotNull(model.ById(70));
        }

        /// <summary>
        /// A material with no row in the item database has no footprint, so it cannot
        /// be placed - and paying half a payout would be worse than paying none.
        /// </summary>
        [Fact]
        public void AnUnplaceableMaterialAbortsTheWholePayout()
        {
            InventoryModel model = GridWith(Scrap(70));

            SalvageResult result = Salvage(model, 70, 1, Table((
                "scrapItem-relic",
                new[]
                {
                    new ScrapReward(1, 0, "iron", 30, 4),
                    new ScrapReward(1, 1, "unobtainium", 30, 4),
                })));

            Assert.Equal(SalvageOutcome.NoRoom, result.Outcome);
            Assert.NotNull(model.ById(70));
            Assert.Single(model.Items);
        }

        /// <summary>
        /// The 5x3 relics are the reason the scrap is removed BEFORE the payout is
        /// placed: fifteen cells the metal is entitled to use.
        /// </summary>
        [Fact]
        public void ABulkyRelicFreesItsOwnCellsForWhatItBecomes()
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            model.Add(Scrap(70, "scrapItem-hugerelic"));

            // Everything except the relic's own footprint is taken.
            int id = 200;
            for (int y = 4; y + 2 <= 18; y += 2)
            {
                for (int x = 0; x + 3 <= 9; x += 3)
                {
                    model.Add(new InventoryItem(id++, "iron", 99, InventoryItem.NotWorn,
                        InventoryItem.NoSlot, x, y, false, InventoryItem.NoSlot, 0, 1, false,
                        new Dictionary<string, string>(), null));
                }
            }

            SalvageResult result = Salvage(model, 70, 1,
                Table(("scrapItem-hugerelic", new[] { new ScrapReward(1, 0, "lead", 30, 4) })));

            Assert.True(result.Paid);
            Assert.Contains(model.Items, i => i.ItemTypeId == "lead" && i.Amount == 30);
        }
    }
}
