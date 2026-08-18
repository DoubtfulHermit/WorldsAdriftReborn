using Newtonsoft.Json.Linq;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Tests;
using WorldsAdriftServer.Social;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The social API end to end: a request in, an envelope out, real rows in a
    /// real PostgreSQL in between.
    ///
    /// Against a real database rather than a fake store, for the same reason the
    /// storage suite is: the constraints ARE the design here - one crew per
    /// character, one live invite per pair - and a fake that accepted what
    /// Postgres refuses would let a broken contract pass green.
    ///
    /// Each test walks the same path the client walks, in the same order, because
    /// the crew panel's reads are a CHAIN: memberships -> crew -> members ->
    /// invites, and each link takes its argument from the previous response. A
    /// test that jumped straight to the last call would not notice a crew id that
    /// never survives the first one.
    /// </summary>
    public class SocialServiceTests
    {
        private const string Region = "community_server";

        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        private sealed class World : IDisposable
        {
            internal TempDb Db { get; }
            internal SocialService Service { get; }

            internal World()
            {
                Db = new TempDb();
                Service = new SocialService(
                    Db.Characters, Db.Crews, Db.SocialInvites, Db.Alliances, Region, () => Now);
            }

            internal Guid Character(string name)
            {
                AccountRecord account = Db.AnAccount(name.ToLowerInvariant());
                CharacterRecord character = TempDb.ACharacter(account.AccountId, name);
                Db.Characters.Save(character);
                return character.CharacterUid;
            }

            internal JObject Get(string url, Guid actor) => Send("GET", url, actor, null);
            internal JObject Post(string url, Guid actor, string? body = null) => Send("POST", url, actor, body);
            internal JObject Put(string url, Guid actor) => Send("PUT", url, actor, null);
            internal JObject Delete(string url, Guid actor) => Send("DELETE", url, actor, null);

            internal JObject Send(string method, string url, Guid actor, string? body)
            {
                SocialRoute route = SocialRoute.Parse(method, url);
                Assert.NotEqual(SocialRouteKind.None, route.Kind);
                return Service.Handle(route, actor, url, body);
            }

            public void Dispose() => Db.Dispose();
        }

        private static string U(Guid uid) => uid.ToString("D");

        // ------------------------------------------------------------ the open

        /// <summary>
        /// The request the whole Social Sheet hangs off, for a player in nothing.
        ///
        /// This is the response that used to be impossible to get - the dead host
        /// threw, and the shared exception handler covered the CREW tab with
        /// "Can't retrieve alliance or crew data". It has to be a success with a
        /// present data object and NO alliance key.
        /// </summary>
        [PostgresFact]
        public void APlayerInNothingGetsASuccessWithNoAllianceAndNoCrew()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject response = world.Get("/memberships/character/" + U(me), me);

            Assert.True(response.Value<bool>("success"));
            Assert.NotNull(response["data"]);
            Assert.Null(response["data"]!["alliance"]);
            Assert.Null(response["data"]!["crew"]);
            Assert.Equal(U(me), response["data"]!.Value<string>("character"));
            Assert.Equal("Billy", response["data"]!["member"]!.Value<string>("name"));
        }

        [PostgresFact]
        public void APlayerWithNoInvitesGetsAnEmptyItemsList()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject response = world.Get("/memberships/invites/character/" + U(me), me);

            Assert.True(response.Value<bool>("success"));
            Assert.Empty((JArray)response["data"]!["items"]!);
        }

        // ----------------------------------------------------------- the crew

        [PostgresFact]
        public void CreatingACrewMakesItVisibleThroughTheClientsOwnReadChain()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject created = world.Post("/crews", me);
            Assert.True(created.Value<bool>("success"));

            string crewId = created["data"]!.Value<string>("uid")!;

            // 1. memberships/character now reports a crew, and the client takes
            //    the crew id from targetId - not from anywhere else.
            JObject memberships = world.Get("/memberships/character/" + U(me), me);
            Assert.Equal(crewId, memberships["data"]!["crew"]!.Value<string>("targetId"));

            // 2. crew/{region}/{uid} names the leader, and the client compares
            //    that string against each member's uid to decide who leads.
            JObject crew = world.Get("/crew/" + Region + "/" + crewId, me);
            Assert.Equal(U(me), crew["data"]!.Value<string>("leaderCharacterUid"));
            Assert.Equal(U(me), crew["data"]!["leaderCharacter"]!.Value<string>("uid"));

            // 3. memberships/crew lists the member - as a BARE array.
            JObject members = world.Get("/memberships/crew/" + crewId, me);
            JArray list = (JArray)members["data"]!;
            Assert.Single(list);
            Assert.Equal(U(me), list[0]["member"]!.Value<string>("uid"));
            Assert.Equal("Billy", list[0]["member"]!.Value<string>("name"));
        }

        /// <summary>
        /// The leader uid in the crew response and the member uid in the member
        /// list must be BYTE IDENTICAL, because the client's leadership test is an
        /// ordinal string comparison between two different responses of ours. A
        /// casing difference here presents as "the crew has no leader".
        /// </summary>
        [PostgresFact]
        public void TheLeaderUidMatchesTheMemberUidExactly()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            string crewId = world.Post("/crews", me)["data"]!.Value<string>("uid")!;

            string leader = world.Get("/crew/" + Region + "/" + crewId, me)["data"]!
                .Value<string>("leaderCharacterUid")!;
            string member = ((JArray)world.Get("/memberships/crew/" + crewId, me)["data"]!)[0]!["member"]!
                .Value<string>("uid")!;

            Assert.Equal(leader, member, StringComparer.Ordinal);
        }

        [PostgresFact]
        public void APlayerCannotFoundASecondCrewWhileInOne()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            world.Post("/crews", me);
            JObject second = world.Post("/crews", me);

            Assert.False(second.Value<bool>("success"));
            Assert.Equal("already_a_member", second.Value<string>("errorCode"));
        }

        // ---------------------------------------------------------- invitations

        [PostgresFact]
        public void TheWholeInviteRoundTripWorks()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid joiner = world.Character("Bones");

            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            JObject sent = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(joiner),
                ["targetType"] = "crew_member",
                ["inviter"] = U(leader),
                ["region"] = Region,
            }.ToString());

            Assert.True(sent.Value<bool>("success"));
            string inviteId = sent["data"]!.Value<string>("id")!;

            // The invitee sees it, with an inviter object - which is what makes
            // the client file it as an INVITE rather than an application.
            JObject theirs = world.Get("/memberships/invites/character/" + U(joiner), joiner);
            JArray items = (JArray)theirs["data"]!["items"]!;
            Assert.Single(items);
            Assert.Equal("new", items[0].Value<string>("status"));
            Assert.Equal(U(leader), items[0]["inviter"]!.Value<string>("uid"));

            // The crew sees it as a pending slot.
            JArray pending = (JArray)world.Get("/memberships/invites/crew/" + crewId, leader)["data"]!["items"]!;
            Assert.Single(pending);

            // Accepting joins them.
            JObject accepted = world.Put(
                "/memberships/invite/accept/" + inviteId + "/" + U(joiner) + "/" + Region, joiner);
            Assert.True(accepted.Value<bool>("success"));

            JArray members = (JArray)world.Get("/memberships/crew/" + crewId, joiner)["data"]!;
            Assert.Equal(2, members.Count);

            // And the invite is no longer pending, so the crew does not report
            // itself a seat short.
            Assert.Empty((JArray)world.Get("/memberships/invites/crew/" + crewId, leader)["data"]!["items"]!);
        }

        [PostgresFact]
        public void ASecondLiveInviteToTheSamePlayerIsRefused()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid joiner = world.Character("Bones");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            string body = new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(joiner),
                ["targetType"] = "crew_member",
                ["inviter"] = U(leader),
            }.ToString();

            Assert.True(world.Post("/memberships/invite", leader, body).Value<bool>("success"));

            JObject again = world.Post("/memberships/invite", leader, body);
            Assert.False(again.Value<bool>("success"));
            Assert.Equal("existing_invite", again.Value<string>("errorCode"));
        }

        [PostgresFact]
        public void InvitingYourselfIsRefusedWithTheClientsOwnWordForIt()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            JObject response = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(leader),
                ["targetType"] = "crew_member",
            }.ToString());

            Assert.Equal("self_invite", response.Value<string>("errorCode"));
        }

        /// <summary>
        /// Only the invitee may answer their own invite. The uid is in the URL, so
        /// without this check anyone could accept on anyone's behalf.
        /// </summary>
        [PostgresFact]
        public void SomebodyElseCannotAcceptYourInvite()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid joiner = world.Character("Bones");
            Guid stranger = world.Character("Silver");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            string inviteId = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(joiner),
                ["targetType"] = "crew_member",
            }.ToString())["data"]!.Value<string>("id")!;

            JObject response = world.Put(
                "/memberships/invite/accept/" + inviteId + "/" + U(joiner) + "/" + Region, stranger);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));
        }

        [PostgresFact]
        public void AnInviteCanOnlyBeAnsweredOnce()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid joiner = world.Character("Bones");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            string inviteId = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(joiner),
                ["targetType"] = "crew_member",
            }.ToString())["data"]!.Value<string>("id")!;

            string url = "/memberships/invite/reject/" + inviteId + "/" + U(joiner);
            Assert.True(world.Put(url, joiner).Value<bool>("success"));

            JObject again = world.Put(url, joiner);
            Assert.False(again.Value<bool>("success"));
            Assert.Equal("invite_not_found", again.Value<string>("errorCode"));
        }

        // --------------------------------------------------------- leaving etc.

        /// <summary>
        /// Boot and leave are the SAME request; only the actor differs. So the
        /// ownership rule has to live in the server, not in which button was
        /// pressed.
        /// </summary>
        [PostgresFact]
        public void AMemberCannotBootTheLeader()
        {
            using World world = new World();
            (Guid leader, Guid member, string crewId) = ACrewOfTwo(world);

            JObject response = world.Delete(
                "/memberships/crew/" + crewId + "/" + U(leader), member);

            Assert.False(response.Value<bool>("success"));
            Assert.Equal("auth_failed", response.Value<string>("errorCode"));

            // And nobody was removed.
            Assert.Equal(2, ((JArray)world.Get("/memberships/crew/" + crewId, leader)["data"]!).Count);
        }

        [PostgresFact]
        public void TheLeaderCanBootAMember()
        {
            using World world = new World();
            (Guid leader, Guid member, string crewId) = ACrewOfTwo(world);

            JObject response = world.Delete(
                "/memberships/crew/" + crewId + "/" + U(member), leader);

            Assert.True(response.Value<bool>("success"));

            // The crew endpoint is sent with dataFieldExpected TRUE, unlike its
            // alliance twin, so an empty envelope would throw "Data in server
            // response was empty" at the player.
            Assert.NotNull(response["data"]);

            Assert.Single((JArray)world.Get("/memberships/crew/" + crewId, leader)["data"]!);
            Assert.Null(world.Get("/memberships/character/" + U(member), member)["data"]!["crew"]);
        }

        [PostgresFact]
        public void AMemberCanLeaveOnTheirOwn()
        {
            using World world = new World();
            (Guid leader, Guid member, string crewId) = ACrewOfTwo(world);

            Assert.True(world.Delete(
                "/memberships/crew/" + crewId + "/" + U(member), member).Value<bool>("success"));

            Assert.Null(world.Get("/memberships/character/" + U(member), member)["data"]!["crew"]);
            Assert.NotNull(world.Get("/memberships/character/" + U(leader), leader)["data"]!["crew"]);
        }

        /// <summary>
        /// Succession is CrewPolicy's decision, not this service's. The test is
        /// here because the DATABASE has to end up agreeing with whatever the
        /// ledger decided - the game server reads these rows back at boot.
        /// </summary>
        [PostgresFact]
        public void ALeavingLeaderHandsTheCrewToTheRemainingMember()
        {
            using World world = new World();
            (Guid leader, Guid member, string crewId) = ACrewOfTwo(world);

            Assert.True(world.Delete(
                "/memberships/crew/" + crewId + "/" + U(leader), leader).Value<bool>("success"));

            JObject crew = world.Get("/crew/" + Region + "/" + crewId, member);
            Assert.Equal(U(member), crew["data"]!.Value<string>("leaderCharacterUid"));
        }

        [PostgresFact]
        public void TheLastMemberLeavingDisbandsTheCrew()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            world.Delete("/memberships/crew/" + crewId + "/" + U(leader), leader);

            Assert.Null(world.Get("/memberships/character/" + U(leader), leader)["data"]!["crew"]);
            Assert.False(world.Get("/crew/" + Region + "/" + crewId, leader).Value<bool>("success"));
        }

        [PostgresFact]
        public void OnlyTheLeaderCanDisband()
        {
            using World world = new World();
            (Guid leader, Guid member, string crewId) = ACrewOfTwo(world);

            Assert.False(world.Delete("/crew/" + Region + "/" + crewId, member).Value<bool>("success"));
            Assert.True(world.Delete("/crew/" + Region + "/" + crewId, leader).Value<bool>("success"));
        }

        /// <summary>
        /// An outstanding offer to join something that no longer exists would sit
        /// in its invitee's list forever, and answering it would try to join a
        /// deleted crew.
        /// </summary>
        [PostgresFact]
        public void DisbandingCancelsTheCrewsOutstandingInvites()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid invitee = world.Character("Bones");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(invitee),
                ["targetType"] = "crew_member",
            }.ToString());

            world.Delete("/crew/" + Region + "/" + crewId, leader);

            // Gone from the invitee's list entirely, not merely marked cancelled
            // in it. This used to assert the opposite - one entry with
            // status "cancelled" - on the reasoning that the client filters on
            // "new" itself. The CREW reader does; the two ALLIANCE readers of this
            // same endpoint do not, and one of them decides whether the APPLY
            // button is offered at all, so a resolved row left in the list bars
            // that player from ever applying again. See InvitesForCharacter.
            JArray theirs = (JArray)world.Get(
                "/memberships/invites/character/" + U(invitee), invitee)["data"]!["items"]!;

            Assert.Empty(theirs);

            // Cancelled rather than deleted, though: the row is still there, and
            // it still records that the offer was made and withdrawn.
            IReadOnlyList<SocialInviteRecord> stored = world.Db.SocialInvites.ForCharacter(invitee);
            Assert.Single(stored);
            Assert.Equal(SocialInviteStatus.Cancelled, stored[0].Status);
        }

        /// <summary>
        /// The same rule from the other direction: an invite the player REJECTED
        /// must leave their list, or the alliance UI keeps offering them a JOIN
        /// that answers invite_not_found and keeps refusing them a fresh APPLY.
        /// </summary>
        [PostgresFact]
        public void ARejectedInviteLeavesTheInviteesList()
        {
            using World world = new World();
            Guid leader = world.Character("Billy");
            Guid invitee = world.Character("Bones");
            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            string inviteId = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(invitee),
                ["targetType"] = "crew_member",
            }.ToString())["data"]!.Value<string>("id")!;

            Assert.Single((JArray)world.Get(
                "/memberships/invites/character/" + U(invitee), invitee)["data"]!["items"]!);

            world.Put("/memberships/invite/reject/" + inviteId + "/" + U(invitee), invitee);

            Assert.Empty((JArray)world.Get(
                "/memberships/invites/character/" + U(invitee), invitee)["data"]!["items"]!);
        }

        /// <summary>
        /// A crew id the client is still holding after somebody else disbanded.
        /// An empty list lets the panel fall back to its no-crew state; an error
        /// would trap the player in a dialog they cannot act on.
        /// </summary>
        [PostgresFact]
        public void AStaleCrewIdListsNobodyRatherThanFailing()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject response = world.Get("/memberships/crew/crew:gone", me);

            Assert.True(response.Value<bool>("success"));
            Assert.Empty((JArray)response["data"]!);
        }

        // ---------------------------------------------------------- name search

        [PostgresFact]
        public void SearchingByNameFindsTheCharacterTheInviteFlowNeeds()
        {
            using World world = new World();
            Guid them = world.Character("Bones");
            Guid me = world.Character("Billy");

            JObject response = world.Get("/screenname/find/Bones", me);

            Assert.True(response.Value<bool>("success"));
            Assert.Equal(U(them), response["screenname"]!.Value<string>("characterUid"));
        }

        [PostgresFact]
        public void SearchingForNobodyExplainsItselfInDescNotAnErrorCode()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject response = world.Get("/screenname/find/Nobody", me);

            Assert.False(response.Value<bool>("success"));
            Assert.False(string.IsNullOrWhiteSpace(response.Value<string>("desc")));
            Assert.Null(response["errorCode"]);
        }

        // ------------------------------------------------------------ alliances

        /// <summary>
        /// This server hosts no alliances, so an empty list is the TRUTH rather
        /// than a stand-in. It leaves the alliance browser rendering correctly and
        /// empty instead of throwing a dialog, without pretending an alliance
        /// feature exists.
        /// </summary>
        [PostgresFact]
        public void TheAllianceBrowserGetsAnHonestlyEmptyList()
        {
            using World world = new World();
            Guid me = world.Character("Billy");

            JObject listed = world.Get("/alliances/" + Region, me);
            Assert.True(listed.Value<bool>("success"));
            Assert.Empty((JArray)listed["data"]!["items"]!);

            JObject searched = world.Get("/alliance/search/" + Region + "?term=x", me);
            Assert.True(searched.Value<bool>("success"));
            Assert.Empty((JArray)searched["data"]!);
        }

        // ---------------------------------------------------------------- setup

        private static (Guid Leader, Guid Member, string CrewId) ACrewOfTwo(World world)
        {
            Guid leader = world.Character("Billy");
            Guid member = world.Character("Bones");

            string crewId = world.Post("/crews", leader)["data"]!.Value<string>("uid")!;

            string inviteId = world.Post("/memberships/invite", leader, new JObject
            {
                ["targetId"] = crewId,
                ["character"] = U(member),
                ["targetType"] = "crew_member",
                ["inviter"] = U(leader),
            }.ToString())["data"]!.Value<string>("id")!;

            world.Put("/memberships/invite/accept/" + inviteId + "/" + U(member) + "/" + Region, member);

            return (leader, member, crewId);
        }
    }
}
