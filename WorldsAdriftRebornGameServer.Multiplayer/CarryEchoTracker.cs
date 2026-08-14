namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The result of folding one inbound 1073 <c>relativeTo</c> into a peer's
    /// carry-echo state: whether the server should echo a 1073 back to that peer,
    /// and the <c>relativeTo</c> entity id to put in it.
    /// </summary>
    public readonly struct CarryEchoDecision
    {
        public CarryEchoDecision(bool shouldEcho, long relativeTo)
        {
            ShouldEcho = shouldEcho;
            RelativeTo = relativeTo;
        }

        /// <summary>Whether to send a minimal 1073 echo back to the owning peer now.</summary>
        public bool ShouldEcho { get; }

        /// <summary>
        /// The <c>relativeTo</c> entity id to echo. Meaningful only when
        /// <see cref="ShouldEcho"/>. It is the EXACT id the client reported it is
        /// standing on - never a ship root or any other id - because the client's
        /// <c>RelativeGameObject</c> setter only arms the PathFollower when the
        /// echoed id resolves to the SAME GameObject the client already chose as its
        /// ground object (<c>LocalRelativeGroundObject == value</c>). Echoing a
        /// different id (e.g. the hull root when the player stands on the deck)
        /// fails that guard and the carry never arms.
        /// </summary>
        public long RelativeTo { get; }

        public static readonly CarryEchoDecision None = new CarryEchoDecision(false, 0);

        public static CarryEchoDecision Echo(long relativeTo) => new CarryEchoDecision(true, relativeTo);

        public override string ToString() =>
            ShouldEcho ? "echo relativeTo " + RelativeTo : "no echo";
    }

    /// <summary>
    /// Decides WHEN to echo a player's own 1073 <c>relativeTo</c> back to the peer
    /// that sent it, so the client-side ship carry can arm.
    ///
    /// WHY THE ECHO EXISTS. The physical carry is
    /// <c>ClientAuthoritativePlayerMovement.RepositionRelativeToGroundedObject</c>,
    /// which <c>MovePosition</c>s the player to track a moving ground object every
    /// FixedUpdate. It runs only when <c>ShouldCorrectPosition()</c> is true, which
    /// needs <c>RelativePathFollower != null</c>. That PathFollower is set ONLY in
    /// <c>HandleRelativeToUpdate</c>, which fires ONLY from a RECEIVED 1073
    /// <c>relativeTo</c> ComponentUpdate (the client's own <c>Send()</c> does not
    /// fire it locally). In stock SpatialOS the platform echoed a worker's own
    /// authoritative updates back to it; this custom server does not, so the owner
    /// never receives its own 1073 and the carry never arms. This tracker drives the
    /// echo that closes that gap.
    ///
    /// WHY ONLY <c>relativeTo</c>, AND ONLY ON A TRANSITION. The player is
    /// authoritative over 1073 and republishes position/bone data every tick;
    /// echoing any of that back would fight the client's own prediction and
    /// rubber-band it. But arming the carry needs nothing but the <c>relativeTo</c>
    /// id - the repositioner recomputes the relative OFFSET locally from live
    /// transforms, so a bare <c>relativeTo</c> echo carries no position to snap to.
    /// And it only needs to CHANGE hands on a board / leave / ship-change, not every
    /// frame, so this echoes only when the exact ground surface changes or the
    /// semantic aboard tracker confirms a leave. A player standing still on a deck
    /// re-sends the same <c>relativeTo</c> (or none at all); either way this stays silent.
    ///
    /// The wire id remains the exact surface id: the client rejects a hull id while
    /// its local ground object is a deck. Semantic board/leave timing comes from
    /// <see cref="AboardTracker"/>, however, so a one-frame Invalid contact gap is
    /// not echoed as disarm/re-arm churn.
    ///
    /// Pure and allocation-light on the hot path (one dictionary lookup; an
    /// allocation only for a peer's first observation). NOT thread-safe, in the mold
    /// of the other server-state holders: one poll loop.
    /// </summary>
    public sealed class CarryEchoTracker
    {
        private sealed class PeerEchoState
        {
            // The last relativeTo id echoed to this peer, and whether one ever was.
            // Seeded empty: the server's own 1073 SEED sets the client's baseline to
            // InvalidEntityId, and the client only sends relativeTo when it changes
            // FROM that baseline, so the first relativeTo we see is a genuine board
            // and is worth echoing.
            public bool HasEchoed;
            public long LastEchoed;
        }

        private readonly Dictionary<ulong, PeerEchoState> _peers = new Dictionary<ulong, PeerEchoState>();

        /// <summary>
        /// Folds one inbound 1073 and its canonical aboard transition into
        /// <paramref name="playerId"/>'s echo state.
        /// </summary>
        /// <param name="playerId">The peer that sent the update (its own entity).</param>
        /// <param name="relativeToPresent">
        /// Whether this delta CARRIED a <c>relativeTo</c> (SpatialOS updates are
        /// deltas - a field is present only when it changed). An update that carried
        /// no <c>relativeTo</c> cannot be a board/leave edge and is a no-op here.
        /// </param>
        /// <param name="relativeTo">The carried <c>relativeTo</c> id. Meaningful only when <paramref name="relativeToPresent"/>.</param>
        public CarryEchoDecision Observe(ulong playerId, in AboardTransition aboard,
            bool relativeToPresent, long relativeTo, bool deferInvalidForShipContactGap)
        {
            if (!_peers.TryGetValue(playerId, out PeerEchoState? state))
            {
                state = new PeerEchoState();
                _peers[playerId] = state;
            }

            // A confirmed leave can mature on a later position-only tick after
            // AboardTracker's contact-gap grace. There is no raw field on that tick,
            // so synthesize InvalidEntityId to finally disarm the client.
            if (aboard.Change == AboardChange.Disembarked && !relativeToPresent)
            {
                const long invalidEntityId = -1;
                state.HasEchoed = true;
                state.LastEchoed = invalidEntityId;
                return CarryEchoDecision.Echo(invalidEntityId);
            }

            if (!relativeToPresent)
            {
                return CarryEchoDecision.None;
            }

            // Invalid during the grace window is intentionally silent. If it is a
            // real leave, AboardTracker emits Disembarked after the deadline and the
            // branch above sends one semantic disarm.
            if (relativeTo <= 0
                && aboard.Change != AboardChange.Disembarked
                && deferInvalidForShipContactGap)
            {
                return CarryEchoDecision.None;
            }

            if (state.HasEchoed && state.LastEchoed == relativeTo)
            {
                // Same ground object as last echo: the player is standing still (or
                // the client is re-asserting the same id). Nothing to arm or disarm.
                return CarryEchoDecision.None;
            }

            state.HasEchoed = true;
            state.LastEchoed = relativeTo;
            return CarryEchoDecision.Echo(relativeTo);
        }

        /// <summary>
        /// Drops a peer's echo state. Part of the ForgetPeer everything-contract: a
        /// reconnecting peer must start with no remembered relativeTo, or its first
        /// genuine board could be deduped away against a dead session's value.
        /// </summary>
        public void Forget(ulong playerId) => _peers.Remove(playerId);
    }
}
