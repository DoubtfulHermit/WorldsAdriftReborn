using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// One truthful observation of the optional retail world-edge policy.
    /// Pushback components are the velocity changes applied during the evaluated
    /// interval, not forces and not a browser-side estimate.
    /// </summary>
    public readonly struct RetailWorldBoundsTelemetry
    {
        public RetailWorldBoundsTelemetry(bool enabled, double boundaryDistanceMetres,
            double pushbackDeltaVxMps, double pushbackDeltaVyMps, double pushbackDeltaVzMps,
            bool hardClamped, bool invalidState, int referenceSubsteps)
        {
            Enabled = enabled;
            BoundaryDistanceMetres = boundaryDistanceMetres;
            PushbackDeltaVxMps = pushbackDeltaVxMps;
            PushbackDeltaVyMps = pushbackDeltaVyMps;
            PushbackDeltaVzMps = pushbackDeltaVzMps;
            HardClamped = hardClamped;
            InvalidState = invalidState;
            ReferenceSubsteps = referenceSubsteps;
        }

        public bool Enabled { get; }
        public double BoundaryDistanceMetres { get; }
        public double PushbackDeltaVxMps { get; }
        public double PushbackDeltaVyMps { get; }
        public double PushbackDeltaVzMps { get; }
        public bool HardClamped { get; }
        public bool InvalidState { get; }
        public int ReferenceSubsteps { get; }

        public static RetailWorldBoundsTelemetry Off => default;
    }

    public readonly struct RetailWorldBoundsStep
    {
        public RetailWorldBoundsStep(FlightState state, RetailWorldBoundsTelemetry telemetry)
        {
            State = state;
            Telemetry = telemetry;
        }

        public FlightState State { get; }
        public RetailWorldBoundsTelemetry Telemetry { get; }
    }

    /// <summary>
    /// Pure, deterministic reconstruction of retail's <c>WorldEdgePushback</c>.
    ///
    /// Recovered constants (not tuning): horizontal hard inset 300 m, push band
    /// 100 m, damping begins one quarter into the band, maximum per-FixedUpdate
    /// inward velocity change 50 m/s, vertical pushback at Y=800 and hard ceiling
    /// Y=1000. Source: preserved client
    /// <c>WAReborn-decompiled/acs/WorldEdgePushback.cs</c>.
    ///
    /// Deployment configuration: edge length. The default 36,000 m is not a
    /// guessed physics constant; it is the preserved release MapFile's authored
    /// <c>WorldInfo.WorldEdgeLength</c> in
    /// <c>docs/research/world-data/wamap-islands.json</c> (extent +/-18,000 m).
    /// A different world may override it with WAREBORN_FLIGHT_WORLD_EDGE_LENGTH.
    ///
    /// The legacy server advances flight at 0.24 s/control point rather than at
    /// Unity FixedUpdate cadence. Callers therefore evaluate flight and this
    /// policy in exact 0.02 s reference substeps while the feature is enabled.
    /// This preserves the recovered per-FixedUpdate velocity-change semantics
    /// without pretending that the whole flight integrator is a 50 Hz rigidbody.
    /// </summary>
    public sealed class RetailWorldBoundsPolicy
    {
        public const double ReferenceStepSeconds = 0.02;
        public const double ReleaseWorldEdgeLengthMetres = 36_000.0;
        public const double HorizontalHardInsetMetres = 300.0;
        public const double HorizontalPushbackBandMetres = 100.0;
        public const double VerticalPushbackMetres = 800.0;
        public const double VerticalHardLimitMetres = 1_000.0;
        public const double MaximumPushbackDeltaMpsPerReferenceStep = 50.0;

        public RetailWorldBoundsPolicy(bool enabled,
            double edgeLengthMetres = ReleaseWorldEdgeLengthMetres)
        {
            Enabled = enabled;
            EdgeLengthMetres = IsValidEdgeLength(edgeLengthMetres)
                ? edgeLengthMetres
                : ReleaseWorldEdgeLengthMetres;
        }

        public bool Enabled { get; }
        public double EdgeLengthMetres { get; }
        public double HorizontalHardLimitMetres =>
            (EdgeLengthMetres * 0.5) - HorizontalHardInsetMetres;
        public double HorizontalPushbackThresholdMetres =>
            HorizontalHardLimitMetres - HorizontalPushbackBandMetres;

        public static RetailWorldBoundsPolicy FromEnvironment(Func<string, string?> getenv)
        {
            if (getenv == null) throw new ArgumentNullException(nameof(getenv));
            bool enabled = getenv("WAREBORN_FLIGHT_WORLD_BOUNDS") == "1";
            double edge = ReleaseWorldEdgeLengthMetres;
            string? raw = getenv("WAREBORN_FLIGHT_WORLD_EDGE_LENGTH");
            if (raw != null
                && double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed)
                && IsValidEdgeLength(parsed))
            {
                edge = parsed;
            }
            return new RetailWorldBoundsPolicy(enabled, edge);
        }

        /// <summary>
        /// Applies one recovered 50 Hz edge evaluation. <paramref name="previousFinite"/>
        /// is the quarantine anchor: a non-finite candidate is rejected and becomes
        /// an at-rest copy of that last finite pose. If both are corrupt, origin is
        /// the only safe deterministic anchor.
        /// </summary>
        public RetailWorldBoundsStep Apply(FlightState previousFinite, FlightState candidate)
        {
            if (!Enabled)
                return new RetailWorldBoundsStep(candidate, RetailWorldBoundsTelemetry.Off);

            if (!IsFinite(candidate))
            {
                FlightState anchor = IsFinite(previousFinite)
                    ? previousFinite
                    : FlightState.AtRestAt(0, 0, 0);
                double safeX = Math.Clamp(anchor.X,
                    -HorizontalHardLimitMetres, HorizontalHardLimitMetres);
                double safeY = Math.Min(anchor.Y, VerticalHardLimitMetres);
                double safeZ = Math.Clamp(anchor.Z,
                    -HorizontalHardLimitMetres, HorizontalHardLimitMetres);
                bool anchorClamped = safeX != anchor.X || safeY != anchor.Y || safeZ != anchor.Z;
                FlightState quarantined = FlightState.AtRestAt(
                    safeX, safeY, safeZ,
                    double.IsFinite(anchor.YawRadians) ? anchor.YawRadians : 0.0);
                return new RetailWorldBoundsStep(quarantined,
                    new RetailWorldBoundsTelemetry(true, DistanceToBoundary(quarantined),
                        0, 0, 0, anchorClamped, true, 1));
            }

            double x = candidate.X, y = candidate.Y, z = candidate.Z;
            double vx = candidate.VxMps, vy = candidate.VyMps, vz = candidate.VzMps;
            bool hard = false;
            double beforeVx = vx, beforeVy = vy, beforeVz = vz;

            EnforcePositive(ref y, ref vy, VerticalPushbackMetres,
                VerticalHardLimitMetres, ref hard);
            double push = HorizontalPushbackThresholdMetres;
            double limit = HorizontalHardLimitMetres;
            EnforcePositive(ref x, ref vx, push, limit, ref hard);
            EnforceNegative(ref x, ref vx, -push, -limit, ref hard);
            EnforcePositive(ref z, ref vz, push, limit, ref hard);
            EnforceNegative(ref z, ref vz, -push, -limit, ref hard);

            var bounded = new FlightState(
                x, y, z, candidate.YawRadians, candidate.YawRateRadPerSec,
                candidate.RollRadians, candidate.PitchRadians, candidate.SpeedCmdMps,
                vx, vy, vz);
            return new RetailWorldBoundsStep(bounded,
                new RetailWorldBoundsTelemetry(true, DistanceToBoundary(bounded),
                    vx - beforeVx, vy - beforeVy, vz - beforeVz,
                    hard, false, 1));
        }

        public double DistanceToBoundary(FlightState state)
        {
            if (!Enabled || !IsFinite(state)) return 0.0;
            return Math.Min(
                VerticalHardLimitMetres - state.Y,
                Math.Min(HorizontalHardLimitMetres - Math.Abs(state.X),
                    HorizontalHardLimitMetres - Math.Abs(state.Z)));
        }

        private static void EnforcePositive(ref double position, ref double velocity,
            double pushThreshold, double hardLimit, ref bool hardClamped)
        {
            if (position <= pushThreshold) return;
            if (position > hardLimit)
            {
                position = hardLimit;
                hardClamped = true;
            }
            double t = Clamp01((position - pushThreshold) / (hardLimit - pushThreshold));
            Dampen(ref velocity, t);
            velocity -= MaximumPushbackDeltaMpsPerReferenceStep * t * t;
        }

        private static void EnforceNegative(ref double position, ref double velocity,
            double pushThreshold, double hardLimit, ref bool hardClamped)
        {
            if (position >= pushThreshold) return;
            if (position < hardLimit)
            {
                position = hardLimit;
                hardClamped = true;
            }
            double t = Clamp01((position - pushThreshold) / (hardLimit - pushThreshold));
            Dampen(ref velocity, t);
            velocity += MaximumPushbackDeltaMpsPerReferenceStep * t * t;
        }

        private static void Dampen(ref double velocity, double t)
        {
            if (t > 0.25)
                velocity *= 1.0 - ((t - 0.25) / 0.75);
        }

        private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

        private static bool IsValidEdgeLength(double value) =>
            double.IsFinite(value)
            && value > 2.0 * (HorizontalHardInsetMetres + HorizontalPushbackBandMetres);

        public static bool IsFinite(FlightState state) =>
            double.IsFinite(state.X) && double.IsFinite(state.Y) && double.IsFinite(state.Z)
            && double.IsFinite(state.YawRadians) && double.IsFinite(state.YawRateRadPerSec)
            && double.IsFinite(state.RollRadians) && double.IsFinite(state.PitchRadians)
            && double.IsFinite(state.SpeedCmdMps)
            && double.IsFinite(state.VxMps) && double.IsFinite(state.VyMps)
            && double.IsFinite(state.VzMps);
    }
}
