using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Authentication
{
    /// <summary>
    /// Sign-up: the page a new player fills in, and the endpoint behind it.
    ///
    /// This is ours, not the game's. The client has a login form but no way to
    /// create an account - upstream sent players to a Bossa web signup that has
    /// been gone since 2019 - so registration happens in a browser and the
    /// credentials are then typed into the game's own form.
    /// </summary>
    internal static class RegistrationHandler
    {
        /// <summary>Serves the sign-up page.</summary>
        internal static void HandleSignupPage(HttpSession session)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", SignupPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(SignupPage.Html);
            session.SendResponseAsync(resp);
        }

        /// <summary>
        /// Creates an account from {"username": ..., "password": ...}.
        ///
        /// Every rejection carries a message meant to be read by the player, in
        /// "error" - the page shows it verbatim. The one exception is a duplicate
        /// username, which has to say so plainly: a sign-up page that hides which
        /// names are taken is a sign-up page nobody can complete.
        /// </summary>
        internal static void HandleRegister(HttpSession session, HttpRequest request)
        {
            string? username = null;

            try
            {
                JObject? body = string.IsNullOrWhiteSpace(request.Body)
                    ? null
                    : JObject.Parse(request.Body);

                username = body?["username"]?.Value<string>();
                string? password = body?["password"]?.Value<string>();

                if (!AccountPolicy.IsUsableUsername(username))
                {
                    Reject(session, 400,
                        "Pick a name between " + AccountPolicy.MinUsernameLength + " and "
                        + AccountPolicy.MaxUsernameLength + " characters, using letters, "
                        + "digits, or @ . + _ - only.");
                    return;
                }

                if (!AccountPolicy.IsUsablePassword(password))
                {
                    Reject(session, 400,
                        "Pick a password of at least " + AccountPolicy.MinPasswordLength
                        + " characters.");
                    return;
                }

                // No Steam id is ever stored. Accounts here are deliberately
                // independent of Steam: the game cannot be bought any more, so
                // requiring a Steam identity would lock out players who have a
                // copy but no entitlement on the account they are using.
                AccountRecord? created = Persistence.Accounts.Repository.Create(
                    username!,
                    username!,
                    password!,
                    null,
                    DateTimeOffset.UtcNow);

                if (created == null)
                {
                    Reject(session, 409, "That name is already taken.");
                    return;
                }

                Console.WriteLine("[info] new account '" + created.Username + "' (id "
                    + created.AccountId + ").");

                JObject ok = new JObject
                {
                    ["success"] = true,
                    ["username"] = created.Username,
                };

                HttpResponse resp = new HttpResponse();
                resp.SetBegin(200);
                resp.SetHeader("Content-Type", "application/json");
                resp.SetBody(ok.ToString());
                session.SendResponseAsync(resp);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Reject(session, 400, "That request was not readable JSON.");
            }
            catch (Exception e)
            {
                // Never let the page hang: it waits on this response.
                Console.WriteLine("[error] /register failed for '" + username + "': " + e);
                Reject(session, 500, "The server could not create the account. Try again shortly.");
            }
        }

        private static void Reject(HttpSession session, int status, string message)
        {
            JObject body = new JObject
            {
                ["success"] = false,
                ["error"] = message,
            };

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", "application/json");
            resp.SetBody(body.ToString());
            session.SendResponseAsync(resp);
        }
    }
}
