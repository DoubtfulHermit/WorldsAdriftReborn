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

        [Fact]
        public void A_complete_form_is_accepted()
        {
            EmblemFormPolicy.Outcome outcome = EmblemFormPolicy.Read(Good());

            Assert.True(outcome.Ok);
            Assert.Equal(Guid.Parse("2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10"), outcome.AllianceId);
            Assert.Equal(Guid.Parse("7a1c9d44-6b2e-4f10-8e33-11b0c4f9a2d7"), outcome.CharacterUid);
            Assert.Equal("2-2-5-7-11-3-13", outcome.Spec.ToCode());
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
        [InlineData(EmblemFormPolicy.FieldColourField, "16")]
        [InlineData(EmblemFormPolicy.DetailColourField, "16")]
        [InlineData(EmblemFormPolicy.ChargeColourField, "16")]
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
