using Npgsql;
using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Schema v8 - alliances, ranks and membership.
    ///
    /// Against a real PostgreSQL rather than a fake, because the invariants worth
    /// testing here are the ones pushed into the DATABASE: they hold even when a
    /// future call site forgets to check, and a fake that accepted what the real
    /// server refuses would let a broken contract pass green. The endpoint suite
    /// in WorldsAdriftServer.Tests runs against an in-memory double instead, and
    /// deliberately: what it checks is response SHAPE, which needs no database and
    /// should not be skipped on a machine that has none.
    /// </summary>
    public class AllianceRepositoryTests
    {
        private const string Region = "community_server";

        private static AllianceRecord AnAlliance(Guid leader, string name = "Rat Corp", Guid? id = null) =>
            new AllianceRecord(
                id ?? Guid.NewGuid(),
                Region,
                name,
                "Rats for life",
                "Squeak",
                string.Empty,
                leader,
                TempDb.Now,
                TempDb.Now);

        private static AllianceRankRecord ARank(
            Guid allianceId,
            string name,
            bool editable,
            string type,
            string permissions = "",
            int sortOrder = 0,
            Guid? id = null) =>
            new AllianceRankRecord(
                id ?? Guid.NewGuid(), allianceId, name, editable, type, "alliance_member",
                permissions, sortOrder);

        private static Guid ACharacterIn(TempDb db, string name, int slot)
        {
            AccountRecord account = db.AnAccount(name.ToLowerInvariant());
            CharacterRecord character = TempDb.ACharacter(account.AccountId, name, slot);
            db.Characters.Save(character);
            return character.CharacterUid;
        }

        [PostgresFact]
        public void An_alliance_round_trips_with_every_field_intact()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);

            Assert.True(db.Alliances.TryInsertAlliance(alliance));

            AllianceRecord stored = db.Alliances.FindAlliance(alliance.AllianceId)!;
            Assert.Equal(alliance.Name, stored.Name);
            Assert.Equal(alliance.Description, stored.Description);
            Assert.Equal(alliance.MessageOfTheDay, stored.MessageOfTheDay);
            Assert.Equal(alliance.Region, stored.Region);
            Assert.Equal(leader, stored.LeaderUid);
            Assert.Equal(string.Empty, stored.EmblemUrl);
        }

        /// <summary>
        /// The client has a duplicate_alliance_name code, so uniqueness is retail's
        /// rule. Case-insensitivity is ours, and it is enforced by the INDEX rather
        /// than by a read-then-write, so two founders racing cannot both win.
        /// </summary>
        [PostgresFact]
        public void Two_alliances_cannot_share_a_name_whatever_the_case()
        {
            using TempDb db = new TempDb();
            Guid one = ACharacterIn(db, "Rattus", 0);
            Guid two = ACharacterIn(db, "Mus", 0);

            Assert.True(db.Alliances.TryInsertAlliance(AnAlliance(one, "Rat Corp")));
            Assert.False(db.Alliances.TryInsertAlliance(AnAlliance(two, "rat corp")));
            Assert.False(db.Alliances.TryInsertAlliance(AnAlliance(two, "RAT CORP")));
            Assert.True(db.Alliances.TryInsertAlliance(AnAlliance(two, "Sky Rats")));
        }

        /// <summary>
        /// The primary key is the CHARACTER, so a double membership is not
        /// something a caller can produce by forgetting to check - writing a second
        /// one MOVES them.
        /// </summary>
        [PostgresFact]
        public void A_character_can_be_in_only_one_alliance_and_the_row_follows_them()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            Guid wanderer = ACharacterIn(db, "Mus", 0);

            AllianceRecord first = AnAlliance(leader, "Rat Corp");
            AllianceRecord second = AnAlliance(leader, "Sky Rats");
            db.Alliances.TryInsertAlliance(first);
            db.Alliances.TryInsertAlliance(second);

            Guid rankOne = Guid.NewGuid();
            Guid rankTwo = Guid.NewGuid();
            db.Alliances.SaveRank(ARank(first.AllianceId, "Member", false, "member", id: rankOne));
            db.Alliances.SaveRank(ARank(second.AllianceId, "Member", false, "member", id: rankTwo));

            db.Alliances.SaveMember(new AllianceMemberRecord(
                wanderer, first.AllianceId, rankOne, "", "", 0, TempDb.Now, TempDb.Now));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                wanderer, second.AllianceId, rankTwo, "", "", 0, TempDb.Now, TempDb.Now));

            Assert.Empty(db.Alliances.MembersOf(first.AllianceId));
            Assert.Single(db.Alliances.MembersOf(second.AllianceId));
            Assert.Equal(second.AllianceId, db.Alliances.MemberOf(wanderer)!.AllianceId);
        }

        /// <summary>
        /// Exactly one default rank of each kind. The client fills its Leader and
        /// BasicMember fields by scanning for them, so a second of either means the
        /// last one silently wins.
        /// </summary>
        [PostgresFact]
        public void An_alliance_cannot_hold_two_default_leader_ranks()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Leader", false, "leader"));

            Assert.Throws<PostgresException>(() =>
                db.Alliances.SaveRank(ARank(alliance.AllianceId, "Boss", false, "leader")));

            // An EDITABLE leader-typed rank is not a default one, so it is allowed.
            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Deputy", true, "leader"));
        }

        [PostgresFact]
        public void An_alliance_cannot_hold_two_default_member_ranks()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Member", false, "member"));

            Assert.Throws<PostgresException>(() =>
                db.Alliances.SaveRank(ARank(alliance.AllianceId, "Recruit", false, "member")));
        }

        /// <summary>
        /// The rank type vocabulary is closed because the client compares it by
        /// string and a third value produces a rank that is neither the leader's
        /// nor a basic member's.
        /// </summary>
        [PostgresFact]
        public void A_rank_type_outside_the_clients_vocabulary_is_refused()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            Assert.Throws<PostgresException>(() =>
                db.Alliances.SaveRank(ARank(alliance.AllianceId, "Warlord", true, "overlord")));
        }

        [PostgresFact]
        public void Disbanding_an_alliance_takes_its_ranks_and_members_with_it()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            Guid member = ACharacterIn(db, "Mus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            Guid rankId = Guid.NewGuid();
            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Member", false, "member", id: rankId));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                leader, alliance.AllianceId, rankId, "", "", 0, TempDb.Now, TempDb.Now));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                member, alliance.AllianceId, rankId, "", "", 1, TempDb.Now, TempDb.Now));

            Assert.True(db.Alliances.DeleteAlliance(alliance.AllianceId));

            Assert.Empty(db.Alliances.AllRanks());
            Assert.Empty(db.Alliances.AllMembers());
            Assert.Null(db.Alliances.MemberOf(member));
        }

        /// <summary>
        /// Deleting the CHARACTER takes their membership - and their alliance, if
        /// they founded it. That cascade is what stops a deleted account leaving an
        /// alliance nobody leads.
        /// </summary>
        [PostgresFact]
        public void Deleting_the_founder_takes_the_alliance_with_them()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);
            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Member", false, "member"));

            db.Characters.Delete(leader);

            Assert.Null(db.Alliances.FindAlliance(alliance.AllianceId));
            Assert.Empty(db.Alliances.AllRanks());
        }

        /// <summary>
        /// Membership is replayed in join order, because succession reads the
        /// longest-standing remaining member straight off it and a shuffled replay
        /// would silently change who inherits.
        /// </summary>
        [PostgresFact]
        public void Membership_comes_back_in_join_order()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            Guid second = ACharacterIn(db, "Mus", 0);
            Guid third = ACharacterIn(db, "Sorex", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            Guid rankId = Guid.NewGuid();
            db.Alliances.SaveRank(ARank(alliance.AllianceId, "Member", false, "member", id: rankId));

            db.Alliances.SaveMember(new AllianceMemberRecord(
                third, alliance.AllianceId, rankId, "", "", 2, TempDb.Now, TempDb.Now));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                leader, alliance.AllianceId, rankId, "", "", 0, TempDb.Now, TempDb.Now));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                second, alliance.AllianceId, rankId, "", "", 1, TempDb.Now, TempDb.Now));

            Assert.Equal(
                new[] { leader, second, third },
                db.Alliances.MembersOf(alliance.AllianceId).Select(m => m.CharacterUid));
        }

        /// <summary>
        /// The whole point of persistence: an alliance survives a restart. Rebuilt
        /// through a second Db over the same schema, which is what the login server
        /// does on boot.
        /// </summary>
        [PostgresFact]
        public void An_alliance_survives_being_read_back_whole()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            Guid member = ACharacterIn(db, "Mus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            Guid leaderRank = Guid.NewGuid();
            Guid memberRank = Guid.NewGuid();
            db.Alliances.SaveRank(ARank(
                alliance.AllianceId, "Leader", false, "leader",
                "edit_group,edit_members,leader_chat", 0, leaderRank));
            db.Alliances.SaveRank(ARank(
                alliance.AllianceId, "Member", false, "member", "", 1, memberRank));

            db.Alliances.SaveMember(new AllianceMemberRecord(
                leader, alliance.AllianceId, leaderRank, "", "", 0, TempDb.Now, TempDb.Now));
            db.Alliances.SaveMember(new AllianceMemberRecord(
                member, alliance.AllianceId, memberRank, "keen", "watch them", 1,
                TempDb.Now, TempDb.Now));

            Repositories.AllianceRepository reopened = new Repositories.AllianceRepository(db.Db);

            Assert.Single(reopened.AllAlliances());
            Assert.Equal(2, reopened.AllRanks().Count);
            Assert.Equal(2, reopened.AllMembers().Count);

            AllianceMemberRecord restored = reopened.MemberOf(member)!;
            Assert.Equal(memberRank, restored.RankId);
            Assert.Equal("keen", restored.OfficerNote);
            Assert.Equal("watch them", restored.PrivateOfficerNote);

            Assert.Equal(
                "edit_group,edit_members,leader_chat",
                reopened.FindRank(leaderRank)!.Permissions);
        }

        [PostgresFact]
        public void A_blank_alliance_name_is_refused_by_the_database()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);

            Assert.Throws<PostgresException>(() =>
                db.Alliances.TryInsertAlliance(AnAlliance(leader, "   ")));
        }

        [PostgresFact]
        public void Editing_an_alliance_keeps_its_founding_date()
        {
            using TempDb db = new TempDb();
            Guid leader = ACharacterIn(db, "Rattus", 0);
            AllianceRecord alliance = AnAlliance(leader);
            db.Alliances.TryInsertAlliance(alliance);

            DateTimeOffset later = TempDb.Now.AddDays(3);
            db.Alliances.SaveAlliance(alliance with
            {
                Description = "Rats forever",
                CreatedAt = later,
                UpdatedAt = later,
            });

            AllianceRecord stored = db.Alliances.FindAlliance(alliance.AllianceId)!;
            Assert.Equal("Rats forever", stored.Description);
            Assert.Equal(TempDb.Now, stored.CreatedAt);
            Assert.Equal(later, stored.UpdatedAt);
        }
    }
}
