using System.IO.Compression;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// What goes in the database column, what goes on the wire, and how a request
    /// for the PNG gets back to a spec.
    ///
    /// The single most important behaviour asserted here is that
    /// <see cref="EmblemUrlPolicy.TryParseRequest"/> never refuses a request it
    /// has claimed - it hands back "no usable code" instead. The client's decoder
    /// does not check whether decoding worked: a 404 body becomes a tiny garbage
    /// texture, <c>Sprite.Create</c> wraps it, the promise resolves NON-null, and
    /// the alliance panel replaces its own placeholder with rubbish. So an error
    /// page is strictly worse than any picture, and "always answer with a crest"
    /// is a correctness requirement rather than politeness.
    /// </summary>
    public class EmblemUrlPolicyTests
    {
        private static readonly Guid Alliance = Guid.Parse("2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10");

        private static EmblemSpec Spec(string code)
        {
            Assert.True(EmblemSpec.TryParse(code, out EmblemSpec spec));
            return spec;
        }

        [Fact]
        public void The_stored_marker_round_trips()
        {
            EmblemSpec spec = Spec("2-2-5-7-11-3-13");

            string stored = EmblemUrlPolicy.Store(spec);
            Assert.Equal("wareborn:emblem:2-2-5-7-11-3-13", stored);

            Assert.True(EmblemUrlPolicy.TryReadStored(stored, out EmblemArtwork back));

            // A heraldic code reads back as heraldic ARTWORK, not as a layer
            // stack: the two forms of an emblem never convert into each other.
            Assert.False(back.IsLayered);
            Assert.Equal(spec, back.Heraldic);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://example.invalid/rat.png")]
        [InlineData("wareborn:emblem:")]
        [InlineData("wareborn:emblem:nonsense")]
        [InlineData("wareborn:emblem:9-0-0-0-0-0-0")]
        [InlineData("WAREBORN:EMBLEM:1-0-0-0-0-0-0")]
        public void Anything_that_is_not_one_of_our_markers_is_not_a_spec(string? stored)
        {
            Assert.False(EmblemUrlPolicy.TryReadStored(stored, out _));
        }

        [Fact]
        public void A_built_emblem_resolves_to_an_absolute_url_on_our_host()
        {
            string url = EmblemUrlPolicy.Resolve(
                "https://wareborn.example/", Alliance, EmblemUrlPolicy.Store(Spec("2-1-2-3-4-5-6")));

            Assert.Equal(
                "https://wareborn.example/alliance-emblem/"
                + Alliance.ToString("D") + ".png?e=2-1-2-3-4-5-6",
                url);

            // Absolute, because the client hands it to new Uri(url) with no base
            // and a relative one throws inside a promise nothing catches.
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _));
        }

        [Fact]
        public void An_operator_set_external_url_still_wins()
        {
            // The pre-existing escape hatch. An operator who puts a plain URL in
            // the column meant it, and the builder must not quietly replace it.
            Assert.Equal(
                "https://example.invalid/rat.png",
                EmblemUrlPolicy.Resolve("https://wareborn.example", Alliance,
                    "https://example.invalid/rat.png"));
        }

        [Fact]
        public void An_alliance_with_no_stored_emblem_gets_its_generated_crest()
        {
            string url = EmblemUrlPolicy.Resolve("https://wareborn.example", Alliance, "");

            Assert.Contains("/alliance-emblem/" + Alliance.ToString("D") + ".png?e=", url,
                StringComparison.Ordinal);
            Assert.EndsWith(EmblemSpec.DefaultFor(Alliance).ToCode(), url, StringComparison.Ordinal);
        }

        [Fact]
        public void The_base_url_is_normalised_so_the_path_never_doubles_its_slash()
        {
            foreach (string baseUrl in new[]
            {
                "https://wareborn.example",
                "https://wareborn.example/",
                "https://wareborn.example///",
                "  https://wareborn.example/  ",
            })
            {
                Assert.StartsWith(
                    "https://wareborn.example/alliance-emblem/",
                    EmblemUrlPolicy.Resolve(baseUrl, Alliance, ""),
                    StringComparison.Ordinal);
            }
        }

        // ------------------------------------------------------------- requests

        [Fact]
        public void A_request_carrying_a_good_code_parses_to_that_code()
        {
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/" + Alliance.ToString("D") + ".png?e=2-2-3-4-5-6-7",
                out Guid id, out EmblemArtwork spec, out bool hasCode, out EmblemUrlPolicy.Format format,
                out int size));

            Assert.Equal(Alliance, id);
            Assert.True(hasCode);
            Assert.Equal("2-2-3-4-5-6-7", spec.ToCode());
            Assert.Equal(EmblemUrlPolicy.Format.Png, format);
            Assert.Equal(EmblemUrlPolicy.DefaultSize, size);
        }

        [Fact]
        public void The_preview_path_parses_with_no_alliance()
        {
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/preview.png?e=2-0-0-0-0-0-0",
                out Guid id, out _, out bool hasCode, out _, out _));

            Assert.Equal(Guid.Empty, id);
            Assert.True(hasCode);
        }

        [Theory]
        [InlineData("/alliance-emblem/" + "2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10.png")]
        [InlineData("/alliance-emblem/2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10.png?e=")]
        [InlineData("/alliance-emblem/2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10.png?e=garbage")]
        [InlineData("/alliance-emblem/2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10.png?e=9-9-9-9-9-9-9")]
        [InlineData("/alliance-emblem/2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10.png?other=1")]
        public void A_bad_or_missing_code_is_still_a_request_we_answer(string url)
        {
            // NOT false, and this is the load-bearing one. Returning false here
            // would drop the request through to the router's 404, and a 404 body
            // reaches the client as a garbage texture it DISPLAYS.
            Assert.True(EmblemUrlPolicy.TryParseRequest(url, out _, out _, out bool hasCode, out _, out _));
            Assert.False(hasCode);
        }

        [Fact]
        public void The_whole_prefix_is_claimed_even_where_nothing_renders()
        {
            // The router answers only what a handler claims - an unclaimed path
            // gets NO response and the socket hangs. So "is this ours" has to be
            // wider than "can we draw it", or the odd corner of our own namespace
            // becomes a hang instead of a 404.
            Assert.True(EmblemUrlPolicy.IsEmblemPath("/alliance-emblem/"));
            Assert.True(EmblemUrlPolicy.IsEmblemPath("/alliance-emblem/not-a-guid.png"));
            Assert.True(EmblemUrlPolicy.IsEmblemPath("/alliance-emblem/deep/path.png"));
            Assert.True(EmblemUrlPolicy.IsEmblemPath("/alliance-emblem/x.jpg?e=1"));

            Assert.False(EmblemUrlPolicy.IsEmblemPath(null));
            Assert.False(EmblemUrlPolicy.IsEmblemPath(""));
            Assert.False(EmblemUrlPolicy.IsEmblemPath("/download/WAPatch.exe"));
            Assert.False(EmblemUrlPolicy.IsEmblemPath("/Alliance-Emblem/x.png"));
            Assert.False(EmblemUrlPolicy.IsEmblemPath("/alliance-emblems/x.png"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("/download/WAPatch.exe")]
        [InlineData("/alliance-emblem/")]
        [InlineData("/alliance-emblem/x.jpg")]
        [InlineData("/alliance-emblem/deep/path.png")]
        [InlineData("/alliance-emblem/..%2f..%2fetc%2fpasswd.png")]
        [InlineData("/alliance-emblem/../../secrets.png")]
        [InlineData("/alliance-emblem/..\\..\\secrets.png")]
        [InlineData("/Alliance-Emblem/x.png")]
        [InlineData("/alliance-emblem/not-a-guid.png")]
        [InlineData("/alliance-emblem/not-a-guid.png?e=1-0-0-0-0-0-0")]
        public void A_url_that_names_no_crest_does_not_parse_to_one(string? url)
        {
            // These get a 404 rather than a picture, and that is safe precisely
            // because nothing ever PUBLISHED one of them: every url this server
            // puts in an alliance payload is "<uid>.png".
            Assert.False(EmblemUrlPolicy.TryParseRequest(url, out _, out _, out _, out _, out _));
        }

        [Fact]
        public void The_preview_url_is_relative_so_it_follows_whatever_origin_the_page_came_from()
        {
            string url = EmblemUrlPolicy.PreviewUrl(Spec("2-0-0-0-0-0-0"));

            Assert.Equal("/alliance-emblem/preview.png?e=2-0-0-0-0-0-0", url);

            // Root-relative, not scheme-qualified: the builder page is served by
            // this same process, so the browser resolves it against whatever
            // origin the operator actually reached us on. (Not asserted via
            // Uri.TryCreate - on Unix a rooted path parses as an absolute file
            // URI, which would make the assertion mean the opposite of this.)
            Assert.StartsWith("/", url, StringComparison.Ordinal);
            Assert.DoesNotContain("://", url, StringComparison.Ordinal);
        }

        // ----------------------------------------------------------- the vector

        [Fact]
        public void The_same_path_with_an_svg_extension_asks_for_the_vector()
        {
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/" + Alliance.ToString("D") + ".svg?e=2-2-3-4-5-6-7",
                out Guid id, out EmblemArtwork spec, out bool hasCode, out EmblemUrlPolicy.Format format,
                out _));

            Assert.Equal(Alliance, id);
            Assert.True(hasCode);
            Assert.Equal(EmblemUrlPolicy.Format.Svg, format);
            Assert.Equal("2-2-3-4-5-6-7", spec.ToCode());
        }

        [Fact]
        public void The_url_the_game_is_given_is_never_the_vector_one()
        {
            // The client decodes PNG and JPEG only, and does not check whether it
            // worked - it would DISPLAY an SVG body as a garbage texture. So the
            // .svg route existing must not leak into anything the client is handed.
            string wire = EmblemUrlPolicy.Resolve("https://wareborn.example", Alliance, "");

            Assert.EndsWith(".png?e=" + EmblemSpec.DefaultFor(Alliance).ToCode(), wire,
                StringComparison.Ordinal);
            Assert.DoesNotContain(".svg", wire, StringComparison.Ordinal);
            Assert.DoesNotContain(".svg", EmblemUrlPolicy.PreviewUrl(Spec("2-0-0-0-0-0-0")),
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_vector_url_names_the_alliance_or_the_preview()
        {
            EmblemSpec spec = Spec("2-1-2-3-4-5-6");

            Assert.Equal(
                "/alliance-emblem/" + Alliance.ToString("D") + ".svg?e=2-1-2-3-4-5-6",
                EmblemUrlPolicy.VectorUrl(Alliance, spec));

            Assert.Equal(
                "/alliance-emblem/preview.svg?e=2-1-2-3-4-5-6",
                EmblemUrlPolicy.VectorUrl(Guid.Empty, spec));

            // The saved filename carries the code, so two crests a player downloads
            // never land on top of each other in their downloads folder.
            Assert.Equal("alliance-crest-2-1-2-3-4-5-6.svg", EmblemUrlPolicy.VectorFileName(spec));
        }

        // ----------------------------------------------------------- the raster

        [Fact]
        public void The_png_url_names_the_size_and_the_alliance_or_the_preview()
        {
            EmblemSpec spec = Spec("2-1-2-3-4-5-6");

            Assert.Equal(
                "/alliance-emblem/" + Alliance.ToString("D") + ".png?e=2-1-2-3-4-5-6&s=1024",
                EmblemUrlPolicy.RasterUrl(Alliance, spec, 1024));

            Assert.Equal(
                "/alliance-emblem/preview.png?e=2-1-2-3-4-5-6&s=512",
                EmblemUrlPolicy.RasterUrl(Guid.Empty, spec, 512));

            // The size is in the FILENAME too. Three downloads of one crest that
            // differed only by a "(1)" the browser added would leave a player
            // opening files to find out which is which.
            Assert.Equal("alliance-crest-2-1-2-3-4-5-6-1024.png",
                EmblemUrlPolicy.RasterFileName(spec, 1024));
        }

        [Theory]
        [InlineData(256)]
        [InlineData(512)]
        [InlineData(1024)]
        public void A_size_the_route_offers_is_read_back_off_the_query(int offered)
        {
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/preview.png?e=2-0-0-0-0-0-0&s=" + offered,
                out _, out _, out bool hasCode, out EmblemUrlPolicy.Format format, out int size));

            Assert.True(hasCode);
            Assert.Equal(EmblemUrlPolicy.Format.Png, format);
            Assert.Equal(offered, size);
        }

        [Theory]
        [InlineData("")]
        [InlineData("&s=")]
        [InlineData("&s=0")]
        [InlineData("&s=1")]
        [InlineData("&s=257")]
        [InlineData("&s=2048")]
        [InlineData("&s=4096")]
        [InlineData("&s=99999999999999999999")]
        [InlineData("&s=-1024")]
        [InlineData("&s=1e3")]
        [InlineData("&s=%201024")]
        [InlineData("&s=1024px")]
        [InlineData("&s=nonsense")]
        public void Any_size_the_route_does_not_offer_renders_the_crest_size(string tail)
        {
            // AN ALLOWLIST, NOT A CLAMP. A clamp would answer s=4096 with the most
            // expensive picture it is willing to draw, from anybody, unauthenticated
            // - the cost of a render is the square of the edge length. Everything
            // that is not one of the three offered sizes is the size the game asks
            // for, which is also the cheapest.
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/preview.png?e=2-0-0-0-0-0-0" + tail,
                out _, out _, out _, out _, out int size));

            Assert.Equal(EmblemUrlPolicy.DefaultSize, size);
            Assert.True(EmblemUrlPolicy.IsOfferedSize(size));
        }

        [Fact]
        public void A_size_on_a_vector_request_is_dropped()
        {
            // A vector has no pixels. Carrying the size into an .svg request would
            // put it in that document's ETag and split one byte-identical body
            // across three cache entries.
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/preview.svg?e=2-0-0-0-0-0-0&s=1024",
                out _, out _, out _, out EmblemUrlPolicy.Format format, out int size));

            Assert.Equal(EmblemUrlPolicy.Format.Svg, format);
            Assert.Equal(EmblemUrlPolicy.DefaultSize, size);
        }

        [Fact]
        public void The_url_the_game_is_given_carries_no_size_at_all()
        {
            // The client holds this string and re-fetches it. Adding a parameter
            // to it would mint a second address for the crest every alliance is
            // already wearing, and every cached copy in every player's BestHTTP
            // store would miss on it once.
            string wire = EmblemUrlPolicy.Resolve("http://wareborn.example", Alliance, "");

            Assert.DoesNotContain("&s=", wire, StringComparison.Ordinal);
            Assert.DoesNotContain("?s=", wire, StringComparison.Ordinal);
            Assert.DoesNotContain("s=", EmblemUrlPolicy.PreviewUrl(Spec("2-0-0-0-0-0-0")),
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_design_code_too_long_to_be_a_filename_is_left_out_of_it()
        {
            // Twenty layers is 262 characters, and every mainstream filesystem
            // stops at 255 BYTES for a name. A browser handed a longer one
            // truncates it silently, leaving something that still looks like a
            // design code and no longer is one.
            List<EmblemLayer> layers = new List<EmblemLayer>();
            for (int i = 0; i < EmblemStack.MaxLayers; i++)
            {
                Assert.True(EmblemLayer.TryCreate(
                    i, 0, 0, 500, 0, 0, EmblemLayer.OpacitySteps, false, false, false,
                    out EmblemLayer layer));
                layers.Add(layer);
            }

            Assert.True(EmblemStack.TryCreate(layers, out EmblemStack stack));
            EmblemArtwork artwork = EmblemArtwork.Of(stack);

            Assert.Equal("alliance-crest-1024.png", EmblemUrlPolicy.RasterFileName(artwork, 1024));
            Assert.Equal("alliance-crest.svg", EmblemUrlPolicy.VectorFileName(artwork));

            foreach (int size in EmblemUrlPolicy.DownloadSizes)
            {
                Assert.True(EmblemUrlPolicy.RasterFileName(artwork, size).Length < 255);
            }
        }
        // ------------------------------------------------------- content encoding

        /// <summary>
        /// The catalogue is the one body on this server worth compressing, and the
        /// one place a wrong answer here is expensive: it is served at an immutable
        /// URL, so a caller handed gzip it cannot read would cache the unreadable
        /// copy for a year.
        /// </summary>
        [Theory]
        [InlineData("gzip", true)]
        [InlineData("gzip, deflate, br", true)]
        [InlineData("deflate, gzip;q=1.0, *;q=0.5", true)]
        [InlineData("GZIP", true)]
        [InlineData("  gzip  ", true)]
        [InlineData("deflate, br", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        // A token that merely ENDS in gzip is a different encoding.
        [InlineData("x-gzip", false)]
        [InlineData("notgzip", false)]
        // And this is how a client says "not gzip, thank you".
        [InlineData("gzip;q=0", false)]
        [InlineData("gzip;q=0.0", false)]
        [InlineData("gzip;q=0.001", true)]
        public void Gzip_is_offered_only_where_it_was_actually_asked_for(
            string? header, bool accepted)
        {
            Assert.Equal(accepted, EmblemUrlPolicy.AcceptsGzip(header));
        }

        /// <summary>
        /// And the compressed body is the SAME document - not a stale one built from
        /// an earlier catalogue, which is the failure a second cached copy invites.
        /// </summary>
        [Fact]
        public void The_compressed_catalogue_unpacks_to_the_catalogue()
        {
            using MemoryStream packed = new MemoryStream(EmblemEditorData.CatalogueGzip);
            using GZipStream gzip = new GZipStream(packed, CompressionMode.Decompress);
            using StreamReader reader = new StreamReader(gzip);

            Assert.Equal(EmblemEditorData.Catalogue, reader.ReadToEnd());

            // Worth having at all: a megabyte of coordinates should fold hard.
            Assert.True(
                EmblemEditorData.CatalogueGzip.Length * 2 < EmblemEditorData.Catalogue.Length,
                "the catalogue compressed to " + EmblemEditorData.CatalogueGzip.Length
                + " bytes from " + EmblemEditorData.Catalogue.Length);
        }
    }
}
