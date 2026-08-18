using NetCoreServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Operator;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.Persistence;

namespace WorldsAdriftServer.Handlers.Admin
{
    /// <summary>
    /// The operator command surface: real authenticated HTTP endpoints for acting
    /// on ANY player, not just whoever is at the keyboard.
    ///
    /// WHAT THIS IS FOR. The existing <c>/admin/api/command</c> could teleport a
    /// player to one of three allowlisted islands and recall a ship by its entity
    /// id. Both were addressed by SESSION identifiers and both were a fixed
    /// allowlist. This surface takes a durable target (a character uid, or a name
    /// resolved to one here), a destination anywhere in the surveyed world, and
    /// refuses - with a reason - rather than acting when either is ambiguous.
    ///
    /// WHAT IT IS NOT. It is not a shell, and each route converts its parameters
    /// into exactly one instruction from a closed vocabulary defined by
    /// <see cref="OperatorCommandWire"/>. It does not talk to the game server
    /// directly either: it hands the formatted line to the existing one-shot bridge
    /// file, which the game server consumes on its authoritative poll loop. That is
    /// the same trust boundary the older bridge had, deliberately unchanged.
    ///
    /// GLUE ONLY. Routing and auth are <see cref="OperatorGate"/>; building a
    /// command out of strings is <see cref="OperatorRequestPolicy"/>; the wire form
    /// is <see cref="OperatorCommandWire"/>; the refusal shapes are
    /// <see cref="OperatorRefusal"/>. All four are pure and tested. What is left
    /// here - reading headers, reading a body, reading the status file, writing the
    /// bridge, writing the response - is the part that needs a socket.
    /// </summary>
    internal static class OperatorEndpoints
    {
        /// <summary>
        /// Takes any request whose path is in the operator namespace. Returns true
        /// when it answered, so the caller stops.
        /// </summary>
        internal static bool TryHandle(
            HttpSession session, HttpRequest request, string path,
            bool authenticated, string? sessionToken)
        {
            if (!OperatorRoute.IsOperatorPath(path)) return false;

            OperatorGate.Decision decision = OperatorGate.Evaluate(
                request.Method,
                path,
                authenticated,
                HeaderValue(request, "X-Wareborn-Admin") == "1",
                AdminAuthPolicy.VerifyCsrf(sessionToken, HeaderValue(request, AdminAuthPolicy.CsrfHeader)));

            if (!decision.Serve)
            {
                Send(session, decision.Status, decision.Refusal!);
                return true;
            }

            switch (decision.Kind)
            {
                case OperatorRouteKind.Targets:
                    Targets(session);
                    return true;
                case OperatorRouteKind.Teleport:
                    Command(session, request, decision.Kind);
                    return true;
                case OperatorRouteKind.SummonShip:
                    Command(session, request, decision.Kind);
                    return true;
                default:
                    Send(session, OperatorRefusal.StatusFor(OperatorErrorCodes.UnknownRoute),
                        OperatorRefusal.UnknownRoute());
                    return true;
            }
        }

        // ---- the read route ------------------------------------------------

