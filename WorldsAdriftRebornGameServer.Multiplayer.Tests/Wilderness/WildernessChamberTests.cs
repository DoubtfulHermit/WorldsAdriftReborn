using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// The Revival Chamber as the ROOM the shrine stands in. Everything here is a
    /// way the building could exist and still not be a room somebody can walk into.
    /// </summary>
    public sealed class WildernessChamberTests
    {
        [Fact]
        public void The_chamber_prefab_is_one_the_client_can_resolve()
        {
            Assert.True(ClientEntityPrefabs.CanResolve(WildernessChamber.AssetName),
                WildernessChamber.AssetName + " is not in the client entity-prefab census");
        }

        /// <summary>
        /// 190602 AND NOTHING ELSE. Seeding 1210 on this prefab is the sealed-well
        /// bug: its only InteractiveObjectVisualizer is on a plate at the bottom of
        /// a drum that is closed on 360/360 bearings, and once the chamber is buried
        /// that plate is 11 m under the terrain. A prompt there could never be
        /// reached and would only ever be a lie.
        /// </summary>
        [Fact]
        public void The_chamber_is_scenery_and_advertises_no_interaction()
        {
            Assert.Equal(new uint[] { 190602 }, WildernessChamber.SeedComponents);
            Assert.DoesNotContain(1210u, WildernessChamber.SeedComponents);
        }

        /// <summary>
        /// The burial depth is DERIVED, not chosen: the doorway sill has to land on
        /// the ground the entry corridor actually stands on. Get this wrong upward
        /// and the door floats; wrong downward and the terrain fills the doorway.
        /// </summary>
        [Fact]
        public void The_doorway_sill_lands_on_the_corridors_own_ground()
        {
            Assert.Equal(WildernessChamber.CorridorGroundY,
                WildernessChamber.DoorwaySillIslandY, 2);
        }

        /// <summary>
        /// ...and a player has to fit under the lintel at the WORST point of that
        /// ground, not the average. The aperture is 4.40 m; the corridor terrain
        /// spans 0.11 m; a player is 2.20 m.
        /// </summary>
        [Fact]
        public void A_player_fits_through_the_doorway_at_its_tightest()
        {
            double clear = WildernessChamber.DoorwayLintelIslandY - WildernessChamber.CorridorGroundMaxY;

            Assert.True(clear >= WildernessChamber.PlayerHeightMetres,
                "the doorway is only " + clear.ToString("0.00") + " m clear at its tightest");
            // And the sill is never above the ground it lands on, or there is a step
            // into thin air where the door should be.
            Assert.True(WildernessChamber.DoorwaySillIslandY <= WildernessChamber.CorridorGroundMaxY);
        }

        /// <summary>
        /// The measured aperture, pinned. 720-ray sections of the prefab's own
        /// collision meshes at 0.1 m steps: every bearing blocked below 10.8, 23 of
        /// 720 open from 10.9 to 15.2, all blocked again at 15.3.
        /// </summary>
        [Fact]
        public void The_doorway_is_the_measured_prefab_aperture()
        {
            Assert.Equal(10.85, WildernessChamber.DoorwaySillLocalY, 2);
            Assert.Equal(15.25, WildernessChamber.DoorwayLintelLocalY, 2);
            Assert.Equal(4.40, WildernessChamber.DoorwayApertureMetres, 2);
        }

        /// <summary>
        /// The room's floor is Haven's terrain, and the player has to STEP IN, not
        /// fall in. The interior terrain at the chosen site measures 4.04..4.49
        /// against a sill at 3.99: level to half a metre. A site whose interior sat
        /// below the sill would be a walled pit with a door in its ceiling, which is
        /// exactly the trap this design exists to avoid - one such site was measured
        /// and rejected.
        /// </summary>
        [Fact]
        public void The_room_floor_is_level_with_the_doorway_not_below_it()
        {
            double floor = WildernessShrine.HavenLocalPlacement.Y;
            double sill = WildernessChamber.DoorwaySillIslandY;

            Assert.True(floor >= sill - 0.5,
                "the chamber floor is " + (sill - floor).ToString("0.00") + " m BELOW the doorway sill");
            Assert.True(floor <= sill + 1.5,
                "the chamber floor is " + (floor - sill).ToString("0.00") + " m above the doorway sill");
        }

        /// <summary>
        /// It carries a real yaw. The prefab has exactly ONE doorway; leaving it at
        /// the identity sentinel points that doorway at world +x whatever the ground
        /// there looks like, which is how a building becomes a sealed drum again.
        /// </summary>
        [Fact]
        public void The_chamber_is_turned_to_face_its_measured_approach()
        {
            Assert.NotEqual(Multiplayer.Placement.Quaternion32Packing.Identity, WildernessChamber.PackedRotation);

            double yaw = ShipyardDockingPolicy.YawFromPacked(WildernessChamber.PackedRotation);
            double degrees = ((yaw * 180.0 / Math.PI) + 360.0) % 360.0;
            // The wire form is 10 bits per component, so the round trip is good to
            // about a tenth of a degree - which is a rounding error on a 3.8 m door,
            // not a facing.
            Assert.True(Math.Abs(degrees - WildernessChamber.YawDegrees) < 0.5,
                "the chamber decodes to " + degrees.ToString("0.00") + " deg");
        }

        /// <summary>
        /// The whole 40 m x 36 m footprint has to clear what is already built on
        /// Haven - recomputed from the embedded prop table, not trusted from a
        /// comment. This is the check whose absence drove the first placement
        /// through the ruined metal camp.
        /// </summary>
        [Fact]
        public void The_chamber_footprint_clears_everything_built_on_haven()
        {
            double clearance = HavenStructures.ClearanceAt(
                WildernessChamber.HavenLocalPlacement.X, WildernessChamber.HavenLocalPlacement.Z);

            // The footprint reaches 21.85 m from the centre at its longest, so the
            // nearest structure has to be beyond that plus a margin for the prop's
            // own size.
            Assert.True(clearance >= 28.0,
                "the chamber is " + clearance.ToString("0.0") + " m from an authored Haven structure");

            // Contrast: the first shrine placement, which was inside the camp.
            Assert.True(HavenStructures.ClearanceAt(176.00, 16.00) < 15.0);
        }

        [Fact]
        public void Nothing_authored_stands_over_the_chamber()
        {
            Assert.Equal(0, HavenStructures.CountNear(
                WildernessChamber.HavenLocalPlacement.X,
                WildernessShrine.HavenLocalPlacement.Y,   // the FLOOR height, not the buried origin
                WildernessChamber.HavenLocalPlacement.Z,
                radiusMetres: 24.0, belowMetres: 4.0, aboveMetres: 40.0));
        }

        /// <summary>
        /// It stands ON Haven. Checked at the FLOOR height: the registration Y is
        /// deliberately 11 m underground, and asking the island-location policy
        /// about a point inside the island's own volume proves nothing.
        /// </summary>
        /// <summary>
        /// NOTHING THIS SERVER PLANTS STANDS INSIDE THE BUILDING. The trees, nodes
        /// and deposits are scattered from the SAME measured Haven surface table the
        /// chamber was sited on, so this is not hypothetical: the first attempt at
        /// this site put tree-46 through the roof.
        /// </summary>
        [Fact]
        public void Nothing_else_this_server_plants_stands_inside_the_chamber()
        {
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator());

            foreach (WorldEntity other in registry.Registrations)
            {
                if (other.Key == WildernessChamber.WorldEntityKey) continue;
                if (other.Key == WildernessShrine.WorldEntityKey) continue;      // the point of the room
                if (other.AssetName.Contains("Island", StringComparison.Ordinal)) continue;

                Assert.False(WildernessChamber.Covers(other.Position, IslandCatalog.Haven),
                    other.Key + " (" + other.AssetName + ") stands inside the Revival Chamber");
            }
        }

        /// <summary>
        /// AND THE SKIP IS NOW A NO-OP. The keep-out is enforced at GENERATION
        /// (HavenSurface.ChamberExclusion, plus one hand-written nugget deleted from
        /// MetalNodes), so by the time registration runs there is nothing left to
        /// skip. That matters beyond tidiness: while the skip was doing the work,
        /// Haven reported 1,526 boot resource entities and delivered 1,521, and the
        /// five missing ones appeared nowhere.
        ///
        /// If a future table puts something back inside the building, this fails
        /// instead of the world quietly losing a resource again.
        /// </summary>
        [Fact]
        public void The_registration_time_skip_has_nothing_left_to_skip()
        {
            // TREES and FUEL are cleared to the apron: not one inside 35 m.
            foreach ((double X, double Y, double Z) local in WorldEntities.DistributedTreeLocals)
            {
                Assert.False(WildernessChamber.Clears(local.X, local.Z),
                    "a tree is still generated on the cleared ground");
            }

            foreach (FuelPods.Placement p in FuelPods.HavenPlacements)
            {
                Assert.False(WildernessChamber.Clears(p.LocalX, p.LocalZ),
                    "a fuel canister is still generated on the cleared ground");
            }

            // METAL keeps its ground right up to the walls - clearing ore to 35 m
            // would cost the starting island a third of its metal to fix a look -
            // but nothing may stand INSIDE the building.
            foreach (MetalNodes.Placement p in MetalNodes.HavenPlacements)
            {
                Assert.False(WildernessChamber.Covers(p.LocalX, p.LocalZ),
                    "a metal node is still inside the chamber walls");
            }

            // ...and therefore the registration-time guard has nothing to do.
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator());
            foreach (WorldEntity e in registry.Registrations)
            {
                if (e.Key == WildernessChamber.WorldEntityKey) continue;
                if (e.Key == WildernessShrine.WorldEntityKey) continue;
                if (e.AssetName.Contains("Island", StringComparison.Ordinal)) continue;
                Assert.False(WildernessChamber.Covers(e.Position, IslandCatalog.Haven),
                    e.Key + " is still being skipped at registration");
            }
        }

        /// <summary>
        /// THE DOORWAY FACES THE APPROACH. The prefab has exactly one opening, and
        /// the previous placement pointed it 132 deg away from the spot the user
        /// stood on - so they were looking at a blank wall and said the tower was
        /// not where they asked. The measured spot is Haven-local (168.0, 4.52, 8.0).
        /// </summary>
        [Fact]
        public void The_doorway_faces_the_ground_the_user_walks_in_from()
        {
            const double SpotX = 168.0, SpotZ = 8.0;

            // Unity yaw: prefab local +x (the doorway) points at (cos yaw, -sin yaw).
            double yaw = WildernessChamber.YawDegrees * Math.PI / 180.0;
            double dx = Math.Cos(yaw), dz = -Math.Sin(yaw);

            double ux = SpotX - WildernessChamber.HavenLocalPlacement.X;
            double uz = SpotZ - WildernessChamber.HavenLocalPlacement.Z;
            double len = Math.Sqrt((ux * ux) + (uz * uz));

            double facing = ((dx * ux) + (dz * uz)) / len;
            Assert.True(facing > 0.85,
                "the doorway is aimed " + (Math.Acos(facing) * 180.0 / Math.PI).ToString("0")
                + " deg away from where the user stands");
        }

        /// <summary>
        /// ...and it is as close to that spot as the geometry allows. The spot ITSELF
        /// cannot hold the building - the ground there is clear for only 12.4 m, with
        /// 14 authored structures inside 22 m - so this pins the compromise rather
        /// than pretending the constraint is not there.
        /// </summary>
        [Fact]
        public void The_chamber_is_within_a_short_walk_of_the_spot_the_user_asked_for()
        {
            double dx = WildernessChamber.HavenLocalPlacement.X - 168.0;
            double dz = WildernessChamber.HavenLocalPlacement.Z - 8.0;

            Assert.InRange(Math.Sqrt((dx * dx) + (dz * dz)), 0.0, 26.0);
        }

        [Fact]
        public void The_chamber_stands_on_haven()
        {
            IslandLocation location = IslandLocationPolicy.Locate(
                IslandCatalog.Haven.LocalToGlobal(
                    WildernessChamber.HavenLocalPlacement.X,
                    WildernessShrine.HavenLocalPlacement.Y,
                    WildernessChamber.HavenLocalPlacement.Z),
                IslandLocationPolicy.KnownWorld());

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.Equal(IslandCatalog.Haven.Id, location.Island!.Id);
        }
    }
}
