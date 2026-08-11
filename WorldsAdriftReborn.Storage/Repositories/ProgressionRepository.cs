using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// A character's knowledge / progression, keyed by character uid.
    ///
    /// The exact sibling of <see cref="InventoryRepository"/>: one row per
    /// character, replaced wholesale, written only by the game server. There is
    /// no cross-process transaction - the login server touches this table only
    /// through the ON DELETE CASCADE that removes a deleted character's
    /// progression with them.
    /// </summary>
    public sealed class ProgressionRepository
    {
        private readonly Db db;

        public ProgressionRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns = "character_uid, data_json, created_at, updated_at";

        /// <summary>
        /// One character's stored progression, or null if none has ever been
        /// saved. Null is the normal first-login answer and means "seed the
        /// defaults", not "something went wrong".
        /// </summary>
        public ProgressionRecord? Find(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM character_progression WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Writes a character's progression, inserting or replacing.
        ///
        /// created_at is preserved on update for the same reason as on the other
        /// tables: the caller re-sends the whole record on every save, so taking
        /// its word for the creation time would reset it on every knowledge gain.
        /// </summary>
        public void Save(ProgressionRecord progression)
        {
            if (progression == null)
            {
                throw new ArgumentNullException(nameof(progression));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO character_progression (" + Columns + ") VALUES ("
                + "@uid, @data_json, @created_at, @updated_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "data_json = excluded.data_json, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", progression.CharacterUid);
            command.Parameters.AddWithValue("data_json", progression.DataJson);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(progression.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(progression.UpdatedAt));

            command.ExecuteNonQuery();
        }

        /// <summary>Removes a character's progression. False if there was none.</summary>
        public bool Delete(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM character_progression WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            return command.ExecuteNonQuery() == 1;
        }

        private static ProgressionRecord Read(NpgsqlDataReader reader)
        {
            return new ProgressionRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                Timestamps.FromDb(reader.GetDateTime(2)),
                Timestamps.FromDb(reader.GetDateTime(3)));
        }
    }
}
