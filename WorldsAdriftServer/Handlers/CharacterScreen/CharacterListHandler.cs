using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Objects.CharacterSelection;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers.CharacterScreen
{
    internal static class CharacterListHandler
    {
        /*
         * URL: /characterList/{buildNumber}/steam/1234
         *
         * once the user clicks on the play button the game requests a list of characters.
         * the response also decides whether there is an option to create a new character using the unlockedSlots field
         *
         * The "1234" is hardcoded by the client for every player, so the account
         * comes from the Security header instead - see Accounts.SecurityHeader.
         */
        internal static void HandleCharacterListRequest(HttpSession session, HttpRequest request, string serverIdentifier )
        {
            AccountRecord? account = CharacterRequest.Authorize(session, request, "character list");
            if (account == null)
            {
                return;
            }

            List<CharacterCreationData> list = AccountRosters.Load(account, serverIdentifier);
            CharacterListResponse characterList = RosterPolicy.ToResponse(list);

            JObject respO = (JObject)JToken.FromObject(characterList);
            if (respO != null)
            {
                HttpResponse resp = new HttpResponse();
                resp.SetBegin(200);
                resp.SetBody(respO.ToString());

                session.SendResponseAsync(resp);
            }
        }
    }
}
