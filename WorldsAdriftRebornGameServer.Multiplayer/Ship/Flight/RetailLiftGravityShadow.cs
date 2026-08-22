using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Recovered retail vertical-flight policy, kept deliberately outside the live
    /// <see cref="FlightSession"/> until the fixed clock and vector rigidbody tracks
    /// are integrated.  It has no Unity, wire or wall-clock dependency, so it can be
    /// replayed as a shadow evaluator without moving a production hull.
    /// </summary>
    public static class RetailLiftGravityShadow
    {
        // RECOVERED from ShipControlVisualizer's serialized fields and code.
        public const double VerticalSpeedCapMps = 2.0;
        public const double VerticalAccelerationCapMps2 = 1.0;
        public const double AbandonedSinkAccelerationMps2 = -0.05;
        public const double AbandonedSinkVelocityThresholdMps = -0.1;

        /// <summary>
        /// Evaluates one deterministic vertical step. Gravity is an input because the
        /// surviving code reads Unity's project-wide Physics.gravity; its serialized
        /// magnitude has not yet been recovered reliably. A Track-3 adapter must feed
        /// the same gravity vector used by the vector shadow model.
        /// </summary>
        public static LiftGravityEvaluation Step(LiftGravityInput input)
        {
            if (!input.IsValid)
            {
                return LiftGravityEvaluation.Invalid(input.VerticalVelocityMps);
            }

            double mass = input.MassKg;
            double capacity = SanitizeCapacity(input.LiftCapacityKg);
            double gravityMagnitude = Math.Abs(input.GravityYMetresPerSecondSquared);
            double requested = Math.Clamp(input.VerticalCommand, -1.0, 1.0);
            double commandAcceleration;
            if (input.IsAbandoned)
            {
                commandAcceleration = input.VerticalVelocityMps < AbandonedSinkVelocityThresholdMps
                    ? 0.0
                    : AbandonedSinkAccelerationMps2;
            }
            else
            {
                if ((requested > 0.0 && input.VerticalVelocityMps > VerticalSpeedCapMps)
                    || (requested < 0.0 && input.VerticalVelocityMps < -VerticalSpeedCapMps))
                {
                    requested = 0.0;
                }
                commandAcceleration = requested * VerticalAccelerationCapMps2;
            }

            double targetCommandForce = commandAcceleration * mass;
            (double commandForce, double smoothingVelocity) = SmoothDamp(
                input.CurrentCommandLiftForceNewtons,
                targetCommandForce,
                input.CommandLiftSmoothingVelocity,
                input.DeltaSeconds * 8.0,
                input.DeltaSeconds);

            double weightCancellation = mass * gravityMagnitude;
            double maximumLift = capacity * gravityMagnitude;
            double requestedLift = weightCancellation
                + input.CompensationForceNewtons
                + commandForce;
            double appliedLift = Math.Clamp(requestedLift, 0.0, maximumLift);
            double gravityForce = mass * input.GravityYMetresPerSecondSquared;
            double netForce = gravityForce + input.ExternalVerticalForceNewtons + appliedLift;
            double acceleration = netForce / mass;
            double nextVelocity = input.VerticalVelocityMps + acceleration * input.DeltaSeconds;

            return new LiftGravityEvaluation(
                valid: true,
                massKg: mass,
                liftCapacityKg: capacity,
                loadRatio: capacity > 0.0 ? mass / capacity : double.PositiveInfinity,
                overloaded: mass > capacity,
                requestedCommandAccelerationMps2: commandAcceleration,
                commandLiftForceNewtons: commandForce,
                commandLiftSmoothingVelocity: smoothingVelocity,
                maximumLiftForceNewtons: maximumLift,
                appliedLiftForceNewtons: appliedLift,
                netVerticalForceNewtons: netForce,
                verticalAccelerationMps2: acceleration,
                nextVerticalVelocityMps: nextVelocity);
        }

        public static double SanitizeCapacity(double capacityKg) =>
            double.IsFinite(capacityKg) && capacityKg > 0.0 ? capacityKg : 0.0;

        /// <summary>
        /// Capacity which both cancels weight and preserves retail's full +1 m/s2
        /// climb authority. This is the safe additive floor for a legacy hull; merely
        /// setting capacity equal to mass would hover but clamp away every upward
        /// command.
        /// </summary>
        public static double LegacyCapacityFloorKg(double massKg, double gravityMagnitude)
        {
            if (!double.IsFinite(massKg) || massKg <= 0.0
                || !double.IsFinite(gravityMagnitude) || gravityMagnitude <= 0.0)
            {
                return 0.0;
            }
            return massKg * (1.0 + VerticalAccelerationCapMps2 / gravityMagnitude);
        }

        // Unity Mathf.SmoothDamp's deterministic scalar form. The polynomial and
        // overshoot guard are recovered engine behavior, not a new tuning curve.
        private static (double Value, double Velocity) SmoothDamp(
            double current, double target, double velocity, double smoothTime, double dt)
        {
            smoothTime = Math.Max(0.0001, smoothTime);
            double omega = 2.0 / smoothTime;
            double x = omega * dt;
            double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = current - target;
            double originalTarget = target;
            double temp = (velocity + omega * change) * dt;
            velocity = (velocity - omega * temp) * exp;
            double output = target + (change + temp) * exp;
            if ((originalTarget - current > 0.0) == (output > originalTarget))
            {
                output = originalTarget;
                velocity = (output - originalTarget) / dt;
            }
            return (output, velocity);
        }
    }

    public readonly struct LiftGravityInput
    {
        public LiftGravityInput(double massKg, double liftCapacityKg,
            double gravityYMetresPerSecondSquared, double verticalVelocityMps,
            double verticalCommand, double deltaSeconds,
            double externalVerticalForceNewtons = 0.0,
            double compensationForceNewtons = 0.0,
            double currentCommandLiftForceNewtons = 0.0,
            double commandLiftSmoothingVelocity = 0.0,
            bool isAbandoned = false)
        {
            MassKg = massKg;
            LiftCapacityKg = liftCapacityKg;
            GravityYMetresPerSecondSquared = gravityYMetresPerSecondSquared;
            VerticalVelocityMps = verticalVelocityMps;
            VerticalCommand = verticalCommand;
            DeltaSeconds = deltaSeconds;
            ExternalVerticalForceNewtons = externalVerticalForceNewtons;
            CompensationForceNewtons = compensationForceNewtons;
            CurrentCommandLiftForceNewtons = currentCommandLiftForceNewtons;
            CommandLiftSmoothingVelocity = commandLiftSmoothingVelocity;
            IsAbandoned = isAbandoned;
        }

        public double MassKg { get; }
        public double LiftCapacityKg { get; }
        public double GravityYMetresPerSecondSquared { get; }
        public double VerticalVelocityMps { get; }
        public double VerticalCommand { get; }
        public double DeltaSeconds { get; }
        public double ExternalVerticalForceNewtons { get; }
        public double CompensationForceNewtons { get; }
        public double CurrentCommandLiftForceNewtons { get; }
        public double CommandLiftSmoothingVelocity { get; }
        public bool IsAbandoned { get; }

        public bool IsValid => double.IsFinite(MassKg) && MassKg > 0.0
            && double.IsFinite(GravityYMetresPerSecondSquared)
            && GravityYMetresPerSecondSquared < 0.0
            && double.IsFinite(VerticalVelocityMps)
            && double.IsFinite(VerticalCommand)
            && double.IsFinite(DeltaSeconds) && DeltaSeconds > 0.0
            && double.IsFinite(ExternalVerticalForceNewtons)
            && double.IsFinite(CompensationForceNewtons)
            && double.IsFinite(CurrentCommandLiftForceNewtons)
            && double.IsFinite(CommandLiftSmoothingVelocity);
    }

    public readonly struct LiftGravityEvaluation
    {
        public LiftGravityEvaluation(bool valid, double massKg, double liftCapacityKg,
            double loadRatio, bool overloaded, double requestedCommandAccelerationMps2,
            double commandLiftForceNewtons, double commandLiftSmoothingVelocity,
            double maximumLiftForceNewtons, double appliedLiftForceNewtons,
            double netVerticalForceNewtons, double verticalAccelerationMps2,
            double nextVerticalVelocityMps)
        {
            Valid = valid;
            MassKg = massKg;
            LiftCapacityKg = liftCapacityKg;
            LoadRatio = loadRatio;
            Overloaded = overloaded;
            RequestedCommandAccelerationMps2 = requestedCommandAccelerationMps2;
            CommandLiftForceNewtons = commandLiftForceNewtons;
            CommandLiftSmoothingVelocity = commandLiftSmoothingVelocity;
            MaximumLiftForceNewtons = maximumLiftForceNewtons;
            AppliedLiftForceNewtons = appliedLiftForceNewtons;
            NetVerticalForceNewtons = netVerticalForceNewtons;
            VerticalAccelerationMps2 = verticalAccelerationMps2;
            NextVerticalVelocityMps = nextVerticalVelocityMps;
        }

        public bool Valid { get; }
        public double MassKg { get; }
        public double LiftCapacityKg { get; }
        public double LoadRatio { get; }
        public bool Overloaded { get; }
        public double RequestedCommandAccelerationMps2 { get; }
        public double CommandLiftForceNewtons { get; }
        public double CommandLiftSmoothingVelocity { get; }
        public double MaximumLiftForceNewtons { get; }
        public double AppliedLiftForceNewtons { get; }
        public double NetVerticalForceNewtons { get; }
        public double VerticalAccelerationMps2 { get; }
        public double NextVerticalVelocityMps { get; }

        public static LiftGravityEvaluation Invalid(double currentVelocity) =>
            new LiftGravityEvaluation(false, 0, 0, 0, false, 0, 0, 0, 0, 0, 0, 0,
                double.IsFinite(currentVelocity) ? currentVelocity : 0.0);
    }

    public enum LiftMigrationDisposition
    {
        Authentic,
        LegacyGrandfatherRequired,
        FutureBuildMustBeBlocked,
        Invalid
    }

    public readonly struct LiftMigrationDecision
    {
        public LiftMigrationDecision(LiftMigrationDisposition disposition,
            double authenticCapacityKg, double effectiveCapacityKg, string reason)
        {
            Disposition = disposition;
            AuthenticCapacityKg = authenticCapacityKg;
            EffectiveCapacityKg = effectiveCapacityKg;
            Reason = reason;
        }
        public LiftMigrationDisposition Disposition { get; }
        public double AuthenticCapacityKg { get; }
        public double EffectiveCapacityKg { get; }
        public string Reason { get; }
    }

    /// <summary>Pure enablement decision; persistence of the decision belongs to Track 2.</summary>
    public static class LiftMigrationPolicy
    {
        public static LiftMigrationDecision Decide(double massKg, double authenticCapacityKg,
            double gravityMagnitude, bool existedBeforeLiftActivation)
        {
            if (!double.IsFinite(massKg) || massKg <= 0.0)
            {
                return new LiftMigrationDecision(LiftMigrationDisposition.Invalid, 0, 0,
                    "invalid mass");
            }
            double capacity = RetailLiftGravityShadow.SanitizeCapacity(authenticCapacityKg);
            double floor = RetailLiftGravityShadow.LegacyCapacityFloorKg(massKg, gravityMagnitude);
            if (floor <= 0.0)
            {
                return new LiftMigrationDecision(LiftMigrationDisposition.Invalid, capacity, capacity,
                    "invalid gravity");
            }
            if (capacity >= floor)
            {
                return new LiftMigrationDecision(LiftMigrationDisposition.Authentic, capacity, capacity,
                    "authentic capacity preserves hover and full climb authority");
            }
            if (existedBeforeLiftActivation)
            {
                return new LiftMigrationDecision(LiftMigrationDisposition.LegacyGrandfatherRequired,
                    capacity, floor, "legacy additive lift floor required; authentic capacity is retained for telemetry");
            }
            return new LiftMigrationDecision(LiftMigrationDisposition.FutureBuildMustBeBlocked,
                capacity, capacity, "new hull exceeds authentic full-climb capacity");
        }
    }

    /// <summary>
    /// Pure production-save audit. Current mounted-part records preserve identity and
    /// prefab but not the core's crafted metal/quality, so capacity is intentionally a
    /// known minimum rather than a fabricated exact value.
    /// </summary>
    public static class ProductionHullLiftAudit
    {
        public const double RecoveredBaseCoreLiftKg = 1000.0;
        public const double RecoveredUpgradeMinimumKg = 400.0;

        public static IReadOnlyList<ProductionHullLiftAuditRow> Audit(
            WorldStateSnapshot snapshot, double gravityMagnitude)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var rows = new List<ProductionHullLiftAuditRow>();
            for (int index = 0; index < snapshot.BuiltShips.Count; index++)
            {
                BuiltShipRecord ship = snapshot.BuiltShips[index];
                if (ship.Salvaged) continue;
                List<MountedPartRecord> parts = snapshot.MountedParts
                    .Where(p => p.BuiltShipIndex == index).ToList();
                if (!ShipPlanModel.TryDecode(ship.HullBytes, out ShipPlanModel? plan, out string? error))
                {
                    rows.Add(ProductionHullLiftAuditRow.Invalid(index, error ?? "invalid hull"));
                    continue;
                }
                ShipHullMetrics metrics = ShipHullMetrics.Measure(plan!);
                double mass = ShipTotalMass.TotalFlightMassKg(
                    HullMassCalculator.HullMassKg(ship.Materials(), metrics), parts.Count);
                int cores = parts.Count(IsCore);
                int recoveredMinimumUpgrades = parts.Count(IsRecoveredMinimumUpgrade);
                // Retail restricted a ship to one core. Never multiply capacity from
                // a corrupt/legacy multi-core record, and never let an orphan module
                // lift a ship after its CoreMain is detached.
                double minimumCapacity = cores > 0
                    ? RecoveredBaseCoreLiftKg
                        + recoveredMinimumUpgrades * RecoveredUpgradeMinimumKg
                    : 0.0;
                LiftMigrationDecision decision = LiftMigrationPolicy.Decide(
                    mass, minimumCapacity, gravityMagnitude, existedBeforeLiftActivation: true);
                rows.Add(new ProductionHullLiftAuditRow(index, true, mass, minimumCapacity,
                    exactCapacityKnown: false, cores, recoveredMinimumUpgrades, parts.Count,
                    decision, "core material/quality is absent from MountedPartRecord; capacity is a recovered lower bound"
                        + (cores > 1 ? "; INVALID MULTI-CORE RECORD requires operator review" : "")));
            }
            return rows;
        }

        private static bool IsCore(MountedPartRecord part) =>
            string.Equals(part.ItemType, "atlasSkyCore", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part.PrefabName, "CoreMain", StringComparison.OrdinalIgnoreCase);

        private static bool IsRecoveredMinimumUpgrade(MountedPartRecord part) =>
            string.Equals(part.ItemType, "skyCoreAtlasEnhancer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part.ItemType, "skyCoreGenerator", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part.PrefabName, "CoreAtlasEnhancer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part.PrefabName, "CoreGenerator", StringComparison.OrdinalIgnoreCase);
    }

    public readonly struct ProductionHullLiftAuditRow
    {
        public ProductionHullLiftAuditRow(int builtShipIndex, bool valid, double massKg,
            double knownMinimumCapacityKg, bool exactCapacityKnown, int coreCount,
            int recoveredMinimumUpgradeCount, int mountedPartCount,
            LiftMigrationDecision migration, string note)
        {
            BuiltShipIndex = builtShipIndex;
            Valid = valid;
            MassKg = massKg;
            KnownMinimumCapacityKg = knownMinimumCapacityKg;
            ExactCapacityKnown = exactCapacityKnown;
            CoreCount = coreCount;
            RecoveredMinimumUpgradeCount = recoveredMinimumUpgradeCount;
            MountedPartCount = mountedPartCount;
            Migration = migration;
            Note = note;
        }
        public int BuiltShipIndex { get; }
        public bool Valid { get; }
        public double MassKg { get; }
        public double KnownMinimumCapacityKg { get; }
        public bool ExactCapacityKnown { get; }
        public int CoreCount { get; }
        public int RecoveredMinimumUpgradeCount { get; }
        public int MountedPartCount { get; }
        public LiftMigrationDecision Migration { get; }
        public string Note { get; }

        public static ProductionHullLiftAuditRow Invalid(int index, string reason) =>
            new ProductionHullLiftAuditRow(index, false, 0, 0, false, 0, 0, 0,
                new LiftMigrationDecision(LiftMigrationDisposition.Invalid, 0, 0, reason), reason);
    }

    /// <summary>One adapter shape for component, admin and Track-3 shadow telemetry.</summary>
    public readonly struct LiftGravityTelemetry
    {
        public LiftGravityTelemetry(double component1257MassKg, double component1258LiftKg,
            double loadRatio, bool overloaded, double appliedLiftNewtons,
            double verticalAccelerationMps2, string capacityProvenance,
            LiftMigrationDisposition migrationDisposition)
        {
            Component1257MassKg = component1257MassKg;
            Component1258LiftKg = component1258LiftKg;
            LoadRatio = loadRatio;
            Overloaded = overloaded;
            AppliedLiftNewtons = appliedLiftNewtons;
            VerticalAccelerationMps2 = verticalAccelerationMps2;
            CapacityProvenance = capacityProvenance;
            MigrationDisposition = migrationDisposition;
        }
        public double Component1257MassKg { get; }
        public double Component1258LiftKg { get; }
        public double LoadRatio { get; }
        public bool Overloaded { get; }
        public double AppliedLiftNewtons { get; }
        public double VerticalAccelerationMps2 { get; }
        public string CapacityProvenance { get; }
        public LiftMigrationDisposition MigrationDisposition { get; }

        public static LiftGravityTelemetry From(LiftGravityEvaluation evaluation,
            LiftMigrationDecision migration, bool exactRecoveredCapacity)
        {
            if (!evaluation.Valid
                || Math.Abs(evaluation.LiftCapacityKg - migration.EffectiveCapacityKg) > 1e-6)
            {
                throw new ArgumentException(
                    "lift evaluation must use the migration decision's effective capacity",
                    nameof(evaluation));
            }
            return new LiftGravityTelemetry(evaluation.MassKg, migration.EffectiveCapacityKg,
                migration.EffectiveCapacityKg > 0.0
                    ? evaluation.MassKg / migration.EffectiveCapacityKg
                    : double.PositiveInfinity,
                evaluation.MassKg > migration.EffectiveCapacityKg,
                evaluation.AppliedLiftForceNewtons,
                evaluation.VerticalAccelerationMps2,
                exactRecoveredCapacity ? "recovered" : "recovered-minimum-or-legacy-floor",
                migration.Disposition);
        }
    }
}
