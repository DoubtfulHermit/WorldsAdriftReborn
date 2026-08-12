namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Why a shard PickUp request was or was not honoured. Exactly one value per
    /// evaluation; <see cref="AtlasPickupOutcome.Grant"/> is the only one that leads to
    /// an inventory grant.
    /// </summary>
    public enum AtlasPickupOutcome
    {
        /// <summary>All checks passed: reserve the shard and attempt the grant.</summary>
        Grant,

        /// <summary>The peer does not own the player entity the request rode in on.</summary>
        NotOwner,

        /// <summary>The interaction verb was not PickUp.</summary>
        WrongVerb,

        /// <summary>The target entity is not a live atlas shard.</summary>
        NotAShard,

        /// <summary>The shard is still lodged in its core (not released by mining).</summary>
        StillLodged,

        /// <summary>The shard has already been collected by someone.</summary>
        AlreadyCollected,

        /// <summary>The shard is reserved by another player mid-transaction.</summary>
        Reserved,

        /// <summary>The player is outside the pickup radius (only checked when a position is known).</summary>
        TooFar,

        /// <summary>
        /// The policy passed and the shard was reserved, but the inventory grant did
        /// not land - an unknown item type (the pending placeholder id) or a full grid.
        /// The reservation is rolled back and the shard stays available. This is a
        /// TRANSACTION outcome only; <see cref="AtlasPickupPolicy.Evaluate"/> never
        /// returns it.
        /// </summary>
        GrantFailed,
    }

    /// <summary>
    /// The outcome of evaluating a shard PickUp request.
    /// </summary>
    public readonly struct AtlasPickupDecision : IEquatable<AtlasPickupDecision>
    {
        public AtlasPickupDecision(AtlasPickupOutcome outcome)
        {
            Outcome = outcome;
        }

        public AtlasPickupOutcome Outcome { get; }

        /// <summary>Whether the transaction should proceed to reserve + grant.</summary>
        public bool ShouldGrant => Outcome == AtlasPickupOutcome.Grant;

        public bool Equals(AtlasPickupDecision other) => Outcome == other.Outcome;

        public override bool Equals(object? obj) => obj is AtlasPickupDecision other && Equals(other);

        public override int GetHashCode() => (int)Outcome;

        public override string ToString() => Outcome.ToString();
    }

    /// <summary>
    /// The rules that decide whether a native 1211 <c>InteractWithObject(target,
    /// PickUp)</c> is allowed to grant an atlas shard. Pure: it takes a snapshot of
    /// FACTS the caller has already gathered (ownership, verb, the shard's state, an
    /// optional distance) and returns a single verdict, so the whole gate - including
    /// the "two players cannot both win" and "not until mined loose" cases - is pinned
    /// by xUnit rather than by a running client.
    ///
    /// It deliberately knows nothing about ENet, the game's InteractVerb enum, or the
    /// registry: the verb arrives as a plain bool and the shard's state as plain bools,
    /// exactly like every other policy in this assembly. The RESERVATION itself is a
    /// stateful mutation and lives in <see cref="AtlasShardRegistry"/>; this policy only
    /// decides whether to ATTEMPT it.
    /// </summary>
    public static class AtlasPickupPolicy
    {
        /// <summary>
        /// Evaluates a shard PickUp request. Checks are ordered cheapest/most
        /// fundamental first so the returned reason is the most basic thing wrong.
        /// </summary>
        /// <param name="peerOwnsPlayer">
        /// Whether the sending peer owns the player entity the 1211 event rode on. A
        /// modified client could address an event to someone else's avatar; this is the
        /// same rule-6 ownership guard the salvage handler applies.
        /// </param>
        /// <param name="verbIsPickUp">Whether the interaction verb was PickUp.</param>
        /// <param name="targetIsShard">Whether the target entity is a registered atlas shard.</param>
        /// <param name="released">Whether the shard has been mined loose (released).</param>
        /// <param name="collected">Whether the shard has already been collected.</param>
        /// <param name="reservedByOther">Whether another player holds the shard's reservation.</param>
        /// <param name="distanceMetres">
        /// Straight-line distance from the player to the shard, or null when the server
        /// has no authoritative player position to check against. Null SKIPS the range
        /// check and trusts the client's own range check (the client only issues the
        /// interaction after its own proximity test, and the salvage path likewise
        /// trusts the client raycast) - the exact retail distance tolerance is not
        /// recoverable from the decompile (findings §5).
        /// </param>
        /// <param name="radiusMetres">The pickup radius the shard's 1210 entry advertises.</param>
        public static AtlasPickupDecision Evaluate(
            bool peerOwnsPlayer,
            bool verbIsPickUp,
            bool targetIsShard,
            bool released,
            bool collected,
            bool reservedByOther,
            double? distanceMetres,
            double radiusMetres)
        {
            if (!peerOwnsPlayer)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.NotOwner);
            }
            if (!verbIsPickUp)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.WrongVerb);
            }
            if (!targetIsShard)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.NotAShard);
            }
            if (collected)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.AlreadyCollected);
            }
            if (!released)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.StillLodged);
            }
            if (reservedByOther)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.Reserved);
            }
            if (distanceMetres.HasValue && distanceMetres.Value > radiusMetres)
            {
                return new AtlasPickupDecision(AtlasPickupOutcome.TooFar);
            }
            return new AtlasPickupDecision(AtlasPickupOutcome.Grant);
        }
    }
}
