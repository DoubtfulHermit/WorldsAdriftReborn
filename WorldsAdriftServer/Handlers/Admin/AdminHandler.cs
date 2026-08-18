using NetCoreServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Persistence;
using WorldsAdriftServer.Web;

namespace WorldsAdriftServer.Handlers.Admin
{
    /// <summary>
    /// The operator dashboard's HTTP surface. Every route lives under /admin and
    /// is gated on a valid session cookie except the login page and the login
    /// POST; an unauthenticated visitor to any of them is shown the login page or
    /// refused, never the data. The routing itself is the same string-matched
    /// style as <see cref="RequestRouterHandler"/> - this class is just the
    /// branch of it that /admin* takes.
    ///
    /// Decisions belong to the policy classes (<see cref="AdminAuthPolicy"/>,
    /// <see cref="AdminSessions"/>, <see cref="ServerConfigPolicy"/>); this glue
    /// only reads the request, asks them, and writes the response.
    /// </summary>
    internal static class AdminHandler
    {
        /// <summary>How many recent signups the dashboard lists.</summary>
        private const int RecentSignups = 10;

        /// <summary>
        /// Handles any request whose URL begins with /admin. Returns true if it
        /// took the request, so the router knows not to fall through.
        /// </summary>
        internal static bool TryHandle(HttpSession session, HttpRequest request)
        {
            string url = request.Url;
            string path = url;
            int q = path.IndexOf('?');
            if (q >= 0)
            {
                path = path.Substring(0, q);
            }

            if (path != "/admin" && !path.StartsWith("/admin/", StringComparison.Ordinal))
            {
                return false;
            }

            // Panel off entirely: no credential installed. Every /admin route
            // says so and nothing else - it must not fall open.
            if (!AdminConfig.IsConfigured)
            {
                Html(session, 503, AdminPage.Disabled);
                return true;
            }

            string method = request.Method;

            if (path == "/admin/login" && method == "POST")
            {
                HandleLogin(session, request);
                return true;
            }

            string? sessionToken = SessionToken(request);
            bool authed = AdminConfig.Sessions.IsValid(sessionToken, DateTimeOffset.UtcNow);

            if (path == "/admin/logout" && method == "POST")
            {
                if (!authed || !VerifyFormCsrf(request, sessionToken))
                {
                    Redirect(session, "/admin");
                    return true;
                }
                HandleLogout(session, request);
                return true;
            }

            if (path == "/admin" && method == "GET")
            {
                if (authed)
                {
                    Html(session, 200, AdminPage.Dashboard(BuildStatsJson(),
                        AdminAuthPolicy.CsrfTokenForSession(sessionToken!), ReleaseWorldMap.Json));
                }
                else
                {
                    Html(session, 200, AdminPage.Login(null));
                }
                return true;
            }

            if (path == "/admin/api/stats" && method == "GET")
            {
                if (!authed)
                {
                    Json(session, 401, "{\"error\":\"unauthenticated\"}");
                    return true;
                }

                Json(session, 200, BuildStatsJson());
                return true;
            }

            if (path == "/admin/api/command" && method == "POST")
            {
                if (!authed)
                {
                    Json(session, 401, "{\"error\":\"unauthenticated\"}");
                    return true;
                }

                HandleAdminCommand(session, request, sessionToken!);
                return true;
            }

            if (path == "/admin/server-name" && method == "POST")
            {
                if (!authed)
                {
                    Redirect(session, "/admin");
                    return true;
                }

                if (!VerifyFormCsrf(request, sessionToken))
                {
                    Redirect(session, "/admin");
                    return true;
                }

                HandleServerName(session, request);
                return true;
            }

            // Any other /admin path: unknown route. Authed sees a 404, everyone
            // else is bounced to the login page so unauth probing learns nothing.
            if (authed)
            {
                Html(session, 404, AdminPage.Login(null));
            }
            else
            {
                Html(session, 200, AdminPage.Login(null));
            }
            return true;
        }

        // ---- auth ----------------------------------------------------------

        private static string? SessionToken(HttpRequest request) =>
            AdminAuthPolicy.TokenFromCookieHeader(HeaderValue(request, "Cookie"));

