using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// IS THE SHADOW OBSERVER ACTUALLY PLUGGED IN - AND STILL INERT?
    ///
    /// <para>
    /// The tests next door prove what the shadow model MEANS. None of them can prove
    /// the poll loop ever calls it, that it is gated on the documented flag, or that
    /// the reader has not quietly grown a mutating call. The game-server assembly has
    /// no test project of its own (it needs a Windows game install to compile
    /// against), so - exactly as <c>IslandStormWiringTests</c> and
    /// <c>ComponentSeedOutcomeWiringTests</c> already do - the connection is asserted
    /// by reading the production source off disk.
    /// </para>
    ///
    /// <para>
    /// Coarse on purpose. String matching is a weak guard and it is the LAST line
    /// here, not the first: the flag itself is tested in SimulationObserverPolicyTests
    /// and the inertness of a disabled runtime is tested structurally in
    /// SimulationShadowRuntimeTests. This file only guards the two things that are
    /// physically unreachable from a test: the call site, and what the reader touches.
    /// </para>
    /// </summary>
    public class SimulationObserverWiringTests
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

        private static string Server() => File.ReadAllText(Path.Combine(
            RepoRoot(), "WorldsAdriftRebornGameServer", "WorldsAdriftRebornGameServer.cs"));

        [Fact]
        public void The_poll_loop_calls_the_observer()
        {
            string source = Server();
            Assert.Contains("MaybeObserveSimulation();", source);
            Assert.Contains("Simulation.Poll(ServerClock.Elapsed)", source);
        }

        [Fact]
        public void The_observer_is_built_behind_the_documented_flag()
        {
            string source = Server();
            Assert.Contains("SimulationObserverPolicy.IsEnabled(", source);
            Assert.Contains("SimulationObserverPolicy.EnabledEnvVar", source);
            // The flag must be read through the policy, never re-spelled at the call
            // site, or the strictness tested next door stops applying.
            Assert.DoesNotContain("\"WAREBORN_SIMULATION_MODEL\"", source);
        }

        [Fact]
        public void The_shadow_section_reaches_the_stats_file()
        {
            Assert.Contains("simulation: new SimulationRuntimeStat(", Server());
        }

        [Fact]
        public void The_world_reader_touches_nothing_that_mutates()
        {
            string reader = ReaderBody();
            // The mutating neighbours of the accessors this reader uses. Each of
            // these sits one method away from something it does call.
            foreach (string forbidden in new[]
                     {
                         "Aboard.Observe", "Aboard.Forget", "DomainHost.Assign",
                         "DomainHost.Move", "DomainHost.Unassign", "DomainHost.MarkGlobal",
                         "DomainHost.Register", "DomainHost.RemoveDomain",
                         "DomainHost.Synchronize", "WorldEntities.Relocate",
                         "ResourceInterest.Tick", "Flight.Tick", "Relay.",
                         "new ENetPeerHandle(", "Send", "Checkout", "Enqueue",
                     })
            {
                Assert.False(reader.Contains(forbidden, StringComparison.Ordinal),
                    "the shadow observer's world reader calls " + forbidden
                    + ", which is not an observation");
            }
        }

        [Fact]
        public void The_world_reader_reads_the_seams_it_is_supposed_to()
        {
            // The floor for the test above, which would otherwise pass on an empty
            // method. These are the four observable sources of the first edges.
            string reader = ReaderBody();
            Assert.Contains("DomainHost.Domains", reader);
            Assert.Contains("ShipDomains.All", reader);
            Assert.Contains("Aboard.AboardShip(", reader);
            Assert.Contains("Flight.PilotEntityOf(", reader);
            Assert.Contains("ResourceInterest.HoldingsFor(", reader);
            Assert.Contains("Players.All()", reader);
        }

        /// <summary>The body of ObserveWorldForShadowModel, and nothing else.</summary>
        private static string ReaderBody()
        {
            string source = Server();
            const string signature = "ObserveWorldForShadowModel()";
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            // The first hit is the method-group reference in the field initialiser;
            // the declaration is the one followed by a body.
            while (start >= 0 && source.IndexOf('{', start) > source.IndexOf(';', start))
                start = source.IndexOf(signature, start + signature.Length, StringComparison.Ordinal);
            Assert.True(start >= 0, "ObserveWorldForShadowModel is gone from the game server");

            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(open, i - open + 1);
            }
            throw new InvalidOperationException("unbalanced braces reading the observer body");
        }
    }
}
