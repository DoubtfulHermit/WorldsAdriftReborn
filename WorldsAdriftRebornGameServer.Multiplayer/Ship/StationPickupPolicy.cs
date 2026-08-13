namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Which kind of placed station a PickUp request is aimed at, as resolved from
    /// the placement ledgers by the caller. <see cref="None"/> means the target is
    /// in neither placed-station ledger - not a station we placed, or already
    /// removed from the ledgers by an earlier pickup.
    /// </summary>
    public enum PickupStationKind
    {
        None,
        Shipyard,
        AssemblyStation,
    }

    /// <summary>
    /// Why a placed-station PickUp request was or was not honoured. Exactly one
    /// value per evaluation; <see cref="Grant"/> is the only one that leads to an
    /// inventory grant. The shape mirrors <see cref="AtlasPickupOutcome"/> - the
    /// other 1211 PickUp transaction - so the two read identically in logs.
    /// </summary>
    public enum StationPickupOutcome
    {
        /// <summary>All checks passed: reserve the station and attempt the grant.</summary>
        Grant,

        /// <summary>The peer does not own the player entity the request rode in on.</summary>
        NotOwner,

        /// <summary>The interaction verb was not PickUp.</summary>
        WrongVerb,

        /// <summary>The station was already picked up (its ledger entries are gone; only the tombstone knows it).</summary>
        AlreadyPickedUp,

        /// <summary>The target entity is not a placed shipyard or Assembly Station.</summary>
        NotAStation,

        /// <summary>The station has a recorded owner and the requester is not them.</summary>
        NotYourStation,

        /// <summary>The shipyard has a ship docked at it - packing it would orphan the hull's dock.</summary>
        ShipDocked,

        /// <summary>A ship-blueprint build or frame-design edit is live on the shipyard.</summary>
        BuildInProgress,

        /// <summary>A station craft session (a selected recipe) is bound to the station.</summary>
        CraftInProgress,

        /// <summary>Materials are slotted into a craft bound to the station - packing would eat them.</summary>
        MaterialsLoaded,

        /// <summary>Another player holds the station's pickup reservation.</summary>
        ReservedByOther,

        /// <summary>The player is outside the pickup range (only checked when a position is known).</summary>
        TooFar,

        /// <summary>
        /// The policy passed and the station was reserved, but the inventory grant
        /// did not land (full grid or unknown item type). The reservation is rolled
        /// back and the station stays placed. This is a TRANSACTION outcome only;
        /// <see cref="StationPickupPolicy.Evaluate"/> never returns it.
        /// </summary>
        GrantFailed,
    }

    /// <summary>The outcome of evaluating a placed-station PickUp request.</summary>
    public readonly struct StationPickupDecision : IEquatable<StationPickupDecision>
    {
        public StationPickupDecision(StationPickupOutcome outcome)
        {
            Outcome = outcome;
        }

        public StationPickupOutcome Outcome { get; }

        /// <summary>Whether the transaction should proceed to reserve + grant.</summary>
        public bool ShouldGrant => Outcome == StationPickupOutcome.Grant;

        public bool Equals(StationPickupDecision other) => Outcome == other.Outcome;

        public override bool Equals(object? obj) => obj is StationPickupDecision other && Equals(other);

        public override int GetHashCode() => (int)Outcome;

        public override string ToString() => Outcome.ToString();
    }

    /// <summary>
    /// The rules that decide whether a native 1211 <c>InteractWithObject(target,
    /// PickUp)</c> is allowed to pack a PLACED shipyard or Assembly Station back
    /// into the requesting player's inventory.
    ///
    /// This is a deliberate NON-RETAIL extension: retail Worlds Adrift had no
    /// deployable pickup at all (codex-verified - both prefabs bake only the Craft
    /// verb, and the scanner tool explicitly refuses every IPlayerPlaceable that is
    /// not a ShipPartVisualizer). The gates here are therefore OUR rules, shaped
    /// like the atlas-shard pickup policy so the whole gate - ownership, busy
    /// states, the "two players cannot both win" reservation and the authoritative
    /// range check - is pinned by xUnit rather than by a running client.
    ///
    /// Pure: it takes a snapshot of FACTS the caller has already gathered from the
    /// placement/dock/build/craft ledgers and returns a single verdict. It knows
    /// nothing about ENet, the game's InteractVerb enum, or the ledgers themselves.
    /// The RESERVATION is a stateful mutation and lives in
    /// <see cref="Placement.StationPickupLedger"/>; this policy only decides
    /// whether to ATTEMPT it.
    /// </summary>
    public static class StationPickupPolicy
    {
        /// <summary>
        /// Extra metres allowed beyond the advertised 1210 interaction radius.
        /// Retail's own interaction COMPLETION check allows two metres of drift
        /// after the hold (InteractAgentObserver.CheckInteraction re-checks
        /// <c>playerLookingAt.InRange(interactLookingAt, 2f)</c> when the timed
        /// hold completes - decompile :397-405), so the server extends the same
        /// leeway rather than rejecting a request the client legitimately sent.
        /// </summary>
        public const double CompletionLeewayMetres = 2.0;

        /// <summary>
        /// Evaluates a station PickUp request. Checks are ordered cheapest/most
        /// fundamental first so the returned reason is the most basic thing wrong.
        /// </summary>
        /// <param name="peerOwnsPlayer">
        /// Whether the sending peer owns the player entity the 1211 event rode on -
        /// the same rule-6 ownership guard every other 1211 dispatch applies.
        /// </param>
        /// <param name="verbIsPickUp">Whether the interaction verb was PickUp.</param>
        /// <param name="alreadyPickedUp">
        /// Whether the station was already packed (the pickup tombstone knows it,
        /// its membership ledgers no longer do). Checked BEFORE the kind so a
        /// duplicate event on a just-packed station reads "already picked up"
        /// rather than "not a station".
        /// </param>
        /// <param name="kind">Which placed-station ledger the target was found in, or None.</param>
        /// <param name="ownerCharacterUid">
        /// The owner character uid recorded when the station was placed
        /// (CharacterOwnership.UidForEntity at placement time), or "" when the
        /// placer had no durable identity. An UNOWNED station ("" owner) is
        /// pickable by anyone: that matches the ownership convention everywhere
        /// else in this assembly (<see cref="Ship.OwnershipRegistrationPolicy"/> -
        /// an empty owner means "nobody owns it", the pre-identity behaviour).
        /// </param>
        /// <param name="requesterCharacterUid">
        /// The acting player's durable character uid, resolved by the SAME
        /// mechanism the placement stamp uses (CharacterOwnership.UidForEntity),
        /// or "" on a volatile session.
        /// </param>
        /// <param name="shipDocked">Shipyard only: whether a built hull is docked at the yard.</param>
        /// <param name="buildInProgress">
        /// Shipyard only: whether any player has a live ship-blueprint build
        /// (materials bill open/filling) or a frame-design edit session on it.
        /// </param>
        /// <param name="craftInProgress">
        /// Whether any player's craft session has a recipe selected at this
        /// station (Assembly Station parts craft).
        /// </param>
        /// <param name="materialsLoaded">
        /// Whether any player's craft session at this station holds slotted
        /// materials - packing the station would silently eat their reservation.
        /// </param>
        /// <param name="reservedByOther">Whether another player holds the pickup reservation.</param>
        /// <param name="distanceMetres">
        /// Straight-line distance from the player's last relayed world position to
        /// the station, or null when the server has no trustworthy world-space
        /// position (relay v2 off, no movement yet, or the player is aboard a ship
        /// and their 190602 is parent-relative). Null SKIPS the range check and
        /// trusts the client's own two-stage range check, exactly as the atlas
        /// pickup does.
        /// </param>
        /// <param name="radiusMetres">
        /// The 1210 interaction radius the serve branch advertises for placed
        /// stations (ShipyardInteraction.CraftRadius). The allowed distance is
        /// this plus <see cref="CompletionLeewayMetres"/>.
        /// </param>
        public static StationPickupDecision Evaluate(
            bool peerOwnsPlayer,
            bool verbIsPickUp,
            bool alreadyPickedUp,
            PickupStationKind kind,
            string? ownerCharacterUid,
            string? requesterCharacterUid,
            bool shipDocked,
            bool buildInProgress,
            bool craftInProgress,
            bool materialsLoaded,
            bool reservedByOther,
            double? distanceMetres,
            double radiusMetres)
        {
            if (!peerOwnsPlayer)
            {
                return new StationPickupDecision(StationPickupOutcome.NotOwner);
            }
            if (!verbIsPickUp)
            {
                return new StationPickupDecision(StationPickupOutcome.WrongVerb);
            }
            if (alreadyPickedUp)
            {
                return new StationPickupDecision(StationPickupOutcome.AlreadyPickedUp);
            }
            if (kind == PickupStationKind.None)
            {
                return new StationPickupDecision(StationPickupOutcome.NotAStation);
            }
            if (!string.IsNullOrEmpty(ownerCharacterUid)
                && !string.Equals(ownerCharacterUid, requesterCharacterUid, StringComparison.Ordinal))
            {
                return new StationPickupDecision(StationPickupOutcome.NotYourStation);
            }
            if (shipDocked)
            {
                return new StationPickupDecision(StationPickupOutcome.ShipDocked);
            }
            if (buildInProgress)
            {
                return new StationPickupDecision(StationPickupOutcome.BuildInProgress);
            }
            if (craftInProgress)
            {
                return new StationPickupDecision(StationPickupOutcome.CraftInProgress);
            }
            if (materialsLoaded)
            {
                return new StationPickupDecision(StationPickupOutcome.MaterialsLoaded);
            }
            if (reservedByOther)
            {
                return new StationPickupDecision(StationPickupOutcome.ReservedByOther);
            }
            if (distanceMetres.HasValue && distanceMetres.Value > radiusMetres + CompletionLeewayMetres)
            {
                return new StationPickupDecision(StationPickupOutcome.TooFar);
            }
            return new StationPickupDecision(StationPickupOutcome.Grant);
        }
    }
}
