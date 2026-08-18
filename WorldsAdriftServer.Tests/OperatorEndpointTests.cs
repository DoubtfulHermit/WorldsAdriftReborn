using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Operator;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The operator command surface's HTTP BOUNDARY: route parsing, authorisation,
    /// the shape of every answer, and the turn from request parameters into one
    /// dispatchable command.
    ///
    /// These endpoints move other people's characters and other people's ships, so
    /// the property under test throughout is that a request which is not
    /// UNAMBIGUOUSLY understood produces a refusal that says why - never a default,
    /// never a best guess, and never an HTML page where a program expected JSON.
    /// </summary>
    public class OperatorRouteTests
    {
        [Fact]
        public void Every_endpoint_resolves()
        {
            Assert.Equal(OperatorRouteKind.Targets,
                OperatorRoute.Parse("GET", "/admin/api/operator/targets").Kind);
            Assert.Equal(OperatorRouteKind.Teleport,
                OperatorRoute.Parse("POST", "/admin/api/operator/teleport").Kind);
            Assert.Equal(OperatorRouteKind.SummonShip,
                OperatorRoute.Parse("POST", "/admin/api/operator/summon-ship").Kind);
        }

        [Theory]
        [InlineData("POST", "/admin/api/operator/targets")]
        [InlineData("GET", "/admin/api/operator/teleport")]
        [InlineData("DELETE", "/admin/api/operator/summon-ship")]
        public void The_method_is_part_of_the_match_not_a_later_check(string method, string path)
        {
            // Resolving first and refusing the verb afterwards produces a different
            // refusal from "no such endpoint", and only one of the two tells a GUI
            // author what they actually did wrong.
            Assert.Equal(OperatorRouteKind.None, OperatorRoute.Parse(method, path).Kind);
        }

        [Fact]
        public void The_namespace_is_recognised_even_for_paths_that_name_nothing()
        {
            // This is the difference between "answer with a JSON refusal listing
            // the endpoints" and "fall through to the HTML login page", which a
            // fetch() reads as a parse error with no clue in it.
            Assert.True(OperatorRoute.IsOperatorPath("/admin/api/operator/nonsense"));
            Assert.False(OperatorRoute.IsOperatorPath("/admin/api/stats"));
            Assert.False(OperatorRoute.IsOperatorPath("/admin"));
        }

        [Fact]
        public void The_catalogue_lists_exactly_the_routes_that_resolve()
        {
            // The refusal for an unknown path prints this list. A route added to
            // the matcher and not to the list would be invisible to every GUI.
            foreach (string entry in OperatorRoute.Catalogue)
            {
                string[] parts = entry.Split(' ');
                Assert.NotEqual(OperatorRouteKind.None, OperatorRoute.Parse(parts[0], parts[1]).Kind);
            }
            Assert.Equal(3, OperatorRoute.Catalogue.Count);
        }
    }

    public class OperatorGateTests
    {
        private const string Path = "/admin/api/operator/teleport";

        private static OperatorGate.Decision Evaluate(
            bool authed = true, bool header = true, bool csrf = true,
            string method = "POST", string path = Path) =>
            OperatorGate.Evaluate(method, path, authed, header, csrf);

        [Fact]
        public void A_fully_credentialled_request_is_served()
        {
            Assert.True(Evaluate().Serve);
        }

        [Fact]
        public void An_unauthenticated_request_is_refused_with_its_route_still_named()
        {
            // The ordering fix SocialGate had to make, made once here rather than
            // learned twice: parse the route BEFORE authorising, so the refusal
            // knows which button produced it.
            OperatorGate.Decision decision = Evaluate(authed: false);

            Assert.False(decision.Serve);
            Assert.Equal(OperatorRouteKind.Teleport, decision.Kind);
            Assert.Equal("operator-teleport", (string?)decision.Refusal!["action"]);
            Assert.Equal(OperatorErrorCodes.Unauthenticated, (string?)decision.Refusal!["code"]);
            Assert.Equal(401, decision.Status);
        }

        [Fact]
        public void A_missing_confirmation_header_is_refused()
        {
            OperatorGate.Decision decision = Evaluate(header: false);

            Assert.False(decision.Serve);
            Assert.Equal(OperatorErrorCodes.Forbidden, (string?)decision.Refusal!["code"]);
            Assert.Equal(403, decision.Status);
        }

        [Fact]
        public void A_bad_csrf_token_is_refused()
        {
            OperatorGate.Decision decision = Evaluate(csrf: false);

            Assert.False(decision.Serve);
            Assert.Equal(403, decision.Status);
        }

        [Fact]
        public void The_read_route_is_gated_exactly_as_hard_as_the_write_routes()
        {
            // A roster of who is online and where they are standing is not a
            // public fact either. Every gate applies to GET targets too.
            foreach (bool authed in new[] { true, false })
            foreach (bool header in new[] { true, false })
            foreach (bool csrf in new[] { true, false })
            {
                bool expected = authed && header && csrf;
                Assert.Equal(expected, Evaluate(authed, header, csrf,
                    "GET", "/admin/api/operator/targets").Serve);
            }
        }

        [Fact]
        public void An_unknown_operator_path_is_refused_in_band_as_json()
        {
            OperatorGate.Decision decision = Evaluate(path: "/admin/api/operator/nope");

            Assert.False(decision.Serve);
            Assert.Equal(404, decision.Status);
            Assert.Equal(OperatorErrorCodes.UnknownRoute, (string?)decision.Refusal!["code"]);
            // It names the endpoints that DO exist, because the reader is a program
            // whose author is the only person who can fix this.
            Assert.Contains("teleport", (string?)decision.Refusal!["reason"]);
        }

        [Fact]
        public void An_unknown_path_is_refused_before_authentication_is_even_considered()
        {
            // Not a security decision, a diagnosability one: an unauthenticated
            // 401 on a misspelled endpoint sends a GUI author to look at cookies.
            Assert.Equal(404, Evaluate(authed: false, path: "/admin/api/operator/nope").Status);
        }
    }

    public class OperatorRefusalTests
    {
        [Fact]
        public void Every_answer_has_the_same_shape_so_a_gui_branches_only_on_ok()
        {
            JObject accepted = OperatorRefusal.Accept("operator-teleport", "done", "entity:5");
            JObject refused = OperatorRefusal.Refuse(
                "operator-teleport", OperatorErrorCodes.BadTarget, "why", "entity:5");

            Assert.True((bool?)accepted["ok"]);
            Assert.False((bool?)refused["ok"]);
            foreach (JObject answer in new[] { accepted, refused })
            {
                Assert.NotNull(answer["action"]);
                Assert.Equal("entity:5", (string?)answer["target"]);
            }
        }

        [Fact]
        public void An_accepted_answer_always_carries_a_warnings_array_even_when_empty()
        {
            // A GUI that has to test for the field's existence before iterating it
            // is a GUI that will forget to, once.
            JObject accepted = OperatorRefusal.Accept("operator-teleport", "done");
            Assert.IsType<JArray>(accepted["warnings"]);
            Assert.Empty((JArray)accepted["warnings"]!);
        }

        [Fact]
        public void Warnings_survive_onto_the_accepted_answer()
        {
            JObject accepted = OperatorRefusal.Accept(
                "operator-teleport", "done", null, new[] { "expect a fall" });
            Assert.Single((JArray)accepted["warnings"]!);
        }

        [Theory]
        [InlineData(OperatorErrorCodes.Unauthenticated, 401)]
        [InlineData(OperatorErrorCodes.Forbidden, 403)]
        [InlineData(OperatorErrorCodes.UnknownRoute, 404)]
        [InlineData(OperatorErrorCodes.BadRequest, 400)]
        [InlineData(OperatorErrorCodes.BadTarget, 400)]
        [InlineData(OperatorErrorCodes.TargetNotFound, 409)]
        [InlineData(OperatorErrorCodes.TargetAmbiguous, 409)]
        [InlineData(OperatorErrorCodes.GameUnavailable, 503)]
        [InlineData(OperatorErrorCodes.Busy, 409)]
        public void Each_code_has_one_status_everywhere(string code, int status)
        {
            // Six unrelated refusals share 409. The CODE is what a GUI switches on,
            // and it must not mean a different status depending on which route
            // emitted it.
            Assert.Equal(status, OperatorRefusal.StatusFor(code));
        }

        [Fact]
        public void A_refusal_reason_is_a_sentence_and_never_a_bare_code()
        {
            JObject refusal = OperatorRefusal.UnknownRoute();
            string reason = (string?)refusal["reason"] ?? "";
            Assert.Contains(" ", reason);
            Assert.EndsWith(".", reason);
        }
    }

    /// <summary>
    /// The stats file is a contract between two processes that are deployed
    /// separately, so a login server WILL at some point read a game server older
    /// than itself. The v8 fields the operator surface needs must therefore be
    /// absent-tolerant, and absent must read as "not published" rather than as a
    /// value.
    /// </summary>
    public class OperatorStatsToleranceTests
    {
        private const string V7 = @"{""schemaVersion"":7,""bootTimeUnixMs"":1,""generatedAtUnixMs"":1,
            ""uptimeSeconds"":1,""relayMode"":""raw"",""relayHz"":0,""build"":""x"",
            ""totalConnects"":1,""totalDisconnects"":0,""currentOnline"":1,""peakOnline"":1,
            ""wireHealthWarning"":false,""secondIslandRegistered"":false,""firstRegionTerrainCount"":0,
            ""players"":[{""entityId"":7,""peerId"":""0x1"",""connectedAtUnixMs"":1,
                ""position"":null,""health"":null}],
            ""runtime"":{""shipDomains"":[{""domainId"":""ship:9"",""hullEntityId"":9}]}}";

        private static GameStatsSnapshot Parse(string json)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "wareborn-operator-test-" + Guid.NewGuid().ToString("n") + ".json");
            File.WriteAllText(path, json);
            try
            {
                GameStatsResult result = GameStats.ReadFrom(path, DateTimeOffset.UnixEpoch.AddSeconds(2));
                Assert.Equal(GameStatsState.Ok, result.State);
                return result.Snapshot!;
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_v7_snapshot_with_no_operator_fields_still_parses()
        {
            GameStatsSnapshot s = Parse(V7);

            Assert.Equal(7, s.SchemaVersion);
            Assert.Single(s.Players);
            // Absent, therefore "" - which OperatorTargetPolicy treats as "no
            // identity yet" and refuses to match, rather than as a uid.
            Assert.Equal(string.Empty, s.Players[0].CharacterUid);
            Assert.Equal(string.Empty,
                (string?)s.ShipDomains[0].Json["ownerCharacterUid"]);
        }
    }

    public class OperatorRequestPolicyTests
    {
        private const string Uid = "66666666-6666-6666-6666-666666666666";

        private static string? NoNames(string name) => null;
        private static string? AlwaysBligh(string name) => name == "Bligh" ? Uid : null;

        [Fact]
        public void A_teleport_becomes_one_fully_specified_command()
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "uid:" + Uid, "island:mental-facility", NoNames);

            Assert.True(outcome.Ok);
            Assert.Equal(OperatorCommandKind.Teleport, outcome.Command.Kind);
            Assert.Equal(OperatorTargetKind.CharacterUid, outcome.Command.Target.Kind);
            Assert.Equal(OperatorDestinationKind.Island, outcome.Command.Destination.Kind);
        }

        [Fact]
        public void A_name_is_resolved_to_a_uid_here_because_only_this_process_can()
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "name:Bligh", "spawn", AlwaysBligh);

            Assert.True(outcome.Ok);
            Assert.Equal(OperatorTargetKind.CharacterUid, outcome.Command.Target.Kind);
            Assert.Equal(Uid, outcome.Command.Target.Value);
        }

        [Fact]
        public void A_name_that_matches_nobody_refuses_rather_than_dispatching_a_name()
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "name:Nobody", "spawn", AlwaysBligh);

            Assert.False(outcome.Ok);
            Assert.Equal(OperatorErrorCodes.TargetNotFound, outcome.Code);
        }

        [Fact]
        public void A_name_with_no_character_store_refuses_and_names_the_alternative()
        {
            // The database being down must not become "no such player": the two
            // need different actions from the operator.
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "name:Bligh", "spawn", null);

            Assert.Equal(OperatorErrorCodes.GameUnavailable, outcome.Code);
            Assert.Contains("uid:", outcome.Reason);
        }

        [Fact]
        public void A_misspelled_island_is_refused_here_rather_than_in_a_result_file()
        {
            // Accepting it and refusing a quarter of a second later, in a file the
            // operator has to go and read, is a much worse place to learn about a
            // typo.
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "entity:5", "island:Atlantis", NoNames);

            Assert.False(outcome.Ok);
            Assert.Equal(OperatorErrorCodes.BadTarget, outcome.Code);
        }

        [Fact]
        public void A_destination_player_is_validated_but_left_unresolved_for_the_game_server()
        {
            // WHERE that player is standing is a fact only the game server has, and
            // it changes between the status file and the dispatch.
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "entity:5", "player:entity:9", NoNames);

            Assert.True(outcome.Ok);
            Assert.Equal(OperatorDestinationKind.Player, outcome.Command.Destination.Kind);
            Assert.Equal("entity:9", outcome.Command.Destination.Value);
        }

        [Fact]
        public void An_unreadable_destination_player_refuses()
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                "entity:5", "player:Bob", NoNames);

            Assert.False(outcome.Ok);
            Assert.Contains("destination player", outcome.Reason);
        }

        [Fact]
        public void A_summon_with_no_hull_named_means_the_ship_they_own()
        {
            // It is the default because it is the request an operator actually has,
            // and because it is the only form that cannot summon the wrong person's
            // ship by mistyping a number.
            foreach (string? hull in new string?[] { null, "", "   " })
            {
                OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildSummonShip(
                    "uid:" + Uid, hull, NoNames);

                Assert.True(outcome.Ok);
                Assert.Equal(OperatorHullKind.Owned, outcome.Command.Hull.Kind);
            }
        }

        [Fact]
        public void A_summon_can_name_an_exact_hull()
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildSummonShip(
                "uid:" + Uid, "hull:4242", NoNames);

            Assert.True(outcome.Ok);
            Assert.Equal(4242, outcome.Command.Hull.HullEntityId);
        }

        [Theory]
        [InlineData(null, "spawn")]
        [InlineData("", "spawn")]
        [InlineData("Bob", "spawn")]
        [InlineData("entity:5", null)]
        [InlineData("entity:5", "")]
        [InlineData("entity:5", "nowhere:1")]
        public void Missing_or_malformed_parameters_refuse_with_a_reason(
            string? target, string? destination)
        {
            OperatorRequestOutcome outcome = OperatorRequestPolicy.BuildTeleport(
                target, destination, NoNames);

            Assert.False(outcome.Ok);
            Assert.NotEqual(string.Empty, outcome.Reason);
        }

        [Fact]
        public void Every_accepted_command_can_be_written_to_the_wire()
        {
            // The endpoint formats what this returns. A command this policy accepts
            // but the wire cannot render would be a 500 at the last moment.
            foreach (OperatorRequestOutcome outcome in new[]
                     {
                         OperatorRequestPolicy.BuildTeleport("uid:" + Uid, "home", NoNames),
                         OperatorRequestPolicy.BuildTeleport("entity:5", "coord:1,2,3", NoNames),
                         OperatorRequestPolicy.BuildTeleport("entity:5", "player:peer:0x9", NoNames),
                         OperatorRequestPolicy.BuildTeleport("peer:0x9", "island:Mental Facility", NoNames),
                         OperatorRequestPolicy.BuildSummonShip("uid:" + Uid, "owned", NoNames),
                         OperatorRequestPolicy.BuildSummonShip("entity:5", "hull:7", NoNames),
                     })
            {
                Assert.True(outcome.Ok, outcome.Reason);
                Assert.True(OperatorCommandWire.TryFormat(
                    outcome.Command, out string line, out string error), error);
                Assert.True(OperatorCommandWire.TryParse(line, out OperatorCommand parsed, out error), error);
                Assert.Equal(outcome.Command, parsed);
            }
        }
    }
}
