using System.Text.Json;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Consumes the authenticated web console's narrow one-shot command file on
    /// the authoritative poll loop. It is intentionally not a shell or generic RPC.
    /// </summary>
    internal sealed class AdminWorldCommandService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        private const string DefaultCommandFile = "/tmp/wareborn-world-admin";
        private const string DefaultResultFile = "/tmp/wareborn-world-admin.result";
        private readonly IClock _clock;
        private readonly string _commandFile;
        private readonly string _resultFile;
        private TimeSpan _nextPoll;

        public AdminWorldCommandService(IClock clock)
        {
            _clock = clock;
            _commandFile = PathFrom("WAREBORN_WORLD_ADMIN_FILE", DefaultCommandFile);
            _resultFile = PathFrom("WAREBORN_WORLD_ADMIN_RESULT_FILE", DefaultResultFile);
        }

        public void Tick()
        {
            if (_clock.Elapsed < _nextPoll) return;
            _nextPoll = _clock.Elapsed + PollInterval;
            if (!File.Exists(_commandFile)) return;

            string text;
            try
            {
                text = File.ReadAllText(_commandFile);
                File.Delete(_commandFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("[admin-world] could not consume command: " + e.Message);
                return;
            }

            if (!AdminWorldCommandPolicy.TryParse(text, out AdminWorldCommand command,
                    out string parseError))
            {
                Complete("unknown", null, false, "Rejected command: " + parseError);
                return;
            }

            bool success;
            string message;
            switch (command.Kind)
            {
                case AdminWorldCommandKind.ResetResources:
                    message = WorldsAdriftRebornGameServer.ResetHarvestResources();
                    success = true;
                    Complete("reset-resources", null, success, message);
                    break;
                case AdminWorldCommandKind.RecallShip:
                    success = TryRecall(command.HullEntityId, command.PlayerEntityId, out message);
                    Complete("recall-ship", command.HullEntityId, success, message);
                    break;
                case AdminWorldCommandKind.StopShip:
                    success = WorldsAdriftRebornGameServer.Flight.TryAdminStop(
                        command.HullEntityId, out message);
                    Complete("stop-ship", command.HullEntityId, success, message);
                    break;
                case AdminWorldCommandKind.ReleaseHelm:
                    success = WorldsAdriftRebornGameServer.Flight.TryAdminReleaseHelm(
                        command.HullEntityId, out message);
                    Complete("release-helm", command.HullEntityId, success, message);
                    break;
                case AdminWorldCommandKind.DeleteShip:
                    success = Crafting.ShipSalvageService.AdminDelete(
                        command.HullEntityId, out message);
                    Complete("delete-ship", command.HullEntityId, success, message);
                    break;
                default:
                    Complete("unknown", null, false, "Unsupported command kind.");
                    break;
            }
        }

        private static bool TryRecall(long hullId, long playerEntityId, out string message)
        {
            foreach ((ulong peerId, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (entityId != playerEntityId) continue;
                ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
                if (peer == null)
                {
                    message = "Player entity " + playerEntityId + " is no longer connected.";
                    return false;
                }
                FixedPointPosition center = WorldsAdriftRebornGameServer.ResourceInterest.CenterFor(peer);
                FixedPointPosition destination = new FixedPointPosition(
                    center.X + 8 * FixedPointPosition.UnitsPerMetre,
                    // Recall above the player rather than near terrain. Built
                    // hulls can span several metres below their origin; 15 m is
                    // enough clearance for Haven ridges and lets the operator
                    // see exactly where the ship arrived.
                    center.Y + 15 * FixedPointPosition.UnitsPerMetre,
                    center.Z);
                if (!WorldsAdriftRebornGameServer.Flight.TryAdminRecall(
                        hullId, destination, out message)) return false;
                message = "Recalled hull " + hullId + " beside player entity "
                    + playerEntityId + ".";
                return true;
            }
            message = "Player entity " + playerEntityId + " is no longer connected.";
            return false;
        }

        private void Complete(string action, long? target, bool success, string message)
        {
            var result = new
            {
                action,
                targetEntityId = target,
                success,
                message = message.Length <= 500 ? message : message.Substring(0, 500),
                completedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            string temporary = _resultFile + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(result));
                File.Move(temporary, _resultFile, overwrite: true);
                Console.WriteLine("[admin-world] " + action + " "
                    + (success ? "completed: " : "rejected: ") + message);
            }
            catch (Exception e)
            {
                Console.WriteLine("[admin-world] could not write result: " + e.Message);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static string PathFrom(string variable, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(variable);
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
