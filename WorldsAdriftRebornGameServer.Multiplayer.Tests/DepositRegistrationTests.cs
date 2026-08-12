using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The anchored deposit's spawn wiring through <see cref="WorldEntities.Default"/>:
    /// it is off unless asked for, and when asked it drags in the GLOBAL entity that
    /// carries the biome table the deposit's visualiser blocks on (without it the rock
    /// exists but never draws). The mining loop itself is server-side glue; this pins
    /// that the right entities are placed.
    /// </summary>
    public class DepositRegistrationTests
    {
        private static WorldEntityRegistry Build(bool includeDeposit, string? depositCount = null)
        {
            return WorldEntities.Default(
                new EntityIdAllocator(),
                includeMetal: false,
                includeTree: false,
                includeDeposit: includeDeposit,
                depositCountEnv: depositCount);
        }

        private static int DepositCountIn(WorldEntityRegistry r) =>
            r.Registrations.Count(e => e.AssetName == MetalDeposits.AssetName);

        [Fact]
        public void DepositsAreOffByDefaultAndPlaceNoGlobalEntity()
        {
            WorldEntityRegistry r = Build(includeDeposit: false);
            Assert.Equal(0, DepositCountIn(r));
            Assert.Null(r.ByKey(WorldEntities.GlobalEntityKey));
        }

        [Fact]
        public void EnablingDepositsPlacesOneDepositAndTheGlobalBiomeEntity()
        {
            WorldEntityRegistry r = Build(includeDeposit: true);
            Assert.Equal(1, DepositCountIn(r));

            // The global entity is the biome dependency - present exactly when deposits
            // are, and it is the GlobalEntity prefab the client hangs the biome
            // visualiser on.
            WorldEntity? global = r.ByKey(WorldEntities.GlobalEntityKey);
            Assert.NotNull(global);
            Assert.Equal("GlobalEntity", global!.AssetName);
        }

        [Fact]
        public void TheGlobalEntityIsRegisteredBeforeTheDeposits()
        {
            WorldEntityRegistry r = Build(includeDeposit: true, depositCount: "3");
            var keys = r.Registrations.Select(e => e.Key).ToList();
            int globalAt = keys.IndexOf(WorldEntities.GlobalEntityKey);
            int firstDepositAt = keys.IndexOf(MetalDeposits.KeyFor(0));
            Assert.True(globalAt >= 0 && firstDepositAt >= 0);
            Assert.True(globalAt < firstDepositAt,
                "the global biome entity must be registered before the deposits that depend on it");
        }

        [Fact]
        public void DepositCountCapsThePlacedDepositsButAlwaysKeepsOneGlobalEntity()
        {
            WorldEntityRegistry r = Build(includeDeposit: true, depositCount: "3");
            Assert.Equal(3, DepositCountIn(r));
            // Exactly one global entity regardless of how many deposits are placed.
            Assert.Single(r.Registrations.Where(e => e.AssetName == WorldEntities.GlobalEntityAssetName));
        }
    }
}
