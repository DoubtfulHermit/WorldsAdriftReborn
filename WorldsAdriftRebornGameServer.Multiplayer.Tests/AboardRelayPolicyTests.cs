using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;
using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class AboardRelayPolicyTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; private set; }
            public void Advance(TimeSpan by) => Elapsed += by;
        }

        [Fact]
        public void Invalid_contact_gap_is_held_while_canonical_tracker_remains_aboard()
        {
            Assert.True(AboardRelayPolicy.HoldRelativeFrame(
                canonicalAboard: true,
                relativeToChanged: true, relativeTo: -1,
                relativeBiasChanged: true, relativeBias: 0f));
        }

        [Fact]
        public void Confirmed_leave_is_relayed_after_tracker_disembarks()
        {
            Assert.False(AboardRelayPolicy.HoldRelativeFrame(
                canonicalAboard: false,
                relativeToChanged: true, relativeTo: -1,
                relativeBiasChanged: true, relativeBias: 0f));
        }

        [Fact]
        public void Valid_ship_contact_and_position_only_updates_are_never_held()
        {
            Assert.False(AboardRelayPolicy.HoldRelativeFrame(
                canonicalAboard: true,
                relativeToChanged: true, relativeTo: 223,
                relativeBiasChanged: true, relativeBias: 1f));
            Assert.False(AboardRelayPolicy.HoldRelativeFrame(
                canonicalAboard: true,
                relativeToChanged: false, relativeTo: 0,
                relativeBiasChanged: false, relativeBias: 0f));
        }

        [Fact]
        public void Tracker_and_relay_hold_the_same_gap_then_release_together()
        {
            const ulong peer = 7;
            const long hull = 70;
            var membership = new ShipMembership();
            membership.Register(hull, hull);
            var clock = new FakeClock();
            var tracker = new AboardTracker(membership, clock);

            tracker.Observe(peer, new AboardSample(
                true, hull, true, 1f, true, true));
            tracker.Observe(peer, new AboardSample(
                true, -1, true, 0f, true, false));
            Assert.True(AboardRelayPolicy.HoldRelativeFrame(
                tracker.IsAboardAnything(peer), true, -1, true, 0f));

            clock.Advance(AboardTracker.ContactGapGrace);
            tracker.Observe(peer, new AboardSample(
                false, 0, false, 0f, false, false));
            Assert.False(AboardRelayPolicy.HoldRelativeFrame(
                tracker.IsAboardAnything(peer), true, -1, true, 0f));
            Assert.True(AboardRelayPolicy.SynthesizeConfirmedDetach(
                AboardChange.Disembarked, relativeToChanged: false));
            Assert.False(AboardRelayPolicy.SynthesizeConfirmedDetach(
                AboardChange.Disembarked, relativeToChanged: true));
        }
    }
}
