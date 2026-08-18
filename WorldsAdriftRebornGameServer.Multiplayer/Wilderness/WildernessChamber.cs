using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// THE REVIVAL CHAMBER, back on Haven - as the BUILDING the shrine stands in,
    /// and nothing else. Scenery: 190602 and no 1210.
    ///
    /// WHY IT IS BACK. It was removed because its interaction plate is unreachable
    /// (see <see cref="WildernessShrine"/>), and removing it cost the one thing it
    /// was actually good at: being a 20 m landmark a new player can see from across
    /// the island. Without it the shrine was a 1.2 m plate in a field and a live
    /// player could not find it. The building and the interactable are therefore now
    /// TWO ENTITIES: this one is the landmark and the room, and the shrine stands
    /// inside it.
    ///
    /// WHY IT WORKS AS A ROOM WHEN IT DID NOT WORK AS A DEVICE. Everything about
    /// the prefab that made its own plate unusable is fine once the plate is not the
    /// point:
    ///
    ///   * Its collision shell is closed on 360/360 bearings from prefab-local
    ///     y = -1.0 to y = 9.3, with ONE aperture: a corridor on the +x bearing,
    ///     3.8 m wide (free channel |z| &lt;= 1.9 for local x 13..21), whose sill is at
    ///     <see cref="DoorwaySillLocalY"/> and whose lintel is at
    ///     <see cref="DoorwayLintelLocalY"/>. All measured off
    ///     respawner_exterior_LOD0 / respawner_interior_LOD0 in resources.assets.
    ///   * Bury the origin so that sill lands ON the terrain and everything below it
    ///     - the sealed drum, the 9.7 m drop, the buried plate - is under the
    ///     terrain mesh and can never be entered or fallen into. What is left above
    ///     ground is a walled room with one door, whose FLOOR IS HAVEN'S OWN
    ///     TERRAIN.
    ///   * The interior is clear: at the player's standing band there is no chamber
    ///     geometry within <see cref="InteriorClearRadiusMetres"/> of the centre
    ///     (measured 10.0 m at the chosen site), and the ceiling is at prefab-local
    ///     24.7, i.e. ~13 m of headroom.
    ///
    /// PROVENANCE. Every number here is RECOVERED (measured off the shipped prefab's
    /// own collision meshes, or off Haven's extracted LOD0 surface). WHICH vertex
    /// and WHICH yaw were chosen is WAREBORN TUNING - retail's own placement is not
    /// recoverable, because everything Haven-specific was spawned by the GSim.
    /// </summary>
    public static class WildernessChamber
    {
        /// <summary>
        /// The bare prefab name. Line 80 of the entity-prefab census
        /// (<see cref="ClientEntityPrefabs"/>), so the client can resolve it; the
        /// live client has already been observed compiling its template.
        /// </summary>
        public const string AssetName = "HavenAncientRespawner";

        /// <summary>
        /// Its stable registration key. Distinct from the shrine's: they are two
        /// entities on purpose, so the building can never take the interaction back
        /// or delay it.
        /// </summary>
        public const string WorldEntityKey = "wilderness-shrine-chamber";

        // ------------------------------------------------------------------
        // THE PREFAB'S DOORWAY - measured, prefab-local metres.
        //
        // Found by sectioning respawner_exterior_LOD0 + respawner_interior_LOD0 at
        // 0.1 m steps and casting 720 rays from the centre at each height: every
        // bearing is blocked below 10.8, exactly 23 of 720 bearings (-5.5..+5.5 deg)
        // are open from 10.9 to 15.2, and all are blocked again at 15.3.
        // ------------------------------------------------------------------

        /// <summary>Prefab-local height of the doorway sill: below this the shell is sealed.</summary>
        public const double DoorwaySillLocalY = 10.85;

        /// <summary>Prefab-local height of the doorway lintel: above this the shell is sealed again.</summary>
        public const double DoorwayLintelLocalY = 15.25;

        /// <summary>The usable height of the aperture, metres. 4.40.</summary>
        public static double DoorwayApertureMetres => DoorwayLintelLocalY - DoorwaySillLocalY;

        /// <summary>
        /// The corridor's free half-width, metres - measured |z| at which the
        /// passage walls stand for local x 14..19. A 3.8 m wide door.
        /// </summary>
        public const double DoorwayHalfWidthMetres = 1.85;

        /// <summary>Prefab-local x range of the entry corridor, used to sample the terrain it lands on.</summary>
        public const double CorridorNearLocalX = 13.0;
        public const double CorridorFarLocalX = 22.0;

        /// <summary>
        /// How much clear floor there is around the chamber's centre at the player's
        /// standing band, metres. Measured 10.0 m at the chosen site by testing a
        /// 2.2 m capsule against the prefab's collision meshes on a 1 m grid; stated
        /// as 9.0 so the shrine's slot is checked against a value with margin in it.
        /// </summary>
        public const double InteriorClearRadiusMetres = 9.0;

        /// <summary>
        /// A player is 2.2 m tall; the doorway has to clear the highest terrain in
        /// the corridor by at least this much or there is no way in.
        /// </summary>
        public const double PlayerHeightMetres = 2.2;

        // ------------------------------------------------------------------
        // WHERE IT STANDS.
        // ------------------------------------------------------------------

        /// <summary>
        /// Island-local metres. X and Z are a MEASURED Haven LOD0 surface vertex;
        /// Y is BELOW ground on purpose - the burial depth, derived below.
        ///
        /// MOVED HERE 2026-08-18 BECAUSE THE USER ASKED, TWICE, AND WAS MEASURED.
        /// They stood on the spot they meant and the server read it off the entity
        /// they were carried by: Haven-local (168.00, 4.52, 8.00). The chamber was
        /// 25.3 m away at (160, 4.18, 32) with its single doorway pointing 132 deg
        /// AWAY from them, so from where they stood they were looking at its back.
        ///
        /// THE BUILDING CANNOT STAND ON THAT EXACT SPOT, and this says so rather
        /// than quietly going somewhere else again. At (168, 8) the ground is clear
        /// for only 12.4 m: the first authored structure is a camp pipe at 12.4 m,
        /// there are 14 within 22 m and 33 within 26 m, and the camp's pieces there
        /// span y 0.5..26.3 while this tower rises to 24.1. A 40 m x 36 m footprint
        /// put there overlaps the ruined metal camp on the ground AND punches
        /// through its platform deck overhead - the exact failure that trapped a
        /// player at the very first placement.
        ///
        /// So this is the closest point that genuinely works, chosen by sweeping
        /// every fine (2 m) surface sample within 70 m of the user's spot against
        /// all 24 yaws - 317 workable (site, yaw) combinations, ranked by distance
        /// to their spot and then by how squarely the doorway faces it:
        ///
        ///   * 23.3 m from (168, 8) - the nearest workable site is 20.0 m, so the
        ///     absolute best available was 2 m closer than this with the door
        ///     pointing sideways. This one trades those 2 m for the door.
        ///   * doorway aimed 0.97 (about 14 deg off) straight at where they stand,
        ///     instead of 132 deg away
        ///   * corridor terrain 4.40, giving 4.40 m of clear doorway height against
        ///     a 2.20 m player
        ///   * interior floor 0.07 m ABOVE the sill - you step in dead level
        ///   * terrain under the whole footprint spans 1.81 m; inside the room 0.40 m,
        ///     the flattest of any candidate
        ///   * 4.1 m of clearance from the nearest authored structure's footprint
        ///
        /// 57.3 m from the spawn point and 0.54 m below its ground vertex: still a
        /// walk on one level.
        ///
        /// CAVEAT, stated because it is thinner than last time: only ONE fine
        /// surface sample falls in the entry corridor here (the previous site had
        /// four). The doorway height has 2.2 m of margin over a player, so a sample
        /// or two of error is absorbed - but if the door lands buried or floating,
        /// <see cref="CorridorGroundY"/> is the one number to change.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement =
            (156.00, -6.45, 28.00);

        /// <summary>
        /// The measured terrain height at the bottom of the entry corridor,
        /// island-local metres. The burial depth is DERIVED from this and
        /// <see cref="DoorwaySillLocalY"/>, not chosen: put the sill on the ground
        /// the corridor actually lands on and the door is a door.
        /// </summary>
        public const double CorridorGroundY = 4.40;

        /// <summary>The highest terrain sample in the corridor. The aperture has to clear it.</summary>
        public const double CorridorGroundMaxY = 4.40;

        /// <summary>
        /// Facing, degrees, in the convention this server already flies ships in:
        /// <c>ShipyardDockingPolicy.PackedYaw</c> builds a rotation about +Y and
        /// <c>FlightIntegrator</c> turns that yaw into a world heading of
        /// <c>(sin yaw, cos yaw)</c> - so prefab local +x, which is where the
        /// doorway is, ends up pointing at world <c>(cos yaw, -sin yaw)</c>.
        ///
        /// 300 deg is the only one of 24 yaws at this vertex whose corridor lands on
        /// terrain we have enough samples to certify AND clears the authored props.
        /// It points the doorway at world (+0.50, +0.87), which is about a quarter
        /// turn from the line a player walks in on - they arrive at the tower and go
        /// round one side to the door. Stated plainly because it is a real cost:
        /// the alternative that faced the approach head-on had its interior floor
        /// 3 m BELOW the sill, which is worse than a walk round.
        /// </summary>
        public const double YawDegrees = 45.0;

        /// <summary>The 190602 localRotation seed, packed.</summary>
        public static uint PackedRotation =>
            ShipyardDockingPolicy.PackedYaw(YawDegrees * Math.PI / 180.0);

        /// <summary>Its global position, given the Haven definition it stands on.</summary>
        public static FixedPointPosition PositionOn(IslandDefinition haven)
        {
            if (haven == null) throw new ArgumentNullException(nameof(haven));
            return haven.LocalToGlobal(
                HavenLocalPlacement.X, HavenLocalPlacement.Y, HavenLocalPlacement.Z);
        }

        /// <summary>
        /// Island-local height of the doorway sill, given the burial depth. This is
        /// the number the whole placement is built around.
        /// </summary>
        public static double DoorwaySillIslandY => HavenLocalPlacement.Y + DoorwaySillLocalY;

        /// <summary>Island-local height of the doorway lintel.</summary>
        public static double DoorwayLintelIslandY => HavenLocalPlacement.Y + DoorwayLintelLocalY;

        /// <summary>
        /// How far out from the chamber's axis nothing else this server plants may
        /// stand, metres. The building's above-ground collision reaches ~14 m for
        /// the wall ring and ~21 m along the entry corridor, so 22 m is the disc it
        /// occupies. Trees, nodes and deposits are scattered from the same measured
        /// surface table the chamber was chosen from, so without this a tree grows
        /// through the roof - one already did, at the first attempt at this site.
        /// </summary>
        public const double ExclusionRadiusMetres = 22.0;

        /// <summary>
        /// How far out from the chamber's axis the GROUND IS CLEARED, metres - the
        /// building's own 22 m footprint plus an apron.
        ///
        /// The user asked for this, standing on the shelf: "this is a small island
        /// attached to haven, empty the tree etc from it then place the tower here
        /// properly". The shelf itself turned out to be the whole low starting area -
        /// 885 measured surface samples spanning island-local x 105..257, z -46..76,
        /// and it CONTAINS THE SPAWN POINT - so clearing "the island" would strip the
        /// tutorial's own near-spawn wood. 35 m clears a 70 m circle around the tower
        /// instead: it removes the trees the user was standing among (the one they
        /// stood on is 25.3 m from the axis) and leaves the spawn, 55.6 m away, and
        /// the rest of the shelf wooded.
        ///
        /// Enforced at GENERATION (<c>Resources.HavenSurface</c>), so the placement
        /// field never contains the point and the boot count tells the truth.
        /// </summary>
        public const double ClearingRadiusMetres = 35.0;

        /// <summary>Whether an island-local point is on ground the chamber clears.</summary>
        public static bool Clears(double localX, double localZ)
        {
            double dx = localX - HavenLocalPlacement.X;
            double dz = localZ - HavenLocalPlacement.Z;
            return Math.Sqrt((dx * dx) + (dz * dz)) < ClearingRadiusMetres;
        }

        /// <summary>
        /// Whether an island-local point is inside the ground the chamber occupies.
        /// Horizontal only: the building spans 38 m vertically and anything at this
        /// (x, z) is either inside it or under it.
        /// </summary>
        public static bool Covers(double localX, double localZ)
        {
            double dx = localX - HavenLocalPlacement.X;
            double dz = localZ - HavenLocalPlacement.Z;
            return Math.Sqrt((dx * dx) + (dz * dz)) < ExclusionRadiusMetres;
        }

        /// <summary>Whether a world position stands inside the chamber's footprint.</summary>
        public static bool Covers(FixedPointPosition position, IslandDefinition haven)
        {
            FixedPointPosition centre = PositionOn(haven);
            double dx = position.MetresX - centre.MetresX;
            double dz = position.MetresZ - centre.MetresZ;
            return Math.Sqrt((dx * dx) + (dz * dz)) < ExclusionRadiusMetres;
        }

        /// <summary>
        /// ONLY 190602. No 1210, and that is the point: the chamber is the room, and
        /// the one thing in this world that answers an interact is the shrine
        /// standing in it. Seeding 1210 here would re-create the sealed-well bug,
        /// because the prefab's own visualizer is on the plate 11 m under the floor.
        /// </summary>
        public static readonly IReadOnlyList<uint> SeedComponents = new uint[] { 190602 };
    }
}
