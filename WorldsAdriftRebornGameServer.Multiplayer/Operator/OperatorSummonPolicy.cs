namespace WorldsAdriftRebornGameServer.Multiplayer.Operator
{
    /// <summary>One live built hull, as far as the summon rule is concerned.</summary>
    public readonly record struct OperatorHull(long HullEntityId, string OwnerCharacterUid);

    public enum OperatorSummonVerdict
    {
        /// <summary>Bring this hull.</summary>
        Summon,

        /// <summary>The named hull is not a live built ship.</summary>
        NoSuchHull,

        /// <summary>"owned" was asked for and the character owns nothing.</summary>
        OwnsNothing,

        /// <summary>"owned" was asked for and the character owns more than one.</summary>
        OwnsSeveral,

        /// <summary>
        /// "owned" was asked for and the target has no durable character uid yet -
        /// their entity is still on a volatile session key, so there is no identity
        /// to match ownership against.
        /// </summary>
        NoCharacterIdentity,
    }

    public readonly record struct OperatorSummonChoice(
        OperatorSummonVerdict Verdict,
        long HullEntityId,
        string Reason,
        bool OwnershipMismatch)
    {
        public bool Ok => Verdict == OperatorSummonVerdict.Summon;
    }

    /// <summary>
    /// WHICH SHIP a summon brings, and what it refuses to guess.
    ///
    /// WHAT "SUMMON" MEANS HERE. It RELOCATES a hull that already exists; it does
    /// not conjure a new one. That is not a shortcut, it is the only reading that
    /// keeps this server's ownership model intact. A ship in this world is built:
    /// a design is saved, a shipyard is placed and docked, materials are spent, and
    /// the resulting hull is written into world state with an
    /// <c>OwnerCharacterUid</c> that the client's own
    /// <c>ShipVisualizer.IsShipOwner</c> checks before it will let anyone aboard or
    /// build on it. A "spawn a fresh hull" command would have to invent a design,
    /// invent an owner, invent a shipyard dock link and invent the persistence row -
    /// four fabrications, each one a way to produce a ship that renders but that
    /// nobody can use, which is precisely the class of ownership breakage this
    /// project has already paid for once.
    ///
    /// So: summon = <c>recall</c>, generalised. The existing recall could only
    /// address a hull by its entity id and a player by theirs, both session-scoped;
    /// what is added is the ability to say "the ship THIS CHARACTER owns" durably,
    /// and to refuse rather than pick when that is not exactly one ship.
    ///
    /// OWNERSHIP IS READ, NEVER WRITTEN. Nothing here transfers a ship. Moving a
    /// hull leaves <c>OwnerCharacterUid</c>, the shipyard dock link and the
    /// registered-uid list exactly as they were - a summoned ship is the owner's
    /// ship, parked somewhere else. An operator naming a hull that belongs to
    /// somebody OTHER than the target is allowed (that is a legitimate thing to
    /// want) but is flagged, because it is also what a mis-click looks like.
    /// </summary>
    public static class OperatorSummonPolicy
    {
        public static OperatorSummonChoice Choose(
            OperatorHullSelector selector,
            string? targetCharacterUid,
            IReadOnlyList<OperatorHull> hulls)
        {
            if (hulls == null) throw new ArgumentNullException(nameof(hulls));

            string targetUid = OperatorTargetPolicy.CanonicalUidText(targetCharacterUid);

            if (selector.Kind == OperatorHullKind.Hull)
            {
                foreach (OperatorHull hull in hulls)
                {
                    if (hull.HullEntityId != selector.HullEntityId) continue;

                    string owner = OperatorTargetPolicy.CanonicalUidText(hull.OwnerCharacterUid);
                    bool mismatch = owner.Length > 0 && targetUid.Length > 0 && owner != targetUid;
                    return new OperatorSummonChoice(
                        OperatorSummonVerdict.Summon, hull.HullEntityId,
                        mismatch
                            ? "Hull " + hull.HullEntityId + " belongs to character " + owner
                              + ", not to the player it is being sent to. It is moved, not "
                              + "transferred: it stays their ship."
                            : string.Empty,
                        mismatch);
                }

                return new OperatorSummonChoice(
                    OperatorSummonVerdict.NoSuchHull, 0,
                    "Hull " + selector.HullEntityId + " is not a live built ship; "
                    + "refresh the world inspector and choose again.",
                    false);
            }

            if (targetUid.Length == 0)
            {
                return new OperatorSummonChoice(
                    OperatorSummonVerdict.NoCharacterIdentity, 0,
                    "That player has no character uid on this server yet, so 'the ship they "
                    + "own' cannot be resolved. Name the hull exactly with hull:<entityId>.",
                    false);
            }

            List<long> owned = new List<long>();
            foreach (OperatorHull hull in hulls)
            {
                if (OperatorTargetPolicy.CanonicalUidText(hull.OwnerCharacterUid) == targetUid)
                {
                    owned.Add(hull.HullEntityId);
                }
            }
            owned.Sort();

            if (owned.Count == 1)
            {
                return new OperatorSummonChoice(
                    OperatorSummonVerdict.Summon, owned[0], string.Empty, false);
            }

            if (owned.Count == 0)
            {
                return new OperatorSummonChoice(
                    OperatorSummonVerdict.OwnsNothing, 0,
                    "Character " + targetUid + " owns no built ship, and this command moves "
                    + "an existing ship rather than creating one. They have to build one first.",
                    false);
            }

            return new OperatorSummonChoice(
                OperatorSummonVerdict.OwnsSeveral, 0,
                "Character " + targetUid + " owns " + owned.Count + " ships ("
                + string.Join(", ", owned) + "); name one with hull:<entityId>.",
                false);
        }
    }
}
