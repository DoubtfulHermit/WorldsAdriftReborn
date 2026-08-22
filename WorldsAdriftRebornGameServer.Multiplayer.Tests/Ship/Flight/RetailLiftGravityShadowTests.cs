using System;
using System.Linq;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class RetailLiftGravityShadowTests
    {
        private const double Gravity = -9.81;
        private const double Dt = 0.02;

        [Theory]
        [InlineData(500.0, 1000.0, false, 0.5)]
        [InlineData(1000.0, 1000.0, false, 1.0)]
        [InlineData(1000.001, 1000.0, true, 1.000001)]
        [InlineData(1500.0, 1000.0, true, 1.5)]
        public void Under_at_and_over_capacity_match_retail_overload_rule(
            double mass, double capacity, bool overloaded, double load)
        {
            LiftGravityEvaluation e = Evaluate(mass, capacity);
            Assert.True(e.Valid);
            Assert.Equal(overloaded, e.Overloaded);
            Assert.Equal(load, e.LoadRatio, 5);
        }

        [Fact]
        public void Under_capacity_hover_cancels_gravity_exactly()
        {
            LiftGravityEvaluation e = Evaluate(800, 1000);
            Assert.Equal(800 * 9.81, e.AppliedLiftForceNewtons, 8);
            Assert.Equal(0, e.NetVerticalForceNewtons, 8);
            Assert.Equal(0, e.VerticalAccelerationMps2, 8);
            Assert.Equal(0, e.NextVerticalVelocityMps, 8);
        }

        [Fact]
        public void At_capacity_hover_is_possible_but_positive_command_has_no_headroom()
        {
            var input = new LiftGravityInput(1000, 1000, Gravity, 0, 1, Dt,
                currentCommandLiftForceNewtons: 1000);
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(input);
            Assert.False(e.Overloaded); // strict >, exactly as ShipLiftVisualizer
            Assert.Equal(1000 * 9.81, e.AppliedLiftForceNewtons, 8);
            Assert.Equal(0, e.VerticalAccelerationMps2, 8);
        }

        [Fact]
        public void Under_capacity_full_command_climbs_at_recovered_one_metre_per_second_squared()
        {
            var input = new LiftGravityInput(800, 1000, Gravity, 0, 1, Dt,
                currentCommandLiftForceNewtons: 800);
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(input);
            Assert.Equal(1.0, e.RequestedCommandAccelerationMps2, 8);
            Assert.Equal(1.0, e.VerticalAccelerationMps2, 8);
            Assert.Equal(Dt, e.NextVerticalVelocityMps, 8);
        }

        [Fact]
        public void Over_capacity_sinks_even_while_commanding_up()
        {
            var input = new LiftGravityInput(1200, 1000, Gravity, 0, 1, Dt,
                currentCommandLiftForceNewtons: 1200);
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(input);
            Assert.True(e.Overloaded);
            Assert.Equal(Gravity * (1.0 - 1000.0 / 1200.0), e.VerticalAccelerationMps2, 8);
            Assert.True(e.NextVerticalVelocityMps < 0);
        }

        [Theory]
        [InlineData(2.0001, 1.0, 0.0)]
        [InlineData(-2.0001, -1.0, 0.0)]
        [InlineData(2.0, 1.0, 1.0)]
        [InlineData(-2.0, -1.0, -1.0)]
        public void Vertical_command_uses_recovered_strict_speed_cap(
            double velocity, double command, double expectedAcceleration)
        {
            var input = new LiftGravityInput(800, 2000, Gravity, velocity, command, Dt);
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(input);
            Assert.Equal(expectedAcceleration, e.RequestedCommandAccelerationMps2, 8);
        }

        [Fact]
        public void Abandoned_hull_sinks_only_until_the_recovered_threshold()
        {
            LiftGravityEvaluation start = RetailLiftGravityShadow.Step(
                new LiftGravityInput(800, 2000, Gravity, 0, 1, Dt, isAbandoned: true));
            LiftGravityEvaluation already = RetailLiftGravityShadow.Step(
                new LiftGravityInput(800, 2000, Gravity, -0.1001, 1, Dt, isAbandoned: true));
            Assert.Equal(-0.05, start.RequestedCommandAccelerationMps2, 8);
            Assert.Equal(0, already.RequestedCommandAccelerationMps2, 8);
        }

        [Fact]
        public void Fuel_state_cannot_change_lift_because_policy_has_no_fuel_input()
        {
            // Two identical evaluations stand for full and empty fuel. Retail's sky
            // core consumes nothing; fuel gates engines, never lift.
            LiftGravityEvaluation full = Evaluate(800, 1000);
            LiftGravityEvaluation empty = Evaluate(800, 1000);
            Assert.Equal(full.AppliedLiftForceNewtons, empty.AppliedLiftForceNewtons);
            Assert.Equal(full.VerticalAccelerationMps2, empty.VerticalAccelerationMps2);
            Assert.DoesNotContain(typeof(LiftGravityInput).GetProperties(),
                p => p.Name.Contains("Fuel", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        [InlineData(-1.0)]
        [InlineData(0.0)]
        public void Negative_nonfinite_or_zero_capacity_is_safely_treated_as_no_lift(double capacity)
        {
            LiftGravityEvaluation e = Evaluate(800, capacity);
            Assert.True(e.Valid);
            Assert.Equal(0, e.LiftCapacityKg);
            Assert.True(e.Overloaded);
            Assert.Equal(0, e.AppliedLiftForceNewtons);
            Assert.Equal(Gravity, e.VerticalAccelerationMps2, 8);
        }

        [Theory]
        [InlineData(double.NaN, -9.81, 0.02)]
        [InlineData(-1.0, -9.81, 0.02)]
        [InlineData(800.0, 9.81, 0.02)]
        [InlineData(800.0, -9.81, 0.0)]
        [InlineData(800.0, -9.81, double.NaN)]
        public void Invalid_mass_gravity_or_step_is_quarantined(double mass, double gravity, double dt)
        {
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(
                new LiftGravityInput(mass, 1000, gravity, 3.5, 1, dt));
            Assert.False(e.Valid);
            Assert.Equal(3.5, e.NextVerticalVelocityMps);
        }

        [Fact]
        public void Compensation_force_and_external_force_cancel_in_the_shadow_adapter()
        {
            const double gust = 300;
            LiftGravityEvaluation e = RetailLiftGravityShadow.Step(
                new LiftGravityInput(800, 2000, Gravity, 0, 0, Dt,
                    externalVerticalForceNewtons: gust,
                    compensationForceNewtons: -gust));
            Assert.Equal(0, e.NetVerticalForceNewtons, 8);
        }

        [Fact]
        public void Legacy_floor_preserves_hover_and_full_climb_not_just_hover()
        {
            double floor = RetailLiftGravityShadow.LegacyCapacityFloorKg(1071, 9.81);
            Assert.Equal(1071 * (1 + 1.0 / 9.81), floor, 8);
            LiftMigrationDecision d = LiftMigrationPolicy.Decide(1071, 1000, 9.81, true);
            Assert.Equal(LiftMigrationDisposition.LegacyGrandfatherRequired, d.Disposition);
            Assert.Equal(floor, d.EffectiveCapacityKg, 8);
            Assert.Equal(1000, d.AuthenticCapacityKg);
        }

        [Fact]
        public void Same_overweight_design_is_grandfathered_only_when_it_already_exists()
        {
            LiftMigrationDecision old = LiftMigrationPolicy.Decide(1071, 1000, 9.81, true);
            LiftMigrationDecision fresh = LiftMigrationPolicy.Decide(1071, 1000, 9.81, false);
            Assert.Equal(LiftMigrationDisposition.LegacyGrandfatherRequired, old.Disposition);
            Assert.Equal(LiftMigrationDisposition.FutureBuildMustBeBlocked, fresh.Disposition);
            Assert.Equal(1000, fresh.EffectiveCapacityKg);
        }

        [Theory]
        [InlineData(double.NaN, 1000, 9.81)]
        [InlineData(-1, 1000, 9.81)]
        [InlineData(800, 1000, double.NaN)]
        [InlineData(800, 1000, 0)]
        public void Migration_refuses_invalid_mass_or_gravity(double mass, double capacity, double gravity)
        {
            Assert.Equal(LiftMigrationDisposition.Invalid,
                LiftMigrationPolicy.Decide(mass, capacity, gravity, true).Disposition);
        }

        [Fact]
        public void Core_detach_is_visible_as_zero_capacity_and_never_silently_authentic()
        {
            WorldStateSnapshot withCore = SnapshotWithParts("atlasSkyCore");
            WorldStateSnapshot withoutCore = SnapshotWithParts();
            ProductionHullLiftAuditRow before = ProductionHullLiftAudit.Audit(withCore, 9.81).Single();
            ProductionHullLiftAuditRow after = ProductionHullLiftAudit.Audit(withoutCore, 9.81).Single();
            Assert.Equal(1000, before.KnownMinimumCapacityKg);
            Assert.Equal(0, after.KnownMinimumCapacityKg);
            Assert.Equal(LiftMigrationDisposition.LegacyGrandfatherRequired, after.Migration.Disposition);
        }

        [Fact]
        public void Orphan_upgrade_has_no_capacity_without_a_core()
        {
            ProductionHullLiftAuditRow row = ProductionHullLiftAudit.Audit(
                SnapshotWithParts("skyCoreGenerator"), 9.81).Single();
            Assert.Equal(1, row.RecoveredMinimumUpgradeCount);
            Assert.Equal(0, row.CoreCount);
            Assert.Equal(0, row.KnownMinimumCapacityKg);
            Assert.Equal(LiftMigrationDisposition.LegacyGrandfatherRequired, row.Migration.Disposition);
        }

        [Fact]
        public void Multiple_cores_are_reported_and_never_multiply_the_single_core_capacity()
        {
            ProductionHullLiftAuditRow row = ProductionHullLiftAudit.Audit(
                SnapshotWithParts("atlasSkyCore", "atlasSkyCore"), 9.81).Single();
            Assert.Equal(2, row.CoreCount);
            Assert.Equal(1000, row.KnownMinimumCapacityKg);
            Assert.Contains("INVALID MULTI-CORE", row.Note);
        }

        [Fact]
        public void Production_audit_counts_core_upgrade_mass_and_recovered_minimum_capacity()
        {
            WorldStateSnapshot snapshot = SnapshotWithParts(
                "atlasSkyCore", "skyCoreAtlasEnhancer", "skyCoreGenerator", "sail");
            ProductionHullLiftAuditRow row = ProductionHullLiftAudit.Audit(snapshot, 9.81).Single();
            double hullMass = HullMassCalculator.HullMassKg(HullMaterials.Legacy, 1, 1);
            Assert.True(row.Valid);
            Assert.Equal(hullMass + 4 * ShipTotalMass.MountedPartMassKg, row.MassKg, 6);
            Assert.Equal(1800, row.KnownMinimumCapacityKg);
            Assert.Equal(1, row.CoreCount);
            Assert.Equal(2, row.RecoveredMinimumUpgradeCount);
            Assert.False(row.ExactCapacityKnown);
            Assert.Contains("lower bound", row.Note);
            Assert.Equal(LiftMigrationDisposition.Authentic, row.Migration.Disposition);
        }

        [Fact]
        public void Production_audit_does_not_mutate_snapshot_and_survives_restart_representation()
        {
            WorldStateSnapshot before = SnapshotWithParts("atlasSkyCore", "sail");
            string json = JsonSerializer.Serialize(before);
            ProductionHullLiftAuditRow first = ProductionHullLiftAudit.Audit(before, 9.81).Single();
            Assert.Equal(json, JsonSerializer.Serialize(before));

            WorldStateSnapshot restored = JsonSerializer.Deserialize<WorldStateSnapshot>(json)!;
            ProductionHullLiftAuditRow second = ProductionHullLiftAudit.Audit(restored, 9.81).Single();
            Assert.Equal(first.MassKg, second.MassKg);
            Assert.Equal(first.KnownMinimumCapacityKg, second.KnownMinimumCapacityKg);
            Assert.Equal(first.Migration.Disposition, second.Migration.Disposition);
        }

        [Fact]
        public void Production_audit_reports_corrupt_hull_without_throwing()
        {
            WorldStateSnapshot snapshot = SnapshotWithParts("atlasSkyCore");
            snapshot.BuiltShips[0].HullBytes = new byte[] { 1, 2, 3 };
            ProductionHullLiftAuditRow row = ProductionHullLiftAudit.Audit(snapshot, 9.81).Single();
            Assert.False(row.Valid);
            Assert.Equal(LiftMigrationDisposition.Invalid, row.Migration.Disposition);
            Assert.NotEmpty(row.Note);
        }

        [Fact]
        public void Salvaged_hulls_are_excluded_and_dangling_parts_do_not_create_rows()
        {
            WorldStateSnapshot snapshot = SnapshotWithParts("atlasSkyCore");
            snapshot.BuiltShips[0].Salvaged = true;
            Assert.Empty(ProductionHullLiftAudit.Audit(snapshot, 9.81));
        }

        [Fact]
        public void Component_admin_and_shadow_telemetry_share_one_mass_and_lift_projection()
        {
            LiftGravityEvaluation e = Evaluate(800, 1000);
            LiftMigrationDecision d = LiftMigrationPolicy.Decide(800, 1000, 9.81, true);
            LiftGravityTelemetry t = LiftGravityTelemetry.From(e, d, exactRecoveredCapacity: false);
            Assert.Equal(800, t.Component1257MassKg);
            Assert.Equal(1000, t.Component1258LiftKg);
            Assert.Equal(0.8, t.LoadRatio, 8);
            Assert.False(t.Overloaded);
            Assert.Equal(e.AppliedLiftForceNewtons, t.AppliedLiftNewtons);
            Assert.Contains("recovered-minimum", t.CapacityProvenance);
        }

        [Fact]
        public void Telemetry_rejects_an_authentic_evaluation_paired_with_a_grandfather_capacity()
        {
            LiftGravityEvaluation authentic = Evaluate(1071, 1000);
            LiftMigrationDecision grandfather = LiftMigrationPolicy.Decide(1071, 1000, 9.81, true);
            Assert.Throws<ArgumentException>(() =>
                LiftGravityTelemetry.From(authentic, grandfather, exactRecoveredCapacity: false));

            LiftGravityEvaluation effective = Evaluate(1071, grandfather.EffectiveCapacityKg);
            LiftGravityTelemetry telemetry = LiftGravityTelemetry.From(
                effective, grandfather, exactRecoveredCapacity: false);
            Assert.Equal(grandfather.EffectiveCapacityKg, telemetry.Component1258LiftKg);
            Assert.False(telemetry.Overloaded);
        }

        [Fact]
        public void Part_mutation_recomputes_mass_and_migration_instead_of_caching_stale_load()
        {
            WorldStateSnapshot snapshot = SnapshotWithParts("atlasSkyCore");
            ProductionHullLiftAuditRow before = ProductionHullLiftAudit.Audit(snapshot, 9.81).Single();
            for (int i = 0; i < 20; i++)
            {
                snapshot.MountedParts.Add(Part(0, "sail"));
            }
            ProductionHullLiftAuditRow after = ProductionHullLiftAudit.Audit(snapshot, 9.81).Single();
            Assert.Equal(before.MassKg + 20 * ShipTotalMass.MountedPartMassKg, after.MassKg, 6);
            Assert.NotEqual(before.Migration.Disposition, after.Migration.Disposition);
        }

        private static LiftGravityEvaluation Evaluate(double mass, double capacity) =>
            RetailLiftGravityShadow.Step(new LiftGravityInput(mass, capacity, Gravity, 0, 0, Dt));

        private static WorldStateSnapshot SnapshotWithParts(params string[] itemTypes)
        {
            var snapshot = new WorldStateSnapshot();
            snapshot.BuiltShips.Add(new BuiltShipRecord
            {
                HullBytes = ShipHull.MinimumHullData()
            });
            foreach (string itemType in itemTypes)
            {
                snapshot.MountedParts.Add(Part(0, itemType));
            }
            return snapshot;
        }

        private static MountedPartRecord Part(int shipIndex, string itemType) => new MountedPartRecord
        {
            PartUid = Guid.NewGuid().ToString("N"),
            BuiltShipIndex = shipIndex,
            ItemType = itemType,
            PrefabName = itemType == "atlasSkyCore" ? "CoreMain" : ""
        };
    }
}
