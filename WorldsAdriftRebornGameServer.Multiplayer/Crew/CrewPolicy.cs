namespace WorldsAdriftRebornGameServer.Multiplayer.Crew
{
    /// <summary>
    /// Why a crew action was refused. The retail UI has exactly one line of text
    /// and a success flag to report with (<c>CrewManagementFeedback</c>: Msg,
    /// Result), so every refusal must be expressible as one sentence a player can
    /// act on.
    /// </summary>
    public enum CrewVerdict
    {
        Ok,
        NotInACrew,
        AlreadyInThisCrew,
        AlreadyInAnotherCrew,
        NoSuchInvite,
        NotTheLeader,
        CannotBootYourself,
        NotAMember,
        CrewIsFull,
        SlotTaken,
        SlotOutOfRange,
        UnknownPlayer,
        CannotInviteYourself,
        AlreadyInvited,
    }

    /// <summary>
    /// The rules of a crew, as pure decisions over a <see cref="CrewLedger"/>.
    ///
    /// Retail's crew is a leader plus slotted members, with invites held on the
    /// INVITEE (6900 <c>InvitesReceived</c> is a map on the invited player's own
    /// component, not a list on the crew). That shape is load-bearing: it means a
    /// player can hold invites from several crews at once and accept one, and it
    /// means an invite survives the inviter going offline.
    ///
    /// Everything here is a decision, not an effect. The caller applies the
    /// resulting mutation and pushes the components; this type never touches the
    /// wire, the database or the clock, so the rules can be tested exhaustively
    /// and cannot disagree with themselves depending on who asked.
    /// </summary>
    public static class CrewPolicy
    {
        /// <summary>
        /// Retail's crew size. The UI lays out a fixed set of slots and
        /// <c>NumSlots</c> is a component field, so this is the default a new crew
        /// starts with rather than a hard ceiling baked into the rules.
        /// </summary>
        public const int DefaultSlots = 4;

        public const int MaxSlots = 8;

        public static CrewVerdict MayInvite(CrewLedger ledger, string inviterUid, string inviteeUid)
        {
            if (string.IsNullOrWhiteSpace(inviterUid) || string.IsNullOrWhiteSpace(inviteeUid))
                return CrewVerdict.UnknownPlayer;
            if (string.Equals(inviterUid, inviteeUid, StringComparison.Ordinal))
                return CrewVerdict.CannotInviteYourself;

            Crew? crew = ledger.CrewOf(inviterUid);

            // Inviting while crewless is how a crew is FOUNDED, so it is allowed;
            // the caller creates the crew and the inviter leads it.
            if (crew != null)
            {
                if (!crew.IsLeader(inviterUid)) return CrewVerdict.NotTheLeader;
                if (crew.Members.Contains(inviteeUid)) return CrewVerdict.AlreadyInThisCrew;
                if (crew.IsFull) return CrewVerdict.CrewIsFull;
                if (ledger.HasInviteFrom(inviteeUid, crew.Id)) return CrewVerdict.AlreadyInvited;
            }

            if (ledger.CrewOf(inviteeUid) != null) return CrewVerdict.AlreadyInAnotherCrew;

            return CrewVerdict.Ok;
        }

        public static CrewVerdict MayAccept(CrewLedger ledger, string inviteeUid, string crewId)
        {
            if (ledger.CrewOf(inviteeUid) != null) return CrewVerdict.AlreadyInAnotherCrew;
            if (!ledger.HasInviteFrom(inviteeUid, crewId)) return CrewVerdict.NoSuchInvite;

            Crew? crew = ledger.ById(crewId);
            if (crew == null) return CrewVerdict.NoSuchInvite;
            if (crew.IsFull) return CrewVerdict.CrewIsFull;

            return CrewVerdict.Ok;
        }

        public static CrewVerdict MayReject(CrewLedger ledger, string inviteeUid, string crewId) =>
            ledger.HasInviteFrom(inviteeUid, crewId) ? CrewVerdict.Ok : CrewVerdict.NoSuchInvite;

        public static CrewVerdict MayBoot(CrewLedger ledger, string actorUid, string targetUid)
        {
            Crew? crew = ledger.CrewOf(actorUid);
            if (crew == null) return CrewVerdict.NotInACrew;
            if (!crew.IsLeader(actorUid)) return CrewVerdict.NotTheLeader;
            if (string.Equals(actorUid, targetUid, StringComparison.Ordinal))
                return CrewVerdict.CannotBootYourself;
            if (!crew.Members.Contains(targetUid)) return CrewVerdict.NotAMember;

            return CrewVerdict.Ok;
        }

        public static CrewVerdict MayLeave(CrewLedger ledger, string actorUid) =>
            ledger.CrewOf(actorUid) == null ? CrewVerdict.NotInACrew : CrewVerdict.Ok;

        public static CrewVerdict MayTakeSlot(CrewLedger ledger, string actorUid, int slot)
        {
            Crew? crew = ledger.CrewOf(actorUid);
            if (crew == null) return CrewVerdict.NotInACrew;
            if (slot < 0 || slot >= crew.NumSlots) return CrewVerdict.SlotOutOfRange;

            string? occupant = crew.OccupantOf(slot);
            if (occupant != null && !string.Equals(occupant, actorUid, StringComparison.Ordinal))
                return CrewVerdict.SlotTaken;

            return CrewVerdict.Ok;
        }

        /// <summary>
        /// Who leads after <paramref name="leavingUid"/> goes, or null when the
        /// crew should disband.
        ///
        /// The longest-standing remaining member succeeds. Retail's rule is not
        /// recorded in anything we hold, so this is OUR choice, chosen because it
        /// is the only one that needs no extra state and cannot be gamed by
        /// rejoining: join order is already the member list's order.
        /// </summary>
        public static string? SuccessorTo(Crew crew, string leavingUid)
        {
            foreach (string member in crew.Members)
                if (!string.Equals(member, leavingUid, StringComparison.Ordinal)) return member;
            return null;
        }

        /// <summary>The one line the retail UI will show. Present tense, actionable.</summary>
        public static string Explain(CrewVerdict verdict) => verdict switch
        {
            CrewVerdict.Ok => "Done.",
            CrewVerdict.NotInACrew => "You are not in a crew.",
            CrewVerdict.AlreadyInThisCrew => "They are already in your crew.",
            CrewVerdict.AlreadyInAnotherCrew => "They are already in another crew.",
            CrewVerdict.NoSuchInvite => "That invite is no longer available.",
            CrewVerdict.NotTheLeader => "Only the crew leader can do that.",
            CrewVerdict.CannotBootYourself => "You cannot remove yourself; leave the crew instead.",
            CrewVerdict.NotAMember => "They are not in your crew.",
            CrewVerdict.CrewIsFull => "Your crew is full.",
            CrewVerdict.SlotTaken => "That slot is taken.",
            CrewVerdict.SlotOutOfRange => "That slot does not exist.",
            CrewVerdict.UnknownPlayer => "No such player.",
            CrewVerdict.CannotInviteYourself => "You cannot invite yourself.",
            CrewVerdict.AlreadyInvited => "They already have an invite from your crew.",
            _ => "That did not work.",
        };
    }
}
