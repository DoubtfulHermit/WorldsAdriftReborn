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

            HttpResponse resp = new HttpResponse();
            CharacterAuthResponse authResp = new CharacterAuthResponse("token", "1", 123, "12.12.12", true);

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
