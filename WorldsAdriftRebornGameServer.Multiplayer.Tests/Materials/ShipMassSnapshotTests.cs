using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    public sealed class ShipMassSnapshotTests
    {
        private static ShipMassSnapshot Snapshot() => ShipMassEvaluator.Build(
            new ShipMassInput(42, new HullMaterials("birch", 5, "iron", 5),
                planDecoded: true, cellCount: 1, deckCount: 1, 2.0, 1.0, 3.0,
                hullMassOverrideRaw: null, parts: new[]
                {
                    new ShipMassPartInput(9, "helm", "Helm01", "deck", 0, 0, 0),
                    new ShipMassPartInput(7, "engine", "proceduralEngineDefault", "engine", 0, 0, -2),
                }),
            previous: null);

        [Fact]
        public void Part_mass_lookup_answers_by_runtime_entity_id()
        {
            ShipMassSnapshot snapshot = Snapshot();
            Assert.True(snapshot.TryPartMassKg(7, out double engine));
            Assert.Equal(58.5, engine);
            Assert.True(snapshot.TryPartMassKg(9, out double helm));
            Assert.Equal(50.0, helm);
        }

        [Fact]
        public void Part_mass_lookup_refuses_an_id_the_snapshot_does_not_carry()
        {
            Assert.False(Snapshot().TryPartMassKg(12345, out double massKg));
            Assert.Equal(0.0, massKg);
        }

        [Fact]
        public void The_snapshot_is_internally_consistent_for_every_reader()
        {
            ShipMassSnapshot snapshot = Snapshot();
            Assert.Equal(42, snapshot.HullEntityId);
            Assert.Equal(new long[] { 7, 9 },
                snapshot.MountedParts.Select(p => p.EntityId).ToArray());
            Assert.Equal(snapshot.MountedParts.Sum(p => p.MassKg),
                snapshot.TotalMountedMassKg, 12);
            Assert.Equal(snapshot.HullStructuralMassKg + snapshot.TotalMountedMassKg,
                snapshot.TotalFlightMassKg, 12);
            Assert.Equal(
                snapshot.HullStructuralMassKg
                    + snapshot.MountedParts.Count * ShipMassEvaluator.DefaultPartMassKg,
                snapshot.LegacyFlatTotalMassKg, 12);
            Assert.NotEmpty(snapshot.Fingerprint);
            Assert.Equal(1, snapshot.Revision);
        }
    }
}
