using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// How much authority the server has for a proxy's shape. Island envelopes and
    /// rotation-expanded hull boxes are conservative telemetry only. A response is
    /// permitted only for reviewed convex geometry; this prevents an AABB corner in
    /// empty air from becoming an invisible wall.
    /// </summary>
    public enum CollisionGeometryConfidence
    {
        ConservativeEnvelope = 0,
        ReviewedConvex = 1
    }

    public readonly record struct CollisionRuntimeProxy(
        CollisionProxy Proxy,
        long FixedStep,
        long AuthorityGeneration,
        double MassKg,
        CollisionGeometryConfidence GeometryConfidence)
    {
        public bool IsValid => FixedStep >= 0 && AuthorityGeneration > 0
            && double.IsFinite(MassKg) && MassKg > 0.0
            && Enum.IsDefined(typeof(CollisionGeometryConfidence), GeometryConfidence);
    }

    public sealed class CollisionRuntimeOptions
    {
        public bool ObserveEnabled { get; init; }
        public bool ResponseEnabled { get; init; }
        public double MaximumVelocityChangeMetresPerSecond { get; init; } = 20.0;

        public bool IsValid => double.IsFinite(MaximumVelocityChangeMetresPerSecond)
            && MaximumVelocityChangeMetresPerSecond > 0.0
            && MaximumVelocityChangeMetresPerSecond <= 50.0;

        public static CollisionRuntimeOptions Off { get; } = new();
    }

    public enum CollisionResponseDisposition
    {
        Off,
        ObservedOnly,
        Applied,
        RejectedStampMismatch,
        RejectedIncompleteEvaluation,
        RejectedAmbiguousGeometry,
        RejectedInitialOverlap,
        RejectedInvalidInput
    }

    public readonly record struct CollisionVelocityCorrection(
        string StableKey,
        ShadowVector3 Before,
        ShadowVector3 After);

    /// <summary>Replay-stable identity for one sorted contact in one authority step.</summary>
    public readonly record struct CollisionContactRecord(
        long FixedStep,
        long AuthorityGeneration,
        int Ordinal,
        CollisionShadowContact Contact);

    public sealed class CollisionRuntimeResult
    {
        internal CollisionRuntimeResult(long fixedStep, long authorityGeneration,
            CollisionShadowResult observation,
            CollisionResponseDisposition disposition,
            IReadOnlyList<CollisionVelocityCorrection> corrections)
        {
            Observation = observation;
            Disposition = disposition;
            Corrections = corrections;
            ContactRecords = Array.AsReadOnly(observation.Contacts.Select((contact, ordinal) =>
                new CollisionContactRecord(fixedStep, authorityGeneration, ordinal, contact)).ToArray());
        }

        public CollisionShadowResult Observation { get; }
        public CollisionResponseDisposition Disposition { get; }
        public IReadOnlyList<CollisionVelocityCorrection> Corrections { get; }
        public IReadOnlyList<CollisionContactRecord> ContactRecords { get; }
        public bool MutatesAuthoritativeVelocity => Disposition == CollisionResponseDisposition.Applied;
    }

    /// <summary>
    /// Fixed-step adapter around <see cref="CollisionShadowEvaluator"/>. Observation
    /// and response consume one authority generation and one step. The response is
    /// deliberately frictionless, inelastic and velocity-only: no position teleport,
    /// restitution, damage or part detachment can be produced here.
    /// </summary>
    public static class CollisionRuntime
    {
        public static CollisionRuntimeResult Evaluate(long fixedStep, long authorityGeneration,
            IReadOnlyList<CollisionRuntimeProxy>? dynamicProxies,
            IReadOnlyList<CollisionRuntimeProxy>? terrainProxies,
            double stepSeconds, CollisionRuntimeOptions? options = null)
        {
            options ??= CollisionRuntimeOptions.Off;
            dynamicProxies ??= Array.Empty<CollisionRuntimeProxy>();
            terrainProxies ??= Array.Empty<CollisionRuntimeProxy>();
            CollisionShadowResult empty = CollisionShadowEvaluator.Evaluate(
                Array.Empty<CollisionProxy>(), Array.Empty<CollisionProxy>(),
                ValidStepOrDefault(stepSeconds));

            if (!options.IsValid || fixedStep < 0 || authorityGeneration <= 0
                || !double.IsFinite(stepSeconds) || stepSeconds <= 0.0
                || stepSeconds > CollisionShadowLimits.MaxStepSeconds
                || dynamicProxies.Any(x => !x.IsValid)
                || terrainProxies.Any(x => !x.IsValid))
                return Result(fixedStep, authorityGeneration, empty, CollisionResponseDisposition.RejectedInvalidInput);

            if (!options.ObserveEnabled)
                return Result(fixedStep, authorityGeneration, empty, CollisionResponseDisposition.Off);

            if (dynamicProxies.Any(x => x.FixedStep != fixedStep
                    || x.AuthorityGeneration != authorityGeneration)
                || terrainProxies.Any(x => x.FixedStep != fixedStep
                    || x.AuthorityGeneration != authorityGeneration))
                return Result(fixedStep, authorityGeneration, empty, CollisionResponseDisposition.RejectedStampMismatch);

            CollisionShadowResult observation = CollisionShadowEvaluator.Evaluate(
                dynamicProxies.Select(x => x.Proxy).ToArray(),
                terrainProxies.Select(x => x.Proxy).ToArray(), stepSeconds);
            if (!options.ResponseEnabled)
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.ObservedOnly);

            // RejectedProxyCount covers evaluator-dropped inputs the runtime gate
            // cannot see (non-finite bounds, duplicate ids, oversized geometry). A
            // response computed while any supplied proxy was silently ignored is an
            // incomplete evaluation; it also keeps the id dictionaries below
            // collision-free by construction.
            CollisionShadowTelemetry t = observation.Telemetry;
            if (t.HardInputRejected || t.RejectedProxyCount > 0
                || t.DynamicCapReached || t.TerrainCapReached
                || t.PairCapReached || t.ContactCapReached)
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.RejectedIncompleteEvaluation);
            if (observation.Contacts.Count == 0)
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.ObservedOnly);
            if (observation.Contacts.Any(x => x.InitialOverlap))
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.RejectedInitialOverlap);

            Dictionary<string, CollisionRuntimeProxy> all = dynamicProxies
                .Concat(terrainProxies).ToDictionary(x => x.Proxy.Id, StringComparer.Ordinal);
            if (observation.Contacts.Any(contact =>
                    !all.TryGetValue(contact.FirstId, out CollisionRuntimeProxy first)
                    || !all.TryGetValue(contact.SecondId, out CollisionRuntimeProxy second)
                    || first.GeometryConfidence != CollisionGeometryConfidence.ReviewedConvex
                    || second.GeometryConfidence != CollisionGeometryConfidence.ReviewedConvex))
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.RejectedAmbiguousGeometry);
            if (observation.Contacts.Any(contact => IsEdgeOrCorner(contact,
                    all[contact.SecondId].Proxy.Bounds)))
                return Result(fixedStep, authorityGeneration, observation, CollisionResponseDisposition.RejectedAmbiguousGeometry);

            Dictionary<string, ShadowVector3> velocity = dynamicProxies.ToDictionary(
                x => x.Proxy.Id, x => x.Proxy.VelocityMetresPerSecond, StringComparer.Ordinal);
            Dictionary<string, ShadowVector3> original = new(velocity, StringComparer.Ordinal);
            foreach (CollisionShadowContact contact in observation.Contacts)
            {
                if (contact.ClosingSpeedMetresPerSecond <= CollisionShadowLimits.Epsilon)
                    continue;
                CollisionRuntimeProxy first = all[contact.FirstId];
                ShadowVector3 firstVelocity = velocity[contact.FirstId];
                double firstInverseMass = 1.0 / first.MassKg;
                double secondInverseMass = contact.Kind == CollisionContactKind.HullHull
                    ? 1.0 / all[contact.SecondId].MassKg : 0.0;
                double impulse = contact.ClosingSpeedMetresPerSecond
                    / (firstInverseMass + secondInverseMass);
                ShadowVector3 firstDelta = contact.Normal * (impulse * firstInverseMass);
                if (firstDelta.Magnitude > options.MaximumVelocityChangeMetresPerSecond)
                    firstDelta = firstDelta.NormalizedOrZero()
                        * options.MaximumVelocityChangeMetresPerSecond;
                velocity[contact.FirstId] = firstVelocity + firstDelta;

                if (contact.Kind == CollisionContactKind.HullHull)
                {
                    ShadowVector3 secondDelta = -contact.Normal * (impulse * secondInverseMass);
                    if (secondDelta.Magnitude > options.MaximumVelocityChangeMetresPerSecond)
                        secondDelta = secondDelta.NormalizedOrZero()
                            * options.MaximumVelocityChangeMetresPerSecond;
                    velocity[contact.SecondId] = velocity[contact.SecondId] + secondDelta;
                }
            }

            CollisionVelocityCorrection[] corrections = velocity.OrderBy(x => x.Key,
                    StringComparer.Ordinal)
                .Where(x => !x.Value.Equals(original[x.Key]))
                .Select(x => new CollisionVelocityCorrection(x.Key, original[x.Key], x.Value))
                .ToArray();
            return new CollisionRuntimeResult(fixedStep, authorityGeneration, observation,
                corrections.Length == 0 ? CollisionResponseDisposition.ObservedOnly
                    : CollisionResponseDisposition.Applied,
                Array.AsReadOnly(corrections));
        }

        private static CollisionRuntimeResult Result(long fixedStep, long authorityGeneration,
            CollisionShadowResult observation,
            CollisionResponseDisposition disposition) => new(fixedStep, authorityGeneration,
                observation, disposition,
                Array.Empty<CollisionVelocityCorrection>());

        private static double ValidStepOrDefault(double step) => double.IsFinite(step)
            && step > 0.0 && step <= CollisionShadowLimits.MaxStepSeconds ? step : 0.02;

        private static bool IsEdgeOrCorner(CollisionShadowContact contact, CollisionAabb target)
        {
            const double tolerance = 1e-8;
            int faces = 0;
            if (Math.Abs(contact.Point.X - target.Minimum.X) <= tolerance
                || Math.Abs(contact.Point.X - target.Maximum.X) <= tolerance) faces++;
            if (Math.Abs(contact.Point.Y - target.Minimum.Y) <= tolerance
                || Math.Abs(contact.Point.Y - target.Maximum.Y) <= tolerance) faces++;
            if (Math.Abs(contact.Point.Z - target.Minimum.Z) <= tolerance
                || Math.Abs(contact.Point.Z - target.Maximum.Z) <= tolerance) faces++;
            return faces > 1;
        }
    }
}
