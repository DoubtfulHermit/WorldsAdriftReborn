using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The login server's narrowly allowlisted writer for the game server's
    /// existing one-shot trigger files. It is deliberately not a shell or a
    /// generic command channel: each action is validated here and converted to
    /// one exact, already-supported game-server instruction.
    /// </summary>
    internal static class AdminCommandBridge
    {
        private const string DefaultTeleportFile = "/tmp/wareborn-teleport";
        private const string DefaultPlacementFile = "/tmp/wareborn-place";
        private const string DefaultWorldAdminFile = "/tmp/wareborn-world-admin";

        private static readonly object WriteGate = new object();

        internal static bool TryBuild(
            string? action,
            string? target,
            string? argument,
            out AdminCommandRequest command,
            out string error)
        {
            command = default;
            error = string.Empty;

            if (action == "teleport")
            {
                if (!TryEntity(target, out long entityId, out error))
                {
                    return false;
                }

                // Haven is the recovery destination. trades-challenge is the
                // PR3 visual-test destination; the game server performs its own
                // final guard and refuses it unless that terrain is registered.
                if (argument != "haven" && argument != "trades-challenge")
                {
                    error = "Choose one of the allowlisted travel destinations.";
                    return false;
                }

                command = new AdminCommandRequest(
                    "teleport", entityId, argument!, argument + " " + entityId,
                    TriggerPath("WAREBORN_TELEPORT_FILE", DefaultTeleportFile));
                return true;
            }

            if (action == "placement")
            {
                if (!TryEntity(target, out long entityId, out error))
                {
                    return false;
                }

                command = new AdminCommandRequest(
                    "placement", entityId, "first deployable", entityId.ToString(),
                    TriggerPath("WAREBORN_PLACEMENT_FILE", DefaultPlacementFile));
                return true;
            }

            if (action == "resources-reset")
            {
                if (argument != "all")
                {
                    error = "Resource reset is restricted to the explicit 'all' operation.";
                    return false;
                }
                command = new AdminCommandRequest(
                    "resources-reset", null, "all resource nodes", "reset-resources all",
                    TriggerPath("WAREBORN_WORLD_ADMIN_FILE", DefaultWorldAdminFile));
                return true;
            }

            if (action == "ship-recall" || action == "ship-stop"
                || action == "helm-release" || action == "ship-delete")
            {
                if (!TryPositiveId(target, "Select an exact ship domain.", out long hullId, out error))
                    return false;
                if (action == "ship-recall")
                {
                    if (!TryPositiveId(argument, "Select the connected player who should receive the recalled ship.",
                            out long playerId, out error))
                        return false;
                    command = new AdminCommandRequest(
                        action, hullId, "hull " + hullId + " to player " + playerId,
                        "recall-ship " + hullId + " " + playerId,
                        TriggerPath("WAREBORN_WORLD_ADMIN_FILE", DefaultWorldAdminFile),
                        relatedPlayerEntityId: playerId);
                    return true;
                }
                if (action == "ship-stop" || action == "helm-release")
                {
                    string payload = action == "ship-stop"
                        ? "stop-ship " + hullId
                        : "release-helm " + hullId;
                    command = new AdminCommandRequest(
                        action, hullId, "hull " + hullId, payload,
                        TriggerPath("WAREBORN_WORLD_ADMIN_FILE", DefaultWorldAdminFile));
                    return true;
                }
                command = new AdminCommandRequest(
                    action, hullId, "hull " + hullId, "delete-ship " + hullId + " DELETE",
                    TriggerPath("WAREBORN_WORLD_ADMIN_FILE", DefaultWorldAdminFile));
                return true;
            }

            error = "Unknown or unsupported admin action.";
            return false;
        }

        internal static bool TryQueue(AdminCommandRequest command, out string error)
        {
            error = string.Empty;
            string temporary = command.TriggerPath + ".admin-" + Guid.NewGuid().ToString("N") + ".tmp";

            lock (WriteGate)
            {
                try
                {
                    // Never overwrite an instruction that the game server has
                    // not consumed. A second click receives a visible busy result
                    // instead of silently replacing the first operator action.
                    if (File.Exists(command.TriggerPath))
                    {
                        error = "The game server has not consumed the previous command yet; wait a moment and retry.";
                        return false;
                    }

                    File.WriteAllText(temporary, command.Payload + Environment.NewLine);
                    File.Move(temporary, command.TriggerPath, overwrite: false);
                    return true;
                }
                catch (IOException)
                {
                    error = "The command queue is busy; wait a moment and retry.";
                    return false;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[warning] admin command bridge write failed: " + e.Message);
                    error = "The command bridge could not queue the request; check the login-server log.";
                    return false;
                }
                finally
                {
                    try
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    catch (Exception)
                    {
                        // A stale uniquely-named temp file cannot execute and is
                        // preferable to turning an admin request into a 500.
                    }
                }
            }
        }

        private static bool TryEntity(string? raw, out long entityId, out string error)
        {
            return TryPositiveId(raw, "Select a connected player.", out entityId, out error);
        }

        private static bool TryPositiveId(string? raw, string invalidMessage,
            out long entityId, out string error)
        {
            if (!long.TryParse(raw, out entityId) || entityId <= 0)
            {
                error = invalidMessage;
                return false;
            }
            error = string.Empty;
            return true;
        }

        private static string TriggerPath(string environmentVariable, string fallback)
        {
            string? configured = Environment.GetEnvironmentVariable(environmentVariable);
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        }
    }

    internal readonly struct AdminCommandRequest
    {
        public AdminCommandRequest(string action, long? targetEntityId, string detail, string payload,
            string triggerPath, long? relatedPlayerEntityId = null)
        {
            Action = action;
            TargetEntityId = targetEntityId;
            Detail = detail;
            Payload = payload;
            TriggerPath = triggerPath;
            RelatedPlayerEntityId = relatedPlayerEntityId;
        }

        public string Action { get; }
        public long? TargetEntityId { get; }
        public string Detail { get; }
        public string Payload { get; }
        public string TriggerPath { get; }
        public long? RelatedPlayerEntityId { get; }
    }

    /// <summary>A bounded, thread-safe audit trail rendered in the dashboard.</summary>
    internal static class AdminCommandJournal
    {
        private const int Capacity = 20;
        private static readonly object Gate = new object();
        private static readonly List<AdminCommandEntry> Entries = new List<AdminCommandEntry>();

        public static AdminCommandEntry Record(
            DateTimeOffset at,
            string action,
            long? targetEntityId,
            string detail,
            bool accepted,
            string message)
        {
            AdminCommandEntry entry = new AdminCommandEntry(
                at, action, targetEntityId, detail, accepted, message);
            lock (Gate)
            {
                Entries.Insert(0, entry);
                if (Entries.Count > Capacity)
                {
                    Entries.RemoveRange(Capacity, Entries.Count - Capacity);
                }
            }
            return entry;
        }

        public static JArray ToJson()
        {
            JArray result = new JArray();
            lock (Gate)
            {
                foreach (AdminCommandEntry entry in Entries)
                {
                    result.Add(entry.ToJson());
                }
            }
            return result;
        }

        internal static void ClearForTests()
        {
            lock (Gate)
            {
                Entries.Clear();
            }
        }
    }

    internal readonly struct AdminCommandEntry
    {
        public AdminCommandEntry(
            DateTimeOffset at,
            string action,
            long? targetEntityId,
            string detail,
            bool accepted,
            string message)
        {
            At = at;
            Action = action;
            TargetEntityId = targetEntityId;
            Detail = detail;
            Accepted = accepted;
            Message = message;
        }

        public DateTimeOffset At { get; }
        public string Action { get; }
        public long? TargetEntityId { get; }
        public string Detail { get; }
        public bool Accepted { get; }
        public string Message { get; }

        public JObject ToJson()
        {
            JObject result = new JObject
            {
                ["atUnixMs"] = At.ToUnixTimeMilliseconds(),
                ["action"] = Action,
                ["detail"] = Detail,
                ["accepted"] = Accepted,
                ["message"] = Message,
            };
            if (TargetEntityId.HasValue)
            {
                result["targetEntityId"] = TargetEntityId.Value;
            }
            return result;
        }
    }
}
