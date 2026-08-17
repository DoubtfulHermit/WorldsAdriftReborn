using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Crews. Unlike the other game-server tables this one describes a
    /// relationship BETWEEN characters, so what is tested here is mostly what the
    /// database refuses: a character in two crews, two members in one seat, and a
    /// crew or member outliving the character it belongs to.
    /// </summary>
    public class CrewRepositoryTests
    {
        private static (TempDb db, CharacterRecord leader, CharacterRecord mate) Crewed()
        {
            TempDb db = new TempDb();
            AccountRecord account = db.AnAccount();
            CharacterRecord leader = TempDb.ACharacter(account.AccountId);
            CharacterRecord mate = TempDb.ACharacter(account.AccountId, name: "Mate", slot: 1);
            db.Characters.Save(leader);
            db.Characters.Save(mate);
            return (db, leader, mate);
        }

        private static CrewRecord ACrew(Guid leaderUid, string id = "crew:1", int slots = 4) =>
            new CrewRecord(id, leaderUid, slots, TempDb.Now, TempDb.Now);

        [PostgresFact]
        public void A_saved_crew_and_its_members_come_back_field_for_field()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                CrewRecord crew = ACrew(leader.CharacterUid);
                db.Crews.SaveCrew(crew);
                CrewMemberRecord first = new CrewMemberRecord(
                    leader.CharacterUid, crew.CrewId, 0, 0, TempDb.Now);
                CrewMemberRecord second = new CrewMemberRecord(
                    mate.CharacterUid, crew.CrewId, 1, null, TempDb.Now);
                db.Crews.SaveMember(first);
                db.Crews.SaveMember(second);

                Assert.Equal(crew, Assert.Single(db.Crews.AllCrews()));
                Assert.Equal(new[] { first, second }, db.Crews.AllMembers());
                Assert.Equal(first, db.Crews.MemberOf(leader.CharacterUid));
            }
        }

        /// <summary>
        /// The primary key on crew_members is the CHARACTER, so this is the
        /// database refusing a double membership rather than every code path that
        /// writes here having to remember to check.
        /// </summary>
        [PostgresFact]
        public void A_character_cannot_be_in_two_crews_at_once()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid, "crew:1"));
                db.Crews.SaveCrew(ACrew(mate.CharacterUid, "crew:2"));
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:1", 1, null, TempDb.Now));

                // Not an error: the row FOLLOWS the character, so joining another
                // crew moves them rather than duplicating them.
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:2", 0, null, TempDb.Now));

                Assert.Equal("crew:2", db.Crews.MemberOf(mate.CharacterUid)!.CrewId);
                Assert.Single(db.Crews.AllMembers().Where(m => m.CharacterUid == mate.CharacterUid));
            }
        }

        [PostgresFact]
        public void Two_members_cannot_hold_the_same_seat()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 0, 2, TempDb.Now));

                Assert.ThrowsAny<Exception>(() => db.Crews.SaveMember(
                    new CrewMemberRecord(mate.CharacterUid, "crew:1", 1, 2, TempDb.Now)));
            }
        }

        /// <summary>
        /// Several members with no seat is normal - the UNIQUE is on (crew, slot)
        /// and Postgres does not treat NULLs as equal - so this must NOT throw.
        /// </summary>
        [PostgresFact]
        public void Several_members_may_have_no_seat_at_all()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 0, null, TempDb.Now));
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:1", 1, null, TempDb.Now));

                Assert.Equal(2, db.Crews.AllMembers().Count);
            }
        }

        [PostgresFact]
        public void Members_come_back_in_join_order_because_succession_depends_on_it()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                // Written out of order on purpose.
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:1", 7, null, TempDb.Now));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 3, null, TempDb.Now));

                Assert.Equal(new[] { leader.CharacterUid, mate.CharacterUid },
                    db.Crews.AllMembers().Select(m => m.CharacterUid));
            }
        }

        [PostgresFact]
        public void Disbanding_a_crew_takes_its_membership_with_it()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 0, null, TempDb.Now));
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:1", 1, null, TempDb.Now));

                Assert.True(db.Crews.DeleteCrew("crew:1"));

                Assert.Empty(db.Crews.AllCrews());
                Assert.Empty(db.Crews.AllMembers());
                Assert.False(db.Crews.DeleteCrew("crew:1"));
            }
        }

        [PostgresFact]
        public void A_deleted_character_takes_their_membership_with_them()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 0, null, TempDb.Now));
                db.Crews.SaveMember(new CrewMemberRecord(
                    mate.CharacterUid, "crew:1", 1, null, TempDb.Now));

                db.Characters.Delete(mate.CharacterUid);

                Assert.Null(db.Crews.MemberOf(mate.CharacterUid));
                Assert.Single(db.Crews.AllMembers());
            }
        }

        [PostgresFact]
        public void A_crew_led_by_nobody_is_refused()
        {
            (TempDb db, CharacterRecord leader, _) = Crewed();
            using (db)
            {
                Assert.ThrowsAny<Exception>(() => db.Crews.SaveCrew(ACrew(Guid.NewGuid())));
                Assert.ThrowsAny<Exception>(() => db.Crews.SaveMember(
                    new CrewMemberRecord(leader.CharacterUid, "crew:nope", 0, null, TempDb.Now)));
            }
        }

        [PostgresFact]
        public void Removing_a_member_reports_whether_there_was_one()
        {
            (TempDb db, CharacterRecord leader, CharacterRecord mate) = Crewed();
            using (db)
            {
                db.Crews.SaveCrew(ACrew(leader.CharacterUid));
                db.Crews.SaveMember(new CrewMemberRecord(
                    leader.CharacterUid, "crew:1", 0, null, TempDb.Now));

                Assert.True(db.Crews.RemoveMember(leader.CharacterUid));
                Assert.False(db.Crews.RemoveMember(mate.CharacterUid));
            }
        }

        [PostgresFact]
        public void An_absurd_slot_count_is_refused_by_the_database()
        {
            (TempDb db, CharacterRecord leader, _) = Crewed();
            using (db)
            {
                Assert.ThrowsAny<Exception>(() =>
                    db.Crews.SaveCrew(ACrew(leader.CharacterUid, "crew:big", slots: 99)));
                Assert.ThrowsAny<Exception>(() =>
                    db.Crews.SaveCrew(ACrew(leader.CharacterUid, "crew:none", slots: 0)));
            }
        }
    }
}
