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
        private readonly HashSet<FixedPointPosition> _positions = new HashSet<FixedPointPosition>();
        private bool _requestSent;
        private int _spawned;
        private int _nextIndex;

        /// <summary>The world-entity key prefix for a handshake-spawned deposit; the island id is folded in by the spawner.</summary>
        public const string KeyPrefix = "handshake-deposit-";

        public IslandResourceLedger(int requestedCount)
        {
            _requestedCount = IslandResourceHandshake.ClampCount(requestedCount);
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
            IReadOnlyList<HandshakeDeposit> accepted =
                SpawnReplyPlan.Accept(items, _spawned, _requestedCount, _positions);

            List<AdmittedDeposit> result = new List<AdmittedDeposit>(accepted.Count);
            foreach (HandshakeDeposit d in accepted)
            {
                _positions.Add(d.Position);
                _spawned++;
                result.Add(new AdmittedDeposit(_nextIndex++, d.Position, d.Variant));
            }
            return result;
        }
    }
}
