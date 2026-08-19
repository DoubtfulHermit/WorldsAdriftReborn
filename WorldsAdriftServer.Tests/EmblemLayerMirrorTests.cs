using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// THE DRIFT GUARD, for the emblem editor.
    ///
    /// The editor's canvas is drawn in the BROWSER while a player drags a layer
    /// around - a 256-pixel PNG round trip per mouse move is not a preview - so
    /// there are two things drawing one picture, which is the arrangement this
    /// repository has already paid for once with the map mirror. "They agree
    /// because I wrote them carefully" is exactly the promise that rots.
    ///
    /// So this suite takes the REAL page the server serves, cuts the marked mirror
    /// out of its script, runs it in a JavaScript engine, and asserts it produces
    /// the same BYTES the C# does - not the same numbers to some tolerance, the
    /// same string. That is possible at all because nothing on either side is a
    /// float: a position is thousandths, a rotation is a whole degree, and the one
    /// decimal in the output is assembled from integers by
    /// <see cref="EmblemLayer.Thousandths"/>. Anything else would have needed a
    /// tolerance, and a tolerance is where the second renderer starts to lie.
    ///
    /// It checks three things, and the third is the one a careless change breaks:
    /// <list type="bullet">
    /// <item>the TRANSFORM and the whole layer's markup, over a corpus that
    ///   includes every extreme the vocabulary allows;</item>
    /// <item>the CODE, encoded and decoded both ways, so a design composed in the
    ///   browser is the design the server parses;</item>
    /// <item>the REFUSALS - a code the server will not accept must be one the
    ///   editor will not offer to save, or a player composes something and is told
    ///   only that something went wrong.</item>
    /// </list>
    /// </summary>
    public class EmblemLayerMirrorTests
    {
        private const string MirrorBegin = "// ==== EMBLEM LAYER MIRROR BEGIN ====";
        private const string MirrorEnd = "// ==== EMBLEM LAYER MIRROR END ====";

        private static readonly Guid AllianceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MineUid = Guid.Parse("22222222-2222-2222-2222-222222222222");

        // --------------------------------------------------------------- corpus

        /// <summary>
        /// Layers chosen to break a careless mirror rather than to look plausible:
        /// both ends of every range, every flag combination INCLUDING the mirror
        /// bit, rotations either side of the wrap, and the sizes where a decimal is
        /// written with one, two and three significant figures.
        ///
        /// The rotations matter more now than they did. A mirrored layer's
        /// reflection is turned by <c>360 - r</c>, so a corpus without <c>r = 0</c>
        /// would never catch a reflection written as <c>rotate(360)</c> - a turn
        /// this vocabulary cannot name and a string the two renderers would
        /// disagree about the moment one of them took the modulus and the other
        /// did not.
        /// </summary>
        private static IEnumerable<EmblemLayer> Corpus()
        {
            (int X, int Y, int Size, int Rotation)[] shapes =
            {
                (0, 0, 1000, 0),
                (0, 0, EmblemLayer.MinSize, 0),
                (0, 0, EmblemLayer.MaxSize, 359),
                (1, -1, EmblemLayer.MinSize + 1, 1),
                (-EmblemLayer.MaxOffset, EmblemLayer.MaxOffset, 500, 90),
                (EmblemLayer.MaxOffset, -EmblemLayer.MaxOffset, 999, 180),
                (250, -750, 1001, 270),
                (-3, 7, 20, 45),
                (999, -999, 100, 1),
                (0, 0, 10, 359),
            };

            int index = 0;

            foreach ((int x, int y, int size, int rotation) in shapes)
            {
                for (int flags = 0; flags <= EmblemLayer.KnownFlags; flags++)
                {
                    Assert.True(EmblemLayer.TryCreate(
                        index % EmblemObjects.Count,
                        x, y, size, rotation,
                        index % EmblemVocabulary.ColourCount,
                        index % (EmblemLayer.OpacitySteps + 1),
                        (flags & EmblemLayer.FlipXBit) != 0,
                        (flags & EmblemLayer.FlipYBit) != 0,
                        (flags & EmblemLayer.MirrorBit) != 0,
                        (flags & EmblemLayer.LockedBit) != 0,
                        out EmblemLayer layer));

                    yield return layer;
                    index++;
                }
            }
        }

        // ----------------------------------------------------------- the gate

        [NodeFact]
        public void The_browser_mirror_writes_the_same_bytes_as_the_server()
        {
            List<EmblemLayer> corpus = Corpus().ToList();

            JArray input = new JArray();
            JArray expected = new JArray();

            foreach (EmblemLayer layer in corpus)
            {
                input.Add(Wire(layer));

                // EVERY INSTANCE'S TRANSFORM, not just the placed one - a mirrored
                // layer is two shapes and the reflection is the half a browser-only
                // implementation would get away with drawing.
                JArray transforms = new JArray();
                for (int instance = 0; instance < layer.Instances; instance++)
                {
                    transforms.Add(layer.Transform(instance));
                }

                // The path data is handed IN rather than looked up in the mirror,
                // exactly as the browser gets it from the catalogue - so what is
                // compared is the markup the mirror builds and nothing else.
                expected.Add(new JObject
                {
                    ["instances"] = layer.Instances,
                    ["transforms"] = transforms,
                    ["opacity"] = layer.FillOpacity(),
                    ["markup"] = EmblemStackSvg.LayerMarkup(layer),
                });
            }

            // Every prefix of the corpus, so encoding is checked at zero layers, at
            // one, and at the twenty-layer ceiling.
            JArray codes = new JArray();
            for (int count = 0; count <= EmblemStack.MaxLayers; count++)
            {
                Assert.True(EmblemStack.TryCreate(corpus.Take(count).ToList(), out EmblemStack stack));
                codes.Add(stack.ToCode());
            }

            JArray refusals = new JArray(Refusals().Select(code => (JToken)code));

            JObject result = Run(new JObject
            {
                ["layers"] = input,
                ["codes"] = codes,
                ["refusals"] = refusals,
                ["pathData"] = PathData(corpus),
            });

            // -------- the markup

            JArray drawn = (JArray)result["drawn"]!;
            Assert.Equal(expected.Count, drawn.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                EmblemLayer layer = corpus[i];

                Assert.True(
                    JToken.DeepEquals(expected[i], drawn[i]),
                    "the mirror disagrees about layer " + i + " (" + layer.Transform() + "):\n"
                    + "  server:  " + expected[i].ToString(Formatting.None) + "\n"
                    + "  browser: " + drawn[i].ToString(Formatting.None));
            }

            // -------- the code, written

            JArray written = (JArray)result["written"]!;
            Assert.Equal(codes.Count, written.Count);

            for (int i = 0; i < codes.Count; i++)
            {
                Assert.Equal(codes[i].Value<string>(), written[i].Value<string>());
            }

            // -------- the code, read back

            JArray read = (JArray)result["read"]!;
            Assert.Equal(codes.Count, read.Count);

            for (int i = 0; i < codes.Count; i++)
            {
                Assert.True(EmblemArtwork.TryParse(codes[i].Value<string>(), out EmblemArtwork artwork));
                Assert.True(artwork.IsLayered);

                JArray theirs = (JArray)read[i]!;
                Assert.Equal(artwork.Stack.Count, theirs.Count);

                for (int j = 0; j < artwork.Stack.Count; j++)
                {
                    Assert.True(
                        JToken.DeepEquals(Wire(artwork.Stack.Layers[j]), theirs[j]),
                        "the mirror decoded layer " + j + " of code " + i + " differently:\n"
                        + "  server:  " + Wire(artwork.Stack.Layers[j]).ToString(Formatting.None) + "\n"
                        + "  browser: " + theirs[j].ToString(Formatting.None));
                }
            }

            // -------- the refusals

            JArray refused = (JArray)result["refused"]!;
            Assert.Equal(refusals.Count, refused.Count);

            for (int i = 0; i < refusals.Count; i++)
            {
                string code = refusals[i].Value<string>()!;

                Assert.False(EmblemArtwork.TryParse(code, out _),
                    "the server accepts '" + code + "', so the corpus is wrong");
                Assert.True(refused[i].Value<bool>(),
                    "the editor would offer to save '" + code + "', which the server refuses");
            }
        }

        /// <summary>
        /// Codes the server refuses. Every one of them is refused for a reason the
        /// browser can also see - a bad length, a character outside the alphabet, a
        /// value out of range, an unknown flag bit - rather than for one it cannot,
        /// like an object index past a catalogue it has not loaded.
        /// </summary>
        private static IEnumerable<string> Refusals()
        {
            yield return "3-0";                                   // a partial layer
            yield return "3-000000000000";                        // twelve, not thirteen
            yield return "3-00000000000000";                      // fourteen
            yield return "3-00000000000-0";                       // a hyphen is not in the alphabet
            yield return "3-000000000000 ";                       // nor is a space
            yield return "4-0000000000000";                       // a version from the future
            yield return "0000000000000";                         // no version at all
            yield return "3-" + new string('0', 21 * EmblemLayerCode.Width);  // twenty-one layers

            // Size zero, which is under the floor.
            yield return "3-00" + "__" + "__" + "00" + "00" + "0" + "0" + "0";

            // The flag byte with a bit we have no meaning for. Sixteen now that the
            // mirror has taken eight - and this line moving is the point of it:
            // the editor and the parser must agree about where the vocabulary
            // ENDS, or a design one of them will draw is one the other refuses.
            yield return "3-000000" + Pair(500) + "00" + "0" + "0"
                + EmblemLayerCode.Alphabet[EmblemLayer.KnownFlags + 1];

            // A rotation of 360, which is a turn this vocabulary does not have.
            yield return "3-00" + Pair(EmblemLayerCode.OffsetBias) + Pair(EmblemLayerCode.OffsetBias)
                + Pair(500) + Pair(360) + "0" + "0" + "0";
        }

        private static string Pair(int value) =>
            new string(new[] { EmblemLayerCode.Alphabet[(value >> 6) & 63], EmblemLayerCode.Alphabet[value & 63] });

        private static JObject Wire(EmblemLayer layer) => new JObject
        {
            ["o"] = layer.Object,
            ["x"] = layer.X,
            ["y"] = layer.Y,
            ["s"] = layer.Size,
            ["r"] = layer.Rotation,
            ["c"] = layer.Colour,
            ["a"] = layer.Opacity,
            ["fx"] = layer.FlipX,
            ["fy"] = layer.FlipY,
            ["mi"] = layer.Mirror,
            ["lk"] = layer.Locked,
        };

        private static JObject PathData(IEnumerable<EmblemLayer> layers)
        {
            JObject data = new JObject();

            foreach (EmblemLayer layer in layers)
            {
                string key = layer.Object.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (data.ContainsKey(key)) continue;

                data[key] = EmblemObjects.All[layer.Object].Path.ToPathData(EmblemObjects.Unit);
            }

            return data;
        }

        // ---------------------------------------------------------- the harness

        /// <summary>
        /// Cuts the mirror out of the REAL page. Not out of the asset file: the
        /// asset carries <c>{{...}}</c> placeholders that the server fills with the
        /// palette and the limits, and a mirror tested before those are filled
        /// would be a mirror tested with different constants from the one a browser
        /// runs.
        /// </summary>
        private static string Mirror()
        {
            string html = AccountPage.Render(Page());

            int begin = html.IndexOf(MirrorBegin, StringComparison.Ordinal);
            Assert.True(begin > 0, "the emblem mirror's opening marker is gone from the served page");

            int end = html.IndexOf(MirrorEnd, begin, StringComparison.Ordinal);
            Assert.True(end > begin, "the emblem mirror's closing marker is gone");

            string mirror = html.Substring(begin, end - begin);

            Assert.DoesNotContain("{{", mirror, StringComparison.Ordinal);
            Assert.Contains("embLayerMarkup", mirror, StringComparison.Ordinal);

            // PURE. A mirror that reached for the DOM could not be run here at
            // all, and a mirror that could not be run here is a mirror nothing
            // checks. Its own prose is stripped first, or the paragraph explaining
            // that it does not fetch would be the thing that fails.
            string code = string.Join('\n', mirror.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            foreach (string forbidden in new[] { "document", "window", "fetch", "querySelector" })
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.Ordinal);
            }

            return mirror;
        }

        private static PortalView Page()
        {
            AllianceCard alliance = new AllianceCard(
                AllianceId, MineUid, "The Kestrels", string.Empty, string.Empty, "Officer",
                Array.Empty<string>(), false,
                Array.Empty<AllianceMemberRow>(), Array.Empty<AllianceRankRow>(),
                Array.Empty<RequestRow>(), Array.Empty<RequestRow>(),
                EmblemSpec.DefaultFor(AllianceId), false, null,
                new AllianceRights(false, false, true, false));

            CharacterSheet sheet = new CharacterSheet(
                MineUid, "Wrenna", 0, DateTimeOffset.UnixEpoch, null, null, null);

            return new PortalView(
                "wrenna", "wrenna", DateTimeOffset.UnixEpoch, null, "-", "-",
                new[] { new CharacterCard(sheet, null, alliance) },
                new string('a', 32), null, false, PortalTabs.Emblem);
        }

        private static JObject Run(JObject input)
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "wareborn-emblem-mirror-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(directory);

            try
            {
                string inputPath = Path.Combine(directory, "input.json");
                File.WriteAllText(inputPath, input.ToString(Formatting.None));

                StringBuilder harness = new StringBuilder();
                harness.Append("const fs = require('fs');\n");
                harness.Append(Mirror());
                harness.Append(@"
const input = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));

