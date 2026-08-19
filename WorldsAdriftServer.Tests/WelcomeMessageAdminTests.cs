using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The welcome message's ADMIN BOUNDARY: who may read the greeting, who may
    /// replace it, and what a replacement has to be.
    ///
    /// This is the one string on the console an operator can change that every
    /// player will read, and it is reachable from a browser with a cookie - so
    /// the property under test is that a request which is not unambiguously an
    /// authorised operator's is refused, and refused with a reason a panel can
    /// show rather than an HTML page a fetch() reads as a parse error.
    ///
    /// The decision lives in <see cref="WelcomeMessageGate"/> rather than inline
    /// in AdminHandler for the reason OperatorGate spells out: a guard written
    /// inside the handler needs an HttpSession and the live admin session set to
    /// run at all, and is therefore a guard no test can reach.
    /// </summary>
    public class WelcomeMessageGateTests
    {
        // ---- reading -------------------------------------------------------

        [Fact]
        public void An_authenticated_operator_may_read_the_greeting()
        {
            Assert.True(WelcomeMessageGate.EvaluateRead(authenticated: true).Serve);
        }

        [Fact]
        public void Reading_the_greeting_refuses_an_unauthenticated_caller()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateRead(authenticated: false);

            Assert.False(d.Serve);
            Assert.Equal(401, d.Status);
            Assert.Equal("unauthenticated", (string?)JObject.Parse(d.Refusal!)["error"]);
        }

        [Fact]
        public void Reading_asks_for_no_header_and_no_token()
        {
            // Deliberate: it changes nothing, and the very same string is served
            // unauthenticated at /welcomeMessage. A confirmation header here
            // would be theatre, and theatre is how a real gate gets weakened
            // later by somebody trimming ceremony that never bought anything.
            Assert.True(WelcomeMessageGate.EvaluateRead(authenticated: true).Serve);
        }

        // ---- writing -------------------------------------------------------

        [Fact]
        public void Writing_the_greeting_is_allowed_only_with_all_three()
        {
            Assert.True(WelcomeMessageGate.EvaluateWrite(
                authenticated: true, hasConfirmationHeader: true, csrfValid: true).Serve);
        }

        [Fact]
        public void Writing_refuses_an_unauthenticated_caller()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateWrite(
                authenticated: false, hasConfirmationHeader: true, csrfValid: true);

            Assert.False(d.Serve);
            Assert.Equal(401, d.Status);
            Assert.Equal("unauthenticated", (string?)JObject.Parse(d.Refusal!)["error"]);
        }

        [Fact]
        public void Writing_refuses_a_missing_confirmation_header()
        {
            // X-Wareborn-Admin is a NON-SIMPLE header, so a cross-origin caller
            // must preflight and this server grants no CORS permission. Without
            // it, another site could ride an operator's cookie to rewrite what
            // every player reads on arrival.
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateWrite(
                authenticated: true, hasConfirmationHeader: false, csrfValid: true);

            Assert.False(d.Serve);
            Assert.Equal(403, d.Status);
            Assert.Contains("X-Wareborn-Admin", (string?)JObject.Parse(d.Refusal!)["message"]);
        }

        [Fact]
        public void Writing_refuses_an_invalid_csrf_token()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateWrite(
                authenticated: true, hasConfirmationHeader: true, csrfValid: false);

            Assert.False(d.Serve);
            Assert.Equal(403, d.Status);
            Assert.Contains("CSRF", (string?)JObject.Parse(d.Refusal!)["message"]);
        }

        [Fact]
        public void Writing_refuses_a_caller_that_has_none_of_the_three()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateWrite(
                authenticated: false, hasConfirmationHeader: false, csrfValid: false);

            Assert.False(d.Serve);
            Assert.Equal(401, d.Status);
        }

        [Fact]
        public void The_checks_happen_in_the_command_endpoints_order()
        {
            // Session first, then the header, then the token - so a caller who is
            // simply signed out is told that, rather than being told its CSRF
            // token is wrong and sent looking in the wrong place.
            Assert.Equal("unauthenticated", (string?)JObject.Parse(
                WelcomeMessageGate.EvaluateWrite(false, false, false).Refusal!)["error"]);
            Assert.Contains("X-Wareborn-Admin", (string?)JObject.Parse(
                WelcomeMessageGate.EvaluateWrite(true, false, false).Refusal!)["message"]);
            Assert.Contains("CSRF", (string?)JObject.Parse(
                WelcomeMessageGate.EvaluateWrite(true, true, false).Refusal!)["message"]);
        }

        [Fact]
        public void Every_refusal_is_json_a_panel_can_render()
        {
            // The caller is a fetch(). An HTML page on a refusal reaches it as a
            // parse error with no clue in it.
            WelcomeMessageGate.Decision[] refusals =
            {
                WelcomeMessageGate.EvaluateRead(false),
                WelcomeMessageGate.EvaluateWrite(false, true, true),
                WelcomeMessageGate.EvaluateWrite(true, false, true),
                WelcomeMessageGate.EvaluateWrite(true, true, false),
                WelcomeMessageGate.EvaluateBody(null),
                WelcomeMessageGate.EvaluateBody("   "),
                WelcomeMessageGate.EvaluateBody(
                    new string('a', ServerConfigPolicy.MaxWelcomeMessageLength + 1)),
            };

            foreach (WelcomeMessageGate.Decision d in refusals)
            {
                Assert.False(d.Serve);
                JObject body = JObject.Parse(d.Refusal!);
                Assert.False(string.IsNullOrWhiteSpace((string?)body["error"]));
                Assert.False(string.IsNullOrWhiteSpace((string?)body["message"]));
            }
        }

        // ---- the body ------------------------------------------------------

        [Fact]
        public void An_ordinary_message_is_accepted()
        {
            Assert.True(WelcomeMessageGate.EvaluateBody(
                "Welcome aboard.\r\n\r\nMind the rigging.").Serve);
        }

        [Fact]
        public void A_missing_message_field_is_refused_by_naming_the_field()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateBody(null);

            Assert.False(d.Serve);
            Assert.Equal(400, d.Status);
            Assert.Contains("message", (string?)JObject.Parse(d.Refusal!)["message"]);
        }

        [Fact]
        public void A_blank_message_is_refused_before_it_can_reach_the_check_constraint()
        {
            // server_config CHECKs that a value is not blank, so a blank that got
            // this far would surface as a database exception on a panel button.
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateBody("   \r\n  ");

            Assert.False(d.Serve);
            Assert.Equal(400, d.Status);
            Assert.Contains("empty", (string?)JObject.Parse(d.Refusal!)["message"]);
        }

        [Fact]
        public void An_over_long_message_is_refused_by_naming_the_limit()
        {
            WelcomeMessageGate.Decision d = WelcomeMessageGate.EvaluateBody(
                new string('a', ServerConfigPolicy.MaxWelcomeMessageLength + 1));

            Assert.False(d.Serve);
            Assert.Equal(400, d.Status);
            Assert.Contains(
                ServerConfigPolicy.MaxWelcomeMessageLength.ToString(),
                (string?)JObject.Parse(d.Refusal!)["message"]);
        }
    }

    /// <summary>
    /// The welcome-message CARD as the browser actually receives it. Same
    /// discipline as <see cref="WebAssetCompositionTests"/>: the fragment file
    /// and the bytes served are two different things once the front end is
    /// composed, so these assertions read the composed page.
    /// </summary>
    public class WelcomeMessagePageTests
    {
        private static string Dashboard() =>
            AdminPage.Dashboard("{}", new string('w', 64), ReleaseWorldMap.Json);

        [Fact]
        public void The_card_and_its_script_are_both_on_the_served_page()
        {
            string html = Dashboard();

            Assert.Contains(WebAssets.ReadTrimmed("admin-welcome.html"), html,
                StringComparison.Ordinal);
            Assert.Contains("admin-welcome.js", AdminPage.AdminScriptFragments);
            Assert.Contains(WebAssets.Read("admin-welcome.js")
                    .Replace("{{csrfToken}}", new string('w', 64), StringComparison.Ordinal),
                html, StringComparison.Ordinal);
        }

        [Fact]
        public void The_save_request_carries_the_confirmation_header_and_the_csrf_token()
        {
            // Both are asserted on the COMPOSED page rather than on the file, so
            // this fails if the fragment is dropped from the load order too.
            string html = Dashboard();

            Assert.Contains("'X-Wareborn-Admin':'1'", html, StringComparison.Ordinal);
            Assert.Contains("'X-Wareborn-CSRF':CSRF", html, StringComparison.Ordinal);
            Assert.Contains("/admin/api/welcome", html, StringComparison.Ordinal);
        }

        [Fact]
        public void The_script_is_composed_after_the_fragment_that_declares_CSRF()
        {
            // The fragments are ONE closure. CSRF is a var in admin-console.js,
            // and this fragment reads it at load time; composed earlier it would
            // send the string "undefined" as the token on every save.
            string[] order = AdminPage.AdminScriptFragments;

            Assert.True(
                Array.IndexOf(order, "admin-console.js") < Array.IndexOf(order, "admin-welcome.js"),
                "admin-welcome.js must be composed after admin-console.js");
        }

        [Fact]
        public void The_public_map_composes_neither_the_card_nor_its_script()
        {
            // The public page must not be able to compose an operator fragment
            // even by accident - and this one carries a CSRF-bound write.
            Assert.DoesNotContain("admin-welcome.js", PublicMapPage.ScriptFragments);

            string html = PublicMapPage.Html("{}", "{}");
            Assert.DoesNotContain("/admin/api/welcome", html, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_top_level_name_the_fragment_adds_is_prefixed_and_unique()
        {
            // THE KNOWN FOOTGUN. Every fragment shares one closure, so a
            // duplicate top-level identifier does not break this card - it
            // silently breaks whichever other panel declared the name first.
            string[] introduced =
            {
                "WELCOME_MAX", "welcomeInFlight", "welcomeSetStatus", "welcomeSetPill",
                "welcomeRecount", "welcomeFill", "welcomeLoad", "welcomeSubmit",
            };

            foreach (string name in introduced)
            {
                Assert.StartsWith("welcome", name, StringComparison.OrdinalIgnoreCase);

                foreach (string fragment in AdminPage.AdminScriptFragments)
                {
                    if (fragment == "admin-welcome.js")
                    {
                        continue;
                    }

                    Assert.False(
                        WebAssets.Read(fragment).Contains(name, StringComparison.Ordinal),
                        "'" + name + "' also appears in " + fragment
                        + "; the console is one closure and the two declarations "
                        + "would collide");
                }
            }
        }
    }
}
