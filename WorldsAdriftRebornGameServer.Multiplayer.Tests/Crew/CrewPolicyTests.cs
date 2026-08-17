using WorldsAdriftRebornGameServer.Multiplayer.Crew;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crew
{
    public sealed class CrewPolicyTests
    {
        private const string Alice = "character:alice";
        private const string Bob = "character:bob";
        private const string Cara = "character:cara";
        private const string Dan = "character:dan";
        private const string CrewId = "crew:1";

        private static CrewLedger WithCrew(out Multiplayer.Crew.Crew crew, int slots = CrewPolicy.DefaultSlots)
        {
            CrewLedger ledger = new CrewLedger();
            crew = ledger.Create(CrewId, Alice, slots);
            return ledger;
        }

        [Fact]
        public void Inviting_while_crewless_is_how_a_crew_is_founded()
        {
            CrewLedger ledger = new CrewLedger();

            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Alice, Bob));
        }

        [Fact]
        public void Only_the_leader_may_invite_into_an_existing_crew()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Invite(Bob, CrewId);
            ledger.Join(Bob, CrewId);

            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Alice, Cara));
            Assert.Equal(CrewVerdict.NotTheLeader, CrewPolicy.MayInvite(ledger, Bob, Cara));
        }

        [Fact]
        public void A_player_cannot_be_poached_out_of_another_crew()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Create("crew:2", Bob);

            Assert.Equal(CrewVerdict.AlreadyInAnotherCrew, CrewPolicy.MayInvite(ledger, Alice, Bob));
        }

        [Theory]
        [InlineData(Alice, Alice, CrewVerdict.CannotInviteYourself)]
        [InlineData(Alice, "", CrewVerdict.UnknownPlayer)]
        [InlineData("", Bob, CrewVerdict.UnknownPlayer)]
        public void Nonsense_invites_are_refused(string from, string to, CrewVerdict expected)
        {
            Assert.Equal(expected, CrewPolicy.MayInvite(new CrewLedger(), from, to));
        }

        [Fact]
        public void A_full_crew_refuses_both_the_invite_and_the_acceptance()
        {
            CrewLedger ledger = WithCrew(out _, slots: 2);
            ledger.Invite(Bob, CrewId);
            ledger.Join(Bob, CrewId);

            Assert.Equal(CrewVerdict.CrewIsFull, CrewPolicy.MayInvite(ledger, Alice, Cara));

            // And the race: an invite issued while there was room, accepted after
            // the seat was taken, must still be refused at acceptance time.
            CrewLedger racing = WithCrew(out _, slots: 2);
            racing.Invite(Cara, CrewId);
            racing.Invite(Bob, CrewId);
            racing.Join(Bob, CrewId);

            Assert.Equal(CrewVerdict.CrewIsFull, CrewPolicy.MayAccept(racing, Cara, CrewId));
        }

        [Fact]
        public void An_invite_can_only_be_accepted_once_and_only_by_its_holder()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Invite(Bob, CrewId);

            Assert.Equal(CrewVerdict.NoSuchInvite, CrewPolicy.MayAccept(ledger, Cara, CrewId));
            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayAccept(ledger, Bob, CrewId));

            ledger.Join(Bob, CrewId);
            Assert.Equal(CrewVerdict.AlreadyInAnotherCrew, CrewPolicy.MayAccept(ledger, Bob, CrewId));
        }

        /// <summary>
        /// Invites live on the invitee, which is what lets one player hold offers
        /// from several crews at once. Accepting one must drop the rest, or they
        /// could accept a second crew afterwards.
        /// </summary>
        [Fact]
        public void Joining_a_crew_drops_every_other_outstanding_invite()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Create("crew:2", Cara);
            ledger.Invite(Bob, CrewId);
            ledger.Invite(Bob, "crew:2");
            Assert.Equal(2, ledger.InvitesFor(Bob).Count);

            ledger.Join(Bob, CrewId);

            Assert.Empty(ledger.InvitesFor(Bob));
            Assert.False(ledger.HasInviteFrom(Bob, "crew:2"));

            // Refused for the MORE useful reason: the membership check runs first,
            // so the player is told they are already crewed rather than that an
            // invite they can still see in their UI has silently expired.
            Assert.Equal(CrewVerdict.AlreadyInAnotherCrew, CrewPolicy.MayAccept(ledger, Bob, "crew:2"));
        }

        [Fact]
        public void Only_the_leader_boots_and_never_themselves()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Join(Bob, CrewId);

            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayBoot(ledger, Alice, Bob));
            Assert.Equal(CrewVerdict.NotTheLeader, CrewPolicy.MayBoot(ledger, Bob, Alice));
            Assert.Equal(CrewVerdict.CannotBootYourself, CrewPolicy.MayBoot(ledger, Alice, Alice));
            Assert.Equal(CrewVerdict.NotAMember, CrewPolicy.MayBoot(ledger, Alice, Cara));
        }

        [Fact]
        public void A_departing_leader_promotes_the_longest_standing_member()
        {
            CrewLedger ledger = WithCrew(out Multiplayer.Crew.Crew crew);
            ledger.Join(Bob, CrewId);
            ledger.Join(Cara, CrewId);

            ledger.Remove(Alice);

            Assert.Equal(Bob, crew.LeaderUid);
            Assert.Equal(new[] { Bob, Cara }, crew.Members);
        }

        [Fact]
        public void The_last_member_leaving_disbands_the_crew_and_its_invites()
        {
            CrewLedger ledger = WithCrew(out _);
            ledger.Invite(Dan, CrewId);

            ledger.Remove(Alice);

            Assert.Null(ledger.ById(CrewId));
            Assert.Empty(ledger.InvitesFor(Dan));
        }

        [Fact]
        public void Slots_are_bounded_and_exclusive()
        {
            CrewLedger ledger = WithCrew(out _, slots: 3);
            ledger.Join(Bob, CrewId);

            Assert.Equal(CrewVerdict.SlotOutOfRange, CrewPolicy.MayTakeSlot(ledger, Alice, -1));
            Assert.Equal(CrewVerdict.SlotOutOfRange, CrewPolicy.MayTakeSlot(ledger, Alice, 3));
            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayTakeSlot(ledger, Alice, 1));

            ledger.TakeSlot(Alice, 1);
            Assert.Equal(CrewVerdict.SlotTaken, CrewPolicy.MayTakeSlot(ledger, Bob, 1));
            // Re-taking your OWN slot is a no-op, not a conflict.
            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayTakeSlot(ledger, Alice, 1));
        }

        [Fact]
        public void Taking_a_second_slot_vacates_the_first()
        {
            CrewLedger ledger = WithCrew(out Multiplayer.Crew.Crew crew);
            ledger.TakeSlot(Alice, 0);
            ledger.TakeSlot(Alice, 2);

            Assert.Equal(2, crew.SlotOf(Alice));
            Assert.Null(crew.OccupantOf(0));
        }

        [Fact]
        public void Leaving_frees_the_slot_you_were_sitting_in()
        {
            CrewLedger ledger = WithCrew(out Multiplayer.Crew.Crew crew);
            ledger.Join(Bob, CrewId);
            ledger.TakeSlot(Bob, 2);

            ledger.Remove(Bob);

            Assert.Null(crew.OccupantOf(2));
        }

        [Fact]
        public void Acting_without_a_crew_says_so_rather_than_throwing()
        {
            CrewLedger ledger = new CrewLedger();

            Assert.Equal(CrewVerdict.NotInACrew, CrewPolicy.MayLeave(ledger, Alice));
            Assert.Equal(CrewVerdict.NotInACrew, CrewPolicy.MayBoot(ledger, Alice, Bob));
            Assert.Equal(CrewVerdict.NotInACrew, CrewPolicy.MayTakeSlot(ledger, Alice, 0));
            ledger.Remove(Alice); // must not throw
        }

        [Fact]
        public void Every_verdict_has_a_line_the_ui_can_show()
        {
            foreach (CrewVerdict verdict in Enum.GetValues<CrewVerdict>())
            {
                string line = CrewPolicy.Explain(verdict);
                Assert.False(string.IsNullOrWhiteSpace(line));
                Assert.NotEqual("That did not work.", line);
            }
        }

        /// <summary>
        /// The grant is what makes the whole crew feature reachable, and its
        /// absence is SILENT: the panel renders, accepts clicks and publishes
        /// nothing, because a client writer binds only for a component the client
        /// holds authority over. Worth a test precisely because nothing else fails
        /// when it is missing.
        /// </summary>
        [Fact]
        public void The_client_is_granted_authority_over_the_crew_action_component()
        {
            Assert.Contains(6901u, MirrorSendPolicy.AuthoritativeComponents);
        }

        /// <summary>
        /// 6900 is the crew's STATE and the server owns it. Granting it would let
        /// a client rewrite its own crew membership, which is the difference
        /// between a crew system and a suggestion.
        /// </summary>
        [Fact]
        public void The_client_is_never_granted_authority_over_crew_state()
        {
            Assert.DoesNotContain(6900u, MirrorSendPolicy.AuthoritativeComponents);
        }
    }
}
