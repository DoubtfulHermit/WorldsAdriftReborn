using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    public class InventorySnapshotTests
    {
        [Fact]
        public void An_inventory_survives_a_round_trip_field_for_field()
        {
            // Any field lost here is a field the player loses on relog.
            InventoryModel original = InventoryTestData.Grid();
            original.Add(new InventoryItem(
                1200, "iron", 12, "None", -1, 4, 9, true, 5, 0, 7, false,
                new Dictionary<string, string> { ["PrimaryColor"] = "#ff00ff", ["totalHealth"] = "100" },
                3));

            InventoryModel restored = InventorySnapshot.Read(InventorySnapshot.Write(original))!;

            Assert.Equal(original.Width, restored.Width);
            Assert.Equal(original.Height, restored.Height);
            Assert.Equal(original.HasBelt, restored.HasBelt);
            Assert.Equal(original.BeltRow, restored.BeltRow);

            InventoryItem item = restored.ById(1200)!;
            Assert.Equal("iron", item.ItemTypeId);
            Assert.Equal(12, item.Amount);
            Assert.Equal(4, item.X);
            Assert.Equal(9, item.Y);
            Assert.True(item.Rotated);
            Assert.Equal(5, item.HotBarSlotNum);
            Assert.Equal(7, item.Quality);
            Assert.Equal(3, item.Rarity);
            Assert.Equal("#ff00ff", item.Meta["PrimaryColor"]);
            Assert.Equal("100", item.Meta["totalHealth"]);
        }

        [Fact]
        public void The_whole_seeded_inventory_round_trips()
        {
            InventoryModel restored = InventorySnapshot.Read(
                InventorySnapshot.Write(InventoryTestData.Seeded()))!;

            Assert.Equal(7, restored.Items.Count);
            Assert.Empty(InventoryPolicy.ValidateForWire(restored, InventoryTestData.Footprints));
        }

        [Fact]
        public void Worn_state_survives_a_relog()
        {
            InventoryModel original = InventoryTestData.Grid();
            original.Add(InventoryTestData.Item(1200, "torso_poncho", 0, 0, slotType: "Body"));

            InventoryModel restored = InventorySnapshot.Read(InventorySnapshot.Write(original))!;

            Assert.Equal("Body", restored.ById(1200)!.SlotType);
        }

        [Fact]
        public void A_stored_slot_type_the_client_cannot_parse_is_downgraded_rather_than_shipped()
        {
            // A row that got a bad slotType into the database must not be able
            // to blank the panel on every future login.
            string json = InventorySnapshot.Write(InventoryTestData.Grid())
                .Replace("\"Items\":[]", "\"Items\":[{\"ItemId\":1200,\"ItemTypeId\":\"iron\",\"Amount\":1,"
                    + "\"SlotType\":\"torso\",\"UtilitySlotNum\":-1,\"X\":0,\"Y\":0,\"Rotated\":false,"
                    + "\"HotBarSlotNum\":-1,\"TimeToBuild\":0,\"Quality\":0,\"LockBoxItem\":false,"
                    + "\"Meta\":{},\"Rarity\":null}]");

            InventoryModel restored = InventorySnapshot.Read(json)!;

            Assert.Equal(InventoryItem.NotWorn, restored.ById(1200)!.SlotType);
        }

        [Fact]
        public void Meta_is_never_null_after_a_round_trip()
        {
            // TryGetValue is called on meta unguarded on every icon update.
            InventoryModel original = InventoryTestData.Grid();
            original.Add(new InventoryItem(
                1200, "iron", 1, "None", -1, 0, 0, false, -1, 0, 0, false, null!, null));

            InventoryModel restored = InventorySnapshot.Read(InventorySnapshot.Write(original))!;

            Assert.NotNull(restored.ById(1200)!.Meta);
        }

        [Fact]
        public void Unreadable_payloads_yield_null_rather_than_throwing()
        {
            // The caller's correct response is to seed a fresh inventory, not to
            // refuse to let the player into the world.
            Assert.Null(InventorySnapshot.Read(null));
            Assert.Null(InventorySnapshot.Read(""));
            Assert.Null(InventorySnapshot.Read("   "));
            Assert.Null(InventorySnapshot.Read("not json"));
            Assert.Null(InventorySnapshot.Read("{\"Version\":1"));
        }

        [Fact]
        public void A_payload_from_an_unknown_version_is_refused_rather_than_misread()
        {
            string json = InventorySnapshot.Write(InventoryTestData.Seeded())
                .Replace("\"Version\":1", "\"Version\":99");

            Assert.Null(InventorySnapshot.Read(json));
        }

        [Fact]
        public void A_payload_with_no_grid_is_refused()
        {
            string json = InventorySnapshot.Write(InventoryTestData.Seeded())
                .Replace("\"Width\":10", "\"Width\":0");

            Assert.Null(InventorySnapshot.Read(json));
        }

        [Fact]
        public void One_unusable_row_does_not_cost_the_player_the_others()
        {
            string json = InventorySnapshot.Write(InventoryTestData.Seeded())
                .Replace("\"ItemTypeId\":\"glider\"", "\"ItemTypeId\":\"\"");

            InventoryModel restored = InventorySnapshot.Read(json)!;

            Assert.Equal(6, restored.Items.Count);
        }

        [Fact]
        public void Writing_never_produces_an_empty_payload()
        {
            // The characters table's data_json CHECK refuses an empty string,
            // and an empty meta is an unguarded TryGetValue on the client.
            Assert.False(string.IsNullOrWhiteSpace(InventorySnapshot.Write(InventoryTestData.Grid())));
        }
    }
}
