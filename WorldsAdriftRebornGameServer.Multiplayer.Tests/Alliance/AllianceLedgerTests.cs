using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Alliance
{
    public sealed class AllianceLedgerTests
    {
        private const string Alice = "character:alice";
        private const string Bob = "character:bob";
        private const string Cara = "character:cara";
        private const string Id = "11111111-1111-1111-1111-111111111111";
        private const string Other = "22222222-2222-2222-2222-222222222222";

        private static AllianceRank Leader() => new AllianceRank(
            "rank:leader", "Leader", false, AllianceRank.TypeLeader, AlliancePermissions.DefaultLeader);

        private static AllianceRank Member() => new AllianceRank(
            "rank:member", "Member", false, AllianceRank.TypeMember, AlliancePermissions.DefaultMember);

        private static AllianceLedger WithAlliance(out Multiplayer.Alliance.Alliance alliance)
        {
            AllianceLedger ledger = new AllianceLedger();
            alliance = ledger.Create(Id, Alice, "Rat Corp", Leader(), Member());
            return ledger;
        }

        /// <summary>
        /// The two flags the client derives its Leader and BasicMember slots from.
        /// A leader rank shipped as editable leaves rankInfo.Leader null and the
        /// founder vanishes from their own alliance panel.
        /// </summary>
        [Fact]
        public void The_default_ranks_are_identified_by_type_plus_not_editable()
        {
            WithAlliance(out Multiplayer.Alliance.Alliance alliance);

            Assert.True(alliance.DefaultLeaderRank!.IsDefaultLeader);
            Assert.True(alliance.DefaultMemberRank!.IsDefaultMember);

            AllianceRank editableLeader = new AllianceRank(
                "rank:x", "Nearly", true, AllianceRank.TypeLeader, AlliancePermissions.DefaultLeader);

            Assert.False(editableLeader.IsDefaultLeader);
        }

        [Fact]
        public void The_founder_is_seated_on_the_leader_rank_at_creation()
        {
            WithAlliance(out Multiplayer.Alliance.Alliance alliance);

            Assert.True(alliance.IsLeader(Alice));
            Assert.Equal("rank:leader", alliance.RankOf(Alice)!.Id);
            Assert.Equal(new[] { Alice }, alliance.Members);
        }

        [Fact]
        public void Joining_drops_every_other_outstanding_request()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Create(Other, Cara, "Sky Rats", Leader(), Member());

            ledger.Request(Bob, Id);
            ledger.Request(Bob, Other);
            Assert.True(ledger.HasLiveRequest(Bob, Other));

            ledger.Join(Bob, Id, "rank:member");

            Assert.False(ledger.HasLiveRequest(Bob, Id));
            Assert.False(ledger.HasLiveRequest(Bob, Other));
        }

        [Fact]
        public void A_leaving_founder_hands_over_the_title_AND_the_rank()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            ledger.Join(Bob, Id, "rank:member");

            ledger.Remove(Alice);

            Assert.Equal(Bob, alliance.LeaderUid);

            // Both, because leadership in this client is two independent facts.
            // Moving only the pointer leaves a founder with no permissions.
            Assert.Equal("rank:leader", alliance.RankOf(Bob)!.Id);
        }

        [Fact]
        public void The_last_member_leaving_dissolves_the_alliance()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Request(Bob, Id);

            ledger.Remove(Alice);

            Assert.Null(ledger.ById(Id));

            // And the offers to join it go with it, rather than sitting in Bob's
            // list forever pointing at nothing.
            Assert.False(ledger.HasLiveRequest(Bob, Id));
        }

        /// <summary>
        /// AllianceClient.TryGetRank THROWS on a rank id it cannot find, and the
        /// throw lands in the handler shared with crews - so a member left
        /// pointing at a deleted rank destroys the whole Social Sheet, not just
        /// their own row.
        /// </summary>
        [Fact]
        public void Deleting_a_rank_moves_its_holders_to_the_default_member_rank()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(new AllianceRank(
                "rank:officer", "Officer", true, AllianceRank.TypeMember,
                new[] { AlliancePermissions.EditMembers }));

            ledger.Join(Bob, Id, "rank:officer");
            Assert.Equal("rank:officer", alliance.RankOf(Bob)!.Id);

            alliance.RemoveRank("rank:officer");

            Assert.Equal("rank:member", alliance.RankOf(Bob)!.Id);
        }

        /// <summary>
        /// Belt as well as braces: even a rank id that was never cleaned up
        /// resolves to something the client can look up.
        /// </summary>
        [Fact]
        public void An_unresolvable_rank_id_still_answers_the_default_member_rank()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            ledger.Join(Bob, Id, "rank:vanished");

            Assert.Equal("rank:member", alliance.RankOf(Bob)!.Id);
        }

        /// <summary>
        /// REGRESSION. Replacing a rank is how it is renamed and re-permissioned,
        /// and it must not disturb who holds it. Implementing AddRank as
        /// "remove then add" ran the holder-reassignment in RemoveRank first and
        /// demoted every member of that rank on every edit.
        /// </summary>
        [Fact]
        public void Replacing_a_rank_keeps_the_people_who_hold_it()
        {
            AllianceLedger ledger = WithAlliance(out Multiplayer.Alliance.Alliance alliance);
            alliance.AddRank(new AllianceRank(
                "rank:officer", "Officer", true, AllianceRank.TypeMember,
                new[] { AlliancePermissions.EditMembers }));
            ledger.Join(Bob, Id, "rank:officer");

            alliance.AddRank(new AllianceRank(
                "rank:officer", "Quartermaster", true, AllianceRank.TypeMember,
                new[] { AlliancePermissions.EditGroup }));

            Assert.Equal("rank:officer", alliance.RankOf(Bob)!.Id);
            Assert.Equal("Quartermaster", alliance.RankOf(Bob)!.Name);
            Assert.True(alliance.RankOf(Bob)!.Grants(AlliancePermissions.EditGroup));
            Assert.False(alliance.RankOf(Bob)!.Grants(AlliancePermissions.EditMembers));
        }

        [Fact]
        public void A_name_is_free_for_the_alliance_that_already_holds_it()
        {
            AllianceLedger ledger = WithAlliance(out _);

            Assert.True(ledger.NameTaken("Rat Corp"));
            Assert.False(ledger.NameTaken("Rat Corp", excluding: Id));
        }

        [Fact]
        public void Live_requests_are_counted_per_alliance()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Create(Other, Cara, "Sky Rats", Leader(), Member());

            ledger.Request(Bob, Id);
            ledger.Request(Cara, Id);
            ledger.Request(Bob, Other);

            Assert.Equal(2, ledger.LiveRequestsFor(Id));
            Assert.Equal(1, ledger.LiveRequestsFor(Other));
        }

        [Fact]
        public void Dissolving_releases_every_member_to_join_something_else()
        {
            AllianceLedger ledger = WithAlliance(out _);
            ledger.Join(Bob, Id, "rank:member");

            ledger.Dissolve(Id);

            Assert.Null(ledger.AllianceOf(Alice));
            Assert.Null(ledger.AllianceOf(Bob));
        }
    }
}
