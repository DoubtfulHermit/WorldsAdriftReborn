using Npgsql;
using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The one table the GAME server writes. These are Postgres-gated like every
    /// other repository test and skip loudly without a database.
    /// </summary>
    public class InventoryRepositoryTests
    {
        private static InventoryRecord AnInventory(Guid characterUid, string json = "{\"Version\":1,\"Items\":[]}")
        {
            return new InventoryRecord(characterUid, json, TempDb.Now, TempDb.Now);
        }

        private static CharacterRecord SavedCharacter(TempDb db)
        {
            CharacterRecord character = TempDb.ACharacter(db.AnAccount().AccountId);
            db.Characters.Save(character);
            return character;
        }

        [PostgresFact]
        public void A_character_with_no_saved_inventory_reads_back_null()
        {
            // The normal first-login answer. It means "seed the defaults", not
            // "something went wrong", so it must not be an exception.
            using TempDb db = new TempDb();

            Assert.Null(db.Inventories.Find(Guid.NewGuid()));
        }

        [PostgresFact]
        public void An_inventory_saved_in_one_session_reads_back_in_the_next()
        {
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);
            string json = "{\"Version\":1,\"Width\":10,\"Items\":[{\"ItemId\":1200}]}";

            db.Inventories.Save(AnInventory(character.CharacterUid, json));

            InventoryRecord? read = db.Inventories.Find(character.CharacterUid);

            Assert.NotNull(read);
            Assert.Equal(json, read!.DataJson);
            Assert.Equal(character.CharacterUid, read.CharacterUid);
        }

        [PostgresFact]
        public void Saving_twice_replaces_rather_than_duplicating()
        {
            // Every drag writes, so this path runs constantly.
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);

            db.Inventories.Save(AnInventory(character.CharacterUid, "{\"Version\":1,\"Items\":[]}"));
            db.Inventories.Save(new InventoryRecord(
                character.CharacterUid, "{\"Version\":1,\"Items\":[1]}", TempDb.Now, TempDb.Now.AddMinutes(5)));

            Assert.Equal("{\"Version\":1,\"Items\":[1]}", db.Inventories.Find(character.CharacterUid)!.DataJson);
            Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM character_inventories;"));
        }

        [PostgresFact]
        public void The_creation_time_is_not_reset_by_a_later_save()
        {
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);

            db.Inventories.Save(AnInventory(character.CharacterUid));
            db.Inventories.Save(new InventoryRecord(
                character.CharacterUid, "{\"Version\":1}", TempDb.Now.AddDays(1), TempDb.Now.AddDays(1)));

            Assert.Equal(TempDb.Now, db.Inventories.Find(character.CharacterUid)!.CreatedAt);
        }

        [PostgresFact]
        public void An_inventory_for_a_character_that_does_not_exist_is_refused()
        {
            // The uid arrives from OUTSIDE - the game server digs it out of a
            // JSON blob a client published - so this is the one key in the
            // database that could be anything at all. An inventory belonging to
            // nobody would be unreachable by any future login.
            using TempDb db = new TempDb();

            Assert.Throws<PostgresException>(() => db.Inventories.Save(AnInventory(Guid.NewGuid())));
        }

        [PostgresFact]
        public void Deleting_a_character_takes_their_inventory_with_them()
        {
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);
            db.Inventories.Save(AnInventory(character.CharacterUid));

            db.Characters.Delete(character.CharacterUid);

            Assert.Null(db.Inventories.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void An_empty_payload_is_refused()
        {
            // An empty payload restores as an inventory with no grid, and the
            // client reads the grid size exactly once at checkout - so it could
            // never be corrected.
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);

            Assert.Throws<PostgresException>(() =>
                db.Execute(
                    "INSERT INTO character_inventories (character_uid, data_json, created_at, updated_at) "
                    + "VALUES (@uid, @json, @at, @at);",
                    ("uid", character.CharacterUid),
                    ("json", "   "),
                    ("at", TempDb.Now.UtcDateTime)));
        }

        [PostgresFact]
        public void Deleting_an_inventory_reports_whether_there_was_one()
        {
            using TempDb db = new TempDb();

            CharacterRecord character = SavedCharacter(db);
            db.Inventories.Save(AnInventory(character.CharacterUid));

            Assert.True(db.Inventories.Delete(character.CharacterUid));
            Assert.False(db.Inventories.Delete(character.CharacterUid));
        }

        [PostgresFact]
        public void Two_characters_keep_separate_inventories()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord one = TempDb.ACharacter(account.AccountId, "Billy", 0);
            CharacterRecord two = TempDb.ACharacter(account.AccountId, "Jim", 1);
            db.Characters.Save(one);
            db.Characters.Save(two);

            db.Inventories.Save(AnInventory(one.CharacterUid, "{\"Version\":1,\"who\":\"one\"}"));
            db.Inventories.Save(AnInventory(two.CharacterUid, "{\"Version\":1,\"who\":\"two\"}"));

            Assert.Contains("one", db.Inventories.Find(one.CharacterUid)!.DataJson);
            Assert.Contains("two", db.Inventories.Find(two.CharacterUid)!.DataJson);
        }

        [PostgresFact]
        public void Saving_null_is_a_programming_error_rather_than_a_silent_no_op()
        {
            using TempDb db = new TempDb();

            Assert.Throws<ArgumentNullException>(() => db.Inventories.Save(null!));
        }
    }
}
