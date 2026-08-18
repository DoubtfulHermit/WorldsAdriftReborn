using Npgsql;
using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Membership change requests - crew and alliance invites and applications.
    ///
    /// Reads are always scoped: an invite is only ever wanted from one of its two
    /// ends ("what have I been offered", "who has this crew invited"), and both
    /// are indexed. There is deliberately no whole-table read, because unlike
    /// crews there is no in-memory ledger to seed - the login server answers each
    /// HTTP request straight from here.
    /// </summary>
    public sealed class SocialInviteRepository : ISocialInviteStore
    {
        private readonly Db db;

        public SocialInviteRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns =
            "invite_id, target_id, target_type, character_uid, inviter_uid, "
            + "message, status, created_at, updated_at";

        /// <summary>One invite by id, or null when it never existed.</summary>
        public SocialInviteRecord? Find(string inviteId)
        {
            if (inviteId == null) throw new ArgumentNullException(nameof(inviteId));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT " + Columns + " FROM social_invites WHERE invite_id = @id;";
            command.Parameters.AddWithValue("id", inviteId);

            using NpgsqlDataReader reader = command.ExecuteReader();
            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Everything offered to, or applied for by, one character - newest last.
        ///
        /// Resolved statuses are included rather than filtered out: the client
        /// does its own filtering on <c>status == "new"</c>
        /// (<c>CrewClient.GetInvite</c>, <c>CrewClient.GetCrewMembers</c>), and a
        /// repository that pre-filtered would quietly diverge from it the first
        /// time a caller wanted history.
        /// </summary>
        public IReadOnlyList<SocialInviteRecord> ForCharacter(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + Columns + " FROM social_invites WHERE character_uid = @uid "
                + "ORDER BY created_at, invite_id;";
            command.Parameters.AddWithValue("uid", characterUid);
            return ReadAll(command);
        }

        /// <summary>Everything offered by one group - the crew's pending list.</summary>
        public IReadOnlyList<SocialInviteRecord> ForTarget(string targetId)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + Columns + " FROM social_invites WHERE target_id = @target "
                + "ORDER BY created_at, invite_id;";
            command.Parameters.AddWithValue("target", targetId);
            return ReadAll(command);
        }

        /// <summary>
        /// Every invite still awaiting an answer, across all groups.
        ///
        /// The crew ledger is rebuilt whole rather than per crew, because the
        /// policy questions are not local, and it needs the outstanding offers as
        /// much as the seated members: without them nothing can count how many
        /// seats a crew has already promised.
        /// </summary>
        public IReadOnlyList<SocialInviteRecord> AllLive()
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT " + Columns + " FROM social_invites WHERE status = @status "
                + "ORDER BY created_at, invite_id;";
            command.Parameters.AddWithValue("status", SocialInviteStatus.New);
            return ReadAll(command);
        }

        /// <summary>
        /// Inserts an invite, or returns false if a live one already covers the
        /// same (character, target) pair.
        ///
        /// The duplicate is caught by the partial unique index rather than by a
        /// read-then-write here, so two invites racing from two sessions cannot
        /// both pass a check and both insert. False maps to the client's own
        /// 'existing_invite' error code.
        /// </summary>
        public bool TryInsert(SocialInviteRecord invite)
        {
            if (invite == null) throw new ArgumentNullException(nameof(invite));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO social_invites (" + Columns + ") VALUES ("
                + "@id, @target, @target_type, @uid, @inviter, @message, @status, "
                + "@created_at, @updated_at) "
                + "ON CONFLICT DO NOTHING;";

            command.Parameters.AddWithValue("id", invite.InviteId);
            command.Parameters.AddWithValue("target", invite.TargetId);
            command.Parameters.AddWithValue("target_type", invite.TargetType);
            command.Parameters.AddWithValue("uid", invite.CharacterUid);
            command.Parameters.AddWithValue("inviter", (object?)invite.InviterUid ?? DBNull.Value);
            command.Parameters.AddWithValue("message", invite.Message);
            command.Parameters.AddWithValue("status", invite.Status);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(invite.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(invite.UpdatedAt));
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// Moves an invite out of 'new'. False when it was already resolved, which
        /// is how a double-click on ACCEPT stops being two joins.
        /// </summary>
        public bool Resolve(string inviteId, string status, DateTimeOffset at)
        {
            if (inviteId == null) throw new ArgumentNullException(nameof(inviteId));
            if (status == null) throw new ArgumentNullException(nameof(status));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE social_invites SET status = @status, updated_at = @at "
                + "WHERE invite_id = @id AND status = 'new';";
            command.Parameters.AddWithValue("id", inviteId);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("at", Timestamps.ToDb(at));
            return command.ExecuteNonQuery() == 1;
        }

        /// <summary>
        /// Cancels every live invite into one group. Used when a crew disbands:
        /// an outstanding offer to join something that no longer exists is a dead
        /// row that would otherwise sit in the invitee's list forever.
        /// </summary>
        public int CancelAllForTarget(string targetId, DateTimeOffset at)
        {
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));

            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText =
                "UPDATE social_invites SET status = 'cancelled', updated_at = @at "
                + "WHERE target_id = @target AND status = 'new';";
            command.Parameters.AddWithValue("target", targetId);
            command.Parameters.AddWithValue("at", Timestamps.ToDb(at));
            return command.ExecuteNonQuery();
        }

        private static IReadOnlyList<SocialInviteRecord> ReadAll(NpgsqlCommand command)
        {
            List<SocialInviteRecord> invites = new List<SocialInviteRecord>();
            using NpgsqlDataReader reader = command.ExecuteReader();
            while (reader.Read()) invites.Add(Read(reader));
            return invites;
        }

        private static SocialInviteRecord Read(NpgsqlDataReader reader) => new SocialInviteRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.GetString(5),
            reader.GetString(6),
            Timestamps.FromDb(reader.GetDateTime(7)),
            Timestamps.FromDb(reader.GetDateTime(8)));
    }
}
