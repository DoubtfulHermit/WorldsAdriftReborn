using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Crews and their membership.
    ///
    /// Unlike the inventory, progression and position repositories this one is
    /// not a per-character key/value store: a crew is a relationship, so it is
    /// read WHOLE at boot into the in-memory ledger and written back as
    /// individual membership changes happen. The game server is the only writer.
    ///
    /// The database enforces the two invariants that matter even if a caller
    /// forgets: one crew per character (crew_members' primary key) and one
    /// character per slot (its unique constraint).
    /// </summary>
    public sealed class CrewRepository
    {
        private readonly Db db;

        public CrewRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string CrewColumns = "crew_id, leader_uid, num_slots, created_at, updated_at";
        private const string MemberColumns = "character_uid, crew_id, join_order, slot, created_at";

        /// <summary>Every crew. Read once at boot to seed the ledger.</summary>
        public IReadOnlyList<CrewRecord> AllCrews()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + CrewColumns + " FROM crews ORDER BY crew_id;";

            List<CrewRecord> crews = new List<CrewRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read()) crews.Add(ReadCrew(reader));
            return crews;
        }

        /// <summary>Every membership, ordered so join order is restored exactly.</summary>
        public IReadOnlyList<CrewMemberRecord> AllMembers()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + MemberColumns + " FROM crew_members ORDER BY crew_id, join_order;";

            List<CrewMemberRecord> members = new List<CrewMemberRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read()) members.Add(ReadMember(reader));
            return members;
        }

        public CrewMemberRecord? MemberOf(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + MemberColumns + " FROM crew_members WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadMember(reader) : null;
        }

        /// <summary>
        /// Writes a crew, inserting or updating leader and slot count. created_at
        /// is preserved on update, as on every other table here.
        /// </summary>
        public void SaveCrew(CrewRecord crew)
        {
            if (crew == null) throw new ArgumentNullException(nameof(crew));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO crews (" + CrewColumns + ") VALUES ("
                + "@crew_id, @leader_uid, @num_slots, @created_at, @updated_at) "
                + "ON CONFLICT (crew_id) DO UPDATE SET "
                + "leader_uid = excluded.leader_uid, num_slots = excluded.num_slots, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("crew_id", crew.CrewId);
            command.Parameters.AddWithValue("leader_uid", crew.LeaderUid);
            command.Parameters.AddWithValue("num_slots", crew.NumSlots);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(crew.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(crew.UpdatedAt));
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Writes a membership. Moving a character between crews is the same
        /// statement: the primary key is the character, so the row follows them.
        /// </summary>
        public void SaveMember(CrewMemberRecord member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO crew_members (" + MemberColumns + ") VALUES ("
                + "@uid, @crew_id, @join_order, @slot, @created_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "crew_id = excluded.crew_id, join_order = excluded.join_order, "
                + "slot = excluded.slot;";

            command.Parameters.AddWithValue("uid", member.CharacterUid);
            command.Parameters.AddWithValue("crew_id", member.CrewId);
            command.Parameters.AddWithValue("join_order", member.JoinOrder);
            command.Parameters.AddWithValue("slot", (object?)member.Slot ?? DBNull.Value);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(member.CreatedAt));
            command.ExecuteNonQuery();
        }

        /// <summary>Removes one member. False when they were not in a crew.</summary>
        public bool RemoveMember(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM crew_members WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// Disbands a crew. The membership rows go with it through the cascade, so
        /// a disbanded crew can never leave a member pointing at nothing.
        /// </summary>
        public bool DeleteCrew(string crewId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM crews WHERE crew_id = @crew_id;";
            command.Parameters.AddWithValue("crew_id", crewId);
            return command.ExecuteNonQuery() == 1;
        }

        private static CrewRecord ReadCrew(NpgsqlDataReader reader) => new CrewRecord(
            reader.GetString(0),
            reader.GetGuid(1),
            reader.GetInt32(2),
            Timestamps.FromDb(reader.GetDateTime(3)),
            Timestamps.FromDb(reader.GetDateTime(4)));

        private static CrewMemberRecord ReadMember(NpgsqlDataReader reader) => new CrewMemberRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            Timestamps.FromDb(reader.GetDateTime(4)));
    }
}
