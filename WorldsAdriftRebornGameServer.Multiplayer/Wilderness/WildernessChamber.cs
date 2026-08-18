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
        /// Y is BELOW ground on purpose - see <see cref="HavenLocalPlacement"/>.
        ///
        /// Chosen by sweeping all 7,791 fine (2 m) Haven surface samples against,
        /// for each of 24 yaws:
        ///
        ///   * reachable on foot from the spawn point - a flood fill over the
        ///     contiguous 8 m surface grid that never climbs more than 2 m per 8 m
        ///     cell. (This matters: the 141 m site this document used to name is on
        ///     a plateau behind a 147% slope.)
        ///   * no authored Haven structure within the rotated 40 m x 36 m footprint
        ///     plus a 6 m per-prop pad (<see cref="HavenStructures"/>) - measured
        ///     gap 7.2 m
        ///   * the terrain under the whole footprint spanning 1.65 m
        ///   * the terrain inside the room (radius 9 m) spanning 0.45 m
        ///   * the terrain along the ENTRY CORRIDOR spanning 0.11 m (3.99..4.10)
        ///   * and the one that decides it: the interior terrain landing 0.05..0.50 m
        ///     ABOVE the doorway sill, so a player steps in level. Sites where the
        ///     interior sits below the sill were rejected - that is a 3 m drop into
        ///     a walled room, which is the trap this whole exercise exists to avoid.
        ///   * no authored rock within 12 m of the centre
        ///
        /// 55.6 m from <c>SpawnPolicy.PlayerSpawnPosition</c>'s local (208, ., 4),
        /// and 0.52 m BELOW the spawn's own ground vertex: a walk on the flat, no
        /// climb.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement =
            (160.00, -6.86, 32.00);

        /// <summary>
        /// The measured terrain height at the bottom of the entry corridor,
        /// island-local metres. The burial depth is DERIVED from this and
        /// <see cref="DoorwaySillLocalY"/>, not chosen: put the sill on the ground
        /// the corridor actually lands on and the door is a door.
        /// </summary>
        public const double CorridorGroundY = 3.99;

        /// <summary>The highest terrain sample in the corridor. The aperture has to clear it.</summary>
        public const double CorridorGroundMaxY = 4.10;

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
        public const double YawDegrees = 300.0;

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
