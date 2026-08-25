using System;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Gravity as a PROVENANCE-LABELLED parameter, never a bare hardcode: the
    /// surviving retail code reads Unity's project-wide Physics.gravity and that
    /// project setting was not recovered, so the value in use must say what it is.
    /// </summary>
    public readonly record struct GravityParameter(double YMetresPerSecondSquared, string Provenance)
    {
        /// <summary>
        /// The stand-in until the retail project setting is recovered: Unity's
        /// engine default of -9.81 m/s2, honestly labelled an approximation.
        /// </summary>
        public static GravityParameter UnityDefaultApproximation { get; } = new GravityParameter(
            -9.81,
            "APPROXIMATION: Unity engine default; the retail project's Physics.gravity is not recovered");

        public double Magnitude => Math.Abs(YMetresPerSecondSquared);

        public bool IsValid => double.IsFinite(YMetresPerSecondSquared)
            && YMetresPerSecondSquared < 0.0
            && !string.IsNullOrWhiteSpace(Provenance);
    }

    /// <summary>What the vertical axis of one hull's step obeys. Pure data.</summary>
    public readonly record struct LiftRuntimeStepPolicy(
        double EffectiveCapacityKg, GravityParameter Gravity, bool IsAbandoned)
    {
        public bool IsValid => double.IsFinite(EffectiveCapacityKg)
            && EffectiveCapacityKg >= 0.0 && Gravity.IsValid;
    }

    /// <summary>
    /// One hull's complete lift-capacity answer: the AUTHENTIC recovered-minimum
    /// capacity (what the mounted cores can defensibly lift) and the EFFECTIVE
    /// capacity the runtime actually enforces, side by side so they can NEVER
    /// silently diverge - a divergence is data in this record, surfaced through
    /// telemetry, not a second formula somewhere else. Both derive from the same
    /// <see cref="ShipMassSnapshot"/> the mass side used.
    /// </summary>
    public readonly record struct LiftCapacityPlan(
        double AuthenticCapacityKg,
        double EffectiveCapacityKg,
        LiftMigrationDisposition Disposition,
        int CoreCount,
        int UpgradeCount,
        bool InvalidMultiCore,
        int MassSnapshotRevision,
        string MassSnapshotFingerprint,
        string CapacityProvenance)
    {
        public bool EffectiveDivergesFromAuthentic =>
            Math.Abs(EffectiveCapacityKg - AuthenticCapacityKg) > 1e-9;
    }

    /// <summary>
    /// The lift/gravity/overload/core-loss runtime: the dormant
    /// <see cref="RetailLiftGravityShadow"/> and <see cref="LiftMigrationPolicy"/>
    /// wired through the reviewed <see cref="IntegratedFlightShadow"/> seam.
    /// Stateless policy - the smoothing/command state lives inside
    /// <see cref="VectorFlightState"/> so every capture/restore/reset carries it.
    ///
    /// GRAVITY EXACTLY ONCE: the seam injects the vector force as the only
    /// external vertical force and rejects any caller-supplied
    /// ExternalVerticalForceNewtons; this type never adds a gravity term of its
    /// own outside <see cref="RetailLiftGravityShadow.Step"/>. Lift acts world-up
    /// at the centre of mass and is torqueless - no retail evidence shows
    /// otherwise, so no torque is invented.
    /// </summary>
    public static class LiftGravityRuntime
    {
        /// <summary>Provenance label for the seed capacity used while the gate is OFF.</summary>
        public const string SeedProvenance =
            "WAREBORN seed (lift is not the limiting factor); lift runtime OFF for this hull";

        /// <summary>Provenance label for the recovered-minimum authentic capacity.</summary>
        public const string RecoveredMinimumProvenance =
            "RECOVERED minimum: base core 1000 kg + 400 kg per known upgrade; core material/quality absent from mount records";

        /// <summary>
        /// The one lift-capacity decision for one hull, from the ONE mass
        /// snapshot. Core rules are the audited retail rules: a ship needs a
        /// main core to lift at all (core detach/loss and orphan upgrade modules
        /// leave zero capacity), a corrupt multi-core record never multiplies
        /// capacity, and upgrades only count while a core is present.
        ///
        /// With the gate OFF for this hull the effective capacity is EXACTLY the
        /// long-standing <see cref="ShipLiftPolicy.SeededTotalLiftKg"/> seed -
        /// byte-identical to today's behavior - while the authentic capacity is
        /// still computed and carried for telemetry.
        /// </summary>
        public static LiftCapacityPlan PlanFor(ShipMassSnapshot snapshot,
            GravityParameter gravity, bool liftRuntimeEnabledForHull,
            bool existedBeforeLiftActivation)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            int cores = 0;
            int upgrades = 0;
            for (int i = 0; i < snapshot.MountedParts.Count; i++)
            {
                // The SAME two evidence fields the production audit consults -
                // itemType-derived and prefab-derived, separately. Feeding the
                // collapsed StablePartKey into both parameters made a core with
                // a non-core itemType but a core prefab count in the audit yet
                // lift zero here.
                MountedPartMassEntry entry = snapshot.MountedParts[i];
                if (ProductionHullLiftAudit.IsCoreIdentity(
                    entry.MaterialEvidence, entry.PrefabEvidence))
                {
                    cores++;
                }
                else if (ProductionHullLiftAudit.IsRecoveredMinimumUpgradeIdentity(
                    entry.MaterialEvidence, entry.PrefabEvidence))
                {
                    upgrades++;
                }
            }

            double authentic = cores > 0
                ? ProductionHullLiftAudit.RecoveredBaseCoreLiftKg
                    + (upgrades * ProductionHullLiftAudit.RecoveredUpgradeMinimumKg)
                : 0.0;

            if (!liftRuntimeEnabledForHull)
            {
                return new LiftCapacityPlan(authentic, ShipLiftPolicy.SeededTotalLiftKg,
                    LiftMigrationDisposition.Authentic, cores, upgrades,
                    cores > 1, snapshot.Revision, snapshot.Fingerprint, SeedProvenance);
            }

            if (cores == 0)
            {
                // Core detach/loss and orphaned upgrade modules: RECOVERED rule -
                // without a main core nothing lifts, and the legacy grandfather
                // floor is a capacity CALIBRATION, never a licence to fly
                // coreless. The hull sinks under gravity until a core returns.
                return new LiftCapacityPlan(0.0, 0.0,
                    LiftMigrationDisposition.Authentic, 0, upgrades,
                    false, snapshot.Revision, snapshot.Fingerprint,
                    "RECOVERED: no main core mounted; zero lift regardless of migration policy");
            }

            LiftMigrationDecision migration = LiftMigrationPolicy.Decide(
                snapshot.TotalFlightMassKg, authentic, gravity.Magnitude,
                existedBeforeLiftActivation);
            return new LiftCapacityPlan(authentic, migration.EffectiveCapacityKg,
                migration.Disposition, cores, upgrades, cores > 1,
                snapshot.Revision, snapshot.Fingerprint,
                RecoveredMinimumProvenance + "; " + migration.Reason);
        }

        /// <summary>
        /// What the 1258 ShipLiftState component serves for this hull. With the
        /// gate OFF this is EXACTLY the historical seed - serving must not change
        /// while the flag is off - and with it ON it is the same effective
        /// capacity the runtime enforces, so client OSD/overload display and
        /// server physics read one truth.
        /// </summary>
        public static double Served1258LiftKg(bool liftRuntimeEnabledForHull,
            LiftCapacityPlan plan) =>
            liftRuntimeEnabledForHull ? plan.EffectiveCapacityKg : ShipLiftPolicy.SeededTotalLiftKg;

        /// <summary>
        /// One hull-vertical + linear integration step THROUGH the reviewed
        /// cross-track seam. The seam enforces: the lift mass equals the force
        /// model's total mass, the caller supplies no external vertical force of
        /// its own (the vector force is injected by the seam exactly once), and
        /// gravity is applied exactly once inside
        /// <see cref="RetailLiftGravityShadow.Step"/>.
        /// </summary>
        public static bool TryIntegrateLinear(string stableHullKey, ShadowMotionState motion,
            VectorRigidBodyShadowResult worldForces, LiftRuntimeStepPolicy policy,
            double verticalCommand, double currentCommandLiftForceNewtons,
            double commandLiftSmoothingVelocity, double deltaSeconds,
            out IntegratedFlightShadowResult result)
        {
            result = default;
            if (!policy.IsValid)
            {
                return false;
            }
            var liftInput = new LiftGravityInput(
                worldForces.Mass.TotalMassKg,
                policy.EffectiveCapacityKg,
                policy.Gravity.YMetresPerSecondSquared,
                motion.VelocityMetresPerSecond.Y,
                verticalCommand,
                deltaSeconds,
                externalVerticalForceNewtons: 0.0,
                compensationForceNewtons: 0.0,
                currentCommandLiftForceNewtons: currentCommandLiftForceNewtons,
                commandLiftSmoothingVelocity: commandLiftSmoothingVelocity,
                isAbandoned: policy.IsAbandoned);
            var input = new IntegratedFlightShadowInput(stableHullKey, motion,
                worldForces, liftInput);
            return IntegratedFlightShadow.TryStep(input, out result);
        }
    }
}
