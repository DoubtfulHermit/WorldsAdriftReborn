using System.Text;
using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Domains
{
    public sealed class DomainWorkerProtocolTests
    {
        private static readonly SimulationDomainId Domain = new("ship:70");
        private static readonly WorkerId WorkerA = new("local:a");
        private static readonly WorkerId WorkerB = new("worker:b");
        private const string DigestA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        private const string DigestB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

        [Fact]
        public void Gate_accepts_ordered_commands_and_retries_idempotently()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var gate = new DomainCommandGate(stamp, replayWindow: 2);
            var first = new DomainCommand("cmd-1", stamp, 1, DigestA);

            Assert.Equal(DomainCommandDisposition.Accepted, gate.Admit(first));
            Assert.Equal(DomainCommandDisposition.Duplicate, gate.Admit(first));
            Assert.Equal(DomainCommandDisposition.IdempotencyConflict,
                gate.Admit(first with { PayloadSha256 = DigestB }));
            Assert.Equal(DomainCommandDisposition.OutOfOrder,
                gate.Admit(new DomainCommand("cmd-3", stamp, 3, DigestA)));
            Assert.Equal(1, gate.LastSequence);
        }

        [Fact]
        public void Same_command_id_at_a_different_sequence_is_not_an_exact_retry()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var gate = new DomainCommandGate(stamp);
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("cmd-1", stamp, 1, DigestA)));

            Assert.Equal(DomainCommandDisposition.IdempotencyConflict,
                gate.Admit(new DomainCommand("cmd-1", stamp, 2, DigestA)));
            Assert.Equal(1, gate.LastSequence);
        }

        [Fact]
        public void Replay_memory_is_bounded()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var gate = new DomainCommandGate(stamp, replayWindow: 2);
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("cmd-1", stamp, 1, DigestA)));
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("cmd-2", stamp, 2, DigestA)));
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("cmd-3", stamp, 3, DigestA)));

            Assert.Equal(2, gate.ReplayEntryCount);
            Assert.Equal(DomainCommandDisposition.OutOfOrder,
                gate.Admit(new DomainCommand("cmd-1", stamp, 1, DigestA)));
        }

        [Fact]
        public void Authority_transfer_rejects_old_worker_and_generation()
        {
            var oldStamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var nextStamp = new DomainAuthorityStamp(Domain, WorkerB,
                AuthorityGeneration.Initial.Next());
            var gate = new DomainCommandGate(oldStamp, lastSequence: 8);

            gate.Transfer(nextStamp, restoredSequence: 8);

            Assert.Equal(DomainCommandDisposition.StaleAuthority,
                gate.Admit(new DomainCommand("old", oldStamp, 9, DigestA)));
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("new", nextStamp, 9, DigestA)));
            Assert.Throws<InvalidOperationException>(() => gate.Transfer(
                nextStamp with { WorkerId = WorkerA }, 9));
        }

        [Fact]
        public void Wrong_worker_cannot_write_even_with_current_generation()
        {
            var authority = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var forged = authority with { WorkerId = WorkerB };
            var gate = new DomainCommandGate(authority);

            Assert.Equal(DomainCommandDisposition.WrongWorker,
                gate.Admit(new DomainCommand("forged", forged, 1, DigestA)));
        }

        [Fact]
        public void Snapshot_is_copied_capped_and_digest_verified()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            byte[] bytes = Encoding.UTF8.GetBytes("snapshot-state");
            CommittedDomainSnapshot snapshot = CommittedDomainSnapshot.Create(stamp, 5, bytes);
            bytes[0] = (byte)'X';

            Assert.True(snapshot.Verify());
            Assert.Equal("snapshot-state", Encoding.UTF8.GetString(snapshot.Payload.Span));
            Assert.Throws<ArgumentOutOfRangeException>(() => CommittedDomainSnapshot.Create(
                stamp, 0, new byte[CommittedDomainSnapshot.MaxPayloadBytes + 1]));
        }

        [Fact]
        public void Kill_restore_ready_promote_advances_generation_once()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            CommittedDomainSnapshot snapshot = CommittedDomainSnapshot.Create(
                stamp, 12, Encoding.UTF8.GetBytes("safe-state"));
            var model = new DomainRecoveryModel(stamp);
            model.Commit(snapshot);

            model.Revoke(WorkerA);
            model.BeginRestore(WorkerB);
            model.MarkReady(WorkerB, snapshot.Sha256.ToLowerInvariant());
            DomainAuthorityStamp promoted = model.Promote(WorkerB);

            Assert.Equal(DomainRecoveryPhase.Active, model.Phase);
            Assert.Equal(2, promoted.Generation.Value);
            Assert.Equal(WorkerB, promoted.WorkerId);
            Assert.Throws<InvalidOperationException>(() => model.Promote(WorkerB));
        }

        [Fact]
        public void Takeover_requires_commit_revoke_candidate_and_matching_digest()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var model = new DomainRecoveryModel(stamp);

            Assert.Throws<InvalidOperationException>(() => model.BeginRestore(WorkerB));
            model.Commit(CommittedDomainSnapshot.Create(stamp, 1,
                Encoding.UTF8.GetBytes("state")));
            Assert.Throws<InvalidOperationException>(() => model.BeginRestore(WorkerB));
            model.Revoke(WorkerA);
            Assert.Throws<InvalidOperationException>(() => model.BeginRestore(WorkerA));
            model.BeginRestore(WorkerB);
            Assert.Throws<InvalidOperationException>(() => model.MarkReady(WorkerB, DigestA));
            Assert.Equal(DomainRecoveryPhase.Restoring, model.Phase);
        }

        [Fact]
        public void Partition_heal_cannot_resurrect_revoked_generation()
        {
            var oldStamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            CommittedDomainSnapshot snapshot = CommittedDomainSnapshot.Create(
                oldStamp, 2, Encoding.UTF8.GetBytes("state"));
            var model = new DomainRecoveryModel(oldStamp);
            model.Commit(snapshot);
            model.Revoke(WorkerA); // coordinator treats loss/partition identically
            model.BeginRestore(WorkerB);
            model.MarkReady(WorkerB, snapshot.Sha256);
            DomainAuthorityStamp current = model.Promote(WorkerB);
            var gate = new DomainCommandGate(current, snapshot.ReplicationSequence);

            Assert.Equal(DomainCommandDisposition.StaleAuthority,
                gate.Admit(new DomainCommand("healed-old-worker", oldStamp, 3, DigestA)));
            Assert.Equal(DomainCommandDisposition.Accepted,
                gate.Admit(new DomainCommand("current-worker", current, 3, DigestA)));
        }

        [Fact]
        public void Same_sequence_cannot_commit_conflicting_snapshot_state()
        {
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var model = new DomainRecoveryModel(stamp);
            model.Commit(CommittedDomainSnapshot.Create(stamp, 4,
                Encoding.UTF8.GetBytes("state-a")));

            Assert.Throws<InvalidOperationException>(() => model.Commit(
                CommittedDomainSnapshot.Create(stamp, 4, Encoding.UTF8.GetBytes("state-b"))));
        }

        [Fact]
        public void Second_failure_cannot_restore_snapshot_from_previous_generation()
        {
            var first = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            CommittedDomainSnapshot snapshot = CommittedDomainSnapshot.Create(
                first, 1, Encoding.UTF8.GetBytes("state"));
            var model = new DomainRecoveryModel(first);
            model.Commit(snapshot);
            model.Revoke(WorkerA);
            model.BeginRestore(WorkerB);
            model.MarkReady(WorkerB, snapshot.Sha256);
            model.Promote(WorkerB);

            model.Revoke(WorkerB);
            Assert.Throws<InvalidOperationException>(() => model.BeginRestore(WorkerA));
        }

        [Fact]
        public void Protocol_identifiers_and_replay_inputs_are_bounded_and_log_safe()
        {
            Assert.Throws<ArgumentException>(() => new WorkerId("worker\nforged-log"));
            var stamp = new DomainAuthorityStamp(Domain, WorkerA, AuthorityGeneration.Initial);
            var gate = new DomainCommandGate(stamp);
            Assert.Throws<ArgumentException>(() => gate.Admit(
                new DomainCommand("cmd\nforged-log", stamp, 1, DigestA)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DomainCommandGate(
                stamp, replayWindow: DomainCommandGate.MaxReplayWindow + 1));
        }
    }
}
