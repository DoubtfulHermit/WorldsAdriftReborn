using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
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
        /// THE TEST THAT WAS MISSING, and the reason this file exists in its present
        /// shape. Every check the buried placement passed was about the doorway or the
        /// room; not one of them asked what the building looks like from OUTSIDE. It
        /// looked like a bunker: 18.59 m of a 37.85 m tower under the terrain, which
        /// the user saw and called ridiculous.
        ///
        /// The terrain is read out of the embedded Haven surface table - the same data
        /// the site was chosen from, recomputed here rather than trusted from a
        /// constant - so a future edit that buries the thing again fails right here.
        /// </summary>
        [Fact]
        public void The_building_stands_proud_of_the_ground_it_is_on()
        {
            double worstGround = WorstGroundWithin(
                WildernessChamber.HavenLocalPlacement.X,
                WildernessChamber.HavenLocalPlacement.Z, 18.0);

            double proud = WildernessChamber.ProudFractionAgainst(worstGround);

            Assert.True(proud >= WildernessChamber.MinimumProudFraction,
                "the chamber stands only " + (100 * proud).ToString("0.0")
                + "% proud of the ground - it reads as a hole, not a tower");

            // ...and the constant that documents it agrees with the measurement.
            Assert.Equal(proud, WildernessChamber.ProudFraction, 1);
        }

        /// <summary>
        /// EVERY PLACEMENT THAT HAS EVER SHIPPED FAILS IT. Not asserted from memory:
        /// each dead origin is re-scored against the same embedded surface table, so
        /// "restore the old placement" cannot pass this file.
        ///
        /// The four, with what they measure: (176, 16) 41.0%, (168, 24) 46.2%,
        /// (160, 32) 48.1%, (156, 28) 49.2% - the one the user complained about.
        /// The buried doctrine's ceiling anywhere on Haven was 50.9%, measured by
        /// sweeping all 3,863 flat fine surface samples against all 24 yaws.
        /// </summary>
        [Theory]
        [InlineData(176.00, 16.00, -6.86)]
        [InlineData(168.00, 24.00, -6.86)]
        [InlineData(160.00, 32.00, -6.86)]
        [InlineData(156.00, 28.00, -6.45)]
        public void Every_placement_that_buried_the_building_fails_that_test(
            double localX, double localZ, double originY)
        {
            double proud = WildernessChamber.ProudFractionAgainst(
                originY, WorstGroundWithin(localX, localZ, 18.0));

            Assert.True(proud < WildernessChamber.MinimumProudFraction,
                "the dead placement at (" + localX + ", " + localZ + ") scores "
                + (100 * proud).ToString("0.0") + "%, so this test would not have caught it");

            // ...and the chamber is not standing at any of them.
            Assert.False(Math.Abs(WildernessChamber.HavenLocalPlacement.X - localX) < 0.5
                && Math.Abs(WildernessChamber.HavenLocalPlacement.Z - localZ) < 0.5,
                "the chamber is back at a dead placement");
        }

        /// <summary>The worst (highest) surface sample within a radius, island-local.</summary>
        private static double WorstGroundWithin(double localX, double localZ, double radiusMetres)
        {
            double worst = double.NegativeInfinity;
            int found = 0;
            foreach (SurfaceSample sample in HavenSurface.Samples)
            {
                double dx = sample.LocalX - localX;
                double dz = sample.LocalZ - localZ;
                if ((dx * dx) + (dz * dz) > radiusMetres * radiusMetres) continue;
                found++;
                if (sample.LocalY > worst) worst = sample.LocalY;
            }

            // A placement measured against three samples is not measured at all.
            Assert.True(found >= 8,
                "only " + found + " surface samples under the footprint at ("
                + localX + ", " + localZ + ")");
            return worst;
        }

        /// <summary>
        /// The building sits on its own ground line, not on its doorway. That is the
        /// whole change: prefab-local 0 is where the foundation spike has finished
        /// widening and the authored interior floor sits, and the registration Y is
        /// the measured terrain seat under the wall ring.
        /// </summary>
        [Fact]
        public void The_registration_height_is_the_ground_the_wall_ring_stands_on()
        {
            Assert.Equal(0.0, WildernessChamber.GroundLineLocalY);
            Assert.Equal(WildernessChamber.SeatGroundY,
                WildernessChamber.HavenLocalPlacement.Y + WildernessChamber.GroundLineLocalY, 2);

            // It is a seat, not a guess: the terrain neither piles up the wall nor
            // drops away from under it by more than the prefab's own footing covers.
            Assert.InRange(WildernessChamber.SeatDugInMetres, 0.0, 1.6);
            Assert.InRange(WildernessChamber.SeatStandOffMetres, 0.0, 3.0);
            Assert.InRange(WildernessChamber.FootprintSpreadMetres, 0.0, 4.0);
        }

        /// <summary>
        /// NOBODY CAN GET INTO THE DRUM. That is the price of standing it up and it
        /// has to be a deliberate, checked property rather than an accident: a player
        /// who got through the door would be in a sealed shell with a 10.85 m drop
        /// and no way back out - the exact trap that needed an admin rescue in August.
        ///
        /// The door has to be far enough above the ground at its foot that no jump,
        /// no slope and no stack of terrain reaches it.
        /// </summary>
        [Fact]
        public void The_doorway_is_out_of_reach_from_the_ground()
        {
            double groundAtFoot = WorstGroundWithin(
                WildernessChamber.HavenLocalPlacement.X,
                WildernessChamber.HavenLocalPlacement.Z, 24.0);

            double aboveGround = WildernessChamber.DoorwaySillIslandY - groundAtFoot;

            Assert.True(aboveGround > 3.0 * WildernessChamber.PlayerHeightMetres,
                "the doorway sill is only " + aboveGround.ToString("0.00")
                + " m above the ground - a player could get into a sealed drum");
        }

        /// <summary>
        /// The measured aperture, pinned - and the bearing it is actually on. The
        /// prefab's collider tree puts Ramp01 at x -1.81..1.81, z -14.17..-12.97 and
        /// Ramp02 at x -1.81..1.81, z -14.72..-14.16: the corridor runs on -z, which
        /// is 270 deg measured as atan2(z, x). The old value said +x - the same
        /// numbers with the two axes transposed - and the yaw was being aimed with a
        /// blank wall.
        /// </summary>
        [Fact]
        public void The_doorway_is_the_measured_prefab_aperture()
        {
            Assert.Equal(10.85, WildernessChamber.DoorwaySillLocalY, 2);
            Assert.Equal(15.25, WildernessChamber.DoorwayLintelLocalY, 2);
            Assert.Equal(4.40, WildernessChamber.DoorwayApertureMetres, 2);
            Assert.Equal(1.81, WildernessChamber.DoorwayHalfWidthMetres, 2);
            Assert.Equal(270.0, WildernessChamber.DoorwayBearingLocalDegrees, 1);
        }

        /// <summary>
        /// It carries a real yaw. The prefab has exactly ONE face worth showing;
        /// leaving the rotation at the identity sentinel turns that face wherever
        /// prefab -z happens to land, which is how a player ends up looking at a
        /// blank wall and saying the tower is not where they asked.
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
            // own size - the camp's platforms are metres across, and 25.9 m of axis
            // clearance was enough to rule out an otherwise better site.
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
                WildernessChamber.HavenLocalPlacement.Y,  // now the ground it stands on
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
        /// THE FRONT FACES THE APPROACH. The prefab has exactly one face worth
        /// showing - the -z one, carrying the doorway, the entry lobe and now the
        /// shrine at its foot - and an earlier placement pointed it 132 deg away from
        /// the spot the user stood on, so they were looking at a blank wall and said
        /// the tower was not where they asked.
        ///
        /// Measured against BOTH the spot the user physically stood on
        /// (Haven-local 168, 4.52, 8) and the spawn point every player arrives from.
        /// Neither is 1.00 and that is deliberate: the ruined camp sits between this
        /// site and the spawn, and the yaws that aim the front dead at the approach
        /// put the shrine's pad under the camp's platform decks.
        /// </summary>
        [Fact]
        public void The_front_of_the_building_faces_the_ground_a_player_arrives_from()
        {
            // Unity yaw: prefab local +z points at (sin yaw, cos yaw), so the front,
            // which is local -z, points at (-sin yaw, -cos yaw).
            double yaw = WildernessChamber.YawDegrees * Math.PI / 180.0;
            double dx = -Math.Sin(yaw), dz = -Math.Cos(yaw);

            // The spot the user stood on: 0.26, i.e. they see a front quarter. Not
            // square, and the threshold says so rather than pretending - what this
            // pins is that they are never again looking at the BACK, which is what
            // 132 deg away (-0.67) gave them.
            Assert.True(Facing(dx, dz, 168.0, 8.0) > 0.0,
                "the front is turned away from the spot the user stands on");

            // The approach out of spawn, which is where every player arrives: 0.68.
            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;
            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;
            Assert.True(Facing(dx, dz, spawn.MetresX - island.MetresX, spawn.MetresZ - island.MetresZ) > 0.60,
                "the front is aimed away from the approach out of spawn");
        }

        private static double Facing(double dx, double dz, double towardX, double towardZ)
        {
            double ux = towardX - WildernessChamber.HavenLocalPlacement.X;
            double uz = towardZ - WildernessChamber.HavenLocalPlacement.Z;
            double len = Math.Sqrt((ux * ux) + (uz * uz));
            return ((dx * ux) + (dz * uz)) / len;
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

            // 14.4 m: the closest any workable stood-up placement gets, and 8.9 m
            // closer than the placement they complained about.
            Assert.InRange(Math.Sqrt((dx * dx) + (dz * dz)), 0.0, 20.0);
        }

        [Fact]
        public void The_chamber_stands_on_haven()
        {
            IslandLocation location = IslandLocationPolicy.Locate(
                IslandCatalog.Haven.LocalToGlobal(
                    WildernessChamber.HavenLocalPlacement.X,
                    WildernessChamber.HavenLocalPlacement.Y,
                    WildernessChamber.HavenLocalPlacement.Z),
                IslandLocationPolicy.KnownWorld());

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.Equal(IslandCatalog.Haven.Id, location.Island!.Id);
        }
    }
}
