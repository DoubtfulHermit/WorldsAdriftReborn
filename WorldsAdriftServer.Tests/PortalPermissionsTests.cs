using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using WorldsAdriftServer.Portal;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Who the account portal lets do what.
    ///
    /// These assert the DELEGATION and the counter-intuitive mapping, not the
    /// alliance rules themselves - those are already asserted exhaustively beside
    /// <see cref="AlliancePolicy"/> in the multiplayer suite, and restating them
    /// here would produce a second set of expectations that could go on passing
    /// after the real rule changed.
    ///
    /// What is genuinely portal-specific, and is therefore pinned hard below:
    ///
    /// <list type="bullet">
    ///   <item>the MOTD is <c>leader_chat</c>, not the honestly-named permission;</item>
    ///   <item>the EMBLEM is <c>edit_group</c> and NOT the MOTD's quirk;</item>
    ///   <item>an actor is answered about the alliance the FORM named, not the one
    ///   they happen to be in.</item>
    /// </list>
    /// </summary>
    public class PortalPermissionsTests
    {
        private const string Alliance = "11111111-1111-1111-1111-111111111111";
        private const string Other = "22222222-2222-2222-2222-222222222222";

        private const string Founder = "character:aaaaaaaa-0000-0000-0000-000000000001";
        private const string Officer = "character:aaaaaaaa-0000-0000-0000-000000000002";
        private const string Plain = "character:aaaaaaaa-0000-0000-0000-000000000003";
        private const string Stranger = "character:aaaaaaaa-0000-0000-0000-000000000009";

        private const string LeaderRank = "rank-leader";
        private const string MemberRank = "rank-member";
        private const string CustomRank = "rank-custom";

        /// <summary>
        /// An alliance with a founder, one plain member, and one officer whose
        /// custom rank carries exactly <paramref name="officerPermissions"/>.
        /// </summary>
        private static AllianceLedger Ledger(params string[] officerPermissions)
        {
            AllianceLedger ledger = new AllianceLedger();

            AllianceRank leader = new AllianceRank(
                LeaderRank, "Leader", false, AllianceRank.TypeLeader, AlliancePermissions.DefaultLeader);
            AllianceRank member = new AllianceRank(
                MemberRank, "Member", false, AllianceRank.TypeMember, AlliancePermissions.DefaultMember);

            Alliance seated = ledger.Create(Alliance, Founder, "The Kestrels", leader, member);
            seated.AddRank(new AllianceRank(
                CustomRank, "Officer", true, AllianceRank.TypeMember, officerPermissions));

            ledger.Join(Officer, Alliance, CustomRank);
            ledger.Join(Plain, Alliance, MemberRank);

            // A second alliance, so "you were answered about the one you named"
            // is a question with two possible answers.
            AllianceRank otherLeader = new AllianceRank(
                "o-leader", "Leader", false, AllianceRank.TypeLeader, AlliancePermissions.DefaultLeader);
            AllianceRank otherMember = new AllianceRank(
                "o-member", "Member", false, AllianceRank.TypeMember, AlliancePermissions.DefaultMember);
            ledger.Create(Other, Stranger, "The Wrens", otherLeader, otherMember);

            return ledger;
        }

        // ------------------------------------------------- the mapping that bites

        [Fact]
        public void TheMotdIsGatedOnLeaderChatAndNotOnItsOwnName()
        {
            // The honest permission, which the client never reads.
            AllianceLedger honest = Ledger(AlliancePermissions.EditMessageOfTheDay);

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(honest, PortalAction.EditMessageOfTheDay, Officer, Alliance));

            // The one it does read.
            AllianceLedger real = Ledger(AlliancePermissions.LeaderChat);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(real, PortalAction.EditMessageOfTheDay, Officer, Alliance));
        }

        [Fact]
        public void TheMotdPermissionNameIsLeaderChat()
        {
            Assert.Equal(
                AlliancePermissions.LeaderChat,
                PortalPermissions.PermissionFor(PortalAction.EditMessageOfTheDay));
        }

        [Fact]
        public void TheEmblemIsGatedOnEditGroupAndDoesNotInheritTheMotdQuirk()
        {
            AllianceLedger byGroup = Ledger(AlliancePermissions.EditGroup);
            AllianceLedger byChat = Ledger(AlliancePermissions.LeaderChat);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(byGroup, PortalAction.EditEmblem, Officer, Alliance));

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(byChat, PortalAction.EditEmblem, Officer, Alliance));

            Assert.Equal(
                AlliancePermissions.EditGroup,
                PortalPermissions.PermissionFor(PortalAction.EditEmblem));
        }

        [Fact]
        public void TheDescriptionAndTheEmblemShareOnePermission()
        {
            Assert.Equal(
                PortalPermissions.PermissionFor(PortalAction.EditDescription),
                PortalPermissions.PermissionFor(PortalAction.EditEmblem));
        }

        [Fact]
        public void EditGroupDoesNotUnlockTheMotdAndLeaderChatDoesNotUnlockTheDescription()
        {
            AllianceLedger group = Ledger(AlliancePermissions.EditGroup);
            AllianceLedger chat = Ledger(AlliancePermissions.LeaderChat);

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(group, PortalAction.EditMessageOfTheDay, Officer, Alliance));

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(chat, PortalAction.EditDescription, Officer, Alliance));
        }

        [Fact]
        public void AdmittingIsARankPermissionAndNotTheFoundersAlone()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditMembers);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(ledger, PortalAction.AdmitOrRescind, Officer, Alliance));

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(ledger, PortalAction.AdmitOrRescind, Founder, Alliance));

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(ledger, PortalAction.AdmitOrRescind, Plain, Alliance));
        }

        // --------------------------------------------------- the leader shortcut

        /// <summary>The four group-level actions, as a set both theories below walk.
        /// A [Theory] cannot carry them: the enum is internal to the server assembly
        /// and an xunit test method has to be public.</summary>
        private static readonly PortalAction[] GroupActions =
        {
            PortalAction.EditDescription,
            PortalAction.EditMessageOfTheDay,
            PortalAction.EditEmblem,
            PortalAction.AdmitOrRescind,
        };

        [Fact]
        public void TheFounderIsAllowedEverythingWhateverTheirRankLists()
        {
            foreach (PortalAction action in GroupActions)
            {
                Assert.Equal(
                    AllianceVerdict.Ok,
                    PortalPermissions.May(Ledger(), action, Founder, Alliance));
            }
        }

        [Fact]
        public void ARankWithNoPermissionsIsRefusedEverything()
        {
            foreach (PortalAction action in GroupActions)
            {
                Assert.Equal(
                    AllianceVerdict.NotPermitted,
                    PortalPermissions.May(Ledger(), action, Plain, Alliance));
            }
        }

        // ------------------------------------------------------------- membership

        [Fact]
        public void ActingOnAnAllianceYouAreNotInIsNotAMember()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditGroup);

            // The officer holds edit_group - in THEIR alliance. Naming the other
            // one must not be answered about theirs.
            Assert.Equal(
                AllianceVerdict.NotAMember,
                PortalPermissions.May(ledger, PortalAction.EditDescription, Officer, Other));

            Assert.Equal(
                AllianceVerdict.NotAMember,
                PortalPermissions.May(ledger, PortalAction.BootMember, Officer, Other, Plain));
        }

        [Fact]
        public void AnUnknownAllianceIsNoSuchAlliance()
        {
            Assert.Equal(
                AllianceVerdict.NoSuchAlliance,
                PortalPermissions.May(Ledger(), PortalAction.EditDescription, Founder,
                    "99999999-9999-9999-9999-999999999999"));
        }

        [Fact]
        public void AMemberActionWithNoTargetIsRefusedRatherThanThrowing()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditMembers);

            Assert.Equal(
                AllianceVerdict.UnknownPlayer,
                PortalPermissions.May(ledger, PortalAction.BootMember, Officer, Alliance));

            Assert.Equal(
                AllianceVerdict.UnknownPlayer,
                PortalPermissions.May(ledger, PortalAction.SetMemberRank, Officer, Alliance, "  "));
        }

        [Fact]
        public void AnEmptyActorIsRefusedRatherThanThrowing()
        {
            Assert.Equal(
                AllianceVerdict.UnknownPlayer,
                PortalPermissions.May(Ledger(), PortalAction.EditDescription, string.Empty, Alliance));
        }

        // ------------------------------------------------------------- the boots

        [Fact]
        public void BootingNeedsEditMembersAndSparesTheFounderAndYourself()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditMembers);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(ledger, PortalAction.BootMember, Officer, Alliance, Plain));

            Assert.Equal(
                AllianceVerdict.CannotBootTheLeader,
                PortalPermissions.May(ledger, PortalAction.BootMember, Officer, Alliance, Founder));

            Assert.Equal(
                AllianceVerdict.CannotBootYourself,
                PortalPermissions.May(ledger, PortalAction.BootMember, Officer, Alliance, Officer));

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.May(Ledger(), PortalAction.BootMember, Officer, Alliance, Plain));
        }

        [Fact]
        public void SettingARankNeedsEditMembersAndNobodyIsPromotedIntoTheFoundersRank()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditMembers);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.MaySetRank(ledger, Officer, Alliance, Plain, CustomRank));

            Assert.Equal(
                AllianceVerdict.RankNotEditable,
                PortalPermissions.MaySetRank(ledger, Officer, Alliance, Plain, LeaderRank));

            Assert.Equal(
                AllianceVerdict.NoSuchRank,
                PortalPermissions.MaySetRank(ledger, Officer, Alliance, Plain, "rank-that-is-not"));

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                PortalPermissions.MaySetRank(Ledger(), Officer, Alliance, Plain, CustomRank));
        }

        [Fact]
        public void SettingARankInAnAllianceYouAreNotInIsNotAMember()
        {
            Assert.Equal(
                AllianceVerdict.NotAMember,
                PortalPermissions.MaySetRank(
                    Ledger(AlliancePermissions.EditMembers), Officer, Other, Plain, CustomRank));
        }

        /// <summary>
        /// The coarse "may you change ranks at all" answer must not be soured by
        /// the rank the target already holds. Asked against the founder - whose
        /// rank is the one nobody may be moved onto - the answer is still Ok,
        /// because the question is about the ACTOR.
        /// </summary>
        [Fact]
        public void TheCoarseRankQuestionIgnoresTheRankTheTargetHolds()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditMembers);

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(ledger, PortalAction.SetMemberRank, Officer, Alliance, Founder));
        }

        // ------------------------------------------------------------ the rights

        [Fact]
        public void RightsForAgreesWithAskingEachActionOneByOne()
        {
            AllianceLedger ledger = Ledger(AlliancePermissions.EditGroup, AlliancePermissions.EditMembers);

            AllianceRights rights = PortalPermissions.RightsFor(ledger, Officer, Alliance);

            Assert.True(rights.EditDescription);
            Assert.True(rights.EditEmblem);
            Assert.True(rights.ManageMembers);
            Assert.False(rights.EditMessageOfTheDay);
            Assert.False(rights.Nothing);
        }

        [Fact]
        public void ARankWithNothingHasNothingToManage()
        {
            Assert.True(PortalPermissions.RightsFor(Ledger(), Plain, Alliance).Nothing);
        }

        [Fact]
        public void TheFounderHasEverything()
        {
            AllianceRights rights = PortalPermissions.RightsFor(Ledger(), Founder, Alliance);

            Assert.True(rights.EditDescription);
            Assert.True(rights.EditMessageOfTheDay);
            Assert.True(rights.EditEmblem);
            Assert.True(rights.ManageMembers);
        }

        [Fact]
        public void LeavingNeedsNothingButMembership()
        {
            AllianceLedger ledger = Ledger();

            Assert.Equal(
                AllianceVerdict.Ok,
                PortalPermissions.May(ledger, PortalAction.Leave, Plain, Alliance));

            Assert.Equal(
                AllianceVerdict.NotInAnAlliance,
                PortalPermissions.May(ledger, PortalAction.Leave, "character:nobody", Alliance));

            Assert.Equal(string.Empty, PortalPermissions.PermissionFor(PortalAction.Leave));
        }

        /// <summary>
        /// Every permission this table names has to be one the CLIENT understands.
        /// A string outside <see cref="AlliancePermissions.All"/> is not an error
        /// to the client, it is invisible - so a typo here would produce a portal
        /// control gated on a permission no rank can ever carry.
        /// </summary>
        [Fact]
        public void EveryNamedPermissionIsOneTheClientCanRead()
        {
            foreach (PortalAction action in Enum.GetValues<PortalAction>())
            {
                string permission = PortalPermissions.PermissionFor(action);

                // Leaving is the one action with no permission behind it.
                if (action == PortalAction.Leave)
                {
                    Assert.Equal(string.Empty, permission);
                    continue;
                }

                Assert.True(AlliancePermissions.IsKnown(permission),
                    action + " names '" + permission + "', which the client cannot read");
            }
        }
    }
}
