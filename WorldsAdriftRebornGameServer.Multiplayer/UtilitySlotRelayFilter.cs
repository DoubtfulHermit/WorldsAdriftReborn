namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The verdict for one inbound 6910 UtilitySlotActivatedState update: whether
    /// to relay it to other players, and - when yes - the full head/body/feet
    /// boolean triple to put on the wire.
    /// </summary>
    public readonly struct UtilitySlotRelayDecision
    {
        /// <summary>Forward a 6910 update to the other players.</summary>
        public bool Relay { get; }

        /// <summary>Head utility slot active (helmet-slot utility deployed).</summary>
        public bool Head { get; }

        /// <summary>Body utility slot active. THE GLIDER is a body utility, so this is the wing-open flag.</summary>
        public bool Body { get; }

        /// <summary>Feet utility slot active.</summary>
        public bool Feet { get; }

        public UtilitySlotRelayDecision(bool relay, bool head, bool body, bool feet)
        {
            Relay = relay;
            Head = head;
            Body = body;
            Feet = feet;
        }

        /// <summary>The "do not relay" verdict, carrying the current triple for reference.</summary>
        internal static UtilitySlotRelayDecision Drop(bool head, bool body, bool feet)
            => new UtilitySlotRelayDecision(false, head, body, feet);
    }

    /// <summary>
    /// Turns the per-frame 6910 UtilitySlotActivatedState stream into a low-rate
    /// on/off EVENT stream, so other players can see a glider deploy or a tool
    /// come into hand WITHOUT the relay that crashed sync on 2026-08-09.
    ///
    /// WHY THIS EXISTS - the whole 6910 story in one place.
    ///
    /// 6910 carries nine fields: three bool slot-active flags (head/body/feet)
    /// and six utility-health floats. The client's writer
    /// (<c>UtilitySlotActivatedBehaviour.Update</c>) sets ALL nine every frame and
    /// calls <c>FinishAndSend</c>, but the generated
    /// <c>FinishAndSend_ResolveDiff</c> CLEARS every field whose value equals the
    /// stored one and sends only if at least one field actually changed (VERIFIED
    /// in Generated.Code). So:
    ///
    ///   * A glider that is simply HELD open sends nothing - all nine match.
    ///   * The ~170/s spam measured while a utility is active is a HEALTH float
    ///     changing every frame; that update carries the health field ONLY, the
    ///     bools having been cleared as unchanged.
    ///   * A glider deploy / retract (or a tool coming into / leaving the hand) is
    ///     a BOOL flip: a separate, rare update carrying head/body/feet only.
    ///
    /// The remote Traveller@Default rig's <c>UtilitySlotActivatedVisualizer</c>
    /// opens/closes the glider and shows the held tool off the BOOLS
    /// (<c>customisation.UseUtility(slot)</c> fires on a bool difference); the
    /// health floats only drive an off-screen durability material. LIVE evidence
    /// 2026-08 confirmed both the glider and the tool-in-hand appeared on remotes
    /// while 6910 was relayed and vanished the instant it was filtered.
    ///
    /// So the safe relay is: forward a 6910 update ONLY when one of the three
    /// bools changes, and DROP every health-only update. That collapses ~170/s to
    /// a handful of transitions and restores the visual with near-zero traffic.
    /// The blanket per-frame path stays OFF in
    /// <see cref="MirrorSendPolicy.IsRelayedToOtherPlayers"/> - this filter is the
    /// only thing that ever relays 6910, exactly as RelayEmitter is the only thing
    /// that relays 190602/1073.
    ///
    /// The payload carries no cross-entity reference (three bools), so
    /// re-addressing the relayed update to the sender's own entity - which is what
    /// every relay does - is correct here, unlike 1231/1037/1211.
    ///
    /// Pure and per-entity; the server keeps one instance and feeds it the
    /// already-deserialized update from a component handler.
    /// </summary>
    public sealed class UtilitySlotRelayFilter
    {
        private readonly struct Triple
        {
            public readonly bool Head;
            public readonly bool Body;
            public readonly bool Feet;

            public Triple(bool head, bool body, bool feet)
            {
                Head = head;
                Body = body;
                Feet = feet;
            }

            public bool Equals(Triple other)
                => Head == other.Head && Body == other.Body && Feet == other.Feet;
        }

        /// <summary>
        /// Last triple relayed for each entity. Absent means never relayed: the
        /// baseline is the remote seed default (all-inactive), so the first
        /// genuine deploy is always a change and always relays.
        /// </summary>
        private readonly Dictionary<long, Triple> _lastRelayed = new();

        /// <summary>
        /// Judge one inbound 6910 update, described by which of its bool fields
        /// were present on the wire (null = the field was absent, i.e. unchanged
        /// or a health-only update) and their values.
        ///
        /// Relays only when the merged head/body/feet state differs from what was
        /// last relayed for this entity; a health-only update (no bool present) is
        /// always dropped. When it relays, the decision carries the FULL triple so
        /// the wire update is self-contained and idempotent.
        /// </summary>
        public UtilitySlotRelayDecision Decide(long entityId, bool? head, bool? body, bool? feet)
        {
            Triple last = _lastRelayed.TryGetValue(entityId, out Triple existing)
                ? existing
                : new Triple(false, false, false);

            bool carriesAnyBool = head.HasValue || body.HasValue || feet.HasValue;
            if (!carriesAnyBool)
            {
                // Health-only update: the ~170/s spam. Nothing the remote's glider
                // or tool visual reads, so it never goes on the wire.
                return UtilitySlotRelayDecision.Drop(last.Head, last.Body, last.Feet);
            }

            // Merge the carried fields over the last-relayed state. A bool-flip
            // update carries only the field that changed (ResolveDiff cleared the
            // rest), so the other two must be filled from what we last sent.
            Triple candidate = new Triple(
                head ?? last.Head,
                body ?? last.Body,
                feet ?? last.Feet);

            if (_lastRelayed.ContainsKey(entityId) && candidate.Equals(last))
            {
                // The bool that arrived matched what we already relayed (e.g. a
                // redundant re-send). Nothing changed; do not relay.
                return UtilitySlotRelayDecision.Drop(last.Head, last.Body, last.Feet);
            }

            if (candidate.Equals(last) && !_lastRelayed.ContainsKey(entityId))
            {
                // First update for this entity but it matches the seed default
                // (all-inactive). The remote already looks like this, so relaying
                // buys nothing - but record the baseline so we do not treat the
                // next identical frame as new.
                _lastRelayed[entityId] = candidate;
                return UtilitySlotRelayDecision.Drop(last.Head, last.Body, last.Feet);
            }

            _lastRelayed[entityId] = candidate;
            return new UtilitySlotRelayDecision(true, candidate.Head, candidate.Body, candidate.Feet);
        }

        /// <summary>
        /// Drop an entity's tracked state on disconnect, so a reconnecting player
        /// starts from the seed-default baseline again. Bounded either way (one
        /// entry per player), but part of the everything-contract of forgetting a
        /// peer.
        /// </summary>
        public void Forget(long entityId) => _lastRelayed.Remove(entityId);
    }
}
