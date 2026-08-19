using WorldsAdriftRebornGameServer.Multiplayer.Alliance;

namespace WorldsAdriftServer.Portal
{
    /// <summary>
    /// One thing the account portal lets a player DO to an alliance.
    ///
    /// A closed list rather than a string, because the whole safety of the portal
    /// rests on the page and the handler asking the same question: the page draws
    /// a control only when the action is permitted, and the handler refuses the
    /// post when it is not. Two places naming an action in prose would eventually
    /// name two different actions.
    /// </summary>
    internal enum PortalAction
    {
        /// <summary>Rewrite the alliance's description.</summary>
        EditDescription,

        /// <summary>Rewrite the message of the day.</summary>
        EditMessageOfTheDay,

        /// <summary>Re-crest the alliance - the builder that was already here.</summary>
        EditEmblem,

        /// <summary>Move another member onto a different rank.</summary>
        SetMemberRank,

        /// <summary>Throw another member out.</summary>
        BootMember,

        /// <summary>
        /// Act FOR the alliance on its incoming applications and outgoing
        /// invites: accept, reject, rescind.
        /// </summary>
        AdmitOrRescind,

        /// <summary>Walk out yourself.</summary>
        Leave,
    }

    /// <summary>
    /// Which permission literal actually governs each portal action, and the
    /// verdict for one actor.
    ///
    /// THE MAPPING IS NOT GUESSABLE, which is the reason this table exists as
    /// data instead of being inlined at each call site. Three of these are
    /// counter-intuitive and every one of them was decided elsewhere, on evidence:
    ///
    /// <list type="bullet">
    ///   <item>The MESSAGE OF THE DAY is gated on <c>leader_chat</c>, NOT on the
    ///   honestly-named <c>edit_message_of_the_day</c>. The retail client reads
    ///   its own MOTD gate off <c>leader_chat</c> (SocialGroupParsers.cs:129), a
    ///   bug this server reproduces deliberately - see
    ///   <see cref="AlliancePermissions.MotdIsReadFrom"/>. A rank carrying only
    ///   <c>edit_message_of_the_day</c> may NOT edit the MOTD, here or in game.</item>
    ///   <item>The EMBLEM is gated on <c>edit_group</c>, the DESCRIPTION's
    ///   permission - not on the MOTD's. The client has no emblem gate of its own
    ///   to disagree with, so the honest permission was available where it was not
    ///   available for the MOTD; see
    ///   <see cref="AlliancePolicy.MayEditEmblem"/>.</item>
    ///   <item>ADMITTING is a RANK permission (<c>edit_members</c>), not "are you
    ///   the founder". The client shows the APPLICATIONS tab to any rank holding
    ///   it, so a portal that only let the founder accept would draw a button that
    ///   always failed.</item>
    /// </list>
    ///
    /// Pure, and it decides nothing itself: every verdict below is
    /// <see cref="AlliancePolicy"/>'s, delegated. That is the point - the portal
    /// must not be a second opinion about who may do what. If a rule changes in
    /// the policy the portal changes with it, and the tests here assert the
    /// delegation rather than restating the rule.
    /// </summary>
    internal static class PortalPermissions
    {
        /// <summary>
        /// The permission literal an action needs, for display. NOT used to make
        /// the decision - <see cref="May"/> does that through the policy - but the
        /// portal names the permission when it refuses, and a name that drifted
        /// from the check would be worse than no name.
        /// </summary>
        internal static string PermissionFor(PortalAction action) => action switch
        {
            PortalAction.EditDescription => AlliancePermissions.EditGroup,
            PortalAction.EditEmblem => AlliancePermissions.EditGroup,

            // leader_chat, on purpose. See the type remarks.
            PortalAction.EditMessageOfTheDay => AlliancePermissions.MotdIsReadFrom,

            PortalAction.SetMemberRank => AlliancePermissions.EditMembers,
            PortalAction.BootMember => AlliancePermissions.EditMembers,
            PortalAction.AdmitOrRescind => AlliancePermissions.EditMembers,

            // Leaving needs nothing but membership; there is no permission to name.
            PortalAction.Leave => string.Empty,

            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown portal action."),
        };

        /// <summary>
        /// May this actor perform this action on this alliance?
        ///
        /// <paramref name="targetKey"/> is required by the two actions that name
        /// another member and ignored by the rest. A missing target on one of
        /// those is <see cref="AllianceVerdict.UnknownPlayer"/> rather than an
        /// exception: it arrives from a form, and a form that omitted a field must
        /// be refused like any other bad request.
        /// </summary>
        internal static AllianceVerdict May(
            AllianceLedger ledger,
            PortalAction action,
            string actorKey,
            string allianceId,
            string? targetKey = null)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(actorKey)) return AllianceVerdict.UnknownPlayer;
            if (string.IsNullOrWhiteSpace(allianceId)) return AllianceVerdict.NoSuchAlliance;

            switch (action)
            {
                case PortalAction.EditDescription:
                    return AlliancePolicy.MayEditDescription(ledger, actorKey, allianceId);

                case PortalAction.EditMessageOfTheDay:
                    return AlliancePolicy.MayEditMessageOfTheDay(ledger, actorKey, allianceId);

                case PortalAction.EditEmblem:
                    return AlliancePolicy.MayEditEmblem(ledger, actorKey, allianceId);

                case PortalAction.AdmitOrRescind:
                    return AlliancePolicy.MayAdmit(ledger, actorKey, allianceId);

                case PortalAction.Leave:
                    return InThisAlliance(ledger, actorKey, allianceId)
                        ?? AlliancePolicy.MayLeave(ledger, actorKey);

                case PortalAction.SetMemberRank:
                case PortalAction.BootMember:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown portal action.");
            }

