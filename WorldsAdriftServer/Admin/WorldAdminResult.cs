using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    internal enum WorldAdminResultState { Missing, Unreadable, Ok }

    internal sealed class WorldAdminResult
    {
        private const string DefaultPath = "/tmp/wareborn-world-admin.result";
        private const int MaxMessageLength = 500;

        public string Action { get; private init; } = "";
        public long? TargetEntityId { get; private init; }
        public bool Success { get; private init; }
        public string Message { get; private init; } = "";
        public long CompletedAtUnixMs { get; private init; }

        internal static string ResultFilePath
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(
                    "WAREBORN_WORLD_ADMIN_RESULT_FILE");
                return string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured.Trim();
            }
        }

        public static (WorldAdminResultState State, WorldAdminResult? Result) Read() =>
            ReadFrom(ResultFilePath);

        internal static (WorldAdminResultState State, WorldAdminResult? Result) ReadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return (WorldAdminResultState.Missing, null);
                JObject o = JObject.Parse(File.ReadAllText(path));
                string action = (string?)o["action"] ?? "";
                if (action != "reset-resources" && action != "recall-ship" && action != "delete-ship")
                    return (WorldAdminResultState.Unreadable, null);
                if (o["success"]?.Type != JTokenType.Boolean
                    || o["message"]?.Type != JTokenType.String
                    || o["completedAtUnixMs"]?.Type != JTokenType.Integer)
                    return (WorldAdminResultState.Unreadable, null);
                long completed = (long)o["completedAtUnixMs"]!;
                if (completed <= 0) return (WorldAdminResultState.Unreadable, null);
                long? target = o["targetEntityId"]?.Type == JTokenType.Integer
                    ? (long?)o["targetEntityId"] : null;
                if ((action == "recall-ship" || action == "delete-ship")
                    && (!target.HasValue || target.Value <= 0))
                    return (WorldAdminResultState.Unreadable, null);
                if (action == "reset-resources" && target.HasValue)
                    return (WorldAdminResultState.Unreadable, null);
                string message = (string?)o["message"] ?? "";
                if (message.Length > MaxMessageLength)
                    message = message.Substring(0, MaxMessageLength);
                return (WorldAdminResultState.Ok, new WorldAdminResult
                {
                    Action = action,
                    TargetEntityId = target,
                    Success = (bool)o["success"]!,
                    Message = message,
                    CompletedAtUnixMs = completed,
                });
            }
            catch (Exception)
            {
                return (WorldAdminResultState.Unreadable, null);
            }
        }

        public JObject ToJson() => new JObject
        {
            ["action"] = Action,
            ["targetEntityId"] = TargetEntityId.HasValue ? TargetEntityId.Value : null,
            ["success"] = Success,
            ["message"] = Message,
            ["completedAtUnixMs"] = CompletedAtUnixMs,
        };
    }
}
