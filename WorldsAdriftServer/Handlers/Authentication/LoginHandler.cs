using NetCoreServer;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Authentication
{
    /// <summary>
    /// The browser sign-in: the page a returning player fills in, and the endpoint
    /// behind it. It is the read-side twin of <see cref="RegistrationHandler"/> -
    /// same self-contained themed page, same JSON body shape - and it exists so a
    /// player can reach the login-gated download page without going through the
    /// game client.
    ///
    /// Glue only: it matches the routes, asks <see cref="Accounts.Repository"/> to
    /// verify the credentials (constant-time PBKDF2, already), mints a browser
    /// session through <see cref="PlayerAuth.Sessions"/> and writes the cookie. It
    /// deliberately reuses <see cref="AdminHandler.ParseForm"/> nowhere: the body
    /// is JSON, exactly as /register reads it, not form-encoded.
    /// </summary>
    internal static class LoginHandler
    {
        /// <summary>
        /// Handles GET/POST <c>/login</c> (and <c>/login/</c>). Returns true if it
        /// took the request so the router does not fall through.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            int q = url.IndexOf('?');
            string path = q >= 0 ? url.Substring(0, q) : url;

            if (path != "/login" && path != "/login/")
            {
                return false;
            }

            if (request.Method == "GET")
            {
                HandleLoginPage(session, request);
                return true;
            }

            if (request.Method == "POST")
            {
                HandleLogin(session, request);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Serves the sign-in page - unless the visitor already carries a live
        /// session cookie, in which case there is nothing to sign into and they are
        /// sent straight to the download page.
        /// </summary>
        private static void HandleLoginPage(HttpSession session, HttpRequest request)
        {
            if (ResolveAccountId(request) != null)
            {
                Redirect(session, "/download");
                return;
            }

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(200);
            resp.SetHeader("Content-Type", LoginPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(LoginPage.Html);
            session.SendResponseAsync(resp);
        }

        /// <summary>
        /// Verifies <c>{"username": ..., "password": ...}</c> and, on success, arms
        /// a session cookie and tells the page to go to /download.
        ///
        /// The failure answer is deliberately one generic 401 whether the username
        /// is unknown or the password is wrong - the same rule /authenticate
        /// follows - so this cannot be used to enumerate accounts. The body-parse
        /// and unexpected-error branches mirror /register exactly.
        /// </summary>
        private static void HandleLogin(HttpSession session, HttpRequest request)
        {
            string? username = null;

            try
            {
                JObject? body = string.IsNullOrWhiteSpace(request.Body)
                    ? null
                    : JObject.Parse(request.Body);

                username = body?["username"]?.Value<string>();
                string? password = body?["password"]?.Value<string>();

                // Repository.Verify is constant-time (PBKDF2 against the real hash
                // on a hit, a dummy hash on a miss), so we hand it whatever arrived
                // and never branch on which half was wrong.
                AccountRecord? account = Accounts.Repository.Verify(username, password);

                if (account == null)
                {
                    // Same answer for an unknown username and a wrong password.
                    Reject(session, 401, "Incorrect username or password.");
                    return;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;
                string token = PlayerAuth.Sessions.Issue(account.AccountId, now);
                Accounts.Repository.TouchLastLogin(account.AccountId, now);

                string cookie = PlayerAuthPolicy.BuildSessionCookie(
                    token, PlayerAuth.Sessions.LifetimeSeconds);

                Console.WriteLine("[info] '" + account.Username + "' signed in to the web download page (account "
                    + account.AccountId + ").");

                JObject ok = new JObject
                {
                    ["ok"] = true,
                    ["redirect"] = "/download",
                };

                HttpResponse resp = new HttpResponse();
                resp.SetBegin(200);
                resp.SetHeader("Content-Type", "application/json");
                resp.SetHeader("Cache-Control", "no-store");
                resp.SetHeader("Set-Cookie", cookie);
                resp.SetBody(ok.ToString());
                session.SendResponseAsync(resp);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                Reject(session, 400, "That request was not readable JSON.");
            }
            catch (Exception e)
            {
                // A database that is down must not look like a wrong password, and
                // the page waits on this response - never let it hang.
                Console.WriteLine("[error] /login failed for '" + username + "': " + e);
                Reject(session, 500, "The server could not sign you in. Try again shortly.");
            }
        }

        /// <summary>
        /// The account behind a request's <c>wa_player</c> cookie, or null. Shared
        /// by the login page's already-signed-in shortcut and the download gate.
        /// </summary>
        internal static long? ResolveAccountId(HttpRequest request)
        {
            string? cookie = HeaderValue(request, "Cookie");
            string? token = PlayerAuthPolicy.TokenFromCookieHeader(cookie);
            return PlayerAuth.Sessions.Resolve(token, DateTimeOffset.UtcNow);
        }

        private static void Reject(HttpSession session, int status, string message)
        {
            // Same body shape /register rejects with: {success:false, error:...}.
            JObject body = new JObject
            {
                ["success"] = false,
                ["error"] = message,
            };

            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", "application/json");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(body.ToString());
            session.SendResponseAsync(resp);
        }

        private static void Redirect(HttpSession session, string location)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(302);
            resp.SetHeader("Location", location);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(string.Empty);
            session.SendResponseAsync(resp);
        }

        private static string? HeaderValue(HttpRequest request, string name)
        {
            for (int i = 0; i < request.Headers; i++)
            {
                (string header, string value) = request.Header(i);
                if (string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return null;
        }
    }
}
