using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class AdminWorldCommandPolicyTests
    {
        [Theory]
        [InlineData("reset-resources all", AdminWorldCommandKind.ResetResources, 0, 0)]
        [InlineData("recall-ship 83 12", AdminWorldCommandKind.RecallShip, 83, 12)]
        [InlineData("delete-ship 83 DELETE", AdminWorldCommandKind.DeleteShip, 83, 0)]
        public void Exact_allowlisted_commands_parse(string text, AdminWorldCommandKind kind,
            long hull, long player)
        {
            Assert.True(AdminWorldCommandPolicy.TryParse(text, out AdminWorldCommand command,
                out string error), error);
            Assert.Equal(kind, command.Kind);
            Assert.Equal(hull, command.HullEntityId);
            Assert.Equal(player, command.PlayerEntityId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("reset-resources haven")]
        [InlineData("recall-ship 0 12")]
        [InlineData("recall-ship 83 -1")]
        [InlineData("delete-ship 83 delete")]
        [InlineData("delete-ship 83 DELETE extra")]
        [InlineData("shell rm -rf")]
        public void Everything_outside_the_contract_is_rejected(string text)
        {
            Assert.False(AdminWorldCommandPolicy.TryParse(text, out _, out _));
        }
    }
}
