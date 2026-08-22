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

        [Fact]
        public void Canvas_wake_activates_restored_hull_and_arms_docked_departure()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            int wake = service.IndexOf("internal bool WakeFromCanvasInteraction", StringComparison.Ordinal);
            Assert.True(wake >= 0);
            string body = service.Substring(wake, service.IndexOf("internal void RefreshDomainOwnership",
                wake, StringComparison.Ordinal) - wake);

            Assert.Contains("_activeHullIds.Add(hullEntityId)", body, StringComparison.Ordinal);
            Assert.Contains("domain.Flight.WakeForCanvas()", body, StringComparison.Ordinal);
            Assert.Contains("Crafting.BuiltShips.ShipyardForHull(hullEntityId)", body,
                StringComparison.Ordinal);
            Assert.Contains("_departingYardByHull[hullEntityId] = yardEntityId", body,
                StringComparison.Ordinal);

            string interaction = Source("WorldsAdriftRebornGameServer", "Game",
                "PartInteractionService.cs");
            Assert.Contains("Flight\n                            .WakeFromCanvasInteraction(hullEntityId.Value)",
                interaction, StringComparison.Ordinal);
        }

        [Fact]
        public void Helm_and_sail_interactions_use_server_side_physical_eligibility()
        {
            string flight = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            string parts = Source("WorldsAdriftRebornGameServer", "Game", "PartInteractionService.cs");
            string eligibility = Source("WorldsAdriftRebornGameServer", "Game",
                "ShipInteractionEligibility.cs");

            Assert.Contains("ShipInteractionEligibility.Allows(", flight, StringComparison.Ordinal);
            Assert.Contains("Multiplayer.Helm.ManRadius", flight, StringComparison.Ordinal);
            Assert.Contains("ShipInteractionEligibility.Allows(", parts, StringComparison.Ordinal);
            Assert.Contains("PartInteractionPolicy.ActivateRadius", parts, StringComparison.Ordinal);
            Assert.Contains("SentEntities\n                .WasSent(peer, targetEntityId)", eligibility,
                StringComparison.Ordinal);
            Assert.Contains("TryCenterFor(peerId", eligibility, StringComparison.Ordinal);
        }

        [Fact]
        public void Fixed_clock_is_opt_in_and_does_not_replace_the_024_wire_cadence()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            Assert.Contains("WAREBORN_FLIGHT_FIXED_STEP", service, StringComparison.Ordinal);
            Assert.Contains("new CadenceTimer(TimeSpan.FromSeconds(ShipMotionPolicy.SendIntervalSeconds))",
                service, StringComparison.Ordinal);
            Assert.Contains("session.AdvanceFixed(", service, StringComparison.Ordinal);
            Assert.Contains("FixedFlightClock.DefaultMaxCatchUpSteps", service, StringComparison.Ordinal);
        }

        [Fact]
        public void Durable_restore_uses_version_validation_and_rotates_authority()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");
            string persistence = Source("WorldsAdriftRebornGameServer", "Game", "Persistence",
                "WorldStatePersistence.cs");
            Assert.Contains("durable.TryRead(out FlightState restoredState", service,
                StringComparison.Ordinal);
            Assert.Contains("ShipDomain.RestoreAfterProcessRestart", service, StringComparison.Ordinal);
            Assert.Contains("UpdateBuiltShipFlight", persistence, StringComparison.Ordinal);
            Assert.Contains("snapshot.BuiltShips[i].FlightSnapshot", persistence,
                StringComparison.Ordinal);
            Assert.Contains("if (FixedStepEnabled && durable != null)", service,
                StringComparison.Ordinal);
            Assert.Contains("if (FixedStepEnabled)", service, StringComparison.Ordinal);
            Assert.Contains("record.FlightSnapshot = null", persistence,
                StringComparison.Ordinal);
        }
    }
}
