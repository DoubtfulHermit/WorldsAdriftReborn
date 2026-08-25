using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class FlightShadowIntegrationTests
    {
        [Fact]
        public void Vector_vertical_force_and_lift_apply_gravity_exactly_once()
        {
            VectorRigidBodyShadowResult forces = Forces(1000, new ShadowVector3(0, 1000, 0));
            LiftGravityInput lift = new(1000, 1100, -10, 0, 0, 0.02);
            IntegratedFlightShadowInput input = new("ship:one", Motion(), forces, lift);

            Assert.True(IntegratedFlightShadow.TryStep(input, out IntegratedFlightShadowResult step));
            Assert.Equal(1000, step.Lift.NetVerticalForceNewtons, 9);
            Assert.Equal(1, step.Lift.VerticalAccelerationMps2, 9);
            Assert.Equal(0.02, step.NextVelocityMetresPerSecond.Y, 9);
            Assert.Contains("gravity supplied once", step.Provenance, StringComparison.Ordinal);
        }

        [Fact]
        public void Integrated_velocity_is_used_for_collision_sweep_after_force_composition()
        {
            VectorRigidBodyShadowResult forces = Forces(100, new ShadowVector3(25000, 0, 0));
            LiftGravityInput lift = new(100, 110, -10, 0, 0, 0.02);
            CollisionProxy wall = Terrain("terrain:wall", new ShadowVector3(1.15, 0, 0),
                new ShadowVector3(0.1, 2, 2));
            IntegratedFlightShadowInput input = new("ship:one", Motion(), forces, lift,
                terrain: new[] { wall });

            Assert.True(IntegratedFlightShadow.TryStep(input, out IntegratedFlightShadowResult step));
            Assert.Equal(5, step.NextVelocityMetresPerSecond.X, 9);
            CollisionShadowContact contact = Assert.Single(step.Collision.Contacts);
            Assert.Equal("ship:one", contact.FirstId);
            Assert.Equal("terrain:wall", contact.SecondId);
            Assert.False(contact.InitialOverlap);
        }

        [Fact]
        public void External_vertical_force_input_is_rejected_to_close_double_gravity_seam()
        {
            LiftGravityInput ambiguous = new(1000, 1100, -10, 0, 0, 0.02,
                externalVerticalForceNewtons: -10000);
            Assert.False(IntegratedFlightShadow.TryStep(
                new IntegratedFlightShadowInput("ship:one", Motion(),
                    Forces(1000, ShadowVector3.Zero), ambiguous), out _));
        }

        [Fact]
        public void Docking_clearance_ignores_expected_capture_volume_but_blocks_other_contacts()
        {
            CollisionProxy ship = Hull("ship:stable", new ShadowVector3(0, 0, 0),
                new ShadowVector3(1, 1, 1));
            CollisionProxy yard = Terrain("yard:stable", new ShadowVector3(0, 0, 0),
                new ShadowVector3(2, 2, 2));
            CollisionProxy rock = Terrain("terrain:rock", new ShadowVector3(0, 0, 0),
                new ShadowVector3(3, 3, 3));

            CollisionClearanceRecord clear = CollisionClearanceRecord.From(
                CollisionShadowEvaluator.Evaluate(new[] { ship }, new[] { yard }, 0.02),
                "ship:stable", "yard:stable", 7);
            CollisionClearanceRecord blocked = CollisionClearanceRecord.From(
                CollisionShadowEvaluator.Evaluate(new[] { ship }, new[] { yard, rock }, 0.02),
                "ship:stable", "yard:stable", 8);

            Assert.True(clear.IsClear);
            Assert.False(blocked.IsClear);
            Assert.Equal(1, blocked.BlockingContactCount);
            Assert.True(BeginDock(clear));
            Assert.False(BeginDock(blocked));
        }

        /// <summary>
        /// THE LIVE ISLAND-ENVELOPE BLOCKER. Island terrain proxies are conservative
        /// AABB envelopes and an island-placed shipyard is by construction inside its
        /// island's envelope, so beside such a yard every approach used to fail
        /// closed as CollisionBlocked - the transactional docking path could not be
        /// switched on anywhere real. The yard's own bubble is the reviewed dock
        /// volume: a TERRAIN contact inside it says nothing about the air the hull is
        /// in and is not counted. Everything else still blocks.
        /// </summary>
        [Fact]
        public void An_island_envelope_inside_the_reviewed_dock_volume_is_not_an_obstruction()
        {
            CollisionProxy ship = Hull("ship:stable", new ShadowVector3(0, 0, 0),
                new ShadowVector3(1, 1, 1));
            // The island the yard stands on: a huge box that swallows the yard.
            CollisionProxy island = Terrain("island:home", new ShadowVector3(0, -100, 0),
                new ShadowVector3(400, 200, 400));
            CollisionShadowResult swept = CollisionShadowEvaluator.Evaluate(
                new[] { ship }, new[] { island }, 0.02);

            CollisionClearanceRecord withoutVolume = CollisionClearanceRecord.From(
                swept, "ship:stable", "yard:stable", 11);
            CollisionClearanceRecord withVolume = CollisionClearanceRecord.From(
                swept, "ship:stable", "yard:stable", 11, OriginBubble);

            Assert.False(withoutVolume.IsClear);
            Assert.Equal(1, withoutVolume.BlockingContactCount);
            Assert.True(withVolume.IsClear);
            Assert.Equal(0, withVolume.BlockingContactCount);
            Assert.True(BeginDock(withVolume));
        }

        /// <summary>
        /// The exemption is narrow on purpose. It never launders a truncated sweep
        /// into clearance, never exempts another SHIP, and never reaches outside the
        /// volume: the fail-closed rules are untouched.
        /// </summary>
        [Fact]
        public void The_reviewed_dock_volume_never_launders_truncation_hulls_or_distant_terrain()
        {
            CollisionProxy ship = Hull("ship:stable", new ShadowVector3(0, 0, 0),
                new ShadowVector3(1, 1, 1));

            // Another ship sitting in the same volume still blocks.
            CollisionProxy other = Hull("ship:other", new ShadowVector3(0, 0, 0),
                new ShadowVector3(1, 1, 1));
            CollisionClearanceRecord hullContact = CollisionClearanceRecord.From(
                CollisionShadowEvaluator.Evaluate(new[] { ship, other },
                    Array.Empty<CollisionProxy>(), 0.02),
                "ship:stable", "yard:stable", 12, OriginBubble);
            Assert.False(hullContact.IsClear);
            Assert.Equal(1, hullContact.BlockingContactCount);

            // Terrain the hull is touching OUTSIDE the volume still blocks: this
            // cliff face is 44 m from the yard, past the 35 m influence sphere.
            CollisionProxy farCliff = Terrain("island:far", new ShadowVector3(48, 0, 0),
                new ShadowVector3(4, 4, 4));
            CollisionProxy movingShip = new("ship:stable", CollisionProxyKind.ShipHull,
                CollisionAabb.FromCentreHalfExtents(new ShadowVector3(40, 0, 0),
                    new ShadowVector3(1, 1, 1)),
                new ShadowVector3(200, 0, 0));
            CollisionClearanceRecord distantTerrain = CollisionClearanceRecord.From(
                CollisionShadowEvaluator.Evaluate(new[] { movingShip },
                    new[] { farCliff }, 0.02),
                "ship:stable", "yard:stable", 13, OriginBubble);
            Assert.False(distantTerrain.IsClear);
            Assert.Equal(1, distantTerrain.BlockingContactCount);

            // And a truncated sweep is still incomplete, volume or not.
            CollisionProxy[] hulls = Enumerable.Range(0, CollisionShadowLimits.MaxDynamicProxies + 1)
                .Select(index => Hull("ship:" + index, ShadowVector3.Zero,
                    new ShadowVector3(1, 1, 1))).ToArray();
            CollisionClearanceRecord truncated = CollisionClearanceRecord.From(
                CollisionShadowEvaluator.Evaluate(hulls, Array.Empty<CollisionProxy>(), 0.02),
                "ship:0", "yard:stable", 14, OriginBubble);
            Assert.False(truncated.EvaluationComplete);
            Assert.False(truncated.IsClear);
        }

        [Fact]
        public void Truncated_collision_work_can_never_issue_clearance()
        {
            CollisionProxy[] hulls = Enumerable.Range(0, CollisionShadowLimits.MaxDynamicProxies + 1)
                .Select(index => Hull("ship:" + index, ShadowVector3.Zero,
                    new ShadowVector3(1, 1, 1))).ToArray();
            CollisionShadowResult collision = CollisionShadowEvaluator.Evaluate(
                hulls, Array.Empty<CollisionProxy>(), 0.02);
            CollisionClearanceRecord clearance = CollisionClearanceRecord.From(collision,
                "ship:0", "yard:stable", 1);

            Assert.True(collision.Telemetry.DynamicCapReached);
            Assert.False(clearance.EvaluationComplete);
            Assert.False(clearance.IsClear);
        }

        [Fact]
        public void Docking_snapshot_uses_stable_keys_and_contains_no_runtime_entity_ids()
        {
            CollisionClearanceRecord clearance = new("ship:stable", "yard:stable", 1, 0, true);
            var lifecycle = new AuthenticDockingLifecycle(99001);
            Assert.True(lifecycle.TryBeginApproach(new DockingApproachRequest(
                99001, 88001, "ship:stable", "yard:stable", "owner", "owner",
                false, false, true, true, clearance,
                new DockingPose(0, 0, 0, 0), new DockingPose(0, 0, 0, 0), default,
                OriginBubble), new ShipDockRegistry(), out _));

            string json = JsonSerializer.Serialize(lifecycle.CaptureSnapshot());
            Assert.Contains("ship:stable", json, StringComparison.Ordinal);
            Assert.Contains("yard:stable", json, StringComparison.Ordinal);
            Assert.DoesNotContain("99001", json, StringComparison.Ordinal);
            Assert.DoesNotContain("88001", json, StringComparison.Ordinal);
            Assert.DoesNotContain("EntityId", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Docking_dto_is_a_nullable_sibling_and_does_not_change_track2_v1()
        {
            var flight = new DurableShipFlightSnapshot { Version = 1, AuthorityGeneration = 9 };
            string legacyJson = JsonSerializer.Serialize(new CombinedSnapshotEnvelope
                { Flight = flight });
            CombinedSnapshotEnvelope legacy = JsonSerializer.Deserialize<CombinedSnapshotEnvelope>(
                legacyJson)!;
            Assert.NotNull(legacy.Flight);
            Assert.Null(legacy.Docking);
            Assert.Equal(1, DurableShipFlightSnapshot.CurrentVersion);

            var combined = new CombinedSnapshotEnvelope
            {
                Flight = flight,
                Docking = new DockingSnapshotV1
                {
                    HullStableKey = "ship:stable",
                    YardStableKey = "yard:stable"
                }
            };
            CombinedSnapshotEnvelope roundTrip = JsonSerializer.Deserialize<CombinedSnapshotEnvelope>(
                JsonSerializer.Serialize(combined))!;
            Assert.Equal(1, roundTrip.Flight!.Version);
            Assert.Equal("yard:stable", roundTrip.Docking!.YardStableKey);
        }

        [Fact]
        public void Pure_shadow_sources_have_zero_live_flight_session_references()
        {
            string root = FindRepositoryRoot();
            string[] pureSources =
            {
                "VectorRigidBodyShadow.cs", "RetailLiftGravityShadow.cs",
                "CollisionShadow.cs", "IntegratedFlightShadow.cs"
            };
            foreach (string source in pureSources)
            {
                string text = File.ReadAllText(Path.Combine(root,
                    "WorldsAdriftRebornGameServer.Multiplayer", "Ship", "Flight", source));
                Assert.DoesNotContain("FlightSession.", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ShipFlightService", text, StringComparison.Ordinal);
                Assert.DoesNotContain("ComponentCollection", text, StringComparison.Ordinal);
                Assert.DoesNotContain("SendToAll", text, StringComparison.Ordinal);
            }
        }

        private static bool BeginDock(CollisionClearanceRecord clearance)
        {
            var lifecycle = new AuthenticDockingLifecycle(200);
            return lifecycle.TryBeginApproach(new DockingApproachRequest(
                200, 100, "ship:stable", "yard:stable", "owner", "owner", false,
                false, true, true, clearance, new DockingPose(0, 0, 0, 0),
                new DockingPose(0, 0, 0, 0), default, OriginBubble),
                new ShipDockRegistry(), out _);
        }

        /// <summary>A yard at the world origin, so a hull at (0,0,0) sits in its dome.</summary>
        private static readonly ShipyardBubble OriginBubble =
            new DockingTuning().BubbleAt(ShadowVector3.Zero);

        private static ShadowMotionState Motion() => new(ShadowVector3.Zero,
            ShadowVector3.Zero, new ShadowVector3(1, 1, 1));

        private static VectorRigidBodyShadowResult Forces(double mass, ShadowVector3 force) =>
            new(new ShadowMassProperties(mass, ShadowVector3.Zero,
                    new ShadowVector3(1, 1, 1), true),
                force, ShadowVector3.Zero, ShadowVector3.Zero, 0, 0);

        private static CollisionProxy Hull(string id, ShadowVector3 centre,
            ShadowVector3 halfExtents) => new(id, CollisionProxyKind.ShipHull,
                CollisionAabb.FromCentreHalfExtents(centre, halfExtents), ShadowVector3.Zero);

        private static CollisionProxy Terrain(string id, ShadowVector3 centre,
            ShadowVector3 halfExtents) => new(id, CollisionProxyKind.IslandTerrain,
                CollisionAabb.FromCentreHalfExtents(centre, halfExtents), ShadowVector3.Zero);

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName,
                       "WorldsAdriftReborn.sln"))) directory = directory.Parent;
            return Assert.IsType<DirectoryInfo>(directory).FullName;
        }

        private sealed class CombinedSnapshotEnvelope
        {
            public DurableShipFlightSnapshot? Flight { get; set; }
            public DockingSnapshotV1? Docking { get; set; }
        }
    }
}
