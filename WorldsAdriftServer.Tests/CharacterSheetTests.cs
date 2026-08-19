using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using WorldsAdriftServer.Portal;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// What the portal's character sheet shows.
    ///
    /// The payloads under test are written by the GAME server's own snapshot
    /// writers rather than typed as JSON literals here. That is the point: the
    /// columns are opaque and the sheet reads them back through the same readers
    /// the game server uses, so a test holding a hand-written payload would be
    /// asserting against a shape nothing produces.
    /// </summary>
    public class CharacterSheetTests
    {
        private static readonly Guid Uid = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");

        private static CharacterRecord Character() => new CharacterRecord(
            Uid, 7, "Wrenna", 0, false, "{}",
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 2, 3, 4, 5, TimeSpan.Zero));

        private static string Progression(
            int knowledge, int lifetime,
            Dictionary<string, int>? nodes = null,
            List<string>? schematics = null,
            List<string>? scans = null) =>
            ProgressionSnapshot.Write(new ProgressionState
            {
                Knowledge = knowledge,
                LifetimeKnowledge = lifetime,
                NodeUses = nodes ?? new Dictionary<string, int>(),
                LearnedSchematics = schematics ?? new List<string>(),
                AlreadyScanned = scans ?? new List<string>(),
            });

        private static InventoryItem Item(
            int id, string type, int amount, string slot = InventoryItem.NotWorn, bool stashed = false) =>
            new InventoryItem(id, type, amount, slot, InventoryItem.NoSlot, 0, 0, false,
                InventoryItem.NoSlot, 0, 0, stashed, new Dictionary<string, string>(), null);

        private static string Inventory(params InventoryItem[] items)
        {
            InventoryModel model = InventoryModel.DefaultGrid();
            foreach (InventoryItem item in items) model.Add(item);
            return InventorySnapshot.Write(model);
        }

        private static CharacterSheet Build(
            string? progression = null, string? inventory = null, PositionRecord? position = null,
            CharacterSheetPolicy.Locator? locate = null) =>
            CharacterSheetPolicy.Build(
                Character(), progression, inventory, position,
                locate ?? CharacterSheetPolicy.NoWorld);

        // ------------------------------------------------------------ knowledge

        [Fact]
        public void KnowledgeIsReadBackThroughTheGameServersOwnReader()
        {
            CharacterSheet sheet = Build(
                Progression(4, 11,
                    schematics: new List<string> { "sch_rope", "sch_anchor" },
                    scans: new List<string> { "a", "b", "c" }));

            Assert.NotNull(sheet.Knowledge);
            Assert.Equal(4, sheet.Knowledge!.Knowledge);
            Assert.Equal(11, sheet.Knowledge.LifetimeKnowledge);
            Assert.Equal(7, sheet.Knowledge.Spent);
            Assert.Equal(3, sheet.Knowledge.Scans);
            Assert.Equal(new[] { "sch_anchor", "sch_rope" }, sheet.Knowledge.Schematics);
        }

        /// <summary>
        /// Null means "never saved", and it is what an unreadable payload produces
        /// too. Both are the same thing to a player - there is nothing to show -
        /// and the alternative, guessing at a payload the reader refused, is how a
        /// page starts telling a player they know things they do not.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{not json")]
        [InlineData("{\"Version\":999,\"Knowledge\":5}")]
        public void AnUnreadableProgressionShowsNothingRatherThanAGuess(string? payload)
        {
            Assert.Null(Build(payload).Knowledge);
        }

        [Fact]
        public void SpentIsClampedSoAHandEditedRowCannotShowANegative()
        {
            CharacterSheet sheet = Build(Progression(9, 2));

            Assert.Equal(0, sheet.Knowledge!.Spent);
        }

        [Fact]
        public void NodeUsesAreCappedAndSortedBiggestFirstThenByName()
        {
            Dictionary<string, int> nodes = new Dictionary<string, int>();
            for (int i = 0; i < CharacterSheetPolicy.TallyRows + 4; i++)
            {
                nodes["node_" + i.ToString("00")] = i;
            }

            // Two with the same count, so the name tie-break has something to do.
            nodes["node_zz"] = 3;

            CharacterSheet sheet = Build(Progression(1, 1, nodes: nodes));
            SheetKnowledge k = sheet.Knowledge!;

            Assert.Equal(CharacterSheetPolicy.TallyRows, k.NodeUses.Count);

            for (int i = 1; i < k.NodeUses.Count; i++)
            {
                Assert.True(k.NodeUses[i - 1].Count >= k.NodeUses[i].Count);
            }

            // The total counts EVERYTHING, not just the rows shown - a cap on the
            // table must not become a cap on the number beside it.
            int expected = 0;
            foreach (KeyValuePair<string, int> pair in nodes) expected += pair.Value;
            Assert.Equal(expected, k.NodeUsesTotal);
        }

        [Fact]
        public void TheSchematicListIsNotCapped()
        {
            List<string> many = new List<string>();
            for (int i = 0; i < CharacterSheetPolicy.TallyRows * 3; i++) many.Add("sch_" + i);

            CharacterSheet sheet = Build(Progression(1, 1, schematics: many));

            Assert.Equal(many.Count, sheet.Knowledge!.Schematics.Count);
        }

        // ------------------------------------------------------------ inventory

        [Fact]
        public void StacksAndUnitsAreCountedSeparately()
        {
            CharacterSheet sheet = Build(inventory: Inventory(
                Item(1, "iron_ore", 5),
                Item(2, "iron_ore", 7),
                Item(3, "rope", 1)));

            SheetInventory inv = sheet.Inventory!;

            Assert.Equal(3, inv.Stacks);
            Assert.Equal(13, inv.Units);
            Assert.Equal(10, inv.Width);
            Assert.Equal(18, inv.Height);
        }

        [Fact]
        public void WornAndStashedAreCounted()
        {
            // "Body" and "Feet", not invented words: InventoryPolicy.LegalSlotTypes
            // is a closed list and the snapshot reader rewrites anything outside it
            // to "None", so a test using a plausible-looking slot name would silently
            // assert that nothing is worn.
            CharacterSheet sheet = Build(inventory: Inventory(
                Item(1, "coat", 1, slot: "Body"),
                Item(2, "boots", 1, slot: "Feet"),
                Item(3, "iron_ore", 4, stashed: true),
                Item(4, "rope", 1)));

            Assert.Equal(2, sheet.Inventory!.Worn);
            Assert.Equal(1, sheet.Inventory.Stashed);
        }

        [Fact]
        public void TheTopTableSumsStacksOfTheSameTypeAndIsCapped()
        {
            List<InventoryItem> items = new List<InventoryItem>
            {
                Item(1, "iron_ore", 5),
                Item(2, "iron_ore", 7),
            };

            for (int i = 0; i < CharacterSheetPolicy.TallyRows + 3; i++)
            {
                items.Add(Item(100 + i, "thing_" + i, 1));
            }

            SheetInventory inv = Build(inventory: Inventory(items.ToArray())).Inventory!;

            Assert.Equal(CharacterSheetPolicy.TallyRows, inv.Top.Count);
            Assert.Equal("iron_ore", inv.Top[0].Name);
            Assert.Equal(12, inv.Top[0].Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{not json")]
        public void AnUnreadableInventoryShowsNothing(string? payload)
        {
            Assert.Null(Build(inventory: payload).Inventory);
        }

        // ------------------------------------------------------------- position

        [Fact]
        public void PositionIsConvertedOutOfTheSimulationsFixedPointUnits()
        {
            // Q52.12: 4096 units to the metre.
            PositionRecord row = new PositionRecord(
                Uid, 4096 * 120, 4096 * -30, 4096 * 7,
                DateTimeOffset.UnixEpoch, new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

            SheetPosition position = Build(position: row,
                locate: (_, _, _) => ("Kestrel's Rest", true)).Position!;

            Assert.Equal(120, position.MetresX, 6);
            Assert.Equal(-30, position.MetresY, 6);
            Assert.Equal(7, position.MetresZ, 6);
            Assert.Equal("Kestrel's Rest", position.Place);
            Assert.True(position.OnKnownTerrain);
            Assert.Equal(row.UpdatedAt, position.SeenAt);
        }

        [Fact]
        public void TheLocatorIsHandedTheStoredUnitsUnchanged()
        {
            long[] seen = new long[3];

            Build(position: new PositionRecord(Uid, 11, 22, 33, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                locate: (x, y, z) => { seen[0] = x; seen[1] = y; seen[2] = z; return ("open sky", false); });

            Assert.Equal(new long[] { 11, 22, 33 }, seen);
        }

        [Fact]
        public void ACharacterThatHasNeverBeenPlacedHasNoPosition()
        {
            Assert.Null(Build().Position);
        }

        // --------------------------------------------------------------- shape

        [Fact]
        public void TheIdentityFieldsComeStraightOffTheRow()
        {
            CharacterSheet sheet = Build();

            Assert.Equal(Uid, sheet.Uid);
            Assert.Equal("Wrenna", sheet.Name);
            Assert.Equal(0, sheet.SlotIndex);
            Assert.Equal(Character().CreatedAt, sheet.CreatedAt);
        }

        /// <summary>
        /// One missing panel must not cost the others. A character with no
        /// inventory row still has a knowledge panel and a position.
        /// </summary>
        [Fact]
        public void EachPanelDegradesOnItsOwn()
        {
            CharacterSheet sheet = Build(
                progression: Progression(2, 5),
                inventory: null,
                position: new PositionRecord(Uid, 0, 0, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));

            Assert.NotNull(sheet.Knowledge);
            Assert.Null(sheet.Inventory);
            Assert.NotNull(sheet.Position);
        }
    }
}
