using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The material a crafted ship part publishes on 1099 decides which MESH the
    /// client builds for a panel prefab, and the shipped client has no wooden
    /// window mesh at any size. These tests pin the one row that has to differ and
    /// the invariant that stops the fix being generalised into a crash.
    /// </summary>
    public class LoosePartSeedMaterialTests
    {
        /// <summary>
        /// THE FIX. metalWindowPanelMeshes1X1 (4 meshes) is the ONLY populated
        /// window mesh array in the shipped ShipPanelDefinitions; all three wooden
        /// window arrays are empty. A wood-seeded window renders nothing at all.
        /// </summary>
        [Fact]
        public void TheWindowIsSeededMetalBecauseNoWoodenWindowMeshExists()
        {
            PartSeedMaterial window = LoosePartSeedMaterial.For("window");

            Assert.Equal(MaterialCategory.Metal, window.Category);
            Assert.Equal("iron", window.MaterialTypeId);
        }

        /// <summary>
        /// The window is resolved through the CATALOGUE too, not only by a bare
        /// string - if the row's itemType is ever renamed, the material must follow
        /// it or the window silently goes invisible again.
        /// </summary>
        [Fact]
        public void TheCatalogueWindowRowResolvesToTheMetalSeed()
        {
            LoosePartDefinition? window = LoosePartCatalogue.ForSchematic("window");

            Assert.NotNull(window);
            Assert.Equal(LoosePartSeedMaterial.WindowItemType, window!.ItemType);
            Assert.Equal(MaterialCategory.Metal, window.SeedMaterial.Category);
            Assert.Equal("iron", window.SeedMaterial.MaterialTypeId);
        }

        /// <summary>
        /// Every OTHER part keeps the deck's proven-safe Wood material. This is the
        /// helm-freeze fix's own choice and it must not be disturbed: category
        /// "Wood" makes GetPrefabFromMaterial return the baked _woodPrefab that
        /// every ship-part prefab carries.
        /// </summary>
        [Fact]
        public void EveryOtherCatalogueRowKeepsTheProvenWoodSeed()
        {
            List<LoosePartDefinition> others = LoosePartCatalogue.All
                .Where(def => def.ItemType != LoosePartSeedMaterial.WindowItemType)
                .ToList();

            Assert.NotEmpty(others);
            foreach (LoosePartDefinition def in others)
            {
                Assert.Equal(MaterialCategory.Wood, def.SeedMaterial.Category);
                Assert.Equal(Deck.MaterialTypeId, def.SeedMaterial.MaterialTypeId);
            }
        }

        /// <summary>
        /// PartGraphicsVariationByMaterial.GetPrefabFromMaterial THROWS on any
        /// category that is not "Wood" or "Metal". A third value here would not be
        /// an invisible part, it would be an exception on every checkout.
        /// </summary>
        [Fact]
        public void NoPartMayPublishACategoryTheClientThrowsOn()
        {
            foreach (LoosePartDefinition def in LoosePartCatalogue.All)
            {
                Assert.True(
                    def.SeedMaterial.Category == MaterialCategory.Wood
                    || def.SeedMaterial.Category == MaterialCategory.Metal,
                    def.ItemType + " publishes category '" + def.SeedMaterial.Category
                    + "'; the client only accepts \"Wood\" or \"Metal\" and throws on anything else.");
                Assert.False(string.IsNullOrWhiteSpace(def.SeedMaterial.MaterialTypeId),
                    def.ItemType + " must name a real material; an empty id NREs ComponentMaterialColors.");
            }
        }

        /// <summary>
        /// An unknown or absent itemType must fall back to Wood, so a part this map
        /// has never heard of behaves exactly as it did before the map existed.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("no-such-part")]
        public void AnUnknownPartFallsBackToWood(string? itemType)
        {
            PartSeedMaterial material = LoosePartSeedMaterial.For(itemType);

            Assert.Equal(MaterialCategory.Wood, material.Category);
            Assert.Equal(Deck.MaterialTypeId, material.MaterialTypeId);
        }
    }
}
