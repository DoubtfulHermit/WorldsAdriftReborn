using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Objects.CharacterSelection;

namespace WorldsAdriftServer.Handlers.CharacterScreen
{
    internal static class CharacterAuthHandler
    {
        /*
         * When the player clicks on "Enter World" the game sends a request for this answer.
         * it also adds two headers: Security and characterUid. The first is the
         * session token we handed out at /authenticate; the second names the
         * character being entered with.
         */
        internal static void HandleCharacterAuth(HttpSession session, HttpRequest request )
        {
            if (CharacterRequest.Authorize(session, request, "Enter World") == null)
            {
                return;
            }

            // The token we answer with becomes BossaNetBootstrap.CharacterClientAuthToken,
            // and grepping the whole decompile that value has exactly ONE consumer:
            // SocialHelper.WebToken, which SocialRequest.DecorateRequest puts in the
            // "Security" header of every alliance and crew request.
            //
            // It used to be the literal string "token", which was harmless while
            // nothing read it. Now that we serve the social API ourselves it is the
            // only identity those requests carry, and a constant would make every
            // player look like the same caller. Echoing the caller's own live
            // session token back - the one they just proved they hold, in the
            // Security header of THIS request - makes the social API authenticate
            // exactly as the character routes already do, and changes nothing else,
            // because nothing else reads it.
            string sessionToken = Persistence.Accounts.HeaderValue(
                request, Persistence.Accounts.SecurityHeader) ?? "token";

            HttpResponse resp = new HttpResponse();
            CharacterAuthResponse authResp = new CharacterAuthResponse(sessionToken, "1", 123, "12.12.12", true);

            JObject respO = (JObject)JToken.FromObject(authResp);
            if(respO != null)
            {
                resp.SetBegin(200);
                resp.SetBody(respO.ToString());

                session.SendResponseAsync(resp);
            }
        }
    }
}
