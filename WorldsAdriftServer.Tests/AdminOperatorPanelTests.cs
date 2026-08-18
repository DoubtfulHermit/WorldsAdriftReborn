using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The operator command panel and the interest-and-streaming view, as the
    /// dashboard actually serves them.
    ///
    /// These assert against the COMPOSED page, in the same direction as
    /// <see cref="WebAssetCompositionTests"/>: the property that matters is not
    /// that a fragment file contains something, but that the bytes a browser
    /// receives do. A fragment dropped from the load order would leave every
    /// per-file assertion green while the console shipped without its panel.
    /// </summary>
    public class AdminOperatorPanelTests
    {
        private static string Dashboard() =>
            AdminPage.Dashboard("{}", new string('c', 64), ReleaseWorldMap.Json);

        [Fact]
        public void The_operator_panel_is_on_the_dashboard_with_its_controls()
        {
            string html = Dashboard();

            Assert.Contains("id=\"operator\"", html);
            Assert.Contains("id=\"opRoster\"", html);
            Assert.Contains("id=\"opTarget\"", html);
            Assert.Contains("id=\"opTargetCustom\"", html);
            Assert.Contains("id=\"opIslandSearch\"", html);
            Assert.Contains("id=\"opIsland\"", html);
            Assert.Contains("id=\"opDestPlayer\"", html);
            Assert.Contains("id=\"opHull\"", html);
            Assert.Contains("data-op-action=\"teleport\"", html);
            Assert.Contains("data-op-action=\"summon-ship\"", html);
            Assert.Contains("data-op-dest=\"island\"", html);
            Assert.Contains("data-op-dest=\"coord\"", html);
            Assert.Contains("data-op-dest=\"player\"", html);
            Assert.Contains("data-op-dest=\"home\"", html);
            Assert.Contains("data-op-dest=\"spawn\"", html);
            Assert.Contains("data-op-hull=\"owned\"", html);
            Assert.Contains("data-op-hull=\"exact\"", html);
        }

        [Fact]
        public void The_panel_calls_the_real_operator_endpoints_with_the_guarded_headers()
        {
            string html = Dashboard();

            Assert.Contains("/admin/api/operator/targets", html);
            Assert.Contains("'/admin/api/operator/teleport'", html);
            Assert.Contains("'/admin/api/operator/summon-ship'", html);
            // The same two request guards every admin write already carries.
            Assert.Contains("'X-Wareborn-Admin':'1'", html);
            Assert.Contains("'X-Wareborn-CSRF':CSRF", html);
        }

        [Fact]
        public void Targets_are_echoed_selectors_never_strings_the_page_invents()
        {
            // The contract that makes the GUI unable to construct an invalid
            // target: every roster-driven value the form posts back IS the
            // `selector` string the targets endpoint supplied. The one advance
            // past that is the clearly-labelled advanced box, which the SERVER
            // validates.
            string html = Dashboard();

            Assert.Contains("function(p){return p.selector;}", html);
            Assert.Contains("function(i){return i.selector;}", html);
            Assert.Contains("function(s){return s.selector;}", html);
        }

        [Fact]
        public void Sending_requires_an_explicit_review_and_confirm_step()
        {
            string html = Dashboard();

            Assert.Contains("id=\"opReview\"", html);
            Assert.Contains("id=\"opConfirm\"", html);
            Assert.Contains("id=\"opSend\"", html);
            Assert.Contains("id=\"opCancel\"", html);
            // Touching the form discards a pending confirmation rather than
            // silently confirming something else.
            Assert.Contains("if(opPending){opPending=null;renderOperatorReview();}", html);
            // What is reviewed is by construction what is sent: one builder
            // produces both the review payload and the request body.
            Assert.Contains("function buildOperatorRequest()", html);
            Assert.Contains("body:JSON.stringify(submitted.body)", html);
        }

        [Fact]
        public void Warnings_refusals_and_durability_are_surfaced_not_swallowed()
        {
            string html = Dashboard();

            // warnings[] from an acceptance are listed, each visibly a warning.
            Assert.Contains("response.warnings", html);
            // A refusal's reason sentence is shown verbatim next to its code.
            Assert.Contains("response.reason", html);
            Assert.Contains("'refused · '", html);
            // A uid-less row is labelled entity-only rather than passed off as
            // durable.
            Assert.Contains("selectorIsDurable", html);
            Assert.Contains("entity-only", html);
            // The one-at-a-time bridge is a named state, not a silent queue.
            Assert.Contains("id=\"opBridge\"", html);
            Assert.Contains("data.code==='busy'", html);
        }

        [Fact]
        public void The_streaming_view_is_on_the_dashboard_with_its_surfaces()
        {
            string html = Dashboard();

            Assert.Contains("id=\"streaming\"", html);
            Assert.Contains("id=\"intLoadBarrier\"", html);
            Assert.Contains("id=\"intSpawnPace\"", html);
            Assert.Contains("id=\"interestSystems\"", html);
            Assert.Contains("id=\"candidacyBanner\"", html);
            Assert.Contains("id=\"candidacyChips\"", html);
            Assert.Contains("id=\"peerInspect\"", html);
            Assert.Contains("id=\"peerHoldings\"", html);
            Assert.Contains("id=\"peerRingsToggle\"", html);
            // The load-barrier incident is actionable by name.
            Assert.Contains("WAREBORN_LOAD_BARRIER=1", html);
        }

        [Fact]
        public void Interest_rings_are_drawn_in_world_metres_on_the_shared_map()
        {
            string html = Dashboard();

            // The layers are created by the operator page's own script, so the
            // public map (which composes the same map-body.html) never carries
            // operator-only structure.
            Assert.Contains("mapInterestLayer", html);
            Assert.Contains("mapCandidacyLayer", html);
            Assert.DoesNotContain("mapInterestLayer", WebAssets.Read("map-body.html"));
            // True scale: circles carry world-metre radii read from telemetry.
            Assert.Contains("class':'map-interest-ring ", html);
            Assert.Contains("t.loadRadiusMetres", html);
            Assert.Contains("interest.resources.loadRadiusMetres", html);
        }

        [Fact]
        public void Absent_interest_telemetry_reads_as_not_reported_never_as_a_number()
        {
            string html = Dashboard();

            Assert.Contains("not reported by this game server (stats schema ", html);
            // The gate on rendering interest data is its explicit presence.
            Assert.Contains("i&&i.present===true", html);
        }

        [Fact]
        public void The_new_fragments_are_operator_only()
        {
            // The public page composes no admin-* fragment (pinned generally by
            // WebAssetCompositionTests); this pins that the two NEW fragments
            // follow that naming rule, so the existing guard covers them.
            Assert.Contains("admin-operator.js", AdminPage.AdminScriptFragments);
            Assert.Contains("admin-topology.js", AdminPage.AdminScriptFragments);
            foreach (string fragment in PublicMapPage.ScriptFragments)
            {
                Assert.NotEqual("admin-operator.js", fragment);
                Assert.NotEqual("admin-topology.js", fragment);
            }
        }
    }
}