        private static bool VerifyFormCsrf(HttpRequest request, string? sessionToken)
        {
            Dictionary<string, string> form = ParseForm(request.Body);
            form.TryGetValue("csrf", out string? csrf);
            return AdminAuthPolicy.VerifyCsrf(sessionToken, csrf);
        }

        private static void HandleLogin(HttpSession session, HttpRequest request)
        {
            Dictionary<string, string> form = ParseForm(request.Body);
            form.TryGetValue("username", out string? user);
            form.TryGetValue("password", out string? pass);

            if (!AdminConfig.Verify(user, pass))
            {
                // Deliberately vague: which of the two was wrong is not the
                // visitor's business.
                HtmlWithCookie(session, 401, AdminPage.Login("That operator name and passphrase were not accepted."), null);
                return;
            }

            string token = AdminConfig.Sessions.Issue(DateTimeOffset.UtcNow);
            string cookie = AdminAuthPolicy.BuildSessionCookie(token, AdminConfig.Sessions.LifetimeSeconds);

            // 303 so the browser re-GETs /admin (and drops the POST from history).
            RedirectWithCookie(session, "/admin", cookie);
        }

        private static void HandleLogout(HttpSession session, HttpRequest request)
        {
            string? token = AdminAuthPolicy.TokenFromCookieHeader(HeaderValue(request, "Cookie"));
            AdminConfig.Sessions.Revoke(token);
            RedirectWithCookie(session, "/admin", AdminAuthPolicy.BuildClearCookie());
        }

        private static void HandleServerName(HttpSession session, HttpRequest request)
        {
            Dictionary<string, string> form = ParseForm(request.Body);
            form.TryGetValue("serverName", out string? name);

            if (ServerConfigPolicy.IsValid(name))
            {
                try
                {
                    Accounts.ServerConfig.SetServerName(name, DateTimeOffset.UtcNow);
                    Console.WriteLine("[info] admin set server name to '"
                        + ServerConfigPolicy.Normalize(name) + "'.");
                }
                catch (Exception e)
                {
                    Console.WriteLine("[error] admin failed to set server name: " + e.Message);
                }
            }

            // Either way, back to the dashboard - it will show the stored value.
            Redirect(session, "/admin");
        }

