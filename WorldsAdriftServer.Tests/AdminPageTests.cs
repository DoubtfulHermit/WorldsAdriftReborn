using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Newtonsoft.Json.Linq;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public class AdminPageTests
    {
        [Fact]
        public void Dashboard_exposes_the_functional_sections_and_csrf_bound_operations()
        {
            string csrf = new string('a', 64);
            string html = AdminPage.Dashboard("{}", csrf);

            Assert.Contains("id=\"world\"", html);
            Assert.Contains("id=\"simulation\"", html);
            Assert.Contains("id=\"operations\"", html);
            Assert.Contains("data-command=\"resources-reset\"", html);
            Assert.Contains("data-command=\"ship-recall\"", html);
            Assert.Contains("data-command=\"ship-stop\"", html);
            Assert.Contains("data-command=\"helm-release\"", html);
            Assert.Contains("data-command=\"ship-delete\"", html);
            Assert.Contains("name=\"csrf\" value=\"" + csrf + "\"", html);
            Assert.Contains("'X-Wareborn-CSRF':CSRF", html);
            Assert.Contains("Latest game-server completion", html);
            Assert.Contains("Simulation fabric", html);
            Assert.Contains("id=\"topologyCanvas\"", html);
            Assert.Contains("id=\"domainInventory\"", html);
            Assert.Contains("id=\"runtimeHostTotal\"", html);
            Assert.Contains("id=\"domainDetail\"", html);
            Assert.Contains("data-domain-filter=\"issues\"", html);
            Assert.Contains("renderTopology", html);
            Assert.Contains("renderDomainInventory", html);
            Assert.Contains("related.slice(0,8)", html);
            Assert.Contains("rows.slice(0,250)", html);
            Assert.DoesNotContain("class=\"domain-grid\"", html);
            Assert.DoesNotContain("Worker A", html);
            Assert.DoesNotContain("migrate", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Dashboard_uses_one_responsive_console_system_and_real_recovery_controls()
        {
            string html = AdminPage.Dashboard("{}", new string('b', 64));

            Assert.Contains("class=\"selectors\"", html);
            Assert.Contains("class=\"recovery-actions\"", html);
            Assert.Contains("id=\"copyShipDiagnostics\"", html);
            Assert.Contains("id=\"selectedShipSummary\"", html);
            Assert.Contains("id=\"stopShip\"", html);
            Assert.Contains("id=\"releaseHelm\"", html);
            Assert.Contains("updateRecoveryActions", html);
            Assert.DoesNotContain("ship-nudge", html);
            Assert.DoesNotContain("Ship position trim", html);
            Assert.Contains("class=\"tool danger-zone\"", html);
            Assert.Contains("class=\"danger-button\"", html);
            Assert.Contains("class=\"receipt\"", html);
            Assert.Contains("@media(max-width:760px)", html);
            Assert.Contains("prefers-reduced-motion", html);
            Assert.Contains(".runtime-overview", html);
            Assert.Contains(".topology-canvas", html);
            Assert.Contains(".host-cluster", html);
            Assert.Contains(".host-domain-grid", html);
            Assert.Contains(".domain-workbench", html);
            Assert.Contains(".domain-detail", html);
            Assert.Contains("@media(max-width:980px)", html);
            Assert.Contains("button:focus-visible", html);
            Assert.Contains("--accent:#74c9cf", html);
            Assert.DoesNotContain("--timber", html);
            Assert.DoesNotContain("#eebd8e", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cdn", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Dashboard_has_a_scalable_terrain_checkout_view_with_the_semantic_states()
        {
            string html = AdminPage.Dashboard("{}", new string('c', 64));

            Assert.Contains("id=\"terrain\"", html);
            Assert.Contains("href=\"#terrain\"", html);
            // Dense tables and a matrix, not one decorative card per player.
            Assert.Contains("id=\"terrainMatrix\"", html);
            Assert.Contains("id=\"terrainPlayers\"", html);
            Assert.Contains("id=\"terrainIslands\"", html);
            Assert.Contains("id=\"terrainEvents\"", html);
            Assert.Contains("class=\"terrain-strip\"", html);
            Assert.DoesNotContain("terrain-card-per-player", html);

            // The exact operator vocabulary, matching IslandTerrainStatePolicy.
            Assert.Contains("'absent':'ABSENT'", html);
            Assert.Contains("'requesting':'REQUESTING'", html);
            Assert.Contains("'waiting-ack':'WAITING ACK'", html);
            Assert.Contains("'ready':'READY'", html);
            Assert.Contains("'draining':'DRAINING'", html);
            Assert.Contains("'unloading':'UNLOADING'", html);
            Assert.Contains("'retained-legacy':'RETAINED (LEGACY)'", html);
            Assert.Contains("'error':'ERROR'", html);

            // Truthful empty/off/stale/legacy states rather than a broken-looking page.
            Assert.Contains("predates terrain telemetry", html);
            Assert.Contains("prerequisite-disabled", html);
            Assert.Contains("Terrain checkout is off.", html);
            Assert.Contains("id=\"noTerrainPlayers\"", html);
            Assert.Contains("id=\"noTerrainIslands\"", html);
            Assert.Contains("id=\"noTerrainEvents\"", html);

            // Scale controls: search, bounded rendering, and a visible cap note.
            Assert.Contains("id=\"terrainSearch\"", html);
            Assert.Contains("rows.slice(0,200)", html);
            Assert.Contains("(t.events||[]).slice(0,40)", html);

            // Local authority only - no worker/migration claim.
            Assert.Contains("local:primary", html);
            Assert.Contains("does not move island authority", html);
            Assert.Contains("describes no remote worker", html);
            Assert.DoesNotContain("migrate", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("distributed", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Terrain_acceptance_panel_reuses_the_guarded_travel_commands_only()
        {
            string html = AdminPage.Dashboard("{}", new string('d', 64));

            Assert.Contains("id=\"acceptanceTravel\"", html);
            Assert.Contains("id=\"acceptanceReturn\"", html);
            Assert.Contains("id=\"acceptancePrereq\"", html);
            Assert.Contains("id=\"havenTravel\"", html);
            // The panel CLICKS the existing controls: one command path, one CSRF
            // header, one journal entry - no duplicated command or element id.
            Assert.Contains("$('mentalFacilityTravel').click()", html);
            Assert.Contains("$('havenTravel').click()", html);
            Assert.Equal(1, Occurrences(html, "data-argument=\"mental-facility\""));
            Assert.Equal(1, Occurrences(html, "data-argument=\"haven\""));
            Assert.Equal(1, Occurrences(html, "id=\"mentalFacilityTravel\""));
            Assert.Equal(1, Occurrences(html, "id=\"havenTravel\""));

            // Honest about what it does and does not prove.
            Assert.Contains("human judgement", html);
            Assert.Contains("Nothing on this page is evidence", html);

            // No new or unsafe operation is introduced by this view.
            Assert.DoesNotContain("force-unload", html);
            Assert.DoesNotContain("data-command=\"terrain", html);
            Assert.DoesNotContain("terrain-unload", html);
            Assert.DoesNotContain("arbitrary", html, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Terrain_view_keeps_the_console_accessible_and_responsive()
        {
            string html = AdminPage.Dashboard("{}", new string('e', 64));

            Assert.Contains("aria-label=\"Filter terrain checkout rows\"", html);
            Assert.Contains("aria-label=\"Recent terrain lifecycle events\"", html);
            Assert.Contains("aria-expanded", html);
            Assert.Contains("tr.tabIndex=0", html);
            Assert.Contains("e.key==='Enter'||e.key===' '", html);
            Assert.Contains(".terrain-table tbody tr:focus-visible", html);
            Assert.Contains(".terrain-strip{grid-template-columns:1fr 1fr}", html);
            Assert.Contains("prefers-reduced-motion", html);
            Assert.Contains("overflow:auto", html);
        }

        [Fact]
        public void Existing_world_simulation_and_operations_views_are_not_regressed()
        {
            string html = AdminPage.Dashboard("{}", new string('f', 64));

            Assert.Contains("id=\"playersTable\"", html);
            Assert.Contains("id=\"topologyCanvas\"", html);
            Assert.Contains("id=\"domainInventory\"", html);
            Assert.Contains("id=\"commandLog\"", html);
            Assert.Contains("id=\"targetShip\"", html);
            Assert.Contains("id=\"targetPlayer\"", html);
            Assert.Contains("data-command=\"placement\"", html);
            Assert.Contains("data-command=\"resources-reset\"", html);
            Assert.Contains("data-command=\"ship-delete\"", html);
            Assert.Contains("'X-Wareborn-Admin':'1'", html);
            Assert.Contains("/admin/api/command", html);
            // Exactly the allowlisted command surface, unchanged.
            Assert.Equal(9, Occurrences(html, "data-command="));
        }

        [Fact]
        public void Dashboard_has_the_release_geography_and_live_position_layers()
        {
            string map = ReleaseWorldMap.Json;
            string html = AdminPage.Dashboard("{}", new string('a', 64), map);

            Assert.Contains("\"worldEdgeLength\":36000", map);
            Assert.Contains("\"havenSeparatorX\":15943.6523", map);
            Assert.Equal(266, Occurrences(map, "\"asset\":"));
            Assert.Equal(12, Occurrences(map, "\"haven\":true"));
            Assert.Equal(20, Occurrences(map, "\"district\":"));
            Assert.Equal(18, Occurrences(map, "\"authoredDistrict\":true"));
            Assert.Equal(2, Occurrences(map, "\"authoredDistrict\":false"));
            Assert.Equal(44, Occurrences(map, "\"x1\":"));
            Assert.Contains("id=\"liveWorldMap\"", html);
            Assert.Contains("id=\"mapBiomeLayer\"", html);
            Assert.Contains("id=\"mapHavenLayer\"", html);
            Assert.Contains("id=\"mapWallLayer\"", html);
            Assert.Contains("id=\"mapIslandLayer\"", html);
            Assert.Contains("id=\"mapShipLayer\"", html);
            Assert.Contains("id=\"mapPlayerLayer\"", html);
            Assert.Contains("renderLiveWorldMap", html);
            Assert.Contains("Player entity ", html);
            Assert.Contains("Drag to pan", html);
            Assert.Contains("Wind Rift", html);
            Assert.Contains("World End", html);
            Assert.Contains("T1 Wilderness", html);
            Assert.Contains("T2 Expanse", html);
            Assert.Contains("T3 Remnants", html);
            Assert.Contains("T4 Badlands", html);
            Assert.Contains("E3 is one cell", html);
            Assert.Contains("NO DISTRICT", html);
            Assert.Contains("no name inferred", html);
            Assert.Contains("Haven is inside", html);
            Assert.Contains("12 preserved starter-island placements", html);
            Assert.Contains("biomeCell", html);
            Assert.Contains("getScreenCTM().inverse()", html);
            Assert.DoesNotContain("stroke-width:32", html);
            Assert.DoesNotContain("stroke-width:52", html);
            Assert.Contains("preserved-release-mapfile", html);
        }

        [Fact]
        public void Release_map_preserves_all_tier_cells_without_inventing_missing_districts()
        {
            JObject map = JObject.Parse(ReleaseWorldMap.Json);
            List<JObject> cells = ((JArray)map["biomes"]!).OfType<JObject>().ToList();

            Assert.Equal(20, cells.Count);
            Assert.Equal(4, cells.Count(cell => (int?)cell["type"] == 1));
            Assert.Equal(4, cells.Count(cell => (int?)cell["type"] == 2));
            Assert.Equal(6, cells.Count(cell => (int?)cell["type"] == 3));
            Assert.Equal(6, cells.Count(cell => (int?)cell["type"] == 4));
            Assert.Equal(new[] { "A2", "A3", "B2", "B3" }, DistrictsForTier(cells, 1));
            Assert.Equal(new[] { "A1", "A4", "B1", "B4" }, DistrictsForTier(cells, 2));
            Assert.Equal(new[] { "C1", "C2", "C3", "C4", "C5", "C6" }, DistrictsForTier(cells, 3));
            Assert.Equal(new[] { "D1", "D2", "D3", "E3" }, DistrictsForTier(cells, 4));

            Assert.Equal(18, cells.Count(cell => (bool?)cell["authoredDistrict"] == true));
            List<JObject> unassigned = cells
                .Where(cell => (bool?)cell["authoredDistrict"] == false).ToList();
            Assert.Equal(2, unassigned.Count);
            Assert.All(unassigned, cell =>
            {
                Assert.Null(cell["district"]!.Value<string?>());
                Assert.Equal(4, (int?)cell["type"]);
            });

            JObject e3 = Assert.Single(cells,
                cell => string.Equals((string?)cell["district"], "E3", StringComparison.Ordinal));
            Assert.Equal(4, (int?)e3["type"]);
        }

        private static string[] DistrictsForTier(IEnumerable<JObject> cells, int tier)
        {
            return cells.Where(cell => (int?)cell["type"] == tier)
                .Select(cell => (string?)cell["district"])
                .Where(district => district != null)
                .Select(district => district!)
                .OrderBy(district => district, StringComparer.Ordinal)
                .ToArray();
        }

        private static int Occurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }
            return count;
        }
    }
}
