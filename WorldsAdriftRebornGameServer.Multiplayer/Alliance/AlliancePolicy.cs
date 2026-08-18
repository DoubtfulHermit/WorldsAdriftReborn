namespace WorldsAdriftRebornGameServer.Multiplayer.Alliance
{
    /// <summary>
    /// Why an alliance action was refused.
    ///
    /// Richer than the client's error vocabulary on purpose. The client's table is
    /// closed and has no word for several of these, so the translation to a wire
    /// code is lossy in one direction and lives at the edge - see
    /// <c>AllianceEndpoints.VerdictCode</c>. Losing the distinction HERE would mean
    /// the rules could not be tested for saying different things about different
    /// situations.
    /// </summary>
    public enum AllianceVerdict
    {
        Ok,
        NotInAnAlliance,
        AlreadyInThisAlliance,
        AlreadyInAnotherAlliance,
        NotAMember,
        NoSuchAlliance,
        NameTaken,
        NameNotAllowed,
        AtCapacity,
        RequestLimitMet,
        AlreadyRequested,
        CannotInviteYourself,
        NotPermitted,
        CannotBootYourself,
        CannotBootTheLeader,
        UnknownPlayer,
        RankNotEditable,
        NoSuchRank,
        RankBelongsToAnotherAlliance,
    }

    /// <summary>
    /// The rules of an alliance, as pure decisions over an
    /// <see cref="AllianceLedger"/>.
    ///
    /// Everything here is a decision, not an effect. The caller applies the
    /// resulting mutation and writes the rows; this type never touches the wire,
    /// the database or the clock, so the rules can be asserted exhaustively and
    /// cannot disagree with themselves depending on which endpoint asked.
    ///
    /// Alliances differ from crews in one structural way that shapes all of this:
    /// a crew's permissions are "leader or not", while an alliance's are a RANK
    /// carrying a permission set the founder can shape. So almost every question
    /// below resolves to "which permission does this action need, and does the
    /// actor's rank grant it" - with the leader short-circuit the client itself
    /// applies (<c>editMembers = permissions.Contains("edit_members") || isLeader</c>,
    /// SocialGroupParsers.cs:134).
    /// </summary>
    public static class AlliancePolicy
    {
        /// <summary>
        /// WAREBORN TUNING, not recovered.
        ///
        /// The client's GameDB carries <c>SocialConstants[ALLIANCE].MAX_MEMBERS</c>
        /// - proof retail had a number - but the row data lived in Bossa's remote
        /// GameDB and no value survives in the shipped install. There is nothing to
        /// recover, so a number is CHOSEN and labelled rather than dressed up as
        /// the original.
        ///
        /// Unlike the crew cap this is NOT a rendering limit. The alliance member
        /// list instantiates one widget per member through <c>UIObjectFactory</c>
        /// behind a <c>ScrollPaginator</c> (AllianceMembersList.CreateListObjects),
        /// so there is no fixed widget budget to overrun and no client crash
        /// waiting behind a large alliance - which is exactly the hazard
        /// CrewRosterLimits exists to prevent for crews. The only reason for a
        /// ceiling here is that <c>alliance_at_capacity</c> exists in the client's
        /// vocabulary and an unbounded roster is an unbounded response body.
        /// </summary>
        public const int DefaultMaxMembers = 100;

        /// <summary>
        /// WAREBORN TUNING, same provenance as <see cref="DefaultMaxMembers"/>:
        /// <c>SocialConstants[ALLIANCE]</c> has MAX_APPS and MAX_INVITES fields
        /// with no values anywhere.
        ///
        /// One number covers both directions because the store holds invites and
        /// applications in one table and the client draws them from one call.
        /// </summary>
        public const int DefaultMaxLiveRequests = 50;

        // ------------------------------------------------------------- founding

        /// <summary>
        /// May this player found an alliance called that?
        ///
        /// The name checks run before the membership check on purpose: a player
        /// already in an alliance who types a taken name should be told the thing
        /// they can act on first. Both refusals are honest either way; this is
        /// only about which sentence they read.
        /// </summary>
        public static AllianceVerdict MayCreate(AllianceLedger ledger, string founderUid, string? name)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(founderUid)) return AllianceVerdict.UnknownPlayer;

            if (!AllianceNamePolicy.IsAcceptable(name)) return AllianceVerdict.NameNotAllowed;
            if (ledger.NameTaken(name!)) return AllianceVerdict.NameTaken;

            return ledger.AllianceOf(founderUid) != null
                ? AllianceVerdict.AlreadyInAnotherAlliance
                : AllianceVerdict.Ok;
        }

        // ------------------------------------------------------------ membership

        /// <summary>
        /// May the actor invite this player into the actor's own alliance?
        ///
        /// Needs <see cref="AlliancePermissions.EditMembers"/>. Note the invitee
        /// being in ANOTHER alliance is a refusal rather than a silent no-op: the
        /// client shows the inviter a name it found through character search, which
        /// says nothing about membership, so "already_in_alliance" is the only way
        /// they learn why.
        /// </summary>
        public static AllianceVerdict MayInvite(
            AllianceLedger ledger, string inviterUid, string inviteeUid, int maxLiveRequests = DefaultMaxLiveRequests)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(inviterUid) || string.IsNullOrWhiteSpace(inviteeUid))
            {
                return AllianceVerdict.UnknownPlayer;
            }

            if (string.Equals(inviterUid, inviteeUid, StringComparison.Ordinal))
            {
                return AllianceVerdict.CannotInviteYourself;
            }

            Alliance? alliance = ledger.AllianceOf(inviterUid);
            if (alliance == null) return AllianceVerdict.NotInAnAlliance;

            AllianceVerdict permitted = Requires(alliance, inviterUid, AlliancePermissions.EditMembers);
            if (permitted != AllianceVerdict.Ok) return permitted;

            if (alliance.Holds(inviteeUid)) return AllianceVerdict.AlreadyInThisAlliance;
            if (ledger.AllianceOf(inviteeUid) != null) return AllianceVerdict.AlreadyInAnotherAlliance;
            if (ledger.HasLiveRequest(inviteeUid, alliance.Id)) return AllianceVerdict.AlreadyRequested;

            return CapacityFor(ledger, alliance, maxLiveRequests);
        }

        /// <summary>
        /// May this player apply to that alliance?
        ///
        /// No permission is involved - an application is made by somebody who is
        /// not a member yet - but every other bound still holds, including the
        /// capacity one: an alliance that is full should not accumulate hopefuls it
        /// cannot seat.
        /// </summary>
        public static AllianceVerdict MayApply(
            AllianceLedger ledger, string applicantUid, string allianceId, int maxLiveRequests = DefaultMaxLiveRequests)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(applicantUid)) return AllianceVerdict.UnknownPlayer;

            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null) return AllianceVerdict.NoSuchAlliance;

            if (alliance.Holds(applicantUid)) return AllianceVerdict.AlreadyInThisAlliance;
            if (ledger.AllianceOf(applicantUid) != null) return AllianceVerdict.AlreadyInAnotherAlliance;
            if (ledger.HasLiveRequest(applicantUid, alliance.Id)) return AllianceVerdict.AlreadyRequested;

            return CapacityFor(ledger, alliance, maxLiveRequests);
        }

        /// <summary>
        /// May this player actually be seated now?
        ///
        /// Asked at ACCEPT time rather than trusting the check made when the offer
        /// was sent, because an alliance can fill up, dissolve, or take the player
        /// in by another route between the two.
        /// </summary>
        public static AllianceVerdict MayJoin(
            AllianceLedger ledger, string uid, string allianceId, int maxMembers = DefaultMaxMembers)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(uid)) return AllianceVerdict.UnknownPlayer;

            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null) return AllianceVerdict.NoSuchAlliance;

            if (alliance.Holds(uid)) return AllianceVerdict.AlreadyInThisAlliance;
            if (ledger.AllianceOf(uid) != null) return AllianceVerdict.AlreadyInAnotherAlliance;
            if (alliance.Members.Count >= maxMembers) return AllianceVerdict.AtCapacity;

            return AllianceVerdict.Ok;
        }

        /// <summary>
        /// May the actor throw this member out?
        ///
        /// Needs <see cref="AlliancePermissions.EditMembers"/>, and the founder is
        /// untouchable. Booting yourself is refused here because the client sends
        /// the SAME request for leaving - the endpoint decides which it is by
        /// comparing the two uids and asks <see cref="MayLeave"/> instead, so
        /// reaching this with actor == target means a caller confused the two.
        /// </summary>
        public static AllianceVerdict MayBoot(AllianceLedger ledger, string actorUid, string targetUid)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(actorUid) || string.IsNullOrWhiteSpace(targetUid))
            {
                return AllianceVerdict.UnknownPlayer;
            }

            if (string.Equals(actorUid, targetUid, StringComparison.Ordinal))
            {
                return AllianceVerdict.CannotBootYourself;
            }

            Alliance? alliance = ledger.AllianceOf(actorUid);
            if (alliance == null) return AllianceVerdict.NotInAnAlliance;
            if (!alliance.Holds(targetUid)) return AllianceVerdict.NotAMember;
            if (alliance.IsLeader(targetUid)) return AllianceVerdict.CannotBootTheLeader;

            return Requires(alliance, actorUid, AlliancePermissions.EditMembers);
        }

        /// <summary>
        /// May this player walk out?
        ///
        /// Always, if they are in one. The founder leaving is allowed and hands the
        /// alliance on - see <see cref="SuccessorTo"/> - because the alternative is
        /// a player permanently trapped in a group they founded.
        /// </summary>
        public static AllianceVerdict MayLeave(AllianceLedger ledger, string uid)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            return ledger.AllianceOf(uid) == null
                ? AllianceVerdict.NotInAnAlliance
                : AllianceVerdict.Ok;
        }

        /// <summary>
        /// Only the founder may dissolve the alliance outright.
        ///
        /// Not <see cref="AlliancePermissions.EditMembers"/>, however sweeping that
        /// permission is: booting everyone one at a time is recoverable and
        /// visible, and destroying the group is neither. The client agrees - the
        /// DISBAND button lives in the founder's own settings state.
        /// </summary>
        public static AllianceVerdict MayDisband(AllianceLedger ledger, string actorUid, string allianceId)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null) return AllianceVerdict.NoSuchAlliance;

            return alliance.IsLeader(actorUid) ? AllianceVerdict.Ok : AllianceVerdict.NotPermitted;
        }

        // ------------------------------------------------------------ the group

        /// <summary>
        /// May the actor edit the alliance's DESCRIPTION? Needs
        /// <see cref="AlliancePermissions.EditGroup"/> - the same permission the
        /// client gates its description field on.
        /// </summary>
        public static AllianceVerdict MayEditDescription(AllianceLedger ledger, string actorUid, string allianceId) =>
            RequiresIn(ledger, actorUid, allianceId, AlliancePermissions.EditGroup);

        /// <summary>
        /// May the actor edit the MESSAGE OF THE DAY?
        ///
        /// Gated on <see cref="AlliancePermissions.MotdIsReadFrom"/>, which is
        /// <c>leader_chat</c> and NOT <c>edit_message_of_the_day</c>. That looks
        /// wrong and is deliberate: the client reads its own MOTD gate off
        /// <c>leader_chat</c> (SocialGroupParsers.cs:129, a retail bug recorded in
        /// docs/research/findings-social-api.md). Enforcing the honest name here
        /// would make the server and the UI disagree about who may type in the box.
        /// </summary>
        public static AllianceVerdict MayEditMessageOfTheDay(AllianceLedger ledger, string actorUid, string allianceId) =>
            RequiresIn(ledger, actorUid, allianceId, AlliancePermissions.MotdIsReadFrom);

        /// <summary>May the actor create, change or delete ranks?</summary>
        public static AllianceVerdict MayEditRanks(AllianceLedger ledger, string actorUid, string allianceId) =>
            RequiresIn(ledger, actorUid, allianceId, AlliancePermissions.EditRanks);

        /// <summary>
        /// May the actor delete this particular rank?
        ///
        /// The two default ranks are refused with the client's own
        /// <c>uneditable_rank</c>: they are not decoration, they are the slots
        /// <c>AllianceRankInformation.CreateLookup</c> fills its <c>Leader</c> and
        /// <c>BasicMember</c> fields from, and an alliance missing either leaves
        /// those null and breaks the panel that reads them.
        /// </summary>
        public static AllianceVerdict MayDeleteRank(AllianceLedger ledger, string actorUid, string rankId)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Alliance? alliance = ledger.AllianceOf(actorUid);
            if (alliance == null) return AllianceVerdict.NotInAnAlliance;

            AllianceRank? rank = alliance.RankById(rankId);
            if (rank == null) return AllianceVerdict.NoSuchRank;
            if (!rank.Editable) return AllianceVerdict.RankNotEditable;

            return Requires(alliance, actorUid, AlliancePermissions.EditRanks);
        }

        /// <summary>May the actor move this member onto that rank?</summary>
        public static AllianceVerdict MaySetRank(
            AllianceLedger ledger, string actorUid, string targetUid, string rankId)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Alliance? alliance = ledger.AllianceOf(actorUid);
            if (alliance == null) return AllianceVerdict.NotInAnAlliance;
            if (!alliance.Holds(targetUid)) return AllianceVerdict.NotAMember;

            AllianceRank? rank = alliance.RankById(rankId);
            if (rank == null) return AllianceVerdict.NoSuchRank;

            // Nobody is promoted INTO the founder's rank by a rank change. The
            // founder is identified by leaderCharacterUid as well as by rank, and
            // handing out the leader rank alone would produce two members the
            // client draws as leader and one alliance that disagrees with itself.
            if (rank.IsDefaultLeader) return AllianceVerdict.RankNotEditable;

            return Requires(alliance, actorUid, AlliancePermissions.EditMembers);
        }

        /// <summary>
        /// May the actor write this member's PUBLIC officer note?
        ///
        /// The client sends this as <c>publicOfficerNote</c> and reads it back as
        /// <c>officerNote</c> - the names do not match across the two directions
        /// and both are load-bearing. Gated on
        /// <see cref="AlliancePermissions.EditOfficerNote"/>, one of the two
        /// permissions the client can read but has no UI to grant, so in practice
        /// only a rank the server built carries it.
        /// </summary>
        public static AllianceVerdict MayEditOfficerNote(AllianceLedger ledger, string actorUid, string targetUid)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Alliance? alliance = ledger.AllianceOf(actorUid);
            if (alliance == null) return AllianceVerdict.NotInAnAlliance;
            if (!alliance.Holds(targetUid)) return AllianceVerdict.NotAMember;

            return Requires(alliance, actorUid, AlliancePermissions.EditOfficerNote);
        }

        /// <summary>
        /// May the actor write a member's PRIVATE officer note?
        ///
        /// Same permission as the public one. The distinction between the two notes
        /// is who may READ them, and the client draws both from the same member
        /// record, so there is no second permission to gate this with.
        /// </summary>
        public static AllianceVerdict MayEditPrivateNote(AllianceLedger ledger, string actorUid, string targetUid) =>
            MayEditOfficerNote(ledger, actorUid, targetUid);

        // ------------------------------------------------------------ succession

        /// <summary>
        /// Who inherits when <paramref name="leavingUid"/> walks out - the
        /// longest-standing remaining member, or null if there is nobody.
        ///
        /// Join order rather than rank, deliberately. Rank is a permission set the
        /// founder shaped, not a line of succession, and picking "the highest rank"
        /// would need an ordering over ranks that neither the client nor the
        /// original service ever defined. Seniority is defined, is visible, and
        /// matches how crews already do it.
        /// </summary>
        public static string? SuccessorTo(Alliance alliance, string leavingUid)
        {
            if (alliance == null) throw new ArgumentNullException(nameof(alliance));

            foreach (string uid in alliance.Members)
            {
                if (!string.Equals(uid, leavingUid, StringComparison.Ordinal)) return uid;
            }

            return null;
        }

        // --------------------------------------------------------------- helpers

        private static AllianceVerdict CapacityFor(AllianceLedger ledger, Alliance alliance, int maxLiveRequests)
        {
            if (alliance.Members.Count >= DefaultMaxMembers) return AllianceVerdict.AtCapacity;

            return ledger.LiveRequestsFor(alliance.Id) >= Math.Max(0, maxLiveRequests)
                ? AllianceVerdict.RequestLimitMet
                : AllianceVerdict.Ok;
        }

        private static AllianceVerdict RequiresIn(
            AllianceLedger ledger, string actorUid, string allianceId, string permission)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));

            Alliance? alliance = ledger.ById(allianceId);
            if (alliance == null) return AllianceVerdict.NoSuchAlliance;

            // Acting on an alliance you are not in is "not a member", not "not
            // permitted": no rank of theirs could ever grant it.
            if (!alliance.Holds(actorUid)) return AllianceVerdict.NotAMember;

            return Requires(alliance, actorUid, permission);
        }

        /// <summary>
        /// The one place a permission is actually tested, including the leader
        /// short-circuit.
        ///
        /// The founder is allowed everything regardless of the permissions listed
        /// on their rank. That mirrors the client, which ORs <c>edit_members</c>
        /// with "is the default leader rank" (SocialGroupParsers.cs:134), and it
        /// closes a trap the rank editor would otherwise open: a founder who
        /// removed a permission from their own rank would lock themselves out of
        /// their own alliance with no way back in.
        /// </summary>
        private static AllianceVerdict Requires(Alliance alliance, string actorUid, string permission)
        {
            if (!alliance.Holds(actorUid)) return AllianceVerdict.NotAMember;
            if (alliance.IsLeader(actorUid)) return AllianceVerdict.Ok;

            AllianceRank? rank = alliance.RankOf(actorUid);
            return rank != null && rank.Grants(permission)
                ? AllianceVerdict.Ok
                : AllianceVerdict.NotPermitted;
        }
    }
}
