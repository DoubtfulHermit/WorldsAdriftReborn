using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Logout-position rows. The decision to use one lives in the game server;
    /// what is tested here is that a position survives the round trip EXACTLY,
    /// that it is keyed by character, and that it leaves with its character
    /// through the foreign-key cascade.
    /// </summary>
    public class PositionRepositoryTests
    {
        [PostgresFact]
        public void A_saved_position_comes_back_field_for_field()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            PositionRecord saved = new PositionRecord(
                character.CharacterUid, 70502113, -1277826, -4629165, TempDb.Now, TempDb.Now);

            db.Positions.Save(saved);

            Assert.Equal(saved, db.Positions.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void A_ship_relative_anchor_round_trips_and_can_be_cleared()
        {
            using TempDb db = new TempDb();
            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            PositionRecord aboard = new PositionRecord(character.CharacterUid,
                10, 20, 30, TempDb.Now, TempDb.Now, 4, 100, 200, -300);
            db.Positions.Save(aboard);
            Assert.Equal(aboard, db.Positions.Find(character.CharacterUid));

            db.Positions.Save(new PositionRecord(character.CharacterUid,
                40, 50, 60, TempDb.Now, TempDb.Now.AddMinutes(1)));
            PositionRecord ashore = db.Positions.Find(character.CharacterUid)!;
            Assert.Null(ashore.BuiltShipIndex);
            Assert.Null(ashore.ShipLocalX);
            Assert.Null(ashore.ShipLocalY);
            Assert.Null(ashore.ShipLocalZ);
        }

        /// <summary>
        /// The whole reason the coordinates are BIGINT columns rather than
        /// floats: a position that drifts on the way to the database and back is
        /// a player who returns slightly inside the floor they logged out on.
        /// </summary>
        [PostgresFact]
        public void Extreme_fixed_point_coordinates_survive_without_drifting()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            db.Positions.Save(new PositionRecord(character.CharacterUid,
                long.MinValue / 2, -8_246_337_208L, long.MaxValue / 2, TempDb.Now, TempDb.Now));

            PositionRecord? read = db.Positions.Find(character.CharacterUid);

            Assert.Equal(long.MinValue / 2, read!.X);
            Assert.Equal(-8_246_337_208L, read.Y);
            Assert.Equal(long.MaxValue / 2, read.Z);
        }

        [PostgresFact]
        public void Saving_again_moves_the_character_rather_than_adding_a_second_row()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            db.Positions.Save(new PositionRecord(
                character.CharacterUid, 1, 2, 3, TempDb.Now, TempDb.Now));
            db.Positions.Save(new PositionRecord(
                character.CharacterUid, 10, 20, 30, TempDb.Now.AddHours(1), TempDb.Now.AddHours(1)));

            PositionRecord? read = db.Positions.Find(character.CharacterUid);

            Assert.Equal(10, read!.X);
            Assert.Equal(TempDb.Now, read.CreatedAt);
            Assert.Equal(TempDb.Now.AddHours(1), read.UpdatedAt);
        }

        [PostgresFact]
        public void A_character_who_has_never_logged_out_has_no_row()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            Assert.Null(db.Positions.Find(character.CharacterUid));
        }

        /// <summary>
        /// The uid reaches this table from outside the database - the game server
        /// digs it out of a JSON blob a client published - so a made-up one must
        /// be refused rather than creating a position belonging to nobody.
        /// </summary>
        [PostgresFact]
        public void A_position_for_a_character_that_does_not_exist_is_refused()
        {
            using TempDb db = new TempDb();

            Assert.ThrowsAny<Exception>(() => db.Positions.Save(
                new PositionRecord(Guid.NewGuid(), 1, 2, 3, TempDb.Now, TempDb.Now)));
        }

        [PostgresFact]
        public void Deleting_a_position_forgets_it_and_reports_whether_there_was_one()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);
            db.Positions.Save(new PositionRecord(
                character.CharacterUid, 1, 2, 3, TempDb.Now, TempDb.Now));

            Assert.True(db.Positions.Delete(character.CharacterUid));
            Assert.Null(db.Positions.Find(character.CharacterUid));
            Assert.False(db.Positions.Delete(character.CharacterUid));
        }

        [PostgresFact]
        public void A_deleted_character_takes_its_position_with_it()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);
            db.Positions.Save(new PositionRecord(
                character.CharacterUid, 1, 2, 3, TempDb.Now, TempDb.Now));

            db.Characters.Delete(character.CharacterUid);

            Assert.Null(db.Positions.Find(character.CharacterUid));
        }
    }
}
