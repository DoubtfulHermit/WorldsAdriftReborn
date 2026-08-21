namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// ONE weather wall, in the exact shape <c>1204 WallSegmentState</c> wants plus
    /// the transform that places it. It is the whole server-side output of this
    /// feature: everything the shipped client does with a wall - the opaque billowing
    /// cloud, the rain, the debris, the audio mix, the free ambient lightning - it
    /// does from these four numbers and a position, with no further server
    /// involvement.
    ///
    /// THREE THINGS HERE ARE EASY TO GET WRONG, so each is named.
    ///
    /// 1. <see cref="HalfLength"/> IS A HALF-LENGTH. <c>WallData</c>'s constructor is
    ///    <c>P1 = position - forward*Length; P2 = position + forward*Length</c>
    ///    (acs/WallData.cs:111-120, PROVED). A 5 km wall is one entity at its
    ///    midpoint carrying 2500, not 5000. Sending the full length doubles the wall.
    ///
    /// 2. ONE ENTITY PER WALL IS EXACT, not an approximation. <c>WallData.DistanceSqr</c>
    ///    is distance-to-ONE-line-segment and <c>WallData.Add</c> merges every segment
    ///    sharing a <see cref="WallId"/> into their axial extent, so N collinear
    ///    segments produce a bit-identical distance field to one segment spanning the
    ///    same extent (findings-storm-walls.md section 6). Retail's subdivision was an
    ///    interest-management device and nothing else.
    ///
    /// 3. <see cref="WallId"/> MUST BE UNIQUE PER WALL. <c>WeatherWalls.Register</c>
    ///    keys <c>_wallsById</c> by it and calls <c>Add</c> on a collision, which
    ///    FUSES the newcomer into the existing wall AND KEEPS THE EXISTING WALL'S
    ///    TYPE (acs/WeatherWalls.cs:87-105 - <c>Type</c> is readonly and set only in
    ///    the constructor). Two walls sharing an id is therefore not a duplicate, it
    ///    is one enormous wall of the wrong kind.
    ///
    /// THE Y COORDINATE IS INERT, and that is why <see cref="Midpoint"/> carries a
    /// flat one. Every distance the wall system computes is XZ-only
    /// (<c>MathUtils.Vector3toXZ</c>, used by <c>DistanceSqr</c>, <c>IsInsideStorm</c>,
    /// <c>GetIntensityAt</c> and <c>QueryAt</c> alike), so walls are infinitely tall
    /// for both force and texture purposes; the renderer's own ceiling is the
    /// shader's <c>_StormHeight ~ 3500</c>, not ours; and an ambient bolt picks its
    /// own height with <c>Random.Range(-1000f, 800f)</c> regardless of where the
    /// segment sits (acs/WallData.cs:122-135, PROVED). Nothing reads the wall's Y.
    /// </summary>
    public readonly struct WallSegmentSeed
    {
        public WallSegmentSeed(
            int wallId,
            WallType type,
            FixedPointPosition midpoint,
            double orientationX,
            double orientationY,
            double orientationZ,
            float halfLength)
        {
            WallId = wallId;
            Type = type;
            Midpoint = midpoint;
            OrientationX = orientationX;
            OrientationY = orientationY;
            OrientationZ = orientationZ;
            HalfLength = halfLength;
        }

        /// <summary>
        /// The wall's identity on the wire, and the key <c>WeatherWalls</c> groups by.
        /// Unique per wall - see the type remarks for what a collision does.
        /// </summary>
        public int WallId { get; }

        /// <summary>Which kind of wall. Goes on the wire as <c>wallType</c>.</summary>
        public WallType Type { get; }

        /// <summary>
        /// The <c>190602 TransformState.localPosition</c> seed: the wall's MIDPOINT.
        /// The visualiser sets only <c>transform.forward</c>; the position comes
        /// entirely from the transform component (acs/WallSegmentVisualizer.cs:19-22).
        /// </summary>
        public FixedPointPosition Midpoint { get; }

        /// <summary>Unit direction along the wall, X component. Wire <c>orientation</c>.</summary>
        public double OrientationX { get; }

        /// <summary>
        /// Unit direction along the wall, Y component. Always 0: the source geometry
        /// is 2D and a non-flat forward would tilt <c>WallData.Forward</c>, which is
        /// the axis the storm-rift yaw torque aligns a ship to.
        /// </summary>
        public double OrientationY { get; }

        /// <summary>Unit direction along the wall, Z component. Wire <c>orientation</c>.</summary>
        public double OrientationZ { get; }

        /// <summary>HALF the wall's length, in metres. Wire <c>length</c>. See the type remarks.</summary>
        public float HalfLength { get; }

        /// <summary>The wire value of <see cref="Type"/>, for the serializer branch.</summary>
        public int WallTypeId => (int)Type;

        /// <summary>The full wall length in metres. For logs, budgets and tests only; never sent.</summary>
        public double LengthMetres => 2.0 * HalfLength;
    }
}