        private static void HandleAdminCommand(HttpSession session, HttpRequest request,
            string sessionToken)
        {
            // This non-simple request header forces a cross-origin browser to
            // preflight. We expose no CORS permission, so another site cannot
            // use an operator's session to fire a game command.
            if (HeaderValue(request, "X-Wareborn-Admin") != "1")
            {
                CommandError(session, 403, "request", null, "Admin command confirmation header is missing.");
                return;
            }
            if (!AdminAuthPolicy.VerifyCsrf(sessionToken,
                    HeaderValue(request, AdminAuthPolicy.CsrfHeader)))
            {
                CommandError(session, 403, "request", null,
                    "The session-bound CSRF token is missing or invalid.");
                return;
            }

            Dictionary<string, string> form = ParseForm(request.Body);
            form.TryGetValue("action", out string? action);
            form.TryGetValue("target", out string? target);
            form.TryGetValue("argument", out string? argument);
            form.TryGetValue("confirmation", out string? confirmation);

            if (!AdminCommandBridge.TryBuild(action, target, argument, out AdminCommandRequest command, out string error))
            {
                CommandError(session, 400, KnownAction(action), null, error);
                return;
            }

            if (!TryReadFreshGame(out GameStatsSnapshot snapshot, out error))
            {
                CommandError(session, 503, command.Action, command.TargetEntityId, error);
                return;
            }

            if (command.Action == "ship-delete"
                && confirmation != "DELETE")
            {
                CommandError(session, 400, command.Action, command.TargetEntityId,
                    "Type DELETE exactly to confirm permanent deletion of hull "
                    + command.TargetEntityId + ".");
                return;
            }

            if (command.Action == "teleport" && command.Detail == "trades-challenge"
                && !snapshot.SecondIslandRegistered)
            {
                CommandError(session, 409, command.Action, command.TargetEntityId,
                    "The test island is not registered on the live game server.");
                return;
            }
            if (command.Action == "teleport" && command.Detail == "mental-facility"
                && snapshot.FirstRegionTerrainCount < 1)
            {
                CommandError(session, 409, command.Action, command.TargetEntityId,
                    "Mental Facility terrain is not registered on the live game server.");
                return;
            }

            if ((command.Action == "teleport" || command.Action == "placement")
                && command.TargetEntityId.HasValue
                && !IsConnectedPlayer(snapshot, command.TargetEntityId.Value, out error))
            {
                CommandError(session, 409, command.Action, command.TargetEntityId, error);
                return;
            }
            if ((command.Action == "ship-recall" || command.Action == "ship-stop"
                    || command.Action == "helm-release" || command.Action == "ship-delete")
                && command.TargetEntityId.HasValue
                && !IsShipDomain(snapshot, command.TargetEntityId.Value, out error))
            {
                CommandError(session, 409, command.Action, command.TargetEntityId, error);
                return;
            }
            if (command.Action == "ship-recall"
                && command.RelatedPlayerEntityId.HasValue
                && !IsConnectedPlayer(snapshot, command.RelatedPlayerEntityId.Value, out error))
            {
                CommandError(session, 409, command.Action, command.TargetEntityId, error);
                return;
            }

            if (!AdminCommandBridge.TryQueue(command, out error))
            {
                CommandError(session, 409, command.Action, command.TargetEntityId, error);
                return;
            }

            string targetText = command.TargetEntityId.HasValue
                ? (command.Action.StartsWith("ship-", StringComparison.Ordinal)
                    || command.Action == "helm-release"
                    ? " for hull entity " : " for player entity ") + command.TargetEntityId.Value
                : string.Empty;
            string message = "Accepted " + command.Action + targetText
                + " and handed it to the game server. This records dispatch, not gameplay completion.";
            AdminCommandEntry entry = AdminCommandJournal.Record(
                DateTimeOffset.UtcNow, command.Action, command.TargetEntityId,
                command.Detail, accepted: true, message);
            Console.WriteLine("[info] admin command accepted: " + command.Action + targetText
                + " (" + command.Detail + ").");
            Json(session, 202, entry.ToJson().ToString(Formatting.None));
        }

        private static bool TryReadFreshGame(out GameStatsSnapshot snapshot, out string error)
        {
            GameStatsResult stats = GameStats.Read(DateTimeOffset.UtcNow);
            if (stats.State != GameStatsState.Ok || stats.Snapshot == null || stats.Stale)
            {
                snapshot = null!;
                error = "Live game status is unavailable or stale; no command was queued.";
                return false;
            }

            snapshot = stats.Snapshot;
            error = string.Empty;
            return true;
        }

        private static bool IsConnectedPlayer(GameStatsSnapshot snapshot, long entityId, out string error)
        {
            foreach (GamePlayerStat player in snapshot.Players)
            {
                if (player.EntityId == entityId)
                {
                    error = string.Empty;
                    return true;
                }
            }

            error = "That player is no longer connected; refresh the player list and choose again.";
            return false;
        }

        private static bool IsShipDomain(GameStatsSnapshot snapshot, long hullEntityId,
            out string error)
        {
            foreach (GameShipDomainStat ship in snapshot.ShipDomains)
            {
                if ((long?)ship.Json["hullEntityId"] == hullEntityId)
                {
                    error = string.Empty;
                    return true;
                }
            }
            error = "That ship domain no longer exists; refresh the world inspector and choose again.";
            return false;
        }

        private static string KnownAction(string? action)
        {
            return action == "teleport" || action == "placement"
                || action == "resources-reset" || action == "ship-recall"
                || action == "ship-stop" || action == "helm-release" || action == "ship-delete"
                ? action
                : "invalid";
        }

        private static void CommandError(HttpSession session, int status, string action, long? target, string message)
        {
            AdminCommandEntry entry = AdminCommandJournal.Record(
                DateTimeOffset.UtcNow, action, target, string.Empty, accepted: false, message);
            Console.WriteLine("[warning] admin command rejected: " + action + ": " + message);
            Json(session, status, entry.ToJson().ToString(Formatting.None));
        }

