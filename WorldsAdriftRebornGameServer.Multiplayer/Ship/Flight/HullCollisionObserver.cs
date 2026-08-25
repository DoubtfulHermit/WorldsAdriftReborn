using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// One hull's in-tick collision observation for one stamped frame, plus the
    /// terrain batch it was evaluated against. The clearance builder is the ONLY
    /// sanctioned way to turn an observation into docking evidence: a clearance is
    /// never hand-constructed as clear, and an observation that did not honestly run
    /// (observer off, invalid input, stamp mismatch, truncated terrain interest set)
    /// can only yield an incomplete - therefore never clear - record.
    /// </summary>
    public readonly record struct HullCollisionObservation(
        CollisionRuntimeResult Result,
        IslandCollisionProxyBatch Terrain,
        FlightAuthorityStamp Stamp,
        string HullStableKey)
    {
        /// <summary>
        /// Whether the swept evaluation actually covered every supplied and nearby
        /// proxy for this frame. Response-side rejections (ambiguous geometry,
        /// initial overlap, incomplete caps) still observed honestly; a non-run
        /// (Off / invalid input / stamp mismatch), a truncated terrain candidate
        /// set, or any evaluator-dropped proxy voids the observation - a dropped
        /// proxy may be the SUBJECT itself, and a sweep that never contained the
        /// subject observed nothing about it.
        /// </summary>
        public bool ObservationRan =>
            Terrain.EvaluationComplete
            && Result != null
            && Result.Disposition != CollisionResponseDisposition.Off
            && Result.Disposition != CollisionResponseDisposition.RejectedInvalidInput
            && Result.Disposition != CollisionResponseDisposition.RejectedStampMismatch
            && Result.Observation.Telemetry.RejectedProxyCount == 0;

        /// <summary>
        /// Builds the docking clearance for one expected target. Kill-list rule:
        /// truncation never means clear - when the observation did not honestly run
        /// the record is explicitly EvaluationComplete=false, which
        /// <see cref="CollisionClearanceRecord.IsClear"/> refuses.
        /// </summary>
        public CollisionClearanceRecord ClearanceFor(string expectedTargetStableKey)
        {
            if (!ObservationRan)
            {
                return new CollisionClearanceRecord(HullStableKey,
                    expectedTargetStableKey, Stamp.FixedStep, 0,
                    EvaluationComplete: false);
            }
            return CollisionClearanceRecord.From(Result.Observation, HullStableKey,
                expectedTargetStableKey, Stamp.FixedStep);
        }
    }

    /// <summary>
    /// Pure composition of the fixed-step collision runtime for one hull: builds the
    /// hull's conservative subject proxy from the canonical committed pose (never a
    /// second position source), evaluates it against an island terrain batch, and
    /// fails closed when the terrain interest set was truncated.
    /// </summary>
    public static class HullCollisionObserver
    {
        /// <summary>
        /// Conservatively expands axis-aligned hull half-extents for an unknown yaw:
        /// while confidence is ConservativeEnvelope the horizontal box must contain
        /// the hull under any rotation about world +Y, so both horizontal axes take
        /// the beam/keel diagonal radius. Height is rotation-invariant here (yaw-only
        /// attitude authority).
        /// </summary>
        public static ShadowVector3 RotationExpandedHalfExtents(ShadowVector3 halfExtents)
        {
            double horizontal = Math.Sqrt(
                halfExtents.X * halfExtents.X + halfExtents.Z * halfExtents.Z);
            return new ShadowVector3(horizontal, halfExtents.Y, horizontal);
        }

        public static HullCollisionObservation Observe(FlightAuthorityStamp stamp,
            string hullStableKey, ShadowVector3 position, ShadowVector3 velocity,
            ShadowVector3 halfExtents, double massKg, double stepSeconds,
            IslandCollisionProxyBatch terrain, CollisionRuntimeOptions? options)
        {
            options ??= CollisionRuntimeOptions.Off;
            var subject = new CollisionRuntimeProxy(new CollisionProxy(
                    hullStableKey, CollisionProxyKind.ShipHull,
                    CollisionAabb.FromCentreHalfExtents(position,
                        RotationExpandedHalfExtents(halfExtents)), velocity),
                stamp.FixedStep, stamp.AuthorityGeneration, massKg,
                CollisionGeometryConfidence.ConservativeEnvelope);

            // A truncated island candidate set means the frame cannot honestly be
            // evaluated: observation is forced off so the disposition (and any
            // clearance built from it) fails closed instead of reporting a clean
            // - but incomplete - sweep.
            CollisionRuntimeOptions effective = terrain.EvaluationComplete
                ? options
                : new CollisionRuntimeOptions
                {
                    ObserveEnabled = false,
                    ResponseEnabled = false,
                    MaximumVelocityChangeMetresPerSecond =
                        options.MaximumVelocityChangeMetresPerSecond
                };

            CollisionRuntimeResult result = CollisionRuntime.Evaluate(
                stamp.FixedStep, stamp.AuthorityGeneration,
                new[] { subject }, terrain.Proxies, stepSeconds, effective);
            return new HullCollisionObservation(result, terrain, stamp, hullStableKey);
        }
    }
}
