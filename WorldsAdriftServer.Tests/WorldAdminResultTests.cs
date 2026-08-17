using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class WorldAdminResultTests
    {
        private static string TempFile() => Path.Combine(Path.GetTempPath(),
            "wareborn-world-admin-result-" + Guid.NewGuid().ToString("N") + ".json");

        [Fact]
        public void Missing_result_is_an_honest_missing_state()
        {
            var read = WorldAdminResult.ReadFrom(TempFile());
            Assert.Equal(WorldAdminResultState.Missing, read.State);
            Assert.Null(read.Result);
        }

        [Theory]
        [InlineData("not-json")]
        [InlineData("{\"action\":\"shell\",\"success\":true,\"completedAtUnixMs\":1}")]
        [InlineData("{\"action\":\"delete-ship\",\"success\":true,\"completedAtUnixMs\":1}")]
        [InlineData("{\"action\":\"reset-resources\",\"success\":\"yes\",\"completedAtUnixMs\":1}")]
        public void Malformed_or_non_allowlisted_results_are_unreadable(string json)
        {
            string path = TempFile(); File.WriteAllText(path, json);
            try { Assert.Equal(WorldAdminResultState.Unreadable,
                WorldAdminResult.ReadFrom(path).State); }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Result_is_allowlist_parsed_and_message_is_bounded()
        {
            string path = TempFile();
            JObject source = new JObject
            {
                ["action"] = "recall-ship", ["targetEntityId"] = 83,
                ["success"] = true, ["message"] = new string('x', 900),
                ["completedAtUnixMs"] = 1723200123000,
                ["secret"] = "must not pass",
            };
            File.WriteAllText(path, source.ToString());
            try
            {
                var read = WorldAdminResult.ReadFrom(path);
                Assert.Equal(WorldAdminResultState.Ok, read.State);
                JObject safe = read.Result!.ToJson();
                Assert.Equal("recall-ship", (string)safe["action"]!);
                Assert.Equal(83, (long)safe["targetEntityId"]!);
                Assert.True((bool)safe["success"]!);
                Assert.Equal(500, ((string)safe["message"]!).Length);
                Assert.Null(safe["secret"]);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("stop-ship")]
        [InlineData("release-helm")]
        public void Ship_recovery_results_require_an_exact_positive_hull(string action)
        {
            string path = TempFile();
            File.WriteAllText(path, new JObject
            {
                ["action"] = action, ["targetEntityId"] = 83,
                ["success"] = true, ["message"] = "done",
                ["completedAtUnixMs"] = 1723200123000,
            }.ToString());
            try
            {
                var read = WorldAdminResult.ReadFrom(path);
                Assert.Equal(WorldAdminResultState.Ok, read.State);
                Assert.Equal(action, read.Result!.Action);
                Assert.Equal(83, read.Result.TargetEntityId);
            }
            finally { File.Delete(path); }
        }
    }
}
