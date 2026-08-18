using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One point a whale's circuit passes through: directly over one island, at
    /// <see cref="SkyWhalePolicy.AltitudeAboveIslandMetres"/> above its highest
    /// terrain. WORLD metres, not island-local - a whale is the one thing on this
    /// server that belongs to no island, so an island-local frame would have no
    /// meaning for the legs between them.
    /// </summary>
    public readonly record struct SkyWhaleWaypoint(
        IslandId IslandId, double X, double Y, double Z);

    /// <summary>
    /// THE PATH, and why it is this shape.
    ///
    /// The brief is a single animal transiting between the islands of one region,
    /// entering a player's world for a minute or two and leaving. Everything below
    /// is WAREBORN TUNING: retail's sky whale had no movement controller, no
    /// spawner and no flock - it was cut - so there is no Bossa design here to be
    /// faithful to, and this file must not be read as one.
    ///
    /// THE SHAPE: a CLOSED, UNIFORM CATMULL-ROM SPLINE through one waypoint per
    /// island of the region, taken in ANGULAR ORDER about the region's centroid.
    /// Four decisions, each with an alternative that was rejected:
    ///
    /// <list type="number">
    /// <item>THROUGH THE ISLANDS, not around them. Catmull-Rom INTERPOLATES its
    ///   control points, so at lap fraction i/N the whale is exactly over island i.
    ///   A Bezier or a B-spline only approaches them, which would make "does the
    ///   whale visit my island" a question about a tolerance instead of an
    ///   identity. The visit is the feature; it should be exact.</item>
    /// <item>ANGULAR ORDER about the centroid, not island-id order and not a
    ///   nearest-neighbour tour. Sorting by <c>atan2</c> about the centroid gives a
    ///   star-shaped, non-self-intersecting ring for ANY scatter of islands, with
    ///   no search and no tie-breaking heuristics - it is a total function of the
    ///   positions. Id order would produce a knot; a nearest-neighbour tour is a
    ///   greedy walk whose result changes discontinuously when one island moves,
    ///   which is precisely the property a closed-form path must not have.</item>
    /// <item>CLOSED, and therefore periodic. The whole feature rests on the pose
    ///   being a pure function of the clock (see
    ///   <see cref="IslandFaunaRegistry"/>'s remarks - a restarted server must
    ///   replay the identical path). A loop makes that trivially true forever; an
    ///   out-and-back would need a direction of travel, which is state.</item>
    /// <item>C1 CONTINUOUS, which is not decoration. The animal carries ONE
    ///   animation clip, <c>Whale_Swim</c>, with no parameters and no turn state
    ///   (RECOVERED). It can only be shown swimming forward. A polyline through
    ///   the same waypoints would snap its heading at every island - a 173 m
    ///   creature pivoting on the spot - whereas a Catmull-Rom spline has a
    ///   continuous tangent everywhere, so the heading this file derives from that
    ///   tangent always turns smoothly and the one clip is always right.</item>
    /// </list>
    ///
    /// WHAT THIS SHAPE COSTS, measured rather than hand-waved. Uniform (rather than
    /// centripetal) Catmull-Rom parameterisation means the whale's SPEED varies
    /// along the lap - faster across the long legs, slower through a cluster - and
    /// can overshoot slightly outside the ring where two waypoints are much closer
    /// together than their neighbours. On the real B3 circuit the instantaneous
    /// speed runs 7.3 to 33.9 m/s about an 18 m/s average
    /// (<c>SkyWhaleMotionTests</c> prints and pins that band). Both were accepted:
    /// an animal that speeds up over open sky and dawdles among the rocks is a
    /// better result than the alternative, and the centripetal variant's exponent
    /// would have to be restated exactly in the browser mirror for a difference
    /// nobody can see.
    /// <see cref="CircuitSeconds"/> is therefore derived from the CHORD length, and
    /// is honest about being an average.
    ///
    /// PURE, TOTAL AND ALLOCATION-FREE once built. <see cref="PositionAt"/> and
    /// <see cref="TangentAt"/> read four control points and evaluate a cubic; there
    /// is no clock, no entropy, no integration and no remembered pose.
    /// </summary>
    public sealed class SkyWhaleCircuit
    {
        private readonly SkyWhaleWaypoint[] _waypoints;

        private SkyWhaleCircuit(RegionId region, SkyWhaleWaypoint[] waypoints,
            double lengthMetres, double circuitSeconds, double phaseFraction)
        {
            Region = region;
            _waypoints = waypoints;
            LengthMetres = lengthMetres;
            CircuitSeconds = circuitSeconds;
            PhaseFraction = phaseFraction;
        }

        /// <summary>The region whose islands this circuit strings together.</summary>
        public RegionId Region { get; }

        /// <summary>The ring, in travel order. Never fewer than three.</summary>
        public IReadOnlyList<SkyWhaleWaypoint> Waypoints => _waypoints;

        /// <summary>
        /// The CHORD length of the ring, in metres - the sum of the straight legs
        /// between consecutive waypoints, closing back to the first.
        ///
        /// Deliberately the chord and not the spline's arc length. The spline is
        /// slightly longer than its control polygon wherever it bulges, so this
        /// UNDERSTATES the distance by a few percent and the whale is correspondingly
        /// a few percent faster than <see cref="SkyWhalePolicy.MetresPerSecond"/>.
        /// The alternative is a numerically integrated arc length, which the browser
        /// mirror would have to reproduce to a nanometre using the same quadrature -
        /// a real risk of drift, bought for an error smaller than the tuning
        /// uncertainty in the speed itself.
        /// </summary>
        public double LengthMetres { get; }

        /// <summary>
        /// How long one lap takes, in seconds: <see cref="LengthMetres"/> divided by
        /// <see cref="SkyWhalePolicy.MetresPerSecond"/>. A CONSEQUENCE of the
        /// region's size rather than a constant, which is the same choice
        /// <see cref="IslandFaunaMovement.MantaMetresPerSecond"/> documents: a fixed
        /// lap time would make a whale in a large cell supersonic and one in a small
        /// cell becalmed.
        /// </summary>
        public double CircuitSeconds { get; }

        /// <summary>Where on the lap this region's whale starts. See <see cref="SkyWhalePolicy.PhaseFractionFor"/>.</summary>
        public double PhaseFraction { get; }

        /// <summary>
        /// Builds a region's circuit, or returns null when the region cannot carry
        /// one.
        ///
        /// NULL RATHER THAN THROW, and the two null cases are both "this region
        /// has no whale", which is a quieter world and nothing worse: fewer than
        /// <see cref="SkyWhalePolicy.MinimumIslandsPerRegion"/> waypoints, or a
        /// degenerate ring of zero length. A server must not fail to boot because a
        /// district selection happened to name two islands.
        /// </summary>
        public static SkyWhaleCircuit? Build(
            RegionId region,
            IEnumerable<SkyWhaleWaypoint> waypoints,
            double metresPerSecond = SkyWhalePolicy.MetresPerSecond,
            double? phaseFraction = null)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            if (metresPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(metresPerSecond),
                    "a whale that does not move has no circuit");
            }

            SkyWhaleWaypoint[] ordered = OrderAroundCentroid(waypoints);
            if (ordered.Length < SkyWhalePolicy.MinimumIslandsPerRegion)
            {
                return null;
            }

            double length = ChordLengthOf(ordered);
            if (!(length > 0.0) || double.IsNaN(length) || double.IsInfinity(length))
            {
                return null;
            }

            return new SkyWhaleCircuit(region, ordered, length,
                length / metresPerSecond,
                phaseFraction ?? SkyWhalePolicy.PhaseFractionFor(region));
        }

        /// <summary>
        /// The ring order: bearing about the lateral centroid, ties broken by island
        /// id so the result cannot depend on the order the caller enumerated in.
        ///
        /// The centroid is the mean of the waypoints rather than of the islands'
        /// AABBs on purpose - the waypoints ARE what is being ordered, so ordering
        /// them about anything else could put a waypoint on the wrong side of the
        /// ring. Y is ignored: this is a lateral ring that rises and falls with the
        /// islands, not a three-dimensional tour.
        /// </summary>
        private static SkyWhaleWaypoint[] OrderAroundCentroid(
            IEnumerable<SkyWhaleWaypoint> waypoints)
        {
            List<SkyWhaleWaypoint> all = new List<SkyWhaleWaypoint>(waypoints);
            if (all.Count == 0) return Array.Empty<SkyWhaleWaypoint>();

            double centreX = 0.0, centreZ = 0.0;
            foreach (SkyWhaleWaypoint waypoint in all)
            {
                centreX += waypoint.X;
                centreZ += waypoint.Z;
            }
            centreX /= all.Count;
            centreZ /= all.Count;

            return all
                .OrderBy(waypoint => Math.Atan2(waypoint.Z - centreZ, waypoint.X - centreX))
                .ThenBy(waypoint => waypoint.IslandId)
                .ToArray();
        }

        /// <summary>The closed control polygon's length, in metres.</summary>
        private static double ChordLengthOf(IReadOnlyList<SkyWhaleWaypoint> ring)
        {
            double total = 0.0;
            for (int i = 0; i < ring.Count; i++)
            {
                SkyWhaleWaypoint from = ring[i];
                SkyWhaleWaypoint to = ring[(i + 1) % ring.Count];
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double dz = to.Z - from.Z;
                total += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
            return total;
        }

        /// <summary>
        /// Where the whale is at lap fraction <paramref name="lap"/>, in world
        /// metres. Any real number is accepted; it is wrapped.
        /// </summary>
        public (double X, double Y, double Z) PositionAt(double lap) =>
            EvaluatePosition(_waypoints, lap);

        /// <summary>
        /// Which way it is going at lap fraction <paramref name="lap"/>: the
        /// spline's derivative, NOT normalised. Callers that want a heading
        /// normalise it themselves (<c>IslandFaunaOrientation.LookRotation</c> does).
        /// </summary>
        public (double X, double Y, double Z) TangentAt(double lap) =>
            EvaluateTangent(_waypoints, lap);

        /// <summary>Where the whale is <paramref name="elapsedSeconds"/> into the world's life.</summary>
        public (double X, double Y, double Z) PositionAtTime(double elapsedSeconds) =>
            PositionAt(LapAt(elapsedSeconds));

        /// <summary>Which way it is going at that instant.</summary>
        public (double X, double Y, double Z) TangentAtTime(double elapsedSeconds) =>
            TangentAt(LapAt(elapsedSeconds));

        /// <summary>
        /// The lap fraction at an absolute elapsed time. ABSOLUTE rather than an
        /// age, for the reason <see cref="IslandFaunaRegistry"/> spells out: an age
        /// depends on when the whale happened to be added, so two servers that
        /// seeded the same region at different points in their boot would disagree
        /// about where the same animal is.
        /// </summary>
        public double LapAt(double elapsedSeconds) =>
            Fraction((elapsedSeconds / CircuitSeconds) + PhaseFraction);

        /// <summary>
        /// WHICH ISLAND THE WHALE REACHES NEXT, and how many seconds away it is.
        ///
        /// This exists for the boot log, and the reason is the one
        /// <c>IslandFaunaService</c> learned the hard way when it started naming
        /// its populated islands: "4 whales across 4 regions" tells an operator the
        /// seeding worked and tells a PLAYER nothing at all, and a feature nobody
        /// can find is indistinguishable from one that is broken. A whale is the
        /// worst case of that - it is one animal in a region several kilometres
        /// across and it is only overhead for about a minute at a time - so the
        /// server says where to stand and when.
        ///
        /// The whale is exactly over waypoint i at lap i/N (Catmull-Rom
        /// interpolates its control points), so this is arithmetic rather than a
        /// search.
        /// </summary>
        public (IslandId IslandId, double Seconds) NextArrivalAfter(double elapsedSeconds)
        {
            double lap = LapAt(elapsedSeconds);
            int n = _waypoints.Length;
            // The next INDEX strictly ahead, wrapping. Exactly on a knot counts as
            // arrived, so the answer is the one after it rather than "in 0 s".
            int next = (int)Math.Floor(lap * n) + 1;
            double untilLaps = Fraction(((double)next / n) - lap);
            return (_waypoints[next % n].IslandId, untilLaps * CircuitSeconds);
        }

        /// <summary>
        /// THE CURVE, as a static over an explicit ring, so a second evaluator can
        /// be tested against exactly this function with the waypoints the wire
        /// carried rather than against a rebuilt circuit.
        ///
        /// Uniform closed Catmull-Rom. The expression is written out in full, in
        /// this term order, and the browser mirror in
        /// <c>WorldsAdriftServer/Web/Assets/map-fauna.js</c> restates it verbatim -
        /// <c>AdminSkyWhaleParityTests</c> fails at a nanometre if they diverge.
        /// Rewriting it in Horner form here without doing the same there would break
        /// that test for a reason no reader would guess, so do not "tidy" one side.
        /// </summary>
        public static (double X, double Y, double Z) EvaluatePosition(
            IReadOnlyList<SkyWhaleWaypoint> ring, double lap)
        {
            (SkyWhaleWaypoint p0, SkyWhaleWaypoint p1, SkyWhaleWaypoint p2,
                SkyWhaleWaypoint p3, double t) = SegmentAt(ring, lap);
            return (
                CubicPosition(p0.X, p1.X, p2.X, p3.X, t),
                CubicPosition(p0.Y, p1.Y, p2.Y, p3.Y, t),
                CubicPosition(p0.Z, p1.Z, p2.Z, p3.Z, t));
        }

        /// <summary>The same curve's derivative with respect to the SEGMENT parameter. See <see cref="EvaluatePosition"/>.</summary>
        public static (double X, double Y, double Z) EvaluateTangent(
            IReadOnlyList<SkyWhaleWaypoint> ring, double lap)
        {
            (SkyWhaleWaypoint p0, SkyWhaleWaypoint p1, SkyWhaleWaypoint p2,
                SkyWhaleWaypoint p3, double t) = SegmentAt(ring, lap);
            return (
                CubicTangent(p0.X, p1.X, p2.X, p3.X, t),
                CubicTangent(p0.Y, p1.Y, p2.Y, p3.Y, t),
                CubicTangent(p0.Z, p1.Z, p2.Z, p3.Z, t));
        }

        /// <summary>
        /// Which segment a lap fraction lands in, and how far along it. The modulo
        /// arithmetic wraps in BOTH directions so a negative or an enormous lap is
        /// as valid as one in [0,1) - a total function, because a clock is allowed
        /// to be large and a test is allowed to be perverse.
        /// </summary>
        private static (SkyWhaleWaypoint P0, SkyWhaleWaypoint P1, SkyWhaleWaypoint P2,
            SkyWhaleWaypoint P3, double T) SegmentAt(
                IReadOnlyList<SkyWhaleWaypoint> ring, double lap)
        {
            if (ring == null) throw new ArgumentNullException(nameof(ring));
            int n = ring.Count;
            if (n < SkyWhalePolicy.MinimumIslandsPerRegion)
            {
                throw new ArgumentException(
                    "a closed circuit needs at least "
                    + SkyWhalePolicy.MinimumIslandsPerRegion + " waypoints", nameof(ring));
            }

            double s = Fraction(lap) * n;
            int i = (int)Math.Floor(s);
            // Fraction() can return a value that multiplies to exactly n in floating
            // point at the very top of its range; clamp rather than index past the end.
            if (i >= n) i = n - 1;
            double t = s - i;

            return (ring[((i - 1) % n + n) % n], ring[i],
                ring[(i + 1) % n], ring[(i + 2) % n], t);
        }

        /// <summary>The uniform Catmull-Rom basis on one axis. See <see cref="EvaluatePosition"/> about the term order.</summary>
        private static double CubicPosition(double p0, double p1, double p2, double p3, double t) =>
            0.5 * ((2.0 * p1)
                + ((-p0 + p2) * t)
                + (((2.0 * p0) - (5.0 * p1) + (4.0 * p2) - p3) * t * t)
                + ((-p0 + (3.0 * p1) - (3.0 * p2) + p3) * t * t * t));

        /// <summary>Its derivative on one axis. See <see cref="EvaluatePosition"/> about the term order.</summary>
        private static double CubicTangent(double p0, double p1, double p2, double p3, double t) =>
            0.5 * ((-p0 + p2)
                + (2.0 * ((2.0 * p0) - (5.0 * p1) + (4.0 * p2) - p3) * t)
                + (3.0 * (-p0 + (3.0 * p1) - (3.0 * p2) + p3) * t * t));

        /// <summary>
        /// The fractional part, always in [0,1). Identical to
        /// <see cref="IslandFaunaSchool.Fraction"/> and restated rather than called
        /// so this type depends on nothing in the fauna stack, which is a different
        /// feature behind a different flag.
        /// </summary>
        public static double Fraction(double value)
        {
            double fraction = value - Math.Floor(value);
            return fraction < 0.0 ? fraction + 1.0 : (fraction >= 1.0 ? 0.0 : fraction);
        }
    }
}
