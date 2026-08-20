using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// IS THE UNDERSTORM ACTUALLY PLUGGED IN?
    ///
    /// The tests next door prove what the storm MEANS: when it fires, what the two
    /// integers say, when the reset is due. Not one of them can prove that the main
    /// loop ever calls <c>Tick</c>, that the wire ever puts 1254 on a socket, or
    /// that the reset the storm calls is the real one. That gap is not theoretical
    /// here - this repo has TWICE shipped a green suite over a feature nobody had
    /// connected, and tree felling shipped green and was shown to nobody for days.
    ///
    /// The game-server assembly has no test project of its own (it needs a Windows
    /// game install to compile against), so the connection is asserted the way
    /// <c>ComponentSeedOutcomeWiringTests</c> and <c>ShipContainerWiringTests</c>
    /// already do it: by reading the production source off disk. Coarse on purpose.
    /// It cannot prove the storm is right; it proves the storm is CONNECTED, and it
    /// goes red the moment somebody unplugs it.
    /// </summary>
    public class IslandStormWiringTests
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
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string Server() => Source(
            "WorldsAdriftRebornGameServer", "WorldsAdriftRebornGameServer.cs");

        private static string Wire() => Source(
            "WorldsAdriftRebornGameServer", "Game", "IslandStormWire.cs");

        private static void Contains(string haystack, string needle, string why) =>
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);

        private static void DoesNotContain(string haystack, string needle, string why) =>
            Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
                "Did NOT expect to find `" + needle + "`. " + why);

        // ====================================================================
        // MUTATION: "delete the service tick"
        // ====================================================================

        [Fact]
        public void Mutation_the_main_loop_actually_ticks_the_storm_service()
        {
            Contains(Server(), "Storms.Tick();",
                "Without this call every storm test in this suite passes over a "
                + "feature that never runs. This is the single line that makes the "
                + "understorm exist at all.");
        }

        [Fact]
        public void The_storm_service_is_constructed_with_the_real_wire_and_the_real_islands()
        {
            string server = Server();
            Contains(server, "new Multiplayer.Islands.IslandStormService(",
                "Something has to build the service.");
            Contains(server, "new Game.IslandStormWire.Wire()",
                "A service built with a stub wire would tick happily and send nothing.");
            Contains(server, "IslandTopology.All",
                "The schedule must cover the islands this world actually serves.");
        }

        [Fact]
        public void The_service_is_declared_after_the_island_topology_it_reads()
        {
            // Static field initialisers run in TEXTUAL order. Declared before
            // IslandTopology, this would read a null registry and schedule storms for
            // an empty world - silently, and only on the release path.
            string server = Server();
            int topology = server.IndexOf("IslandTopology =", StringComparison.Ordinal);
            int storms = server.IndexOf("Storms = BuildStormService()", StringComparison.Ordinal);

            Assert.True(topology > 0, "IslandTopology declaration not found");
            Assert.True(storms > 0, "Storms declaration not found");
            Assert.True(storms > topology,
                "Storms is declared BEFORE IslandTopology, so it will read a null registry.");
        }

        [Fact]
        public void Every_operator_knob_is_read_from_the_environment()
        {
            string server = Server();
            foreach (string knob in new[]
                     {
                         "IslandStormPolicy.EnabledFromEnvironment()",
                         "IslandStormPolicy.CadenceEnvVar",
                         "IslandStormPolicy.DurationEnvVar",
                         "IslandStormPolicy.JitterEnvVar",
                         "IslandStormPolicy.CountdownRefreshEnvVar",
                     })
            {
                Contains(server, knob, "A knob nobody reads is a knob that does not exist.");
            }
        }

        [Fact]
        public void The_boot_log_says_whether_storms_are_on_or_off()
        {
            // A feature that is off should SAY so, with the name of the variable that
            // turns it on. A silent boot is how an unplugged feature survives.
            string server = Server();
            Contains(server, "[info] understorms: ON",
                "An operator must be able to see the settled schedule in the log.");
            Contains(server, "[info] understorms: OFF",
                "An operator must be able to see that it is off, and what turns it on.");
        }

        // ====================================================================
        // MUTATION: "no-op the 1254 push"
        // ====================================================================

        [Fact]
        public void Mutation_the_wire_really_sends_a_1254_component_update()
        {
            string wire = Wire();
            Contains(wire, "SendOPHelper.SendComponentUpdateOp(",
                "A PushTimer that records and sends nothing is exactly the failure "
                + "shape this file exists to prevent.");
            Contains(wire, "IslandLightningTimerState.Update()",
                "The update must be the real generated 1254 type.");
            Contains(wire, "IslandLightningTimerStateComponentId = 1254",
                "1254 IslandLightningTimerState, namespace Bossa.Travellers.Loot.");
        }

        [Fact]
        public void The_wire_sends_all_three_fields_the_storm_owns()
        {
            string wire = Wire();
            Contains(wire, "SetEstimatedMilliTillNextLightning(",
                "Without the countdown there is no 30 s warning.");
            Contains(wire, "SetEstimatedMilliTillLightningEnd(",
                "This IS the storm switch. Without it nothing ever storms.");
            Contains(wire, "SetGeneration(",
                "The cycle counter is the only field that says WHICH storm this is.");
        }

        [Fact]
        public void The_wire_keeps_the_peers_stored_component_in_step()
        {
            Contains(Wire(), "timer.ApplyTo(stored)",
                "Without this, a later re-serve from the stored object resurrects the "
                + "seeded 50 s countdown - or, far worse, re-asserts a storm that has "
                + "already ended.");
        }

        [Fact]
        public void A_player_who_logs_in_during_a_storm_is_seeded_into_it()
        {
            // Updates only reach peers that ALREADY hold the component, so without
            // this a joiner gets the static seed - clear sky - and hears nothing
            // until the storm is over. They would stand under ninety bolts and see
            // none of them, and it would read as "the storm did not work".
            string serializer = Source("WorldsAdriftRebornGameServer", "Game",
                "Components", "ComponentsSerializer.cs");

            Contains(serializer, "IslandStormWire.SeedFor(entityId)",
                "The 1254 seed must be answered from the live schedule.");
            Contains(serializer, "storm?.MillisTillLightningEnd ?? 0",
                "A joiner mid-storm needs the storm switch set; a joiner outside one "
                + "needs it at exactly 0.");
            Contains(Wire(), "internal static IslandStormUpdate? SeedFor(long entityId)",
                "The seam the serializer calls.");
            Contains(Wire(), "if (!WorldsAdriftRebornGameServer.Storms.Enabled) return null;",
                "With storms off the seed must be byte-identical to what it always was.");
        }

        [Fact]
        public void The_seed_still_pins_isLightningActive_to_false()
        {
            string serializer = Source("WorldsAdriftRebornGameServer", "Game",
                "Components", "ComponentsSerializer.cs");
            int seed = serializer.IndexOf("IslandLightningTimerStateData(", StringComparison.Ordinal);
            Assert.True(seed > 0, "the 1254 seed was not found");
            int end = serializer.IndexOf("obj = ilData;", seed, StringComparison.Ordinal);
            Assert.True(end > seed, "the end of the 1254 seed was not found");

            string block = serializer.Substring(seed, end - seed);
            Contains(block, "false,",
                "isLightningActive must stay hard-coded false in the seed - a storm "
                + "seeded with it true would teleport the island toward Y -1500.");
            Assert.DoesNotContain("storm?.IsLightningActive", block, StringComparison.Ordinal);
        }

        // ====================================================================
        // MUTATION: "write isLightningActive = true"
        // ====================================================================

        [Fact]
        public void Mutation_nothing_in_the_server_ever_writes_isLightningActive()
        {
            // ⚠ THE ISLAND-DROP HAZARD.
            // IslandLocalTransformBehaviour.HandleLightningActiveUpdated(true) writes
            // the island's transform to GetEndOfWorldPosition() - doomsday code that
            // lerps the island's Y toward -250..-1500 m. The bool buys NOTHING: the
            // visualiser that renders a storm switches on
            // EstimatedMilliTillLightningEnd > 0, an INT.
            //
            // Three absences currently defuse it (our 1042 Options are empty; we
            // never grant island transform authority; and IslandLocalTransformBehaviour
            // is baked onto 0 of the 255 shipped island bundles). None of them is ours
            // to rely on. The rule costs nothing.
            DoesNotContain(Wire(), ".SetIsLightningActive(",
                "This can teleport an island into the depths, and it buys nothing.");
            DoesNotContain(Server(), ".SetIsLightningActive(",
                "This can teleport an island into the depths, and it buys nothing.");
        }

        [Fact]
        public void The_wire_never_sends_the_whole_component()
        {
            // 1254's ToUpdate() sets all SEVEN properties - including
            // isLightningActive. Sending the whole component would arm the hazard
            // above on every single push.
            DoesNotContain(Wire(), ".ToUpdate()",
                "Sending Data.ToUpdate() would re-assert isLightningActive on every push.");
        }

        [Fact]
        public void The_wire_never_routes_a_storm_through_the_player_relay()
        {
            // RelayToOtherPlayers substitutes the SENDER's entity id for the address,
            // so an island's timer would arrive addressed to whichever player happened
            // to be moving and no island would ever storm on anyone's screen.
            DoesNotContain(Wire(), "RelayToOtherPlayers(",
                "A relayed island timer is addressed to a player, not to the island.");
        }

        // ====================================================================
        // MUTATION: "no-op the reset call"
        // ====================================================================

        [Fact]
        public void Mutation_the_storm_calls_the_real_resource_reset()
        {
            Contains(Wire(), "ResetHarvestResourcesOn(",
                "The storm must reuse the reset that already exists and is already "
                + "tested, not a second implementation of it. A ResetIslandResources "
                + "that logs and returns is a storm that refreshes nothing.");
        }

        [Fact]
        public void The_reset_the_storm_calls_is_the_one_that_drives_all_four_ledgers()
        {
            // Guards against the reset body itself being hollowed out under the
            // storm's feet. Note the `include` argument: these are the SCOPED
            // overloads, and a call site that dropped it would silently go back to
            // resetting the whole world.
            string server = Server();
            Contains(server, "Harvest.ResetAll(include)", "trees");
            Contains(server, "Nodes.ResetAll(include)", "metal nodes");
            Contains(server, "MetalHarvest.ResetAll(include)", "metal deposits");
            Contains(server, "FuelCanisters.ResetAll(include)", "fuel canisters");
        }

        // ====================================================================
        // S2 MUTATION: "make the per-island reset global again"
        // ====================================================================

        [Fact]
        public void Mutation_the_storms_reset_is_scoped_to_the_island_that_stormed()
        {
            // ⚠ THE S2 DEFECT, AND IT IS INVISIBLE TO EVERY OTHER TEST IN THIS FILE.
            // A ResetIslandResources(islandId) that ignores its argument and calls
            // the world-wide ResetHarvestResources() ticks, logs, and reintroduces
            // the 3 m 32 s delay MEASURED on production on 2026-08-20 - while the
            // pure service tests next door stay green, because they only see that
            // SOMETHING was reset.
            string wire = Wire();

            Contains(wire, "public string ResetIslandResources(string islandId)",
                "The wire's reset must take an island.");
            Contains(wire, "new Multiplayer.Islands.IslandId(islandId)",
                "...and must actually pass it through, not drop it on the floor.");

            int body = wire.IndexOf("public string ResetIslandResources(", StringComparison.Ordinal);
            Assert.True(body > 0, "ResetIslandResources was not found in the wire");
            int end = wire.IndexOf("\n        }", body, StringComparison.Ordinal);
            Assert.True(end > body, "the end of ResetIslandResources was not found");

            Assert.DoesNotContain("ResetHarvestResources()", wire.Substring(body, end - body),
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_per_island_reset_resolves_ownership_from_the_interest_services_own_map()
        {
            // Two classifications that could disagree would mean a storm resetting
            // resources the player standing on the island does not hold. There is one
            // map, and the understorm reads it.
            string server = Server();
            Contains(server, "ResourceInterest.IslandOf(entityId)",
                "The understorm must ask the service that already classified every "
                + "streamed resource per island for checkout.");

            string interest = Source("WorldsAdriftRebornGameServer", "Game",
                "ResourceInterestService.cs");
            Contains(interest, "public IslandId? IslandOf(long entityId)",
                "The accessor over _resourceIslands is the S2 seam (§14.10).");
            Contains(interest, "public IReadOnlyDictionary<long, IslandId> ResourceIslands",
                "The map itself is exposed read-only for anything that needs the whole set.");
        }

        [Fact]
        public void The_per_island_reset_does_not_silently_do_nothing_when_interest_is_off()
        {
            // ⚠ THE FAIL-SILENT. ResourceInterestService only populates
            // _resourceIslands when spatial interest is on (production reads
            // WAREBORN_INTEREST_RADIUS_M=120, PROVED 2026-08-20 - but an operator can
            // unset it). With an empty map and no fallback, every storm would reset
            // exactly zero resources and every test here would still be green.
            string server = Server();
            Contains(server, "IslandResourceInterestPolicy.ClosestIsland(",
                "An unclassified resource must be re-derived from its position with "
                + "the same rule the interest map was built from.");
            Contains(server, "internal static Multiplayer.Islands.IslandId? IslandOwningResource(",
                "One seam answers 'which island owns this resource', for both paths.");
        }

        [Fact]
        public void The_operators_reset_resources_all_is_still_world_wide()
        {
            // S2 scopes the STORM's reset. It does not take the world-wide reset
            // away from the authenticated operator, who asked for exactly that.
            string server = Server();
            Contains(server, "internal static string ResetHarvestResources() => ResetHarvestResourcesIn(null);",
                "reset-resources all must still mean all.");

            string admin = Source("WorldsAdriftRebornGameServer", "Game",
                "AdminWorldCommandService.cs");
            Contains(admin, "WorldsAdriftRebornGameServer.ResetHarvestResources()",
                "The operator command keeps the global body.");
        }

        [Fact]
        public void The_boot_log_no_longer_promises_a_last_island_world_reset()
        {
            // The S1 log line described the defect accurately. Leaving it in place
            // after the fix would make a correct server describe itself as broken.
            DoesNotContain(Server(), "World resources reset when the last island's storm ends.",
                "S2 resets each island at its own storm end; the log must say so.");
        }

        // ====================================================================
        // Trees riding the storm
        // ====================================================================

        [Fact]
        public void Per_tree_regrowth_is_handed_over_to_the_storm()
        {
            string server = Server();
            Contains(server, "IslandStormPolicy.PerTreeRegrowthEnabled(",
                "With storms on, TreeHarvest's own doc says DueRespawns should stop "
                + "firing and the forest should come back with the lightning.");
            Contains(server, "if (!PerTreeRegrowth)",
                "The gate has to be in front of the DueRespawns loop, not merely computed.");
            Contains(server, "WAREBORN_TREE_RESPAWN_SECONDS",
                "The revert path must keep working.");
        }
    }
}