            if (string.IsNullOrWhiteSpace(targetKey)) return AllianceVerdict.UnknownPlayer;

            // Both member actions resolve the alliance from the ACTOR, so the
            // alliance the form named has to be checked against the one they are
            // actually in - otherwise a member of X posting Y's id is answered
            // about X, and succeeds against a group they never named. Exactly the
            // guard AllianceEndpoints.RemoveMember applies for the same reason.
            AllianceVerdict? mismatch = InThisAlliance(ledger, actorKey, allianceId);
            if (mismatch != null) return mismatch.Value;

            return action == PortalAction.BootMember
                ? AlliancePolicy.MayBoot(ledger, actorKey, targetKey!)
                : AlliancePolicy.MaySetRank(ledger, actorKey, targetKey!, RankOf(ledger, targetKey!));
        }

        /// <summary>
        /// May the actor move <paramref name="targetKey"/> onto
        /// <paramref name="rankId"/> specifically?
        ///
        /// Separate from <see cref="May"/> because a rank change names a THIRD
        /// thing - the rank - and <see cref="AlliancePolicy.MaySetRank"/> refuses
        /// some ranks outright (nobody is promoted into the founder's). The page
        /// asks the general question to decide whether to draw the control; the
        /// handler asks this one before it writes.
        /// </summary>
        internal static AllianceVerdict MaySetRank(
            AllianceLedger ledger, string actorKey, string allianceId, string targetKey, string rankId)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            if (string.IsNullOrWhiteSpace(actorKey) || string.IsNullOrWhiteSpace(targetKey))
            {
                return AllianceVerdict.UnknownPlayer;
            }

            AllianceVerdict? mismatch = InThisAlliance(ledger, actorKey, allianceId);
            if (mismatch != null) return mismatch.Value;

            return AlliancePolicy.MaySetRank(ledger, actorKey, targetKey, rankId);
        }

        /// <summary>
        /// Every action's verdict at once, for the page.
        ///
        /// One call so a section cannot be drawn from a different ledger than the
        /// one the neighbouring section was drawn from.
        /// </summary>
        internal static AllianceRights RightsFor(AllianceLedger ledger, string actorKey, string allianceId)
        {
            return new AllianceRights(
                EditDescription: May(ledger, PortalAction.EditDescription, actorKey, allianceId) == AllianceVerdict.Ok,
                EditMessageOfTheDay: May(ledger, PortalAction.EditMessageOfTheDay, actorKey, allianceId) == AllianceVerdict.Ok,
                EditEmblem: May(ledger, PortalAction.EditEmblem, actorKey, allianceId) == AllianceVerdict.Ok,
                ManageMembers: May(ledger, PortalAction.AdmitOrRescind, actorKey, allianceId) == AllianceVerdict.Ok);
        }

        /// <summary>
        /// Null when the actor is a member of exactly the alliance named, and the
        /// refusal to return otherwise.
        /// </summary>
        private static AllianceVerdict? InThisAlliance(AllianceLedger ledger, string actorKey, string allianceId)
        {
            Alliance? mine = ledger.AllianceOf(actorKey);
            if (mine == null) return AllianceVerdict.NotInAnAlliance;

            return string.Equals(mine.Id, allianceId, StringComparison.Ordinal)
                ? null
                : AllianceVerdict.NotAMember;
        }

        /// <summary>
        /// The rank a member currently holds, used only to ask the general "may
        /// you change ranks at all" question without naming a new one. Empty when
        /// they hold none, which <see cref="AlliancePolicy.MaySetRank"/> answers
        /// with <see cref="AllianceVerdict.NoSuchRank"/> - and NOT with a
        /// permission verdict, so the page never draws a control on the strength
        /// of a missing rank.
        /// </summary>
        private static string RankOf(AllianceLedger ledger, string targetKey)
        {
            Alliance? alliance = ledger.AllianceOf(targetKey);
            AllianceRank? rank = alliance?.RankOf(targetKey);

            // The DEFAULT MEMBER rank rather than the one they hold: asking "may
            // you move this person onto the rank they already have" would answer
            // RankNotEditable for the founder and hide the control for everyone
            // beneath them too. The default member rank is the one every rank
            // change can legally target.
            return alliance?.DefaultMemberRank?.Id ?? rank?.Id ?? string.Empty;
        }
    }

    /// <summary>
    /// What one signed-in character may do to one alliance, as four booleans the
    /// page can draw from.
    ///
    /// Booleans rather than verdicts because the PAGE only ever asks "draw this or
    /// not" - the reason for a refusal belongs on the response to a post that was
    /// actually attempted, where it can name the permission.
    /// </summary>
    internal sealed record AllianceRights(
        bool EditDescription,
        bool EditMessageOfTheDay,
        bool EditEmblem,
        bool ManageMembers)
    {
        /// <summary>True when there is nothing at all to manage - the card renders
        /// read-only and says so once rather than hiding four controls silently.</summary>
        public bool Nothing =>
            !EditDescription && !EditMessageOfTheDay && !EditEmblem && !ManageMembers;
    }
}
