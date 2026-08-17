using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// What a ship records about its own substance, and - the part that must not
    /// break the live world - what an OLD ship with no record is taken to be.
    /// </summary>
    public class HullMaterialsTests
    {
        [Fact]
        public void The_dominant_wood_and_metal_are_the_ones_most_of_went_in()
        {
            HullMaterials materials = HullMaterials.FromConsumed(new[]
            {
                ("birch", 2, 3),
                ("oak", 7, 5),
                ("iron", 1, 2),
                ("copper", 6, 4),
            });

            Assert.Equal("oak", materials.WoodId);
            Assert.Equal("copper", materials.MetalId);
            Assert.Equal(5, materials.WoodQuality);
            Assert.Equal(4, materials.MetalQuality);
        }

        [Fact]
        public void A_hull_of_one_family_records_only_that_family()
        {
            HullMaterials wooden = HullMaterials.FromConsumed(new[] { ("cedar", 10, 1) });
            Assert.Equal("cedar", wooden.WoodId);
            Assert.Null(wooden.MetalId);

            HullMaterials metal = HullMaterials.FromConsumed(new[] { ("titanium", 4, 8) });
            Assert.Null(metal.WoodId);
            Assert.Equal("titanium", metal.MetalId);
        }

        [Fact]
        public void Non_material_ingredients_are_ignored_not_rejected()
        {
            // A recipe also eats fuel and atlas shards; they are not what the ship
            // is MADE of and must not blank out the real answer.
            HullMaterials materials = HullMaterials.FromConsumed(new[]
            {
                ("fuel", 20, 1),
                ("atlasShard", 5, 1),
                ("ash", 3, 6),
            });

            Assert.Equal("ash", materials.WoodId);
            Assert.Null(materials.MetalId);
        }

        [Fact]
        public void The_best_quality_seen_for_a_material_wins()
        {
            // One good plank among poor ones built a slightly better ship.
            HullMaterials materials = HullMaterials.FromConsumed(new[]
            {
                ("birch", 5, 2),
                ("birch", 1, 9),
            });
            Assert.Equal("birch", materials.WoodId);
            Assert.Equal(9, materials.WoodQuality);
        }

        [Fact]
        public void An_empty_or_null_consumption_records_nothing_and_never_throws()
        {
            Assert.True(HullMaterials.FromConsumed(null!).IsEmpty);
            Assert.True(HullMaterials.FromConsumed(new (string, int, int)[0]).IsEmpty);
            Assert.True(HullMaterials.FromConsumed(new[] { ("fuel", 3, 1) }).IsEmpty);
        }

        [Fact]
        public void Zero_and_negative_amounts_do_not_count_as_a_material()
        {
            Assert.True(HullMaterials.FromConsumed(new[] { ("oak", 0, 1), ("iron", -4, 1) }).IsEmpty);
        }

        // ------------------------------------------------------------------
        // The migration. The user has five ships in the live world; none of them
        // has a recorded material. They must keep flying, and they must keep
        // flying AS WHAT THEY ARE, not as something new.
        // ------------------------------------------------------------------

        [Fact]
        public void A_legacy_hull_with_no_record_becomes_birch_and_iron()
        {
            HullMaterials legacy = new HullMaterials(null, 0, null, 0).OrLegacy();
            Assert.Equal("birch", legacy.WoodId);
            Assert.Equal("iron", legacy.MetalId);
            Assert.False(legacy.IsEmpty);
        }

        [Fact]
        public void A_hull_that_DOES_have_a_record_is_left_alone_by_the_migration()
        {
            HullMaterials recorded = new HullMaterials("palm", 7, "gold", 9);
            HullMaterials after = recorded.OrLegacy();
            Assert.Same(recorded, after);
            Assert.Equal("palm", after.WoodId);
            Assert.Equal("gold", after.MetalId);
        }

        [Fact]
        public void A_half_recorded_hull_is_left_alone_rather_than_being_completed()
        {
            // An all-metal hull is a legitimate build, not a broken record. If the
            // migration "helpfully" added birch it would make the ship heavier than
            // the player built it.
            HullMaterials metalOnly = new HullMaterials(null, 1, "steel", 4).OrLegacy();
            Assert.Null(metalOnly.WoodId);
            Assert.Equal("steel", metalOnly.MetalId);
        }

        [Fact]
        public void A_material_offered_in_the_wrong_slot_is_dropped_not_stored()
        {
            // Storing "oak" as the METAL would silently corrupt every later mass
            // calculation, so the contradiction is refused at the door.
            HullMaterials crossed = new HullMaterials("iron", 1, "oak", 1);
            Assert.Null(crossed.WoodId);
            Assert.Null(crossed.MetalId);
        }

        [Fact]
        public void Quality_is_clamped_into_the_range_the_world_actually_produces()
        {
            // Observed quality across every island is 1..10.
            Assert.Equal(1, new HullMaterials("oak", -5, null, 0).WoodQuality);
            Assert.Equal(10, new HullMaterials("oak", 99, null, 0).WoodQuality);
        }
    }
}
