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
            EmblemSpec spec = Spec("1-2-5-7-11-3-13");

            string stored = EmblemUrlPolicy.Store(spec);
            Assert.Equal("wareborn:emblem:1-2-5-7-11-3-13", stored);

            Assert.True(EmblemUrlPolicy.TryReadStored(stored, out EmblemSpec back));
            Assert.Equal(spec, back);
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
                "https://wareborn.example/", Alliance, EmblemUrlPolicy.Store(Spec("1-1-2-3-4-5-6")));

            Assert.Equal(
                "https://wareborn.example/alliance-emblem/"
                + Alliance.ToString("D") + ".png?e=1-1-2-3-4-5-6",
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
                "/alliance-emblem/" + Alliance.ToString("D") + ".png?e=1-2-3-4-5-6-7",
                out Guid id, out EmblemSpec spec, out bool hasCode));

            Assert.Equal(Alliance, id);
            Assert.True(hasCode);
            Assert.Equal("1-2-3-4-5-6-7", spec.ToCode());
        }

        [Fact]
        public void The_preview_path_parses_with_no_alliance()
        {
            Assert.True(EmblemUrlPolicy.TryParseRequest(
                "/alliance-emblem/preview.png?e=1-0-0-0-0-0-0",
                out Guid id, out _, out bool hasCode));

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
            Assert.True(EmblemUrlPolicy.TryParseRequest(url, out _, out _, out bool hasCode));
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
            Assert.False(EmblemUrlPolicy.TryParseRequest(url, out _, out _, out _));
        }

        [Fact]
        public void The_preview_url_is_relative_so_it_follows_whatever_origin_the_page_came_from()
        {
            string url = EmblemUrlPolicy.PreviewUrl(Spec("1-0-0-0-0-0-0"));

            Assert.Equal("/alliance-emblem/preview.png?e=1-0-0-0-0-0-0", url);

            // Root-relative, not scheme-qualified: the builder page is served by
            // this same process, so the browser resolves it against whatever
            // origin the operator actually reached us on. (Not asserted via
            // Uri.TryCreate - on Unix a rooted path parses as an absolute file
            // URI, which would make the assertion mean the opposite of this.)
            Assert.StartsWith("/", url, StringComparison.Ordinal);
            Assert.DoesNotContain("://", url, StringComparison.Ordinal);
        }
    }
}
