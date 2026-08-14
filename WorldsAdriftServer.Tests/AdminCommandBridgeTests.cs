using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class AdminCommandBridgeTests
    {
        [Fact]
        public void Teleport_is_targeted_and_destination_allowlisted()
        {
            Assert.True(AdminCommandBridge.TryBuild(
                "teleport", "42", "trades-challenge", out AdminCommandRequest command, out string error), error);
            Assert.Equal("trades-challenge 42", command.Payload);
            Assert.Equal(42, command.TargetEntityId);

            Assert.False(AdminCommandBridge.TryBuild(
                "teleport", "42", "coord", out _, out error));
            Assert.Contains("allowlisted", error);
        }

        [Theory]
        [InlineData("north", "0 0 1")]
        [InlineData("south", "0 0 -1")]
        [InlineData("east", "1 0 0")]
        [InlineData("west", "-1 0 0")]
        public void Ship_nudges_are_fixed_to_one_metre(string direction, string payload)
        {
            Assert.True(AdminCommandBridge.TryBuild(
                "ship-nudge", null, direction, out AdminCommandRequest command, out string error), error);
            Assert.Equal(payload, command.Payload);
            Assert.Null(command.TargetEntityId);
        }

        [Theory]
        [InlineData("ship-nudge", null, "up")]
        [InlineData("placement", "everyone", null)]
        [InlineData("shell", null, "systemctl restart")]
        public void Free_form_or_malformed_commands_are_refused(string action, string? target, string? argument)
        {
            Assert.False(AdminCommandBridge.TryBuild(action, target, argument, out _, out _));
        }

        [Fact]
        public void Queue_never_overwrites_an_unconsumed_command()
        {
            string directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "wareborn-admin-command-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, "trigger");

            try
            {
                AdminCommandRequest first = new AdminCommandRequest(
                    "test", 1, "first", "haven 1", path);
                AdminCommandRequest second = new AdminCommandRequest(
                    "test", 2, "second", "haven 2", path);

                Assert.True(AdminCommandBridge.TryQueue(first, out string error), error);
                Assert.False(AdminCommandBridge.TryQueue(second, out error));
                Assert.Contains("previous command", error);
                Assert.Equal("haven 1" + Environment.NewLine, File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Fact]
        public void World_operations_have_exact_allowlisted_payload_contracts()
        {
            Assert.True(AdminCommandBridge.TryBuild(
                "resources-reset", null, "all", out AdminCommandRequest reset, out string error), error);
            Assert.Equal("reset-resources all", reset.Payload);

            Assert.True(AdminCommandBridge.TryBuild(
                "ship-recall", "83", "12", out AdminCommandRequest recall, out error), error);
            Assert.Equal("recall-ship 83 12", recall.Payload);
            Assert.Equal(83, recall.TargetEntityId);
            Assert.Equal(12, recall.RelatedPlayerEntityId);

            Assert.True(AdminCommandBridge.TryBuild(
                "ship-delete", "83", null, out AdminCommandRequest delete, out error), error);
            Assert.Equal("delete-ship 83 DELETE", delete.Payload);
        }

        [Theory]
        [InlineData("resources-reset", null, "nearby")]
        [InlineData("ship-recall", "83", null)]
        [InlineData("ship-recall", "all", "12")]
        [InlineData("ship-delete", "all", null)]
        public void World_operations_refuse_ambiguous_targets(string action, string? target,
            string? argument)
        {
            Assert.False(AdminCommandBridge.TryBuild(action, target, argument, out _, out _));
        }
    }
}