        /// <summary>
        /// EVERYTHING A GUI NEEDS TO BUILD THE FORMS, in one call.
        ///
        /// It is a single endpoint rather than three because the three answers are
        /// only useful together: a player row is worth showing because there is a
        /// destination to send them to, and an island is worth listing because
        /// somebody can be sent there. Serving them in one snapshot also means the
        /// roster and the ship list a GUI renders came from the SAME read of the
        /// status file, so a ship cannot be attributed to a player who is not in
        /// the list next to it.
        ///
        /// Every row carries a ready-made <c>selector</c>: the exact string to post
        /// back. A GUI that only ever echoes those strings cannot construct an
        /// invalid target, and it never has to know that a uid is preferred over an
        /// entity id - that preference is expressed here, once, by which one the
        /// selector is built from.
        /// </summary>
        private static void Targets(HttpSession session)
        {
            GameStatsResult stats = GameStats.Read(DateTimeOffset.UtcNow);
            if (stats.State != GameStatsState.Ok || stats.Snapshot == null)
            {
                Send(session,
                    OperatorRefusal.StatusFor(OperatorErrorCodes.GameUnavailable),
                    OperatorRefusal.Refuse(
                        OperatorRoute.ActionOf(OperatorRouteKind.Targets),
                        OperatorErrorCodes.GameUnavailable,
                        "The game server is not reporting, so there is nobody to act on."));
                return;
            }

            GameStatsSnapshot snapshot = stats.Snapshot;

            JArray players = new JArray();
            foreach (GamePlayerStat player in snapshot.Players)
            {
                string uid = OperatorTargetPolicy.CanonicalUidText(player.CharacterUid);
                JObject row = new JObject
                {
                    ["entityId"] = player.EntityId,
                    ["peerId"] = player.PeerId,
                    ["characterUid"] = uid,
                    // The DURABLE selector when there is one. An entity id is a
                    // fallback and is labelled as such, because a GUI that used it
                    // by preference would be building commands that can go stale
                    // between the render and the click.
                    ["selector"] = uid.Length > 0
                        ? "uid:" + uid
                        : "entity:" + player.EntityId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                    ["selectorIsDurable"] = uid.Length > 0,
                    ["characterName"] = NameOf(uid) ?? "",
                    ["hasPosition"] = player.HasPosition,
                };
                if (player.HasPosition)
                {
                    row["x"] = player.X;
                    row["y"] = player.Y;
                    row["z"] = player.Z;
                }
                players.Add(row);
            }

            JArray ships = new JArray();
            foreach (GameShipDomainStat ship in snapshot.ShipDomains)
            {
                long hullEntityId = (long?)ship.Json["hullEntityId"] ?? 0;
                ships.Add(new JObject
                {
                    ["hullEntityId"] = hullEntityId,
                    ["ownerCharacterUid"] = OperatorTargetPolicy.CanonicalUidText(
                        (string?)ship.Json["ownerCharacterUid"]),
                    ["selector"] = "hull:" + hullEntityId.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    ["piloted"] = (bool?)ship.Json["piloted"] ?? false,
                    ["x"] = (double?)ship.Json["x"] ?? 0,
                    ["y"] = (double?)ship.Json["y"] ?? 0,
                    ["z"] = (double?)ship.Json["z"] ?? 0,
                });
            }

            // Which islands the game server has terrain machinery for RIGHT NOW.
            // Reported as a separate flag rather than by filtering the catalogue,
            // because "this island exists but is not registered this boot" is a
            // thing an operator needs to be able to see and understand - the fix is
            // a restart with a wider rollout, not a different island.
            HashSet<string> terrainKnown = new HashSet<string>(StringComparer.Ordinal);
            if (snapshot.Terrain.Json["islands"] is JArray islandRows)
            {
                foreach (JToken token in islandRows)
                {
                    string? id = (string?)token["islandId"];
                    if (!string.IsNullOrEmpty(id)) terrainKnown.Add(id!);
                }
            }

            JArray islands = new JArray();
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                islands.Add(new JObject
                {
                    ["id"] = record.Definition.Id.Value,
                    ["displayName"] = record.Definition.DisplayName,
                    ["cellId"] = record.CellId,
                    ["cellTier"] = record.CellTier,
                    ["selector"] = "island:" + record.Definition.Id.Value,
                    ["terrainKnown"] = terrainKnown.Contains(record.Definition.Id.Value),
                });
            }

            JObject body = OperatorRefusal.Accept(
                OperatorRoute.ActionOf(OperatorRouteKind.Targets),
                "Live operator roster.");
            body["stale"] = stats.Stale;
            body["ageSeconds"] = Math.Round(stats.Age.TotalSeconds, 1);
            body["players"] = players;
            body["ships"] = ships;
            body["islands"] = islands;
            body["targetKinds"] = new JArray(
                "uid:<guid>", "entity:<id>", "peer:0x<hex>", "name:<character name>");
            body["destinationKinds"] = new JArray(
                "island:<id or display name>", "coord:<x>,<y>,<z>",
                "player:<target selector>", "home", "spawn");
            body["hullKinds"] = new JArray("owned", "hull:<entityId>");

            Send(session, 200, body);
        }

        // ---- the write routes ----------------------------------------------

