using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    public class FuelLifecyclePolicyTests
    {
        private const long Hull = 501;
        private const long OtherHull = 502;
        private const long Generator = 701;

        [Fact]
        public void Latched_unmanned_demand_keeps_burning_without_more_input_deltas()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Generator, Hull, 100);
            ledger.SetDemand(Hull, new HullPropulsionDemand(0.5, 2));

            ledger.Burn(10, 1);
            ledger.Burn(10, 1); // no new 1111 and no pilot mirror update

            Assert.Equal(80, ledger.Read(Hull).Level);
            Assert.Equal(0.5, ledger.DemandOf(Hull).Throttle);
        }

        [Fact]
        public void Remanning_without_a_throttle_delta_does_not_change_demand()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Generator, Hull, 100);
            ledger.SetDemand(Hull, new HullPropulsionDemand(-0.75, 1));

            ledger.Burn(4, 2);

            Assert.Equal(-0.75, ledger.DemandOf(Hull).Throttle);
            Assert.Equal(94, ledger.Read(Hull).Level);
        }

        [Fact]
        public void No_combustion_engine_means_no_burn_even_with_a_latched_lever()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Generator, Hull, 100);
            ledger.SetDemand(Hull, new HullPropulsionDemand(1, 0));

            ledger.Burn(1000, 25);

            Assert.Equal(100, ledger.Read(Hull).Level);
        }

        [Fact]
        public void Multiple_engines_scale_consumption_while_generators_scale_range()
        {
            var oneEngine = new ShipFuelLedger();
            oneEngine.Register(Generator, Hull, 100);
            oneEngine.Register(702, Hull, 100);
            oneEngine.SetDemand(Hull, new HullPropulsionDemand(1, 1));

            var twoEngines = new ShipFuelLedger();
            twoEngines.Register(Generator, Hull, 100);
            twoEngines.Register(702, Hull, 100);
            twoEngines.SetDemand(Hull, new HullPropulsionDemand(1, 2));

            oneEngine.Burn(10, 1);
            twoEngines.Burn(10, 1);

            Assert.Equal(190, oneEngine.Read(Hull).Level);
            Assert.Equal(180, twoEngines.Read(Hull).Level);
            Assert.Equal(200, twoEngines.Read(Hull).Capacity);
        }

        [Fact]
        public void Dry_engine_gate_can_remove_engine_force_without_removing_sail_force()
        {
            var input = new FlightControlInput(1, 0, 0, 0, 0);
            var dryPropulsion = new ShipPropulsion(800, engineThrustNewtons: 0, unfurledSails: 1);

            ShipForceEvaluation evaluation = ShipForceEvaluator.Evaluate(
                0, 0, 0, input, dryPropulsion, new FlightTuning(), 0);

            Assert.Equal(0, evaluation.EngineForceNewtons);
            Assert.NotEqual(0, evaluation.SailForceNewtons);
        }

        [Fact]
        public void Generator_snapshot_round_trips_an_explicit_empty_tank()
        {
            var snapshot = GeneratorFuelSnapshot.Capture(new FuelReading(100, 0));
            var mounted = new MountedPartRecord
            {
                PartUid = "stable-generator",
                ItemType = "powerGenerator",
                GeneratorFuel = snapshot,
            };

            string json = JsonSerializer.Serialize(mounted);
            MountedPartRecord restored = JsonSerializer.Deserialize<MountedPartRecord>(json)!;

            Assert.NotNull(restored.GeneratorFuel);
            Assert.True(restored.GeneratorFuel!.TryRestore(100, out FuelReading reading));
            Assert.Equal(0, reading.Level);
        }

        [Fact]
        public void Legacy_record_without_snapshot_remains_distinguishable_from_empty()
        {
            MountedPartRecord restored = JsonSerializer.Deserialize<MountedPartRecord>(
                "{\"PartUid\":\"legacy\",\"ItemType\":\"powerGenerator\"}")!;

            Assert.Null(restored.GeneratorFuel);
        }

        [Fact]
        public void Corrupt_snapshot_fails_closed_and_never_grants_fuel()
        {
            var negative = new GeneratorFuelSnapshot { Version = 1, Capacity = 100, Level = -10 };
            var future = new GeneratorFuelSnapshot { Version = 999, Capacity = 100, Level = 100 };
            var overCapacity = new GeneratorFuelSnapshot { Version = 1, Capacity = 100, Level = 101 };

            Assert.False(negative.TryRestore(100, out FuelReading negativeReading));
            Assert.Equal(0, negativeReading.Level);
            Assert.False(future.TryRestore(100, out FuelReading futureReading));
            Assert.Equal(0, futureReading.Level);
            Assert.False(overCapacity.TryRestore(100, out FuelReading overCapacityReading));
            Assert.Equal(0, overCapacityReading.Level);
        }

        [Fact]
        public void Detached_restore_transfers_the_same_fuel_to_another_hull_without_duplication()
        {
            var ledger = new ShipFuelLedger();
            var snapshot = GeneratorFuelSnapshot.Capture(new FuelReading(100, 37.5));

            Assert.True(ledger.RestoreDetached(Generator, snapshot, 100));
            Assert.False(ledger.IsMetered(Hull));
            Assert.True(ledger.Register(Generator, OtherHull, 100));

            Assert.Equal(37.5, ledger.Read(OtherHull).Level);
            Assert.Equal(1, ledger.GeneratorsOn(OtherHull));
            Assert.Equal(0, ledger.GeneratorsOn(Hull));
            Assert.False(ledger.Register(Generator, OtherHull, 100));
            Assert.Equal(37.5, ledger.Read(OtherHull).Level);
        }

        [Fact]
        public void Capacity_change_clamps_level_without_treating_it_as_corruption()
        {
            var snapshot = new GeneratorFuelSnapshot { Version = 1, Capacity = 100, Level = 80 };

            Assert.True(snapshot.TryRestore(50, out FuelReading restored));
            Assert.Equal(50, restored.Capacity);
            Assert.Equal(50, restored.Level);
        }
    }
}
