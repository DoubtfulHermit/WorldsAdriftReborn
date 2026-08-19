using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Portal;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Renders every tab of the portal against a full, realistic view - and, when
    /// asked, writes the result somewhere a browser can open it.
    ///
    /// WHY THIS EXISTS. An editor is a VISUAL artefact. Every other test in this
    /// project asserts a string, and a passing string says nothing about whether a
    /// control is legible, whether the layout has collapsed to two columns on a
    /// phone, or whether a selection box has ended up underneath the picture it is
    /// selecting - all three of which happened while this was being built and none
    /// of which any assertion here would have caught.
    ///
    /// So it does two jobs. Unconditionally it renders all five tabs and checks
    /// the things that CAN be checked without eyes: that each one produces a page,
    /// that no placeholder reached it unfilled, and that nothing reaches off this
    /// host. With <c>WAREBORN_PORTAL_DUMP</c> set to a directory it also writes
    /// each tab, the object catalogue and the emblem's PNG and SVG into it, laid
    /// out at the paths the page asks for - so:
    ///
    /// <code>
    /// WAREBORN_PORTAL_DUMP=/tmp/portal dotnet test WorldsAdriftServer.Tests \
    ///     --filter FullyQualifiedName~PortalSnapshotDump
    /// (cd /tmp/portal &amp;&amp; python3 -m http.server 8899)
    /// chromium --headless=new --window-size=1440,1150 \
    ///     --screenshot=emblem.png http://127.0.0.1:8899/emblem.html
    /// </code>
    ///
    /// gives a real screenshot of the real editor, with a real seven-layer design
    /// already on the canvas, in about ten seconds. The design below is chosen to
    /// exercise the parts a screenshot has to show: a locked layer, a translucent
    /// one, a traced device, two of the same object, and the layer count.
    /// </summary>
    public class PortalSnapshotDump
    {
        private static readonly Guid AllianceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid MineUid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OtherUid = Guid.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly Guid LeaderRankId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        private static readonly Guid MemberRankId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        [Fact]
        public void Every_tab_renders_and_can_be_written_out_for_a_look()
        {
            EmblemStack sample = Design();
            EmblemArtwork sampleArt = EmblemArtwork.Of(sample);
            PortalView sampleView = View(sampleArt);

            foreach (PortalTab tab in PortalTabs.For(sampleView))
            {
                string html = AccountPage.Render(sampleView with { Tab = tab.Id });

                Assert.True(html.Length > 2000, tab.Id + " rendered almost nothing");
                Assert.DoesNotContain("{{", html, StringComparison.Ordinal);

                // The W3C SVG namespace is an identifier, not an address.
                string reach = html.Replace("http://www.w3.org/2000/svg", "", StringComparison.Ordinal);
                Assert.DoesNotContain("http://", reach, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("https://", reach, StringComparison.OrdinalIgnoreCase);
            }

            string? root = Environment.GetEnvironmentVariable("WAREBORN_PORTAL_DUMP");
            if (string.IsNullOrEmpty(root)) return;

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "alliance-emblem"));

            EmblemStack design = Design();
            EmblemArtwork artwork = EmblemArtwork.Of(design);

            File.WriteAllText(Path.Combine(root, "alliance-emblem", "objects.json"),
                EmblemEditorData.Catalogue);
            File.WriteAllBytes(Path.Combine(root, "alliance-emblem", "preview.png"),
                EmblemImages.Png(artwork));
            File.WriteAllText(Path.Combine(root, "alliance-emblem", "preview.svg"), artwork.ToSvg());

            PortalView view = View(artwork);

            foreach (PortalTab tab in PortalTabs.For(view))
            {
                File.WriteAllText(Path.Combine(root, tab.Id + ".html"),
                    AccountPage.Render(view with { Tab = tab.Id }));
            }

            File.WriteAllText(Path.Combine(root, "code.txt"), design.ToCode());
        }

        private static EmblemStack Design()
        {
            List<EmblemLayer> layers = new List<EmblemLayer>();

            void Add(string name, int x, int y, int size, int rotation, int colour, int opacity,
                bool flipX = false, bool mirror = false, bool locked = false)
            {
                int obj = 0;
                for (int i = 0; i < EmblemObjects.Count; i++)
                {
                    if (EmblemObjects.All[i].Name == name) { obj = i; break; }
                }

                Assert.True(EmblemLayer.TryCreate(obj, x, y, size, rotation, colour, opacity,
                    flipX, false, mirror, locked, out EmblemLayer layer));
                layers.Add(layer);
            }

            // EVERY POSITION HERE IS A MULTIPLE OF A HUNDRED, which is the editor's
            // grid step - so this design is also a picture of what building one
            // with the grid on produces, and of the fact that the grid left no
            // trace in the code that produced it.
            Add("Roundel", 0, 0, 1000, 0, 0, 40, locked: true);
            Add("Disc", 0, 0, 900, 0, 11, 40, locked: true);
            Add("Chevron", 0, 300, 800, 0, 4, 40);
            Add("Wolf head", 0, -100, 600, 0, 3, 40);

            // THE PAIR THAT USED TO BE TWO LAYERS. A six-point star at -500 and
            // another at +500 was the only way to make this symmetrical before
            // mirroring existed; it is one layer now, it costs one slot instead of
            // two, and dragging it moves both stars.
            Add("Six-point star", 500, -500, 200, 15, 13, 40, mirror: true);

            Add("Slim bar", 0, 600, 900, 0, 7, 28);

            Assert.True(EmblemStack.TryCreate(layers, out EmblemStack stack));
            return stack;
        }

        private static PortalView View(EmblemArtwork artwork)
        {
            CharacterSheet sheet = new CharacterSheet(
                MineUid, "Wrenna", 0, DateTimeOffset.UnixEpoch,
                new SheetKnowledge(4, 11, 7, new[] { "sch_rope", "sch_hull_plate" },
                    new[] { new SheetTally("node_iron", 3) }, 3, 2),
                new SheetInventory(10, 18, 2, 12, 1, 0, new[] { new SheetTally("iron_ore", 12) }),
                new SheetPosition(120, -3, 44, "Kestrel's Rest", true, DateTimeOffset.UnixEpoch));

            AllianceCard alliance = new AllianceCard(
                AllianceId, MineUid, "The Kestrels", "We fly at dawn.", "Meet at the spire.",
                "Officer", new[] { "edit_group", "edit_members" }, false,
                new[]
                {
                    new AllianceMemberRow(MineUid, "Wrenna", "Officer", MemberRankId, false, true, false, false),
                    new AllianceMemberRow(OtherUid, "Halloran", "Member", MemberRankId, false, false, true, true),
                },
                new[]
                {
                    new AllianceRankRow(LeaderRankId, "Leader", false, true, new[] { "edit_group" }),
                    new AllianceRankRow(MemberRankId, "Member", false, false, Array.Empty<string>()),
                },
                new[] { new RequestRow("invite:a", "Sesta", "Let me in", DateTimeOffset.UnixEpoch) },
                Array.Empty<RequestRow>(),
                artwork, true, null,
                new AllianceRights(true, true, true, true));

            CrewCard crew = new CrewCard("crew:1", "Halloran's crew", 4, new[]
            {
                new CrewMemberRow("Halloran", true, false, 0),
                new CrewMemberRow("Wrenna", false, true, null),
            });

            return new PortalView(
                "wrenna", "wrenna", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                "2026.08.19", "2",
                new[] { new CharacterCard(sheet, crew, alliance) },
                new string('a', 32), null, false);
        }
    }
}
