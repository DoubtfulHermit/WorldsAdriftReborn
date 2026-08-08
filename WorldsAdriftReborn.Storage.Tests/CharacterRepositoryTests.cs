using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Schema;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Rosters. The shaping rules - one trailing empty slot, stable ordering, the
    /// character limit - stay in RosterPolicy over in WorldsAdriftServer; what is
    /// tested here is that storing and reloading a roster gives back exactly what
    /// was stored, in the order the client will render it.
    /// </summary>
    public class CharacterRepositoryTests
    {
        [PostgresFact]
        public void A_saved_character_comes_back_field_for_field()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord saved = TempDb.ACharacter(account.AccountId, "Billy Bones");

            db.Characters.Save(saved);

            Assert.Equal(saved, db.Characters.Find(saved.CharacterUid));
        }

        [PostgresFact]
        public void The_cosmetics_blob_is_returned_byte_for_byte()
        {
            // The client is the only thing that understands it, so anything that
            // reorders or reformats it is corruption we cannot see.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            const string blob =
                "{\"Cosmetics\":{\"Head\":{\"id\":\"1\",\"n\":\"hat\"}},\"z\":1,\"a\":2,\"f\":1.50}";

            CharacterRecord saved = TempDb.ACharacter(account.AccountId) with { DataJson = blob };

            db.Characters.Save(saved);

            Assert.Equal(blob, db.Characters.Find(saved.CharacterUid)!.DataJson);
        }

        [PostgresFact]
        public void A_roster_comes_back_in_slot_order_however_it_was_written()
        {
            // The order is what the client renders; an unordered read would
            // shuffle the character list between requests.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.Save(TempDb.ACharacter(account.AccountId, "Third", 2));
            db.Characters.Save(TempDb.ACharacter(account.AccountId, "First", 0));
            db.Characters.Save(TempDb.ACharacter(account.AccountId, "Second", 1));

            Assert.Equal(
                new[] { "First", "Second", "Third" },
                db.Characters.ListForAccount(account.AccountId).Select(c => c.Name).ToArray());
        }

        [PostgresFact]
        public void One_players_roster_never_contains_anothers_characters()
        {
            // The whole reason accounts exist: "whose roster do I see".
            using TempDb db = new TempDb();

            AccountRecord mine = db.AnAccount("timu");
            AccountRecord theirs = db.AnAccount("friend");

            db.Characters.Save(TempDb.ACharacter(mine.AccountId, "Billy", 0));
            db.Characters.Save(TempDb.ACharacter(theirs.AccountId, "Silver", 0));

            Assert.Equal(
                new[] { "Billy" },
                db.Characters.ListForAccount(mine.AccountId).Select(c => c.Name).ToArray());
            Assert.Equal(
                new[] { "Silver" },
                db.Characters.ListForAccount(theirs.AccountId).Select(c => c.Name).ToArray());
        }

        [PostgresFact]
        public void An_account_with_no_characters_gets_an_empty_list_not_a_null()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            Assert.Empty(db.Characters.ListForAccount(account.AccountId));
            Assert.Empty(db.Characters.ListForAccount(999999));
        }

        [PostgresFact]
        public void Saving_a_character_again_updates_it_rather_than_adding_a_second()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord original = TempDb.ACharacter(account.AccountId, "Billy");

            db.Characters.Save(original);
            db.Characters.Save(original with
            {
                Name = "Billy Renamed",
                UpdatedAt = TempDb.Now.AddHours(1),
            });

            Assert.Single(db.Characters.ListForAccount(account.AccountId));
            Assert.Equal("Billy Renamed", db.Characters.Find(original.CharacterUid)!.Name);
        }

        [PostgresFact]
        public void An_edit_does_not_reset_when_the_character_was_created()
        {
            // The client re-sends the whole entry on every save, so taking its
            // word for the creation time would reset it on every edit.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord original = TempDb.ACharacter(account.AccountId);

            db.Characters.Save(original);
            db.Characters.Save(original with
            {
                CreatedAt = TempDb.Now.AddDays(5),
                UpdatedAt = TempDb.Now.AddDays(5),
            });

            Assert.Equal(TempDb.Now, db.Characters.Find(original.CharacterUid)!.CreatedAt);
        }

        [PostgresFact]
        public void The_game_server_can_resolve_a_character_uid_to_its_owner()
        {
            // The game server's only query against this table.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord character = TempDb.ACharacter(account.AccountId);

            db.Characters.Save(character);

            Assert.Equal(account.AccountId, db.Characters.Find(character.CharacterUid)!.AccountId);
            Assert.Null(db.Characters.Find(Guid.NewGuid()));
            Assert.Null(db.Characters.Find(Guid.Empty));
        }

        [PostgresFact]
        public void Replacing_a_roster_can_renumber_slots_across_each_other()
        {
            // Row by row this would trip the unique index on (account, slot) the
            // moment a renumbering crossed itself. In one transaction it cannot.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            CharacterRecord first = TempDb.ACharacter(account.AccountId, "First", 0);
            CharacterRecord second = TempDb.ACharacter(account.AccountId, "Second", 1);

            db.Characters.ReplaceRoster(account.AccountId, new[] { first, second });

            db.Characters.ReplaceRoster(
                account.AccountId,
                new[] { second with { SlotIndex = 0 }, first with { SlotIndex = 1 } });

            Assert.Equal(
                new[] { "Second", "First" },
                db.Characters.ListForAccount(account.AccountId).Select(c => c.Name).ToArray());
        }

        [PostgresFact]
        public void Replacing_a_roster_drops_the_characters_that_are_no_longer_in_it()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            CharacterRecord keep = TempDb.ACharacter(account.AccountId, "Keep", 0);
            CharacterRecord drop = TempDb.ACharacter(account.AccountId, "Drop", 1);

            db.Characters.ReplaceRoster(account.AccountId, new[] { keep, drop });
            db.Characters.ReplaceRoster(account.AccountId, new[] { keep });

            Assert.Single(db.Characters.ListForAccount(account.AccountId));
            Assert.Null(db.Characters.Find(drop.CharacterUid));
        }

        [PostgresFact]
        public void Replacing_one_players_roster_leaves_everybody_elses_alone()
        {
            using TempDb db = new TempDb();

            AccountRecord mine = db.AnAccount("timu");
            AccountRecord theirs = db.AnAccount("friend");

            db.Characters.Save(TempDb.ACharacter(theirs.AccountId, "Silver", 0));
            db.Characters.ReplaceRoster(
                mine.AccountId, new[] { TempDb.ACharacter(mine.AccountId, "Billy", 0) });

            Assert.Single(db.Characters.ListForAccount(theirs.AccountId));
        }

        [PostgresFact]
        public void An_empty_replacement_clears_the_roster()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.Save(TempDb.ACharacter(account.AccountId));
            db.Characters.ReplaceRoster(account.AccountId, Array.Empty<CharacterRecord>());

            Assert.Empty(db.Characters.ListForAccount(account.AccountId));
        }

        [PostgresFact]
        public void A_failed_roster_write_leaves_the_previous_roster_intact()
        {
            // One transaction, so a bad row in the middle of a save cannot leave
            // the player with half a character list.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            CharacterRecord good = TempDb.ACharacter(account.AccountId, "Billy", 0);

            db.Characters.ReplaceRoster(account.AccountId, new[] { good });

            Assert.ThrowsAny<Exception>(() => db.Characters.ReplaceRoster(
                account.AccountId,
                new[]
                {
                    TempDb.ACharacter(account.AccountId, "Fine", 0),
                    TempDb.ACharacter(account.AccountId, "Broken", SchemaScripts.MaxCharacters + 1),
                }));

            Assert.Equal(
                new[] { "Billy" },
                db.Characters.ListForAccount(account.AccountId).Select(c => c.Name).ToArray());
        }

        [PostgresFact]
        public void A_roster_may_hold_the_full_set_plus_its_create_a_character_slot()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            List<CharacterRecord> roster = Enumerable
                .Range(0, SchemaScripts.MaxCharacters)
                .Select(i => TempDb.ACharacter(account.AccountId, "Traveller " + i, i))
                .ToList();

            roster.Add(TempDb.ACharacter(
                account.AccountId, "New Traveller", SchemaScripts.MaxCharacters, empty: true));

            db.Characters.ReplaceRoster(account.AccountId, roster);

            IReadOnlyList<CharacterRecord> stored = db.Characters.ListForAccount(account.AccountId);

            Assert.Equal(SchemaScripts.MaxCharacters + 1, stored.Count);
            Assert.Single(stored, c => c.IsEmptySlot);
            Assert.True(stored[^1].IsEmptySlot);
        }

        [PostgresFact]
        public void Deleting_a_character_removes_only_that_one()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            CharacterRecord keep = TempDb.ACharacter(account.AccountId, "Keep", 0);
            CharacterRecord go = TempDb.ACharacter(account.AccountId, "Go", 1);

            db.Characters.Save(keep);
            db.Characters.Save(go);

            Assert.True(db.Characters.Delete(go.CharacterUid));
            Assert.False(db.Characters.Delete(go.CharacterUid));
            Assert.Single(db.Characters.ListForAccount(account.AccountId));
            Assert.NotNull(db.Characters.Find(keep.CharacterUid));
        }

        [PostgresFact]
        public void A_roster_write_stamps_the_owner_it_was_told_rather_than_the_one_in_the_rows()
        {
            // Guards against an adapter that builds records before it knows the
            // account, which is the shape the conversion on the server side has.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            db.Characters.ReplaceRoster(
                account.AccountId, new[] { TempDb.ACharacter(0, "Billy", 0) });

            Assert.Single(db.Characters.ListForAccount(account.AccountId));
        }
    }
}
