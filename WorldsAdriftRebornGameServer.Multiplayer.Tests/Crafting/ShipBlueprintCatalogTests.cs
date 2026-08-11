using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The per-player SHIP BLUEPRINTS catalogue served in 1274.shipBlueprintList. A fresh
    /// player must have the default blueprint so the list is never empty and a cost bill
    /// can be selected without saving first; SaveBlueprint grows the list.
    /// </summary>
    public class ShipBlueprintCatalogTests
    {
        [Fact]
        public void A_fresh_catalogue_has_the_default_blueprint()
        {
            // Non-empty on first touch => SHIP BLUEPRINTS is clickable immediately.
            var cat = new PlayerShipBlueprints();
            Assert.Single(cat.Available);
            Assert.Equal(PlayerShipBlueprints.DefaultBlueprintId, cat.Available[0]);
            Assert.True(cat.Contains(PlayerShipBlueprints.DefaultBlueprintId));
        }

        [Fact]
        public void Save_adds_a_new_blueprint()
        {
            var cat = new PlayerShipBlueprints();
            Assert.True(cat.Save("My Cutter"));
            Assert.True(cat.Contains("My Cutter"));
            Assert.Equal(2, cat.Available.Count);
        }

        [Fact]
        public void Saving_a_duplicate_id_is_a_no_op_but_succeeds()
        {
            // The client's save must still resolve; the list must not gain a duplicate row.
            var cat = new PlayerShipBlueprints();
            Assert.True(cat.Save("My Cutter"));
            Assert.True(cat.Save("My Cutter"));
            Assert.Equal(2, cat.Available.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Saving_a_null_or_empty_id_is_rejected(string? id)
        {
            var cat = new PlayerShipBlueprints();
            Assert.False(cat.Save(id!));
            Assert.Single(cat.Available);
        }

        [Fact]
        public void Store_is_per_entity_and_seeds_on_first_touch()
        {
            const long a = 900001L;
            const long b = 900002L;
            ShipBlueprintCatalogStore.Forget(a);
            ShipBlueprintCatalogStore.Forget(b);

            PlayerShipBlueprints catA = ShipBlueprintCatalogStore.For(a);
            catA.Save("A-only");

            PlayerShipBlueprints catB = ShipBlueprintCatalogStore.For(b);
            Assert.False(catB.Contains("A-only"));
            Assert.True(catB.Contains(PlayerShipBlueprints.DefaultBlueprintId));

            // Same entity returns the same live catalogue.
            Assert.Same(catA, ShipBlueprintCatalogStore.For(a));

            ShipBlueprintCatalogStore.Forget(a);
            ShipBlueprintCatalogStore.Forget(b);
        }
    }
}
