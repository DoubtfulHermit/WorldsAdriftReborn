using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Objects.SteamObjects;

namespace WorldsAdriftServer.Handlers.Authentication
{
    internal static class SteamAuthenticationHandler
    {
        internal static void HandleAuthRequest(HttpSession session, HttpRequest request, string playerName)
        {
            JObject reqO = JObject.Parse(request.Body);
            if (reqO != null)
            {
                SteamAuthRequestToken reqToken = reqO.ToObject<SteamAuthRequestToken>();

                if (reqToken != null)
                {
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
                    //   - a SUCCESS must carry a non-empty screenName, which is read
                    //     unguarded on the password path.
                    bool hasPassword = reqToken.bossaCredential != null
                        && !string.IsNullOrWhiteSpace(reqToken.bossaCredential.userKey)
                        && !string.IsNullOrWhiteSpace(reqToken.bossaCredential.secret);

                    if (!hasPassword)
                    {
                        Console.WriteLine("[info] /authenticate with no password; asking the client to show its login form.");

                        SteamAuthResponseToken noAccount = new SteamAuthResponseToken(null, null, null, false);
                        noAccount.desc = "no_bossa_registration";

                        HttpResponse noAccountResp = new HttpResponse();
                        noAccountResp.SetBegin(200);
                        noAccountResp.SetBody(((JObject)JToken.FromObject(noAccount)).ToString());
                        session.SendResponseAsync(noAccountResp);
                        return;
                    }

                    Console.WriteLine("[info] /authenticate from '" + reqToken.bossaCredential.userKey
                        + "' (password login). Accounts are not implemented yet - accepting.");

                    SteamAuthResponseToken respToken = new SteamAuthResponseToken("superCoolToken", "777", "999", true);
                    respToken.screenName = playerName;

                    JObject respO = (JObject)JToken.FromObject(respToken);
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
    }
}
