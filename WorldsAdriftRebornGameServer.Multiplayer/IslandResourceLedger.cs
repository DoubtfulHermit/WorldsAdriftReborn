using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>One deposit the ledger has admitted for spawning: its per-island index, position and variant.</summary>
    public readonly struct AdmittedDeposit
    {
        public AdmittedDeposit(int index, FixedPointPosition position, string variant)
        {
            Index = index;
            Position = position;
            Variant = variant;
        }

        /// <summary>Monotonic per-island index; the spawner composes a unique world-entity key from it.</summary>
        public int Index { get; }

        public FixedPointPosition Position { get; }
        public string Variant { get; }
    }

    /// <summary>
    /// One reply batch as the ledger settled it: what to spawn, why the rest was dropped,
    /// and whether the whole batch was refused because the island had already fallen back
    /// to the static table.
    /// </summary>
    public readonly struct LedgerAdmission
    {
        public LedgerAdmission(
            IReadOnlyList<AdmittedDeposit> admitted,
            SpawnReplyOutcome outcome,
            bool refusedBecauseFallbackFired)
        {
            Admitted = admitted;
            Outcome = outcome;
            RefusedBecauseFallbackFired = refusedBecauseFallbackFired;
        }

        /// <summary>The deposits the caller should spawn now.</summary>
        public IReadOnlyList<AdmittedDeposit> Admitted { get; }

        /// <summary>The per-reason drop counts and the first out-of-bounds sample.</summary>
        public SpawnReplyOutcome Outcome { get; }

        /// <summary>True when nothing was even considered: the static fallback owns this island.</summary>
        public bool RefusedBecauseFallbackFired { get; }
    }

    /// <summary>
    /// The mutable, per-ISLAND bookkeeping of the resource handshake: how many deposits
    /// this island asked for, whether the request event has gone out, and every position
    /// already spawned - so the clamp, the dedup and the "ask once" idempotency all hold
    /// across many 1011 replies (a client re-sending, or several clients each sampling
    /// the same island).
    ///
    /// Still pure C# (no ENet, no Improbable types): the trust envelope is unit-tested
    /// without a running client. The actual entity spawn is the glue's job
    /// (DepositHandshakeSpawner); this only decides WHAT is allowed.
    ///
    /// NOT thread-safe, by the same single-poll-loop contract as
    /// <see cref="WorldEntityRegistry"/> and <see cref="EntityIdAllocator"/>.
    /// </summary>
    public sealed class IslandResourceLedger
    {
        private readonly int _requestedCount;
        private readonly IslandBounds? _bounds;
        private readonly HashSet<FixedPointPosition> _positions = new HashSet<FixedPointPosition>();
        private bool _requestSent;
        private bool _deadlineArmed;
        private bool _fallbackFired;
        private int _spawned;
        private int _nextIndex;

        /// <summary>The world-entity key prefix for a handshake-spawned deposit; the island id is folded in by the spawner.</summary>
        public const string KeyPrefix = "handshake-deposit-";

        public IslandResourceLedger(int requestedCount)
            : this(requestedCount, bounds: null)
        {
        }

        /// <summary>
        /// A ledger that also refuses any replied position outside
        /// <paramref name="bounds"/> - the coordinate-frame guard. Production passes
        /// <see cref="IslandBounds.Haven"/>; null keeps the pre-guard behaviour.
        /// </summary>
        public IslandResourceLedger(int requestedCount, IslandBounds? bounds)
        {
            _requestedCount = IslandResourceHandshake.ClampCount(requestedCount);
            _bounds = bounds;
        }

        /// <summary>The clamped number of deposits this island will ever spawn from the handshake.</summary>
        public int RequestedCount => _requestedCount;

        /// <summary>How many deposits have been admitted (and, by the caller, spawned) so far.</summary>
        public int SpawnedCount => _spawned;

        /// <summary>Whether the 1010 SpawnResources request has already been dispatched for this island.</summary>
        public bool RequestSent => _requestSent;

        /// <summary>Whether the requested count has been reached; further replies admit nothing.</summary>
        public bool Satisfied => _spawned >= _requestedCount;

        /// <summary>
        /// Marks the 1010 request as sent, returning true only the FIRST time. The caller
        /// sends the <c>SpawnResources</c> event only when this returns true, so the same
        /// island is never asked twice however many peers check it out.
        /// </summary>
        public bool MarkRequestSent()
        {
            if (_requestSent)
            {
                return false;
            }
            _requestSent = true;
            return true;
        }

        /// <summary>
        /// Admits a client reply batch: applies <see cref="SpawnReplyPlan.Accept"/> against
        /// the current spawned count and used positions, then records the winners (position
        /// + monotonic index) so a re-send or a second client cannot double-spawn or exceed
        /// the count. Returns exactly the deposits the caller should spawn now.
        /// </summary>
        public IReadOnlyList<AdmittedDeposit> Admit(IEnumerable<ResourceReplyItem>? items)
        {
            return AdmitDetailed(items).Admitted;
        }

        /// <summary>
        /// <see cref="Admit"/>, but returning the batch's DROP REASONS alongside the
        /// winners so the caller can log a refused reply with the reason (out-of-bounds is
        /// a coordinate-frame bug and must not be reported as "duplicate"). Once
        /// <see cref="FallbackFired"/> the ledger admits nothing at all: the island's ore
        /// has already been resolved by the static table, and mixing the two sets is the
        /// one outcome that would be undiagnosable from the log.
        /// </summary>
        public LedgerAdmission AdmitDetailed(IEnumerable<ResourceReplyItem>? items)
        {
            if (_fallbackFired)
            {
                return new LedgerAdmission(
                    System.Array.Empty<AdmittedDeposit>(),
                    new SpawnReplyOutcome(System.Array.Empty<HandshakeDeposit>(), 0, 0, 0, null),
                    refusedBecauseFallbackFired: true);
            }

            SpawnReplyOutcome outcome =
                SpawnReplyPlan.Evaluate(items, _spawned, _requestedCount, _positions, _bounds);

            List<AdmittedDeposit> result = new List<AdmittedDeposit>(outcome.Accepted.Count);
            foreach (HandshakeDeposit d in outcome.Accepted)
            {
                _positions.Add(d.Position);
                _spawned++;
                result.Add(new AdmittedDeposit(_nextIndex++, d.Position, d.Variant));
            }
            return new LedgerAdmission(result, outcome, refusedBecauseFallbackFired: false);
        }

        /// <summary>Whether the fallback deadline has been armed for this island; true only the FIRST time.</summary>
        public bool MarkDeadlineArmed()
        {
            if (_deadlineArmed)
            {
                return false;
            }
            _deadlineArmed = true;
            return true;
        }

        /// <summary>Whether the static-table fallback has been used for this island.</summary>
        public bool FallbackFired => _fallbackFired;

        /// <summary>
        /// Latches the island onto the static fallback, returning true only the FIRST
        /// time. After this the ledger admits no further client replies, so a reply that
        /// arrives after the deadline cannot stack a second set of deposits on top of the
        /// hand-placed one.
        /// </summary>
        public bool MarkFallbackFired()
        {
            if (_fallbackFired)
            {
                return false;
            }
            _fallbackFired = true;
            return true;
        }
    }
}
