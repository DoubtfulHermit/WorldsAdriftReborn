using WorldsAdriftRebornGameServer.Multiplayer.Crew;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Crew
{
    /// <summary>
    /// The bound that stops a crew handing the Social Sheet more entries than it
    /// can draw.
    ///
    /// The client concatenates members and outstanding invites into one list and
    /// draws it into a fixed set of five non-leader widgets. One entry too many
    /// indexes past the end, and the throw lands in the handler shared with
    /// alliances - so an over-invited crew takes out the entire sheet, both tabs.
    /// </summary>
    public sealed class CrewRosterLimitsTests
    {
        private const string Leader = "character:leader";
        private const string CrewId = "crew:1";

        [Fact]
        public void TheClientBoundIsAFullLeaderPlusFiveBars()
        {
            Assert.Equal(5, CrewRosterLimits.ClientNonLeaderRows);
            Assert.Equal(6, CrewRosterLimits.ClientRenderableEntries);
        }

        // ---- the game rule: never offer a seat that does not exist ------------

        [Theory]
        [InlineData(4, 1, 3)]   // the default crew, leader alone
        [InlineData(4, 2, 2)]
        [InlineData(4, 4, 0)]   // full
        [InlineData(4, 5, 0)]   // over-full, never negative
        public void InvitesAreCappedByTheFreeSeats(int slots, int members, int expected)
        {
            Assert.Equal(expected, CrewRosterLimits.MaxLiveInvites(slots, members));
        }

        // ---- the client rule, which is NOT redundant --------------------------

        /// <summary>
        /// CrewPolicy.MaxSlots is 8, so seats alone would let a crew promise more
        /// entries than the panel can draw. The renderable bound has to win.
        /// </summary>
        [Theory]
        [InlineData(8, 1, 5)]
        [InlineData(7, 1, 5)]
        [InlineData(8, 3, 3)]
        [InlineData(8, 6, 0)]
        public void InvitesAreAlsoCappedByWhatTheSheetCanDraw(int slots, int members, int expected)
        {
            Assert.Equal(expected, CrewRosterLimits.MaxLiveInvites(slots, members));
        }

        [Theory]
        [InlineData(8, 1)]
        [InlineData(8, 2)]
        [InlineData(6, 1)]
        [InlineData(4, 1)]
        public void MembersPlusTheMaximumInvitesNeverExceedWhatCanBeDrawn(int slots, int members)
        {
            int total = members + CrewRosterLimits.MaxLiveInvites(slots, members);

            Assert.True(total <= CrewRosterLimits.ClientRenderableEntries,
                $"{members} members + invites = {total}, past the sheet's {CrewRosterLimits.ClientRenderableEntries}");
        }

        [Fact]
        public void TheLastPermittedInviteFitsAndTheNextDoesNot()
        {
            Assert.True(CrewRosterLimits.MayHoldAnotherInvite(4, 1, 2));
            Assert.False(CrewRosterLimits.MayHoldAnotherInvite(4, 1, 3));
        }

        // ---- the defensive clamp on what actually goes out the door -----------

        [Theory]
        [InlineData(0, 0)]
        [InlineData(4, 4)]
        [InlineData(6, 6)]
        [InlineData(9, 6)]   // rows written before the cap existed
        public void MemberRowsAreClampedToWhatCanBeDrawn(int stored, int emitted)
        {
            Assert.Equal(emitted, CrewRosterLimits.EmittableMembers(stored));
        }

        [Theory]
        [InlineData(1, 3, 3)]
        [InlineData(1, 9, 5)]   // legacy over-invited crew
        [InlineData(6, 4, 0)]   // members already fill the panel
        [InlineData(9, 4, 0)]
        [InlineData(3, 0, 0)]
        public void InviteRowsYieldToMemberRows(int members, int liveInvites, int emitted)
        {
            Assert.Equal(emitted, CrewRosterLimits.EmittableInvites(members, liveInvites));
        }

        [Theory]
        [InlineData(1, 9)]
        [InlineData(9, 9)]
        [InlineData(6, 1)]
        [InlineData(0, 0)]
        public void WhatIsEmittedIsAlwaysDrawable(int members, int invites)
        {
            int total = CrewRosterLimits.EmittableMembers(members)
                      + CrewRosterLimits.EmittableInvites(members, invites);

            Assert.True(total <= CrewRosterLimits.ClientRenderableEntries,
                $"emitting {total} entries, past the sheet's {CrewRosterLimits.ClientRenderableEntries}");
        }

        // ---- the policy actually enforces it ---------------------------------

        /// <summary>
        /// REGRESSION. MayInvite counted seated members and nothing else, so a
        /// leader alone in a four-slot crew could send invite after invite and
        /// every one was allowed. The seventh entry killed the sheet.
        /// </summary>
        [Fact]
        public void ALeaderCannotInviteMorePeopleThanTheCrewHasSeats()
        {
            CrewLedger ledger = new CrewLedger();
            ledger.Create(CrewId, Leader);

            for (int i = 0; i < 3; i++)
            {
                string invitee = "character:invitee" + i;
                Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Leader, invitee));
                ledger.Invite(invitee, CrewId);
            }

            Assert.Equal(CrewVerdict.InviteLimitMet,
                CrewPolicy.MayInvite(ledger, Leader, "character:oneTooMany"));
        }

        [Fact]
        public void TheLimitCountsInvitesAndMembersTogether()
        {
            CrewLedger ledger = new CrewLedger();
            ledger.Create(CrewId, Leader);
            ledger.Join("character:member", CrewId);      // 2 of 4 seats used

            ledger.Invite("character:a", CrewId);
            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Leader, "character:b"));

            ledger.Invite("character:b", CrewId);         // 2 members + 2 invites = 4
            Assert.Equal(CrewVerdict.InviteLimitMet,
                CrewPolicy.MayInvite(ledger, Leader, "character:c"));
        }

        [Fact]
        public void CancellingAnInviteFreesItsSeatAgain()
        {
            CrewLedger ledger = new CrewLedger();
            ledger.Create(CrewId, Leader);
            ledger.Invite("character:a", CrewId);
            ledger.Invite("character:b", CrewId);
            ledger.Invite("character:c", CrewId);

            Assert.Equal(CrewVerdict.InviteLimitMet,
                CrewPolicy.MayInvite(ledger, Leader, "character:d"));

            ledger.CancelInvite("character:c", CrewId);
            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Leader, "character:d"));
        }

        [Fact]
        public void AnotherCrewsInvitesDoNotCountAgainstThisOne()
        {
            CrewLedger ledger = new CrewLedger();
            ledger.Create(CrewId, Leader);
            ledger.Create("crew:2", "character:otherLeader");

            ledger.Invite("character:x", "crew:2");
            ledger.Invite("character:y", "crew:2");
            ledger.Invite("character:z", "crew:2");

            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Leader, "character:a"));
        }

        [Fact]
        public void TheLedgerCountsOneCrewsOutstandingOffers()
        {
            CrewLedger ledger = new CrewLedger();
            ledger.Create(CrewId, Leader);

            Assert.Equal(0, ledger.LiveInvitesFor(CrewId));

            ledger.Invite("character:a", CrewId);
            ledger.Invite("character:b", CrewId);
            Assert.Equal(2, ledger.LiveInvitesFor(CrewId));

            ledger.CancelInvite("character:a", CrewId);
            Assert.Equal(1, ledger.LiveInvitesFor(CrewId));
        }

        /// <summary>
        /// Founding is still allowed while crewless - there is no crew yet to be
        /// full, and that is how a crew comes into existence at all.
        /// </summary>
        [Fact]
        public void TheLimitDoesNotBlockFoundingACrew()
        {
            CrewLedger ledger = new CrewLedger();

            Assert.Equal(CrewVerdict.Ok, CrewPolicy.MayInvite(ledger, Leader, "character:first"));
        }
    }
}
