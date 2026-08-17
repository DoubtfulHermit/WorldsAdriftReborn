using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The server's "which entity id is a ship surface, and whose ship is it" map.
    /// The bridge from a raw 1073 relativeTo to a ship identity.
    /// </summary>
    public class ShipMembershipTests
    {
        [Fact]
        public void A_registered_hull_maps_to_itself_as_its_own_root()
        {
            ShipMembership m = new ShipMembership();
            Assert.True(m.Register(100, 100));

            Assert.Equal(100, m.RootOf(100));
        }

        [Fact]
        public void A_deck_part_maps_to_its_hull()
        {
            // Once Deck01 parts are bolted on, the player stands on the deck part,
            // whose id maps to the hull.
            ShipMembership m = new ShipMembership();
            m.Register(100, 100);   // hull
            m.Register(101, 100);   // a deck part of that hull

            Assert.Equal(100, m.RootOf(101));
            Assert.Equal(100, m.RootOf(100));
        }

        [Fact]
        public void Every_hull_deck_and_mounted_member_canonicalizes_to_one_root()
        {
            ShipMembership m = new ShipMembership();
            m.Register(100, 100);
            m.Register(101, 100);
            m.Register(102, 100);

            Assert.Equal(100, m.RootOf(100));
            Assert.Equal(100, m.RootOf(101));
            Assert.Equal(100, m.RootOf(102));
        }

        [Fact]
        public void Detached_member_no_longer_resolves_to_the_ship()
        {
            ShipMembership m = new ShipMembership();
            m.Register(102, 100);

            Assert.False(m.Unregister(102, expectedRoot: 999));
            Assert.Equal(100, m.RootOf(102));
            Assert.True(m.Unregister(102, expectedRoot: 100));
            Assert.Null(m.RootOf(102));
        }

        [Fact]
        public void An_unregistered_id_is_not_a_ship_surface()
        {
            // The island, a tree, empty air: all "not aboard".
            ShipMembership m = new ShipMembership();
            m.Register(100, 100);

            Assert.Null(m.RootOf(999));
        }

        [Fact]
        public void Re_registering_the_same_pair_is_an_idempotent_no_op()
        {
            // Every joining client walks the identical spawn plan, but there is one
            // ship - the second registration must not throw or duplicate.
            ShipMembership m = new ShipMembership();
            Assert.True(m.Register(100, 100));
            Assert.False(m.Register(100, 100));
            Assert.Equal(100, m.RootOf(100));
        }

        [Fact]
        public void Re_registering_a_surface_to_a_different_root_is_a_spawn_bug()
        {
            ShipMembership m = new ShipMembership();
            m.Register(101, 100);

            Assert.Throws<System.ArgumentException>(() => m.Register(101, 200));
        }

        [Fact]
        public void Empty_and_roots_report_the_registered_ships()
        {
            ShipMembership m = new ShipMembership();
            Assert.True(m.IsEmpty);

            m.Register(100, 100);
            m.Register(101, 100);   // same ship, second surface
            m.Register(200, 200);   // a second ship

            Assert.False(m.IsEmpty);
            Assert.Equal(2, m.Roots().Count);
            Assert.Contains(100L, m.Roots());
            Assert.Contains(200L, m.Roots());
        }
    }
}
