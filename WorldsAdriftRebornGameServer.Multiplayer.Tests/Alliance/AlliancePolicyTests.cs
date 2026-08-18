using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Alliance
{
    /// <summary>
    /// The alliance rules, as plain facts over a ledger. No database, no wire, no
    /// clock - which is the point of keeping them in this project rather than in
    /// the endpoint that calls them.
    /// </summary>
    public sealed class AlliancePolicyTests
    {
        private const string Alice = "character:alice";
        private const string Bob = "character:bob";
        private const string Cara = "character:cara";
        private const string Dan = "character:dan";
        private const string Id = "11111111-1111-1111-1111-111111111111";
        private const string Other = "22222222-2222-2222-2222-222222222222";

        private static AllianceRank Leader(params string[] permissions) => new AllianceRank(
            "rank:leader", "Leader", false, AllianceRank.TypeLeader,
            permissions.Length == 0 ? AlliancePermissions.DefaultLeader : permissions);

        private static AllianceRank Member(params string[] permissions) => new AllianceRank(
            "rank:member", "Member", false, AllianceRank.TypeMember, permissions);

        private static AllianceRank Officer(params string[] permissions) => new AllianceRank(
            "rank:officer", "Officer", true, AllianceRank.TypeMember, permissions);

        private static AllianceLedger WithAlliance(
            out Multiplayer.Alliance.Alliance alliance, string name = "Rat Corp")
        {
            AllianceLedger ledger = new AllianceLedger();
            alliance = ledger.Create(Id, Alice, name, Leader(), Member());
            return ledger;
        }

        // ---- founding ------------------------------------------------------

        [Fact]
        public void A_player_in_nothing_may_found_an_alliance()
        {
            Assert.Equal(
                AllianceVerdict.Ok,
                AlliancePolicy.MayCreate(new AllianceLedger(), Alice, "Rat Corp"));
        }

        [Fact]
        public void A_player_may_not_found_a_second_alliance_while_in_one()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.Equal(
                AllianceVerdict.AlreadyInAnotherAlliance,
                AlliancePolicy.MayCreate(ledger, Alice, "Something Else"));
        }

        /// <summary>
        /// Case-insensitively. Two alliances a player cannot tell apart in a list
        /// that shows nothing but the name is the failure this prevents.
        /// </summary>
        [Fact]
        public void A_name_already_taken_is_refused_whatever_its_case()
        {
            AllianceLedger ledger = WithAlliance(out _, "Rat Corp");

            Assert.Equal(AllianceVerdict.NameTaken, AlliancePolicy.MayCreate(ledger, Bob, "rat corp"));
            Assert.Equal(AllianceVerdict.NameTaken, AlliancePolicy.MayCreate(ledger, Bob, "RAT CORP"));
        }

        [Fact]
        public void A_name_the_client_would_not_have_typed_is_refused()
        {
            // The client's own regex rejects digits. A request carrying one did
            // not come from the retail UI.
            Assert.Equal(
                AllianceVerdict.NameNotAllowed,
                AlliancePolicy.MayCreate(new AllianceLedger(), Alice, "Rat Corp 2"));
        }

        // ---- permissions ---------------------------------------------------

        /// <summary>
        /// The founder is allowed everything regardless of what their rank lists.
        /// Mirrors the client, which ORs edit_members with "is the leader rank",
        /// and closes the trap of a founder editing themselves out of their own
        /// alliance.
        /// </summary>
        [Fact]
        public void The_founder_may_act_even_with_an_empty_permission_set()
        {
            AllianceLedger ledger = new AllianceLedger();
            ledger.Create(Id, Alice, "Rat Corp", Leader(System.Array.Empty<string>()), Member());
            ledger.Join(Bob, Id, "rank:member");

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayBoot(ledger, Alice, Bob));
            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayEditDescription(ledger, Alice, Id));
            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayEditMessageOfTheDay(ledger, Alice, Id));
        }

        [Fact]
        public void A_plain_member_may_not_invite_boot_or_edit_the_group()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Join(Bob, Id, "rank:member");
            ledger.Join(Cara, Id, "rank:member");

            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayInvite(ledger, Bob, Dan));
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayBoot(ledger, Bob, Cara));
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayEditDescription(ledger, Bob, Id));
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayEditRanks(ledger, Bob, Id));
        }

        [Fact]
        public void An_officer_with_edit_members_may_invite_and_boot()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(Officer(AlliancePermissions.EditMembers));
            ledger.Join(Bob, Id, "rank:officer");
            ledger.Join(Cara, Id, "rank:member");

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayInvite(ledger, Bob, Dan));
            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayBoot(ledger, Bob, Cara));

            // ... but edit_members is not edit_group.
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayEditDescription(ledger, Bob, Id));
        }

        /// <summary>
        /// REGRESSION GUARD for a retail client bug that must be reproduced, not
        /// corrected. SocialGroupParsers.cs:129 reads the MOTD gate off
        /// <c>leader_chat</c>, not off <c>edit_message_of_the_day</c>. A server
        /// that enforced the honest name would show the player an editable box
        /// they are then refused, or a locked box they are permitted.
        /// </summary>
        [Fact]
        public void The_motd_permission_is_leader_chat_because_that_is_what_the_client_reads()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);

            alliance.AddRank(Officer(AlliancePermissions.EditMessageOfTheDay));
            ledger.Join(Bob, Id, "rank:officer");

            Assert.Equal(
                AllianceVerdict.NotPermitted,
                AlliancePolicy.MayEditMessageOfTheDay(ledger, Bob, Id));

            alliance.AddRank(new AllianceRank(
                "rank:officer", "Officer", true, AllianceRank.TypeMember,
                new[] { AlliancePermissions.LeaderChat }));

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayEditMessageOfTheDay(ledger, Bob, Id));
            Assert.Equal(AlliancePermissions.LeaderChat, AlliancePermissions.MotdIsReadFrom);
        }

        [Fact]
        public void Nobody_outside_the_alliance_may_edit_it()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.Equal(AllianceVerdict.NotAMember, AlliancePolicy.MayEditDescription(ledger, Bob, Id));
            Assert.Equal(AllianceVerdict.NoSuchAlliance, AlliancePolicy.MayEditDescription(ledger, Alice, Other));
        }

        // ---- membership ----------------------------------------------------

        [Fact]
        public void You_cannot_invite_yourself()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.Equal(AllianceVerdict.CannotInviteYourself, AlliancePolicy.MayInvite(ledger, Alice, Alice));
        }

        [Fact]
        public void Somebody_already_in_another_alliance_cannot_be_invited()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Create(Other, Bob, "Sky Rats", Leader(), Member());

            Assert.Equal(
                AllianceVerdict.AlreadyInAnotherAlliance,
                AlliancePolicy.MayInvite(ledger, Alice, Bob));
        }

        [Fact]
        public void A_second_live_offer_to_the_same_person_is_refused()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Request(Bob, Id);

            Assert.Equal(AllianceVerdict.AlreadyRequested, AlliancePolicy.MayInvite(ledger, Alice, Bob));
            Assert.Equal(AllianceVerdict.AlreadyRequested, AlliancePolicy.MayApply(ledger, Bob, Id));
        }

        [Fact]
        public void An_application_needs_no_permission_but_still_obeys_membership()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayApply(ledger, Bob, Id));
            Assert.Equal(AllianceVerdict.AlreadyInThisAlliance, AlliancePolicy.MayApply(ledger, Alice, Id));
            Assert.Equal(AllianceVerdict.NoSuchAlliance, AlliancePolicy.MayApply(ledger, Bob, Other));
        }

        /// <summary>
        /// Re-checked at ACCEPT rather than trusted from when the offer was made,
        /// because an alliance can fill up or take the player in by the other
        /// route in between.
        /// </summary>
        [Fact]
        public void A_full_alliance_refuses_the_join_even_with_a_live_invite()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Request(Bob, Id);

            Assert.Equal(AllianceVerdict.AtCapacity, AlliancePolicy.MayJoin(ledger, Bob, Id, maxMembers: 1));
            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayJoin(ledger, Bob, Id, maxMembers: 2));
        }

        [Fact]
        public void The_founder_cannot_be_booted()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(Officer(AlliancePermissions.EditMembers));
            ledger.Join(Bob, Id, "rank:officer");

            Assert.Equal(AllianceVerdict.CannotBootTheLeader, AlliancePolicy.MayBoot(ledger, Bob, Alice));
        }

        [Fact]
        public void Only_the_founder_may_disband()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(Officer(AlliancePermissions.EditMembers, AlliancePermissions.EditGroup));
            ledger.Join(Bob, Id, "rank:officer");

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayDisband(ledger, Alice, Id));
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayDisband(ledger, Bob, Id));
        }

        [Fact]
        public void Anyone_in_an_alliance_may_leave_it_including_the_founder()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Join(Bob, Id, "rank:member");

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayLeave(ledger, Alice));
            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayLeave(ledger, Bob));
            Assert.Equal(AllianceVerdict.NotInAnAlliance, AlliancePolicy.MayLeave(ledger, Cara));
        }

        // ---- ranks ---------------------------------------------------------

        /// <summary>
        /// The two default ranks are structural: the client fills its Leader and
        /// BasicMember slots from them and then dereferences those slots.
        /// </summary>
        [Fact]
        public void The_default_ranks_cannot_be_deleted()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.Equal(AllianceVerdict.RankNotEditable, AlliancePolicy.MayDeleteRank(ledger, Alice, "rank:leader"));
            Assert.Equal(AllianceVerdict.RankNotEditable, AlliancePolicy.MayDeleteRank(ledger, Alice, "rank:member"));
        }

        [Fact]
        public void A_custom_rank_can_be_deleted_by_somebody_with_edit_ranks()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(Officer(AlliancePermissions.EditRanks));
            ledger.Join(Bob, Id, "rank:officer");
            ledger.Join(Cara, Id, "rank:member");

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MayDeleteRank(ledger, Bob, "rank:officer"));
            Assert.Equal(AllianceVerdict.NotPermitted, AlliancePolicy.MayDeleteRank(ledger, Cara, "rank:officer"));
        }

        /// <summary>
        /// Nobody is promoted into the founder's rank by a rank change: leadership
        /// is ALSO leaderCharacterUid, and handing out only the rank would produce
        /// two members the client draws as leader.
        /// </summary>
        [Fact]
        public void Nobody_can_be_moved_onto_the_leader_rank()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Join(Bob, Id, "rank:member");

            Assert.Equal(
                AllianceVerdict.RankNotEditable,
                AlliancePolicy.MaySetRank(ledger, Alice, Bob, "rank:leader"));

            Assert.Equal(AllianceVerdict.Ok, AlliancePolicy.MaySetRank(ledger, Alice, Bob, "rank:member"));
        }

        [Fact]
        public void A_rank_that_does_not_exist_is_refused_rather_than_stored()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Join(Bob, Id, "rank:member");

            Assert.Equal(
                AllianceVerdict.NoSuchRank,
                AlliancePolicy.MaySetRank(ledger, Alice, Bob, "rank:nonexistent"));
        }

        // ---- succession ----------------------------------------------------

        [Fact]
        public void Succession_goes_to_the_longest_standing_remaining_member()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            ledger.Join(Bob, Id, "rank:member");
            ledger.Join(Cara, Id, "rank:member");

            Assert.Equal(Bob, AlliancePolicy.SuccessorTo(alliance, Alice));
        }

        [Fact]
        public void A_founder_alone_has_no_successor()
        {
            WithAlliance(out Multiplayer.Alliance.Alliance alliance);

            Assert.Null(AlliancePolicy.SuccessorTo(alliance, Alice));
        }
    }
}
