using System;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class HullCollisionObserverTests
    {
        private static readonly FlightAuthorityStamp Stamp = new(42, 9);
        private static readonly CollisionRuntimeOptions Observe =
            new() { ObserveEnabled = true };

        [Fact]
        public void Subject_proxy_is_conservative_and_rotation_expanded_from_the_canonical_pose()
        {
            ShadowVector3 half = new(2.0, 1.0, 6.0);
            ShadowVector3 expanded = HullCollisionObserver.RotationExpandedHalfExtents(half);

            Assert.Equal(Math.Sqrt(40.0), expanded.X, 12);
            Assert.Equal(Math.Sqrt(40.0), expanded.Z, 12);
            Assert.Equal(1.0, expanded.Y, 12);

            HullCollisionObservation observation = HullCollisionObserver.Observe(Stamp,
                "ship:3", new ShadowVector3(100, 50, -20), new ShadowVector3(1, 0, 0),
                half, 4044.0, 0.02, EmptyCompleteTerrain(), Observe);

            Assert.True(observation.ObservationRan);
            Assert.Equal("ship:3", observation.HullStableKey);
            Assert.Equal(Stamp, observation.Stamp);
            Assert.Equal(CollisionResponseDisposition.ObservedOnly,
                observation.Result.Disposition);
        }

        [Fact]
        public void Truncated_island_interest_set_never_yields_a_clear_clearance()
        {
            var truncated = new IslandCollisionProxyBatch(
                Array.Empty<CollisionRuntimeProxy>(), EvaluationComplete: false, 900);

            HullCollisionObservation observation = HullCollisionObserver.Observe(Stamp,
                "ship:3", new ShadowVector3(0, 0, 0), default,
                new ShadowVector3(1, 1, 1), 1000.0, 0.02, truncated, Observe);

            Assert.False(observation.ObservationRan);
            Assert.Equal(CollisionResponseDisposition.Off, observation.Result.Disposition);
            CollisionClearanceRecord clearance = observation.ClearanceFor("yard:1:2:3");
            Assert.False(clearance.EvaluationComplete);
            Assert.False(clearance.IsClear);
            Assert.Equal(Stamp.FixedStep, clearance.FixedStep);
        }

        [Fact]
        public void Dropped_subject_hull_proxy_never_yields_a_clear_clearance()
        {
            // 300 m/s exceeds the evaluator's 250 m/s validation cap, so the
            // subject proxy itself is silently dropped and the sweep runs with
            // zero dynamics. Zero contacts here means "the hull was never
            // evaluated", not "the approach is clear".
            HullCollisionObservation observation = HullCollisionObserver.Observe(Stamp,
                "ship:3", new ShadowVector3(0, 5000, 0), new ShadowVector3(300, 0, 0),
                new ShadowVector3(3, 1, 8), 4044.0, 0.02, EmptyCompleteTerrain(), Observe);

            Assert.False(observation.ObservationRan);
            Assert.Equal(1,
                observation.Result.Observation.Telemetry.RejectedProxyCount);
            CollisionClearanceRecord clearance = observation.ClearanceFor("yard:1:2:3");
            Assert.False(clearance.EvaluationComplete);
            Assert.False(clearance.IsClear);
            Assert.Equal(Stamp.FixedStep, clearance.FixedStep);
        }

        [Fact]
        public void Observer_off_or_invalid_stamp_never_yields_a_clear_clearance()
        {
            HullCollisionObservation off = HullCollisionObserver.Observe(Stamp, "ship:3",
                new ShadowVector3(0, 0, 0), default, new ShadowVector3(1, 1, 1),
                1000.0, 0.02, EmptyCompleteTerrain(), CollisionRuntimeOptions.Off);
            Assert.False(off.ObservationRan);
            Assert.False(off.ClearanceFor("yard:1:2:3").IsClear);

            HullCollisionObservation invalid = HullCollisionObserver.Observe(
                new FlightAuthorityStamp(-1, 0), "ship:3", new ShadowVector3(0, 0, 0),
                default, new ShadowVector3(1, 1, 1), 1000.0, 0.02,
                EmptyCompleteTerrain(), Observe);
            Assert.False(invalid.ObservationRan);
            Assert.False(invalid.ClearanceFor("yard:1:2:3").IsClear);
        }

        [Fact]
        public void Honest_empty_sweep_far_from_terrain_is_clear_for_the_same_step_only()
        {
            HullCollisionObservation observation = HullCollisionObserver.Observe(Stamp,
                "ship:3", new ShadowVector3(0, 5000, 0), new ShadowVector3(2, 0, 0),
                new ShadowVector3(3, 1, 8), 4044.0, 0.02, EmptyCompleteTerrain(), Observe);

            CollisionClearanceRecord clearance = observation.ClearanceFor("yard:1:2:3");
            Assert.True(clearance.IsClear);
            Assert.Equal(Stamp.FixedStep, clearance.FixedStep);
            Assert.Equal("ship:3", clearance.SubjectStableKey);
        }

        [Fact]
        public void Nearby_island_envelope_blocks_clearance_conservatively()
        {
            ShadowVector3 origin = new(IslandCatalog.Haven.GlobalOrigin.MetresX,
                IslandCatalog.Haven.GlobalOrigin.MetresY,
                IslandCatalog.Haven.GlobalOrigin.MetresZ);
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.Require(IslandCatalog.HavenId);
            ShadowVector3 inside = new(origin.X + (envelope.MinX + envelope.MaxX) * 0.5,
                origin.Y + (envelope.MinY + envelope.MaxY) * 0.5,
                origin.Z + (envelope.MinZ + envelope.MaxZ) * 0.5);
            IslandCollisionProxyBatch terrain = IslandCollisionProxyAdapter.Nearby(
                inside, Stamp.FixedStep, Stamp.AuthorityGeneration, 64.0);
            Assert.True(terrain.EvaluationComplete);
            Assert.NotEmpty(terrain.Proxies);

            HullCollisionObservation observation = HullCollisionObserver.Observe(Stamp,
                "ship:3", inside, default, new ShadowVector3(3, 1, 8), 4044.0, 0.02,
                terrain, Observe);

            Assert.True(observation.ObservationRan);
            CollisionClearanceRecord clearance = observation.ClearanceFor("yard:1:2:3");
            Assert.True(clearance.BlockingContactCount > 0);
            Assert.False(clearance.IsClear);
        }

        [Fact]
        public void Replayed_observation_is_deterministic()
        {
            ShadowVector3 origin = new(IslandCatalog.Haven.GlobalOrigin.MetresX,
                IslandCatalog.Haven.GlobalOrigin.MetresY + 400,
                IslandCatalog.Haven.GlobalOrigin.MetresZ);
            IslandCollisionProxyBatch terrain = IslandCollisionProxyAdapter.Nearby(
                origin, Stamp.FixedStep, Stamp.AuthorityGeneration);

            HullCollisionObservation a = HullCollisionObserver.Observe(Stamp, "ship:3",
                origin, new ShadowVector3(0, -40, 0), new ShadowVector3(3, 1, 8),
                4044.0, 0.02, terrain, Observe);
            HullCollisionObservation b = HullCollisionObserver.Observe(Stamp, "ship:3",
                origin, new ShadowVector3(0, -40, 0), new ShadowVector3(3, 1, 8),
                4044.0, 0.02, terrain, Observe);

            Assert.Equal(a.Result.Observation.Contacts, b.Result.Observation.Contacts);
            Assert.Equal(a.Result.ContactRecords.Select(x => (x.Ordinal, x.Contact)),
                b.Result.ContactRecords.Select(x => (x.Ordinal, x.Contact)));
            Assert.Equal(a.ClearanceFor("yard:1:2:3"), b.ClearanceFor("yard:1:2:3"));
        }

        private static IslandCollisionProxyBatch EmptyCompleteTerrain() => new(
            Array.Empty<CollisionRuntimeProxy>(), EvaluationComplete: true, 0);
    }
}
