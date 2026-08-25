using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The bubble's geometry on its own. Provenance under test: the 35 m radius is
    /// the RECOVERED Shipyard.ImpactRadius default and the sphere test is the
    /// recovered Shipyard.IsWithinRange; the dome floor and the exit margin are
    /// WAReborn tuning and are pinned here so a silent edit is a red test, not a
    /// live surprise.
    /// </summary>
    public sealed class ShipyardBubbleTests
    {
        private static readonly ShadowVector3 Yard = new(1000, 200, -400);
        private static readonly DockingTuning Tuning = new();
        private static readonly ShipyardBubble Bubble = Tuning.BubbleAt(Yard);

        [Fact]
        public void The_recovered_impact_radius_is_thirty_five_metres_about_the_yard()
        {
            Assert.Equal(35.0, Tuning.ApproachRadiusMetres);
            Assert.Equal(35.0, Bubble.ImpactRadiusMetres);
            Assert.True(Bubble.IsWithinRange(new ShadowVector3(1035, 200, -400)));
            Assert.False(Bubble.IsWithinRange(new ShadowVector3(1035.01, 200, -400)));
            // IsWithinRange is the plain sphere the client uses - every direction.
            Assert.True(Bubble.IsWithinRange(new ShadowVector3(1000, 200 - 34, -400)));
        }

        [Fact]
        public void The_dome_is_the_upper_half_of_that_sphere()
        {
            Assert.Equal(0.0, Tuning.DomeFloorOffsetMetres);
            Assert.Equal(Yard.Y, Bubble.DomeFloorMetres);

            var justAbove = new ShadowVector3(1020, 200, -400);
            var justBelow = new ShadowVector3(1020, 199.99, -400);
            Assert.True(Bubble.ContainsDock(justAbove));
            Assert.True(Bubble.IsWithinRange(justBelow));
            Assert.False(Bubble.IsAboveYard(justBelow));
            Assert.False(Bubble.ContainsDock(justBelow));
        }

        [Fact]
        public void Exit_needs_the_margin_so_the_edge_cannot_flap()
        {
            Assert.Equal(2.0, Tuning.BubbleExitMarginMetres);
            // Entry at exactly 35 m, exit only past 37 m: the band between them is
            // where a hovering hull would otherwise oscillate.
            Assert.True(Bubble.IsWithinRange(new ShadowVector3(1035, 200, -400)));
            Assert.False(Bubble.HasFullyCleared(new ShadowVector3(1035, 200, -400)));
            Assert.False(Bubble.HasFullyCleared(new ShadowVector3(1037, 200, -400)));
            Assert.True(Bubble.HasFullyCleared(new ShadowVector3(1037.01, 200, -400)));
        }

        [Fact]
        public void Fully_cleared_subtracts_the_hulls_own_radius()
        {
            var centre = new ShadowVector3(1040, 200, -400);
            Assert.True(Bubble.HasFullyCleared(centre));
            Assert.True(Bubble.HasFullyCleared(centre, hullClearanceRadiusMetres: 2.9));
            Assert.False(Bubble.HasFullyCleared(centre, hullClearanceRadiusMetres: 3.1));
        }

        [Fact]
        public void An_invalid_bubble_or_a_nonfinite_point_answers_no_to_everything()
        {
            var broken = new ShipyardBubble(Yard, 0.0, 0.0, 2.0);
            Assert.False(broken.IsValid);
            Assert.False(broken.IsWithinRange(Yard));
            Assert.False(broken.ContainsDock(Yard));
            Assert.False(broken.HasFullyCleared(new ShadowVector3(9999, 9999, 9999)));

            var nonFinite = new ShadowVector3(double.NaN, 200, -400);
            Assert.False(Bubble.IsWithinRange(nonFinite));
            Assert.False(Bubble.IsAboveYard(nonFinite));
            Assert.False(Bubble.ContainsDock(nonFinite));
            // Fail closed on exit too: an unreadable pose is not proof of departure.
            Assert.False(Bubble.HasFullyCleared(nonFinite));
        }

        [Fact]
        public void The_dock_pose_the_yard_builds_at_is_always_inside_its_own_dome()
        {
            // The recovered placement puts a built hull HoverHeightMetres straight
            // above the yard, so the docked pose must satisfy the dome the capture
            // gate uses - otherwise a ship could never settle where it belongs.
            var dockPose = new ShadowVector3(Yard.X,
                Yard.Y + BuiltShipPlacement.HoverHeightMetres, Yard.Z);
            Assert.True(Bubble.ContainsDock(dockPose));
            Assert.False(Bubble.HasFullyCleared(dockPose));
        }
    }
}
