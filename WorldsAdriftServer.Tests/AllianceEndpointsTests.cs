using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Social;
using WorldsAdriftRebornGameServer.Multiplayer.Alliance;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The alliance API driven the way the client drives it: a method and a URL
    /// in, an envelope out, through the REAL <see cref="SocialRoute"/> parser.
    ///
    /// Every test here is a plain <c>[Fact]</c>. That is the point. The crew
    /// equivalents are <c>[PostgresFact]</c> and are skipped on any machine
    /// without a database - which is where two shipped defects hid - and the
    /// alliance shapes are the part most likely to be subtly wrong, because they
    /// were recovered by reading a decompiler's output rather than a specification.
    ///
    /// Requests are built as STRINGS rather than by calling methods directly, so a
    /// route that stopped resolving, or resolved to the wrong kind, fails here
    /// instead of silently exercising a method nothing can reach. That is exactly
    /// the class of bug that produced the original report: POST /alliance parsed
    /// to nothing and was answered "not implemented".
    /// </summary>
    public sealed class AllianceEndpointsTests
    {
        private const string Region = "community_server";

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        private sealed class World
        {
            private readonly Dictionary<Guid, string> names = new();

            internal AllianceStoreDouble Alliances { get; } = new AllianceStoreDouble();
            internal InviteStoreDouble Invites { get; } = new InviteStoreDouble();
            internal AllianceEndpoints Endpoints { get; }

            internal World()
            {
                Endpoints = new AllianceEndpoints(
                    Alliances,
                    Invites,
                    uid => names.TryGetValue(uid, out string? name) ? name : null,
                    Region,
                    () => Now);
            }

            internal Guid Character(string name)
            {
                Guid uid = Guid.NewGuid();
                names[uid] = name;
                return uid;
            }

            /// <summary>Routes for real, then serves. An unroutable URL fails the
            /// test rather than falling through to a refusal.</summary>
            internal JObject Send(string method, string url, Guid actor, string? body = null)
            {
                SocialRoute route = SocialRoute.Parse(method, url);
                Assert.NotEqual(SocialRouteKind.None, route.Kind);
                return Endpoints.Handle(route, actor, url, body);
            }

            internal JObject Get(string url, Guid actor) => Send("GET", url, actor);
            internal JObject Post(string url, Guid actor, string? body = null) => Send("POST", url, actor, body);
            internal JObject Patch(string url, Guid actor, string body) => Send("PATCH", url, actor, body);
            internal JObject Delete(string url, Guid actor) => Send("DELETE", url, actor);

            /// <summary>The exact body the retail client sends on CREATE.</summary>
            internal JObject Found(Guid founder, string name, string? description = null, string? motd = null)
            {
                JObject payload = new JObject
                {
                    ["leaderCharacterUid"] = U(founder),
                    ["name"] = name,
                    ["region"] = Region,
                };

                // The client OMITS these when the player left the box empty, so
                // the tests do too.
                if (!string.IsNullOrEmpty(description)) payload["description"] = description;
                if (!string.IsNullOrEmpty(motd)) payload["messageOfTheDay"] = motd;

                return Post("/alliance", founder, payload.ToString());
            }
        }

        private static string U(Guid uid) => uid.ToString("D");

        private static string Uid(JObject response) => response["data"]!.Value<string>("uid")!;

        // ================================================================ create

        /// <summary>
        /// THE REGRESSION. This is the request the player sent, verbatim from the
        /// production log, that came back "not implemented" and reached them as the
        /// client's generic unknown-error dialog, E00001.
        /// </summary>
        [Fact]
        public void The_exact_create_request_from_the_bug_report_succeeds()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");

            JObject response = world.Post("/alliance", founder, new JObject
            {
                ["leaderCharacterUid"] = U(founder),
                ["name"] = "Rat Corp",
                ["description"] = "Rats for life",
                ["region"] = Region,
            }.ToString());

            Assert.True(response.Value<bool>("success"));

            JObject data = (JObject)response["data"]!;
            Assert.Equal("Rat Corp", data.Value<string>("name"));
            Assert.Equal("Rats for life", data.Value<string>("description"));
            Assert.Equal(Region, data.Value<string>("region"));
            Assert.Equal(U(founder), data.Value<string>("leaderCharacterUid"));
            Assert.Equal(1, data.Value<int>("memberCount"));
        }

        /// <summary>
        /// The client runs every alliance id it later sends back through
        /// SocialHelper.SanitizeGuid, which requires a hyphen and then constructs a
        /// System.Guid. A crew-style "alliance:{guid}" would throw a
        /// FormatException INSIDE the client, with no request and nothing in our
        /// log to explain it.
        /// </summary>
        [Fact]
        public void The_alliance_id_is_a_bare_guid_because_the_client_parses_it()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");

            string uid = Uid(world.Found(founder, "Rat Corp"));

            Assert.True(Guid.TryParse(uid, out _));
            Assert.Contains("-", uid);
            Assert.DoesNotContain(":", uid);
            Assert.Equal(uid.ToLowerInvariant(), uid);
        }

        /// <summary>
        /// A create must leave the alliance OPENABLE, which takes more than a row
        /// in one table: the client needs both default ranks to fill its Leader and
        /// BasicMember slots, and the founder's own rankId must appear among them
        /// or AllianceClient.TryGetRank throws.
        /// </summary>
        [Fact]
        public void Founding_creates_both_default_ranks_and_seats_the_founder_on_the_leader_one()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JArray ranks = (JArray)world.Get("/ranks/" + uid, founder)["data"]!;
            Assert.Equal(2, ranks.Count);

            JObject leader = ranks.Cast<JObject>().Single(r =>
                r.Value<string>("rankType") == "leader");
            JObject member = ranks.Cast<JObject>().Single(r =>
                r.Value<string>("rankType") == "member");

            // rankType + !editable is how the client identifies the two defaults.
            Assert.False(leader.Value<bool>("editable"));
            Assert.False(member.Value<bool>("editable"));
            Assert.Equal("alliance_member", leader.Value<string>("membershipType"));

            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, founder)["data"]!["items"]!;
            string founderRank = ((JObject)members[0]).Value<string>("rankId")!;

            Assert.Contains(founderRank, ranks.Select(r => ((JObject)r).Value<string>("uid")));
            Assert.Equal(leader.Value<string>("uid"), founderRank);
        }

        /// <summary>
        /// The client reads its MOTD gate off leader_chat, not off
        /// edit_message_of_the_day (SocialGroupParsers.cs:129 - a retail bug). A
        /// leader rank without leader_chat renders the founder's own MOTD box
        /// locked.
        /// </summary>
        [Fact]
        public void The_leader_rank_carries_leader_chat_so_the_founder_can_edit_the_motd()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JArray ranks = (JArray)world.Get("/ranks/" + uid, founder)["data"]!;
            JObject leader = ranks.Cast<JObject>().Single(r => r.Value<string>("rankType") == "leader");
            List<string?> permissions = ((JArray)leader["permissions"]!).Select(p => p.Value<string>()).ToList();

            Assert.Contains("leader_chat", permissions);
            Assert.Contains("edit_members", permissions);
            Assert.Contains("edit_group", permissions);
        }

        [Fact]
        public void A_duplicate_name_is_refused_with_the_clients_own_code()
        {
            World world = new World();
            Guid one = world.Character("Rattus");
            Guid two = world.Character("Mus");

            world.Found(one, "Rat Corp");
            JObject second = world.Found(two, "rat corp");

            Assert.False(second.Value<bool>("success"));
            Assert.Equal("duplicate_alliance_name", second.Value<string>("errorCode"));
        }

        [Fact]
        public void A_player_cannot_found_a_second_alliance_while_in_one()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");

            world.Found(founder, "Rat Corp");
            JObject second = world.Found(founder, "Sky Rats");

            Assert.False(second.Value<bool>("success"));
            Assert.Equal("already_in_alliance", second.Value<string>("errorCode"));
        }

        [Fact]
        public void Founding_in_somebody_elses_name_is_refused()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid victim = world.Character("Mus");

            JObject response = world.Post("/alliance", founder, new JObject
            {
                ["leaderCharacterUid"] = U(victim),
                ["name"] = "Rat Corp",
                ["region"] = Region,
            }.ToString());

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));
        }

        [Fact]
        public void A_name_the_client_would_have_refused_to_type_is_refused_here_too()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");

            JObject response = world.Found(founder, "Rat Corp 2");

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("invalid_name", response.Value<string>("errorCode"));
        }

        // ================================================== the client read chain

        /// <summary>
        /// The whole chain the Social Sheet walks after a create, in the order it
        /// walks it, each link taking its argument from the previous response. A
        /// test that jumped to the last call would not notice an id that never
        /// survives the first.
        /// </summary>
        [Fact]
        public void The_alliance_is_reachable_through_the_clients_own_read_chain()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp", "Rats for life", "Squeak"));

            // 1. alliance/find/{region}/{characterUid} - by PERSON, not by group.
            JObject found = world.Get("/alliance/find/" + Region + "/" + U(founder), founder);
            Assert.True(found.Value<bool>("success"));
            Assert.Equal(uid, found["data"]!.Value<string>("uid"));
            Assert.Equal("Squeak", found["data"]!.Value<string>("messageOfTheDay"));

            // 2. alliance/{region}/{allianceUid} - by group.
            JObject one = world.Get("/alliance/" + Region + "/" + uid, founder);
            Assert.Equal("Rat Corp", one["data"]!.Value<string>("name"));

            // 3. the roster.
            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, founder)["data"]!["items"]!;
            Assert.Single(members);
            Assert.Equal(U(founder), ((JObject)members[0]).Value<string>("memberId"));
            Assert.Equal(uid, ((JObject)members[0]).Value<string>("targetId"));

            // 4. the ranks.
            Assert.Equal(2, ((JArray)world.Get("/ranks/" + uid, founder)["data"]!).Count);

            // 5. the pending list - both directions in one call.
            JObject pending = world.Get("/memberships/invites/alliance/" + uid, founder);
            Assert.NotNull(pending["data"]!["items"]);
            Assert.Empty((JArray)pending["data"]!["items"]!);
        }

        /// <summary>
        /// The two list shapes, which are NOT the same and which the client parses
        /// differently. alliances/{region} nests at data.items; the search returns
        /// a BARE array of id strings which the client casts with
        /// <c>model.data as JArray</c> and no null guard.
        /// </summary>
        [Fact]
        public void The_browser_uses_data_items_and_the_search_returns_a_bare_array_of_ids()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JObject list = world.Get("/alliances/" + Region, founder);
            Assert.Equal(JTokenType.Object, list["data"]!.Type);
            Assert.Single((JArray)list["data"]!["items"]!);

            JObject search = world.Get("/alliance/search/" + Region + "?term=rat", founder);
            Assert.Equal(JTokenType.Array, search["data"]!.Type);
            Assert.Equal(uid, ((JArray)search["data"]!)[0].Value<string>());

            // Then the client POSTs the ids it got back - a BARE array again.
            JObject batch = world.Post("/alliance/" + Region + "/batch", founder,
                new JObject { ["batch"] = new JArray { uid } }.ToString());

            Assert.Equal(JTokenType.Array, batch["data"]!.Type);
            Assert.Equal("Rat Corp", ((JObject)((JArray)batch["data"]!)[0]).Value<string>("name"));
        }

        /// <summary>
        /// No matches must be an EMPTY ARRAY, not an object and not an absent data
        /// key: the client does <c>model.data as JArray</c> and immediately reads
        /// <c>.Count</c>, so anything else is an NRE inside the client.
        /// </summary>
        [Fact]
        public void A_search_with_no_matches_is_an_empty_array_rather_than_an_object()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            world.Found(founder, "Rat Corp");

            JObject search = world.Get("/alliance/search/" + Region + "?term=zzzz", founder);

            Assert.Equal(JTokenType.Array, search["data"]!.Type);
            Assert.Empty((JArray)search["data"]!);
        }

        /// <summary>
        /// A stale id in a batch is skipped rather than failing the call. The list
        /// the client holds came from a search that may be seconds old, and one
        /// disbanded alliance must not empty the result.
        /// </summary>
        [Fact]
        public void A_batch_skips_ids_that_no_longer_resolve()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JObject batch = world.Post("/alliance/" + Region + "/batch", founder,
                new JObject { ["batch"] = new JArray { uid, U(Guid.NewGuid()), "not-a-guid" } }.ToString());

            Assert.True(batch.Value<bool>("success"));
            Assert.Single((JArray)batch["data"]!);
        }

        // ============================================ invitations & applications

        /// <summary>
        /// An INVITE, end to end. Note what makes it an invite rather than an
        /// application: <c>inviter</c> is non-null. The client uses exactly that to
        /// decide which of its two sections the row belongs in.
        /// </summary>
        [Fact]
        public void An_invitation_round_trips_and_carries_a_non_null_inviter()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid joiner = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JObject sent = world.Endpoints.SendInvite(founder, new JObject
            {
                ["targetId"] = uid,
                ["character"] = U(joiner),
                ["targetType"] = "alliance_member",
                ["inviter"] = U(founder),
                ["message"] = "come aboard",
            });

            Assert.True(sent.Value<bool>("success"));
            JObject row = (JObject)sent["data"]!;
            Assert.Equal("alliance_member", row.Value<string>("targetType"));
            Assert.Equal("new", row.Value<string>("status"));
            Assert.Equal("Rat Corp", row.Value<string>("targetName"));
            Assert.Equal(JTokenType.Object, row["inviter"]!.Type);
            Assert.Equal(U(founder), row["inviter"]!.Value<string>("uid"));

            // It shows in the alliance's pending list, in data.items.
            JArray pending = (JArray)world.Get("/memberships/invites/alliance/" + uid, founder)["data"]!["items"]!;
            Assert.Single(pending);

            // Accepting seats them on the DEFAULT MEMBER rank.
            SocialInviteRecord stored = world.Invites.Find(row.Value<string>("id")!)!;
            Assert.True(world.Endpoints.Accept(stored).Value<bool>("success"));

            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, founder)["data"]!["items"]!;
            Assert.Equal(2, members.Count);

            JArray ranks = (JArray)world.Get("/ranks/" + uid, founder)["data"]!;
            string memberRank = ranks.Cast<JObject>()
                .Single(r => r.Value<string>("rankType") == "member").Value<string>("uid")!;

            Assert.Equal(memberRank, members.Cast<JObject>()
                .Single(m => m.Value<string>("memberId") == U(joiner)).Value<string>("rankId"));
        }

        /// <summary>
        /// An APPLICATION. <c>inviter</c> must come back as an explicit JSON NULL -
        /// that null is the client's structural discriminator
        /// (CheckMembershipRequestType), and an object there would put the row in
        /// the INVITATIONS section instead of APPLICATIONS.
        /// </summary>
        [Fact]
        public void An_application_round_trips_and_carries_a_null_inviter()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid applicant = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JObject applied = world.Post("/memberships/join", applicant, new JObject
            {
                ["targetId"] = uid,
                ["character"] = U(applicant),
                ["targetType"] = "alliance_member",
                ["message"] = "let me in",
                ["region"] = Region,
            }.ToString());

            Assert.True(applied.Value<bool>("success"));

            JObject row = (JObject)applied["data"]!;
            Assert.NotNull(row["inviter"]);
            Assert.Equal(JTokenType.Null, row["inviter"]!.Type);
            Assert.Equal("alliance_member", row.Value<string>("targetType"));
        }

        [Fact]
        public void A_second_live_application_to_the_same_alliance_is_refused()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid applicant = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            string body = new JObject
            {
                ["targetId"] = uid,
                ["character"] = U(applicant),
                ["targetType"] = "alliance_member",
            }.ToString();

            Assert.True(world.Post("/memberships/join", applicant, body).Value<bool>("success"));

            JObject second = world.Post("/memberships/join", applicant, body);
            Assert.False(second.Value<bool>("success"));
            Assert.Equal("existing_invite", second.Value<string>("errorCode"));
        }

        [Fact]
        public void Applying_while_already_in_an_alliance_is_refused()
        {
            World world = new World();
            Guid one = world.Character("Rattus");
            Guid two = world.Character("Mus");
            string mine = Uid(world.Found(one, "Rat Corp"));
            world.Found(two, "Sky Rats");

            JObject applied = world.Post("/memberships/join", two, new JObject
            {
                ["targetId"] = mine,
                ["character"] = U(two),
                ["targetType"] = "alliance_member",
            }.ToString());

            Assert.False(applied.Value<bool>("success"));
            Assert.Equal("already_in_alliance", applied.Value<string>("errorCode"));
        }

        /// <summary>
        /// A permission question, not a leadership one. YourAllianceManagementButtons
        /// shows the APPLICATIONS tab on the strength of edit_members, so a server
        /// that only let the founder admit would show a button that always failed.
        /// </summary>
        [Fact]
        public void An_officer_with_edit_members_may_admit_but_a_plain_member_may_not()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid officer = world.Character("Mus");
            Guid plain = world.Character("Sorex");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            Seat(world, uid, officer);
            Seat(world, uid, plain);

            Assert.False(world.Endpoints.MayAdmit(officer, uid));
            Assert.False(world.Endpoints.MayAdmit(plain, uid));
            Assert.True(world.Endpoints.MayAdmit(founder, uid));

            // Give the officer a rank that grants it, and the answer changes.
            JObject rank = (JObject)world.Post("/rank", founder, new JObject
            {
                ["target"] = uid,
                ["name"] = "Officer",
                ["permissions"] = new JArray { "edit_members" },
            }.ToString())["data"]!;

            world.Patch("/memberships/character/" + U(officer) + "/" + uid, founder,
                new JObject { ["rankUid"] = rank.Value<string>("uid") }.ToString());

            Assert.True(world.Endpoints.MayAdmit(officer, uid));
            Assert.False(world.Endpoints.MayAdmit(plain, uid));
        }

        /// <summary>
        /// An alliance's pending list names players who have not joined it. Only
        /// members see it.
        /// </summary>
        [Fact]
        public void An_outsider_cannot_read_an_alliances_pending_list()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid outsider = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JObject response = world.Get("/memberships/invites/alliance/" + uid, outsider);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));
        }

        // ============================================== description, MOTD, ranks

        [Fact]
        public void The_founder_can_change_the_description_and_the_message_of_the_day()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp", "Rats for life"));

            JObject response = world.Patch("/alliance/" + Region + "/" + uid, founder, new JObject
            {
                ["messageOfTheDay"] = "Squeak louder",
                ["description"] = "Rats forever",
            }.ToString());

            Assert.True(response.Value<bool>("success"));
            Assert.Equal("Squeak louder", response["data"]!.Value<string>("messageOfTheDay"));
            Assert.Equal("Rats forever", response["data"]!.Value<string>("description"));
        }

        /// <summary>
        /// The client sends BOTH keys on every edit whichever box was typed in, so
        /// each field is gated separately and only if it actually changed -
        /// otherwise somebody with edit_group would overwrite the MOTD with their
        /// stale copy every time they touched the description.
        /// </summary>
        [Fact]
        public void A_member_who_may_edit_neither_field_changes_nothing()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid plain = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp", "Rats for life", "Squeak"));
            Seat(world, uid, plain);

            JObject response = world.Patch("/alliance/" + Region + "/" + uid, plain, new JObject
            {
                ["messageOfTheDay"] = "hijacked",
                ["description"] = "hijacked",
            }.ToString());

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));

            JObject after = world.Get("/alliance/" + Region + "/" + uid, founder);
            Assert.Equal("Squeak", after["data"]!.Value<string>("messageOfTheDay"));
            Assert.Equal("Rats for life", after["data"]!.Value<string>("description"));
        }

        /// <summary>
        /// Resending the current values changes nothing and is refused with the
        /// client's own word for it, rather than reported as a save that happened.
        /// </summary>
        [Fact]
        public void An_edit_that_changes_nothing_is_empty_update_payload()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp", "Rats for life", "Squeak"));

            JObject response = world.Patch("/alliance/" + Region + "/" + uid, founder, new JObject
            {
                ["messageOfTheDay"] = "Squeak",
                ["description"] = "Rats for life",
            }.ToString());

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("empty_update_payload", response.Value<string>("errorCode"));
        }

        [Fact]
        public void A_custom_rank_can_be_created_edited_and_deleted()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid member = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, member);

            JObject created = (JObject)world.Post("/rank", founder, new JObject
            {
                ["target"] = uid,
                ["name"] = "Officer",
                ["editable"] = true,
                ["rankType"] = "member",
                ["membershipType"] = "alliance_member",
                ["permissions"] = new JArray { "edit_members", "edit_everything" },
            }.ToString())["data"]!;

            string rankId = created.Value<string>("uid")!;

            // The invented permission is dropped - the vocabulary is closed, and an
            // unknown entry is a button nobody can ever see.
            Assert.Equal(new[] { "edit_members" },
                ((JArray)created["permissions"]!).Select(p => p.Value<string>()));

            // Ranks the client creates are always editable member ranks, whatever
            // it claims, so they cannot displace the two structural defaults.
            Assert.True(created.Value<bool>("editable"));
            Assert.Equal("member", created.Value<string>("rankType"));

            JObject renamed = (JObject)world.Send("PUT", "/rank/" + rankId, founder, new JObject
            {
                ["name"] = "Quartermaster",
                ["permissions"] = new JArray { "edit_group" },
            }.ToString())["data"]!;

            Assert.Equal("Quartermaster", renamed.Value<string>("name"));

            // DELETE rank is the one DELETE the client sends with the DEFAULT
            // dataFieldExpected, i.e. TRUE - an empty envelope throws "Data in
            // server response was empty" at the player.
            JObject deleted = world.Delete("/rank/" + rankId, founder);
            Assert.True(deleted.Value<bool>("success"));
            Assert.NotNull(deleted["data"]);
        }

        [Fact]
        public void A_default_rank_cannot_be_edited_or_deleted()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            JArray ranks = (JArray)world.Get("/ranks/" + uid, founder)["data"]!;
            string leaderRank = ranks.Cast<JObject>()
                .Single(r => r.Value<string>("rankType") == "leader").Value<string>("uid")!;

            Assert.Equal("uneditable_rank",
                world.Delete("/rank/" + leaderRank, founder).Value<string>("errorCode"));

            Assert.Equal("uneditable_rank",
                world.Send("PUT", "/rank/" + leaderRank, founder,
                    new JObject { ["name"] = "Boss" }.ToString()).Value<string>("errorCode"));
        }

        /// <summary>
        /// AllianceClient.TryGetRank THROWS on a rank id absent from
        /// ranks/{allianceUid}, and the throw lands in the handler shared with
        /// crews - so a member stranded on a deleted rank would destroy the whole
        /// Social Sheet, both tabs.
        /// </summary>
        [Fact]
        public void Deleting_a_rank_moves_its_holders_somewhere_the_client_can_look_up()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid member = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, member);

            string rankId = ((JObject)world.Post("/rank", founder, new JObject
            {
                ["target"] = uid,
                ["name"] = "Officer",
                ["permissions"] = new JArray { "edit_members" },
            }.ToString())["data"]!).Value<string>("uid")!;

            world.Patch("/memberships/character/" + U(member) + "/" + uid, founder,
                new JObject { ["rankUid"] = rankId }.ToString());

            world.Delete("/rank/" + rankId, founder);

            JArray ranks = (JArray)world.Get("/ranks/" + uid, founder)["data"]!;
            List<string?> known = ranks.Select(r => ((JObject)r).Value<string>("uid")).ToList();

            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, founder)["data"]!["items"]!;
            foreach (JObject entry in members.Cast<JObject>())
            {
                Assert.Contains(entry.Value<string>("rankId"), known);
            }
        }

        /// <summary>
        /// The officer-note names do not match across directions:
        /// <c>publicOfficerNote</c> goes in, <c>officerNote</c> comes back.
        /// </summary>
        [Fact]
        public void The_public_officer_note_goes_in_under_one_name_and_comes_back_under_another()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid member = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, member);

            JObject response = world.Patch("/memberships/character/" + U(member) + "/" + uid, founder,
                new JObject { ["publicOfficerNote"] = "reliable" }.ToString());

            Assert.True(response.Value<bool>("success"));
            Assert.Equal("reliable", response["data"]!.Value<string>("officerNote"));

            JObject privately = world.Patch("/memberships/character/" + U(member) + "/" + uid, founder,
                new JObject { ["privateOfficerNote"] = "watch them" }.ToString());

            Assert.Equal("watch them", privately["data"]!.Value<string>("privateOfficerNote"));
            Assert.Equal("reliable", privately["data"]!.Value<string>("officerNote"));
        }

        // ================================================= leaving and disbanding

        [Fact]
        public void A_member_can_leave_and_the_founder_can_boot()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid leaver = world.Character("Mus");
            Guid booted = world.Character("Sorex");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, leaver);
            Seat(world, uid, booted);

            Assert.True(world.Delete("/memberships/alliance/" + uid + "/" + U(leaver), leaver)
                .Value<bool>("success"));

            Assert.True(world.Delete("/memberships/alliance/" + uid + "/" + U(booted), founder)
                .Value<bool>("success"));

            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, founder)["data"]!["items"]!;
            Assert.Single(members);
        }

        /// <summary>
        /// The alliance in the PATH has to be the one being acted on. Both policy
        /// questions behind this route resolve the alliance from the ACTOR rather
        /// than from the URL, so without the check an actor in one alliance
        /// sending a path naming another would be answered about their own - a
        /// boot that succeeds against a group the caller never named.
        /// </summary>
        [Fact]
        public void A_boot_addressed_to_the_wrong_alliance_is_refused()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid member = world.Character("Mus");
            Guid stranger = world.Character("Sorex");
            string mine = Uid(world.Found(founder, "Rat Corp"));
            string theirs = Uid(world.Found(stranger, "Sky Rats"));
            Seat(world, mine, member);

            JObject response = world.Delete("/memberships/alliance/" + theirs + "/" + U(member), founder);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("invalid_entity_pair", response.Value<string>("errorCode"));

            // ... and the member is still where they were.
            Assert.Equal(2, ((JArray)world.Get("/memberships/alliance/" + mine, founder)["data"]!["items"]!).Count);
        }

        [Fact]
        public void A_plain_member_cannot_boot_anybody()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid one = world.Character("Mus");
            Guid two = world.Character("Sorex");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, one);
            Seat(world, uid, two);

            JObject response = world.Delete("/memberships/alliance/" + uid + "/" + U(two), one);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));
        }

        [Fact]
        public void The_founder_cannot_be_booted_by_an_officer()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid officer = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, officer);

            string rankId = ((JObject)world.Post("/rank", founder, new JObject
            {
                ["target"] = uid,
                ["name"] = "Officer",
                ["permissions"] = new JArray { "edit_members" },
            }.ToString())["data"]!).Value<string>("uid")!;

            world.Patch("/memberships/character/" + U(officer) + "/" + uid, founder,
                new JObject { ["rankUid"] = rankId }.ToString());

            JObject response = world.Delete("/memberships/alliance/" + uid + "/" + U(founder), officer);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));
        }

        /// <summary>
        /// Leadership is TWO facts - leaderCharacterUid and the rank held - and
        /// moving only one leaves a founder with no permissions or a member the
        /// panel draws as leader.
        /// </summary>
        [Fact]
        public void A_leaving_founder_hands_over_both_the_title_and_the_rank()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid heir = world.Character("Mus");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, heir);

            world.Delete("/memberships/alliance/" + uid + "/" + U(founder), founder);

            JObject alliance = world.Get("/alliance/" + Region + "/" + uid, heir);
            Assert.Equal(U(heir), alliance["data"]!.Value<string>("leaderCharacterUid"));
            Assert.Equal(U(heir), alliance["data"]!["leaderCharacter"]!.Value<string>("uid"));

            JArray ranks = (JArray)world.Get("/ranks/" + uid, heir)["data"]!;
            string leaderRank = ranks.Cast<JObject>()
                .Single(r => r.Value<string>("rankType") == "leader").Value<string>("uid")!;

            JArray members = (JArray)world.Get("/memberships/alliance/" + uid, heir)["data"]!["items"]!;
            Assert.Equal(leaderRank, ((JObject)members[0]).Value<string>("rankId"));
        }

        [Fact]
        public void The_last_member_leaving_dissolves_the_alliance()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            world.Delete("/memberships/alliance/" + uid + "/" + U(founder), founder);

            Assert.Empty((JArray)world.Get("/alliances/" + Region, founder)["data"]!["items"]!);

            // And the founder is free to found another with the same name.
            Assert.True(world.Found(founder, "Rat Corp").Value<bool>("success"));
        }

        [Fact]
        public void Only_the_founder_may_disband_and_it_cancels_the_pending_offers()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            Guid member = world.Character("Mus");
            Guid hopeful = world.Character("Sorex");
            string uid = Uid(world.Found(founder, "Rat Corp"));
            Seat(world, uid, member);

            world.Post("/memberships/join", hopeful, new JObject
            {
                ["targetId"] = uid,
                ["character"] = U(hopeful),
                ["targetType"] = "alliance_member",
            }.ToString());

            Assert.Equal("auth_failed",
                world.Delete("/alliance/" + Region + "/" + uid, member).Value<string>("errorCode"));

            JObject disbanded = world.Delete("/alliance/" + Region + "/" + uid, founder);
            Assert.True(disbanded.Value<bool>("success"));

            // dataFieldExpected:false on this one - no data key is correct, and an
            // explicit JSON null would defeat the client's own null guard.
            Assert.Null(disbanded["data"]);

            Assert.Empty(world.Invites.AllLive());
        }

        // ============================================================ invariants

        /// <summary>
        /// The transport rules, asserted across every alliance response at once. An
        /// array root or an empty body throws in the client before anything is
        /// read, and statusCode / originalResponseData are written by the CLIENT
        /// after parsing - emitting them would be inventing wire fields.
        /// </summary>
        [Fact]
        public void Every_alliance_response_is_a_json_object_with_no_client_injected_fields()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            List<JObject> responses = new List<JObject>
            {
                world.Get("/alliances/" + Region, founder),
                world.Get("/alliance/search/" + Region + "?term=rat", founder),
                world.Get("/alliance/" + Region + "/" + uid, founder),
                world.Get("/alliance/find/" + Region + "/" + U(founder), founder),
                world.Get("/memberships/alliance/" + uid, founder),
                world.Get("/memberships/invites/alliance/" + uid, founder),
                world.Get("/ranks/" + uid, founder),
                world.Get("/alliance/" + Region + "/" + U(Guid.NewGuid()), founder),
            };

            foreach (JObject response in responses)
            {
                Assert.Equal(JTokenType.Object, response.Type);
                Assert.NotNull(response["success"]);
                Assert.Null(response["statusCode"]);
                Assert.Null(response["originalResponseData"]);
            }
        }

        /// <summary>
        /// Every refusal must carry a code from the client's CLOSED table. An
        /// invented one does not produce a slightly-wrong message, it prints
        /// "Unknown error code: whatever_we_invented" in a dialog box.
        /// </summary>
        [Fact]
        public void Every_verdict_maps_to_a_code_the_client_can_look_up()
        {
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal)
            {
                "alliance_at_capacity", "already_a_member", "already_in_alliance", "auth_failed",
                "crew_at_capacity", "duplicate_alliance_name", "dynamo_read", "empty_update_payload",
                "existing_invite", "invalid_entity_id", "invalid_entity_pair", "invalid_name",
                "invite_limit_met", "invite_not_found", "json_deserialization", "no_auth_token",
                "no_ranks_found_in_alliance", "self_invite", "uneditable_rank",
            };

            foreach (AllianceVerdict verdict in Enum.GetValues<AllianceVerdict>())
            {
                Assert.Contains(AllianceEndpoints.VerdictCode(verdict), known);
            }
        }

        /// <summary>
        /// A malformed body is a refusal, not a 500. An exception escaping here
        /// becomes a dropped connection, which the client reports as a transport
        /// modal with no code at all.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        public void A_malformed_create_body_is_refused_rather_than_thrown(string body)
        {
            World world = new World();
            Guid founder = world.Character("Rattus");

            JObject response = world.Post("/alliance", founder, body);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("json_deserialization", response.Value<string>("errorCode"));
        }

        /// <summary>
        /// PROVED, and it is the whole answer to "the crest could not be changed":
        /// the client never sends an emblem. The Create Alliance panel has exactly
        /// three input fields - name, description, message of the day - and the
        /// emblem is a read-only URL the client GETs and turns into a sprite. So
        /// the field is always emitted, and always empty unless an operator sets
        /// it, which leaves the client's own placeholder in place.
        /// </summary>
        [Fact]
        public void The_emblem_url_is_always_emitted_and_starts_empty()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            JObject created = (JObject)world.Found(founder, "Rat Corp")["data"]!;

            Assert.NotNull(created["emblemUrl"]);
            Assert.Equal(string.Empty, created.Value<string>("emblemUrl"));
        }

        /// <summary>
        /// An operator-set crest is served back verbatim, because that is the only
        /// channel there is: SpriteDownloader does a plain GET on the URL with no
        /// auth headers.
        /// </summary>
        [Fact]
        public void An_operator_set_emblem_url_is_served_back_unchanged()
        {
            World world = new World();
            Guid founder = world.Character("Rattus");
            string uid = Uid(world.Found(founder, "Rat Corp"));

            AllianceRecord stored = world.Alliances.FindAlliance(Guid.Parse(uid))!;
            world.Alliances.SaveAlliance(stored with { EmblemUrl = "http://example.invalid/rat.png" });

            JObject alliance = world.Get("/alliance/" + Region + "/" + uid, founder);
            Assert.Equal("http://example.invalid/rat.png", alliance["data"]!.Value<string>("emblemUrl"));
        }

        // --------------------------------------------------------------- helper

        /// <summary>
        /// Puts somebody in an alliance the way the API does: apply, then admit.
        /// Never by writing a row directly - a helper that reimplemented the join
        /// would stop the suite noticing if the real one went missing.
        /// </summary>
        private static void Seat(World world, string allianceUid, Guid character)
        {
            JObject applied = world.Post("/memberships/join", character, new JObject
            {
                ["targetId"] = allianceUid,
                ["character"] = U(character),
                ["targetType"] = "alliance_member",
            }.ToString());

            Assert.True(applied.Value<bool>("success"));

            SocialInviteRecord row = world.Invites.Find(applied["data"]!.Value<string>("id")!)!;
            Assert.True(world.Endpoints.Accept(row).Value<bool>("success"));
            world.Invites.Resolve(row.InviteId, SocialInviteStatus.Accepted, Now);
        }
    }
}
