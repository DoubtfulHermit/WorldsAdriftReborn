using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// The 1099 material list a built ship publishes. This is the fix for the error
    /// the live client logs on every ship mesh build:
    ///
    ///   [ComponentMaterialColors] No wooden or metal materials found for
    ///   ShipFrame 283/Generated
    ///
    /// which ComponentMaterialColors.cs:173-177 emits when BOTH its wood and metal
    /// buckets come back empty - i.e. when the server sends an empty list, which is
    /// exactly what the hull branch used to do.
    /// </summary>
    public class HullMaterialPublicationTests
    {
        [Fact]
        public void A_hull_never_publishes_the_empty_list_that_causes_the_client_error()
        {
            // The whole point. Every hull, however it was recorded, must put at least
            // one entry in a bucket ComponentMaterialColors recognises.
            foreach (HullMaterials materials in new[]
            {
                HullMaterials.Legacy,
                new HullMaterials("oak", 4, "copper", 7),
                new HullMaterials("cedar", 1, null, 0),
                new HullMaterials(null, 0, "tungsten", 9),
                new HullMaterials(null, 0, null, 0).OrLegacy(),
            })
            {
                var published = HullMaterialPublication.ForHull(materials);
                Assert.NotEmpty(published);
                Assert.All(published, m => Assert.Contains(
                    m.Category, new[] { MaterialCategory.Wood, MaterialCategory.Metal }));
            }
        }

        [Fact]
        public void A_wooden_hull_with_metal_fittings_leads_with_the_WOOD()
        {
            // SetMaterials takes the FIRST entry's family as the component's dominant
            // material (ComponentMaterialColors.cs:150-172). A wooden ship must read
            // as wood with a metal accent, not as metal.
            var published = HullMaterialPublication.ForHull(new HullMaterials("birch", 3, "iron", 2));

            Assert.Equal(2, published.Count);
            Assert.Equal(MaterialCategory.Wood, published[0].Category);
            Assert.Equal("birch", published[0].MaterialTypeId);
            Assert.Equal(MaterialCategory.Metal, published[1].Category);
            Assert.Equal("iron", published[1].MaterialTypeId);
        }

        [Fact]
        public void Every_published_entry_names_a_REAL_item_id_the_client_can_resolve()
        {
            // MaterialManager resolves by name and falls back to a MAGENTA material on
            // a miss. That is survivable but ugly, so no published id may be invented.
            foreach (ShipMaterial material in MaterialCatalog.Materials)
            {
                var hull = material.IsWood
                    ? new HullMaterials(material.Id, 5, null, 0)
                    : new HullMaterials(null, 0, material.Id, 5);

                foreach (SlottedMaterialSpec spec in HullMaterialPublication.ForHull(hull))
                {
                    Assert.NotNull(MaterialCatalog.Find(spec.MaterialTypeId));
                }
            }
        }

        [Fact]
        public void Slot_indices_are_positional_and_start_at_zero()
        {
            // Retail reads material slots positionally (ModularCannon maps
            // materialDefinitions[0..3] onto its four component slots).
            var published = HullMaterialPublication.ForHull(new HullMaterials("oak", 1, "steel", 1));
            Assert.Equal(new[] { 0, 1 }, published.Select(m => m.Index).ToArray());

            var single = HullMaterialPublication.ForHull(new HullMaterials(null, 0, "steel", 1));
            Assert.Equal(new[] { 0 }, single.Select(m => m.Index).ToArray());
        }

        [Fact]
        public void Every_published_amount_is_positive_because_the_salvage_helper_sums_them()
        {
            // MaterialsEffectsData.GetOrDefaultFromMaterialList sums the amounts and
            // divides; a zero total is the bug that made the salvage beam do nothing.
            foreach (SlottedMaterialSpec spec in HullMaterialPublication.ForHull(HullMaterials.Legacy))
            {
                Assert.True(spec.Amount > 0);
            }
            Assert.True(HullMaterialPublication.ForDeck(HullMaterials.Legacy).Amount > 0);
        }

        // ------------------------------------------------------------------
        // The deck. This one is not cosmetic: an empty list here throws in
        // ShipDeckVisualizer.OnEnable and the player falls through the floor.
        // ------------------------------------------------------------------

        [Fact]
        public void A_deck_always_gets_exactly_one_entry_at_index_zero()
        {
            foreach (HullMaterials materials in new[]
            {
                HullMaterials.Legacy,
                new HullMaterials("palm", 8, "gold", 3),
                new HullMaterials(null, 0, "titanium", 6),
                new HullMaterials(null, 0, null, 0),
            })
            {
                SlottedMaterialSpec deck = HullMaterialPublication.ForDeck(materials);
                Assert.Equal(0, deck.Index);
                Assert.NotNull(MaterialCatalog.Find(deck.MaterialTypeId));
                Assert.Contains(deck.Category, new[] { MaterialCategory.Wood, MaterialCategory.Metal });
            }
        }

        [Fact]
        public void A_deck_follows_its_hull_and_a_legacy_deck_is_still_birch()
        {
            // Preserves today's behaviour exactly for every existing ship: the deck
            // was hardcoded to birch/"Wood", and a legacy hull still yields that.
            SlottedMaterialSpec legacy = HullMaterialPublication.ForDeck(HullMaterials.Legacy);
            Assert.Equal("birch", legacy.MaterialTypeId);
            Assert.Equal(MaterialCategory.Wood, legacy.Category);

            // A wooden ship's deck is that wood.
            Assert.Equal("oak", HullMaterialPublication.ForDeck(new HullMaterials("oak", 2, "iron", 2)).MaterialTypeId);

            // An all-metal ship gets metal decking rather than incongruous timber.
            SlottedMaterialSpec metal = HullMaterialPublication.ForDeck(new HullMaterials(null, 0, "steel", 4));
            Assert.Equal("steel", metal.MaterialTypeId);
            Assert.Equal(MaterialCategory.Metal, metal.Category);
        }

        [Fact]
        public void A_null_input_is_survived_rather_than_thrown_on()
        {
            Assert.Empty(HullMaterialPublication.ForHull(null!));
            SlottedMaterialSpec deck = HullMaterialPublication.ForDeck(null!);
            Assert.Equal("birch", deck.MaterialTypeId);
        }
    }
}
