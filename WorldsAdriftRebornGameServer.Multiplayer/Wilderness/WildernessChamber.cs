using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// THE REVIVAL CHAMBER, back on Haven - as a TOWER STANDING ON THE GROUND.
    /// Scenery: 190602 and no 1210.
    ///
    /// WHAT CHANGED, 2026-08-19, AND WHY. The user looked at it and said it was
    /// "half in the ground, it's ridiculous". They were right to the metre, and the
    /// burial was not an accident - it was the design. The previous doctrine buried
    /// the origin until the prefab's own doorway sill met the terrain, so that the
    /// room inside had Haven's own terrain as its floor and a player could walk in
    /// through the authored door.
    ///
    /// The cost of that doctrine was never measured from OUTSIDE, and it is this:
    ///
    ///   * <c>respawner_exterior_LOD0</c>, the mesh a player actually sees, spans
    ///     prefab-local y <see cref="MeshBottomLocalY"/> .. <see cref="MeshTopLocalY"/>
    ///     - <see cref="MeshHeightMetres"/> m of building.
    ///   * The doorway sill is <see cref="DoorwaySillLocalY"/> m up that wall. Put the
    ///     sill on the ground and 10.85 + 7.36 = 18.21 m of the mesh is underneath it.
    ///   * Measured at the old placement (156, -6.45, 28) against Haven's 2 m LOD0
    ///     surface: 18.59 m of 37.85 m below the terrain. FORTY-NINE PER CENT.
    ///
    /// AND NO SITE FIXES IT. Every one of Haven's 3,863 flat fine surface samples was
    /// swept against all 24 yaws under the old doctrine: 821 workable (site, yaw)
    /// combinations, and the BEST of them stands 50.9% proud. The burial is a property
    /// of the prefab, not of the ground - so "move it somewhere flatter" could only
    /// ever have bought a few centimetres.
    ///
    /// SO IT IS STOOD UP. The origin now sits on <see cref="GroundLineLocalY"/> - the
    /// prefab's own ground line, where the tapering foundation spike (r 8..11 m at
    /// y -7.4, widening to r 16 by y -1) stops and the body begins, and where the
    /// authored interior floor is. Only the foundation is buried; the tower stands
    /// <see cref="ExposedHeightMetres"/> m proud, <see cref="ProudFraction"/> of itself.
    ///
    /// WHAT THAT COSTS, STATED PLAINLY. The room is no longer enterable. The prefab's
    /// one aperture is 10.85 m up a sheer exterior wall and its own ramps
    /// (<c>Ramp01</c> 9.50..10.66, <c>Ramp02</c> 10.57..10.66, both INSIDE the
    /// corridor) do not reach the ground from outside - there was never a way in from
    /// the terrain, only a way in from a terrain raised to meet the door. So the
    /// chamber is now purely the LANDMARK, and the shrine stands at its foot instead
    /// of at its centre: <see cref="ShrineSlotLocal"/>. That is the whole trade, and
    /// it is the right way round - a 30 m tower you can see from the spawn point with
    /// the pad at its base beats a 19 m drum you can walk inside.
    ///
    /// A CORRECTION WORTH KEEPING. The old code recorded the doorway on prefab-local
    /// +x, with "a free channel |z| &lt;= 1.9 for local x 13..21". The prefab's own
    /// collider tree says otherwise: <c>Ramp01</c> is a box at x -1.81..1.81,
    /// z -14.17..-12.97 and <c>Ramp02</c> at x -1.81..1.81, z -14.72..-14.16, and
    /// <c>Light By Door</c> hangs at (-0.09, 14.20, -5.96). The corridor runs on
    /// -z, not +x - the same numbers with the two axes transposed. It did not bite,
    /// because the ground at that site happened to be flat on both bearings, but the
    /// yaw was being aimed by the wrong face of the building.
    ///
    /// PROVENANCE. Every prefab number here is RECOVERED, measured off
    /// <c>HavenAncientRespawner</c> in the shipped client's resources.assets with
    /// UnityPy (full TRS chains, not summed local positions). Every terrain number is
    /// measured off Haven's extracted LOD0 surface. WHICH vertex and WHICH yaw were
    /// chosen is WAREBORN TUNING - retail's own placement is not recoverable, because
    /// everything Haven-specific was spawned by the GSim.
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
        // THE BUILDING'S OWN SHAPE - measured, prefab-local metres, off
        // respawner_exterior_LOD0 (13,584 vertices) and the collider tree.
        // ------------------------------------------------------------------

        /// <summary>
        /// The bottom of the visible mesh: the tip of the foundation spike. Below
        /// prefab-local -6 the mesh is only r 8..11 m wide, so this is a footing, not
        /// a facade - it is MEANT to be under the ground.
        /// </summary>
        public const double MeshBottomLocalY = -7.36;

        /// <summary>The top of the visible mesh - the roof.</summary>
        public const double MeshTopLocalY = 30.49;

        /// <summary>How much building there is, top to toe. 37.85 m.</summary>
        public static double MeshHeightMetres => MeshTopLocalY - MeshBottomLocalY;

        /// <summary>
        /// THE PREFAB'S OWN GROUND LINE, and the number this whole placement is now
        /// built around. At prefab-local 0 the foundation spike has finished widening
        /// (r 12..16 by y -1), the body starts, and the authored interior floor sits
        /// (<c>respawner_interior</c> has a floor at y -1..0 and the SpawnPad's top
        /// plate at +0.39). Sit this on the terrain and the building stands the way it
        /// was modelled to.
        /// </summary>
        public const double GroundLineLocalY = 0.0;

        /// <summary>
        /// Prefab-local height of the doorway sill. Kept because it is a measured
        /// fact about the prefab and because it is the number that used to decide the
        /// burial - not because anything walks through it any more. At the placement
        /// below it lands nearly 10 m up a sheer wall.
        /// </summary>
        public const double DoorwaySillLocalY = 10.85;

        /// <summary>Prefab-local height of the doorway lintel.</summary>
        public const double DoorwayLintelLocalY = 15.25;

        /// <summary>The usable height of the aperture, metres. 4.40.</summary>
        public static double DoorwayApertureMetres => DoorwayLintelLocalY - DoorwaySillLocalY;

        /// <summary>
        /// Which way the doorway faces in prefab-local terms, degrees measured as
        /// <c>atan2(z, x)</c>. 270 = straight down -z. CORRECTED: the old code said
        /// 0 (+x), which was the same measurement with x and z transposed.
        /// Evidence: <c>Ramp01</c> box x -1.81..1.81 / z -14.17..-12.97,
        /// <c>Ramp02</c> box x -1.81..1.81 / z -14.72..-14.16, and the whole entry
        /// lobe of <c>respawner_interior_LOD0</c> reaching z = -29.6 at y 9..17.
        /// </summary>
        public const double DoorwayBearingLocalDegrees = 270.0;

        /// <summary>
        /// The corridor's free half-width, metres - the Ramp01/Ramp02 collider boxes
        /// measure |x| &lt;= 1.81 for local z -13.0..-14.7. A 3.6 m wide door.
        /// </summary>
        public const double DoorwayHalfWidthMetres = 1.81;

        /// <summary>
        /// A player is 2.2 m tall. Kept as the scale the "nobody can climb in" test
        /// is stated against.
        /// </summary>
        public const double PlayerHeightMetres = 2.2;

        // ------------------------------------------------------------------
        // WHERE IT STANDS.
        // ------------------------------------------------------------------

        /// <summary>
        /// Island-local metres. X and Z are a MEASURED Haven LOD0 surface vertex; Y
        /// is the SEAT - the median terrain height on the ring the building's wall
        /// stands on (72 probes at r = 11, 14 and 16 m), so the base meets the ground
        /// all the way round instead of floating on one side and sinking on the other.
        ///
        /// Chosen by re-sweeping all 3,863 flat fine (2 m) Haven surface samples
        /// against all 24 yaws under the STOOD-UP rules - the base ring seats, the
        /// whole 36 x 40 m footprint is flat, the footprint clears the authored props
        /// on the ground and overhead, and the shrine's slot at the foot is walkable.
        /// 145 workable (site, yaw) combinations over 39 distinct sites; this is the
        /// one nearest the spot the user physically stood on (Haven-local 168, 8)
        /// whose front face also looks at the approach from spawn.
        ///
        /// Measured here, all island-local:
        ///
        ///   * terrain on the wall ring 4.07 .. 5.45, seat 4.46 - dug in 0.98 m at
        ///     the worst bearing, standing off 0.39 m at the best. The tower meets
        ///     its ground; it neither floats nor sinks.
        ///   * terrain under the whole 36 x 40 m footprint 4.01 .. 6.16, spanning
        ///     2.15 m over 399 probes on a 2 m grid
        ///   * mesh bottom -2.90, roof 34.95: it stands 29.51 .. 30.88 m proud,
        ///     78.0% .. 81.6% of itself, against 49.2% at the placement the user
        ///     complained about
        ///   * 29.3 m from the nearest authored structure - 6.37 m of clear ground
        ///     between the camp and the whole 36 x 40 m footprint rectangle - with
        ///     nothing authored inside it and nothing overhead below the roof
        ///   * 17.0 m from the spot the user measured out, against 23.3 m for the
        ///     placement they complained about, and 54.4 m from the spawn point
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement =
            (156.00, 4.46, 20.00);

        /// <summary>
        /// The median terrain height on the wall ring at the chosen site, island-local
        /// - i.e. the ground the building is seated on. Equal to
        /// <c>HavenLocalPlacement.Y</c> by construction; kept separately so a test can
        /// say WHY that Y is that number.
        /// </summary>
        public const double SeatGroundY = 4.46;

        /// <summary>
        /// How far the terrain rises above the seat at the worst of the 72 ring
        /// probes, metres - how deep the tower is dug in on its highest side. This is
        /// the number the old placement got wrong by a factor of eighteen.
        /// </summary>
        public const double SeatDugInMetres = 0.98;

        /// <summary>
        /// How far the terrain falls below the seat at the best of the ring probes,
        /// metres - how far the wall stands off the ground on its lowest side. The
        /// foundation reaches 7.36 m down at r &lt;= 11 and about 5 m down at r 12..16,
        /// so anything under ~3 m here is covered by the prefab's own footing.
        /// </summary>
        public const double SeatStandOffMetres = 0.39;

        /// <summary>Terrain spread under the whole 36 x 40 m footprint, metres.</summary>
        public const double FootprintSpreadMetres = 2.15;

        /// <summary>
        /// How much of the building a player standing at its worst bearing can see,
        /// metres. THE MEASUREMENT THAT WAS MISSING: every test the old placement
        /// passed was about the doorway or the room, and not one of them asked what
        /// the thing looks like from outside.
        /// </summary>
        public static double ExposedHeightMetres => MeshTopLocalY - SeatDugInMetres;

        /// <summary>What fraction of the building stands above ground at its worst bearing.</summary>
        public static double ProudFraction => ExposedHeightMetres / MeshHeightMetres;

        /// <summary>
        /// The gate. A building that shows less than this much of itself reads as a
        /// hole, not a tower - the user's word for 49% was "ridiculous". 70% is below
        /// the 77.9% this placement measures and far above anything the old buried
        /// doctrine could reach anywhere on Haven (its ceiling was 50.9%), so it
        /// fails every burial and passes this.
        /// </summary>
        public const double MinimumProudFraction = 0.70;

        /// <summary>
        /// What fraction of the building stands above a given island-local ground
        /// height, at the current placement. Pure, so a test can feed it terrain read
        /// straight out of the embedded Haven surface table rather than a constant.
        /// </summary>
        public static double ProudFractionAgainst(double groundY) =>
            ProudFractionAgainst(HavenLocalPlacement.Y, groundY);

        /// <summary>The same, for an arbitrary origin height - so dead placements can be shown to fail.</summary>
        public static double ProudFractionAgainst(double originY, double groundY) =>
            ((originY + MeshTopLocalY) - groundY) / MeshHeightMetres;

        /// <summary>
        /// Facing, degrees, in the convention this server already flies ships in:
        /// <c>ShipyardDockingPolicy.PackedYaw</c> builds a rotation about +Y and
        /// <c>FlightIntegrator</c> turns that yaw into a world heading of
        /// <c>(sin yaw, cos yaw)</c> - so prefab local +x ends up pointing at world
        /// <c>(cos yaw, -sin yaw)</c> and prefab local +z at <c>(sin yaw, cos yaw)</c>.
        ///
        /// 240 deg turns the building's FRONT - the -z face, the one carrying the
        /// doorway and the entry lobe - to world (+0.87, +0.50), and puts the shrine's
        /// slot at Haven-local (176.78, 32.00): 41.9 m out of spawn and only 25 deg
        /// off the straight line from spawn to the tower, so a player walks to the pad
        /// and the tower is 12 m further on and slightly to their left.
        ///
        /// The front is 47 deg off pointing at the spawn dead on (0.68), and the
        /// reason it is not squarer is the ruined metal camp. The camp lies between
        /// this site and the spawn, so every yaw that aims the front straight down the
        /// approach lands the shrine's pad within 8 - 11 m of the camp's raised
        /// platform decks - i.e. under a deck, which is the failure that trapped a
        /// player at the very first placement. This yaw puts the pad 22.4 m clear of
        /// anything authored. The old 45 deg was aimed with the +x face, which is a
        /// blank wall.
        /// </summary>
        public const double YawDegrees = 240.0;

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
        /// Island-local height of the doorway sill. Now nearly 10 m up a sheer wall:
        /// a ruin's high entrance that reads as one from the ground and that nobody
        /// on foot can reach, which is the point - a player who got THROUGH it would
        /// be in a sealed drum with a 10.85 m drop and no way back out.
        /// </summary>
        public static double DoorwaySillIslandY => HavenLocalPlacement.Y + DoorwaySillLocalY;

        /// <summary>Island-local height of the doorway lintel.</summary>
        public static double DoorwayLintelIslandY => HavenLocalPlacement.Y + DoorwayLintelLocalY;

        // ------------------------------------------------------------------
        // THE SHRINE'S SLOT, and the ground the pair of them occupy.
        // ------------------------------------------------------------------

        /// <summary>
        /// Where the shrine stands, in PREFAB-LOCAL metres: 24 m straight out of the
        /// building's front face, on the same -z bearing the doorway looks down.
        ///
        /// It used to be (0, 0) - the centre of the room - and this is the one thing
        /// the user's fix costs them. 24 m is chosen, not free: the exterior mesh
        /// reaches r ~16.5 m at ground level and its front lobe overhangs to z = -21.9
        /// higher up, so 24 m clears the wall by 7.5 m and the overhang by 2 m, and
        /// leaves the shrine's whole 4.5 m prompt ring on open ground.
        ///
        /// Rotated by <see cref="YawDegrees"/> and added to the chamber, so the two
        /// can never drift apart: <see cref="ShrineSlotOn"/> is the only definition,
        /// and <c>WildernessShrine.HavenLocalPlacement</c> reads its x/z from it.
        /// </summary>
        public static readonly (double X, double Z) ShrineSlotLocal = (0.00, -24.00);

        /// <summary>
        /// The shrine's slot in ISLAND-LOCAL metres - the chamber's own x/z plus its
        /// slot turned by the chamber's yaw. Haven-local (176.78, 32.00).
        /// </summary>
        public static (double X, double Z) ShrineSlotOn()
        {
            double a = YawDegrees * Math.PI / 180.0;
            double cos = Math.Cos(a), sin = Math.Sin(a);
            return (HavenLocalPlacement.X + (ShrineSlotLocal.X * cos) + (ShrineSlotLocal.Z * sin),
                    HavenLocalPlacement.Z - (ShrineSlotLocal.X * sin) + (ShrineSlotLocal.Z * cos));
        }

        /// <summary>How far out the shrine stands from the chamber's axis, metres. 24.</summary>
        public static double ShrineSlotRadiusMetres =>
            Math.Sqrt((ShrineSlotLocal.X * ShrineSlotLocal.X) + (ShrineSlotLocal.Z * ShrineSlotLocal.Z));

        /// <summary>
        /// How far out from the chamber's axis nothing else this server plants may
        /// stand, metres. The building's own above-ground collision reaches ~21.9 m
        /// along its front lobe, and the shrine now stands at 24 m with a 5 m prompt
        /// ring around it, so 29 m is the disc the pair occupies.
        ///
        /// Widened from 22 m when the shrine moved out of the room: at 22 m the
        /// deposit field could generate ore on top of the teleporter pad. Trees,
        /// nodes and deposits are scattered from the same measured surface table the
        /// chamber was chosen from, so without this a tree grows through the roof -
        /// one already did, at the first attempt at the previous site.
        /// </summary>
        public const double ExclusionRadiusMetres = 29.0;

        /// <summary>
        /// How far out from the chamber's axis the GROUND IS CLEARED, metres - the
        /// 29 m the building and its shrine occupy, plus an apron.
        ///
        /// The user asked for this, standing on the shelf: "this is a small island
        /// attached to haven, empty the tree etc from it then place the tower here
        /// properly". The shelf itself turned out to be the whole low starting area -
        /// 885 measured surface samples spanning island-local x 105..257, z -46..76,
        /// and it CONTAINS THE SPAWN POINT - so clearing "the island" would strip the
        /// tutorial's own near-spawn wood. 35 m clears a 70 m circle around the tower
        /// instead, and leaves the spawn, 54 m away, and the rest of the shelf wooded.
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
        /// ONLY 190602. No 1210, and that is the point: the chamber is the landmark,
        /// and the one thing in this world that answers an interact is the shrine
        /// standing at its foot. Seeding 1210 here would re-create the sealed-well
        /// bug, because the prefab's own visualizer is on the plate at the bottom of
        /// the drum.
        /// </summary>
        public static readonly IReadOnlyList<uint> SeedComponents = new uint[] { 190602 };
    }
}
