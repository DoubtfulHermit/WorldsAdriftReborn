using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Alliance
{
    /// <summary>
    /// The permission vocabulary, pinned against the client that reads it.
    ///
    /// These look like assertions that a constant equals its own value, and they
    /// are not: each literal below was read out of the decompiled client, and the
    /// point of writing it down twice is that a transcription slip has to break a
    /// test rather than silently produce a permission nobody can hold.
    /// </summary>
    public sealed class AlliancePermissionsTests
    {
        [Fact]
        public void The_literals_are_the_ones_the_client_reads()
        {
            Assert.Equal("edit_group", AlliancePermissions.EditGroup);
            Assert.Equal("edit_message_of_the_day", AlliancePermissions.EditMessageOfTheDay);
            Assert.Equal("leader_chat", AlliancePermissions.LeaderChat);
            Assert.Equal("edit_ranks", AlliancePermissions.EditRanks);
            Assert.Equal("edit_members", AlliancePermissions.EditMembers);
            Assert.Equal("edit_officer_note", AlliancePermissions.EditOfficerNote);
            Assert.Equal("read_officer_note", AlliancePermissions.ReadOfficerNote);
        }

        /// <summary>
        /// The vocabulary is CLOSED. An invented permission is not a warning, it
        /// is a button nobody can ever see.
        /// </summary>
        [Fact]
        public void An_unknown_permission_is_dropped_rather_than_stored()
        {
            IReadOnlyList<string> kept = AlliancePermissions.Sanitize(
                new[] { "edit_members", "edit_everything", null, "leader_chat" });

            Assert.Equal(new[] { "edit_members", "leader_chat" }, kept);
        }

        [Fact]
        public void Duplicates_are_collapsed_and_order_is_kept()
        {
            IReadOnlyList<string> kept = AlliancePermissions.Sanitize(
                new[] { "leader_chat", "edit_members", "leader_chat" });

            Assert.Equal(new[] { "leader_chat", "edit_members" }, kept);
        }

        [Fact]
        public void A_null_permission_list_sanitises_to_empty_rather_than_throwing()
        {
            Assert.Empty(AlliancePermissions.Sanitize(null));
        }

        /// <summary>
        /// The leader rank must carry leader_chat, because that is where the
        /// client reads the MOTD gate from - and edit_message_of_the_day as well,
        /// because that is what the client WRITES when the box is ticked, so a
        /// founder rank without it would differ from one the client round-tripped.
        /// </summary>
        [Fact]
        public void The_default_leader_rank_carries_both_halves_of_the_motd_bug()
        {
            Assert.Contains(AlliancePermissions.LeaderChat, AlliancePermissions.DefaultLeader);
            Assert.Contains(AlliancePermissions.EditMessageOfTheDay, AlliancePermissions.DefaultLeader);
        }

        [Fact]
        public void The_default_leader_rank_carries_everything_the_client_knows()
        {
            foreach (string permission in AlliancePermissions.All)
            {
                Assert.Contains(permission, AlliancePermissions.DefaultLeader);
            }
        }

        [Fact]
        public void The_default_member_rank_carries_nothing()
        {
            Assert.Empty(AlliancePermissions.DefaultMember);
        }
    }
}
