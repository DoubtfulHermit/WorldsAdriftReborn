using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// THE DRIFT GUARD, for the whale.
    ///
    /// Both maps draw the animal moving by evaluating the game server's own
    /// closed-form circuit in the browser rather than being sent a position, and
    /// that is only honest while the two evaluators agree. "They agree because I
    /// wrote them carefully" is exactly the promise that rots, so this suite cuts
    /// the marked mirror out of the REAL served page, runs it in a JavaScript
    /// engine against the REAL published circuits, and asserts it returns the
    /// metres <see cref="SkyWhaleCircuit"/> returns.
    ///
    /// WHAT IS CLAIMED, precisely, because the whale's claim is narrower than the
    /// fauna's. The map's islands are placed by the preserved MapFile; the game
    /// server places them from its own runtime catalogue, and the two are allowed
    /// to differ. So this does NOT claim the browser puts the whale on the same
    /// world coordinate the server does. It claims that GIVEN THE SAME ROUTE both
    /// evaluate the same curve to a nanometre - the motion is ONE function, and
    /// only the geometry fed to it has two sources. Everything about the route that
    /// is a DECISION rather than a placement - the order of the zones, the order of
    /// the islands inside them, where the crossings are cut, the lap time, the
    /// phase - is computed once by the server's own code and published, never
    /// re-derived in the browser, which is what makes that narrower claim
    /// sufficient.
    ///
    /// THE MIGRATION DID NOT WIDEN THE MIRROR, and this suite is the evidence: the
    /// browser's arithmetic is unchanged from the four-whale design, because
    /// zone-to-zone travel is expressed as more control points on the same closed
    /// spline rather than as an event a second evaluator would have to re-implement.
    /// What changed is only WHICH points are published, which this suite checks
    /// against the server's own plan.
    /// </summary>
    public class AdminSkyWhaleParityTests
    {
        private const string MirrorBegin = "// ==== SKY WHALE MOTION MIRROR BEGIN ====";
        private const string MirrorEnd = "// ==== SKY WHALE MOTION MIRROR END ====";

        /// <summary>
        /// Nanometres. Both evaluators run the same terms in the same order over
        /// the same doubles, so anything a reordered expression could produce is
        /// orders of magnitude above this.
        /// </summary>
        private const double ExactTolerance = 1e-9;

        /// <summary>
        /// Timestamps chosen to land in the places a happy-path sample sails past:
        /// the start of a lap, either side of a waypoint knot, either side of the
        /// wrap, a whole day in, and a month of uptime - the sample that catches a
        /// rounded circuit period, because an error in a divisor of elapsed seconds
        /// is multiplied by how long the server has been running.
        /// </summary>
        private static readonly double[] Moments =
        {
            0.0, 1.0, 37.5, 119.0, 120.0, 121.0,
            600.25, 900.0, 1_199.5, 1_200.7,
            3_600.25, 43_200.0, 86_400.0, 604_800.0, 2_592_000.0,
        };

        [NodeFact]
        public void The_browser_mirror_flies_the_same_circuit_as_the_server()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);
            JArray routes = (JArray)EmbeddedWorldMap(html)["whaleRoutes"]!;
            Assert.NotEmpty(routes);

            List<JObject> samples = new List<JObject>();
            List<(SkyWhaleWaypoint[] Ring, double CircuitSeconds, double Phase, double T, string Where)>
                expected = new List<(SkyWhaleWaypoint[], double, double, double, string)>();

            // EVERY published route, not a sample of them: they are the same curve
            // over different control points, and the cheap one to get wrong is
            // whichever one nobody looked at.
            foreach (JObject circuit in routes.OfType<JObject>())
            {
    string regionId = (string)circuit["routeId"]!;
                double circuitSeconds = (double)circuit["circuitSeconds"]!;
                double phase = (double)circuit["phaseFraction"]!;
                (JArray ringJson, SkyWhaleWaypoint[] ring) = RingOf(circuit);

                foreach (double t in Moments)
                {
                    samples.Add(new JObject
                    {
                        ["ring"] = ringJson,
                        ["circuitSeconds"] = circuitSeconds,
                        ["phaseFraction"] = phase,
                        ["t"] = t,
                    });
                    expected.Add((ring, circuitSeconds, phase, t,
                        regionId + " at t=" + t.ToString(CultureInfo.InvariantCulture)));
                }

                // AND EVERY KNOT, either side - all eighty-odd of them now, island
                // and crossing alike. A Catmull-Rom implementation that got its
                // segment indexing or its wrap-around wrong agrees everywhere except
                // exactly here, which is where a whale would visibly jump on one map
                // and not the other. The knots at either end of a CROSSING are the
                // interesting new ones: they are where a zone hand-off happens, and
                // a hand-off that was not C1 would show up here first.
                for (int i = 0; i < ring.Length; i++)
                {
                    foreach (double offset in new[] { -1e-6, 0.0, 1e-6 })
                    {
                        double lap = ((double)i / ring.Length) + offset;
                        // Turn the lap fraction back into the time that produces
                        // it, so the mirror exercises its OWN lapAt as well.
                        double t = (lap - phase) * circuitSeconds;
                        samples.Add(new JObject
                        {
                            ["ring"] = ringJson,
                            ["circuitSeconds"] = circuitSeconds,
                            ["phaseFraction"] = phase,
                            ["t"] = t,
                        });
                        expected.Add((ring, circuitSeconds, phase, t,
                            regionId + " knot " + i + " offset "
                            + offset.ToString(CultureInfo.InvariantCulture)));
                    }
                }
            }

            JArray actual = Evaluate(mirror, samples);
            Assert.Equal(samples.Count, actual.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                (SkyWhaleWaypoint[] ring, double circuitSeconds, double phase, double t,
                    string where) = expected[i];
                double lap = SkyWhaleCircuit.Fraction((t / circuitSeconds) + phase);
                (double x, double y, double z) = SkyWhaleCircuit.EvaluatePosition(ring, lap);
                JArray got = (JArray)actual[i];

                Assert.True(Math.Abs(x - (double)got[0]!) <= ExactTolerance,
                    where + ": X was " + got[0] + ", the server circuit says " + x);
                Assert.True(Math.Abs(y - (double)got[1]!) <= ExactTolerance,
                    where + ": Y was " + got[1] + ", the server circuit says " + y);
                Assert.True(Math.Abs(z - (double)got[2]!) <= ExactTolerance,
                    where + ": Z was " + got[2] + ", the server circuit says " + z);

                // The HEADING too, because the animal carries one forward-swim clip
                // and nothing else: a map that drew it pointing the wrong way would
                // be drawing something the game cannot show.
                (double tx, double ty, double tz) = SkyWhaleCircuit.EvaluateTangent(ring, lap);
                Assert.True(Math.Abs(tx - (double)got[3]!) <= ExactTolerance,
                    where + ": tangent X was " + got[3] + ", the server circuit says " + tx);
                Assert.True(Math.Abs(ty - (double)got[4]!) <= ExactTolerance,
                    where + ": tangent Y was " + got[4] + ", the server circuit says " + ty);
                Assert.True(Math.Abs(tz - (double)got[5]!) <= ExactTolerance,
                    where + ": tangent Z was " + got[5] + ", the server circuit says " + tz);
            }
        }

        /// <summary>
        /// The mirror must be CUT OUT of the served page, and the numbers it reads
        /// must be numbers the projection actually publishes. A renamed field would
        /// otherwise reach the browser as <c>undefined</c>, and <c>undefined</c> in
        /// this arithmetic is NaN - an animal that silently stops being drawn.
        /// </summary>
        [Fact]
        public void The_served_page_carries_the_marked_whale_mirror_and_the_circuits_it_needs()
        {
            string html = AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);
            string mirror = ExtractMirror(html);

            Assert.Contains("function whaleMotion()", mirror);
            Assert.Contains("positionAt", mirror);
            Assert.Contains("tangentAt", mirror);
            Assert.Contains("lapAt", mirror);

            JObject map = EmbeddedWorldMap(html);
            JObject model = (JObject)map["whaleModel"]!;
            foreach (string field in new[]
            {
                "metresPerSecond", "altitudeAboveIslandMetres", "callIntervalSeconds",
                "loadRadiusMetres", "unloadRadiusMetres", "callRadiusMetres",
                "poseIntervalSeconds", "minimumIslands", "perPeerWhales",
            })
            {
                Assert.True(model[field] != null,
                    "the projection does not publish the whale constant '" + field + "'");
            }

            // The constants must be the policy's own, not a second literal table.
            SkyWhaleMapConstants c = SkyWhaleMapModel.Constants;
            Assert.Equal(c.MetresPerSecond, (double)model["metresPerSecond"]!);
            Assert.Equal(c.CallIntervalSeconds, (double)model["callIntervalSeconds"]!);
            Assert.Equal(c.LoadRadiusMetres, (double)model["loadRadiusMetres"]!);
            Assert.Equal(c.CallRadiusMetres, (double)model["callRadiusMetres"]!);
        }

        /// <summary>
        /// The world's ONE route must be published, in TRAVEL ORDER, with its lap
        /// time untrimmed - and with each waypoint's zone and its island/crossing
        /// flag, which is what lets the map draw the migration as a migration. A
        /// waypoint silently dropped is a whale that flies one path in the game and
        /// another on the map.
        /// </summary>
        [Fact]
        public void The_rollouts_route_is_published_exactly_as_the_server_computes_it()
        {
            // THE ONE THE PRODUCTION SERVER FLIES. The route is a function of which
            // cells were rolled out, so the map publishes one per cell set a rollout
            // can name in a word and the live whale joins by id; a map that carried
            // only the full-catalogue route would draw a tier-1 server's whale on a
            // 9-hour, 20-cell loop it is not on.
            JObject map = EmbeddedWorldMap(
                AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json));
            JArray routes = (JArray)map["whaleRoutes"]!;

            SkyWhalePlacement placement =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("tier1"))!.Value;
            JObject published = routes.OfType<JObject>().Single(
                row => (string?)row["routeId"] == placement.Whale.RouteId);

            Assert.Equal(placement.Circuit.IslandCount, (int)published["islandCount"]!);
            List<string> zones = placement.Circuit.Regions
                .Select(region => region.Value).ToList();
            Assert.Equal(zones, ((JArray)published["regionIds"]!)
                .Select(token => (string?)token));

            // Exactly, not nearly: the circuit period DIVIDES elapsed seconds,
            // so its error is multiplied by how long the server has been up -
            // the same rule mantaLapSeconds is held to, and for the same reason.
            Assert.Equal(placement.Circuit.CircuitSeconds, (double)published["circuitSeconds"]!);
            Assert.Equal(placement.Circuit.PhaseFraction, (double)published["phaseFraction"]!);

            JArray waypoints = (JArray)published["waypoints"]!;
            Assert.Equal(placement.Circuit.Waypoints.Count, waypoints.Count);
            for (int w = 0; w < waypoints.Count; w++)
            {
                SkyWhaleWaypoint expected = placement.Circuit.Waypoints[w];
                JObject row = (JObject)waypoints[w];
                // TRAVEL ORDER, waypoint by waypoint. The browser uses this order
                // verbatim rather than re-sorting, so if it were wrong here the
                // map would fly a different migration than the game.
                Assert.Equal(expected.IslandId.Value, (string?)row["islandId"]);
                Assert.Equal(zones.IndexOf(expected.Region.Value), (int)row["z"]!);
                // The crossing flag is INK, not arithmetic - the map dashes those
                // legs - but a route that lost it would draw the whole migration as
                // one indistinguishable scribble.
                Assert.Equal(expected.IsTransit, row["t"] != null);
            }

            // And the route really does span every cell, which is the whole change.
            Assert.True(placement.Circuit.Regions.Count > 1,
                "a single whale that never leaves one zone is not a migration");
            Assert.Contains(placement.Circuit.Waypoints, waypoint => waypoint.IsTransit);
        }

        /// <summary>
        /// DIFFERENT ROLLOUTS MUST BE DIFFERENT ROUTES WITH DIFFERENT NAMES, and
        /// this is the test that stands in front of the failure the plural exists
        /// for: a four-cell world and a twenty-cell world produce different orders,
        /// different lap times and different phases, and a shared name would let a
        /// map draw one while the game flew the other. It cannot be caught by any
        /// amount of parity arithmetic, because both sides would be evaluating the
        /// same function correctly on the wrong control points.
        /// </summary>
        [Fact]
        public void A_rollouts_route_is_named_after_its_cells_and_never_collides_with_anothers()
        {
            SkyWhalePlacement tier1 =
                SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("tier1"))!.Value;
            SkyWhalePlacement all = SkyWhalePlan.Build(ReleaseWorldCatalog.All)!.Value;

            Assert.NotEqual(tier1.Whale.RouteId, all.Whale.RouteId);
            Assert.NotEqual(tier1.Circuit.CircuitSeconds, all.Circuit.CircuitSeconds);
            Assert.Equal("release-route-a2-a3-b2-b3", tier1.Whale.RouteId);

            JArray routes = (JArray)EmbeddedWorldMap(
                AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json))["whaleRoutes"]!;
            string[] ids = routes.OfType<JObject>()
                .Select(row => (string)row["routeId"]!).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Contains(tier1.Whale.RouteId, ids);
            Assert.Contains(all.Whale.RouteId, ids);
        }

        /// <summary>
        /// The published local offsets, plus the runtime catalogue's own island
        /// origins, must reconstruct the server's world ring to a centimetre - the
        /// rounding the projection applies and nothing more.
        /// </summary>
        [Fact]
        public void The_published_offsets_reconstruct_the_servers_own_waypoints()
        {
            JObject map = EmbeddedWorldMap(
                AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json));
            SkyWhalePlacement plan = SkyWhalePlan.Build(ReleaseWorldCatalog.All)!.Value;
            JObject published = ((JArray)map["whaleRoutes"]!).OfType<JObject>()
                .Single(row => (string?)row["routeId"] == plan.Whale.RouteId);

            JArray waypoints = (JArray)published["waypoints"]!;
            for (int w = 0; w < waypoints.Count; w++)
            {
                SkyWhaleWaypoint expected = plan.Circuit.Waypoints[w];
                JObject row = (JObject)waypoints[w];
                // For a CROSSING point the island named here is its ANCHOR - the
                // nearer of the two islands its leg runs between - and the same
                // reconstruction has to hold for it, or a crossing would drift away
                // from the rocks either side of it on a map that places them
                // slightly differently.
                FixedPointPosition origin = ReleaseWorldCatalog
                    .ByIsland(expected.IslandId)!.Definition.GlobalOrigin;

                Assert.True(Math.Abs(
                    (origin.MetresX + (double)row["lx"]!) - expected.X) <= 0.01);
                Assert.True(Math.Abs(
                    (origin.MetresY + (double)row["ly"]!) - expected.Y) <= 0.01);
                Assert.True(Math.Abs(
                    (origin.MetresZ + (double)row["lz"]!) - expected.Z) <= 0.01);
            }
        }

        /// <summary>
        /// One published circuit's ring in the shape the BROWSER builds it: each
        /// waypoint's local offset added to the island's origin. The C# side gets
        /// the identical numbers, so any difference the test reports is a
        /// difference in the CURVE rather than in the geometry.
        /// </summary>
        private static (JArray Json, SkyWhaleWaypoint[] Ring) RingOf(JObject circuit)
        {
            JArray json = new JArray();
            List<SkyWhaleWaypoint> ring = new List<SkyWhaleWaypoint>();
            foreach (JObject waypoint in ((JArray)circuit["waypoints"]!).OfType<JObject>())
            {
                IslandId islandId = new IslandId((string)waypoint["islandId"]!);
                FixedPointPosition origin =
                    ReleaseWorldCatalog.ByIsland(islandId)!.Definition.GlobalOrigin;
                double x = origin.MetresX + (double)waypoint["lx"]!;
                double y = origin.MetresY + (double)waypoint["ly"]!;
                double z = origin.MetresZ + (double)waypoint["lz"]!;
                json.Add(new JObject { ["x"] = x, ["y"] = y, ["z"] = z });
                ring.Add(new SkyWhaleWaypoint(islandId, x, y, z));
            }
            return (json, ring.ToArray());
        }

        /// <summary>
        /// EVERY island the route names must be an island the map actually DRAWS,
        /// and this test is load-bearing in a way it was not before.
        ///
        /// The browser builds the drawn route by looking each waypoint's island up
        /// in the map's own island index and adding the local offset to the MapFile
        /// placement. A waypoint whose island is not on the map makes the ring
        /// unbuildable, and the browser then draws NOTHING - because a partial route
        /// would be a different migration, which is worse than no whale.
        ///
        /// With four region rings that failure was contained: one region's whale
        /// vanished and three still flew. With ONE world route it is all or nothing,
        /// so a single island that the map cannot resolve takes the entire feature
        /// off both maps. That is exactly the kind of silent, total loss a test has
        /// to stand in front of.
        /// </summary>
        [Fact]
        public void Every_island_on_the_route_is_an_island_the_map_can_actually_draw()
        {
            JObject map = EmbeddedWorldMap(
                AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json));

            HashSet<string> drawable = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject island in ((JArray)map["islands"]!).OfType<JObject>())
            {
                // The browser's index is keyed on the inventory's island id and is
                // only populated for islands that carry a fauna block - the same two
                // conditions map-render.js applies.
                if (island["fauna"] == null || island["inventory"] == null) continue;
                string? id = (string?)island["inventory"]!["islandId"];
                if (!string.IsNullOrEmpty(id)) drawable.Add(id!);
            }

            foreach (JObject route in ((JArray)map["whaleRoutes"]!).OfType<JObject>())
            {
                foreach (JObject waypoint in ((JArray)route["waypoints"]!).OfType<JObject>())
                {
                    string id = (string)waypoint["islandId"]!;
                    Assert.True(drawable.Contains(id),
                        "the whale route '" + route["routeId"] + "' passes over '" + id
                        + "', which the map does not draw - so the browser cannot build "
                        + "the route and BOTH maps would silently stop drawing the whale "
                        + "entirely");
                }
            }
        }

        private static string ExtractMirror(string html)
        {
            int begin = html.IndexOf(MirrorBegin, StringComparison.Ordinal);
            int end = html.IndexOf(MirrorEnd, StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin,
                "the served admin page no longer carries the marked sky whale mirror, so "
                + "nothing is pinning the browser's arithmetic to the server's");
            return html.Substring(begin + MirrorBegin.Length, end - begin - MirrorBegin.Length);
        }

        private static JObject EmbeddedWorldMap(string html)
        {
            const string open = "<script id=\"releaseWorldMap\" type=\"application/json\">";
            int begin = html.IndexOf(open, StringComparison.Ordinal);
            Assert.True(begin >= 0, "the served page carries no embedded release world map");
            begin += open.Length;
            int end = html.IndexOf("</script>", begin, StringComparison.Ordinal);
            return JObject.Parse(html.Substring(begin, end - begin));
        }

        /// <summary>
        /// Runs the extracted mirror in the engine and hands back what it says. The
        /// harness deliberately does nothing but call the mirror's own functions:
        /// any arithmetic it did itself would be arithmetic the page does not do.
        /// </summary>
        private static JArray Evaluate(string mirror, IReadOnlyList<JObject> samples)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "wareborn-whale-parity-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(directory);
            try
            {
                string inputPath = Path.Combine(directory, "input.json");
                File.WriteAllText(inputPath, new JObject
                {
                    ["samples"] = new JArray(samples),
                }.ToString(Formatting.None));

                StringBuilder script = new StringBuilder();
                script.Append(mirror);
                script.Append(@"
const input = JSON.parse(require('fs').readFileSync(process.argv[2], 'utf8'));
const motion = whaleMotion();
process.stdout.write(JSON.stringify(input.samples.map(function(s){
  const lap = motion.lapAt(s, s.t);
  const p = motion.positionAt(s.ring, lap);
  const d = motion.tangentAt(s.ring, lap);
  return [p.x, p.y, p.z, d.x, d.y, d.z];
})));
");
                string scriptPath = Path.Combine(directory, "parity.js");
                File.WriteAllText(scriptPath, script.ToString());

                return JArray.Parse(NodeFactAttribute.Run(scriptPath, inputPath));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }
    }
}
