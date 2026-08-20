using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Walls
{
    /// <summary>
    /// ARE THE WEATHER WALLS ACTUALLY PLUGGED IN?
    ///
    /// The tests next door prove what a wall MEANS - the half-length, the direction,
    /// the flag, the seed order. Not one of them can prove that
    /// <c>ComponentsSerializer</c> has a 1204 branch at all, that the spawn plan is
    /// handed the flag, or that 8065 still says "Player" to everything else. That gap
    /// is the exact one this repo has shipped a green suite over twice.
    ///
    /// The game-server assembly has no test project of its own (it needs a Windows
    /// game install to compile against), so the connection is asserted the way
    /// <c>IslandStormWiringTests</c> already does it: by reading the production source
    /// off disk. Coarse on purpose. It cannot prove the walls are RIGHT; it proves
    /// they are CONNECTED, and it goes red the moment somebody unplugs one.
    /// </summary>
    public class WallSegmentWiringTests
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
            "WorldsAdriftRebornGameServer", "Game", "WallSegmentWire.cs");

        private static string Serializer() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs");

        /// <summary>
        /// Runs of whitespace collapsed to one space, so a source assertion pins the
        /// CODE rather than the line wrapping a reformat might change.
        /// </summary>
        private static string Collapsed(string source) =>
            System.Text.RegularExpressions.Regex.Replace(source, @"[ \t\r\n]+", " ");

        /// <summary>
        /// The source with every comment stripped, so a "must never appear" assertion
        /// is about CODE and not about a comment explaining why the code must never
        /// appear. The first version of the 1229 test failed on the paragraph in
        /// WallSegmentWire's own doc comment warning people off 1229 - which is the
        /// documentation working and the test misreading it. A guard that punishes
        /// writing down the reason is a guard that gets the reason deleted.
        /// </summary>
        private static string CodeOnly(string source)
        {
            string noBlocks = System.Text.RegularExpressions.Regex.Replace(
                source, @"/\*.*?\*/", " ",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            return System.Text.RegularExpressions.Regex.Replace(
                noBlocks, @"//.*?$", " ",
                System.Text.RegularExpressions.RegexOptions.Multiline);
        }

        private static void Contains(string haystack, string needle, string why) =>
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);

        private static void DoesNotContain(string haystack, string needle, string why) =>
            Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
                "Did NOT expect to find `" + needle + "`. " + why);

        // ====================================================================
        // MUTATION: "delete the 1204 branch"
        // ====================================================================

        [Fact]
        public void Mutation_the_serializer_actually_serves_1204()
        {
            string source = Collapsed(Serializer());

            Contains(source, "componentId == 1204",
                "Without a 1204 branch every wall is an entity the client renders "
                + "nothing for: WallSegmentVisualizer's single [Require] never "
                + "resolves, so it never enables, so WeatherWalls never registers the "
                + "wall - and a Unity visualiser that fails to enable prints NOTHING.");

            Contains(source, "new WallSegmentState.Data(new WallSegmentStateData(",
                "The branch must build the real gencode component, not a placeholder.");

            Contains(source, "WallSegmentWire.SeedFor(entityId)",
                "The geometry must come from the catalogue via the wire, so 'which wall "
                + "is this entity' stays one testable lookup rather than an id guess.");
        }

        [Fact]
        public void Mutation_the_1204_branch_sends_the_HALF_length_from_the_seed()
        {
            string source = Collapsed(Serializer());

            Contains(source, "seed.HalfLength",
                "WallData does P1 = position - forward*Length and P2 = position + "
                + "forward*Length, so `length` is a HALF-length. Passing a full length "
                + "here doubles every wall in the world and looks correct from "
                + "everywhere except its ends.");

            Contains(source, "seed.WallTypeId",
                "wallType is the wire int the client casts straight to "
                + "WorldEditorWallData.WallType.");
            Contains(source, "seed.WallId",
                "wallId is the key WeatherWalls groups segments by.");
        }

        // ====================================================================
        // MUTATION: "widen 8065 carelessly"
        // ====================================================================

        [Fact]
        public void Mutation_8065_asks_the_policy_rather_than_hardcoding_a_new_literal()
        {
            string source = Collapsed(Serializer());

            Contains(source, "WallSegmentWire.BlueprintNameFor(entityId)",
                "8065 is read by EVERY entity in this world. The wall widening must go "
                + "through one unit-tested function, or the next edit to that literal "
                + "changes what all of them receive with nothing to catch it.");

            DoesNotContain(source, "new BlueprintData(\"WallSegment\")",
                "A second hardcoded literal beside the first is exactly the shape of "
                + "the bug this indirection exists to prevent.");
        }

        // ====================================================================
        // MUTATION: "unplug the spawn plan"
        // ====================================================================

        [Fact]
        public void Mutation_the_spawn_plan_is_actually_handed_the_wall_flag()
        {
            string source = Collapsed(Server());

            Contains(source, "WeatherWallsEnabled, WeatherWallTypes)",
                "WorldEntities.Default must receive the flag, or WAREBORN_WALLS=1 "
                + "registers nothing and the feature is green and invisible.");

            Contains(source, "Multiplayer.Walls.WallPolicy.EnabledFromEnvironment()",
                "The flag must be read from the environment, not left at a constant.");
        }

        [Fact]
        public void Mutation_the_flag_is_declared_BEFORE_the_registry_that_reads_it()
        {
            // C# initialises static fields in DECLARATION ORDER. Declared below
            // WorldEntities, WeatherWallsEnabled would still be `false` when the
            // registry is built and WAREBORN_WALLS would read as permanently off no
            // matter what the operator set - a feature flag that silently does
            // nothing, and a green suite over it. The same trap the Storms service
            // documents.
            string source = Server();
            int flag = source.IndexOf("internal static readonly bool WeatherWallsEnabled",
                StringComparison.Ordinal);
            int types = source.IndexOf("internal static readonly string? WeatherWallTypes",
                StringComparison.Ordinal);
            int registry = source.IndexOf("internal static readonly WorldEntityRegistry WorldEntities",
                StringComparison.Ordinal);

            Assert.True(flag > 0, "WeatherWallsEnabled must exist.");
            Assert.True(types > 0, "WeatherWallTypes must exist.");
            Assert.True(registry > 0, "The WorldEntities registry must exist.");
            Assert.True(flag < registry,
                "WeatherWallsEnabled must be DECLARED BEFORE the WorldEntities registry "
                + "that reads it; static initialisers run in declaration order, so "
                + "below it the flag is always false.");
            Assert.True(types < registry,
                "WeatherWallTypes must be DECLARED BEFORE the WorldEntities registry "
                + "that reads it, for the same reason.");
        }

        [Fact]
        public void Mutation_the_boot_banner_still_says_whether_walls_are_on()
        {
            // A feature that is off should SAY it is off with the name of the variable
            // that turns it on. A silent boot is how an unplugged feature survives.
            Contains(Collapsed(Server()), "Multiplayer.Walls.WorldWalls.Describe(",
                "The boot banner must print the wall state - including the storm-rift "
                + "kilometrage, which is the ambient-bolt spawn-rate input and the one "
                + "cost here that was derived rather than measured.");
        }

        // ====================================================================
        // MUTATION: "serve 1229 too, while we're here"
        // ====================================================================

        [Fact]
        public void Mutation_nobody_serves_1229_GlobalWallDataState()
        {
            DoesNotContain(CodeOnly(Wire()), "1229",
                "1229 carries wind/gust/torque scalars as a Map<string,float>. Retail's "
                + "50 values are UNRECOVERABLE, the client Debug.LogErrors once per "
                + "missing key, and a missing TORQUE key makes it SILENTLY skip that "
                + "wall type's whole table. Half-populating it is worse than not "
                + "serving it - and it would buy nothing anyway, because the behaviours "
                + "that read it are UnityWorker-side and are not on our hulls.");

            DoesNotContain(Collapsed(CodeOnly(Serializer())), "componentId == 1229",
                "Same, on the other side of the seam.");

            DoesNotContain(CodeOnly(Serializer()), "GlobalWallDataState",
                "Same, by name - the gencode type must not be constructed anywhere.");
        }

        // ====================================================================
        // MUTATION: "write isLightningActive"
        // ====================================================================

        [Fact]
        public void Mutation_the_wall_wire_never_touches_the_island_drop_flag()
        {
            // IslandLocalTransformBehaviour answers a rising isLightningActive by
            // lerping the island's Y toward -250..-1500 m. This file has no business
            // near it, and "it is a lightning feature" is exactly the reasoning that
            // would put it here one day.
            DoesNotContain(CodeOnly(Wire()), "isLightningActive",
                "Writing it teleports islands out of the world.");
        }

        // ====================================================================
        // MUTATION: "push wall updates every tick"
        // ====================================================================

        [Fact]
        public void Mutation_the_wall_wire_sends_nothing_at_all()
        {
            // A wall is static geometry told to the client once at checkout. There is
            // no push loop because there is nothing to push, and a 190602 re-send to a
            // live entity is a documented hazard elsewhere in this server. If somebody
            // adds a sender here, this goes red and they have to say why.
            DoesNotContain(CodeOnly(Wire()), "SendOPHelper",
                "The wall wire is a lookup, not a sender.");
            DoesNotContain(CodeOnly(Wire()), "RelayToOtherPlayers",
                "That method substitutes the SENDER's entity id for the address.");
        }
    }
}
