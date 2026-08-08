using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Objects.CharacterSelection;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers.CharacterScreen
{
    internal static class CharacterSaveHandler
    {
        /// <summary>
        /// Stores a character and replies with the whole roster.
        ///
        /// The reply is not a formality. The client feeds this response through
        /// LobbySystem.RefreshCharactersFromJObject - the same reader it uses for
        /// the character list - and only advances past the creation screen if
        /// that returns true (LobbySystem.cs:429-435). The previous "{}" reply
        /// failed that parse.
        /// </summary>
        internal static void HandleCharacterSave(HttpSession session, HttpRequest request, string serverIdentifier)
        {
            HttpResponse resp = new HttpResponse();

            try
            {
                AccountRecord? account = CharacterRequest.Authorize(session, request, "character save");
                if (account == null)
                {
                    return;
                }

                JObject reqO = JObject.Parse(request.Body);
                CharacterCreationData? characterData = reqO?.ToObject<CharacterCreationData>();

                if (characterData == null)
                {
                    Console.WriteLine("[error] character save had no readable body; ignoring.");
                    resp.SetBegin(400);
                    resp.SetBody("{}");
                    session.SendResponseAsync(resp);
                    return;
                }

                List<CharacterCreationData> roster = AccountRosters.Save(account, characterData, serverIdentifier);

                JObject respO = (JObject)JToken.FromObject(RosterPolicy.ToResponse(roster));

                resp.SetBegin(200);
                resp.SetBody(respO.ToString());
                session.SendResponseAsync(resp);
            }
            catch (Exception e)
            {
                // Never leave the client without a response - it waits on this
                // before leaving the character screen.
                Console.WriteLine("[error] character save failed: " + e);
                resp.SetBegin(500);
                resp.SetBody("{}");
                session.SendResponseAsync(resp);
            }
        }
    }
}
