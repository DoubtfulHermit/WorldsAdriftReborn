using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// THE MIGRATION. Five ships exist in the live world-state.json, all written
    /// before materials were recorded. None of them may fail to load, change mass,
    /// or come back unflyable. These tests are the contract that guarantees it.
    /// </summary>
    public class BuiltShipMaterialPersistenceTests
    {
        [Fact]
        public void A_record_written_before_this_feature_still_deserialises()
        {
            // Exactly the shape the live file has today: no material members at all.
            const string legacyJson = """
            {
              "Salvaged": false,
              "HullX": 70502113, "HullY": -1273730, "HullZ": -4580013,
              "HullYawRadians": 0,
              "HullBytes": "AQAAAAAA6AAAGAAA6AAAGAAAAAAAAAHoAAAYAADoAAAYAAAAAAAA",
              "OwnerCharacterUid": "someone",
              "ShipyardX": 0, "ShipyardY": 0, "ShipyardZ": 0
            }
            """;

            BuiltShipRecord? record = JsonSerializer.Deserialize<BuiltShipRecord>(legacyJson);

            Assert.NotNull(record);
            Assert.Equal("someone", record!.OwnerCharacterUid);
            Assert.Equal("", record.HullWoodId);
            Assert.Equal(0, record.HullWoodQuality);
        }

        [Fact]
        public void A_legacy_record_is_restated_as_the_birch_and_iron_it_has_always_been()
        {
            // The heart of the migration: an absent material is not "unknown", it is
            // KNOWN - the server hardcoded Deck.MaterialTypeId = "birch" and mapped
            // "Metal" -> "iron". So the ship does not change, it just says so.
            var legacy = new BuiltShipRecord();
            HullMaterials materials = legacy.Materials();

            Assert.Equal("birch", materials.WoodId);
            Assert.Equal("iron", materials.MetalId);
            Assert.False(materials.IsEmpty);
        }

        [Fact]
        public void A_legacy_ship_keeps_a_sane_mass_and_stays_inside_the_core_budget()
        {
            // The property that actually matters to a player with a ship in the sky.
            double mass = HullMassCalculator.HullMassKg(new BuiltShipRecord().Materials(), 1, 1);
            Assert.True(mass > 0.0);
            Assert.True(mass < MaterialCatalog.BaseSkyCoreLiftKg,
                "a restored legacy ship at " + mass + " kg must not be born overloaded");
            // And its agility multiplier must be a real number near 1, not a lurch.
            Assert.InRange(HullMassCalculator.AgilityScale(mass), 1.0, HullMassCalculator.MaxAgility);
        }

        [Fact]
        public void Materials_round_trip_through_json_unchanged()
        {
            var record = new BuiltShipRecord();
            record.SetMaterials(new HullMaterials("palm", 8, "copper", 3));

            string json = JsonSerializer.Serialize(record);
            BuiltShipRecord? back = JsonSerializer.Deserialize<BuiltShipRecord>(json);

            Assert.NotNull(back);
            HullMaterials materials = back!.Materials();
            Assert.Equal("palm", materials.WoodId);
            Assert.Equal(8, materials.WoodQuality);
            Assert.Equal("copper", materials.MetalId);
            Assert.Equal(3, materials.MetalQuality);
        }

        [Fact]
        public void An_all_metal_ship_round_trips_without_gaining_phantom_timber()
        {
            var record = new BuiltShipRecord();
            record.SetMaterials(new HullMaterials(null, 0, "titanium", 6));

            BuiltShipRecord back = JsonSerializer.Deserialize<BuiltShipRecord>(
                JsonSerializer.Serialize(record))!;

            HullMaterials materials = back.Materials();
            Assert.Null(materials.WoodId);
            Assert.Equal("titanium", materials.MetalId);
            // A half-recorded ship must NOT be "completed" into birch, which would
            // make it heavier than the player built it.
            Assert.Equal("", back.HullWoodId);
        }

        [Fact]
        public void A_whole_snapshot_with_mixed_old_and_new_ships_loads()
        {
            const string mixedJson = """
            {
              "BuiltShips": [
                { "HullBytes": "", "OwnerCharacterUid": "old" },
                { "HullBytes": "", "OwnerCharacterUid": "new",
                  "HullWoodId": "ash", "HullWoodQuality": 4,
                  "HullMetalId": "steel", "HullMetalQuality": 7 }
              ]
            }
            """;

            WorldStateSnapshot? snapshot = JsonSerializer.Deserialize<WorldStateSnapshot>(mixedJson);

            Assert.NotNull(snapshot);
            Assert.Equal(2, snapshot!.BuiltShips.Count);
            Assert.Equal("birch", snapshot.BuiltShips[0].Materials().WoodId);   // restated
            Assert.Equal("ash", snapshot.BuiltShips[1].Materials().WoodId);     // as recorded
            Assert.Equal("steel", snapshot.BuiltShips[1].Materials().MetalId);
        }

        [Fact]
        public void An_unknown_material_id_in_a_hand_edited_file_does_not_break_loading()
        {
            var record = new BuiltShipRecord { HullWoodId = "mithril", HullWoodQuality = 5 };
            HullMaterials materials = record.Materials();

            // The bogus id is dropped and the record falls back to legacy rather than
            // producing a ship made of nothing.
            Assert.Equal("birch", materials.WoodId);
            Assert.True(HullMassCalculator.HullMassKg(materials, 1, 1) > 0.0);
        }

        [Fact]
        public void SetMaterials_tolerates_a_null_rather_than_throwing_mid_save()
        {
            var record = new BuiltShipRecord();
            record.SetMaterials(new HullMaterials("oak", 3, null, 0));
            record.SetMaterials(null!);
            // Unchanged, not cleared - a null is "nothing to say", not "erase it".
            Assert.Equal("oak", record.HullWoodId);
        }
    }
}
