using System.Collections.Generic;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Which origin a crest URL names.
    ///
    /// The behaviour under test is one line long and it is the whole bug: the
    /// alliance emblem never appeared in the game because the URL said https, and
    /// the game client has no TLS above 1.0 (Mono.Security.Protocol.Tls's
    /// SecurityProtocolType enum is Ssl2/Ssl3/Tls and nothing else), while the
    /// public host answers a TLS 1.0 ClientHello with a fatal protocol_version
    /// alert. The download was rejected and the panel kept its placeholder.
    ///
    /// So the assertion that matters most here is the boring-looking one:
    /// a request that arrived with a Host header produces an http:// origin.
    /// </summary>
    public class EmblemOriginTests
    {
        private const string Fallback = "https://configured.example";

        [Fact]
        public void A_direct_request_gets_the_host_it_arrived_on_over_plain_http()
        {
            Assert.Equal(
                "http://62.171.161.19:8085",
                EmblemOrigin.For("62.171.161.19:8085", null, null, Fallback));
        }

        [Fact]
        public void The_scheme_is_never_https_for_a_direct_request()
        {
            // Even when the configured fallback is https and the host looks like a
            // public name. This listener speaks plain http; the caller reached it
            // that way; anything else is a guess, and the guess is the bug.
            string origin = EmblemOrigin.For("wareborn.example", null, null, Fallback);

            Assert.StartsWith("http://", origin, StringComparison.Ordinal);
            Assert.DoesNotContain("https", origin, StringComparison.Ordinal);
        }

        [Fact]
        public void A_proxied_request_gets_the_forwarded_host_and_scheme()
        {
            Assert.Equal(
                "https://wareborn.example",
                EmblemOrigin.For("127.0.0.1:8080", "wareborn.example", "https", Fallback));

            Assert.Equal(
                "http://wareborn.example",
                EmblemOrigin.For("127.0.0.1:8080", "wareborn.example", "http", Fallback));
        }

        [Fact]
        public void A_proxy_that_did_not_say_the_scheme_is_assumed_to_have_terminated_tls()
        {
            // A proxy is the only reason X-Forwarded-Host exists, and a proxy that
            // is not terminating TLS is the unusual case. Guessing http there would
            // hand out a URL that 308-redirects to https, which is worse than
            // naming https outright because it looks like it works.
            Assert.Equal(
                "https://wareborn.example",
                EmblemOrigin.For("127.0.0.1:8080", "wareborn.example", null, Fallback));
        }

        [Fact]
        public void The_first_entry_of_a_forwarded_host_list_wins()
        {
            Assert.Equal(
                "https://outer.example",
                EmblemOrigin.For("127.0.0.1", "outer.example, inner.example", "https", Fallback));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_host_at_all_falls_back_to_configuration(string? host)
        {
            Assert.Equal(Fallback, EmblemOrigin.For(host, null, null, Fallback));
        }

        [Fact]
        public void The_fallback_loses_its_trailing_slashes()
        {
            Assert.Equal("https://configured.example",
                EmblemOrigin.For(null, null, null, "https://configured.example///"));
        }

        /// <summary>
        /// The Host header is attacker-controlled and the string it produces is
        /// pasted into a URL that other players' clients fetch. Anything that is
        /// not a bare host[:port] is refused rather than sanitised - a half-cleaned
        /// host is how a header turns into a redirect to somewhere else.
        /// </summary>
        [Theory]
        [InlineData("evil.example/path")]
        [InlineData("evil.example\\path")]
        [InlineData("evil.example?x=1")]
        [InlineData("evil.example#frag")]
        [InlineData("user@evil.example")]
        [InlineData("evil.example:80:81")]
        [InlineData("http://evil.example")]
        [InlineData("evil example")]
        [InlineData("evil.example\r\nX-Bad: 1")]
        public void A_host_that_is_not_a_bare_host_is_refused(string host)
        {
            Assert.Equal(Fallback, EmblemOrigin.For(host, null, null, Fallback));
            Assert.Equal(Fallback, EmblemOrigin.For(null, host, "https", Fallback));
        }

        /// <summary>
        /// The one-line regression guard for the whole feature: whatever this
        /// module returns for a real game request, the URL built from it must be
        /// absolute (HttpHelper.GenerateRequest throws UriFormatException on a
        /// relative one) and must NOT be https (the client cannot speak it).
        /// </summary>
        [Fact]
        public void A_real_game_request_yields_a_url_the_client_can_actually_fetch()
        {
            Guid alliance = Guid.Parse("30f69339-7785-4a4d-90c9-d4e19fd22b98");
            string origin = EmblemOrigin.For("62.171.161.19:8085", null, null, Fallback);

            Assert.True(EmblemSpec.TryParse("2-0-7-43-9-9-4", out EmblemSpec spec));
            string url = EmblemUrlPolicy.PublicUrl(origin, alliance, spec);

            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed));
            Assert.Equal("http", parsed!.Scheme);
            Assert.Equal(
                "http://62.171.161.19:8085/alliance-emblem/"
                + "30f69339-7785-4a4d-90c9-d4e19fd22b98.png?e=2-0-7-43-9-9-4",
                url);
        }

        /// <summary>
        /// The boot line, and the shout.
        ///
        /// An https fallback origin has no other symptom: nothing errors, the
        /// route still serves a browser, and only the players notice - months
        /// later - that no alliance has a crest. So it gets a warning at the one
        /// moment somebody is reading the log.
        /// </summary>
        [Fact]
        public void An_https_fallback_origin_is_warned_about_at_boot()
        {
            List<string> lines = new List<string>();
            EmblemImages.ReportConfiguration("https://wareborn.example", lines.Add);

            Assert.Contains(lines, l => l.StartsWith("[info]", StringComparison.Ordinal)
                && l.Contains("https://wareborn.example", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("[warn]", StringComparison.Ordinal)
                && l.Contains(EmblemImages.BaseUrlVariable, StringComparison.Ordinal));
        }

        [Fact]
        public void A_plain_http_fallback_origin_is_reported_without_a_warning()
        {
            List<string> lines = new List<string>();
            EmblemImages.ReportConfiguration("http://62.171.161.19:8085", lines.Add);

            Assert.Single(lines);
            Assert.StartsWith("[info]", lines[0], StringComparison.Ordinal);
        }

        [Fact]
        public void An_unset_variable_is_named_in_the_boot_line()
        {
            List<string> lines = new List<string>();
            EmblemImages.ReportConfiguration(null, lines.Add);

            Assert.Contains(EmblemImages.BaseUrlVariable, lines[0], StringComparison.Ordinal);
            Assert.Contains("unset", lines[0], StringComparison.Ordinal);
        }
    }
}
