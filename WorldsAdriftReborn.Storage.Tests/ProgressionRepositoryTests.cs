using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Knowledge / progression rows. The shaping lives in the game server; what is
    /// tested here is that storing and reloading a progression payload gives back
    /// exactly what was stored, that it is keyed by character, and that it is
    /// removed with its character through the foreign-key cascade.
    /// </summary>
    public class ProgressionRepositoryTests
    {
        [PostgresFact]
        public void A_saved_progression_comes_back_field_for_field()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            ProgressionRecord saved = new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":9871}", TempDb.Now, TempDb.Now);

            db.Progressions.Save(saved);

            Assert.Equal(saved, db.Progressions.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void Saving_progression_again_replaces_it_rather_than_adding_a_second()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            db.Progressions.Save(new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":1}", TempDb.Now, TempDb.Now));
            db.Progressions.Save(new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":50}", TempDb.Now.AddHours(1), TempDb.Now.AddHours(1)));

            Assert.Equal("{\"Knowledge\":50}", db.Progressions.Find(character.CharacterUid)!.DataJson);
        }

        [PostgresFact]
        public void An_edit_does_not_reset_when_the_progression_was_created()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            db.Progressions.Save(new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":1}", TempDb.Now, TempDb.Now));
            db.Progressions.Save(new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":2}", TempDb.Now.AddDays(5), TempDb.Now.AddDays(5)));

            Assert.Equal(TempDb.Now, db.Progressions.Find(character.CharacterUid)!.CreatedAt);
        }

        [PostgresFact]
        public void An_unsaved_character_has_no_progression()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);

            Assert.Null(db.Progressions.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void Deleting_a_character_takes_its_progression_with_it()
        {
            // The ON DELETE CASCADE: a deleted character's progression is
            // unreachable, so it must go with them.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);
            db.Characters.Save(character);
            db.Progressions.Save(new ProgressionRecord(
                character.CharacterUid, "{\"Knowledge\":9871}", TempDb.Now, TempDb.Now));

            Assert.True(db.Characters.Delete(character.CharacterUid));
            Assert.Null(db.Progressions.Find(character.CharacterUid));
        }

        [PostgresFact]
        public void A_rewritten_roster_keeps_a_surviving_characters_progression()
        {
            // The knowledge counterpart of the inventory relog test: a character
            // still in the roster must keep its progression across a rewrite.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord hermit = TempDb.ACharacter(account.AccountId, "Hermit", 0);

            db.Characters.ReplaceRoster(account.AccountId, new[] { hermit });
            db.Progressions.Save(new ProgressionRecord(
                hermit.CharacterUid, "{\"Knowledge\":8781}", TempDb.Now, TempDb.Now));

            db.Characters.ReplaceRoster(account.AccountId, new[] { hermit });

            Assert.Equal("{\"Knowledge\":8781}", db.Progressions.Find(hermit.CharacterUid)!.DataJson);
        }

        [PostgresFact]
        public void Progression_cannot_belong_to_a_character_that_does_not_exist()
        {
            // The whole reason this is a Postgres table and not a JSON file: a uid
            // off the wire that names no character is refused by the foreign key.
            using TempDb db = new TempDb();

            Assert.ThrowsAny<Exception>(() => db.Progressions.Save(new ProgressionRecord(
                Guid.NewGuid(), "{\"Knowledge\":1}", TempDb.Now, TempDb.Now)));
        }
    }
}
