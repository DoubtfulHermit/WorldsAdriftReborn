using System;
using System.Collections.Generic;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class DockingRuntimeTests
    {
        private static readonly DockingPose Target = new(100, 20, -50, 0.5);

        [Fact]
        public void Runtime_is_default_off_and_does_not_claim_or_publish()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var runtime = new DockingRuntime(200, claims, port);

            DockingRuntimeResult result = runtime.TryBeginApproach(Request(1), Clear(1));

            Assert.Equal(DockingRuntimeDisposition.Off, result.Disposition);
            Assert.False(claims.IsShipyardOccupied(100));
            Assert.Empty(port.Commits);
        }

        [Fact]
        public void Approach_claim_persistence_and_component_projection_commit_together()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var runtime = Enabled(claims, port);

            DockingRuntimeResult result = runtime.TryBeginApproach(Request(1), Clear(1));

            Assert.Equal(DockingRuntimeDisposition.Committed, result.Disposition);
            Assert.Equal(200, claims.DockedShipFor(100));
            DockingRuntimeCommit commit = Assert.Single(port.Commits);
            Assert.Equal((int)DockingPhase.Approaching, commit.Snapshot.Phase);
            Assert.True(commit.Components.ApproachingDock);
            Assert.False(commit.Components.Docked);
            Assert.Equal(200, commit.Components.YardDockedHullEntityId);
            Assert.False(commit.FreezeVelocity);
        }

        [Fact]
        public void Failed_transaction_rolls_back_exact_claim_and_lifecycle()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort { Accept = false };
            var runtime = Enabled(claims, port);

            DockingRuntimeResult result = runtime.TryBeginApproach(Request(1), Clear(1));

            Assert.Equal(DockingRuntimeDisposition.TransactionRolledBack, result.Disposition);
            Assert.Equal(DockingPhase.Undocked, runtime.Lifecycle.Phase);
            Assert.False(claims.IsShipyardOccupied(100));
        }

        [Fact]
        public void Capture_freeze_and_docked_components_use_increasing_same_generation_steps()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var runtime = Enabled(claims, port);
            Assert.Equal(DockingRuntimeDisposition.Committed,
                runtime.TryBeginApproach(Request(1, new DockingPose(104, 20, -50, 0)), Clear(1)).Disposition);

            DockingRuntimeResult capture = runtime.Step(Frame(2, runtime.Lifecycle.Pose), Clear(2));
            Assert.Equal(DockingPhase.Captured, capture.Phase);
            Assert.True(capture.FreezeVelocity);
            Assert.True(port.Commits[^1].Components.Docked);

            long step = 3;
            while (runtime.Lifecycle.Phase != DockingPhase.Docked && step < 200)
            {
                runtime.Step(Frame(step, runtime.Lifecycle.Pose), Clear(step));
                step++;
            }
            Assert.Equal(DockingPhase.Docked, runtime.Lifecycle.Phase);
            Assert.All(port.Commits, c => Assert.Equal(9, c.Stamp.AuthorityGeneration));
        }

        [Fact]
        public void Duplicate_stale_and_foreign_generation_frames_fail_closed()
        {
            var runtime = Enabled(new ShipDockRegistry(), new RecordingPort());
            Assert.Equal(DockingRuntimeDisposition.Committed,
                runtime.TryBeginApproach(Request(10), Clear(10)).Disposition);

            Assert.Equal(DockingRuntimeDisposition.RejectedStampMismatch,
                runtime.Step(Frame(10, runtime.Lifecycle.Pose), Clear(10)).Disposition);
            Assert.Equal(DockingRuntimeDisposition.RejectedStampMismatch,
                runtime.Step(Frame(11, runtime.Lifecycle.Pose), Clear(11, generation: 10)).Disposition);
        }

        [Fact]
        public void Departure_retains_claim_until_clear_then_publishes_both_unlinks()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var runtime = Enabled(claims, port);
            runtime.TryBeginApproach(Request(1, Target), Clear(1));
            runtime.Step(Frame(2, Target), Clear(2));
            for (long step = 3; runtime.Lifecycle.Phase != DockingPhase.Docked; step++)
                runtime.Step(Frame(step, runtime.Lifecycle.Pose), Clear(step));

            long next = port.Commits[^1].Stamp.FixedStep + 1;
            DockingRuntimeResult leaving = runtime.Step(
                Frame(next, Target, DockingPropulsion.Sail, outside: false), Clear(next));
            Assert.Equal(DockingPhase.Departing, leaving.Phase);
            Assert.Equal(200, claims.DockedShipFor(100));

            DockingRuntimeResult clear = runtime.Step(
                Frame(next + 1, Target, DockingPropulsion.Sail, outside: true), Clear(next + 1));
            Assert.True(clear.LinkReleased);
            Assert.Equal(DockingPhase.Undocked, clear.Phase);
            Assert.False(claims.IsShipyardOccupied(100));
            Assert.Equal(0, port.Commits[^1].Components.YardEntityId);
            Assert.Equal(0, port.Commits[^1].Components.YardDockedHullEntityId);
        }

        [Fact]
        public void Restart_uses_stable_snapshot_and_fresh_runtime_ids()
        {
            var source = Enabled(new ShipDockRegistry(), new RecordingPort());
            source.TryBeginApproach(Request(1, Target), Clear(1));
            source.Step(Frame(2, Target), Clear(2));
            DockingSnapshotV1 durable = source.Lifecycle.CaptureSnapshot();
            string json = JsonSerializer.Serialize(new BuiltShipRecord { DockingSnapshot = durable });
            BuiltShipRecord restoredRecord = JsonSerializer.Deserialize<BuiltShipRecord>(json)!;

            var freshClaims = new ShipDockRegistry();
            var port = new RecordingPort();
            var restored = new DockingRuntime(900, freshClaims, port,
                new DockingRuntimeOptions { Enabled = true });
            Assert.True(restored.TryRestore(restoredRecord.DockingSnapshot!, 700,
                new FlightAuthorityStamp(50, 10)));
            Assert.Equal(900, freshClaims.DockedShipFor(700));
            Assert.Equal(700, restored.Lifecycle.YardEntityId);
            Assert.DoesNotContain("\"HullEntityId\"", JsonSerializer.Serialize(durable));
        }

        [Fact]
        public void Restore_conflict_and_transaction_failure_leave_no_stale_claim()
        {
            DockingSnapshotV1 snapshot = CapturedSnapshot();
            var occupied = new ShipDockRegistry();
            occupied.TryClaim(700, 901);
            var conflict = new DockingRuntime(900, occupied, new RecordingPort(),
                new DockingRuntimeOptions { Enabled = true });
            Assert.False(conflict.TryRestore(snapshot, 700, new FlightAuthorityStamp(1, 1)));
            Assert.Equal(901, occupied.DockedShipFor(700));

            var claims = new ShipDockRegistry();
            var failed = new DockingRuntime(900, claims,
                new RecordingPort { Accept = false },
                new DockingRuntimeOptions { Enabled = true });
            Assert.False(failed.TryRestore(snapshot, 700, new FlightAuthorityStamp(1, 1)));
            Assert.False(claims.IsShipyardOccupied(700));
        }

        [Fact]
        public void Impossible_exact_rollback_fails_closed_to_undocked_without_throwing()
        {
            var claims = new ShipDockRegistry();
            var port = new RecordingPort();
            var runtime = Enabled(claims, port);
            runtime.TryBeginApproach(Request(1, Target), Clear(1));
            runtime.Step(Frame(2, Target), Clear(2));
            Assert.Equal(DockingPhase.Captured, runtime.Lifecycle.Phase);

            // Externally break the claim and give the yard to another hull, then
            // fail the transaction: the exact prior state cannot be restored.
            Assert.True(claims.Release(100, 200));
            Assert.Equal(ShipDockClaimResult.Claimed, claims.TryClaim(100, 999));
            port.Accept = false;

            DockingRuntimeResult result = runtime.Step(Frame(3, Target), Clear(3));

            Assert.Equal(DockingRuntimeDisposition.TransactionRolledBack, result.Disposition);
            Assert.Equal(DockingPhase.Undocked, runtime.Lifecycle.Phase);
            Assert.Equal(999, claims.DockedShipFor(100));
        }

        [Fact]
        public void Restore_commit_failure_keeps_preexisting_legacy_claim()
        {
            DockingSnapshotV1 snapshot = CapturedSnapshot();
            var claims = new ShipDockRegistry();
            Assert.Equal(ShipDockClaimResult.Claimed, claims.TryClaim(700, 900));
            var runtime = new DockingRuntime(900, claims,
                new RecordingPort { Accept = false },
                new DockingRuntimeOptions { Enabled = true });

            Assert.False(runtime.TryRestore(snapshot, 700, new FlightAuthorityStamp(1, 1)));
            Assert.Equal(900, claims.DockedShipFor(700));
        }

        private static DockingRuntime Enabled(ShipDockRegistry claims, RecordingPort port) =>
            new(200, claims, port, new DockingRuntimeOptions { Enabled = true });

        private static DockingApproachRequest Request(long step,
            DockingPose? pose = null) => new(200, 100, "ship:stable", "yard:stable",
                "owner", "owner", false, false, true, true,
                Clear(step).Clearance, pose ?? new DockingPose(104, 20, -50, 0),
                Target, new DockingMotion(0, 0, 0, 0));

        private static StampedCollisionClearance Clear(long step, long generation = 9) =>
            new(new CollisionClearanceRecord("ship:stable", "yard:stable", step, 0, true),
                new FlightAuthorityStamp(step, generation));

        private static DockingFrame Frame(long step, DockingPose pose,
            DockingPropulsion propulsion = DockingPropulsion.None, bool outside = false) =>
            new(0.02, true, true, propulsion, Clear(step).Clearance, outside,
                pose, DockingMotion.Frozen);

        private static DockingSnapshotV1 CapturedSnapshot()
        {
            var runtime = Enabled(new ShipDockRegistry(), new RecordingPort());
            runtime.TryBeginApproach(Request(1, Target), Clear(1));
            runtime.Step(Frame(2, Target), Clear(2));
            return runtime.Lifecycle.CaptureSnapshot();
        }

        private sealed class RecordingPort : IDockingRuntimeTransaction
        {
            public bool Accept { get; set; } = true;
            public List<DockingRuntimeCommit> Commits { get; } = new();
            public bool TryCommit(DockingRuntimeCommit commit)
            {
                Commits.Add(commit);
                return Accept;
            }
        }
    }
}
