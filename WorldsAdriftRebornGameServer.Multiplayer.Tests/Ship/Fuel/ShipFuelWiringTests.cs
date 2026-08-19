using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// IS THE FUEL SUBSYSTEM ACTUALLY PLUGGED IN? - the same guard
    /// <c>ScrapSalvageWiringTests</c> exists for, applied to fuel.
    ///
    /// Every rule in this feature lives in <c>ShipFuelPolicy</c>,
    /// <c>ShipFuelLedger</c> and <c>FuelGaugePushTracker</c>, and all three are
    /// fully covered. All three can be perfect while the gauge's needle sits dead,
    /// the tank never burns and the refuel prompt does nothing - because the wiring
    /// lives in the game-server assembly, which has no test project and cannot have
    /// one (it needs a Windows game install to compile against). This repo has
    /// twice shipped a green suite over an invisible feature for exactly that
    /// reason.
    ///
    /// So the six seams are asserted the only way available from here: by reading
    /// the production source off disk. These are deliberately COARSE. They cannot
    /// prove fuel is correct; the policy and ledger tests do that. They prove the
    /// wires are connected, and they go red the moment one is cut.
    /// </summary>
    public class ShipFuelWiringTests
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

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        /// <summary>
        /// THE headline seam. 1105 FuelGaugeState is the single component
        /// FuelGaugeVisualizer [Require]s; without this branch the seed batch that
        /// now names 1105 is DROPPED WHOLESALE (it is all-or-nothing), so the gauge
        /// would not merely stay dead - it would stop rendering entirely.
        /// </summary>
        [Fact]
        public void TheSerializerServes1105FromTheHullsTank()
        {
            string serializer = Source("WorldsAdriftRebornGameServer", "Game", "Components",
                "ComponentsSerializer.cs");

            Contains(serializer, "componentId == 1105",
                "FuelGaugeVisualizer [Require]s 1105 and nothing else, and the fuelGauge row now SEEDS "
                + "1105 - an unserved seeded id drops the whole interest batch and the part spawns invisible.");
            Contains(serializer, "ShipFuel.ReadingForGauge(entityId)",
                "The value must come from the hull's tank. A hardcoded number is worse than not serving "
                + "it at all: a needle pinned at a constant reads as a bug forever.");
            Contains(serializer, "new FuelGaugeState.Data(",
                "The gencode Data ctor is what the client's own serializer writes.");
        }

        /// <summary>Without the tick nothing ever burns, and fuel is decoration again.</summary>
        [Fact]
        public void TheMainLoopTicksTheFuelService()
        {
            // Newline-anchored ON PURPOSE. A bare substring search for
            // "ShipFuel.Tick();" survives someone commenting the call out, which is
            // exactly how this wire is most likely to die - and the mutation run
            // proved it: `//ShipFuel.Tick();` slipped straight past the loose form.
            Contains(Source("WorldsAdriftRebornGameServer", "WorldsAdriftRebornGameServer.cs"),
                "\n                ShipFuel.Tick();",
                "Burning, the gauge push and the run-dry cut all happen on this tick. Without it the tank "
                + "level never changes and the whole subsystem is inert.");
        }

        /// <summary>
        /// THE REFUEL DOOR. Without the drain call on the burn tick a tank empties
        /// once and can never be filled again by any gesture in the game.
        /// </summary>
        [Fact]
        public void TheBurnTickDrainsTheHullsBunker()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs");

            // Newline-anchored for the same reason ShipFuel.Tick() is: a bare
            // substring survives someone commenting the call out.
            Contains(service, "\n                DrainBunkers(hullEntityId);",
                "The bunker drain IS the refuel. Cut this line and every tank in the world is a "
                + "one-way valve - it burns down and nothing can ever put fuel back in.");
            Contains(service, "ShipFuelBunkerPolicy.Plan(",
                "The split across containers must come from the pure policy, or the invariant that a "
                + "plan's units sum to exactly what the tank can take is asserted nowhere.");
            Contains(service, "ShipFuelBunkerPolicy.ShouldDraw(",
                "The WIRE rule. Without this first line the drain walks a hull's containers and pushes "
                + "1081 every few seconds of flight, on entities riding a moving ship - the traffic "
                + "class that caused this project's desync spiral.");
            Contains(service, "ShipContainerService.IsContainer(",
                "Only a CONTAINER is a bunker. Without this gate the drain would walk every mounted "
                + "part, and InventoryService.ForEntity's DefaultModel fallback would hand a railing "
                + "an inventory full of gauntlets.");
        }

        /// <summary>
        /// THE OTHER HALF OF THE SAME DECISION, and it must be asserted as an ABSENCE
        /// because that is the failure mode: a prompt that reads "Activate Atlas
        /// Pulse" and quietly refuels instead is exactly the lying control
        /// PartInteractionPolicy exists to forbid, and nothing about it is visible
        /// server-side. Re-adding the dispatch here without re-adding the verb would
        /// also be dead code that reads as a working feature.
        /// </summary>
        [Fact]
        public void TheSkyCoreNoLongerAnswersWithARefuel()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "PartInteractionService.cs");

            Assert.False(service.Contains("ShipFuel.TryRefuel(", StringComparison.Ordinal),
                "The sky core's Activate is labelled by a BAKED client asset reading \"Activate Atlas "
                + "Pulse\" (InteractiveObjectVisualizer.GetTutorialStep -> MOUSE_OVER_CORE), and that "
                + "names a real retail action - 1306 ShipAtlasPulseState. Refuelling there is a control "
                + "that lies about what it does. Refuel is the hull's bunker; see ShipFuelBunkerPolicy.");
        }

        /// <summary>
        /// The 1111 mirror. The client DIFF-SUPPRESSES this stream, so a held stick
        /// is silent - if fuel does not see the deltas, a pilot who sets the throttle
        /// once flies for free forever.
        /// </summary>
        [Fact]
        public void TheThrottleStreamPassesThroughFuel()
        {
            string handler = Source("WorldsAdriftRebornGameServer", "Game", "Components", "Update",
                "Handlers", "ShipControlInput_Handler.cs");

            Contains(handler, "ShipFuel.OnControlInput(",
                "Fuel must mirror the 1111 delta, because a held throttle sends no packet at all.");
            Contains(handler, "Flight.OnControlInput(\n                entityId,\n                throttle,",
                "Flight must be given the throttle FUEL returned, not the raw one - that return value is "
                + "the thrust gate for a dry ship.");
        }

        /// <summary>
        /// A hull gets its fuel system from a mounted sky core. Both the fresh-mount
        /// and the boot-restore path must register, or a ship that has a core on disk
        /// comes back unmetered and its gauge reads a static full tank forever.
        /// </summary>
        [Fact]
        public void BothMountPathsGiveTheHullItsFuelSystem()
        {
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "PartMountService.cs"),
                "ShipFuel.OnPartMounted(",
                "Bolting a sky core on is what creates the tank.");
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Crafting", "LoosePartSpawner.cs"),
                "ShipFuel.OnPartMounted(",
                "A restored ship must get its tank back on boot, or every ship in the world silently "
                + "loses its fuel system at the next restart.");
        }

        /// <summary>
        /// Fuel mirrors flight's 1111 stream, so it must forget a player wherever
        /// flight forgets one. Two opinions of a stick nobody is touching, and the
        /// one that burns fuel is the wrong one to leave stale.
        /// </summary>
        [Fact]
        public void ADisconnectingPlayerIsForgottenByFuelToo()
        {
            string server = Source("WorldsAdriftRebornGameServer", "WorldsAdriftRebornGameServer.cs");

            Contains(server, "Flight.OnPlayerGone(ownEntity.Value);",
                "The anchor: fuel's cleanup must sit with flight's, not somewhere else that a later "
                + "refactor can separate them.");
            Contains(server, "ShipFuel.ForgetPlayer(ownEntity.Value);",
                "A disconnected pilot's mirrored throttle would otherwise keep burning fuel for a ship "
                + "flight has already settled to rest.");
        }

        /// <summary>
        /// Losing the core must make the hull unmetered again. Without this a ship
        /// whose core was lifted keeps burning fuel it can no longer be given - the
        /// one way this feature could genuinely strand somebody.
        /// </summary>
        [Fact]
        public void LosingTheCoreMakesTheHullUnmeteredAgain()
        {
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Components", "Update",
                "Handlers", "PlacementToolPlayerState_Handler.cs"),
                "ShipFuel.OnPartUnmounted(",
                "A lifted sky core takes the refuel door with it; the hull must stop burning, or it can "
                + "run dry with no way left to fill it.");
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Crafting", "MountedPartSalvageService.cs"),
                "ShipFuel.OnPartUnmounted(",
                "Salvaging the core is the same loss by another route.");
        }
    }
}
