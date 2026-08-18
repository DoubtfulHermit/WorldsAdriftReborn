namespace WorldsAdriftRebornGameServer.Multiplayer.Alliance
{
    /// <summary>
    /// The permission vocabulary an alliance rank carries.
    ///
    /// RECOVERED, and closed. These seven strings are the complete set the
    /// client's <c>SocialGroupParsers</c> understands - five it writes
    /// (<c>ServerRankPermissionsFromAllianceRank</c>, :225-249) and two more it
    /// only ever reads (<c>ParseAllianceRankFromRankDataModel</c>, :131-132). A
    /// string outside this set is not an error to the client, it is simply
    /// invisible: <c>permissions.Contains(...)</c> answers false and the player
    /// loses the button. So inventing a permission does not produce a warning, it
    /// produces a feature nobody can use.
    ///
    /// Pure and engine-free: the login server enforces these and the game server
    /// may one day gate alliance chat on them, and the two must not each keep
    /// their own spelling of the same word.
    /// </summary>
    public static class AlliancePermissions
    {
        /// <summary>Edit the alliance's description. Read by the client as
        /// <c>AllianceRank.EditGroup</c>, which gates the description field in
        /// YourAllianceTitleSegment.PopulateFields.</summary>
        public const string EditGroup = "edit_group";

        /// <summary>
        /// Edit the message of the day - WRITTEN by the client, never READ by it.
        ///
        /// This is the retail bug recorded in docs/research/findings-social-api.md
        /// and it must be reproduced, not corrected. SocialGroupParsers.cs:129-130:
        ///
        ///     bool editMessageOfTheDay = serverModel.permissions.Contains("leader_chat");
        ///     bool leaderChat          = serverModel.permissions.Contains("leader_chat");
        ///
        /// Both lines read <c>leader_chat</c>. A rank that carries only
        /// <c>edit_message_of_the_day</c> therefore renders with the MOTD field
        /// LOCKED, however obviously that contradicts the name. See
        /// <see cref="MotdIsReadFrom"/>, which is what the server actually gates on.
        /// </summary>
        public const string EditMessageOfTheDay = "edit_message_of_the_day";

        /// <summary>The alliance radio channel - and, because of the bug above,
        /// the de facto MOTD permission as well.</summary>
        public const string LeaderChat = "leader_chat";

        /// <summary>Create, modify and delete ranks.</summary>
        public const string EditRanks = "edit_ranks";

        /// <summary>
        /// Invite, accept applications, boot, and change other members' ranks.
        ///
        /// Note the client ORs this with "is the default leader rank"
        /// (SocialGroupParsers.cs:134), so a leader has it whether or not it is
        /// listed. The server lists it anyway rather than relying on that, because
        /// the two must agree even for a rank that is not the leader's.
        /// </summary>
        public const string EditMembers = "edit_members";

        /// <summary>
        /// Write another member's officer note. READ by the client, never written
        /// by it - so this is a server-set permission with no UI to grant it.
        /// </summary>
        public const string EditOfficerNote = "edit_officer_note";

        /// <summary>Read another member's officer note. Server-set only, as above.</summary>
        public const string ReadOfficerNote = "read_officer_note";

        /// <summary>
        /// Every permission the client understands, in the order the leader rank
        /// lists them. Anything not in here is invisible to the client.
        /// </summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            EditGroup,
            EditMessageOfTheDay,
            LeaderChat,
            EditRanks,
            EditMembers,
            EditOfficerNote,
            ReadOfficerNote,
        };

        /// <summary>
        /// The permission the server must actually check for "may edit the MOTD".
        ///
        /// It is <see cref="LeaderChat"/>, not <see cref="EditMessageOfTheDay"/>,
        /// because that is what the client's own gate reads. If the server enforced
        /// the honest name the two would disagree: the player would see an editable
        /// MOTD field they are refused when they use, or a locked one they are
        /// permitted. Matching the client's mistake is the only way the UI and the
        /// rule can say the same thing.
        /// </summary>
        public const string MotdIsReadFrom = LeaderChat;

        /// <summary>
        /// The founder's rank. Everything, including the two permissions the client
        /// cannot grant through any UI.
        ///
        /// <see cref="LeaderChat"/> AND <see cref="EditMessageOfTheDay"/> are both
        /// present deliberately: the client only reads the first, but it WRITES
        /// both whenever a rank has the MOTD box ticked, so a leader rank missing
        /// the second would differ from one the client had round-tripped.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultLeader = new[]
        {
            EditGroup,
            EditMessageOfTheDay,
            LeaderChat,
            EditRanks,
            EditMembers,
            EditOfficerNote,
            ReadOfficerNote,
        };

        /// <summary>
        /// The rank everyone else joins on: no permissions at all.
        ///
        /// Empty rather than "read the officer note": an alliance's default member
        /// is a member, and every capability past that is a decision the founder
        /// makes by creating a rank. An empty list is also the only default that
        /// cannot leak information the founder did not choose to share.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultMember = Array.Empty<string>();

        /// <summary>True when the client would recognise this string.</summary>
        public static bool IsKnown(string? permission)
        {
            if (permission == null) return false;

            foreach (string known in All)
            {
                if (string.Equals(known, permission, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Drops anything the client cannot read, preserving order and removing
        /// duplicates.
        ///
        /// Applied on the way IN, at rank create and rank modify. The client sends
        /// its permissions as a free JSON array, and an unknown entry stored now is
        /// an unknown entry emitted forever - invisible in the UI, but a real
        /// permission as far as any server-side check that spelled it the same way
        /// is concerned. Filtering at the boundary keeps "what is stored" and "what
        /// the player can see" the same set.
        /// </summary>
        public static IReadOnlyList<string> Sanitize(IEnumerable<string?>? requested)
        {
            List<string> kept = new List<string>();
            if (requested == null) return kept;

            foreach (string? candidate in requested)
            {
                if (!IsKnown(candidate)) continue;
                if (kept.Contains(candidate!)) continue;
                kept.Add(candidate!);
            }

            return kept;
        }
    }
}
