using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// A character's inventory, keyed by character uid.
    ///
    /// This is the FIRST table the game server writes. Everything before it was
    /// login-server-owned and game-server-read-only, which is why Db's docblock
    /// says there is one writer per table - that is still true, it is just that
    /// this table's one writer is the other process. There is still no
    /// cross-process transaction: the login server never touches this table
    /// except through the ON DELETE CASCADE that removes a deleted character's
    /// inventory with them.
    ///
    /// One row per character, replaced wholesale. 1081 is a full-state component
    /// with no add-delta - the client receives the entire list on every update -
    /// so a per-item table would be a join whose only consumer immediately
    /// flattens it back into one list. The shape follows the wire.
    /// </summary>
    public sealed class InventoryRepository
    {
        private readonly Db db;

        public InventoryRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns = "character_uid, data_json, created_at, updated_at";

        /// <summary>
        /// One character's stored inventory, or null if they have never had one
        /// saved. Null is the normal first-login answer and means "seed the
        /// defaults", not "something went wrong".
        /// </summary>
        public InventoryRecord? Find(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM character_inventories WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Writes a character's inventory, inserting or replacing.
        ///
        /// created_at is preserved on update for the same reason as on
        /// characters: the caller re-sends the whole record on every save, so
        /// taking its word for the creation time would reset it every time a
        /// player moved an item.
        /// </summary>
        public void Save(InventoryRecord inventory)
        {
            if (inventory == null)
            {
                throw new ArgumentNullException(nameof(inventory));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO character_inventories (" + Columns + ") VALUES ("
                + "@uid, @data_json, @created_at, @updated_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "data_json = excluded.data_json, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", inventory.CharacterUid);
            command.Parameters.AddWithValue("data_json", inventory.DataJson);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(inventory.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(inventory.UpdatedAt));

            command.ExecuteNonQuery();
        }

        /// <summary>Removes a character's inventory. False if there was none.</summary>
        public bool Delete(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM character_inventories WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            return command.ExecuteNonQuery() == 1;
        }

        private static InventoryRecord Read(NpgsqlDataReader reader)
        {
            return new InventoryRecord(
                reader.GetGuid(0),
                reader.GetString(1),
                Timestamps.FromDb(reader.GetDateTime(2)),
                Timestamps.FromDb(reader.GetDateTime(3)));
        }
    }
}
