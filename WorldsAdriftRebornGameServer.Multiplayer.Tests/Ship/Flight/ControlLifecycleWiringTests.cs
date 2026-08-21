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
        public void First_authorized_helm_input_enters_delta_merge_without_a_takeover_delay()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            Assert.Contains("held.Merge(throttle, vertical", service, StringComparison.Ordinal);
            Assert.DoesNotContain("takeover.Filter(throttle, vertical)", service,
                StringComparison.Ordinal);
            Assert.DoesNotContain("release to neutral before commanding the helm", service,
                StringComparison.Ordinal);
        }
    }
}
