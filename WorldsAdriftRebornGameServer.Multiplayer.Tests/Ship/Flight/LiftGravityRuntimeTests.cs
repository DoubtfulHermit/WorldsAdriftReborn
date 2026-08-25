using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class LiftGravityRuntimeTests
    {
        private const double Dt = FixedFlightClock.StepSeconds;
        private static readonly GravityParameter Gravity = GravityParameter.UnityDefaultApproximation;

        private static ShipMassSnapshot Snapshot(int extraParts = 0, int cores = 1,
            int upgrades = 0, ShipMassSnapshot? previous = null)
        {
            var parts = new List<ShipMassPartInput>();
            long id = 100;
            for (int i = 0; i < cores; i++)
            {
                parts.Add(new ShipMassPartInput(id++, "atlasSkyCore", "CoreMain", "deck", 0, 0, 0));
            }
            for (int i = 0; i < upgrades; i++)
            {
                parts.Add(new ShipMassPartInput(id++, "skyCoreAtlasEnhancer", "CoreAtlasEnhancer",
                    "coreModule", 0, 0, 0));
            }
            for (int i = 0; i < extraParts; i++)
            {
                parts.Add(new ShipMassPartInput(id++, "shipLamp", "LampSmall", "deck", 0, 0, 0));
            }
            return ShipMassEvaluator.Build(new ShipMassInput(3639, null,
                planDecoded: false, cellCount: 0, deckCount: 0,
                hullHalfExtentXMetres: 2.0, hullHalfExtentYMetres: 1.5, hullHalfExtentZMetres: 6.0,
                hullMassOverrideRaw: null, parts), previous);
        }

        private static LiftGravityEvaluation SeamStep(double massKg, double capacityKg,
            double verticalVelocity, double verticalCommand,
            double commandForce = 0.0, double smoothingVelocity = 0.0,
            bool abandoned = false)
        {
            var mass = new ShadowMassProperties(massKg, ShadowVector3.Zero,
                new ShadowVector3(1e5, 1e5, 1e5), true);
            var forces = new VectorRigidBodyShadowResult(mass, ShadowVector3.Zero,
                ShadowVector3.Zero, ShadowVector3.Zero, 0, 0);
            var motion = new ShadowMotionState(new ShadowVector3(0, 300, 0),
                new ShadowVector3(0, verticalVelocity, 0), new ShadowVector3(2, 1.5, 6));
            Assert.True(LiftGravityRuntime.TryIntegrateLinear("hull:test", motion, forces,
                new LiftRuntimeStepPolicy(capacityKg, Gravity, abandoned),
                verticalCommand, commandForce, smoothingVelocity, Dt,
                out IntegratedFlightShadowResult result));
            return result.Lift;
        }

        [Fact]
        public void Gravity_enters_as_a_provenance_labelled_parameter()
        {
            Assert.True(Gravity.IsValid);
            Assert.Contains("APPROXIMATION", Gravity.Provenance);
            Assert.Contains("not recovered", Gravity.Provenance);
            Assert.False(new GravityParameter(9.81, "upward gravity").IsValid);
            Assert.False(new GravityParameter(-9.81, "").IsValid);
        }

        [Fact]
        public void With_the_gate_off_the_effective_capacity_is_exactly_the_historical_seed()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(Snapshot(), Gravity,
                liftRuntimeEnabledForHull: false, existedBeforeLiftActivation: true);

            Assert.Equal(ShipLiftPolicy.SeededTotalLiftKg, plan.EffectiveCapacityKg);
            Assert.Equal(ProductionHullLiftAudit.RecoveredBaseCoreLiftKg, plan.AuthenticCapacityKg);
            Assert.True(plan.EffectiveDivergesFromAuthentic);
            Assert.Contains("OFF", plan.CapacityProvenance);
        }

        [Fact]
        public void Component_1258_serving_does_not_change_while_the_flag_is_off()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(Snapshot(), Gravity,
                liftRuntimeEnabledForHull: false, existedBeforeLiftActivation: true);

            Assert.Equal(ShipLiftPolicy.SeededTotalLiftKg,
                LiftGravityRuntime.Served1258LiftKg(false, plan));
        }

        [Fact]
        public void With_the_gate_on_1258_serves_the_enforced_effective_capacity()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(Snapshot(), Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(plan.EffectiveCapacityKg,
                LiftGravityRuntime.Served1258LiftKg(true, plan));
        }

        [Fact]
        public void A_light_hull_with_a_core_flies_on_its_authentic_capacity()
        {
            ShipMassSnapshot snapshot = Snapshot();
            Assert.True(snapshot.TotalFlightMassKg
                < RetailLiftGravityShadow.LegacyCapacityFloorKg(
                    snapshot.TotalFlightMassKg, Gravity.Magnitude));

            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(snapshot, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(LiftMigrationDisposition.Authentic, plan.Disposition);
            Assert.Equal(plan.AuthenticCapacityKg, plan.EffectiveCapacityKg);
            Assert.False(plan.EffectiveDivergesFromAuthentic);
        }

        [Fact]
        public void A_grandfathered_heavy_hull_gets_the_floor_while_authentic_stays_visible()
        {
            ShipMassSnapshot snapshot = Snapshot(extraParts: 19);
            Assert.True(snapshot.TotalFlightMassKg > ProductionHullLiftAudit.RecoveredBaseCoreLiftKg);

            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(snapshot, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(LiftMigrationDisposition.LegacyGrandfatherRequired, plan.Disposition);
            Assert.Equal(RetailLiftGravityShadow.LegacyCapacityFloorKg(
                snapshot.TotalFlightMassKg, Gravity.Magnitude), plan.EffectiveCapacityKg);
            Assert.Equal(ProductionHullLiftAudit.RecoveredBaseCoreLiftKg, plan.AuthenticCapacityKg);
            Assert.True(plan.EffectiveDivergesFromAuthentic);
        }

        [Fact]
        public void A_future_build_over_authentic_capacity_is_blocked_not_grandfathered()
        {
            ShipMassSnapshot snapshot = Snapshot(extraParts: 19);

            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(snapshot, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: false);

            Assert.Equal(LiftMigrationDisposition.FutureBuildMustBeBlocked, plan.Disposition);
            Assert.Equal(plan.AuthenticCapacityKg, plan.EffectiveCapacityKg);
            Assert.True(snapshot.TotalFlightMassKg > plan.EffectiveCapacityKg);
        }

        [Fact]
        public void A_core_identified_only_by_prefab_lifts_in_the_plan_exactly_as_it_audits()
        {
            // A legacy mount record whose itemType is not core-like but whose
            // prefab IS CoreMain: the production audit counts it as a core
            // (IsCoreIdentity matches either field), so the live plan must lift
            // on it too - plan and audit consult identical evidence, never the
            // collapsed stable key fed into both parameters.
            Assert.True(ProductionHullLiftAudit.IsCoreIdentity("legacyPartRecord", "CoreMain"));
            var parts = new List<ShipMassPartInput>
            {
                new ShipMassPartInput(100, "legacyPartRecord", "CoreMain", "deck", 0, 0, 0),
            };
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(new ShipMassInput(3639, null,
                planDecoded: false, cellCount: 0, deckCount: 0,
                hullHalfExtentXMetres: 2.0, hullHalfExtentYMetres: 1.5,
                hullHalfExtentZMetres: 6.0, hullMassOverrideRaw: null, parts),
                previous: null);

            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(snapshot, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(1, plan.CoreCount);
            Assert.Equal(ProductionHullLiftAudit.RecoveredBaseCoreLiftKg,
                plan.AuthenticCapacityKg);
            Assert.True(plan.EffectiveCapacityKg > 0.0,
                "the prefab-identified core audits as a core but lifted zero in the plan");
        }

        [Fact]
        public void Core_loss_grounds_the_hull_even_under_the_grandfather_policy()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(
                Snapshot(cores: 0, upgrades: 2), Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(0.0, plan.AuthenticCapacityKg);
            Assert.Equal(0.0, plan.EffectiveCapacityKg);
            Assert.Contains("no main core", plan.CapacityProvenance);
        }

        [Fact]
        public void A_coreless_hull_falls_under_exactly_one_gravity()
        {
            LiftGravityEvaluation lift = SeamStep(massKg: 1000.0, capacityKg: 0.0,
                verticalVelocity: 0.0, verticalCommand: 0.0);

            Assert.Equal(Gravity.YMetresPerSecondSquared, lift.VerticalAccelerationMps2, 12);
            Assert.Equal(Gravity.YMetresPerSecondSquared * Dt, lift.NextVerticalVelocityMps, 12);
        }

        [Fact]
        public void Orphan_upgrade_modules_lift_nothing_without_a_core()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(
                Snapshot(cores: 0, upgrades: 3), Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(3, plan.UpgradeCount);
            Assert.Equal(0.0, plan.EffectiveCapacityKg);
        }

        [Fact]
        public void A_corrupt_multi_core_record_never_multiplies_capacity()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(
                Snapshot(cores: 3, upgrades: 1), Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.True(plan.InvalidMultiCore);
            Assert.Equal(ProductionHullLiftAudit.RecoveredBaseCoreLiftKg
                + ProductionHullLiftAudit.RecoveredUpgradeMinimumKg, plan.AuthenticCapacityKg);
        }

        [Fact]
        public void Known_upgrades_add_the_recovered_minimum_each()
        {
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(
                Snapshot(cores: 1, upgrades: 2), Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.Equal(ProductionHullLiftAudit.RecoveredBaseCoreLiftKg
                + (2 * ProductionHullLiftAudit.RecoveredUpgradeMinimumKg),
                plan.AuthenticCapacityKg);
        }

        [Fact]
        public void At_the_exact_threshold_the_hull_hovers_but_cannot_climb()
        {
            LiftGravityEvaluation atThreshold = SeamStep(massKg: 1000.0, capacityKg: 1000.0,
                verticalVelocity: 0.0, verticalCommand: 1.0,
                commandForce: 500.0, smoothingVelocity: 0.0);

            // Strict overload is mass > capacity: equality is NOT overloaded, the
            // weight is exactly cancelled, and every upward command clamps away.
            Assert.False(atThreshold.Overloaded);
            Assert.Equal(atThreshold.MaximumLiftForceNewtons, atThreshold.AppliedLiftForceNewtons);
            Assert.Equal(0.0, atThreshold.NextVerticalVelocityMps);
        }

        [Fact]
        public void One_gram_over_the_threshold_is_strictly_overloaded()
        {
            LiftGravityEvaluation over = SeamStep(massKg: 1000.001, capacityKg: 1000.0,
                verticalVelocity: 0.0, verticalCommand: 0.0);

            Assert.True(over.Overloaded);
            Assert.True(over.NextVerticalVelocityMps < 0.0);
        }

        [Fact]
        public void The_seam_rejects_any_external_vertical_force_it_did_not_inject()
        {
            var mass = new ShadowMassProperties(1000.0, ShadowVector3.Zero,
                new ShadowVector3(1e5, 1e5, 1e5), true);
            var forces = new VectorRigidBodyShadowResult(mass, ShadowVector3.Zero,
                ShadowVector3.Zero, ShadowVector3.Zero, 0, 0);
            var motion = new ShadowMotionState(new ShadowVector3(0, 300, 0),
                ShadowVector3.Zero, new ShadowVector3(2, 1.5, 6));
            var doubledForce = new LiftGravityInput(1000.0, 1e6,
                Gravity.YMetresPerSecondSquared, 0.0, 0.0, Dt,
                externalVerticalForceNewtons: 100.0);

            Assert.False(IntegratedFlightShadow.TryStep(new IntegratedFlightShadowInput(
                "hull:test", motion, forces, doubledForce), out _));
        }

        [Fact]
        public void The_seam_rejects_a_lift_mass_that_disagrees_with_the_force_mass()
        {
            var mass = new ShadowMassProperties(1000.0, ShadowVector3.Zero,
                new ShadowVector3(1e5, 1e5, 1e5), true);
            var forces = new VectorRigidBodyShadowResult(mass, ShadowVector3.Zero,
                ShadowVector3.Zero, ShadowVector3.Zero, 0, 0);
            var motion = new ShadowMotionState(new ShadowVector3(0, 300, 0),
                ShadowVector3.Zero, new ShadowVector3(2, 1.5, 6));
            var wrongMass = new LiftGravityInput(999.0, 1e6,
                Gravity.YMetresPerSecondSquared, 0.0, 0.0, Dt);

            Assert.False(IntegratedFlightShadow.TryStep(new IntegratedFlightShadowInput(
                "hull:test", motion, forces, wrongMass), out _));
        }

        [Fact]
        public void An_abandoned_hull_sinks_to_the_recovered_terminal_creep_and_holds_it()
        {
            double vy = 0.0, commandForce = 0.0, smoothing = 0.0;
            for (int i = 0; i < 3000; i++)
            {
                LiftGravityEvaluation lift = SeamStep(1000.0, ShipLiftPolicy.SeededTotalLiftKg,
                    vy, verticalCommand: 0.0, commandForce, smoothing, abandoned: true);
                vy = lift.NextVerticalVelocityMps;
                commandForce = lift.CommandLiftForceNewtons;
                smoothing = lift.CommandLiftSmoothingVelocity;
            }

            Assert.InRange(vy, -0.2,
                RetailLiftGravityShadow.AbandonedSinkVelocityThresholdMps);
        }

        [Fact]
        public void Part_mutation_rebuilds_the_plan_from_the_new_snapshot_revision()
        {
            ShipMassSnapshot withCore = Snapshot();
            LiftCapacityPlan before = LiftGravityRuntime.PlanFor(withCore, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            ShipMassSnapshot detached = Snapshot(cores: 0, previous: withCore);
            LiftCapacityPlan after = LiftGravityRuntime.PlanFor(detached, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            Assert.True(detached.Revision > withCore.Revision);
            Assert.Equal(withCore.Revision, before.MassSnapshotRevision);
            Assert.Equal(detached.Revision, after.MassSnapshotRevision);
            Assert.True(before.EffectiveCapacityKg > 0.0);
            Assert.Equal(0.0, after.EffectiveCapacityKg);
        }

        [Fact]
        public void Both_capacities_come_from_the_same_snapshot_and_policy_pair()
        {
            ShipMassSnapshot snapshot = Snapshot(extraParts: 19);
            LiftCapacityPlan plan = LiftGravityRuntime.PlanFor(snapshot, Gravity,
                liftRuntimeEnabledForHull: true, existedBeforeLiftActivation: true);

            // The plan carries the snapshot identity, so telemetry can prove the
            // divergence it reports is between two readings of ONE mass truth.
            Assert.Equal(snapshot.Fingerprint, plan.MassSnapshotFingerprint);
            Assert.Equal(snapshot.Revision, plan.MassSnapshotRevision);
            LiftMigrationDecision migration = LiftMigrationPolicy.Decide(
                snapshot.TotalFlightMassKg, plan.AuthenticCapacityKg,
                Gravity.Magnitude, existedBeforeLiftActivation: true);
            Assert.Equal(migration.EffectiveCapacityKg, plan.EffectiveCapacityKg);
        }
    }
}
