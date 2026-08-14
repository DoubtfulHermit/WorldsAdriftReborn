using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Domains
{
    public sealed class ShipDomainTests
    {
        private static ShipDomain Domain() => new ShipDomain(
            70, 3, new FlightSession(FlightState.AtRestAt(10, 20, 30)));

        [Fact]
        public void Domain_identity_is_stable_and_members_are_whole_ship_state()
        {
            ShipDomain domain = Domain();
            domain.ReplaceMembers(new long[] { 71, 72 }, new long[] { 80, 81 });
            domain.ReplaceAboard(new ulong[] { 9002, 9001, 9002 });

            Assert.Equal(new SimulationDomainId("ship:70"), domain.Id);
            Assert.Equal(new long[] { 71, 72 }, domain.DeckEntityIds);
            Assert.Equal(new long[] { 80, 81 }, domain.MountedPartEntityIds);
            Assert.Equal(new ulong[] { 9001, 9002 }, domain.AboardPeerIds);
        }

        [Fact]
        public void Pilot_handoff_advances_generation_and_rejects_old_authority()
        {
            ShipDomain domain = Domain();
            ShipAuthorityToken first = domain.AcquirePilot(100, 80);
            Assert.True(domain.TrySetInput(first, new FlightControlInput(1, 0, 0, 0, 0)));
            Assert.True(domain.ReleasePilot(first, abandoned: false));

            ShipAuthorityToken second = domain.AcquirePilot(101, 80);

            Assert.True(second.Generation.Value > first.Generation.Value);
            Assert.False(domain.TrySetInput(first, new FlightControlInput(-1, 0, 0, 0, 0)));
            Assert.True(domain.TrySetInput(second, new FlightControlInput(0.5f, 0, 0, 0, 0)));
            Assert.Equal(0.5f, domain.Flight.Input.Throttle);
        }

        [Fact]
        public void Duplicate_acquire_is_idempotent_and_does_not_rotate_authority()
        {
            ShipDomain domain = Domain();
            ShipAuthorityToken first = domain.AcquirePilot(100, 80);
            ShipAuthorityToken duplicate = domain.AcquirePilot(100, 80);

            Assert.Equal(first.Generation, duplicate.Generation);
            Assert.Equal(first.PlayerEntityId, duplicate.PlayerEntityId);
        }

        [Fact]
        public void Capture_restore_preserves_pose_control_timeline_authority_and_membership()
        {
            ShipDomain domain = Domain();
            domain.ReplaceMembers(new long[] { 71, 72 }, new long[] { 80, 81 });
            domain.ReplaceAboard(new ulong[] { 9001 });
            ShipAuthorityToken token = domain.AcquirePilot(100, 80);
            Assert.True(domain.TrySetInput(token, new FlightControlInput(0.75f, 0.2f, 0, 0.1f, 0)));
            FlightEmit before = domain.Flight.Advance(1_000_000, 0.24, new FlightTuning());
            Assert.True(before.Emit);

            ShipDomain restored = ShipDomain.Restore(domain.Capture());

            Assert.Equal(domain.Id, restored.Id);
            Assert.Equal(domain.Generation, restored.Generation);
            Assert.Equal(domain.PersistentIndex, restored.PersistentIndex);
            Assert.Equal(domain.Flight.State.X, restored.Flight.State.X, 9);
            Assert.Equal(domain.Flight.State.Z, restored.Flight.State.Z, 9);
            Assert.Equal(domain.Flight.Input, restored.Flight.Input);
            Assert.Equal(domain.DeckEntityIds, restored.DeckEntityIds);
            Assert.Equal(domain.MountedPartEntityIds, restored.MountedPartEntityIds);
            Assert.Equal(domain.AboardPeerIds, restored.AboardPeerIds);
            FlightEmit after = restored.Flight.Advance(1_000_240, 0.24, new FlightTuning());
            Assert.True(after.Spec.TimestampMs > before.Spec.TimestampMs);
            Assert.True(restored.TrySetInput(token, FlightControlInput.Neutral));
        }

        [Fact]
        public void Snapshot_collections_do_not_alias_the_domain()
        {
            ShipDomain domain = Domain();
            long[] decks = { 71 };
            domain.ReplaceMembers(decks, Array.Empty<long>());
            ShipDomainSnapshot snapshot = domain.Capture();
            decks[0] = 999;
            domain.ReplaceMembers(new long[] { 72 }, Array.Empty<long>());

            Assert.Equal(new long[] { 71 }, snapshot.DeckEntityIds);
        }

        [Fact]
        public void Member_sets_reject_cross_category_duplicates_and_the_hull()
        {
            ShipDomain domain = Domain();
            domain.ReplaceMembers(new long[] { 72 }, new long[] { 80 });
            Assert.Throws<ArgumentException>(() =>
                domain.ReplaceMembers(new long[] { 71 }, new long[] { 71 }));
            Assert.Throws<ArgumentException>(() =>
                domain.ReplaceMembers(new long[] { 70 }, Array.Empty<long>()));
            Assert.Equal(new long[] { 72 }, domain.DeckEntityIds);
            Assert.Equal(new long[] { 80 }, domain.MountedPartEntityIds);
        }

        [Fact]
        public void Registry_hosts_one_domain_per_hull_and_has_a_safe_initial_generation()
        {
            var registry = new ShipDomainRegistry();
            Assert.Equal(AuthorityGeneration.Initial, registry.GenerationFor(70));
            ShipDomain domain = registry.Register(Domain());
            Assert.Same(domain, registry.ByHull(70));
            Assert.Throws<ArgumentException>(() => registry.Register(Domain()));
            Assert.True(registry.Remove(70));
            Assert.Null(registry.ByHull(70));
        }
    }
}