        // ---- stats payload -------------------------------------------------

        /// <summary>
        /// The single JSON payload the dashboard renders from, served both as the
        /// first-paint bootstrap and by /admin/api/stats. Combines the live game
        /// snapshot (from the game server's file) with the account figures (from
        /// Postgres). Neither source is allowed to take the panel down: a missing
        /// stats file and an unreachable database each degrade to a flagged,
        /// rendered state.
        /// </summary>
        private static string BuildStatsJson()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            (WorldAdminResultState resultState, WorldAdminResult? latestResult) =
                WorldAdminResult.Read();

            JObject root = new JObject
            {
                ["serverName"] = ReadServerName(),
                ["game"] = BuildGameJson(now),
                ["accounts"] = BuildAccountsJson(now),
                ["commands"] = new JObject
                {
                    ["recent"] = AdminCommandJournal.ToJson(),
                    ["completionState"] = resultState.ToString().ToLowerInvariant(),
                    ["latestCompletion"] = latestResult?.ToJson(),
                },
            };

            // EscapeHtml so the SAME payload is safe both as the /admin/api/stats
            // body and inlined into the dashboard's <script> bootstrap: a server
            // name containing </script> or a quote is emitted as < etc and
            // cannot break out of the tag or the JSON.
            using System.IO.StringWriter sw = new System.IO.StringWriter();
            using (JsonTextWriter writer = new JsonTextWriter(sw))
            {
                writer.Formatting = Formatting.None;
                writer.StringEscapeHandling = StringEscapeHandling.EscapeHtml;
                root.WriteTo(writer);
            }
            return sw.ToString();
        }

        private static string ReadServerName()
        {
            try
            {
                return Accounts.ServerConfig.GetServerName();
            }
            catch (Exception)
            {
                return ServerConfigPolicy.DefaultServerName;
            }
        }

        private static JObject BuildGameJson(DateTimeOffset now)
        {
            GameStatsResult result = GameStats.Read(now);

            JObject game = new JObject
            {
                ["reporting"] = result.State == GameStatsState.Ok,
                ["state"] = result.State.ToString().ToLowerInvariant(),
            };

            if (result.State != GameStatsState.Ok || result.Snapshot == null)
            {
                game["players"] = new JArray();
                return game;
            }

            GameStatsSnapshot s = result.Snapshot;

            game["ageSeconds"] = Math.Round(result.Age.TotalSeconds, 1);
            game["stale"] = result.Stale;
            game["uptimeSeconds"] = s.UptimeSeconds;
            game["relayMode"] = s.RelayMode;
            game["build"] = s.Build;
            game["totalConnects"] = s.TotalConnects;
            game["totalDisconnects"] = s.TotalDisconnects;
            game["currentOnline"] = s.CurrentOnline;
            game["peakOnline"] = s.PeakOnline;
            game["wireHealthWarning"] = s.WireHealthWarning;
            game["secondIslandRegistered"] = s.SecondIslandRegistered;
            game["firstRegionTerrainCount"] = s.FirstRegionTerrainCount;
            game["schemaVersion"] = s.SchemaVersion;
            game["terrain"] = s.Terrain.Json;
            game["fauna"] = s.Fauna.Json;
            game["runtime"] = new JObject
            {
                ["hostMode"] = s.RuntimeHostMode,
                ["hostId"] = s.RuntimeHostId,
                ["ownedEntityCount"] = s.RuntimeOwnedEntityCount,
                ["globalEntityCount"] = s.RuntimeGlobalEntityCount,
                ["unownedEntityCount"] = s.RuntimeUnownedEntityCount,
                ["ownershipIssueCount"] = s.RuntimeOwnershipIssueCount,
                ["domains"] = new JArray(s.RuntimeDomains.Select(x => x.Json)),
                ["shipDomains"] = new JArray(s.ShipDomains.Select(x => x.Json)),
            };

            JArray players = new JArray();
            foreach (GamePlayerStat p in s.Players)
            {
                long connectedForSeconds = (long)Math.Max(0, (now - p.ConnectedAt).TotalSeconds);
                JObject pj = new JObject
                {
                    ["entityId"] = p.EntityId,
                    ["peerId"] = p.PeerId,
                    ["connectedForSeconds"] = connectedForSeconds,
                    ["hasHealth"] = p.HasHealth,
                };
                if (p.HasHealth)
                {
                    pj["rttMs"] = p.RttMs;
                    pj["packetsLost"] = p.PacketsLost;
                    pj["packetsSent"] = p.PacketsSent;
                    pj["inFlightBytes"] = p.InFlightBytes;
                    pj["spiral"] = p.Spiral;
                }
                pj["hasPosition"] = p.HasPosition;
                if (p.HasPosition)
                {
                    pj["x"] = p.X;
                    pj["y"] = p.Y;
                    pj["z"] = p.Z;
                }
                players.Add(pj);
            }
            game["players"] = players;

            return game;
        }

