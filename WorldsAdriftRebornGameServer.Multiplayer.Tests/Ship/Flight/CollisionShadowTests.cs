using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class CollisionShadowTests
    {
        [Fact]
        public void Sixty_metres_per_second_cannot_tunnel_through_terrain()
        {
            CollisionProxy hull = Hull("ship", Centre(0, 0, 0, 1, 1, 1), new(60, 0, 0));
            CollisionProxy wall = Terrain("island", Centre(10, 0, 0, 0.25, 10, 10));

            CollisionShadowContact contact = CollisionShadowEvaluator
                .Evaluate(new[] { hull }, new[] { wall }, 0.2).Contacts.Single();

            Assert.Equal(CollisionContactKind.Terrain, contact.Kind);
            Assert.Equal(0.7291666666666666, contact.TimeOfImpact, 12);
            Assert.Equal(new ShadowVector3(-1, 0, 0), contact.Normal);
            Assert.Equal(60.0, contact.ClosingSpeedMetresPerSecond);
            Assert.False(contact.InitialOverlap);
            Assert.Equal(9.75, contact.Point.X, 12);
        }

        [Fact]
        public void Grazing_face_and_island_seam_are_inclusive_and_stable()
        {
            CollisionProxy hull = Hull("ship", Centre(0, 2, 0, 1, 1, 1), new(10, 0, 0));
            CollisionProxy seamA = Terrain("a", Centre(4, 0, -1, 1, 1, 1));
            CollisionProxy seamB = Terrain("b", Centre(4, 0, 1, 1, 1, 1));

            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(
                new[] { hull }, new[] { seamB, seamA }, 0.25);

            Assert.Equal(2, result.Contacts.Count);
            Assert.All(result.Contacts, contact => Assert.Equal(0.8, contact.TimeOfImpact, 12));
            Assert.Equal(new[] { "a", "b" }, result.Contacts.Select(x => x.SecondId));
        }

        [Fact]
        public void Initial_overlap_has_deterministic_minimum_penetration_normal()
        {
            CollisionProxy hull = Hull("ship", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero);
            CollisionProxy terrain = Terrain("terrain", Centre(0, 0, 0, 2, 2, 2));

            CollisionShadowContact contact = CollisionShadowEvaluator
                .Evaluate(new[] { hull }, new[] { terrain }, 0.02).Contacts.Single();

            Assert.True(contact.InitialOverlap);
            Assert.Equal(0.0, contact.TimeOfImpact);
            Assert.Equal(new ShadowVector3(-1, 0, 0), contact.Normal);
            Assert.Equal(0.0, contact.ClosingSpeedMetresPerSecond);
        }

        [Fact]
        public void Two_moving_hulls_use_relative_motion_and_world_contact_point()
        {
            CollisionProxy eastbound = Hull("a", Centre(-3, 0, 0, 1, 1, 1), new(10, 0, 0));
            CollisionProxy westbound = Hull("b", Centre(3, 0, 0, 1, 1, 1), new(-10, 0, 0));

            CollisionShadowContact contact = CollisionShadowEvaluator
                .Evaluate(new[] { westbound, eastbound }, Array.Empty<CollisionProxy>(), 0.25)
                .Contacts.Single();

            Assert.Equal(CollisionContactKind.HullHull, contact.Kind);
            Assert.Equal("a", contact.FirstId);
            Assert.Equal("b", contact.SecondId);
            Assert.Equal(0.8, contact.TimeOfImpact, 12);
            Assert.Equal(20.0, contact.ClosingSpeedMetresPerSecond);
            Assert.Equal(0.0, contact.Point.X, 12);
        }

        [Fact]
        public void Contact_order_is_independent_of_input_order()
        {
            CollisionProxy[] hulls =
            {
                Hull("z", Centre(-3, 0, 0, 1, 1, 1), new(10, 0, 0)),
                Hull("a", Centre(3, 0, 0, 1, 1, 1), new(-10, 0, 0)),
                Hull("m", Centre(0, 10, 0, 1, 1, 1), new(0, -20, 0))
            };
            CollisionProxy[] terrain = { Terrain("ground", Centre(0, 0, 0, 20, 0.5, 20)) };

            CollisionShadowContact[] forward = CollisionShadowEvaluator
                .Evaluate(hulls, terrain, 0.25).Contacts.ToArray();
            CollisionShadowContact[] reverse = CollisionShadowEvaluator
                .Evaluate(hulls.Reverse().ToArray(), terrain.Reverse().ToArray(), 0.25).Contacts.ToArray();

            Assert.Equal(forward, reverse);
            Assert.NotEmpty(forward);
        }

        [Fact]
        public void Separating_and_parallel_proxies_do_not_false_positive()
        {
            CollisionProxy hull = Hull("ship", Centre(0, 5, 0, 1, 1, 1), new(-60, 0, 0));
            CollisionProxy wall = Terrain("wall", Centre(10, 0, 0, 0.25, 100, 100));

            Assert.Empty(CollisionShadowEvaluator.Evaluate(new[] { hull }, new[] { wall }, 0.25).Contacts);
        }

        [Fact]
        public void Invalid_and_adversarial_geometry_is_rejected_without_contacts()
        {
            CollisionProxy[] bad =
            {
                Hull("nan", new CollisionAabb(new(double.NaN, 0, 0), new(1, 1, 1)), ShadowVector3.Zero),
                Hull("reverse", new CollisionAabb(new(2, 2, 2), new(1, 1, 1)), ShadowVector3.Zero),
                Hull("fast", Centre(0, 0, 0, 1, 1, 1), new(251, 0, 0)),
                Hull("huge", Centre(0, 0, 0, 513, 1, 1), ShadowVector3.Zero),
                new CollisionProxy("wrong-kind", CollisionProxyKind.IslandTerrain,
                    Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero),
                Hull("duplicate", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero),
                Hull("duplicate", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero)
            };

            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(bad,
                new[] { Terrain("terrain", Centre(0, 0, 0, 10, 10, 10)) }, 0.02);

            Assert.Single(result.Contacts);
            Assert.Equal(1, result.Telemetry.AcceptedDynamicCount);
            Assert.Equal(6, result.Telemetry.RejectedProxyCount);
        }

        [Fact]
        public void Moving_terrain_and_cross_kind_duplicate_ids_are_rejected()
        {
            CollisionProxy hull = Hull("same", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero);
            CollisionProxy movingTerrain = new("moving", CollisionProxyKind.IslandTerrain,
                Centre(0, 0, 0, 2, 2, 2), new ShadowVector3(1, 0, 0));
            CollisionProxy duplicateTerrain = Terrain("same", Centre(0, 0, 0, 2, 2, 2));

            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(
                new[] { hull }, new[] { movingTerrain, duplicateTerrain }, 0.02);

            Assert.Empty(result.Contacts);
            Assert.Equal(0, result.Telemetry.AcceptedTerrainCount);
            Assert.Equal(2, result.Telemetry.RejectedProxyCount);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-0.02)]
        [InlineData(0.251)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Invalid_step_rejects_the_batch(double step)
        {
            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(
                new[] { Hull("ship", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero) },
                Array.Empty<CollisionProxy>(), step);

            Assert.Empty(result.Contacts);
            Assert.True(result.Telemetry.HardInputRejected);
        }

        [Fact]
        public void Dynamic_and_contact_work_is_strictly_capped()
        {
            CollisionProxy[] hulls = Enumerable.Range(0, CollisionShadowLimits.MaxDynamicProxies + 50)
                .Select(i => Hull("ship-" + i.ToString("D4"), Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero))
                .ToArray();

            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(
                hulls, Array.Empty<CollisionProxy>(), 0.02);

            Assert.Equal(CollisionShadowLimits.MaxDynamicProxies, result.Telemetry.AcceptedDynamicCount);
            Assert.True(result.Telemetry.DynamicCapReached);
            Assert.True(result.Telemetry.ContactCapReached);
            Assert.Equal(CollisionShadowLimits.MaxContacts, result.Contacts.Count);
            Assert.True(result.Telemetry.NarrowphaseTestCount <= CollisionShadowLimits.MaxCandidatePairs);
        }

        [Fact]
        public void Hard_input_cap_avoids_unbounded_sort_or_pair_work()
        {
            CollisionProxy proxy = Hull("ship", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero);
            CollisionProxy[] tooMany = Enumerable.Repeat(proxy, CollisionShadowLimits.HardInputCount + 1).ToArray();

            CollisionShadowResult result = CollisionShadowEvaluator.Evaluate(
                tooMany, Array.Empty<CollisionProxy>(), 0.02);

            Assert.True(result.Telemetry.HardInputRejected);
            Assert.Empty(result.Contacts);
            Assert.Equal(0, result.Telemetry.BroadphaseCandidateCount);
        }

        [Fact]
        public void Telemetry_compares_shadow_to_current_authoritative_contact_count()
        {
            CollisionProxy hull = Hull("ship", Centre(0, 0, 0, 1, 1, 1), ShadowVector3.Zero);
            CollisionProxy terrain = Terrain("terrain", Centre(0, 0, 0, 2, 2, 2));

            CollisionShadowTelemetry telemetry = CollisionShadowEvaluator
                .Evaluate(new[] { hull }, new[] { terrain }, 0.02, currentAuthoritativeContactCount: 0)
                .Telemetry;

            Assert.Equal(1, telemetry.TerrainContactCount);
            Assert.Equal(0, telemetry.CurrentAuthoritativeContactCount);
            Assert.Equal(1, telemetry.ShadowOnlyContactCount);
        }

        private static CollisionAabb Centre(double x, double y, double z, double hx, double hy, double hz) =>
            CollisionAabb.FromCentreHalfExtents(new ShadowVector3(x, y, z), new ShadowVector3(hx, hy, hz));

        private static CollisionProxy Hull(string id, CollisionAabb bounds, ShadowVector3 velocity) =>
            new(id, CollisionProxyKind.ShipHull, bounds, velocity);

        private static CollisionProxy Terrain(string id, CollisionAabb bounds) =>
            new(id, CollisionProxyKind.IslandTerrain, bounds, ShadowVector3.Zero);
    }
}
