using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>Inclusive world-space axis-aligned bounds, in metres.</summary>
    public readonly record struct CollisionAabb(ShadowVector3 Minimum, ShadowVector3 Maximum)
    {
        public bool IsFinite => Minimum.IsFinite && Maximum.IsFinite;
        public bool IsOrdered => Minimum.X <= Maximum.X && Minimum.Y <= Maximum.Y && Minimum.Z <= Maximum.Z;
        public ShadowVector3 Centre => (Minimum + Maximum) * 0.5;
        public ShadowVector3 HalfExtents => (Maximum - Minimum) * 0.5;

        public static CollisionAabb FromCentreHalfExtents(ShadowVector3 centre, ShadowVector3 halfExtents) =>
            new(centre - halfExtents, centre + halfExtents);

        public bool Overlaps(CollisionAabb other) =>
            Minimum.X <= other.Maximum.X && Maximum.X >= other.Minimum.X &&
            Minimum.Y <= other.Maximum.Y && Maximum.Y >= other.Minimum.Y &&
            Minimum.Z <= other.Maximum.Z && Maximum.Z >= other.Minimum.Z;

        public CollisionAabb Swept(ShadowVector3 displacement)
        {
            ShadowVector3 endMin = Minimum + displacement;
            ShadowVector3 endMax = Maximum + displacement;
            return new CollisionAabb(
                new ShadowVector3(Math.Min(Minimum.X, endMin.X), Math.Min(Minimum.Y, endMin.Y), Math.Min(Minimum.Z, endMin.Z)),
                new ShadowVector3(Math.Max(Maximum.X, endMax.X), Math.Max(Maximum.Y, endMax.Y), Math.Max(Maximum.Z, endMax.Z)));
        }
    }

    public enum CollisionProxyKind
    {
        ShipHull,
        IslandTerrain
    }

    /// <summary>
    /// Server-authored conservative collision representation. Client collision
    /// reports never construct or modify one of these records.
    /// </summary>
    public readonly record struct CollisionProxy(
        string Id,
        CollisionProxyKind Kind,
        CollisionAabb Bounds,
        ShadowVector3 VelocityMetresPerSecond);

    public enum CollisionContactKind
    {
        Terrain,
        HullHull
    }

    /// <summary>Stable SHADOW contact. It is telemetry, not a response or damage command.</summary>
    public readonly record struct CollisionShadowContact(
        CollisionContactKind Kind,
        string FirstId,
        string SecondId,
        double TimeOfImpact,
        ShadowVector3 Point,
        ShadowVector3 Normal,
        double ClosingSpeedMetresPerSecond,
        bool InitialOverlap);

    /// <summary>Bounded work and comparison facts suitable for later admin telemetry.</summary>
    public readonly record struct CollisionShadowTelemetry(
        int SuppliedDynamicCount,
        int SuppliedTerrainCount,
        int AcceptedDynamicCount,
        int AcceptedTerrainCount,
        int RejectedProxyCount,
        int BroadphaseCandidateCount,
        int NarrowphaseTestCount,
        int TerrainContactCount,
        int HullContactCount,
        int CurrentAuthoritativeContactCount,
        int ShadowOnlyContactCount,
        bool DynamicCapReached,
        bool TerrainCapReached,
        bool PairCapReached,
        bool ContactCapReached,
        bool HardInputRejected);

    public sealed class CollisionShadowResult
    {
        internal CollisionShadowResult(IReadOnlyList<CollisionShadowContact> contacts, CollisionShadowTelemetry telemetry)
        {
            Contacts = contacts;
            Telemetry = telemetry;
        }

        public IReadOnlyList<CollisionShadowContact> Contacts { get; }
        public CollisionShadowTelemetry Telemetry { get; }
    }

    /// <summary>
    /// Immutable proof consumed by later policies such as docking. A clear result is
    /// never inferred from a truncated or rejected collision batch. Stable domain
    /// keys are used deliberately; runtime entity ids are not persistence identities.
    /// The expected subject/target overlap may be excluded for a shipyard capture
    /// volume, while every other contact remains blocking.
    /// </summary>
    public readonly record struct CollisionClearanceRecord(
        string SubjectStableKey,
        string ExpectedTargetStableKey,
        long FixedStep,
        int BlockingContactCount,
        bool EvaluationComplete)
    {
        public bool IsValid => !string.IsNullOrWhiteSpace(SubjectStableKey)
            && !string.IsNullOrWhiteSpace(ExpectedTargetStableKey)
            && FixedStep >= 0 && BlockingContactCount >= 0;

        public bool IsClear => IsValid && EvaluationComplete && BlockingContactCount == 0;

        public static CollisionClearanceRecord From(CollisionShadowResult result,
            string subjectStableKey, string expectedTargetStableKey, long fixedStep)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            // RejectedProxyCount == 0 is load-bearing: a proxy the evaluator's
            // Validate silently dropped (over-speed, non-finite, oversized) may be
            // the SUBJECT itself, and a sweep that never contained the subject has
            // zero contacts for the wrong reason. Dropped input never means clear.
            bool complete = !result.Telemetry.HardInputRejected
                && result.Telemetry.RejectedProxyCount == 0
                && !result.Telemetry.DynamicCapReached
                && !result.Telemetry.TerrainCapReached
                && !result.Telemetry.PairCapReached
                && !result.Telemetry.ContactCapReached;
            int blockers = result.Contacts.Count(contact =>
                Touches(contact, subjectStableKey)
                && !IsExpectedPair(contact, subjectStableKey, expectedTargetStableKey));
            return new CollisionClearanceRecord(subjectStableKey,
                expectedTargetStableKey, fixedStep, blockers, complete);
        }
        private static bool Touches(CollisionShadowContact contact, string stableKey) =>
            string.Equals(contact.FirstId, stableKey, StringComparison.Ordinal)
            || string.Equals(contact.SecondId, stableKey, StringComparison.Ordinal);

        private static bool IsExpectedPair(CollisionShadowContact contact,
            string subjectStableKey, string targetStableKey) =>
            (string.Equals(contact.FirstId, subjectStableKey, StringComparison.Ordinal)
                && string.Equals(contact.SecondId, targetStableKey, StringComparison.Ordinal))
            || (string.Equals(contact.SecondId, subjectStableKey, StringComparison.Ordinal)
                && string.Equals(contact.FirstId, targetStableKey, StringComparison.Ordinal));
    }

    /// <summary>Hard complexity and geometry limits. Inputs are server-owned only.</summary>
    public static class CollisionShadowLimits
    {
        public const int MaxDynamicProxies = 256;
        public const int MaxTerrainProxies = 512;
        public const int MaxCandidatePairs = 16384;
        public const int MaxContacts = 1024;
        public const int HardInputCount = 4096;
        public const int MaxIdLength = 96;
        public const double MaxHalfExtentMetres = 512.0;
        public const double MaxAbsoluteCoordinateMetres = 100000.0;
        public const double MaxSpeedMetresPerSecond = 250.0;
        public const double MaxStepSeconds = 0.25;
        internal const double Epsilon = 1e-10;
    }

    /// <summary>
    /// Pure deterministic broadphase and swept-contact evaluator. It intentionally
    /// does not reference FlightSession, network packets, damage, or aboard state.
    /// Terrain uses extracted envelopes as conservative static boxes; hulls use
    /// conservative world AABBs until Track 3 supplies authoritative orientation.
    /// </summary>
    public static class CollisionShadowEvaluator
    {
        public static CollisionShadowResult Evaluate(
            IReadOnlyList<CollisionProxy>? dynamicProxies,
            IReadOnlyList<CollisionProxy>? terrainProxies,
            double stepSeconds,
            int currentAuthoritativeContactCount = 0)
        {
            dynamicProxies ??= Array.Empty<CollisionProxy>();
            terrainProxies ??= Array.Empty<CollisionProxy>();
            int currentCount = Math.Max(0, currentAuthoritativeContactCount);

            if (dynamicProxies.Count > CollisionShadowLimits.HardInputCount ||
                terrainProxies.Count > CollisionShadowLimits.HardInputCount ||
                !double.IsFinite(stepSeconds) || stepSeconds <= 0.0 || stepSeconds > CollisionShadowLimits.MaxStepSeconds)
            {
                return Empty(dynamicProxies.Count, terrainProxies.Count, currentCount, hardRejected: true);
            }

            int rejected = 0;
            bool dynamicCap = false;
            bool terrainCap = false;
            List<CollisionProxy> dynamics = Validate(dynamicProxies, CollisionProxyKind.ShipHull,
                CollisionShadowLimits.MaxDynamicProxies, ref rejected, ref dynamicCap);
            List<CollisionProxy> terrain = Validate(terrainProxies, CollisionProxyKind.IslandTerrain,
                CollisionShadowLimits.MaxTerrainProxies, ref rejected, ref terrainCap);
            HashSet<string> dynamicIds = new(dynamics.Select(proxy => proxy.Id), StringComparer.Ordinal);
            rejected += terrain.RemoveAll(proxy => dynamicIds.Contains(proxy.Id));

            List<CollisionShadowContact> contacts = new();
            int candidates = 0;
            int narrowphase = 0;
            bool pairCap = false;
            bool contactCap = false;

            // Terrain always precedes hull-hull work. Stable input sorting makes
            // pair selection and contact order independent of caller iteration.
            foreach (CollisionProxy hull in dynamics)
            {
                ShadowVector3 displacement = hull.VelocityMetresPerSecond * stepSeconds;
                CollisionAabb swept = hull.Bounds.Swept(displacement);
                foreach (CollisionProxy island in terrain)
                {
                    if (!swept.Overlaps(island.Bounds)) continue;
                    if (candidates >= CollisionShadowLimits.MaxCandidatePairs) { pairCap = true; break; }
                    candidates++;
                    narrowphase++;
                    if (TrySweep(hull.Bounds, displacement, island.Bounds, out SweepHit hit))
                    {
                        if (contacts.Count >= CollisionShadowLimits.MaxContacts) { contactCap = true; break; }
                        contacts.Add(ToContact(CollisionContactKind.Terrain, hull, island,
                            hull.VelocityMetresPerSecond, hit));
                    }
                }
                if (pairCap || contactCap) break;
            }

            if (!pairCap && !contactCap)
            {
                for (int i = 0; i < dynamics.Count; i++)
                {
                    for (int j = i + 1; j < dynamics.Count; j++)
                    {
                        CollisionProxy first = dynamics[i];
                        CollisionProxy second = dynamics[j];
                        ShadowVector3 firstDelta = first.VelocityMetresPerSecond * stepSeconds;
                        ShadowVector3 secondDelta = second.VelocityMetresPerSecond * stepSeconds;
                        if (!first.Bounds.Swept(firstDelta).Overlaps(second.Bounds.Swept(secondDelta))) continue;
                        if (candidates >= CollisionShadowLimits.MaxCandidatePairs) { pairCap = true; break; }
                        candidates++;
                        narrowphase++;
                        ShadowVector3 relativeDelta = firstDelta - secondDelta;
                        if (TrySweep(first.Bounds, relativeDelta, second.Bounds, out SweepHit hit))
                        {
                            if (contacts.Count >= CollisionShadowLimits.MaxContacts) { contactCap = true; break; }
                            ShadowVector3 secondMotionAtImpact = secondDelta * hit.Time;
                            SweepHit worldHit = hit with { Point = hit.Point + secondMotionAtImpact };
                            contacts.Add(ToContact(CollisionContactKind.HullHull, first, second,
                                first.VelocityMetresPerSecond - second.VelocityMetresPerSecond, worldHit));
                        }
                    }
                    if (pairCap || contactCap) break;
                }
            }

            contacts.Sort(ContactComparer.Instance);
            int terrainContacts = contacts.Count(x => x.Kind == CollisionContactKind.Terrain);
            int hullContacts = contacts.Count - terrainContacts;
            CollisionShadowTelemetry telemetry = new(
                dynamicProxies.Count, terrainProxies.Count, dynamics.Count, terrain.Count, rejected,
                candidates, narrowphase, terrainContacts, hullContacts, currentCount,
                Math.Max(0, contacts.Count - currentCount), dynamicCap, terrainCap,
                pairCap, contactCap, false);
            return new CollisionShadowResult(Array.AsReadOnly(contacts.ToArray()), telemetry);
        }

        private static CollisionShadowResult Empty(int dynamicCount, int terrainCount, int currentCount, bool hardRejected)
        {
            CollisionShadowTelemetry telemetry = new(dynamicCount, terrainCount, 0, 0,
                dynamicCount + terrainCount, 0, 0, 0, 0, currentCount, 0,
                false, false, false, false, hardRejected);
            return new CollisionShadowResult(Array.Empty<CollisionShadowContact>(), telemetry);
        }

        private static List<CollisionProxy> Validate(IReadOnlyList<CollisionProxy> source,
            CollisionProxyKind requiredKind, int capacity, ref int rejected, ref bool capReached)
        {
            List<CollisionProxy> valid = new(Math.Min(source.Count, capacity));
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (CollisionProxy proxy in source.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (!IsValid(proxy, requiredKind) || !ids.Add(proxy.Id))
                {
                    rejected++;
                    continue;
                }
                if (valid.Count < capacity) valid.Add(proxy);
                else
                {
                    rejected++;
                    capReached = true;
                }
            }
            return valid;
        }

        private static bool IsValid(CollisionProxy proxy, CollisionProxyKind kind)
        {
            if (proxy.Kind != kind || string.IsNullOrWhiteSpace(proxy.Id) || proxy.Id.Length > CollisionShadowLimits.MaxIdLength)
                return false;
            if (!proxy.Bounds.IsFinite || !proxy.Bounds.IsOrdered || !proxy.VelocityMetresPerSecond.IsFinite)
                return false;
            if (kind == CollisionProxyKind.IslandTerrain
                && !proxy.VelocityMetresPerSecond.Equals(ShadowVector3.Zero))
                return false;
            ShadowVector3 centre = proxy.Bounds.Centre;
            ShadowVector3 half = proxy.Bounds.HalfExtents;
            return Math.Abs(centre.X) <= CollisionShadowLimits.MaxAbsoluteCoordinateMetres &&
                Math.Abs(centre.Y) <= CollisionShadowLimits.MaxAbsoluteCoordinateMetres &&
                Math.Abs(centre.Z) <= CollisionShadowLimits.MaxAbsoluteCoordinateMetres &&
                half.X > 0.0 && half.Y > 0.0 && half.Z > 0.0 &&
                half.X <= CollisionShadowLimits.MaxHalfExtentMetres &&
                half.Y <= CollisionShadowLimits.MaxHalfExtentMetres &&
                half.Z <= CollisionShadowLimits.MaxHalfExtentMetres &&
                proxy.VelocityMetresPerSecond.Magnitude <= CollisionShadowLimits.MaxSpeedMetresPerSecond;
        }

        private readonly record struct SweepHit(double Time, ShadowVector3 Point,
            ShadowVector3 Normal, bool InitialOverlap);

        private static bool TrySweep(CollisionAabb moving, ShadowVector3 displacement,
            CollisionAabb target, out SweepHit hit)
        {
            ShadowVector3 origin = moving.Centre;
            ShadowVector3 half = moving.HalfExtents;
            CollisionAabb expanded = new(target.Minimum - half, target.Maximum + half);

            if (Contains(expanded, origin))
            {
                ShadowVector3 normal = MinimumPenetrationNormal(origin, expanded);
                hit = new SweepHit(0.0, SurfacePoint(moving.Centre, half, target, normal), normal, true);
                return true;
            }

            double enter = 0.0;
            double exit = 1.0;
            ShadowVector3 normalAtEnter = ShadowVector3.Zero;
            if (!Slab(origin.X, displacement.X, expanded.Minimum.X, expanded.Maximum.X,
                    new ShadowVector3(-1, 0, 0), new ShadowVector3(1, 0, 0), ref enter, ref exit, ref normalAtEnter) ||
                !Slab(origin.Y, displacement.Y, expanded.Minimum.Y, expanded.Maximum.Y,
                    new ShadowVector3(0, -1, 0), new ShadowVector3(0, 1, 0), ref enter, ref exit, ref normalAtEnter) ||
                !Slab(origin.Z, displacement.Z, expanded.Minimum.Z, expanded.Maximum.Z,
                    new ShadowVector3(0, 0, -1), new ShadowVector3(0, 0, 1), ref enter, ref exit, ref normalAtEnter) ||
                enter < -CollisionShadowLimits.Epsilon || enter > 1.0 + CollisionShadowLimits.Epsilon)
            {
                hit = default;
                return false;
            }

            double time = Math.Clamp(enter, 0.0, 1.0);
            ShadowVector3 centre = origin + displacement * time;
            hit = new SweepHit(time, SurfacePoint(centre, half, target, normalAtEnter), normalAtEnter, false);
            return true;
        }

        private static bool Slab(double origin, double displacement, double minimum, double maximum,
            ShadowVector3 negativeNormal, ShadowVector3 positiveNormal,
            ref double enter, ref double exit, ref ShadowVector3 normalAtEnter)
        {
            if (Math.Abs(displacement) <= CollisionShadowLimits.Epsilon)
                return origin >= minimum && origin <= maximum;

            double first = (minimum - origin) / displacement;
            double second = (maximum - origin) / displacement;
            double near;
            double far;
            ShadowVector3 nearNormal;
            if (first <= second)
            {
                near = first; far = second; nearNormal = negativeNormal;
            }
            else
            {
                near = second; far = first; nearNormal = positiveNormal;
            }

            // Strict comparison intentionally keeps X before Y before Z on ties.
            if (near > enter + CollisionShadowLimits.Epsilon)
            {
                enter = near;
                normalAtEnter = nearNormal;
            }
            exit = Math.Min(exit, far);
            return enter <= exit + CollisionShadowLimits.Epsilon && exit >= -CollisionShadowLimits.Epsilon;
        }

        private static bool Contains(CollisionAabb bounds, ShadowVector3 point) =>
            point.X >= bounds.Minimum.X && point.X <= bounds.Maximum.X &&
            point.Y >= bounds.Minimum.Y && point.Y <= bounds.Maximum.Y &&
            point.Z >= bounds.Minimum.Z && point.Z <= bounds.Maximum.Z;

        private static ShadowVector3 MinimumPenetrationNormal(ShadowVector3 point, CollisionAabb bounds)
        {
            double[] distance =
            {
                point.X - bounds.Minimum.X, bounds.Maximum.X - point.X,
                point.Y - bounds.Minimum.Y, bounds.Maximum.Y - point.Y,
                point.Z - bounds.Minimum.Z, bounds.Maximum.Z - point.Z
            };
            ShadowVector3[] normal =
            {
                new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0),
                new(0, 1, 0), new(0, 0, -1), new(0, 0, 1)
            };
            int chosen = 0;
            for (int i = 1; i < distance.Length; i++)
                if (distance[i] < distance[chosen] - CollisionShadowLimits.Epsilon) chosen = i;
            return normal[chosen];
        }

        private static ShadowVector3 SurfacePoint(ShadowVector3 movingCentre, ShadowVector3 movingHalf,
            CollisionAabb target, ShadowVector3 normal)
        {
            ShadowVector3 face = new(
                movingCentre.X - normal.X * movingHalf.X,
                movingCentre.Y - normal.Y * movingHalf.Y,
                movingCentre.Z - normal.Z * movingHalf.Z);
            return new ShadowVector3(
                Math.Clamp(face.X, target.Minimum.X, target.Maximum.X),
                Math.Clamp(face.Y, target.Minimum.Y, target.Maximum.Y),
                Math.Clamp(face.Z, target.Minimum.Z, target.Maximum.Z));
        }

        private static CollisionShadowContact ToContact(CollisionContactKind kind,
            CollisionProxy first, CollisionProxy second, ShadowVector3 relativeVelocity, SweepHit hit)
        {
            double closing = Math.Max(0.0, -ShadowVector3.Dot(relativeVelocity, hit.Normal));
            return new CollisionShadowContact(kind, first.Id, second.Id, hit.Time,
                hit.Point, hit.Normal, closing, hit.InitialOverlap);
        }

        private sealed class ContactComparer : IComparer<CollisionShadowContact>
        {
            public static readonly ContactComparer Instance = new();
            public int Compare(CollisionShadowContact x, CollisionShadowContact y)
            {
                int value = x.TimeOfImpact.CompareTo(y.TimeOfImpact);
                if (value != 0) return value;
                value = x.Kind.CompareTo(y.Kind);
                if (value != 0) return value;
                value = StringComparer.Ordinal.Compare(x.FirstId, y.FirstId);
                return value != 0 ? value : StringComparer.Ordinal.Compare(x.SecondId, y.SecondId);
            }
        }
    }
}
