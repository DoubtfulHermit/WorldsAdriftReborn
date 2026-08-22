using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Safety envelope for the observation-only wall/storm evaluator. None of
    /// these values are retail balance: they bound untrusted/corrupt inputs and
    /// operator-authored WAREBORN tuning before the values reach vector maths.
    /// </summary>
    public static class VectorWallStormShadowLimits
    {
        public const int MaxWallSegments = 128;
        public const int MaxGustPulses = 32;
        public const int MaxDamageTargets = 256;
        public const double MaxWallWindMetresPerSecond = 100.0;
        public const double MaxGustForceNewtons = 1_000_000.0;
        public const double MaxYawTorqueNewtonMetres = 10_000_000.0;
        public const double MaxShipMassKg = 1_000_000.0;
        public const double MaxShipSpeedMetresPerSecond = 250.0;
        public const double MaxWorldCoordinateMetres = 1_000_000.0;
        public const double MinStepSeconds = 0.001;
        public const double MaxStepSeconds = 0.1;
        public const double MaxDamageFractionPerIntent = 0.25;
        public const int MinDamageIntervalTicks = 10;
        public const int MaxIdLength = 128;

        // RECOVERED compile-time WallData constants.
        public const double FullStrengthDistanceMetres = 200.0;
        public const double PhysicsDistanceMetres = 400.0;
        public const double LightningDistanceMetres = 300.0;
        public const double VisualDistanceMetres = 800.0;
        public const double GustDurationSeconds = 0.5;
        public const double MassAttenuationSaturationKg = 4000.0;
    }

    public enum VectorWallType
    {
        WindRift = 0,
        StormRift = 1,
        Typhon = 2,
        SandStorm = 3,
        IceStorm = 4,
        WorldEndWall = 5,
    }

    /// <summary>One whole authored wall, using the same XZ line geometry as 1204.</summary>
    public readonly record struct VectorWallSegment(
        int WallId,
        VectorWallType Type,
        ShadowVector3 First,
        ShadowVector3 Second)
    {
        public bool IsValid => WallId >= 0
            && Enum.IsDefined(typeof(VectorWallType), Type)
            && First.IsFinite && Second.IsFinite
            && MaxAbs(First) <= VectorWallStormShadowLimits.MaxWorldCoordinateMetres
            && MaxAbs(Second) <= VectorWallStormShadowLimits.MaxWorldCoordinateMetres
            && double.IsFinite(HorizontalLengthSquared)
            && HorizontalLengthSquared > VectorRigidBodyShadowPolicy.VectorEpsilon;

        private static double MaxAbs(ShadowVector3 value) =>
            Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));

        public double HorizontalLengthSquared
        {
            get
            {
                double dx = Second.X - First.X;
                double dz = Second.Z - First.Z;
                return dx * dx + dz * dz;
            }
        }

        public ShadowVector3 Forward
        {
            get
            {
                double length = Math.Sqrt(HorizontalLengthSquared);
                return length > 0.0
                    ? new ShadowVector3((Second.X - First.X) / length, 0.0, (Second.Z - First.Z) / length)
                    : ShadowVector3.Zero;
            }
        }

        public ShadowVector3 ClosestPoint(ShadowVector3 point)
        {
            double dx = Second.X - First.X;
            double dz = Second.Z - First.Z;
            double t = ((point.X - First.X) * dx + (point.Z - First.Z) * dz)
                / HorizontalLengthSquared;
            t = Math.Clamp(t, 0.0, 1.0);
            return new ShadowVector3(First.X + t * dx, point.Y, First.Z + t * dz);
        }

        public double HorizontalDistance(ShadowVector3 point)
        {
            ShadowVector3 closest = ClosestPoint(point);
            double x = point.X - closest.X;
            double z = point.Z - closest.Z;
            return Math.Sqrt(x * x + z * z);
        }
    }

    /// <summary>
    /// Per-type operator values. Wind/gust/torque/damage magnitudes are absent
    /// from the surviving retail client and therefore default to zero/off.
    /// </summary>
    public readonly record struct VectorWallTypeTuning(
        bool MechanicsEnabled,
        double HorizontalWindMetresPerSecond,
        double VerticalWindMetresPerSecond,
        double SmallGustForceNewtons,
        double BigGustForceNewtons,
        double YawTorqueNewtonMetres,
        double TorqueDampeningDotStart,
        double TorqueDampeningDotEnd,
        bool DamageIntentsEnabled,
        int DamageIntervalTicks,
        double DamageFractionPerIntent)
    {
        public static VectorWallTypeTuning Disabled => new(
            false, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, false,
            VectorWallStormShadowLimits.MinDamageIntervalTicks, 0.0);

        public bool IsValid =>
            double.IsFinite(HorizontalWindMetresPerSecond)
            && Math.Abs(HorizontalWindMetresPerSecond) <= VectorWallStormShadowLimits.MaxWallWindMetresPerSecond
            && double.IsFinite(VerticalWindMetresPerSecond)
            && Math.Abs(VerticalWindMetresPerSecond) <= VectorWallStormShadowLimits.MaxWallWindMetresPerSecond
            && double.IsFinite(SmallGustForceNewtons) && SmallGustForceNewtons >= 0.0
            && SmallGustForceNewtons <= VectorWallStormShadowLimits.MaxGustForceNewtons
            && double.IsFinite(BigGustForceNewtons) && BigGustForceNewtons >= 0.0
            && BigGustForceNewtons <= VectorWallStormShadowLimits.MaxGustForceNewtons
            && double.IsFinite(YawTorqueNewtonMetres) && YawTorqueNewtonMetres >= 0.0
            && YawTorqueNewtonMetres <= VectorWallStormShadowLimits.MaxYawTorqueNewtonMetres
            && double.IsFinite(TorqueDampeningDotStart) && TorqueDampeningDotStart >= -1.0
            && TorqueDampeningDotStart <= 1.0
            && double.IsFinite(TorqueDampeningDotEnd)
            && TorqueDampeningDotEnd >= TorqueDampeningDotStart
            && TorqueDampeningDotEnd <= 1.0
            && DamageIntervalTicks >= VectorWallStormShadowLimits.MinDamageIntervalTicks
            && double.IsFinite(DamageFractionPerIntent) && DamageFractionPerIntent >= 0.0
            && DamageFractionPerIntent <= VectorWallStormShadowLimits.MaxDamageFractionPerIntent;
    }

    public sealed class VectorWallStormTuning
    {
        private readonly IReadOnlyDictionary<VectorWallType, VectorWallTypeTuning> _types;

        public VectorWallStormTuning(IReadOnlyDictionary<VectorWallType, VectorWallTypeTuning>? types = null,
            double dragExponent = 2.5, double dragCoefficient = 0.007)
        {
            _types = types == null
                ? new Dictionary<VectorWallType, VectorWallTypeTuning>()
                : new Dictionary<VectorWallType, VectorWallTypeTuning>(types);
            DragExponent = dragExponent;
            DragCoefficient = dragCoefficient;
        }

        // RECOVERED from the shipped ShipConfig asset; still injectable because
        // retail could remotely override shipconfig at runtime.
        public double DragExponent { get; }
        public double DragCoefficient { get; }

        public bool IsValid => double.IsFinite(DragExponent) && DragExponent >= 1.0 && DragExponent <= 4.0
            && double.IsFinite(DragCoefficient) && DragCoefficient >= 0.0 && DragCoefficient <= 1.0
            && _types.Count <= Enum.GetValues<VectorWallType>().Length
            && _types.All(pair => Enum.IsDefined(typeof(VectorWallType), pair.Key) && pair.Value.IsValid);

        public VectorWallTypeTuning For(VectorWallType type) =>
            _types.TryGetValue(type, out VectorWallTypeTuning value)
                ? value
                : VectorWallTypeTuning.Disabled;
    }

    public enum VectorWallGustSize
    {
        Small,
        Big,
    }

    /// <summary>
    /// A server-clock-authored pulse. Track 2 must own/snapshot scheduling; this
    /// policy only reproduces retail's 0.5 s triangular envelope and direction.
    /// </summary>
    public readonly record struct VectorWallGustPulse(
        int WallId,
        VectorWallGustSize Size,
        long StartTick,
        ShadowVector3 HullLocalApplicationPoint,
        ulong DirectionSeed);

    public enum VectorWallDamageTargetKind
    {
        Sail,
        Wing,
        Engine,
        HullPart,
    }

    /// <summary>Only server-owned, validated target identities belong here.</summary>
    public readonly record struct VectorWallDamageTarget(
        string EntityId,
        VectorWallDamageTargetKind Kind,
        bool LightningStrikable = true)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(EntityId)
            && EntityId.Length <= VectorWallStormShadowLimits.MaxIdLength
            && Enum.IsDefined(typeof(VectorWallDamageTargetKind), Kind);
    }

    public enum VectorWallDamageIntentKind
    {
        WindRiftSailExposure,
        SandStormPartExposure,
        StormRiftLightning,
    }

    /// <summary>
    /// Idempotent suggestion only. It cannot mutate health, detach a part or send
    /// a component update; an eventual authoritative damage service must dedupe
    /// IntentId and apply its own audited health policy.
    /// </summary>
    public readonly record struct VectorWallDamageIntent(
        string IntentId,
        VectorWallDamageIntentKind Kind,
        int WallId,
        string ShipId,
        string TargetEntityId,
        long FixedTick,
        double ExposureIntensity,
        double SuggestedDamageFraction);

    public readonly record struct VectorWallStormInput(
        string ShipId,
        ShadowVector3 WorldPosition,
        ShadowVector3 WorldVelocity,
        ShadowVector3 WorldForward,
        ShadowVector3 AmbientWindWorld,
        double MassKg,
        ShadowVector3 CentreOfMassLocal,
        long FixedTick,
        double FixedStepSeconds,
        ShadowVector3 CurrentScalarWallForceLocal)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(ShipId)
            && ShipId.Length <= VectorWallStormShadowLimits.MaxIdLength
            && WorldPosition.IsFinite && WorldVelocity.IsFinite && WorldForward.IsFinite
            && AmbientWindWorld.IsFinite && CentreOfMassLocal.IsFinite
            && CurrentScalarWallForceLocal.IsFinite
            && WorldVelocity.Magnitude <= VectorWallStormShadowLimits.MaxShipSpeedMetresPerSecond
            && MaxAbs(WorldPosition) <= VectorWallStormShadowLimits.MaxWorldCoordinateMetres
            && double.IsFinite(WorldForward.Magnitude)
            && WorldForward.Magnitude <= VectorWallStormShadowLimits.MaxWorldCoordinateMetres
            && CurrentScalarWallForceLocal.Magnitude <= VectorRigidBodyShadowPolicy.MaxForceNewtons
            && MassKg > 0.0 && double.IsFinite(MassKg)
            && MassKg <= VectorWallStormShadowLimits.MaxShipMassKg
            && FixedTick >= 0 && double.IsFinite(FixedStepSeconds)
            && FixedStepSeconds >= VectorWallStormShadowLimits.MinStepSeconds
            && FixedStepSeconds <= VectorWallStormShadowLimits.MaxStepSeconds
            && Flatten(WorldForward).Magnitude > VectorRigidBodyShadowPolicy.VectorEpsilon;

        private static ShadowVector3 Flatten(ShadowVector3 value) =>
            new(value.X, 0.0, value.Z);

        private static double MaxAbs(ShadowVector3 value) =>
            Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z)));
    }

    public readonly record struct VectorWallInfluenceSample(
        int WallId,
        VectorWallType Type,
        double DistanceMetres,
        double PhysicsIntensity,
        double VisualIntensity,
        bool LightningEligible,
        bool SelectedForDrag,
        bool SelectedForTypeEffects);

    public readonly record struct VectorWallScalarComparison(
        ShadowVector3 ScalarWallForceLocal,
        ShadowVector3 ShadowWallForceLocal)
    {
        public ShadowVector3 DeltaLocal => ShadowWallForceLocal - ScalarWallForceLocal;
        public double ScalarMagnitude => ScalarWallForceLocal.Magnitude;
        public double ShadowMagnitude => ShadowWallForceLocal.Magnitude;
    }

    public sealed class VectorWallStormShadowResult
    {
        internal VectorWallStormShadowResult(
            ShadowVector3 wallDragForceLocal,
            ShadowVector3 gustForceLocal,
            ShadowVector3 gustTorqueLocal,
            ShadowVector3 alignmentTorqueLocal,
            IReadOnlyList<VectorWallInfluenceSample> samples,
            IReadOnlyList<VectorWallDamageIntent> intents,
            VectorWallScalarComparison comparison,
            int rejectedSegments,
            int rejectedPulses,
            int rejectedTargets)
        {
            WallDragForceLocal = wallDragForceLocal;
            GustForceLocal = gustForceLocal;
            GustTorqueLocal = gustTorqueLocal;
            AlignmentTorqueLocal = alignmentTorqueLocal;
            Samples = samples;
            DamageIntents = intents;
            Comparison = comparison;
            RejectedSegments = rejectedSegments;
            RejectedPulses = rejectedPulses;
            RejectedTargets = rejectedTargets;
        }

        public ShadowVector3 WallDragForceLocal { get; }
        public ShadowVector3 GustForceLocal { get; }
        public ShadowVector3 TotalForceLocal => WallDragForceLocal + GustForceLocal;
        public ShadowVector3 GustTorqueLocal { get; }
        public ShadowVector3 AlignmentTorqueLocal { get; }
        public ShadowVector3 TotalTorqueLocal => GustTorqueLocal + AlignmentTorqueLocal;
        public IReadOnlyList<VectorWallInfluenceSample> Samples { get; }
        public IReadOnlyList<VectorWallDamageIntent> DamageIntents { get; }
        public VectorWallScalarComparison Comparison { get; }
        public int RejectedSegments { get; }
        public int RejectedPulses { get; }
        public int RejectedTargets { get; }
    }

    /// <summary>
    /// Engine-free, side-effect-free vector wall/storm evaluator. It reads no
    /// environment, wall clock, network component or live FlightSession.
    /// </summary>
    public static class VectorWallStormShadow
    {
        public static bool TryEvaluate(
            VectorWallStormInput input,
            IReadOnlyList<VectorWallSegment> segments,
            VectorWallStormTuning tuning,
            IReadOnlyList<VectorWallGustPulse>? gustPulses,
            IReadOnlyList<VectorWallDamageTarget>? damageTargets,
            out VectorWallStormShadowResult result)
        {
            result = null!;
            if (!input.IsValid || segments == null || tuning == null || !tuning.IsValid
                || segments.Count > VectorWallStormShadowLimits.MaxWallSegments
                || (gustPulses?.Count ?? 0) > VectorWallStormShadowLimits.MaxGustPulses
                || (damageTargets?.Count ?? 0) > VectorWallStormShadowLimits.MaxDamageTargets)
            {
                return false;
            }

            ShadowVector3 forward = Flatten(input.WorldForward).NormalizedOrZero();
            ShadowVector3 right = new(forward.Z, 0.0, -forward.X);
            var accepted = new List<(VectorWallSegment Segment, double Distance, double Intensity, double Visual)>();
            int rejectedSegments = 0;
            var seenWallIds = new HashSet<int>();
            for (int i = 0; i < segments.Count; i++)
            {
                VectorWallSegment segment = segments[i];
                if (!segment.IsValid || !seenWallIds.Add(segment.WallId))
                {
                    rejectedSegments++;
                    continue;
                }
                double distance = segment.HorizontalDistance(input.WorldPosition);
                double intensity = PhysicsIntensity(distance);
                double visual = VisualIntensity(distance);
                if (intensity > 0.0 || visual > 0.0)
                {
                    accepted.Add((segment, distance, intensity, visual));
                }
            }

            (VectorWallSegment Segment, double Distance, double Intensity, double Visual)? nearestDrag = accepted
                .Where(item => item.Intensity > 0.0 && tuning.For(item.Segment.Type).MechanicsEnabled)
                .OrderBy(item => item.Distance).ThenBy(item => item.Segment.WallId).FirstOrDefaultNullable();
            Dictionary<VectorWallType, (VectorWallSegment Segment, double Distance, double Intensity, double Visual)> nearestByType = accepted
                .Where(item => item.Intensity > 0.0)
                .GroupBy(item => item.Segment.Type)
                .ToDictionary(group => group.Key,
                    group => group.OrderBy(item => item.Distance).ThenBy(item => item.Segment.WallId).First());

            ShadowVector3 wallDragLocal = ShadowVector3.Zero;
            if (nearestDrag.HasValue)
            {
                var selected = nearestDrag.Value;
                VectorWallTypeTuning wallTuning = tuning.For(selected.Segment.Type);
                ShadowVector3 authoredWind = WallWindWorld(selected.Segment, input.WorldPosition, wallTuning);
                ShadowVector3 mixedWind = Lerp(input.AmbientWindWorld, authoredWind, selected.Intensity);
                double attenuation = MassAttenuation(input.MassKg);
                ShadowVector3 fullDrag = WindDragWorld(mixedWind, input.WorldVelocity, input.MassKg,
                    input.FixedStepSeconds, attenuation, tuning);
                ShadowVector3 baseline = WindDragWorld(input.AmbientWindWorld, input.WorldVelocity, input.MassKg,
                    input.FixedStepSeconds, attenuation, tuning);
                wallDragLocal = WorldToHull(fullDrag - baseline, right, forward);
            }

            var gustAccumulator = new ShadowForceAccumulator();
            int rejectedPulses = 0;
            if (gustPulses != null)
            {
                for (int i = 0; i < gustPulses.Count; i++)
                {
                    VectorWallGustPulse pulse = gustPulses[i];
                    if (!nearestByType.Values.Any(item => item.Segment.WallId == pulse.WallId)
                        || pulse.StartTick < 0 || !pulse.HullLocalApplicationPoint.IsFinite
                        || pulse.HullLocalApplicationPoint.Magnitude > VectorRigidBodyShadowPolicy.MaxMountOffsetMetres)
                    {
                        rejectedPulses++;
                        continue;
                    }
                    var wall = nearestByType.Values.First(item => item.Segment.WallId == pulse.WallId);
                    VectorWallTypeTuning wallTuning = tuning.For(wall.Segment.Type);
                    if (!wallTuning.MechanicsEnabled)
                    {
                        rejectedPulses++;
                        continue;
                    }
                    double envelope = GustEnvelope(input.FixedTick, pulse.StartTick, input.FixedStepSeconds);
                    ShadowVector3 directionWorld = GustDirection(wall.Segment.Type, pulse.DirectionSeed);
                    double strength = pulse.Size == VectorWallGustSize.Big
                        ? wallTuning.BigGustForceNewtons
                        : wallTuning.SmallGustForceNewtons;
                    ShadowVector3 forceLocal = WorldToHull(directionWorld
                        * (strength * wall.Intensity * envelope), right, forward);
                    if (!gustAccumulator.TryAdd(forceLocal, pulse.HullLocalApplicationPoint,
                            input.CentreOfMassLocal, torqueless: false))
                    {
                        rejectedPulses++;
                    }
                }
            }

            ShadowVector3 alignmentTorque = ShadowVector3.Zero;
            foreach (var wall in nearestByType.Values.OrderBy(value => value.Segment.Type))
            {
                VectorWallTypeTuning wallTuning = tuning.For(wall.Segment.Type);
                if (!wallTuning.MechanicsEnabled
                    || wall.Segment.Type == VectorWallType.WindRift
                    || wall.Segment.Type == VectorWallType.Typhon
                    || wall.Segment.Type == VectorWallType.IceStorm
                    || wallTuning.YawTorqueNewtonMetres <= 0.0)
                {
                    continue;
                }
                double dot = ShadowVector3.Dot(wall.Segment.Forward, forward);
                double dotFactor = InverseAlignment(dot, wallTuning.TorqueDampeningDotStart,
                    wallTuning.TorqueDampeningDotEnd);
                ShadowVector3 wallRight = new(wall.Segment.Forward.Z, 0.0, -wall.Segment.Forward.X);
                bool flip = ShadowVector3.Dot(forward, -wallRight) < 0.0;
                double torque = wallTuning.YawTorqueNewtonMetres * wall.Intensity * dotFactor;
                alignmentTorque += new ShadowVector3(0.0, flip ? -torque : torque, 0.0);
            }

            List<VectorWallDamageTarget> validTargets = (damageTargets ?? Array.Empty<VectorWallDamageTarget>())
                .Where(target => target.IsValid)
                .GroupBy(target => target.EntityId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.EntityId, StringComparer.Ordinal).ToList();
            int rejectedTargets = (damageTargets?.Count ?? 0) - validTargets.Count;
            var intents = new List<VectorWallDamageIntent>();
            foreach (var wall in nearestByType.Values.OrderBy(value => value.Segment.Type))
            {
                AddDamageIntent(input, wall.Segment, wall.Distance, wall.Intensity,
                    tuning.For(wall.Segment.Type), validTargets, intents);
            }

            var samples = accepted
                .OrderBy(item => item.Segment.WallId)
                .Select(item => new VectorWallInfluenceSample(
                    item.Segment.WallId, item.Segment.Type, item.Distance, item.Intensity, item.Visual,
                    item.Segment.Type == VectorWallType.StormRift
                        && item.Distance <= VectorWallStormShadowLimits.LightningDistanceMetres,
                    nearestDrag.HasValue && nearestDrag.Value.Segment.WallId == item.Segment.WallId,
                    nearestByType.TryGetValue(item.Segment.Type, out var nearest)
                        && nearest.Segment.WallId == item.Segment.WallId))
                .ToArray();

            ShadowVector3 shadowForce = wallDragLocal + gustAccumulator.ForceNewtons;
            result = new VectorWallStormShadowResult(wallDragLocal, gustAccumulator.ForceNewtons,
                gustAccumulator.RawTorqueNewtonMetres, alignmentTorque, samples, intents,
                new VectorWallScalarComparison(input.CurrentScalarWallForceLocal, shadowForce),
                rejectedSegments, rejectedPulses, rejectedTargets);
            return true;
        }

        public static double PhysicsIntensity(double distanceMetres)
        {
            if (!double.IsFinite(distanceMetres) || distanceMetres > VectorWallStormShadowLimits.PhysicsDistanceMetres)
            {
                return 0.0;
            }
            if (distanceMetres < VectorWallStormShadowLimits.FullStrengthDistanceMetres)
            {
                return 1.0;
            }
            return 1.0 - (distanceMetres - VectorWallStormShadowLimits.FullStrengthDistanceMetres)
                / (VectorWallStormShadowLimits.PhysicsDistanceMetres
                    - VectorWallStormShadowLimits.FullStrengthDistanceMetres);
        }

        public static double VisualIntensity(double distanceMetres)
        {
            if (!double.IsFinite(distanceMetres) || distanceMetres >= VectorWallStormShadowLimits.VisualDistanceMetres)
            {
                return 0.0;
            }
            double ratio = distanceMetres / VectorWallStormShadowLimits.VisualDistanceMetres;
            return 0.95 * Math.Clamp(1.0 - ratio * ratio, 0.0, 1.0);
        }

        public static double MassAttenuation(double massKg)
        {
            if (!double.IsFinite(massKg) || massKg < 0.0)
            {
                return 0.0;
            }
            return 1.0 - Math.Clamp(massKg / VectorWallStormShadowLimits.MassAttenuationSaturationKg,
                0.0, 1.0) * 0.75;
        }

        public static double GustEnvelope(long tick, long startTick, double stepSeconds)
        {
            if (tick < startTick || !double.IsFinite(stepSeconds) || stepSeconds <= 0.0)
            {
                return 0.0;
            }
            double elapsed = (tick - startTick) * stepSeconds;
            if (elapsed >= VectorWallStormShadowLimits.GustDurationSeconds)
            {
                return 0.0;
            }
            double progress = elapsed / VectorWallStormShadowLimits.GustDurationSeconds;
            return 1.0 - Math.Abs(progress * 2.0 - 1.0);
        }

        public static ShadowVector3 GustDirection(VectorWallType type, ulong seed)
        {
            if (type == VectorWallType.WindRift)
            {
                return -ShadowVector3.Up;
            }
            if (type != VectorWallType.StormRift && type != VectorWallType.SandStorm)
            {
                return ShadowVector3.Zero;
            }
            // Deterministic replacement for UnityEngine.Random.Range. Direction is
            // recovered (random horizontal); the PRNG sequence is not.
            ulong mixed = Mix(seed);
            double angle = (mixed / (double)ulong.MaxValue) * Math.PI * 2.0;
            return new ShadowVector3(Math.Cos(angle), 0.0, Math.Sin(angle));
        }

        private static void AddDamageIntent(VectorWallStormInput input,
            VectorWallSegment segment, double distance, double intensity,
            VectorWallTypeTuning tuning, IReadOnlyList<VectorWallDamageTarget> targets,
            List<VectorWallDamageIntent> intents)
        {
            if (!tuning.DamageIntentsEnabled || tuning.DamageFractionPerIntent <= 0.0
                || intensity <= 0.0 || targets.Count == 0)
            {
                return;
            }
            VectorWallDamageIntentKind kind;
            IEnumerable<VectorWallDamageTarget> eligible;
            if (segment.Type == VectorWallType.WindRift)
            {
                kind = VectorWallDamageIntentKind.WindRiftSailExposure;
                eligible = targets.Where(target => target.Kind == VectorWallDamageTargetKind.Sail);
            }
            else if (segment.Type == VectorWallType.SandStorm)
            {
                kind = VectorWallDamageIntentKind.SandStormPartExposure;
                eligible = targets.Where(target => target.Kind is VectorWallDamageTargetKind.Wing
                    or VectorWallDamageTargetKind.Engine);
            }
            else if (segment.Type == VectorWallType.StormRift
                && distance <= VectorWallStormShadowLimits.LightningDistanceMetres)
            {
                kind = VectorWallDamageIntentKind.StormRiftLightning;
                eligible = targets.Where(target => target.LightningStrikable);
            }
            else
            {
                return;
            }

            long bucket = input.FixedTick / tuning.DamageIntervalTicks;
            ulong identity = StableHash(input.ShipId, segment.WallId, (int)kind, bucket);
            if (input.FixedTick % tuning.DamageIntervalTicks
                != (long)(identity % (ulong)tuning.DamageIntervalTicks))
            {
                return;
            }
            VectorWallDamageTarget[] candidates = eligible.ToArray();
            if (candidates.Length == 0)
            {
                return;
            }
            VectorWallDamageTarget target = candidates[(int)(Mix(identity) % (ulong)candidates.Length)];
            string intentId = $"wall:{segment.WallId}:{(int)kind}:{input.ShipId}:{bucket}:{target.EntityId}";
            intents.Add(new VectorWallDamageIntent(intentId, kind, segment.WallId, input.ShipId,
                target.EntityId, input.FixedTick, intensity, tuning.DamageFractionPerIntent));
        }

        private static ShadowVector3 WallWindWorld(VectorWallSegment segment,
            ShadowVector3 point, VectorWallTypeTuning tuning)
        {
            if (segment.Type == VectorWallType.WindRift)
            {
                ShadowVector3 away = Flatten(point - segment.ClosestPoint(point)).NormalizedOrZero();
                return away * tuning.HorizontalWindMetresPerSecond
                    + ShadowVector3.Up * tuning.VerticalWindMetresPerSecond;
            }
            if (segment.Type == VectorWallType.IceStorm)
            {
                return -ShadowVector3.Up;
            }
            if (segment.Type == VectorWallType.Typhon)
            {
                return segment.Forward;
            }
            return segment.Forward * tuning.HorizontalWindMetresPerSecond;
        }

        private static ShadowVector3 WindDragWorld(ShadowVector3 wind, ShadowVector3 velocity,
            double mass, double dt, double attenuation, VectorWallStormTuning tuning)
        {
            ShadowVector3 relative = wind * attenuation - velocity;
            double magnitude = relative.Magnitude;
            if (magnitude <= VectorRigidBodyShadowPolicy.VectorEpsilon)
            {
                return ShadowVector3.Zero;
            }
            double acceleration = Math.Pow(magnitude, tuning.DragExponent) * tuning.DragCoefficient;
            acceleration = Math.Clamp(acceleration, 0.0, magnitude / dt);
            double force = Math.Min(mass * acceleration, VectorRigidBodyShadowPolicy.MaxForceNewtons);
            return relative / magnitude * force;
        }

        private static double InverseAlignment(double dot, double start, double end)
        {
            if (dot <= start) return 1.0;
            if (dot >= end) return 0.0;
            return 1.0 - (dot - start) / (end - start);
        }

        private static ShadowVector3 WorldToHull(ShadowVector3 world,
            ShadowVector3 right, ShadowVector3 forward) =>
            new(ShadowVector3.Dot(world, right), world.Y, ShadowVector3.Dot(world, forward));

        private static ShadowVector3 Flatten(ShadowVector3 value) => new(value.X, 0.0, value.Z);
        private static ShadowVector3 Lerp(ShadowVector3 from, ShadowVector3 to, double t) =>
            from + (to - from) * Math.Clamp(t, 0.0, 1.0);

        private static ulong StableHash(string shipId, int wallId, int kind, long bucket)
        {
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < shipId.Length; i++)
            {
                hash ^= shipId[i];
                hash *= 1099511628211UL;
            }
            hash ^= unchecked((ulong)wallId); hash *= 1099511628211UL;
            hash ^= unchecked((ulong)kind); hash *= 1099511628211UL;
            hash ^= unchecked((ulong)bucket); hash *= 1099511628211UL;
            return hash;
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9UL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }

        private static (VectorWallSegment Segment, double Distance, double Intensity, double Visual)?
            FirstOrDefaultNullable(this IOrderedEnumerable<(VectorWallSegment Segment, double Distance, double Intensity, double Visual)> source)
        {
            foreach (var item in source) return item;
            return null;
        }
    }
}
