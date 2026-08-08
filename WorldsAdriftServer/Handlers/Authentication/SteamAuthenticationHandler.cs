using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Objects.SteamObjects;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers.Authentication
{
    internal static class SteamAuthenticationHandler
    {
        /// <summary>
        /// The desc sent when the password is wrong.
        ///
        /// The client branches on exactly one value here: "bossa_account_not_validated"
        /// takes the not-verified path and reads jToken["token"] unguarded, which
        /// would be a NullReferenceException on a failure that carries no token
        /// (BossaNetBootstrap.cs:417-421). Every other non-empty string lands in
        /// LandingScreen.LoginFailed's else branch and shows the form's own
        /// "incorrect credentials" warning, which is what we want. The exact
        /// text is never displayed.
        /// </summary>
        private const string BadCredentials = "invalid_credentials";

        internal static void HandleAuthRequest(HttpSession session, HttpRequest request)
        {
            JObject reqO = JObject.Parse(request.Body);
            if (reqO == null)
            {
                return;
            }

            SteamAuthRequestToken reqToken = reqO.ToObject<SteamAuthRequestToken>();
            if (reqToken == null)
            {
                return;
            }

            // The game ships a username/password form on its landing screen and
            // builds it on every launch - it is hidden about two seconds later,
            // the moment this handler answers success. That unconditional success
            // is the ONLY reason nobody has seen it.
            //
            // Answering "no linked account" instead sends the client down
            // BossaNetBootstrap -> onAuthNoLinkedBossaAccount -> LandingScreen
            // .NoLinkedAccount -> SetLoginFormActive(true), and the form appears.
            //
            // Two rules the client enforces without guarding, so breaking either
            // gives a dead menu rather than an error:
            //   - a failure MUST carry a non-empty desc. The client's handler has
            //     no else branch, so a failure without one fires no callback at
            //     all and leaves a blank screen with no form.
            //   - a SUCCESS must carry a non-empty token, playerId, bossaId and
            //     screenName. IsSuccessResponse checks the first three and
            //     screenName is read unguarded right after.
            bool hasPassword = reqToken.bossaCredential != null
                && !string.IsNullOrWhiteSpace(reqToken.bossaCredential.userKey)
                && !string.IsNullOrWhiteSpace(reqToken.bossaCredential.secret);

            if (!hasPassword)
            {
                Console.WriteLine("[info] /authenticate with no password; asking the client to show its login form.");
                Send(session, Failure("no_bossa_registration"));
                return;
            }

            string username = reqToken.bossaCredential.userKey;

            AccountRecord? account;
            try
            {
                account = Accounts.Repository.Verify(username, reqToken.bossaCredential.secret);
            }
            catch (Exception e)
            {
                // A database that is down must not look like a wrong password:
                // the player would keep retyping a correct one. The client's
                // AuthError path shows "could not connect", which is true.
                Console.WriteLine("[error] /authenticate could not reach the account database: " + e);
                HttpResponse unavailable = new HttpResponse();
                unavailable.SetBegin(503);
                unavailable.SetBody("{}");
                session.SendResponseAsync(unavailable);
                return;
            }

            if (account == null)
            {
                // Deliberately the same answer for an unknown username and a
                // wrong password, so this cannot be used to enumerate accounts.
                Console.WriteLine("[info] /authenticate rejected for '" + username + "'.");
                Send(session, Failure(BadCredentials));
                return;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            SessionRecord issued = Accounts.Sessions.Issue(account.AccountId, now);
            Accounts.Repository.TouchLastLogin(account.AccountId, now);

            Console.WriteLine("[info] '" + account.Username + "' signed in (account "
                + account.AccountId + ").");

            // token comes back to us in the Security header on every later
            // request; playerId and bossaId only have to be present and non-empty.
            SteamAuthResponseToken respToken = new SteamAuthResponseToken(
                issued.Token,
                account.AccountId.ToString(),
                account.AccountId.ToString(),
                true);
            respToken.screenName = account.DisplayName;

            Send(session, respToken);
        }

        private static SteamAuthResponseToken Failure(string desc)
        {
            SteamAuthResponseToken token = new SteamAuthResponseToken(null, null, null, false);
            token.desc = desc;
            return token;
        }

        private static void Send(HttpSession session, SteamAuthResponseToken token)
        {
            // 200 with success=false, not 401: the client only parses a body on
            // 200 or 401, and 200 is the path both failure branches already read.
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetBody(((JObject)JToken.FromObject(token)).ToString());
            session.SendResponseAsync(resp);
        }
    }
}
