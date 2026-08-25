using System;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class CollisionRuntimeTests
    {
        [Fact]
        public void Observation_is_deterministic_and_never_mutates_velocity()
        {
            CollisionRuntimeProxy ship = Hull("ship", 12, 7, 1000, 0, 60);
            CollisionRuntimeProxy wall = Terrain("wall", 12, 7, 10,
                CollisionGeometryConfidence.ConservativeEnvelope);
            var options = new CollisionRuntimeOptions { ObserveEnabled = true };

            CollisionRuntimeResult a = CollisionRuntime.Evaluate(12, 7,
                new[] { ship }, new[] { wall }, 0.2, options);
            CollisionRuntimeResult b = CollisionRuntime.Evaluate(12, 7,
                new[] { ship }, new[] { wall }, 0.2, options);

            Assert.Equal(CollisionResponseDisposition.ObservedOnly, a.Disposition);
            Assert.Equal(a.Observation.Contacts, b.Observation.Contacts);
            Assert.Empty(a.Corrections);
            Assert.False(a.MutatesAuthoritativeVelocity);
        }

        [Fact]
        public void Response_fails_closed_for_stale_authority_or_step()
        {
            var options = On();
            Assert.Equal(CollisionResponseDisposition.RejectedStampMismatch,
                CollisionRuntime.Evaluate(13, 7,
                    new[] { Hull("ship", 12, 7, 1000, 0, 60) },
                    new[] { Terrain("wall", 12, 7, 10) }, 0.2, options).Disposition);
            Assert.Equal(CollisionResponseDisposition.RejectedStampMismatch,
                CollisionRuntime.Evaluate(12, 8,
                    new[] { Hull("ship", 12, 7, 1000, 0, 60) },
                    new[] { Terrain("wall", 12, 7, 10) }, 0.2, options).Disposition);
        }

        [Fact]
        public void Conservative_island_aabb_is_observed_but_never_solved()
        {
            CollisionRuntimeResult result = CollisionRuntime.Evaluate(12, 7,
                new[] { Hull("ship", 12, 7, 1000, 0, 60) },
                new[] { Terrain("island-envelope", 12, 7, 10,
                    CollisionGeometryConfidence.ConservativeEnvelope) }, 0.2, On());

            Assert.Single(result.Observation.Contacts);
            Assert.Equal(CollisionResponseDisposition.RejectedAmbiguousGeometry,
                result.Disposition);
            Assert.Empty(result.Corrections);
        }

        [Fact]
        public void Reviewed_wall_stops_tunnelling_with_bounded_inelastic_response()
        {
            CollisionRuntimeResult result = CollisionRuntime.Evaluate(12, 7,
                new[] { Hull("ship", 12, 7, 1000, 0, 60) },
                new[] { Terrain("wall", 12, 7, 10) }, 0.2, On(maxDelta: 20));

            CollisionVelocityCorrection correction = Assert.Single(result.Corrections);
            Assert.Equal(CollisionResponseDisposition.Applied, result.Disposition);
            Assert.Equal(60, correction.Before.X);
            Assert.Equal(40, correction.After.X); // hard response budget, no teleport
            Assert.Equal(0, correction.After.Y);
        }

        [Fact]
        public void Equal_mass_hulls_share_inelastic_impulse_in_stable_key_order()
        {
            CollisionRuntimeProxy a = Dynamic("a", -3, 10, 1000);
            CollisionRuntimeProxy b = Dynamic("b", 3, -10, 1000);

            CollisionRuntimeResult forward = CollisionRuntime.Evaluate(1, 1,
                new[] { b, a }, Array.Empty<CollisionRuntimeProxy>(), 0.25, On());
            CollisionRuntimeResult reverse = CollisionRuntime.Evaluate(1, 1,
                new[] { a, b }, Array.Empty<CollisionRuntimeProxy>(), 0.25, On());

            Assert.Equal(new[] { "a", "b" }, forward.Corrections.Select(x => x.StableKey));
            Assert.Equal(forward.Corrections, reverse.Corrections);
            Assert.All(forward.Corrections, x => Assert.Equal(0, x.After.X, 10));
        }

        [Fact]
        public void Initial_overlap_and_resting_manifold_do_not_inject_energy()
        {
            CollisionRuntimeProxy ship = Dynamic("ship", 0, 0, 1000);
            CollisionRuntimeProxy wall = TerrainAt("wall", 0);
            CollisionRuntimeResult result = CollisionRuntime.Evaluate(1, 1,
                new[] { ship }, new[] { wall }, 0.02, On());

            Assert.Equal(CollisionResponseDisposition.RejectedInitialOverlap,
                result.Disposition);
            Assert.Empty(result.Corrections);
        }

        [Fact]
        public void Grazing_contact_has_no_normal_impulse()
        {
            var ship = new CollisionRuntimeProxy(new CollisionProxy("ship",
                CollisionProxyKind.ShipHull, Box(0, 2, 0, 1, 1, 1), new(10, 0, 0)),
                1, 1, 1000, CollisionGeometryConfidence.ReviewedConvex);
            var floor = new CollisionRuntimeProxy(new CollisionProxy("floor",
                CollisionProxyKind.IslandTerrain, Box(3, 0, 0, 1, 1, 5), default),
                1, 1, 1, CollisionGeometryConfidence.ReviewedConvex);

            CollisionRuntimeResult result = CollisionRuntime.Evaluate(1, 1,
                new[] { ship }, new[] { floor }, 0.25, On());
            Assert.NotEmpty(result.Observation.Contacts);
            Assert.Equal(CollisionResponseDisposition.RejectedAmbiguousGeometry,
                result.Disposition);
            Assert.Empty(result.Corrections);
        }

        [Fact]
        public void Invalid_options_and_oversized_batches_fail_closed()
        {
            CollisionRuntimeResult invalid = CollisionRuntime.Evaluate(1, 1,
                Array.Empty<CollisionRuntimeProxy>(), Array.Empty<CollisionRuntimeProxy>(),
                0.02, new CollisionRuntimeOptions
                { ObserveEnabled = true, ResponseEnabled = true,
                    MaximumVelocityChangeMetresPerSecond = double.PositiveInfinity });
            Assert.Equal(CollisionResponseDisposition.RejectedInvalidInput, invalid.Disposition);

            CollisionRuntimeProxy[] tooMany = Enumerable.Range(0,
                    CollisionShadowLimits.HardInputCount + 1)
                .Select(i => Dynamic("h" + i, i % 100, 0, 1)).ToArray();
            CollisionRuntimeResult capped = CollisionRuntime.Evaluate(1, 1, tooMany,
                Array.Empty<CollisionRuntimeProxy>(), 0.02, On());
            Assert.Equal(CollisionResponseDisposition.RejectedIncompleteEvaluation,
                capped.Disposition);
            Assert.Empty(capped.Corrections);
        }

        [Fact]
        public void Duplicate_proxy_ids_reject_response_without_throwing()
        {
            CollisionRuntimeResult result = CollisionRuntime.Evaluate(12, 7,
                new[] { Hull("ship", 12, 7, 1000, 0, 60), Hull("ship", 12, 7, 1000, 0, 60) },
                new[] { Terrain("wall", 12, 7, 10) }, 0.2, On());

            Assert.Equal(CollisionResponseDisposition.RejectedIncompleteEvaluation,
                result.Disposition);
            Assert.Empty(result.Corrections);
        }

        [Fact]
        public void Evaluator_dropped_nonfinite_proxy_rejects_response()
        {
            var nonFinite = new CollisionRuntimeProxy(new CollisionProxy("ghost",
                CollisionProxyKind.ShipHull,
                new CollisionAabb(new ShadowVector3(double.NaN, 0, 0), new ShadowVector3(1, 1, 1)),
                default), 12, 7, 1000, CollisionGeometryConfidence.ReviewedConvex);

            CollisionRuntimeResult result = CollisionRuntime.Evaluate(12, 7,
                new[] { Hull("ship", 12, 7, 1000, 0, 60), nonFinite },
                new[] { Terrain("wall", 12, 7, 10) }, 0.2, On());

            Assert.Equal(CollisionResponseDisposition.RejectedIncompleteEvaluation,
                result.Disposition);
            Assert.Empty(result.Corrections);
        }

        private static CollisionRuntimeOptions On(double maxDelta = 50) => new()
        { ObserveEnabled = true, ResponseEnabled = true,
            MaximumVelocityChangeMetresPerSecond = maxDelta };

        private static CollisionRuntimeProxy Hull(string id, long step, long generation,
            double mass, double x, double speed) => new(new CollisionProxy(id,
                CollisionProxyKind.ShipHull, Box(x, 0, 0, 1, 1, 1), new(speed, 0, 0)),
                step, generation, mass, CollisionGeometryConfidence.ReviewedConvex);

        private static CollisionRuntimeProxy Dynamic(string id, double x, double vx,
            double mass) => new(new CollisionProxy(id, CollisionProxyKind.ShipHull,
                Box(x, 0, 0, 1, 1, 1), new(vx, 0, 0)), 1, 1, mass,
                CollisionGeometryConfidence.ReviewedConvex);

        private static CollisionRuntimeProxy Terrain(string id, long step,
            long generation, double x, CollisionGeometryConfidence confidence =
                CollisionGeometryConfidence.ReviewedConvex) => new(new CollisionProxy(id,
                CollisionProxyKind.IslandTerrain, Box(x, 0, 0, .25, 10, 10), default),
                step, generation, 1, confidence);

        private static CollisionRuntimeProxy TerrainAt(string id, double x) => new(
            new CollisionProxy(id, CollisionProxyKind.IslandTerrain,
                Box(x, 0, 0, 2, 2, 2), default), 1, 1, 1,
            CollisionGeometryConfidence.ReviewedConvex);

        private static CollisionAabb Box(double x, double y, double z,
            double hx, double hy, double hz) => CollisionAabb.FromCentreHalfExtents(
                new(x, y, z), new(hx, hy, hz));
    }
}
