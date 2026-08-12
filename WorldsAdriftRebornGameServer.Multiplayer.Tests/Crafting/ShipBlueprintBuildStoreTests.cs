using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crafting
{
    /// <summary>
    /// The build store keys on (shipyard, player). The shipyard is a SHARED world
    /// entity, so two players building at the same yard must have separate fill state -
    /// a shipyard-only key would merge them and one player's materials would show in
    /// the other's panel. These pin that isolation.
    /// </summary>
    public class ShipBlueprintBuildStoreTests
    {
        private static ShipBlueprintBuild NewBuild() =>
            new ShipBlueprintBuild("Makeshift Ship", ShipBlueprintRecipe.TestMakeshiftShip());

        [Fact]
        public void Two_players_on_the_same_shipyard_have_separate_builds()
        {
            const long shipyard = 100;
            const long alice = 1;
            const long bob = 2;

            ShipBlueprintBuild aliceBuild = NewBuild();
            ShipBlueprintBuild bobBuild = NewBuild();
            ShipBlueprintBuildStore.Set(shipyard, alice, aliceBuild);
            ShipBlueprintBuildStore.Set(shipyard, bob, bobBuild);

            Assert.Same(aliceBuild, ShipBlueprintBuildStore.Get(shipyard, alice));
            Assert.Same(bobBuild, ShipBlueprintBuildStore.Get(shipyard, bob));
            Assert.NotSame(ShipBlueprintBuildStore.Get(shipyard, alice),
                           ShipBlueprintBuildStore.Get(shipyard, bob));

            ShipBlueprintBuildStore.ForgetPlayer(alice);
            ShipBlueprintBuildStore.ForgetPlayer(bob);
        }

        [Fact]
        public void Clear_drops_only_that_players_build_on_that_shipyard()
        {
            const long shipyard = 200;
            const long alice = 3;
            const long bob = 4;
            ShipBlueprintBuildStore.Set(shipyard, alice, NewBuild());
            ShipBlueprintBuildStore.Set(shipyard, bob, NewBuild());

            ShipBlueprintBuildStore.Clear(shipyard, alice);

            Assert.Null(ShipBlueprintBuildStore.Get(shipyard, alice));
            Assert.NotNull(ShipBlueprintBuildStore.Get(shipyard, bob));

            ShipBlueprintBuildStore.ForgetPlayer(bob);
        }

        [Fact]
        public void ForgetPlayer_drops_that_players_builds_on_every_shipyard()
        {
            const long alice = 5;
            ShipBlueprintBuildStore.Set(300, alice, NewBuild());
            ShipBlueprintBuildStore.Set(301, alice, NewBuild());

            ShipBlueprintBuildStore.ForgetPlayer(alice);

            Assert.Null(ShipBlueprintBuildStore.Get(300, alice));
            Assert.Null(ShipBlueprintBuildStore.Get(301, alice));
        }
    }
}
