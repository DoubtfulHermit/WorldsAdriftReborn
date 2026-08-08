using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Per-account character rosters.
    ///
    /// This is the only table two processes touch, and only one of them writes:
    /// the login server owns it, and the game server SELECTs from it to resolve a
    /// characterUid. So there is no cross-process transaction to get wrong and no
    /// reverse write path at all.
    ///
    /// Note the name collides with WorldsAdriftServer.Persistence.CharacterRepository,
    /// which is the JSON-backed one this eventually replaces. Different namespace,
    /// and the adapter that converts to and from CharacterCreationData lives over
    /// there - nothing in this library names a game type.
    /// </summary>
    public sealed class CharacterRepository
    {
        private readonly Db db;

        public CharacterRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns =
            "character_uid, account_id, name, slot_index, is_empty_slot, data_json, "
            + "created_at, updated_at";

        /// <summary>
        /// The account's roster, in slot order. Ordered by the database rather
        /// than by the caller because the order is what the client renders and an
        /// unordered SELECT would shuffle the character list between requests.
        /// </summary>
        public IReadOnlyList<CharacterRecord> ListForAccount(long accountId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM characters WHERE account_id = @account_id "
                + "ORDER BY slot_index;";
            command.Parameters.AddWithValue("account_id", accountId);

            List<CharacterRecord> roster = new List<CharacterRecord>();

            using NpgsqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                roster.Add(Read(reader));
            }

            return roster;
        }

        /// <summary>
        /// One character by uid, whoever owns it. This is the game server's only
        /// query: it has a characterUid off the wire and needs to know whose it
        /// is.
        /// </summary>
        public CharacterRecord? Find(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM characters WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Replaces an account's whole roster with the given rows, in ONE
        /// transaction.
        ///
        /// Whole-roster rather than per-character because that is the shape the
        /// rules already come in: RosterPolicy.Normalize takes a list and returns
        /// the list the client should see, with slots renumbered and the trailing
        /// empty slot placed. Writing that back row by row would mean a moment
        /// where the stored roster is neither the old one nor the new one, and
        /// the unique index on (account_id, slot_index) would reject a
        /// renumbering that happened to cross itself.
        ///
        /// One transaction is also the performance rule: a durable commit costs
        /// milliseconds, so N autocommits is N times that, stalling whichever
        /// loop is waiting on the save.
        /// </summary>
        public void ReplaceRoster(long accountId, IReadOnlyList<CharacterRecord> roster)
        {
            if (roster == null)
            {
                throw new ArgumentNullException(nameof(roster));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();

            using (NpgsqlCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM characters WHERE account_id = @account_id;";
                delete.Parameters.AddWithValue("account_id", accountId);
                delete.ExecuteNonQuery();
            }

            foreach (CharacterRecord character in roster)
            {
                Insert(connection, transaction, character with { AccountId = accountId });
            }

            transaction.Commit();
        }

        /// <summary>
        /// Inserts or updates one character, keyed on its uid.
        ///
        /// created_at is preserved on update - the client re-sends the whole
        /// entry on every save, so taking its word for the creation time would
        /// reset it on every edit.
        /// </summary>
        public void Save(CharacterRecord character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();

            Insert(connection, transaction, character);

            transaction.Commit();
        }

        /// <summary>Removes one character. Returns false if it was not there.</summary>
        public bool Delete(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM characters WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            return command.ExecuteNonQuery() == 1;
        }

        private static void Insert(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CharacterRecord character)
        {
            using NpgsqlCommand command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO characters (" + Columns + ") VALUES ("
                + "@uid, @account_id, @name, @slot_index, @is_empty_slot, @data_json, "
                + "@created_at, @updated_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "account_id = excluded.account_id, "
                + "name = excluded.name, "
                + "slot_index = excluded.slot_index, "
                + "is_empty_slot = excluded.is_empty_slot, "
                + "data_json = excluded.data_json, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", character.CharacterUid);
            command.Parameters.AddWithValue("account_id", character.AccountId);
            command.Parameters.AddWithValue("name", character.Name);
            command.Parameters.AddWithValue("slot_index", character.SlotIndex);
            command.Parameters.AddWithValue("is_empty_slot", character.IsEmptySlot);
            command.Parameters.AddWithValue("data_json", character.DataJson);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(character.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(character.UpdatedAt));

            command.ExecuteNonQuery();
        }

        private static CharacterRecord Read(NpgsqlDataReader reader)
        {
            return new CharacterRecord(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                Timestamps.FromDb(reader.GetDateTime(6)),
                Timestamps.FromDb(reader.GetDateTime(7)));
        }
    }
}
