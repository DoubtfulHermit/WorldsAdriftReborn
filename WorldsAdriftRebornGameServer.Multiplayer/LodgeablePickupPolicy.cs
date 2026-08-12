namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Why a lodgeable-pickup PickUp request was or was not honoured. Exactly one
    /// value per evaluation; <see cref="LodgeablePickupOutcome.Grant"/> is the only
    /// one that leads to an inventory grant.
    ///
    /// The SHARED gate outcome, extracted from the atlas-shard vertical so both the
    /// atlas shard and the fuel pod decide a PickUp with identical rules. The atlas
    /// vertical keeps its own <c>AtlasPickupOutcome</c> names for its callers and
    /// tests, and maps onto these one-to-one (see <see cref="AtlasPickupPolicy"/>).
    /// </summary>
    public enum LodgeablePickupOutcome
    {
        /// <summary>All checks passed: reserve the pickup and attempt the grant.</summary>
        Grant,

        /// <summary>The peer does not own the player entity the request rode in on.</summary>
        NotOwner,

        /// <summary>The interaction verb was not PickUp.</summary>
        WrongVerb,

        /// <summary>The target entity is not a live lodgeable pickup.</summary>
        NotAPickup,

        /// <summary>The pickup is still lodged (not released).</summary>
        StillLodged,

        /// <summary>The pickup has already been collected by someone.</summary>
        AlreadyCollected,

        /// <summary>The pickup is reserved by another player mid-transaction.</summary>
        Reserved,

        /// <summary>The player is outside the pickup radius (only checked when a position is known).</summary>
        TooFar,

        /// <summary>
        /// The policy passed and the pickup was reserved, but the inventory grant did
        /// not land - an unknown item type or a full grid. The reservation is rolled
        /// back and the pickup stays available. This is a TRANSACTION outcome only;
        /// <see cref="LodgeablePickupPolicy.Evaluate"/> never returns it.
        /// </summary>
        GrantFailed,
    }

    /// <summary>
    /// The outcome of evaluating a lodgeable-pickup PickUp request.
    /// </summary>
    public readonly struct LodgeablePickupDecision : IEquatable<LodgeablePickupDecision>
    {
        public LodgeablePickupDecision(LodgeablePickupOutcome outcome)
        {
            Outcome = outcome;
        }

        public LodgeablePickupOutcome Outcome { get; }

        /// <summary>Whether the transaction should proceed to reserve + grant.</summary>
        public bool ShouldGrant => Outcome == LodgeablePickupOutcome.Grant;

        public bool Equals(LodgeablePickupDecision other) => Outcome == other.Outcome;

        public override bool Equals(object? obj) => obj is LodgeablePickupDecision other && Equals(other);

        public override int GetHashCode() => (int)Outcome;

        public override string ToString() => Outcome.ToString();
    }

    /// <summary>
    /// The rules that decide whether a native 1211 <c>InteractWithObject(target,
    /// PickUp)</c> is allowed to grant a LODGEABLE PICKUP (an atlas shard, a fuel
    /// pod). Pure: it takes a snapshot of FACTS the caller has already gathered
    /// (ownership, verb, the pickup's state, an optional distance) and returns a
    /// single verdict, so the whole gate - including the "two players cannot both
    /// win" and "not until released" cases - is pinned by xUnit rather than by a
    /// running client.
    ///
    /// It deliberately knows nothing about ENet, the game's InteractVerb enum, or the
    /// registry: the verb arrives as a plain bool and the pickup's state as plain
    /// bools, exactly like every other policy in this assembly. The RESERVATION
    /// itself is a stateful mutation and lives in
    /// <see cref="LodgeablePickupRegistry"/>; this policy only decides whether to
    /// ATTEMPT it.
    /// </summary>
    public static class LodgeablePickupPolicy
    {
        /// <summary>
        /// Evaluates a PickUp request. Checks are ordered cheapest/most fundamental
        /// first so the returned reason is the most basic thing wrong.
        /// </summary>
        /// <param name="peerOwnsPlayer">
        /// Whether the sending peer owns the player entity the 1211 event rode on. A
        /// modified client could address an event to someone else's avatar; this is
        /// the same rule-6 ownership guard the salvage handler applies.
        /// </param>
        /// <param name="verbIsPickUp">Whether the interaction verb was PickUp.</param>
        /// <param name="targetIsPickup">Whether the target entity is a registered lodgeable pickup.</param>
        /// <param name="released">Whether the pickup has been released (freed).</param>
        /// <param name="collected">Whether the pickup has already been collected.</param>
        /// <param name="reservedByOther">Whether another player holds the pickup's reservation.</param>
        /// <param name="distanceMetres">
        /// Straight-line distance from the player to the pickup, or null when the
        /// server has no authoritative player position to check against. Null SKIPS
        /// the range check and trusts the client's own range check (the client only
        /// issues the interaction after its own proximity test) - the exact retail
        /// distance tolerance is not recoverable from the decompile.
        /// </param>
        /// <param name="radiusMetres">The pickup radius the pickup's 1210 entry advertises.</param>
        public static LodgeablePickupDecision Evaluate(
            bool peerOwnsPlayer,
            bool verbIsPickUp,
            bool targetIsPickup,
            bool released,
            bool collected,
            bool reservedByOther,
            double? distanceMetres,
            double radiusMetres)
        {
            if (!peerOwnsPlayer)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.NotOwner);
            }
            if (!verbIsPickUp)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.WrongVerb);
            }
            if (!targetIsPickup)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.NotAPickup);
            }
            if (collected)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.AlreadyCollected);
            }
            if (!released)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.StillLodged);
            }
            if (reservedByOther)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.Reserved);
            }
            if (distanceMetres.HasValue && distanceMetres.Value > radiusMetres)
            {
                return new LodgeablePickupDecision(LodgeablePickupOutcome.TooFar);
            }
            return new LodgeablePickupDecision(LodgeablePickupOutcome.Grant);
        }
    }
}