        private static void Command(HttpSession session, HttpRequest request, OperatorRouteKind kind)
        {
            string action = OperatorRoute.ActionOf(kind);
            Dictionary<string, string> body = ReadBody(request);
            body.TryGetValue("target", out string? target);

            OperatorRequestOutcome outcome;
            if (kind == OperatorRouteKind.Teleport)
            {
                body.TryGetValue("destination", out string? destination);
                outcome = OperatorRequestPolicy.BuildTeleport(target, destination, LookupName);
            }
            else
            {
                body.TryGetValue("hull", out string? hull);
                outcome = OperatorRequestPolicy.BuildSummonShip(target, hull, LookupName);
            }

            if (!outcome.Ok)
            {
                Refuse(session, action, outcome.Code, outcome.Reason, target);
                return;
            }

            if (!OperatorCommandWire.TryFormat(outcome.Command, out string line, out string formatError))
            {
                Refuse(session, action, OperatorErrorCodes.BadRequest, formatError, target);
                return;
            }

            // The game server has to be alive AND its status fresh before a command
            // is queued. A command written into the bridge file while the game
            // server is down sits there and fires on its next boot, at whoever
            // happens to be online then - which is the worst possible time for a
            // teleport nobody remembers asking for.
            GameStatsResult stats = GameStats.Read(DateTimeOffset.UtcNow);
            if (stats.State != GameStatsState.Ok || stats.Snapshot == null || stats.Stale)
            {
                Refuse(session, action, OperatorErrorCodes.GameUnavailable,
                    "Live game status is unavailable or stale; no command was queued.", target);
                return;
            }

            string selector = outcome.Command.Target.ToSelector();
            if (!AdminCommandBridge.TryQueueOperatorLine(
                    line, action, null, selector + " -> " + Argument(outcome.Command),
                    out string queueError))
            {
                Refuse(session, action, OperatorErrorCodes.Busy, queueError, target);
                return;
            }

            // EVERY command, accepted or refused, with actor, target and parameters.
            // The actor is "the operator" and not a name because there is exactly
            // one admin credential on this server; if that ever stops being true
            // this is the line that has to grow a name.
            string message = "Accepted " + action + " for " + selector + " ("
                + Argument(outcome.Command) + ") and handed it to the game server. "
                + "This records DISPATCH, not gameplay completion - read the command "
                + "result for what the world actually did.";
            AdminCommandJournal.Record(
                DateTimeOffset.UtcNow, action, null,
                selector + " -> " + Argument(outcome.Command), accepted: true, message);
            Console.WriteLine("[info] operator: " + action + " actor=admin target=" + selector
                + " argument=" + Argument(outcome.Command) + " -> queued.");

            Send(session, 202, OperatorRefusal.Accept(action, message, selector));
        }

        private static string Argument(OperatorCommand command) =>
            command.Kind == OperatorCommandKind.Teleport
                ? command.Destination.ToSpec()
                : command.Hull.ToSelector();

        // ---- glue ----------------------------------------------------------

        /// <summary>
        /// Resolves a character name to a uid, or null. Best-effort by
        /// construction: a database that is down produces null, which the caller
        /// turns into "no character is named that" - and an operator who needs
        /// certainty has uid: and entity: available and unaffected.
        /// </summary>
        private static string? LookupName(string characterName)
        {
            try
            {
                CharacterRecord? found = Accounts.Characters.FindByName(characterName);
                return found?.CharacterUid.ToString("D");
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] operator: character name lookup failed: " + e.Message);
                return null;
            }
        }

        private static string? NameOf(string canonicalUid)
        {
            if (canonicalUid.Length == 0 || !Guid.TryParse(canonicalUid, out Guid uid)) return null;
            try
            {
                return Accounts.Characters.Find(uid)?.Name;
            }
            catch (Exception)
            {
                // A roster that renders without names is still a usable roster; a
                // 500 because the account database blinked is not.
                return null;
            }
        }

        /// <summary>
        /// Reads the request body as either JSON or form-urlencoded.
        ///
        /// Both, because the two callers are different: a fetch() from the
        /// dashboard sends JSON, and a curl or an HTML form sends the urlencoded
        /// form the rest of this panel already uses. Sniffed on the first
        /// non-whitespace character rather than on Content-Type, which browsers and
        /// command-line tools disagree about often enough to be a support burden.
        /// </summary>
        private static Dictionary<string, string> ReadBody(HttpRequest request)
        {
            string body = request.Body ?? string.Empty;
            string trimmed = body.TrimStart();

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                Dictionary<string, string> parsed =
                    new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    JObject json = JObject.Parse(trimmed);
                    foreach (KeyValuePair<string, JToken?> pair in json)
                    {
                        if (pair.Value == null || pair.Value.Type == JTokenType.Null) continue;
                        parsed[pair.Key] = pair.Value.Type == JTokenType.String
                            ? (string?)pair.Value ?? string.Empty
                            : pair.Value.ToString(Formatting.None);
                    }
                }
                catch (JsonException)
                {
                    // An unparseable body is an empty body: the parameter refusals
                    // below name the field that is missing, which is more useful
                    // than "invalid JSON at line 1".
                }
                return parsed;
            }

            return AdminHandler.ParseForm(body);
        }

        private static void Refuse(
            HttpSession session, string action, string code, string reason, string? target)
        {
            Console.WriteLine("[info] operator: " + action + " actor=admin target="
                + (target ?? "(none)") + " -> refused (" + code + "): " + reason);
            AdminCommandJournal.Record(
                DateTimeOffset.UtcNow, action, null, target ?? "(none)", accepted: false, reason);
            Send(session, OperatorRefusal.StatusFor(code),
                OperatorRefusal.Refuse(action, code, reason, target));
        }

        private static void Send(HttpSession session, int status, JObject body)
        {
            HttpResponse response = new HttpResponse();
            response.SetBegin(status);
            response.SetHeader("Content-Type", "application/json");
            response.SetHeader("Cache-Control", "no-store");
            response.SetBody(body.ToString(Formatting.None));
            session.SendResponseAsync(response);
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
