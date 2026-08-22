using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The shipyard&lt;-&gt;built-hull dock association that BuiltShips delegates to. The
    /// FORWARD direction feeds the shipyard's 1205 DockedShipId (and the one-ship-per-yard
    /// CRAFT gate); the REVERSE direction feeds the hull's own 1114 DockableState.DockEntityId,
    /// which is what lets the client's DockableVisualizer enable so the shipyard presents an
    /// "active docked ship" for the crafted-part lift. The property that matters and only
    /// fails on a live client is that the two directions never disagree through
    /// set/overwrite/clear - pinned here.
    /// </summary>
    public class ShipDockRegistryTests
    {
        [Fact]
        public void Empty_registry_reports_no_dock_in_either_direction()
        {
            var docks = new ShipDockRegistry();

            Assert.Equal(0, docks.DockedShipFor(shipyardEntityId: 100));
            Assert.Equal(0, docks.ShipyardForHull(hullEntityId: 200));
            Assert.False(docks.IsShipyardOccupied(100));
            Assert.False(docks.IsHullDocked(200));
            Assert.Empty(docks.OccupiedShipyards);
        }

        [Fact]
        public void Docking_reports_both_directions()
        {
            var docks = new ShipDockRegistry();

            docks.SetDocked(shipyardEntityId: 100, hullEntityId: 200);

            Assert.Equal(200, docks.DockedShipFor(100));
            Assert.Equal(100, docks.ShipyardForHull(200)); // the hull's 1114 dock entity id
            Assert.True(docks.IsShipyardOccupied(100));
            Assert.True(docks.IsHullDocked(200));
            Assert.Equal(new long[] { 100 }, docks.OccupiedShipyards.ToArray());
        }

        [Fact]
        public void Re_docking_a_new_hull_at_a_yard_drops_the_old_hulls_reverse_entry()
        {
            var docks = new ShipDockRegistry();
            docks.SetDocked(100, 200);

            docks.SetDocked(100, 201); // a fresh build replaces the old ship at the same yard

            Assert.Equal(201, docks.DockedShipFor(100));
            Assert.Equal(100, docks.ShipyardForHull(201));
            // The old hull must no longer claim to be docked anywhere - the two maps agree.
            Assert.Equal(0, docks.ShipyardForHull(200));
            Assert.False(docks.IsHullDocked(200));
        }

        [Fact]
        public void Moving_a_hull_to_a_new_yard_drops_the_old_yards_forward_entry()
        {
            var docks = new ShipDockRegistry();
            docks.SetDocked(100, 200);

            docks.SetDocked(101, 200); // same hull, different yard

            Assert.Equal(101, docks.ShipyardForHull(200));
            Assert.Equal(200, docks.DockedShipFor(101));
            // The old yard must no longer report the hull as docked.
            Assert.Equal(0, docks.DockedShipFor(100));
            Assert.False(docks.IsShipyardOccupied(100));
        }

        [Fact]
        public void Clearing_a_dock_frees_both_sides_and_returns_the_hull()
        {
            var docks = new ShipDockRegistry();
            docks.SetDocked(100, 200);

            long cleared = docks.ClearDocked(100);

            Assert.Equal(200, cleared);
            Assert.Equal(0, docks.DockedShipFor(100));
            Assert.Equal(0, docks.ShipyardForHull(200));
            Assert.False(docks.IsShipyardOccupied(100));
            Assert.False(docks.IsHullDocked(200));
        }

        [Fact]
        public void Clearing_an_empty_yard_is_a_harmless_zero()
        {
            var docks = new ShipDockRegistry();
            Assert.Equal(0, docks.ClearDocked(100));
        }

        [Fact]
        public void Distinct_yards_hold_distinct_ships_independently()
        {
            var docks = new ShipDockRegistry();
            docks.SetDocked(100, 200);
            docks.SetDocked(101, 201);

            Assert.Equal(200, docks.DockedShipFor(100));
            Assert.Equal(201, docks.DockedShipFor(101));
            Assert.Equal(2, docks.OccupiedShipyards.Count);
        }

        [Fact]
        public void Shared_is_a_single_stable_instance()
        {
            Assert.Same(ShipDockRegistry.Shared, ShipDockRegistry.Shared);
        }

        [Fact]
        public void Try_claim_never_overwrites_a_competing_live_claim()
        {
            var docks = new ShipDockRegistry();

            Assert.Equal(ShipDockClaimResult.Claimed, docks.TryClaim(100, 200));
            Assert.Equal(ShipDockClaimResult.AlreadyClaimed, docks.TryClaim(100, 200));
            Assert.Equal(ShipDockClaimResult.RejectedYardOccupied, docks.TryClaim(100, 201));
            Assert.Equal(ShipDockClaimResult.RejectedHullLinked, docks.TryClaim(101, 200));
            Assert.Equal(200, docks.DockedShipFor(100));
            Assert.Equal(100, docks.ShipyardForHull(200));
        }
    }
}
