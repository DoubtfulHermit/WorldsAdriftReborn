using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Alliances, their ranks and their membership.
    ///
    /// Shaped like <see cref="CrewRepository"/> - whole-table reads for rebuilding
    /// the ledger, scoped reads for answering one HTTP request - with one
    /// difference that is not cosmetic: alliances have only ONE writer. Crews are
    /// written by both servers, because the game server holds a live crew ledger
    /// for the beacon and the chat channel. Nothing in the game server knows what
    /// an alliance is, so every row here is written by the login server answering
    /// the retail Social Sheet, and there is no second process to stay in step
    /// with.
    ///
    /// The database enforces what the client cannot survive being wrong: one
    /// alliance per character (alliance_members' primary key), one name
    /// (alliances_one_name), and exactly one default rank of each kind
    /// (the two partial unique indexes).
    /// </summary>
    public sealed class AllianceRepository : IAllianceStore
    {
        private readonly Db db;

        public AllianceRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string AllianceColumns =
            "alliance_id, region, name, description, message_of_the_day, emblem_url, "
            + "leader_uid, created_at, updated_at";

        private const string RankColumns =
            "rank_id, alliance_id, name, editable, rank_type, membership_type, permissions, sort_order";

        private const string MemberColumns =
            "character_uid, alliance_id, rank_id, officer_note, private_officer_note, "
            + "join_order, created_at, updated_at";

        // ----------------------------------------------------------------- reads

        public IReadOnlyList<AllianceRecord> AllAlliances()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + AllianceColumns + " FROM alliances ORDER BY created_at, alliance_id;";
            return ReadAlliances(command);
        }

        public IReadOnlyList<AllianceRankRecord> AllRanks()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + RankColumns + " FROM alliance_ranks ORDER BY alliance_id, sort_order, rank_id;";
            return ReadRanks(command);
        }

        public IReadOnlyList<AllianceMemberRecord> AllMembers()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + MemberColumns + " FROM alliance_members ORDER BY alliance_id, join_order;";
            return ReadMembers(command);
        }

        public AllianceRecord? FindAlliance(Guid allianceId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + AllianceColumns + " FROM alliances WHERE alliance_id = @id;";
            command.Parameters.AddWithValue("id", allianceId);

            IReadOnlyList<AllianceRecord> found = ReadAlliances(command);
            return found.Count == 0 ? null : found[0];
        }

        public AllianceMemberRecord? MemberOf(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + MemberColumns + " FROM alliance_members WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            IReadOnlyList<AllianceMemberRecord> found = ReadMembers(command);
            return found.Count == 0 ? null : found[0];
        }

        public IReadOnlyList<AllianceMemberRecord> MembersOf(Guid allianceId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + MemberColumns + " FROM alliance_members WHERE alliance_id = @id ORDER BY join_order;";
            command.Parameters.AddWithValue("id", allianceId);
            return ReadMembers(command);
        }

        public IReadOnlyList<AllianceRankRecord> RanksOf(Guid allianceId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + RankColumns + " FROM alliance_ranks WHERE alliance_id = @id "
                + "ORDER BY sort_order, rank_id;";
            command.Parameters.AddWithValue("id", allianceId);
            return ReadRanks(command);
        }

        public AllianceRankRecord? FindRank(Guid rankId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + RankColumns + " FROM alliance_ranks WHERE rank_id = @id;";
            command.Parameters.AddWithValue("id", rankId);

            IReadOnlyList<AllianceRankRecord> found = ReadRanks(command);
            return found.Count == 0 ? null : found[0];
        }

        // ---------------------------------------------------------------- writes

        /// <summary>
        /// Founds an alliance, or answers false when the name is taken.
        ///
        /// <c>ON CONFLICT DO NOTHING</c> rather than a prior SELECT: the unique
        /// index on <c>lower(name)</c> is the only check two racing founders both
        /// cannot pass. It covers the primary key too, which cannot realistically
        /// collide - the id is a fresh GUID - but costs nothing to have covered.
        /// </summary>
        public bool TryInsertAlliance(AllianceRecord alliance)
        {
            if (alliance == null) throw new ArgumentNullException(nameof(alliance));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO alliances (" + AllianceColumns + ") VALUES ("
                + "@id, @region, @name, @description, @motd, @emblem, @leader, @created_at, @updated_at) "
                + "ON CONFLICT DO NOTHING;";

            BindAlliance(command, alliance);
            return command.ExecuteNonQuery() == 1;
        }

        public void SaveAlliance(AllianceRecord alliance)
        {
            if (alliance == null) throw new ArgumentNullException(nameof(alliance));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            // created_at is preserved on update, as on every other table here: it
            // is the founding date the alliance panel prints.
            command.CommandText =
                "INSERT INTO alliances (" + AllianceColumns + ") VALUES ("
                + "@id, @region, @name, @description, @motd, @emblem, @leader, @created_at, @updated_at) "
                + "ON CONFLICT (alliance_id) DO UPDATE SET "
                + "name = excluded.name, description = excluded.description, "
                + "message_of_the_day = excluded.message_of_the_day, emblem_url = excluded.emblem_url, "
                + "leader_uid = excluded.leader_uid, updated_at = excluded.updated_at;";

            BindAlliance(command, alliance);
            command.ExecuteNonQuery();
        }

        public void SaveRank(AllianceRankRecord rank)
        {
            if (rank == null) throw new ArgumentNullException(nameof(rank));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO alliance_ranks (" + RankColumns + ") VALUES ("
                + "@rank_id, @alliance_id, @name, @editable, @rank_type, @membership_type, "
                + "@permissions, @sort_order) "
                + "ON CONFLICT (rank_id) DO UPDATE SET "
                + "name = excluded.name, editable = excluded.editable, "
                + "rank_type = excluded.rank_type, membership_type = excluded.membership_type, "
                + "permissions = excluded.permissions, sort_order = excluded.sort_order;";

            command.Parameters.AddWithValue("rank_id", rank.RankId);
            command.Parameters.AddWithValue("alliance_id", rank.AllianceId);
            command.Parameters.AddWithValue("name", rank.Name);
            command.Parameters.AddWithValue("editable", rank.Editable);
            command.Parameters.AddWithValue("rank_type", rank.RankType);
            command.Parameters.AddWithValue("membership_type", rank.MembershipType);
            command.Parameters.AddWithValue("permissions", rank.Permissions);
            command.Parameters.AddWithValue("sort_order", rank.SortOrder);
            command.ExecuteNonQuery();
        }

        public bool DeleteRank(Guid rankId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM alliance_ranks WHERE rank_id = @id;";
            command.Parameters.AddWithValue("id", rankId);
            return command.ExecuteNonQuery() == 1;
        }

        public void SaveMember(AllianceMemberRecord member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            // The primary key is the character, so moving somebody between
            // alliances is this same statement: the row follows them.
            command.CommandText =
                "INSERT INTO alliance_members (" + MemberColumns + ") VALUES ("
                + "@uid, @alliance_id, @rank_id, @officer_note, @private_note, "
                + "@join_order, @created_at, @updated_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "alliance_id = excluded.alliance_id, rank_id = excluded.rank_id, "
                + "officer_note = excluded.officer_note, "
                + "private_officer_note = excluded.private_officer_note, "
                + "join_order = excluded.join_order, updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", member.CharacterUid);
            command.Parameters.AddWithValue("alliance_id", member.AllianceId);
            command.Parameters.AddWithValue("rank_id", member.RankId);
            command.Parameters.AddWithValue("officer_note", member.OfficerNote);
            command.Parameters.AddWithValue("private_note", member.PrivateOfficerNote);
            command.Parameters.AddWithValue("join_order", member.JoinOrder);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(member.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(member.UpdatedAt));
            command.ExecuteNonQuery();
        }

        public bool RemoveMember(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM alliance_members WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// Dissolves an alliance. Ranks and memberships go with it through the
        /// cascade, so a disbanded alliance can never leave a member pointing at
        /// nothing.
        /// </summary>
        public bool DeleteAlliance(Guid allianceId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "DELETE FROM alliances WHERE alliance_id = @id;";
            command.Parameters.AddWithValue("id", allianceId);
            return command.ExecuteNonQuery() == 1;
        }

        // --------------------------------------------------------------- reading

        private static void BindAlliance(NpgsqlCommand command, AllianceRecord alliance)
        {
            command.Parameters.AddWithValue("id", alliance.AllianceId);
            command.Parameters.AddWithValue("region", alliance.Region);
            command.Parameters.AddWithValue("name", alliance.Name);
            command.Parameters.AddWithValue("description", alliance.Description);
            command.Parameters.AddWithValue("motd", alliance.MessageOfTheDay);
            command.Parameters.AddWithValue("emblem", alliance.EmblemUrl);
            command.Parameters.AddWithValue("leader", alliance.LeaderUid);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(alliance.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(alliance.UpdatedAt));
        }

        private static IReadOnlyList<AllianceRecord> ReadAlliances(NpgsqlCommand command)
        {
            List<AllianceRecord> alliances = new List<AllianceRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                alliances.Add(new AllianceRecord(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetGuid(6),
                    Timestamps.FromDb(reader.GetDateTime(7)),
                    Timestamps.FromDb(reader.GetDateTime(8))));
            }

            return alliances;
        }

        private static IReadOnlyList<AllianceRankRecord> ReadRanks(NpgsqlCommand command)
        {
            List<AllianceRankRecord> ranks = new List<AllianceRankRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ranks.Add(new AllianceRankRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetBoolean(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt32(7)));
            }

            return ranks;
        }

        private static IReadOnlyList<AllianceMemberRecord> ReadMembers(NpgsqlCommand command)
        {
            List<AllianceMemberRecord> members = new List<AllianceMemberRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                members.Add(new AllianceMemberRecord(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetGuid(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt32(5),
                    Timestamps.FromDb(reader.GetDateTime(6)),
                    Timestamps.FromDb(reader.GetDateTime(7))));
            }

            return members;
        }
    }
}
