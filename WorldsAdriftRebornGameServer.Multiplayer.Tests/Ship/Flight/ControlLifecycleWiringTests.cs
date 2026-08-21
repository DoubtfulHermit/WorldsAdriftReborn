using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>Coarse guards for the game/client assembly seams around the pure policies.</summary>
    public sealed class ControlLifecycleWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        [Fact]
        public void Activate_release_is_emitted_on_physical_interact_key_up()
        {
            string client = Source("WorldsAdriftReborn", "Patching", "Interactions",
                "ActivateRelease_Patch.cs");
            Assert.Contains("verb == InteractVerb.Activate", client, StringComparison.Ordinal);
            Assert.Contains("input.GetButtonUp(InputButtons.Interact)", client, StringComparison.Ordinal);
            Assert.Contains("TriggerReleaseInteraction(target).FinishAndSend()", client,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Server_release_and_default_events_rearm_the_sail_edge()
        {
            string handler = Source("WorldsAdriftRebornGameServer", "Game", "Components", "Update",
                "Handlers", "InteractAgentState_Handler.cs");
            Assert.Contains("man.verb == InteractVerb.Default", handler, StringComparison.Ordinal);
            Assert.Contains("PartInteractions.OnInteractionReleased(\n                            entityId, man.target.Id)",
                handler, StringComparison.Ordinal);
            Assert.Contains("PartInteractions.OnInteractionReleased(\n                        entityId, release.interactEntityId.Id)",
                handler, StringComparison.Ordinal);
        }

        [Fact]
        public void Helm_input_is_filtered_before_delta_merge_and_cleaned_on_release()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            int filter = service.IndexOf("takeover.Filter(throttle, vertical)", StringComparison.Ordinal);
            int merge = service.IndexOf("held.Merge(throttle, vertical", StringComparison.Ordinal);
            Assert.True(filter >= 0 && merge > filter,
                "takeover filtering must happen before client deltas enter flight state");
            Assert.Contains("_takeoverInputs.Remove(playerEntityId)", service, StringComparison.Ordinal);
        }
    }
}
