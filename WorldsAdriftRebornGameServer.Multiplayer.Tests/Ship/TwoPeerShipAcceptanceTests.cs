using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Headless reproduction of the multiplayer journey that exposed the Colin
    /// regressions.  This intentionally crosses the policy seams in one test:
    /// passenger-relative relay, coherent root/member replication, helm authority
    /// handoff, stale-input rejection, and independent per-peer checkout/re-entry.
    /// It is the deterministic gate below the real two-client visual acceptance run.
    /// </summary>
    public sealed class TwoPeerShipAcceptanceTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; private set; }
            public void Advance(TimeSpan by) => Elapsed += by;
        }

        [Fact]
        public void Owner_observer_handoff_and_reentry_keep_one_coherent_ship_domain()
        {
            const long hull = 200;
            const long deck = 201;
            const long helm = 202;
            const long sail = 203;
            const long ownerPlayer = 300;
            const long observerPlayer = 301;
            const int ownerPeer = 10;
            const int observerPeer = 11;

            var domain = new ShipDomain(hull, 4,
                new FlightSession(FlightState.AtRestAt(0, 0, 0)));
            domain.ReplaceMembers(new[] { deck }, new[] { helm, sail });
            domain.ReplaceAboard(new[] { (ulong)ownerPeer });

            var checkout = new EntitySendLedger<int>();
            IReadOnlyList<long> addOrder = ShipDomainInterestPolicy.AddOrder(
                hull, ShipDomainInterestPolicy.Members(domain.DeckEntityIds,
                    domain.MountedPartEntityIds));
            Assert.Equal(new long[] { hull, deck, helm, sail }, addOrder);
            foreach (long entityId in addOrder)
            {
                checkout.MarkSent(ownerPeer, entityId);
                checkout.MarkSent(observerPeer, entityId);
            }

            // Owner takes control. Every emitted frame is one generation/sequence
            // and members are legal only after the root timeline reached the peer.
            ShipAuthorityToken ownerToken = domain.AcquirePilot(ownerPlayer, helm);
            Assert.True(domain.TrySetInput(ownerToken,
                new FlightControlInput(0.7f, 0.35f, 0, 0, 0)));
            var cursor = new ShipReplicationCursor();
            long previousStamp = 0;
            double previousX = domain.Flight.State.X;
            double previousZ = domain.Flight.State.Z;
            for (int frame = 1; frame <= 120; frame++)
            {
                FlightEmit emitted = domain.Flight.Advance(
                    1_000 + ((frame - 1) * 240), 0.24, new FlightTuning());
                Assert.True(emitted.Emit);
                Assert.True(emitted.Spec.TimestampMs > previousStamp);
                if (previousStamp != 0)
                    Assert.Equal(240, emitted.Spec.TimestampMs - previousStamp);
                previousStamp = emitted.Spec.TimestampMs;

                Assert.True(cursor.TryNext(hull, domain.Generation.Value,
                    out ShipReplicationStamp stamp));
                Assert.Equal(frame, stamp.Sequence);
                foreach (long member in domain.DeckEntityIds)
                    AssertMemberFollowsRoot(member);
                foreach (long member in domain.MountedPartEntityIds)
                    AssertMemberFollowsRoot(member);

                previousX = emitted.Spec.X;
                previousZ = emitted.Spec.Z;
            }
            Assert.True(Math.Abs(previousX) + Math.Abs(previousZ) > 0.1);

            // A momentary collider seam must not make the remote avatar detach
            // from the moving coordinate frame and trail/fly ahead of the ship.
            var membership = new ShipMembership();
            membership.Register(hull, hull);
            membership.Register(deck, hull);
            membership.Register(helm, hull);
            membership.Register(sail, hull);
            var clock = new FakeClock();
            var aboard = new AboardTracker(membership, clock);
            aboard.Observe((ulong)ownerPeer,
                new AboardSample(true, deck, true, 1f, true, true));
            aboard.Observe((ulong)ownerPeer,
                new AboardSample(true, -1, true, 0f, true, false));
            Assert.True(aboard.IsAboardAnything((ulong)ownerPeer));
            Assert.True(AboardRelayPolicy.HoldRelativeFrame(
                canonicalAboard: true, relativeToChanged: true, relativeTo: -1,
                relativeBiasChanged: true, relativeBias: 0f));

            // Clean handoff: the observer becomes the sole authority. Delayed input
            // from the old owner cannot move the hull in the new generation.
            Assert.True(domain.ReleasePilot(ownerToken, abandoned: false));
            ShipAuthorityToken observerToken = domain.AcquirePilot(observerPlayer, helm);
            Assert.True(observerToken.Generation.Value > ownerToken.Generation.Value);
            Assert.False(domain.TrySetInput(ownerToken,
                new FlightControlInput(-1, -1, 0, 0, 0)));
            Assert.True(domain.TrySetInput(observerToken,
                new FlightControlInput(0.4f, -0.25f, 0, 0, 0)));
            Assert.True(cursor.TryNext(hull, domain.Generation.Value, out ShipReplicationStamp handoff));
            Assert.Equal(1, handoff.Sequence);
            Assert.False(cursor.TryNext(hull, ownerToken.Generation.Value, out _));

            for (int frame = 1; frame <= 60; frame++)
            {
                FlightEmit emitted = domain.Flight.Advance(
                    30_000 + ((frame - 1) * 240), 0.24, new FlightTuning());
                Assert.True(emitted.Emit);
                Assert.True(cursor.TryNext(hull, domain.Generation.Value,
                    out ShipReplicationStamp stamp));
                // Sequence 1 was the handoff frame above.
                Assert.Equal(frame + 1, stamp.Sequence);
                foreach (long member in domain.MountedPartEntityIds)
                    AssertMemberFollowsRoot(member);
            }

            // One peer walking out removes only that peer's members/root. The near
            // observer remains checked out and continues to receive the domain.
            foreach (long entityId in ShipDomainInterestPolicy.RemoveOrder(
                hull, ShipDomainInterestPolicy.Members(domain.DeckEntityIds,
                    domain.MountedPartEntityIds)))
                checkout.ForgetEntity(ownerPeer, entityId);

            Assert.False(checkout.WasSent(ownerPeer, hull));
            Assert.True(checkout.WasSent(observerPeer, hull));
            Assert.All(new[] { deck, helm, sail }, id =>
                Assert.True(checkout.WasSent(observerPeer, id)));

            // Returning peer receives the authoritative root first and every current
            // member after it; no stale, component-only ghost can be reconstructed.
            foreach (long entityId in addOrder)
                checkout.MarkSent(ownerPeer, entityId);
            Assert.True(checkout.WasSent(ownerPeer, hull));
            Assert.All(new[] { deck, helm, sail }, id =>
                Assert.True(checkout.WasSent(ownerPeer, id)));

            void AssertMemberFollowsRoot(long member)
            {
                Assert.True(ShipDomainDeliveryPolicy.DeliverMember(
                    domainRelevant: true,
                    rootDelivered: checkout.WasSent(observerPeer, hull),
                    auxiliaryRequired: true,
                    auxiliaryDelivered: true,
                    memberCheckedOut: checkout.WasSent(observerPeer, member)));
            }
        }
    }
}
