using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Social;

namespace WorldsAdriftServer.Handlers.Social
{
    /// <summary>
    /// The dead Bossa alliances service, served from here.
    ///
    /// Worlds Adrift's Social Sheet - alliances AND the crew panel players
    /// actually use - was backed by a REST host at ConfigKeys.AlliancesUrl, not
    /// by SpatialOS. The host is gone, so opening the sheet threw and
    /// SocialCharacterSheet.TriggerAllianceExceptionHandler, which is shared
    /// between alliances and crews, covered the whole sheet including the crew
    /// tab. The reconstructed contract is in docs/research/findings-social-api.md
    /// with file:line citations into the decompile.
    ///
    /// One rule dominates the shape of this file: **this client treats any
    /// non-200 as a transport failure**. HttpHelper.HandleResponseStatusCode
    /// returns early only on a literal 200 and otherwise pops "Issue connecting to
    /// server. Please try again in a bit!" at the player, discarding the body. So
    /// there is no 404 here and no 500 - a request we cannot serve is answered
    /// with 200 and success:false, which is the only channel that reaches the
    /// player as a sentence rather than as a modal about the network.
    /// </summary>
    internal static class SocialHandler
    {
        /// <summary>
        /// The region the client will put in every region-bearing path.
        ///
        /// It is not a filter we honour - there is one deployment - but it has to
        /// be accepted, and it has to match what /deploymentStatus advertises,
        /// because the client copies the chosen server's identifier into
        /// BossaNetBootstrap.CharacterRegion at character select
        /// (LobbySystem.cs:135) and never derives it again.
        /// </summary>
        internal const string Region = "community_server";

        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            if (request == null) return false;

            string url = request.Url ?? string.Empty;
            if (!SocialRoute.IsSocialUrl(url)) return false;

            // From here on the request belongs to us, whether or not we can serve
            // it. Falling through to the router's 404 would reach the player as a
            // network error dialog.
            try
            {
                Answer(session, Resolve(request, url));
            }
            catch (Exception e)
            {
                // Including database failures. An exception escaping to
                // NetCoreServer would become a dropped connection, which the
                // client reports as the same unhelpful transport modal.
                Console.WriteLine("[error] social request " + request.Method + " " + url + " failed: " + e);
                Answer(session, SocialEnvelope.Error("dynamo_read"));
            }

            return true;
        }

        private static JObject Resolve(HttpRequest request, string url)
        {
            string? token = Accounts.HeaderValue(request, Accounts.SecurityHeader);

            // "CharacterUid" on the social path, "characterUid" on
            // /authorizeCharacter. HeaderValue is case-insensitive, which is what
            // the wire says anyway.
            string? claimed = Accounts.HeaderValue(request, "CharacterUid");

            AccountRecord? account = Accounts.Resolve(request);

            IReadOnlyList<Guid> owned = account == null
                ? Array.Empty<Guid>()
                : OwnedCharacters(account.AccountId);

            SocialIdentityPolicy.Outcome identity = SocialIdentityPolicy.Authorize(
                hasSecurityHeader: token != null,
                hasLiveSession: account != null,
                claimedCharacterUid: claimed,
                charactersOnAccount: owned);

            if (!identity.Authorized)
            {
                return SocialEnvelope.Error(identity.ErrorCode!);
            }

            SocialRoute route = SocialRoute.Parse(request.Method ?? "GET", url);
            if (route.Kind == SocialRouteKind.None)
            {
                // A social URL we do not implement - every alliance endpoint past
                // listing and searching. Refused deliberately and in band rather
                // than faked: a plausible-looking alliance the UI half-accepts is
                // worse for a player than a clear refusal.
                Console.WriteLine("[info] social endpoint not implemented: "
                    + request.Method + " " + url);
                return SocialEnvelope.Error("dynamo_read");
            }

            SocialService service = new SocialService(
                Accounts.Characters, Accounts.Crews, Accounts.SocialInvites, Region);

            return service.Handle(route, identity.Character, url, request.Body);
        }

        private static IReadOnlyList<Guid> OwnedCharacters(long accountId)
        {
            List<Guid> owned = new List<Guid>();
            foreach (CharacterRecord character in Accounts.Characters.ListForAccount(accountId))
            {
                if (character.IsEmptySlot) continue;
                owned.Add(character.CharacterUid);
            }

            return owned;
        }

        /// <summary>
        /// Sends the envelope. Always 200, always a JSON object body - both of
        /// those are client requirements, not conventions. See SocialEnvelope.
        /// </summary>
        private static void Answer(HttpSession session, JObject envelope)
        {
            HttpResponse response = new HttpResponse();
            response.SetBegin(200);
            response.SetHeader("Content-Type", "application/json");
            response.SetHeader("Cache-Control", "no-store");
            response.SetBody(envelope.ToString(Newtonsoft.Json.Formatting.None));
            session.SendResponseAsync(response);
        }
    }
}
