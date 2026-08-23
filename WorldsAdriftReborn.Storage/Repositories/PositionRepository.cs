using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Where a character logged out, keyed by character uid.
    ///
    /// The same shape as <see cref="InventoryRepository"/> and
    /// <see cref="ProgressionRepository"/>: one row per character, replaced
    /// wholesale, written only by the game server, and reached by the login
    /// server only through the ON DELETE CASCADE that removes a deleted
    /// character's position with them.
    /// </summary>
    public sealed class PositionRepository
    {
        private readonly Db db;

        public PositionRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns = "character_uid, x, y, z, created_at, updated_at, "
            + "built_ship_index, ship_local_x, ship_local_y, ship_local_z";

        /// <summary>
        /// One character's stored position, or null if none has ever been saved.
        /// Null is the normal first-login answer and means "use the spawn point",
        /// not "something went wrong".
        /// </summary>
        public PositionRecord? Find(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM character_positions WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Writes a character's position, inserting or replacing. created_at is
        /// preserved on update for the same reason as the sibling tables: the
        /// caller re-sends the whole record every save, so taking its word for the
        /// creation time would reset it on every step the player takes.
        /// </summary>
        public void Save(PositionRecord position)
        {
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "INSERT INTO character_positions (" + Columns + ") VALUES ("
                + "@uid, @x, @y, @z, @created_at, @updated_at, "
                + "@ship_index, @local_x, @local_y, @local_z) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "x = excluded.x, y = excluded.y, z = excluded.z, "
                + "built_ship_index = excluded.built_ship_index, "
                + "ship_local_x = excluded.ship_local_x, "
                + "ship_local_y = excluded.ship_local_y, "
                + "ship_local_z = excluded.ship_local_z, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", position.CharacterUid);
            command.Parameters.AddWithValue("x", position.X);
            command.Parameters.AddWithValue("y", position.Y);
            command.Parameters.AddWithValue("z", position.Z);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(position.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(position.UpdatedAt));
            command.Parameters.AddWithValue("ship_index", (object?)position.BuiltShipIndex ?? DBNull.Value);
            command.Parameters.AddWithValue("local_x", (object?)position.ShipLocalX ?? DBNull.Value);
            command.Parameters.AddWithValue("local_y", (object?)position.ShipLocalY ?? DBNull.Value);
            command.Parameters.AddWithValue("local_z", (object?)position.ShipLocalZ ?? DBNull.Value);

            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Forgets a character's position, so their next login uses the spawn
        /// point. This is the operator's unstick button.
        /// </summary>
        public bool Delete(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM character_positions WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            return command.ExecuteNonQuery() == 1;
        }

        private static PositionRecord Read(NpgsqlDataReader reader)
        {
            return new PositionRecord(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetInt64(2),
                reader.GetInt64(3),
                Timestamps.FromDb(reader.GetDateTime(4)),
                Timestamps.FromDb(reader.GetDateTime(5)),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9));
        }
    }
}
