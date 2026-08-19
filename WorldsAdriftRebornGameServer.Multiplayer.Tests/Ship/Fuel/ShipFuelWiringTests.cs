using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
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
    /// So the seams are asserted the only way available from here: by reading the
    /// production source off disk. These are deliberately COARSE. They cannot prove
    /// fuel is correct; the policy and ledger tests do that. They prove the wires are
    /// connected, and they go red the moment one is cut.
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
                "The value must come from the hull's pooled tank. A hardcoded number is worse than not "
                + "serving it at all: a needle pinned at a constant reads as a bug forever.");
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
        /// THE REFUEL DOOR. Without this dispatch the power generator still shows the
        /// client's own "Refuel" prompt, the player holds E, and nothing happens -
        /// which is worse than no prompt at all, because the client is then making a
        /// promise the server does not keep.
        /// </summary>
        [Fact]
        public void HoldingEOnAGeneratorRefuelsTheShip()
        {
            string interaction = Source("WorldsAdriftRebornGameServer", "Game", "PartInteractionService.cs");

            // Newline-anchored for the same reason ShipFuel.Tick() is: a bare
            // substring survives someone commenting the call out.
            Contains(interaction,
                "\n            int? refuelled = WorldsAdriftRebornGameServer.ShipFuel.TryRefuel(",
                "The generator's Activate IS the refuel. Cut this and every tank in the world is a "
                + "one-way valve - it burns down, and the prompt that says \"Refuel\" does nothing.");

            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs");
            Contains(service, "IsGenerator(mount.Value.ItemType)",
                "The target must actually be a mounted GENERATOR. Without this gate, holding E on any "
                + "mounted part would empty the player's fuel into whichever hull it sits on.");
            Contains(service, "_ledger.Deposit(hullEntityId, carried)",
                "Ask the POOL first and take from the player only what it accepted, or a nearly-full "
                + "ship eats a whole stack.");
            // Newline-anchored: the mutation run proved a bare substring survives
            // `// _ledger.Withdraw(...)`, which is the most likely way this line dies.
            Contains(service, "\n                _ledger.Withdraw(hullEntityId, moved);",
                "The rollback. If the inventory drawdown fails after the pool accepted, the fuel must "
                + "come back out of the pool rather than being created from nothing - otherwise a "
                + "refuel that cannot be paid for is free fuel.");
            Contains(service,
                "CraftingPolicy.AvailableFor(model, InventoryWire.CategoryLookup, FuelPods.ItemTypeId)",
                "The count and the drawdown MUST agree on what counts as fuel, or a refuel can fill the "
                + "tank and then fail to pay for it. Both go through CraftingPolicy's matching rule.");
        }

        /// <summary>
        /// THE BUNKER DRAIN IS GONE, and its absence is asserted because bringing it
        /// back would silently double the refuel: fuel left in a ship's trunk would
        /// trickle into the tank on its own, and a player would never learn that the
        /// generator is the door. It only ever existed as a workaround for having no
        /// honest prompt, and the generator's prompt is honest.
        /// </summary>
        [Fact]
        public void TheBunkerDrainIsGone()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs");

            Assert.False(service.Contains("DrainBunkers", StringComparison.Ordinal),
                "Refuelling is holding E on the generator, which is what the client's own overlay asset "
                + "STANDARD_MOUSE_OVER_GENERATOR already promises. A second, invisible refuel path on "
                + "the burn tick would make the visible one look optional - and it walks every container "
                + "on every flying hull, which is the wire traffic the multiplayer-safety rule warns "
                + "about.");
            Assert.False(service.Contains("ShipFuelBunkerPolicy", StringComparison.Ordinal),
                "The pure policy behind the bunker drain was deleted with it.");
        }

        /// <summary>
        /// THE SKY CORE STAYS SILENT, and it must be asserted as an ABSENCE because
        /// that is the failure mode: a prompt that reads "Activate Atlas Pulse" and
        /// quietly refuels instead is exactly the lying control PartInteractionPolicy
        /// exists to forbid, and nothing about it is visible server-side.
        /// </summary>
        [Fact]
        public void TheSkyCoreNoLongerAnswersWithARefuel()
        {
            Assert.False(
                Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs")
                    .Contains("atlasSkyCore", StringComparison.Ordinal),
                "The sky core's Activate is labelled by a BAKED client asset reading \"Activate Atlas "
                + "Pulse\" (InteractiveObjectVisualizer.GetTutorialStep -> MOUSE_OVER_CORE), and that "
                + "names a real retail action - 1306 ShipAtlasPulseState. Refuelling there is a control "
                + "that lies about what it does. The refuel door is the power generator, whose own "
                + "baked prompt reads \"Refuel\".");

            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor("atlasSkyCore"));
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
        /// A hull gets its fuel system from a mounted POWER GENERATOR. Both the
        /// fresh-mount and the boot-restore path must register, or a ship that has a
        /// generator on disk comes back unmetered and its gauge reads a static full
        /// tank forever.
        /// </summary>
        [Fact]
        public void BothMountPathsGiveTheHullItsFuelSystem()
        {
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "PartMountService.cs"),
                "ShipFuel.OnPartMounted(",
                "Bolting a generator on is what gives the hull a tank.");
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Crafting", "LoosePartSpawner.cs"),
                "ShipFuel.OnPartMounted(",
                "A restored ship must get its tank back on boot, or every ship in the world silently "
                + "loses its fuel system at the next restart.");

            // BOTH schematic keys, or half the generators a player can craft are inert
            // props: powerGenerator and powerGenerator01 are two catalogue rows over
            // one PowerGenerator01 prefab.
            // The WHOLE predicate, not the two keys separately: the mutation run
            // proved that asserting `itemType == "powerGenerator"` on its own survives
            // an `&& false` spliced in beside it, which silently turns every base-key
            // generator back into an inert prop with the suite still green.
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs");
            Contains(service,
                "\n            itemType == \"powerGenerator\" || itemType == \"powerGenerator01\";",
                "BOTH schematic keys must count and neither may be gated. powerGenerator and "
                + "powerGenerator01 are two catalogue rows over the one PowerGenerator01 prefab, so "
                + "dropping either leaves half the generators a player can craft as inert props.");
            Contains(service, "_ledger.Register(partEntityId, hullEntityId, Capacity)",
                "The tank is keyed on the GENERATOR, not the hull - that is what makes two generators "
                + "pool, and what makes the fuel travel with the part when it is lifted off.");
        }

        /// <summary>
        /// THE SAFETY RULE, asserted at the two places it can be lost. It is the rule
        /// that stops this feature grounding a ship nobody consented to ground, and
        /// the mutation run walked past BOTH halves of it before this test existed:
        ///
        ///   * ONLY a generator gives a hull a fuel system. Drop the IsGenerator gate
        ///     on the mount seam and every lamp, panel and railing bolted to a hull
        ///     registers as a 100-unit tank, so a ship's capacity becomes a function
        ///     of its decoration and its engines cut when the decoration runs dry.
        ///   * A hull with NO generator reads FULL, never empty. Serve a zero there
        ///     and every ship in the world that has not built a generator gets a
        ///     needle pinned at empty - a lie in the punitive direction, about ships
        ///     this feature deliberately does not touch.
        ///
        /// Both live in the game-server assembly, which has no test project, so this
        /// is the only place either can be held.
        /// </summary>
        [Fact]
        public void AHullWithNoGeneratorHasNoFuelSystemAndReadsFull()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs");

            Contains(service, "\n            if (!Enabled || !IsGenerator(itemType))",
                "ONLY a power generator may give a hull a fuel system. Without this gate every mounted "
                + "part registers as a tank, and a ship's range becomes a function of how many lamps "
                + "are bolted to it.");
            Contains(service,
                "\n            return mount == null ? FuelReading.Unmetered : _ledger.Read(mount.Value.HullEntityId);",
                "A gauge on a hull with no generator must read a FULL static tank. That hull genuinely "
                + "has unlimited range, so a needle pinned at empty would be the lie in the punitive "
                + "direction - and FuelReading.Unmetered is the value the whole no-generator-no-gate "
                + "rule is written against.");
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
        /// Losing the last generator must make the hull unmetered again. Without this
        /// a ship whose generator was lifted keeps burning fuel it can no longer be
        /// given - the one way this feature could genuinely strand somebody.
        /// </summary>
        [Fact]
        public void LosingTheGeneratorMakesTheHullUnmeteredAgain()
        {
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Components", "Update",
                "Handlers", "PlacementToolPlayerState_Handler.cs"),
                "ShipFuel.OnPartUnmounted(",
                "A lifted generator takes the tank AND the refuel door with it; the hull must stop "
                + "burning, or it can run dry with no way left to fill it.");
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "Crafting", "MountedPartSalvageService.cs"),
                "ShipFuel.OnPartUnmounted(",
                "Salvaging the generator is the same loss by another route.");

            // ...and the service must actually RELEASE it. Both handlers can call in
            // correctly while the ledger keeps the generator on the hull, which is the
            // shape that strands somebody: the ship keeps burning fuel through a part
            // that is no longer there to refuel. Newline-anchored, because the
            // mutation run walked straight past a `false &&` on this line.
            Contains(Source("WorldsAdriftRebornGameServer", "Game", "ShipFuelService.cs"),
                "\n            if (_ledger.Unregister(partEntityId))",
                "OnPartUnmounted must release the generator from the hull's pool, or a stripped ship "
                + "goes on burning fuel it can no longer be given.");
        }
    }
}
