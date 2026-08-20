using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The shadow model reaching the World Inspector.
    ///
    /// The projection tests next door prove the section survives the wire. Not one
    /// of them can prove the served page renders it, that the renderer is actually
    /// fed, or that the panel still says out loud which half of the card is
    /// authoritative and which half is an observation. That last point is the one
    /// worth a test: the whole discipline of this panel is that live simulation
    /// state is labelled distinctly from static map evidence, and a shadow overlay
    /// silently borrowing the authority card's voice would be a regression nobody
    /// would notice until an operator acted on it.
    /// </summary>
    public class SimulationInspectorPageTests
    {
        private static string Page() => AdminPage.Dashboard("{}", new string('f', 64));

        [Fact]
        public void The_shadow_model_block_is_served()
        {
            string html = Page();
            Assert.Contains("id=\"shadowModel\"", html);
            Assert.Contains("id=\"simulationState\"", html);
            Assert.Contains("id=\"simulationInteractions\"", html);
            foreach (string id in new[]
                     {
                         "simDomainTotal", "simEntityTotal", "simInteractionTotal",
                         "simActiveTotal", "simPressureTotal", "simulationIdentity",
                         "simulationSummary", "simulationResultCount", "simulationCadence",
                     })
            {
                Assert.Contains("id=\"" + id + "\"", html);
            }
        }

        [Fact]
        public void The_authoritative_topology_is_still_served_beside_it()
        {
            // The overlay must be an ADDITION. If it ever displaces the ownership
            // topology, the panel stops showing what the server actually owns.
            string html = Page();
            Assert.Contains("id=\"topologyCanvas\"", html);
            Assert.Contains("id=\"domainInventory\"", html);
            Assert.Contains("id=\"hostMode\"", html);
        }

        [Fact]
        public void The_page_says_the_overlay_is_an_observation_and_the_score_is_uncalibrated()
        {
            string html = Page();
            Assert.Contains("observation overlay", html);
            Assert.Contains("uncalibrated", html);
            // The panel must not present the overlay as ownership.
            Assert.Contains("what the game loop actually owns", html);
        }

        [Fact]
        public void The_renderer_is_wired_to_the_poll()
        {
            string html = Page();
            Assert.Contains("latestSimulation=g.simulation||null;", html);
            Assert.Contains("renderSimulationShadow();", html);
            Assert.Contains("function renderSimulationShadow()", html);
            // Joined to the ownership rows on the shared domain id.
            Assert.Contains("function shadowDomainFor(", html);
        }

        [Fact]
        public void The_overlay_never_reaches_the_public_map()
        {
            // Same rule as the operator command panel and the loot layer: the
            // public page does not receive the code, so there is nothing to hide.
            string html = PublicMapPage.Html("{}", "{}");
            Assert.DoesNotContain("renderSimulationShadow", html);
            Assert.DoesNotContain("shadowModel", html);
        }
    }
}
