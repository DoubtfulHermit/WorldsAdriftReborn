using WorldsAdriftServer.Emblems;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Web;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The builder's posted form, and the player-session CSRF token that guards
    /// it - the account page is the first thing behind the <c>wa_player</c> cookie
    /// that changes state, and everything before it only ever read.
    /// </summary>
    public class EmblemFormPolicyTests
    {
        private static Dictionary<string, string> Good() => new Dictionary<string, string>
        {
            [EmblemFormPolicy.AllianceField] = "2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10",
            [EmblemFormPolicy.CharacterField] = "7a1c9d44-6b2e-4f10-8e33-11b0c4f9a2d7",
            [EmblemFormPolicy.ShapeField] = "2",
            [EmblemFormPolicy.DivisionField] = "5",
            [EmblemFormPolicy.ChargeField] = "7",
            [EmblemFormPolicy.FieldColourField] = "11",
            [EmblemFormPolicy.DetailColourField] = "3",
            [EmblemFormPolicy.ChargeColourField] = "13",
        };

        /// <summary>The layered editor's form: the two actors and ONE design.</summary>
        private static Dictionary<string, string> Design(string code) => new Dictionary<string, string>
        {
            [EmblemFormPolicy.AllianceField] = "2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10",
            [EmblemFormPolicy.CharacterField] = "7a1c9d44-6b2e-4f10-8e33-11b0c4f9a2d7",
            [EmblemFormPolicy.DesignField] = code,
        };

        private static string OneLayer()
        {
            Assert.True(EmblemLayer.TryCreate(4, -250, 125, 700, 30, 9, 32, true, false, true,
                out EmblemLayer layer));
            Assert.True(EmblemStack.TryCreate(new[] { layer }, out EmblemStack stack));
            return stack.ToCode();
        }

        // ------------------------------------------------------ the layered form

        [Fact]
        public void A_posted_design_is_read_as_the_whole_emblem()
        {
            string code = OneLayer();
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(Design(code));

            Assert.True(outcome.Ok);
            Assert.True(outcome.Artwork.IsLayered);
            Assert.Equal(code, outcome.Artwork.ToCode());
        }

        /// <summary>
        /// AN EMPTY DESIGN IS REFUSED. A crest with no layers is fully transparent,
        /// and in game that is indistinguishable from a crest that failed to
        /// download - so an alliance that saved one would look broken to everybody
        /// including itself, with no way to tell which it was.
        /// </summary>
        [Fact]
        public void A_design_with_no_layers_is_refused_with_a_reason()
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(Design("3-"));

            Assert.False(outcome.Ok);
            Assert.Contains("at least one layer", outcome.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("")]
        [InlineData("3-0")]
        [InlineData("3-00000000000-0")]
        [InlineData("nonsense")]
        public void A_design_the_editor_could_not_have_produced_is_refused(string code)
        {
            Assert.False(EmblemFormPolicy.Read(Design(code)).Ok);
        }

        [Fact]
        public void An_absurdly_long_design_is_refused_before_it_is_parsed()
        {
            Assert.False(EmblemFormPolicy.Read(Design("3-" + new string('0', 100_000))).Ok);
        }

        /// <summary>
        /// A design field WINS over the older six. The heraldic branch is kept only
        /// so a page that was already open when this shipped still saves; a post
        /// carrying both came from something that is not either builder.
        /// </summary>
        [Fact]
        public void A_design_field_is_preferred_over_the_older_choices()
        {
            Dictionary<string, string> form = Good();
            form[EmblemFormPolicy.DesignField] = OneLayer();

            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(form);

            Assert.True(outcome.Ok);
            Assert.True(outcome.Artwork.IsLayered);
        }

        /// <summary>
        /// And the older form still works on its own, because somebody's tab has
        /// been open since before this shipped.
        /// </summary>
        [Fact]
        public void A_form_from_the_older_builder_is_still_accepted()
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(Good());

            Assert.True(outcome.Ok);
            Assert.False(outcome.Artwork.IsLayered);
        }

        [Fact]
        public void A_complete_form_is_accepted()
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(Good());

            Assert.True(outcome.Ok);
            Assert.Equal(Guid.Parse("2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10"), outcome.AllianceId);
            Assert.Equal(Guid.Parse("7a1c9d44-6b2e-4f10-8e33-11b0c4f9a2d7"), outcome.CharacterUid);
            Assert.Equal("2-2-5-7-11-3-13", outcome.Artwork.ToCode());
        }

        [Fact]
        public void A_null_form_is_refused_with_a_reason()
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(null);

            Assert.False(outcome.Ok);
            Assert.NotEqual(string.Empty, outcome.Reason);
        }

        [Theory]
        [InlineData(EmblemFormPolicy.AllianceField)]
        [InlineData(EmblemFormPolicy.CharacterField)]
        [InlineData(EmblemFormPolicy.ShapeField)]
        [InlineData(EmblemFormPolicy.DivisionField)]
        [InlineData(EmblemFormPolicy.ChargeField)]
        [InlineData(EmblemFormPolicy.FieldColourField)]
        [InlineData(EmblemFormPolicy.DetailColourField)]
        [InlineData(EmblemFormPolicy.ChargeColourField)]
        public void Every_field_is_required(string missing)
        {
            Dictionary<string, string> form = Good();
            form.Remove(missing);

            Assert.False(EmblemFormPolicy.Read(form).Ok);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-guid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("2f9b6f2e1c314f4a9a3e8d0f9c6b7a10' OR 1=1--")]
        public void An_unreadable_or_empty_id_is_refused(string id)
        {
            Dictionary<string, string> alliance = Good();
            alliance[EmblemFormPolicy.AllianceField] = id;
            Assert.False(EmblemFormPolicy.Read(alliance).Ok);

            Dictionary<string, string> character = Good();
            character[EmblemFormPolicy.CharacterField] = id;
            Assert.False(EmblemFormPolicy.Read(character).Ok);
        }

        [Theory]
        [InlineData(EmblemFormPolicy.ShapeField, "5")]
        [InlineData(EmblemFormPolicy.DivisionField, "10")]
        [InlineData(EmblemFormPolicy.ChargeField, "61")]
        // Past any palette this vocabulary will grow to - see the note in
        // EmblemSpecTests on why "one past today's count" rots.
        [InlineData(EmblemFormPolicy.FieldColourField, "900")]
        [InlineData(EmblemFormPolicy.DetailColourField, "900")]
        [InlineData(EmblemFormPolicy.ChargeColourField, "900")]
        [InlineData(EmblemFormPolicy.ShapeField, "-1")]
        [InlineData(EmblemFormPolicy.ShapeField, "999")]
        [InlineData(EmblemFormPolicy.ShapeField, "1e3")]
        [InlineData(EmblemFormPolicy.ShapeField, " 1")]
        [InlineData(EmblemFormPolicy.ShapeField, "1.0")]
        [InlineData(EmblemFormPolicy.ShapeField, "")]
        [InlineData(EmblemFormPolicy.ShapeField, "99999999999999999")]
        public void A_choice_outside_the_vocabulary_is_refused_not_clamped(string field, string value)
        {
            Dictionary<string, string> form = Good();
            form[field] = value;

            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(form);

            Assert.False(outcome.Ok);
            Assert.Equal(Guid.Empty, outcome.AllianceId);
        }

        // ------------------------------------------------------------ the token

        [Fact]
        public void A_player_csrf_token_is_bound_to_its_session()
        {
            string token = PlayerAuthPolicy.CsrfTokenForSession("session-a");

            Assert.NotEqual(string.Empty, token);
            Assert.True(PlayerAuthPolicy.VerifyCsrf("session-a", token));
            Assert.False(PlayerAuthPolicy.VerifyCsrf("session-b", token));
        }

        [Fact]
        public void A_player_token_is_not_an_admin_token()
        {
            // The domain strings differ ON PURPOSE. If they did not, a token
            // minted for this page would be accepted by the operator endpoints,
            // which share the same session-token shape.
            Assert.NotEqual(
                PlayerAuthPolicy.CsrfTokenForSession("shared"),
                AdminAuthPolicy.CsrfTokenForSession("shared"));

            Assert.False(PlayerAuthPolicy.VerifyCsrf(
                "shared", AdminAuthPolicy.CsrfTokenForSession("shared")));
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("session", null)]
        [InlineData("session", "")]
        [InlineData(null, "token")]
        [InlineData("", "")]
        [InlineData("session", "0000000000000000000000000000000000000000000000000000000000000000")]
        public void A_missing_or_wrong_token_never_verifies(string? session, string? presented)
        {
            Assert.False(PlayerAuthPolicy.VerifyCsrf(session, presented));
        }
    }
}
