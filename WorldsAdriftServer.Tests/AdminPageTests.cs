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
            // The guard's intent is "no AUTHORITY migration claim" - this server
            // never moves an island between workers. The bare word had to be
            // narrowed when the fauna gained its (recovered-vocabulary) Migrate
            // behaviour: a school crossing between feeding grounds is wildlife,
            // not topology.
            Assert.DoesNotContain("authority migrat", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("migrates authority", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("worker migrat", html, StringComparison.OrdinalIgnoreCase);
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
        public void Observatory_links_world_simulation_and_infrastructure_without_inventing_workers()
        {
            string html = AdminPage.Dashboard("{}", new string('o', 64), ReleaseWorldMap.Json);

            Assert.Contains("role=\"tablist\" aria-label=\"Observatory view\"", html);
            Assert.Contains("id=\"modeWorld\" data-observatory-mode-button=\"world\"", html);
            Assert.Contains("id=\"modeSimulation\" data-observatory-mode-button=\"simulation\"", html);
            Assert.Contains("id=\"modeInfrastructure\" data-observatory-mode-button=\"infrastructure\"", html);
            Assert.Contains("id=\"observatoryWorld\" data-observatory-panel=\"world\"", html);
            Assert.Equal(2, Occurrences(html, "data-observatory-panel=\"simulation\""));
            Assert.Contains("id=\"observatoryInfrastructure\" data-observatory-panel=\"infrastructure\"", html);
            Assert.Contains(".mode-panel[hidden]{display:none!important}", html);

            Assert.Contains("id=\"observatorySelection\"", html);
            Assert.Contains("function updateSharedSelection", html);
            Assert.Contains("selectRuntimeDomain(d.domainId,false)", html);
            Assert.Contains("selectRuntimeDomain(d.domainId,false)", html);

            Assert.Contains("id=\"infraHostId\">local:primary", html);
            Assert.Contains("id=\"infraCpu\">not reported", html);
            Assert.Contains("id=\"infraMemory\">not reported", html);
            Assert.Contains("id=\"infraThreads\">not reported", html);
            Assert.Contains("none configured or reported", html);
            Assert.DoesNotContain("compute score", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Worker A", html);
        }

        [Fact]
        public void Observatory_keeps_the_shadow_observer_and_accepts_a_bounded_event_ring()
        {
            string html = AdminPage.Dashboard("{}", new string('t', 64));

            Assert.Contains("Interaction shadow model", html);
            Assert.Contains("observer off", html);
            Assert.Contains("warming", html);
            Assert.Contains("uncalibrated", html);
            Assert.Contains("id=\"worldInspectorTimeline\"", html);
            Assert.Contains("var inspector=g&&g.worldInspector", html);
            Assert.Contains("events.slice(0,40)", html);
            Assert.Contains("Not reported by this game server schema.", html);
            Assert.Contains("ArrowRight", html);
            Assert.Contains("ArrowLeft", html);
            Assert.Contains("@media(max-width:760px)", html);
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

            // Local authority only - no worker/migration claim. Narrowed from
            // the bare word when fauna gained its Migrate behaviour (schools
            // crossing between feeding grounds - wildlife, not topology).
            Assert.Contains("local:primary", html);
            Assert.Contains("does not move island authority", html);
            Assert.Contains("describes no remote worker", html);
            Assert.DoesNotContain("authority migrat", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("migrates authority", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("worker migrat", html, StringComparison.OrdinalIgnoreCase);
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
            Assert.Contains("id=\"mapShellLayer\"", html);
            Assert.Contains("id=\"mapDetail\"", html);
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
            Assert.Contains("UNASSIGNED", html);
            Assert.Contains("Haven is inside", html);
            Assert.Contains("12 preserved starter-island placements", html);
            Assert.Contains("biomeCell", html);
            Assert.Contains("getScreenCTM().inverse()", html);
            Assert.DoesNotContain("stroke-width:32", html);
            Assert.DoesNotContain("stroke-width:52", html);
            Assert.Contains("preserved-release-mapfile", html);
        }

        [Fact]
        public void Map_and_terrain_views_are_labelled_with_their_own_provenance()
        {
            string html = AdminPage.Dashboard("{}", new string('g', 64), ReleaseWorldMap.Json);

            // The SVG cartography is preserved map evidence, not live simulation.
            Assert.Contains("Preserved release-world map", html);
            Assert.DoesNotContain("Live release-world map", html);
            Assert.Contains("map evidence", html);
            Assert.Contains("static embedded projection of the preserved Bossa release MapFile", html);
            Assert.Contains("historical map evidence, not live simulation state", html);
            Assert.Contains("None of this geometry is read from the running game server", html);
            Assert.Contains("Static map evidence: release MapFile", html);

            // Only the overlay is live, and its cadence is stated where it is described.
            Assert.Contains("Only the ship and player markers, and the ring drawn around each"
                + " simulated island domain, are live", html);
            // The console reads faster than the game server writes, on purpose:
            // a reader slower than its source is guaranteed to miss generations.
            Assert.Contains("every 1.5 seconds", html);
            Assert.Contains("roughly 3-second stats snapshots", html);
            Assert.Contains("var REFRESH_MS = 1500;", html);

            // The terrain view is signed as the authoritative live set.
            Assert.Contains("provenance-tag live", html);
            Assert.Contains("live simulation state", html);
            Assert.Contains("Island inventory &middot; islands this game server is simulating", html);
            Assert.Contains("authoritative live set of islands the running game server is"
                + " actually simulating", html);

            // The one visual distinction that is exactly derivable is legended:
            // the live ring over a simulated island domain's reported position.
            Assert.Contains(".map-swatch.runtime", html);
            Assert.Contains("Currently simulated island domain (live)", html);
            Assert.Contains("Every other mark is preserved map evidence", html);
            Assert.Contains("currently simulated island domain, resident on this host", html);
        }

        [Fact]
        public void The_map_surface_carries_no_statistics_and_detail_lives_in_the_panel()
        {
            string html = AdminPage.Dashboard("{}", new string('k', 64), ReleaseWorldMap.Json);

            // WHAT THIS TEST IS FOR. A previous iteration stamped an abbreviated
            // resource roll-up on every tier cell - "11 isl / 55 db / 72 dep / 60
            // tr / *8" - because the ask was read as "make the inventory visible"
            // rather than "make it reachable". It made the world unreadable. The
            // map is a map again; nothing but zone identity is drawn on it.
            Assert.DoesNotContain("cellRollupShort", html);
            Assert.DoesNotContain("' isl · '", html);
            Assert.DoesNotContain("' db · '", html);
            Assert.DoesNotContain("' dep · '", html);
            Assert.DoesNotContain("+' tr'", html);
            Assert.DoesNotContain("class=\"stock\"", html);
            Assert.DoesNotContain("'class':'stock'", html);
            Assert.DoesNotContain("id=\"mapResources\"", html);
            // A cell draws exactly two lines: its district and its tier name.
            Assert.Contains("districtLine.textContent=hasDistrict?b.district:'UNASSIGNED'", html);
            Assert.Contains("tierLine.textContent='T'+b.type+' · '+info.name", html);

            // Detail is reachable by CLICKING, and the click targets are real.
            Assert.Contains("function selectIsland(node)", html);
            Assert.Contains("function selectZone(z)", html);
            Assert.Contains("function selectWorld()", html);
            Assert.Contains("function renderMapDetail()", html);
            Assert.Contains("function detailIsland(panel,scroll,node)", html);
            Assert.Contains("function detailZone(panel,scroll,z)", html);
            Assert.Contains("id=\"mapDetail\"", html);

            // THE BUG THAT MADE THE FEATURE LOOK ABSENT. Capturing the pointer on
            // pointerdown retargets the compatibility click too, so every island
            // click was delivered to the SVG and silently reset the panel. The
            // capture must happen only once a drag has actually started.
            Assert.DoesNotContain("mapDragged=false;svg.setPointerCapture(e.pointerId)", html);
            Assert.Contains("if(!mapDragged){", html);
            Assert.Contains("svg.setPointerCapture(e.pointerId);drag.captured=true;", html);

            // Discoverability: hover affordance, cursor, hit target, hint.
            Assert.Contains(".map-marker{cursor:pointer}", html);
            Assert.Contains("group.classList.add('hot')", html);
            Assert.Contains("'class':'mk-hit'", html);
            Assert.Contains("click an island for its full inventory", html);

            // Progressive disclosure instead of crammed text: zoom classes, real
            // coastlines and island names appear as you zoom in.
            Assert.Contains("svg.classList.toggle('zoom-near'", html);
            Assert.Contains("svg.zoom-near .map-shell-layer{opacity:1", html);
            Assert.Contains("function shellPath(i)", html);
            Assert.Contains("svg.zoom-near .map-marker .map-island-name{opacity:1}", html);

            // The words are spelled out in the panel.
            Assert.Contains("'Databanks'", html);
            Assert.Contains("'Metal deposits'", html);
            Assert.Contains("'Trees'", html);

            // LIVE WILDLIFE. It is its own layer with its own toggle, it is drawn
            // BENEATH the ship and player overlays so scenery never covers an
            // operator's actual subject, and it is never on the drawing surface as
            // text.
            Assert.Contains("id=\"mapFaunaLayer\"", html);
            Assert.Contains("id=\"mapFauna\" checked>wildlife", html);
            Assert.Contains("id=\"mantaSymbol\"", html);
            Assert.Contains("id=\"jellySymbol\"", html);
            Assert.True(html.IndexOf("id=\"mapFaunaLayer\"", StringComparison.Ordinal)
                        < html.IndexOf("id=\"mapShipLayer\"", StringComparison.Ordinal),
                "wildlife must be drawn under the live ship and player markers");
            Assert.Contains("function faunaMotion(M)", html);
            Assert.Contains("function noteFauna(g)", html);
            Assert.Contains("function renderFaunaFrame()", html);
            // Nothing is drawn without a roster AND a clock from the game server.
            Assert.Contains("f.present===true&&f.enabled===true", html);
            // The panel answers what lives on an island, in words - the counts
            // and the geometry, presented as data with no caption about where
            // the project got them.
            Assert.Contains("function appendIslandFauna(scroll,i,inv)", html);
            Assert.Contains("'Creatures'", html);
            Assert.DoesNotContain("Wareborn tuning, not Bossa data. ", html);

            // The ledger survives as the all-islands view, driven by ONE search.
            Assert.Contains("id=\"ledgerBody\"", html);
            Assert.Contains("function renderIslandLedger()", html);
            Assert.Contains("id=\"ledgerFilter\"", html);
            Assert.Contains("function applyMapFilter()", html);
        }

        [Fact]
        public void Island_counts_are_reconciled_from_the_live_stats_rather_than_hardcoded()
        {
            string html = AdminPage.Dashboard("{}", new string('h', 64), ReleaseWorldMap.Json);

            Assert.Contains("id=\"mapReconcile\"", html);
            Assert.Contains("id=\"terrainReconcile\"", html);
            Assert.Contains("islands on the preserved release map", html);
            Assert.Contains("currently simulated", html);

            // One helper feeds BOTH labels, so the two panels cannot disagree.
            Assert.Equal(1, Occurrences(html, "function islandReconciliationText()"));
            Assert.Contains("text('mapReconcile',line);text('terrainReconcile',line);", html);

            // The live half is read from the same terrain section the checkout
            // view renders; the map half from the embedded projection. Neither
            // number is a literal in the page.
            Assert.Contains("count:(t.islands||[]).length", html);
            Assert.Contains("var mapCount=(worldMap.islands||[]).length;", html);
            Assert.DoesNotContain("3 currently simulated", html);
            Assert.DoesNotContain("266 islands on the preserved release map", html);
            Assert.DoesNotContain("266 islands /", html);
        }

        [Fact]
        public void Reconciled_counts_state_a_condition_instead_of_fabricating_a_number()
        {
            string html = AdminPage.Dashboard("{}", new string('i', 64), ReleaseWorldMap.Json);

            Assert.Contains("currently simulated count unavailable: ", html);
            Assert.Contains("'the game server is not reporting'", html);
            Assert.Contains("predates terrain telemetry (stats schema ", html);
            Assert.Contains("its last stats snapshot is ", html);
            Assert.Contains("preserved release map not loaded", html);

            // A degraded census carries a condition and NO count, so a missing,
            // stale or older-schema snapshot can never render as a real zero.
            Assert.Contains("{known:false,condition:", html);
            Assert.Contains("return {known:true,count:(t.islands||[]).length};", html);
            Assert.Contains("census.known", html);
            Assert.DoesNotContain("{known:false,count:", html);
        }

        [Fact]
        public void Provenance_labelling_leaves_the_zone_signage_rules_untouched()
        {
            string html = AdminPage.Dashboard("{}", new string('j', 64), ReleaseWorldMap.Json);

            // The unassigned Tier-4 cells stay unassigned, and Holy Ruins keeps
            // its two conflicting preserved facts. This is about the DATA, not
            // about captions: the panel no longer annotates where a number came
            // from, but it still must not invent a district that does not exist.
            Assert.Contains("E3 is one cell", html);
            Assert.Contains("UNASSIGNED", html);
            Assert.Contains("two Tier-4 Badlands cells are explicitly unassigned", html);
            Assert.Contains("not silently invented as E1/E2 or merged into E3", html);
            // No unassigned cell acquires a district, under any spelling.
            Assert.DoesNotContain("\"district\":\"E1\"", html);
            Assert.DoesNotContain("\"district\":\"E2\"", html);
            Assert.DoesNotContain("District E1", html);
            Assert.DoesNotContain("District E2", html);
            // Holy Ruins keeps its two conflicting preserved facts. The island is
            // named now that the map carries per-island inventory, so the rule is
            // no longer "never mention it" but the stronger one: BOTH tiers are
            // published side by side and neither is quietly dropped to make the
            // other consistent.
            JObject map = JObject.Parse(ReleaseWorldMap.Json);
            JObject holyRuins = ((JArray)map["islands"]!).OfType<JObject>()
                .Single(island => (string?)island["inventory"]?["name"] == "Holy Ruins");
            Assert.Equal("A4", (string?)holyRuins["inventory"]!["cell"]);
            Assert.Equal(2, (int?)holyRuins["inventory"]!["cellTier"]);
            Assert.Equal(3, (int?)holyRuins["inventory"]!["surveyTier"]);
            Assert.NotEqual((int?)holyRuins["inventory"]!["cellTier"],
                            (int?)holyRuins["inventory"]!["surveyTier"]);
            Assert.Contains("surveyTier", html);
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

        [Fact]
        public void World_map_legend_and_cells_cannot_disagree_about_tier_colour()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64));

            foreach (MapTierColours tier in MapTierPalette.All)
            {
                // The cell is drawn TRANSLUCENT, so the legend key is not the CSS
                // hex - it is the composite of that hex, that opacity and the ocean
                // rule, all three of which are emitted here from MapTierPalette.
                // The old bug was exactly this gap: raw hex in the key, 38%
                // composite on the map.
                Assert.Contains(
                    $".map-biome.type-{tier.Tier}{{fill:{tier.Hue};fill-opacity:{MapTierPalette.FillOpacityCss}}}",
                    html);
                Assert.Contains($".map-swatch.tier-{tier.Tier}{{background:{tier.Fill}}}", html);
                Assert.Contains($"map-swatch tier tier-{tier.Tier}", html);
                Assert.Contains($"T{tier.Tier} ", html);
                Assert.Equal(tier.Fill, MapTierPalette.Composite(
                    tier.Hue, MapTierPalette.Ocean, MapTierPalette.FillOpacity));
            }

            // The backdrop the composite assumes is the backdrop the page paints,
            // emitted from the same file rather than hand-written in the stylesheet.
            Assert.Contains($".map-ocean{{fill:{MapTierPalette.Ocean}}}", html);
            Assert.DoesNotContain(".map-ocean{fill:#09151d}.map-world-boundary", html);

            // The transparency is on the FILL, not on the layer. A layer opacity
            // would also dim the cell stroke and the label drawn on it, and neither
            // of those was measured against a dimmed version of itself.
            Assert.DoesNotContain(".map-biome{stroke:#233a45;stroke-width:1;vector-effect:non-scaling-stroke;opacity:", html);
            Assert.DoesNotContain(".map-biome.type-1{fill:#4b934f;opacity:", html);

            // Wall strokes and their legend keys come off one list too, and the
            // retired Storm Rift violet - which the lilac Remnants fill swallowed -
            // appears nowhere.
            foreach (MapWallColours wall in MapWallPalette.All)
            {
                Assert.Contains($".map-wall.type-{wall.Type}{{stroke:{wall.Colour};", html);
                Assert.Contains($".map-swatch.wall-{wall.Type}{{background:{wall.Colour}}}", html);
                Assert.Contains($"map-swatch wall-{wall.Type}", html);
            }
            Assert.DoesNotContain("#9b86d8", html);

            // Tier is never encoded by colour alone: the cell carries its tier as text.
            Assert.Contains("tierLine.textContent='T'+b.type", html);
            Assert.Contains("'class':'map-cell-label type-'+b.type", html);
        }

        [Fact]
        public void Every_release_island_carries_its_seeded_inventory_onto_the_map()
        {
            JObject map = JObject.Parse(ReleaseWorldMap.Json);
            List<JObject> islands = ((JArray)map["islands"]!).OfType<JObject>().ToList();

            // 266 MapFile placements: 254 ordinary islands plus 12 Haven reserve
            // placements, which are hand-tuned and carry no surveyed inventory.
            Assert.Equal(266, islands.Count);
            Assert.Equal(254, islands.Count(island => island["inventory"] != null));
            Assert.Equal(12, islands.Count(island => (bool?)island["haven"] == true));
            Assert.All(islands.Where(island => (bool?)island["haven"] == true),
                island => Assert.Null(island["inventory"]));

            // World totals, so the page never re-derives a count by hand.
            JObject totals = (JObject)map["resourceTotals"]!;
            Assert.Equal(1930, (int?)totals["deposits"]);
            Assert.Equal(1233, (int?)totals["databanks"]);
            Assert.Equal(13266, (int?)totals["trees"]);
            Assert.Equal(193, (int?)totals["islandsWithInferredOres"]);

            // Per island the deposits are broken down by ore, and the rows account
            // for every deposit - an ore breakdown that does not add up would be a
            // fabricated one.
            foreach (JObject island in islands.Where(island => island["inventory"] != null))
            {
                JObject inventory = (JObject)island["inventory"]!;
                int deposits = (int?)inventory["deposits"] ?? -1;
                Assert.Equal(deposits, ((JArray)inventory["ores"]!).Sum(ore => (int?)ore["deposits"] ?? 0));
                Assert.NotNull((string?)inventory["name"]);
                Assert.Contains((string?)inventory["oreSource"],
                    new[] { "survey-pve", "survey-pvp", "inferred-tier" });
                Assert.Equal((string?)inventory["oreSource"] == "inferred-tier",
                    (bool?)inventory["oresInferred"]);
                // Wood is labelled on exactly the same terms as ore. 180 islands
                // grow an inference; the page must never present one as a survey.
                Assert.Contains((string?)inventory["woodSource"],
                    new[] { "survey", "survey-none", "inferred-tier" });
                Assert.Equal((string?)inventory["woodSource"] == "inferred-tier",
                    (bool?)inventory["woodsInferred"]);
                // Not recovered, so stated as zero rather than guessed at.
                Assert.Equal(0, (int?)inventory["fuelPods"]);
                // Loot containers USED to be pinned at zero here for the same
                // reason. They are real now: unlike fuel-pod placements, retail's
                // loot PLACEMENT ALGORITHM survived in the shipped client
                // (LootablePerAreaDataVisualizer budgets by flat surface area,
                // IslandDataBankAndLootableSpawnerVisualizer seats them 20 m
                // apart), so these are that procedure over this island's own
                // measured surface rather than an invented number. Only the budget
                // constants are this project's - see LootBudget.
                //
                // Bounded rather than exact, because the exact per-island count is
                // ReleaseLootCatalogTests' business and pinning it twice would mean
                // two places to update for one tuning change.
                Assert.InRange((int?)inventory["lootContainers"] ?? -1,
                    0, WorldsAdriftRebornGameServer.Multiplayer.Loot.LootBudget.MaxContainers);
            }
        }

        [Fact]
        public void Ships_are_drawn_as_their_own_hulls_under_their_own_layer_and_toggle()
        {
            string html = AdminPage.Dashboard("{}", new string('s', 64), ReleaseWorldMap.Json);

            // The hull outlines are their own layer, and it sits UNDER the
            // constant-size marks and under the player marks - a hull is the
            // biggest live thing on this map and must not bury the two smallest.
            Assert.Contains("id=\"mapShipHullLayer\"", html);
            Assert.True(html.IndexOf("id=\"mapShipHullLayer\"", StringComparison.Ordinal)
                        < html.IndexOf("id=\"mapShipLayer\"", StringComparison.Ordinal),
                "the true-scale hulls must be drawn under the constant-size ship marks");
            Assert.True(html.IndexOf("id=\"mapShipLayer\"", StringComparison.Ordinal)
                        < html.IndexOf("id=\"mapPlayerLayer\"", StringComparison.Ordinal),
                "players must stay on top of ships");

            // A hull is drawn from the published ring, oriented by the published
            // heading. Neither is optional: a ring with no rotation is a ship
            // pointing north forever.
            Assert.Contains("function shipHullPath(outline)", html);
            Assert.Contains("h.present===true&&path", html);
            Assert.Contains("' rotate('+deg.toFixed(1)+')'", html);
            Assert.Contains("deg=p.yaw*180/Math.PI", html);

            // Progressive disclosure is per SHIP, not per zoom level, because a
            // big hull earns its outline sooner than a small one at the same zoom.
            Assert.Contains("var shown=(n.keelMetres/mapPx)>=SHIP_HULL_MIN_PX;", html);
            Assert.Contains("var SHIP_HULL_MIN_PX=14;", html);
            Assert.Contains("hull-shown", html);

            // A ship must not read as an island: cold blue, mostly hollow, a
            // hairline that does not thicken, against the islands' opaque slab.
            Assert.Contains(".map-ship-hull{fill:#8aa6ff", html);
            Assert.Contains("vector-effect:non-scaling-stroke", html);
            Assert.Contains(".map-ship-hull.reckoned{stroke-dasharray:", html);
            Assert.Contains(".map-ship-hull.held{", html);

            // Detail lives in the panel, reachable by clicking, and the drag
            // guard is the same one the islands use.
            Assert.Contains("function detailShip(panel,scroll,hullEntityId)", html);
            Assert.Contains("function selectShip(hullEntityId)", html);
            Assert.Contains("if(!mapDragged)selectShip(d.hullEntityId)", html);
            Assert.Contains("mapSelection.kind==='ship'", html);

            // Toggleable, and the toggle takes the hulls with the marks.
            Assert.Contains("id=\"mapShips\" checked", html);
            Assert.Contains("$('mapShipHullLayer').style.display=$('mapShips').checked", html);

            // The honesty channel, in words, in the legend and in the panel.
            Assert.Contains("id=\"mapShipNote\"", html);
            Assert.Contains("function shipNoteText()", html);
            Assert.Contains("dead reckoning", html);
            Assert.Contains("Could be out by at most", html);
            Assert.Contains("The mark has STOPPED", html);
            Assert.Contains("This hull is AT REST", html);
        }

        [Fact]
        public void The_panel_presents_the_worlds_data_without_annotating_where_it_came_from()
        {
            string html = AdminPage.Dashboard("{}", new string('k', 64), ReleaseWorldMap.Json);

            // WHAT THIS TEST IS FOR, AND WHY IT IS INVERTED FROM WHAT IT WAS.
            // This console used to caption every composed number: amber INFERRED
            // badges on ore rows, "Inferred, not recovered" preambles, a
            // "Provenance: RECOVERED - read on the PvP shard" line, an "inferred
            // ore only" filter, a "Wareborn tuning, not Bossa data" flag on the
            // wildlife. The operator's call was to stop: this is a recreation of
            // a world, and someone reading the panel to find out where the iron
            // is does not need an argument about how the project came by the
            // answer. The data is unchanged; only the annotation is gone, and
            // this test exists so it cannot creep back one caption at a time.
            //
            // THE RECORD ITSELF IS NOT GONE, and must not be. The catalogue still
            // carries oresInferred and oreSource per island, the source comments
            // still say which numbers were composed, and docs/research still has
            // the derivation. That is how WE avoid fooling ourselves; it is not a
            // caption for the screen.
            foreach (string caption in new[]
            {
                "Inferred, not recovered.",
                "composed from the surveyed same-tier cohort",
                "plausible, not Bossa data",
                "row.className='is-inferred'",
                "tr.is-inferred td.ore:after",
                "(inv.oresInferred?'INFERRED: ':'')",
                "Partly inferred.",
                "Fully recovered.",
                "RECOVERED - read on the PvP shard",
                "one ruleset removed from the PvE world",
                "ORE_SOURCE_LABEL",
                "Ore types on 193 of the 254 islands",
                "Inferred ore tables",
                "ledgerInferredOnly",
                "inferred ore only",
                "Wareborn tuning, not Bossa data",
                "How much of this is Bossa data",
                "never invented",
                "How MANY there are is Wareborn tuning",
            })
            {
                Assert.DoesNotContain(caption, html);
            }

            // The DATA is all still there, and is what an operator came for.
            Assert.Contains("'Metal deposits by ore'", html);
            Assert.Contains("function oreTable(ores)", html);
            Assert.Contains("function oreSummary(inv)", html);
            Assert.Contains("'Quality '+o.quality", html);
            Assert.Contains("Metal deposits by ore across this zone", html);

            // An ABSENCE is a fact about the world, so it is still reported as a
            // number rather than omitted - it just no longer argues its case.
            Assert.Contains("Fuel pods: 0", html);
            Assert.Contains("Loot containers: 0", html);

            // And the per-island record survives in the projection the page is
            // built from, which is the half that must never be dropped.
            JObject map = JObject.Parse(ReleaseWorldMap.Json);
            JObject inventory = ((JArray)map["islands"]!).OfType<JObject>()
                .First(island => island["inventory"] != null)["inventory"]!.ToObject<JObject>()!;
            Assert.NotNull(inventory["oresInferred"]);
            Assert.NotNull(inventory["oreSource"]);
        }

        [Fact]
        public void Every_drawn_tier_cell_can_be_joined_to_the_islands_inside_it()
        {
            // The cell roll-up is only possible because the projection names each
            // cell the same way the runtime catalogue does, including Bossa's two
            // null districts.
            JObject map = JObject.Parse(ReleaseWorldMap.Json);
            var cellIds = ((JArray)map["biomes"]!).OfType<JObject>()
                .Select(cell => (string?)cell["cellId"]).ToList();

            Assert.Equal(20, cellIds.Count);
            Assert.Equal(20, cellIds.Distinct().Count());
            Assert.Contains("unassigned-t4-1", cellIds);
            Assert.Contains("unassigned-t4-2", cellIds);

            var islandCells = ((JArray)map["islands"]!).OfType<JObject>()
                .Where(island => island["inventory"] != null)
                .Select(island => (string?)island["inventory"]!["cell"])
                .Distinct().ToList();
            Assert.All(islandCells, cell => Assert.Contains(cell, cellIds));
        }

        [Fact]
        public void World_map_no_longer_ships_the_colour_blind_hostile_palette()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64));

            foreach (string retired in new[] { "#93c47d", "#6d9eeb", "#8e7cc3", "#f6b26b" })
                Assert.DoesNotContain(retired, html);

            // Two walls have now been moved off a tier they were disappearing into.
            // The sand-storm wall sat dE00 8.5 from the old tier-4 swatch; the storm
            // rift sat 8.2 from the lilac tier-3 that replaced the old violet. Both
            // are single-sourced from MapWallPalette now, key and stroke together.
            // (#d9b36b survives as the console's --warn token, which is not a map
            // colour; what must be gone is its use as a wall stroke or a map key.)
            Assert.DoesNotContain("stroke:#d9b36b", html);
            Assert.DoesNotContain("background:#d9b36b", html);
            Assert.DoesNotContain("#9b86d8", html);
            Assert.Contains(".map-wall.type-3{stroke:#e8963c;", html);
            Assert.Contains(".map-swatch.wall-3{background:#e8963c}", html);
            Assert.Contains(".map-wall.type-1{stroke:#c04ae8;", html);
            Assert.Contains(".map-swatch.wall-1{background:#c04ae8}", html);
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
