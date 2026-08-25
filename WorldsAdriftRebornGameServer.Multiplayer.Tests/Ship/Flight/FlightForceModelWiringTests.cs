using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// IS THE FORCE MODEL ACTUALLY PLUGGED IN? - the same guard
    /// <c>ShipPartMaterialWiringTests</c> and <c>ScrapSalvageWiringTests</c> exist
    /// for, aimed this time at the flight service.
    ///
    /// This guard was written because its absence was DEMONSTRATED, not feared.
    /// Deleting the <c>PropulsionFor(...)</c> argument from the service's
    /// <c>Advance</c> call compiles clean and leaves all 3,915 Multiplayer tests
    /// and all 1,192 server tests green, while every ship in the world silently
    /// goes back to flying at a flat 12 m/s. <see cref="ShipForceModelTests"/> and
    /// <see cref="FlightForceModelIntegrationTests"/> prove the physics; they
    /// cannot prove the physics is REACHED, because the game-server assembly has
    /// no test project - it needs a Windows game install to compile against.
    ///
    /// So the seam is asserted the only way available from here: by reading the
    /// production source off disk. This is a COARSE test. It cannot prove the
    /// propulsion is derived correctly. It proves the flight tick consults it at
    /// all, and it goes red the moment somebody unhooks it.
    /// </summary>
    public class FlightForceModelWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string FlightService() => File.ReadAllText(Path.Combine(RepoRoot(),
            "WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs"));

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        [Fact]
        public void TheFlightTickPassesPropulsionIntoTheIntegrator()
        {
            Contains(FlightService(), "PropulsionFor(hullEntityId, unfurledSails)",
                "the per-tick Advance call must hand the integrator this hull's real mass, "
                + "engine thrust and sail count. Without it the force model is dead code and "
                + "every ship flies at the flat tuned speed again, with no test noticing.");
        }

        [Fact]
        public void ThrustIsDerivedFromMountedEnginesRatherThanAssumed()
        {
            string service = FlightService();
            Contains(service, "Crafting.MountedParts.OnHull(hullEntityId)",
                "engine thrust must come from the parts actually mounted on THIS hull.");
            Contains(service, "ShipPartKinds.Engine",
                "the mounted parts must be filtered to engines; counting every part as an "
                + "engine would make a hull full of railings fly like a racer.");
            Contains(service, "_tuning.EngineThrustNewtons",
                "per-engine thrust must come from the tuning, not a local literal - the live "
                + "verdict on flight is a feel judgement made at the helm, and it must be "
                + "answerable with a restart rather than a rebuild.");
        }

        [Fact]
        public void MassIsDerivedFromTheHullRatherThanAssumed()
        {
            string service = FlightService();
            Contains(service, "ShipMassSnapshots.For(hullEntityId).TotalFlightMassKg",
                "flight mass must come off the ONE cached ShipMassSnapshot - the same typed "
                + "per-part policy that serves 1257/1121; otherwise mounting and salvage "
                + "only change the UI mass. The snapshot's evaluator is where "
                + "HullMassCalculator and the material geometry live, unit-tested.");
            Assert.False(service.Contains("ShipTotalMass.TotalFlightMassKg", StringComparison.Ordinal),
                "The retired flat hull+N*50 formula must not creep back beside the snapshot.");
        }

        [Fact]
        public void RuntimeAndInspectorUseTheSameEvaluatedForceSample()
        {
            string service = FlightService();
            Contains(service, "session.Advance(",
                "the runtime must advance the authoritative flight session.");
            Contains(service, "_wallFlightInfluence.Segments)",
                "the configured wall-force segments must reach that runtime advance call.");
            Contains(service, "domain.Flight.LastForceEvaluation",
                "the inspector must publish the sample consumed by runtime rather than "
                + "resampling varying wind against a newer clock.");
        }

        [Fact]
        public void TheForceModelStaysBehindItsOwnFlag()
        {
            string service = FlightService();
            Contains(service, "WAREBORN_FLIGHT_FORCES",
                "the force model must remain switchable. It changes how every existing ship "
                + "handles - hulls built before it existed have no reason to carry engines - "
                + "so turning it on is an operator decision, not a deploy side effect.");
            Contains(service, "if (!ForceModelEnabled)",
                "PropulsionFor must return null when the flag is off, which is what keeps the "
                + "integrator bit-identical to today's behaviour for a live server.");
        }
    }
}
