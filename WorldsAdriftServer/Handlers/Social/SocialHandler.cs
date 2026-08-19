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
            // Parsed out here so the catch below can still refuse in the shape the
            // route's own reader understands. Pure string work, and None on
            // anything unrecognised, so it cannot itself be the thing that throws.
            SocialRouteKind kind = SocialRouteKind.None;
            try
            {
                kind = SocialRoute.Parse(request.Method ?? "GET", url).Kind;
            }
            catch
            {
                // Deliberately swallowed: an unparseable URL is a refusal, not a
                // crash, and kind stays None.
            }

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
                Answer(session, SocialRefusal.For(kind, SocialErrorCodes.StoreUnavailable));
            }

            return true;
        }

        /// <summary>
        /// Thin glue: read the two headers and the account - the only parts that
        /// need I/O - then let SocialGate decide, and serve if it says so.
        /// </summary>
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

            SocialGate.Decision decision = SocialGate.Evaluate(
                method: request.Method ?? "GET",
                url: url,
                hasSecurityHeader: token != null,
                hasLiveSession: account != null,
                claimedCharacterUid: claimed,
                charactersOnAccount: owned);

            if (!decision.Serve)
            {
                if (decision.Route.Kind == SocialRouteKind.None)
                {
                    Console.WriteLine("[info] social endpoint not implemented: "
                        + request.Method + " " + url);
                }

                return decision.Refusal!;
            }

            SocialService service = new SocialService(
                Accounts.Characters, Accounts.Crews, Accounts.SocialInvites, Accounts.Alliances, Region,
                clock: null,
                // The alliance payload carries an absolute crest URL, and the game
                // client CANNOT fetch it over https - its Mono TLS stack has no
                // protocol above TLS 1.0 and the public host refuses that with a
                // protocol_version alert. So the URL is built from the origin this
                // very request arrived on, which the caller has just demonstrated
                // it can reach. See Emblems.EmblemOrigin.
                emblemBaseUrl: Emblems.EmblemOrigin.For(
                    Accounts.HeaderValue(request, "Host"),
                    Accounts.HeaderValue(request, "X-Forwarded-Host"),
                    Accounts.HeaderValue(request, "X-Forwarded-Proto"),
                    Emblems.EmblemImages.BaseUrl));

            return service.Handle(decision.Route, decision.Character, url, request.Body);
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