const drawn = input.layers.map(function (layer) {
  const transforms = [];
  for (let i = 0; i < embInstances(layer); i++) { transforms.push(embTransform(layer, i)); }

  return {
    instances: embInstances(layer),
    transforms: transforms,
    opacity: embThousandths(layer.a * embLimits.opacityUnit),
    markup: embLayerMarkup(layer, input.pathData[String(layer.o)])
  };
});

const written = input.codes.map(function (code, at) {
  return embEncode(input.layers.slice(0, at));
});

const read = input.codes.map(embDecode);
const refused = input.refusals.map(function (code) { return embDecode(code) === null; });

process.stdout.write(JSON.stringify({drawn: drawn, written: written, read: read, refused: refused}));
");

                string harnessPath = Path.Combine(directory, "mirror.js");
                File.WriteAllText(harnessPath, harness.ToString());

                return JObject.Parse(NodeFactAttribute.Run(harnessPath, inputPath));
            }
            finally
            {
                try { Directory.Delete(directory, true); } catch { /* a temp dir */ }
            }
        }

        // --------------------------------------------------------- the markers

        /// <summary>
        /// The markers are the contract. A rename that loses them turns this whole
        /// suite into a skip nobody notices, so the file is checked directly as
        /// well as through the page.
        /// </summary>
        [Fact]
        public void The_mirror_is_marked_in_the_asset_itself()
        {
            string script = WebAssets.Read("emblem-editor.js");

            Assert.Contains(MirrorBegin, script, StringComparison.Ordinal);
            Assert.Contains(MirrorEnd, script, StringComparison.Ordinal);
            Assert.True(
                script.IndexOf(MirrorBegin, StringComparison.Ordinal)
                < script.IndexOf(MirrorEnd, StringComparison.Ordinal));
        }

        /// <summary>
        /// SYMMETRY AND THE GRID ARE BOTH REACHABLE AND BOTH SAY WHETHER THEY ARE
        /// ON.
        ///
        /// They are toggles rather than actions - a mirrored layer STAYS mirrored,
        /// and the grid stays on until it is turned off - so each carries
        /// aria-pressed. A toggle that looks like a button is the one a player
        /// presses twice and gives up on.
        ///
        /// Here rather than in AccountPageTests because these are about the emblem
        /// editor's own markup, and that file is the portal SHELL's.
        /// </summary>
        [Fact]
        public void The_editor_offers_mirror_and_grid_as_toggles_that_say_their_state()
        {
            string html = AccountPage.Render(Page());

            Assert.Contains("data-mirror aria-pressed=\"false\"", html, StringComparison.Ordinal);
            Assert.Contains("data-grid aria-pressed=\"false\"", html, StringComparison.Ordinal);

            // Neither is a form field. The mirror rides in the design code with
            // every other property of a layer, and the grid rides nowhere at all.
            Assert.DoesNotContain("name=\"mirror\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("name=\"grid\"", html, StringComparison.Ordinal);
        }

        /// <summary>
        /// The limits the server stamps into the script are the place a future
        /// reader would be tempted to put a grid step. They are not one.
        /// </summary>
        [Fact]
        public void The_grid_is_not_one_of_the_limits_the_server_stamps_in()
        {
            foreach (string forbidden in new[] { "grid", "snap" })
            {
                Assert.DoesNotContain(forbidden, EmblemEditorData.LimitsJson(),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// THE GRID IS NOWHERE INSIDE THE MIRROR, and it must never be.
        ///
        /// The mirror is the region that turns a layer into markup and into a code,
        /// and the grid is an editor affordance: it changes which values a player
        /// produces and nothing about what a value means. If a grid step, a grid
        /// flag or a "snapped" bit ever appeared in here, two designs that look
        /// identical would encode differently and the URL would stop being a
        /// function of the picture - which is the property the whole immutable-cache
        /// arrangement rests on.
        ///
        /// Checked against the SERVED page rather than the file, so it also catches
        /// a grid constant stamped in from the server side.
        /// </summary>
        [NodeFact]
        public void The_grid_is_not_part_of_the_encoding()
        {
            string mirror = Mirror();

            foreach (string forbidden in new[] { "grid", "snap" })
            {
                Assert.DoesNotContain(forbidden, mirror, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// The mirror's own constants are stamped in by the server rather than
        /// typed into the script - so a browser cannot be working in a different
        /// space from the parser that will read what it produces.
        /// </summary>
        [Fact]
        public void The_units_and_the_palette_reach_the_script_from_the_server()
        {
            string html = AccountPage.Render(Page());

            Assert.Contains(EmblemEditorData.LimitsJson(), html, StringComparison.Ordinal);
            Assert.Contains(EmblemEditorData.PaletteJson(), html, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        }
    }
}