        private static JObject BuildAccountsJson(DateTimeOffset now)
        {
            try
            {
                DateTimeOffset startOfDay = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);

                int total = Accounts.Repository.Count();
                int today = Accounts.Repository.CountCreatedSince(startOfDay);
                int characters = Accounts.Repository.CountCharacters();
                IReadOnlyList<AccountSummary> recent = Accounts.Repository.Recent(RecentSignups);

                JArray recentJson = new JArray();
                foreach (AccountSummary a in recent)
                {
                    recentJson.Add(new JObject
                    {
                        ["username"] = a.Username,
                        ["createdAtUnixMs"] = a.CreatedAt.ToUnixTimeMilliseconds(),
                        ["characters"] = a.CharacterCount,
                    });
                }

                return new JObject
                {
                    ["available"] = true,
                    ["total"] = total,
                    ["today"] = today,
                    ["characters"] = characters,
                    ["recent"] = recentJson,
                };
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] admin dashboard could not read the account database: " + e.Message);
                return new JObject { ["available"] = false };
            }
        }

        // ---- request/response plumbing -------------------------------------

        /// <summary>
        /// Parses an application/x-www-form-urlencoded body into a map. Tolerant:
        /// a malformed pair is skipped rather than throwing, because a login form
        /// is the last place to surface a 500.
        /// </summary>
        internal static Dictionary<string, string> ParseForm(string? body)
        {
            Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(body))
            {
                return form;
            }

            foreach (string pair in body.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                int eq = pair.IndexOf('=');
                string key = eq >= 0 ? pair.Substring(0, eq) : pair;
                string value = eq >= 0 ? pair.Substring(eq + 1) : string.Empty;

                try
                {
                    key = Uri.UnescapeDataString(key.Replace('+', ' '));
                    value = Uri.UnescapeDataString(value.Replace('+', ' '));
                }
                catch (Exception)
                {
                    continue;
                }

                if (key.Length > 0)
                {
                    form[key] = value;
                }
            }

            return form;
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

        private static void Html(HttpSession session, int status, string body)
        {
            HtmlWithCookie(session, status, body, null);
        }

        private static void HtmlWithCookie(HttpSession session, int status, string body, string? setCookie)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", AdminPage.ContentType);
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetHeader("X-Content-Type-Options", "nosniff");
            resp.SetHeader("Referrer-Policy", "same-origin");
            if (setCookie != null)
            {
                resp.SetHeader("Set-Cookie", setCookie);
            }
            resp.SetBody(body);
            session.SendResponseAsync(resp);
        }

        private static void Json(HttpSession session, int status, string body)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(status);
            resp.SetHeader("Content-Type", "application/json");
            resp.SetHeader("Cache-Control", "no-store");
            resp.SetBody(body);
            session.SendResponseAsync(resp);
        }

        private static void Redirect(HttpSession session, string location)
        {
            RedirectWithCookie(session, location, null);
        }

        private static void RedirectWithCookie(HttpSession session, string location, string? setCookie)
        {
            HttpResponse resp = new HttpResponse();
            resp.SetBegin(303);
            resp.SetHeader("Location", location);
            resp.SetHeader("Cache-Control", "no-store");
            if (setCookie != null)
            {
                resp.SetHeader("Set-Cookie", setCookie);
            }
            resp.SetBody(string.Empty);
            session.SendResponseAsync(resp);
        }
    }
}
