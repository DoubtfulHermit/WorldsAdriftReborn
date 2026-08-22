using System.Security.Cryptography;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    /// <summary>
    /// Pure, transport-independent contracts for a future worker boundary. Nothing
    /// in this file opens a socket or moves live authority.
    /// </summary>
    public readonly record struct WorkerId
    {
        public const int MaxLength = 64;

        public WorkerId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("worker id is required", nameof(value));
            string canonical = value.Trim();
            if (canonical.Length > MaxLength)
                throw new ArgumentOutOfRangeException(nameof(value), "worker id is too long");
            if (!canonical.All(ProtocolValidation.IdentifierCharacter))
                throw new ArgumentException("worker id contains unsafe characters", nameof(value));
            Value = canonical;
        }

        public string Value { get; }
        public override string ToString() => Value;
    }

    public readonly record struct DomainAuthorityStamp(
        SimulationDomainId DomainId, WorkerId WorkerId, AuthorityGeneration Generation);

    public sealed class CommittedDomainSnapshot
    {
        public const int CurrentVersion = 1;
        public const int MaxPayloadBytes = 8 * 1024 * 1024;

        private readonly byte[] _payload;

        private CommittedDomainSnapshot(DomainAuthorityStamp authority, long replicationSequence,
            byte[] payload, byte[] digest)
        {
            Authority = authority;
            ReplicationSequence = replicationSequence;
            _payload = payload;
            Sha256 = Convert.ToHexString(digest);
        }

        public int Version => CurrentVersion;
        public DomainAuthorityStamp Authority { get; }
        public long ReplicationSequence { get; }
        public int PayloadBytes => _payload.Length;
        public string Sha256 { get; }

        /// <summary>A defensive copy; callers can never mutate the committed bytes.</summary>
        public ReadOnlyMemory<byte> Payload => _payload.ToArray();

        public static CommittedDomainSnapshot Create(DomainAuthorityStamp authority,
            long replicationSequence, ReadOnlySpan<byte> payload)
        {
            ProtocolValidation.Authority(authority, nameof(authority));
            if (replicationSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(replicationSequence));
            if (payload.Length > MaxPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload), "snapshot exceeds the protocol cap");
            byte[] copy = payload.ToArray();
            return new CommittedDomainSnapshot(authority, replicationSequence, copy,
                SHA256.HashData(copy));
        }

        public bool Verify() => CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(Sha256), SHA256.HashData(_payload));
    }

    public readonly record struct DomainCommand(
        string CommandId,
        DomainAuthorityStamp Authority,
        long Sequence,
        string PayloadSha256)
    {
        public const int MaxCommandIdLength = 96;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(CommandId) || CommandId.Length > MaxCommandIdLength)
                throw new ArgumentException("command id is missing or too long", nameof(CommandId));
            if (!CommandId.All(ProtocolValidation.IdentifierCharacter))
                throw new ArgumentException("command id contains unsafe characters", nameof(CommandId));
            ProtocolValidation.Authority(Authority, nameof(Authority));
            if (Sequence <= 0) throw new ArgumentOutOfRangeException(nameof(Sequence));
            if (PayloadSha256 == null || PayloadSha256.Length != 64
                || !PayloadSha256.All(Uri.IsHexDigit))
                throw new ArgumentException("payload digest must be SHA-256 hex", nameof(PayloadSha256));
        }
    }

    public enum DomainCommandDisposition
    {
        Accepted,
        Duplicate,
        StaleAuthority,
        WrongWorker,
        OutOfOrder,
        IdempotencyConflict,
    }

    /// <summary>
    /// Bounded ordering/idempotency gate for one authority epoch. It deliberately
    /// stores only command ids and digests, never attacker-controlled payloads.
    /// </summary>
    public sealed class DomainCommandGate
    {
        public const int DefaultReplayWindow = 1024;
        public const int MaxReplayWindow = 16_384;

        private readonly int _capacity;
        private readonly Dictionary<string, string> _accepted = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        private DomainAuthorityStamp _authority;
        private long _lastSequence;

        public DomainCommandGate(DomainAuthorityStamp authority,
            long lastSequence = 0, int replayWindow = DefaultReplayWindow)
        {
            ProtocolValidation.Authority(authority, nameof(authority));
            if (lastSequence < 0) throw new ArgumentOutOfRangeException(nameof(lastSequence));
            if (replayWindow <= 0 || replayWindow > MaxReplayWindow)
                throw new ArgumentOutOfRangeException(nameof(replayWindow));
            _authority = authority;
            _lastSequence = lastSequence;
            _capacity = replayWindow;
        }

        public DomainAuthorityStamp Authority => _authority;
        public long LastSequence => _lastSequence;
        public int ReplayEntryCount => _accepted.Count;

        public DomainCommandDisposition Admit(DomainCommand command)
        {
            command.Validate();
            if (command.Authority.DomainId != _authority.DomainId
                || command.Authority.Generation != _authority.Generation)
                return DomainCommandDisposition.StaleAuthority;
            if (command.Authority.WorkerId != _authority.WorkerId)
                return DomainCommandDisposition.WrongWorker;

            if (_accepted.TryGetValue(command.CommandId, out string? digest))
                return string.Equals(digest, command.PayloadSha256, StringComparison.OrdinalIgnoreCase)
                    ? DomainCommandDisposition.Duplicate
                    : DomainCommandDisposition.IdempotencyConflict;
            if (command.Sequence != _lastSequence + 1)
                return DomainCommandDisposition.OutOfOrder;

            _lastSequence = command.Sequence;
            _accepted.Add(command.CommandId, command.PayloadSha256.ToUpperInvariant());
            _order.Enqueue(command.CommandId);
            while (_order.Count > _capacity)
                _accepted.Remove(_order.Dequeue());
            return DomainCommandDisposition.Accepted;
        }

        public void Transfer(DomainAuthorityStamp nextAuthority, long restoredSequence)
        {
            if (nextAuthority.DomainId != _authority.DomainId)
                throw new ArgumentException("authority transfer cannot change domain", nameof(nextAuthority));
            ProtocolValidation.Authority(nextAuthority, nameof(nextAuthority));
            if (nextAuthority.Generation != _authority.Generation.Next())
                throw new InvalidOperationException("authority generation must advance exactly once");
            if (restoredSequence < 0) throw new ArgumentOutOfRangeException(nameof(restoredSequence));
            _authority = nextAuthority;
            _lastSequence = restoredSequence;
            _accepted.Clear();
            _order.Clear();
        }
    }

    public enum DomainRecoveryPhase
    {
        Active,
        AuthorityRevoked,
        Restoring,
        CandidateReady,
    }

    /// <summary>
    /// Deterministic single-coordinator failure policy. It models the safety rules
    /// a real coordinator must preserve; it is not consensus and does not claim to
    /// survive coordinator loss or a network partition by itself.
    /// </summary>
    public sealed class DomainRecoveryModel
    {
        private DomainAuthorityStamp _authority;
        private CommittedDomainSnapshot? _snapshot;
        private WorkerId? _candidate;

        public DomainRecoveryModel(DomainAuthorityStamp authority)
        {
            ProtocolValidation.Authority(authority, nameof(authority));
            _authority = authority;
            Phase = DomainRecoveryPhase.Active;
        }

        public DomainRecoveryPhase Phase { get; private set; }
        public DomainAuthorityStamp Authority => _authority;
        public bool HasCommittedSnapshot => _snapshot != null;

        public void Commit(CommittedDomainSnapshot snapshot)
        {
            if (Phase != DomainRecoveryPhase.Active)
                throw new InvalidOperationException("snapshots can only be committed by active authority");
            if (snapshot.Authority != _authority || !snapshot.Verify())
                throw new InvalidOperationException("snapshot authority or digest is invalid");
            if (_snapshot != null)
            {
                if (snapshot.ReplicationSequence < _snapshot.ReplicationSequence)
                    throw new InvalidOperationException("snapshot sequence cannot move backwards");
                if (snapshot.ReplicationSequence == _snapshot.ReplicationSequence
                    && !string.Equals(snapshot.Sha256, _snapshot.Sha256, StringComparison.Ordinal))
                    throw new InvalidOperationException("one replication sequence cannot commit two states");
            }
            _snapshot = snapshot;
        }

        public void Revoke(WorkerId observedWorker)
        {
            if (Phase != DomainRecoveryPhase.Active || observedWorker != _authority.WorkerId)
                throw new InvalidOperationException("only the current active authority can be revoked");
            Phase = DomainRecoveryPhase.AuthorityRevoked;
            _candidate = null;
        }

        public void BeginRestore(WorkerId candidate)
        {
            if (Phase != DomainRecoveryPhase.AuthorityRevoked)
                throw new InvalidOperationException("authority must be revoked before recovery");
            if (_snapshot == null)
                throw new InvalidOperationException("recovery requires a committed snapshot");
            if (_snapshot.Authority != _authority)
                throw new InvalidOperationException("recovery requires a snapshot from the revoked generation");
            if (candidate == _authority.WorkerId)
                throw new InvalidOperationException("revoked worker cannot be its own recovery candidate");
            _candidate = candidate;
            Phase = DomainRecoveryPhase.Restoring;
        }

        public void MarkReady(WorkerId candidate, string restoredSha256)
        {
            if (Phase != DomainRecoveryPhase.Restoring || !_candidate.HasValue
                || candidate != _candidate.Value)
                throw new InvalidOperationException("unexpected recovery candidate");
            if (_snapshot == null || !string.Equals(restoredSha256, _snapshot.Sha256,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("restored snapshot digest does not match commit");
            Phase = DomainRecoveryPhase.CandidateReady;
        }

        public DomainAuthorityStamp Promote(WorkerId candidate)
        {
            if (Phase != DomainRecoveryPhase.CandidateReady || !_candidate.HasValue
                || candidate != _candidate.Value)
                throw new InvalidOperationException("candidate is not ready");
            _authority = new DomainAuthorityStamp(
                _authority.DomainId, candidate, _authority.Generation.Next());
            _candidate = null;
            Phase = DomainRecoveryPhase.Active;
            return _authority;
        }
    }

    internal static class ProtocolValidation
    {
        private const int MaxDomainIdLength = 128;

        internal static void Authority(DomainAuthorityStamp authority, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(authority.DomainId.Value)
                || string.IsNullOrWhiteSpace(authority.WorkerId.Value)
                || authority.DomainId.Value.Length > MaxDomainIdLength
                || !authority.DomainId.Value.All(IdentifierCharacter)
                || authority.Generation.Value <= 0)
                throw new ArgumentOutOfRangeException(parameterName, "authority stamp is incomplete");
        }

        internal static bool IdentifierCharacter(char value) =>
            (value >= 'a' && value <= 'z')
            || (value >= 'A' && value <= 'Z')
            || (value >= '0' && value <= '9')
            || value == '.' || value == '_' || value == ':' || value == '-';
    }
}
