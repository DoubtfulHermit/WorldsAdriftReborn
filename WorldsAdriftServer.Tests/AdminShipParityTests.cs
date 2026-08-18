using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// THE DRIFT GUARD FOR SHIPS, and the wire that feeds it.
    ///
    /// A ship's position is a MEASUREMENT, unlike a creature's, so the console
    /// does not evaluate a closed form of it. What it does evaluate is the rule
    /// for how far a measurement may be carried forward between snapshots, and
    /// that rule is arithmetic in two places - <see cref="ShipMapMotion"/> and a
    /// mirror of it in the served page - which is exactly the promise that rots.
    ///
    /// So this suite runs the WHOLE CHAIN rather than the formula alone: it builds
    /// a real <see cref="StatsSnapshot"/> the way the game server does, serialises
    /// it, reads it back through the login server's own allowlist projection, cuts
    /// the marked mirror out of the REAL served page, and asserts the browser's
    /// arithmetic over the PROJECTED numbers equals what the C# says. A renamed
    /// field, a dropped clamp or a changed formula each break it.
    /// </summary>
    public class AdminShipParityTests
    {
        private const string MirrorBegin = "// ==== SHIP MOTION MIRROR BEGIN ====";
        private const string MirrorEnd = "// ==== SHIP MOTION MIRROR END ====";

        /// <summary>
        /// Nanometres, for the parity itself: the two evaluators run the same
        /// arithmetic over the same doubles.
        /// </summary>
        private const double ExactTolerance = 1e-9;

        /// <summary>
        /// Ages spanning the interesting parts of the rule: nothing yet, well
        /// inside the window, exactly on it, past it, absurdly past it, and a
        /// clock that ran backwards.
        /// </summary>
        private static readonly double[] Ages =
        {
            -3.0, -0.001, 0.0, 0.25, 1.0, 3.16, 3.1622776601683795,
            3.2, 4.0, 12.0, 600.0,
        };

        /// <summary>
        /// The live player's saved hull, byte for byte off the server. Using the
        /// REAL hull rather than a synthetic one means the outline that travels
        /// this wire is the one a real ship has.
        /// </summary>
        private const string LiveSavedHullHex =
            "020000000000e80000180000e8008e18008e0000000000ffff0000e80000180000e8"
            + "00001800000000000001e80000180000e8007218007200000000";

        // ---- the mirror is the arithmetic --------------------------------------

        [NodeFact]
        public void The_browser_mirror_reckons_the_same_metres_as_the_server_rule()
        {
            JObject game = ProjectedGame(Snapshot());
            JObject model = (JObject)game["shipModel"]!;
            JObject ship = (JObject)((JArray)game["runtime"]!["shipDomains"]!)[0]!;

            ShipMapPose measured = PoseOf(ship);
            double window = (double)model["windowSeconds"]!;

            List<JObject> samples = new List<JObject>();
            foreach (double age in Ages)
            {
                samples.Add(new JObject { ["s"] = StateOf(ship), ["age"] = age });
            }

            JArray actual = Evaluate(ExtractMirror(Dashboard()), model, samples);
            Assert.Equal(samples.Count, actual.Count);

            for (int i = 0; i < Ages.Length; i++)
            {
                double age = Ages[i];
                ShipMapPose expected = ShipMapMotion.PoseAt(measured, age, window);
                JArray got = (JArray)actual[i];
                string where = "at age " + age.ToString(CultureInfo.InvariantCulture);

                Assert.True(Math.Abs(expected.X - (double)got[0]!) <= ExactTolerance,
                    where + ": X was " + got[0] + ", the server rule says " + expected.X);
                Assert.True(Math.Abs(expected.Z - (double)got[1]!) <= ExactTolerance,
                    where + ": Z was " + got[1] + ", the server rule says " + expected.Z);
                Assert.True(Math.Abs(expected.YawRadians - (double)got[2]!) <= ExactTolerance,
                    where + ": yaw was " + got[2] + ", the server rule says " + expected.YawRadians);
                Assert.True(Math.Abs(ShipMapMotion.Reckoned(age, window) - (double)got[3]!) <= ExactTolerance,
                    where + ": the reckoned seconds disagree");
                Assert.True(Math.Abs(ShipMapMotion.ErrorBoundMetres(
                        (double)model["accelMps2"]!, ShipMapMotion.Reckoned(age, window)) - (double)got[4]!)
                        <= ExactTolerance,
                    where + ": the printed error bound disagrees");
            }
        }

        /// <summary>
        /// THE CASE THAT IS EASIEST TO GET WRONG. A game server that predates this
        /// feature publishes no model, which reaches the browser as a zero window.
        /// Zero must reckon NOTHING on both sides - a floor applied on one side
        /// only would draw every hull half a second ahead of a measurement the
        /// server never offered a velocity for.
        /// </summary>
        [NodeFact]
        public void An_absent_model_reckons_nothing_on_both_sides()
        {
            JObject absent = GameShipModelStat.Absent().Json;
            JObject ship = (JObject)((JArray)ProjectedGame(Snapshot())["runtime"]!["shipDomains"]!)[0]!;

            List<JObject> samples = new List<JObject>();
            foreach (double age in Ages) samples.Add(new JObject { ["s"] = StateOf(ship), ["age"] = age });

            JArray actual = Evaluate(ExtractMirror(Dashboard()), absent, samples);
            ShipMapPose measured = PoseOf(ship);

            for (int i = 0; i < Ages.Length; i++)
            {
                Assert.Equal(0.0, ShipMapMotion.Reckoned(Ages[i], 0), 12);
                Assert.True(Math.Abs(measured.X - (double)((JArray)actual[i])[0]!) <= ExactTolerance,
                    "an absent model still moved the hull at age " + Ages[i]);
                Assert.True(Math.Abs(measured.Z - (double)((JArray)actual[i])[1]!) <= ExactTolerance,
                    "an absent model still moved the hull at age " + Ages[i]);
            }
        }

        /// <summary>
        /// The mirror must be CUT OUT of the served page, and every number it
        /// reads must be a field the projection actually publishes - an
        /// <c>undefined</c> in this arithmetic is a NaN transform, which is a
        /// ship that silently vanishes rather than one drawn in the wrong place.
        /// </summary>
        [Fact]
        public void The_served_page_carries_the_marked_mirror_and_the_model_it_needs()
        {
            string mirror = ExtractMirror(Dashboard());
            Assert.Contains("function shipMotion(M)", mirror);
            Assert.Contains("poseAt", mirror);

            JObject model = (JObject)ProjectedGame(Snapshot())["shipModel"]!;
            foreach (string field in new[]
            {
                "windowSeconds", "maxWindowSeconds", "accelMps2",
            })
            {
                Assert.True(mirror.Contains("M." + field, StringComparison.Ordinal),
                    "the mirror never reads the published constant '" + field + "'");
                Assert.True(model[field] != null,
                    "the mirror reads 'M." + field + "' but the projection does not publish it");
            }
        }

        // ---- the wire carries the hull the server derived -----------------------

        /// <summary>
        /// THE SHAPE SURVIVES THE WIRE. The ring the browser is handed must be the
        /// one <see cref="ShipMapSilhouette"/> derived from the player's hull
        /// bytes, point for point, to the centimetre the snapshot trims to. This
        /// is the claim the whole feature rests on: the outline on the map is the
        /// player's own hull and not a drawing.
        /// </summary>
        [Fact]
        public void The_published_outline_is_the_ring_the_silhouette_derived()
        {
            JObject ship = (JObject)((JArray)ProjectedGame(Snapshot())["runtime"]!["shipDomains"]!)[0]!;
            JObject hull = (JObject)ship["hull"]!;
            JArray outline = (JArray)hull["outline"]!;

            ShipMapSilhouette expected = ShipMapSilhouette.Of(LiveSavedHull());
            Assert.False(expected.IsEmpty);
            Assert.True((bool)hull["present"]!);
            Assert.Equal(expected.Outline.Count * 2, outline.Count);

            for (int i = 0; i < expected.Outline.Count; i++)
            {
                Assert.Equal(expected.Outline[i].X, (double)outline[i * 2]!, 2);
                Assert.Equal(expected.Outline[i].Z, (double)outline[i * 2 + 1]!, 2);
            }

            ShipHullMetrics metrics = expected.Metrics;
            Assert.Equal(metrics.BeamMetres, (double)hull["beamMetres"]!, 2);
            Assert.Equal(metrics.KeelMetres, (double)hull["keelMetres"]!, 2);
            Assert.Equal(metrics.CellCount, (int)hull["cellCount"]!);
            Assert.Equal(expected.SectionCount, (int)hull["sectionCount"]!);
        }

        /// <summary>
        /// The heading is carried, and it is the flight state's own yaw. Before
        /// this, the pose was published without it and a console could only draw a
        /// ship as a dot - the number was already in the struct the position was
        /// read from.
        /// </summary>
        [Fact]
        public void The_published_pose_carries_the_heading_and_both_derivatives()
        {
            JObject ship = (JObject)((JArray)ProjectedGame(Snapshot())["runtime"]!["shipDomains"]!)[0]!;

            Assert.Equal(0.9, (double)ship["yawRadians"]!, 9);
            Assert.Equal(0.05, (double)ship["yawRateRadPerSec"]!, 9);
            Assert.Equal(7.5, (double)ship["vxMps"]!, 9);
            Assert.Equal(-2.25, (double)ship["vzMps"]!, 9);
        }

        /// <summary>
        /// A ship whose hull bytes never arrived must project to a hull that is
        /// explicitly NOT present, with an empty ring - so the console draws a
        /// plain mark and says why, rather than a substitute shape or a path
        /// attribute built from nothing.
        /// </summary>
        [Fact]
        public void A_ship_with_no_decodable_hull_projects_to_an_absent_shape()
        {
            JObject ship = (JObject)((JArray)ProjectedGame(Snapshot(withHull: false))["runtime"]!["shipDomains"]!)[0]!;
            JObject hull = (JObject)ship["hull"]!;

            Assert.False((bool)hull["present"]!);
            Assert.Empty((JArray)hull["outline"]!);
            Assert.Equal(0.0, (double)hull["beamMetres"]!, 6);
        }

        /// <summary>
        /// A game server that predates the ship block at all - schema 7 - must
        /// project to an absent model and to ships with absent hulls, never to an
        /// exception. The two processes are deployed separately; this is the
        /// normal state during a rollout.
        /// </summary>
        [Fact]
        public void A_schema_seven_snapshot_still_parses_and_reports_an_absent_model()
        {
            JObject file = JObject.Parse(Snapshot().ToJson());
            file["schemaVersion"] = 7;
            file.Remove("shipModel");
            foreach (JToken token in (JArray)file["runtime"]!["shipDomains"]!)
            {
                ((JObject)token).Remove("hull");
                ((JObject)token).Remove("yawRadians");
            }

            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(file);
            Assert.False(parsed.ShipModel.Present);
            Assert.False((bool)parsed.ShipModel.Json["present"]!);
            Assert.Single(parsed.ShipDomains);

            JObject hull = (JObject)parsed.ShipDomains[0].Json["hull"]!;
            Assert.False((bool)hull["present"]!);
            Assert.Equal(0.0, (double)parsed.ShipDomains[0].Json["yawRadians"]!, 9);
        }

        /// <summary>
        /// The projection CLAMPS, because these numbers reach an SVG transform and
        /// a per-frame loop. A hostile or corrupt file must cost a wrong figure,
        /// never a hung browser or a view box stretched around a ship at the edge
        /// of the number line.
        /// </summary>
        [Fact]
        public void A_malformed_snapshot_is_clamped_rather_than_forwarded()
        {
            JObject file = JObject.Parse(Snapshot().ToJson());
            JObject ship = (JObject)((JArray)file["runtime"]!["shipDomains"]!)[0]!;
            ship["vxMps"] = 1e18;
            ship["yawRateRadPerSec"] = double.NaN;
            JObject hull = (JObject)ship["hull"]!;
            hull["beamMetres"] = -1e12;
            JArray huge = new JArray();
            for (int i = 0; i < 4000; i++) huge.Add(1e9);
            hull["outline"] = huge;
            ((JObject)file["shipModel"]!)["windowSeconds"] = 1e9;

            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(file);
            JObject projected = parsed.ShipDomains[0].Json;
            JObject projectedHull = (JObject)projected["hull"]!;

            Assert.InRange((double)projected["vxMps"]!, -2000, 2000);
            Assert.Equal(0.0, (double)projected["yawRateRadPerSec"]!, 9);
            Assert.InRange((double)projectedHull["beamMetres"]!, -2000, 0);
            Assert.Equal(1024, ((JArray)projectedHull["outline"]!).Count);
            Assert.InRange((double)parsed.ShipModel.Json["windowSeconds"]!, 0, 1000);
        }

        // ---- fixtures ----------------------------------------------------------

        private static string Dashboard() =>
            AdminPage.Dashboard("{}", new string('a', 64), ReleaseWorldMap.Json);

        private static ShipPlanModel LiveSavedHull()
        {
            byte[] bytes = new byte[LiveSavedHullHex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(LiveSavedHullHex.Substring(i * 2, 2), 16);
            }
            Assert.True(ShipPlanModel.TryDecode(bytes, out ShipPlanModel? plan, out string? error), error);
            return plan!;
        }

        /// <summary>
        /// A snapshot shaped exactly as the game server builds one, with one ship
        /// under way on the real hull.
        /// </summary>
        private static StatsSnapshot Snapshot(bool withHull = true)
        {
            ShipHullStat hull = withHull
                ? new ShipHullStat(
                    ShipMapSilhouette.Of(LiveSavedHull()), "character-uid-1", false,
                    new WorldsAdriftRebornGameServer.Multiplayer.Materials.HullMaterials("birch", 4, "iron", 3))
                : ShipHullStat.Unavailable;

            ShipDomainStat ship = new ShipDomainStat(
                "domain:ship:1", 4242, 3, 91, 240, 120,
                1234.5, 210.25, -876.5, true, true, true, 77,
                new long[] { 77 }, 6, 4, 1,
                yawRadians: 0.9, yawRateRadPerSec: 0.05,
                vxMps: 7.5, vyMps: 0.25, vzMps: -2.25,
                hull: hull);

            return new StatsSnapshot(
                bootTimeUnixMs: 1_723_200_000_000, generatedAtUnixMs: 1_723_200_120_000,
                uptimeSeconds: 120, relayMode: "v2@20Hz", relayHz: 20, build: "test",
                totalConnects: 1, totalDisconnects: 0, currentOnline: 1, peakOnline: 1,
                players: Array.Empty<PlayerStat>(),
                shipDomains: new[] { ship },
                shipModel: new ShipMapRuntimeStat(
                    FlightTuning.DefaultAccelMps2, FlightTuning.DefaultMaxSpeedMps));
        }

        /// <summary>
        /// The snapshot as the BROWSER receives it: serialised by the game server,
        /// re-parsed and allowlist-rebuilt by the login server. Every number the
        /// parity check uses has been through both.
        /// </summary>
        private static JObject ProjectedGame(StatsSnapshot snapshot)
        {
            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(JObject.Parse(snapshot.ToJson()));
            return new JObject
            {
                ["shipModel"] = parsed.ShipModel.Json,
                ["runtime"] = new JObject
                {
                    ["shipDomains"] = new JArray(parsed.ShipDomains.Select(x => x.Json)),
                },
            };
        }

        private static JObject StateOf(JObject ship) => new JObject
        {
            ["x"] = (double)ship["x"]!,
            ["z"] = (double)ship["z"]!,
            ["yaw"] = (double)ship["yawRadians"]!,
            ["vx"] = (double)ship["vxMps"]!,
            ["vz"] = (double)ship["vzMps"]!,
            ["yawRate"] = (double)ship["yawRateRadPerSec"]!,
        };

        private static ShipMapPose PoseOf(JObject ship) => new ShipMapPose(
            (double)ship["x"]!, (double)ship["z"]!, (double)ship["yawRadians"]!,
            (double)ship["vxMps"]!, (double)ship["vzMps"]!, (double)ship["yawRateRadPerSec"]!);

        private static string ExtractMirror(string html)
        {
            int begin = html.IndexOf(MirrorBegin, StringComparison.Ordinal);
            int end = html.IndexOf(MirrorEnd, StringComparison.Ordinal);
            Assert.True(begin >= 0 && end > begin,
                "the served admin page no longer carries the marked ship motion mirror, so "
                + "nothing is pinning the browser's arithmetic to the server's");
            return html.Substring(begin + MirrorBegin.Length, end - begin - MirrorBegin.Length);
        }

        /// <summary>
        /// Runs the extracted mirror and hands back what it says. The harness does
        /// nothing but call it: any arithmetic here would be arithmetic the page
        /// does not do.
        /// </summary>
        private static JArray Evaluate(string mirror, JObject model, IReadOnlyList<JObject> samples)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "wareborn-ship-parity-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(directory);
            try
            {
                string inputPath = Path.Combine(directory, "input.json");
                File.WriteAllText(inputPath, new JObject
                {
                    ["model"] = model,
                    ["samples"] = new JArray(samples),
                }.ToString(Formatting.None));

                StringBuilder script = new StringBuilder();
                script.Append(mirror);
                script.Append(@"
const input = JSON.parse(require('fs').readFileSync(process.argv[2], 'utf8'));
const motion = shipMotion(input.model);
process.stdout.write(JSON.stringify(input.samples.map(function(s){
  const pose = motion.poseAt(s.s, s.age);
  const t = motion.reckoned(s.age);
  return [pose.x, pose.z, pose.yaw, t, motion.errorBound(t)];
})));
");
                string scriptPath = Path.Combine(directory, "parity.js");
                File.WriteAllText(scriptPath, script.ToString());

                return JArray.Parse(NodeFactAttribute.Run(scriptPath, inputPath));
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
            }
        }
    }
}
