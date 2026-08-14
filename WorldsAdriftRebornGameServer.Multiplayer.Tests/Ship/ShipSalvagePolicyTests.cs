using System;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public class ShipSalvagePolicyTests
    {
        [Fact]
        public void Only_the_owner_may_reclaim_the_exact_built_hull_docked_at_this_yard()
        {
            Assert.Equal(ShipSalvageReject.Accept,
                ShipSalvagePolicy.Evaluate(true, "alice", "alice", 42, true, 7, 7));
            Assert.Equal(ShipSalvageReject.NotOwnedPlayer,
                ShipSalvagePolicy.Evaluate(false, "alice", "alice", 42, true, 7, 7));
            Assert.Equal(ShipSalvageReject.NotShipyardOwner,
                ShipSalvagePolicy.Evaluate(true, "mallory", "alice", 42, true, 7, 7));
            Assert.Equal(ShipSalvageReject.NoDockedShip,
                ShipSalvagePolicy.Evaluate(true, "alice", "alice", 0, false, 0, 7));
            Assert.Equal(ShipSalvageReject.HullNotBuilt,
                ShipSalvagePolicy.Evaluate(true, "alice", "alice", 42, false, 7, 7));
            Assert.Equal(ShipSalvageReject.DockMismatch,
                ShipSalvagePolicy.Evaluate(true, "alice", "alice", 42, true, 8, 7));
        }

        [Fact]
        public void Dropped_parts_keep_their_ship_relative_pose_in_world_space()
        {
            uint yaw90 = Quaternion32Packing.Encode(
                (float)Math.Cos(Math.PI / 4), 0f, (float)Math.Sin(Math.PI / 4), 0f);
            var hull = FixedPointPosition.FromMetres(100, 20, 200);
            var local = FixedPointPosition.FromMetres(0, 3, 5);

            (FixedPointPosition p, uint r) = ShipSalvagePolicy.DropPose(
                hull, yaw90, local, Quaternion32Packing.Identity);

            Assert.InRange(p.MetresX, 104.97, 105.03);
            Assert.InRange(p.MetresY, 22.99, 23.01);
            Assert.InRange(p.MetresZ, 199.97, 200.03);
            (float w, _, float y, _) = Quaternion32Packing.Decode(r);
            Assert.InRange(w, 0.70f, 0.72f);
            Assert.InRange(y, 0.70f, 0.72f);
        }
    }
}
