using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One point the whale's route passes through.
    ///
    /// TWO KINDS OF POINT, and the distinction is the whole of the migration. An
    /// ISLAND waypoint sits directly over one island, at
    /// <see cref="SkyWhalePolicy.AltitudeAboveIslandMetres"/> above its highest
    /// terrain - that is a VISIT, and Catmull-Rom interpolates its control points
    /// so the visit is an identity rather than a tolerance. A TRANSIT waypoint sits
    /// on the open-sky leg BETWEEN two zones and is over nothing at all; it exists
    /// only so the crossing is made of segments the same size as the ones inside a
    /// zone (see <see cref="SkyWhaleRoute"/> on why that is a speed requirement and
    /// not decoration).
    ///
    /// A TRANSIT WAYPOINT STILL CARRIES AN ISLAND ID, and it is not a lie: it is
    /// the ANCHOR the point is published relative to. Every coordinate this server
    /// sends the map is an island-local offset, because the map places islands from
    /// the preserved MapFile and the game server places them from its own
    /// catalogue; a transit point published in absolute metres would drift away
    /// from the rocks either side of it. The anchor is the nearer of the leg's two
    /// endpoints, so the error is always the smaller of the two.
    ///
    /// WORLD METRES, not island-local - a whale is the one thing on this server
    /// that belongs to no island, so an island-local frame would have no meaning
    /// for the legs between them, and now none at all for the legs between zones.
    /// </summary>
    /// <param name="IslandId">The island this point is over, or - when
    /// <see cref="SkyWhaleWaypoint.IsTransit"/> - the island it is published
    /// relative to.</param>
    /// <param name="Region">The region this point belongs to. For a transit point,
    /// the region of its anchor island - which is deliberately NOT used to say
    /// where the whale is: <see cref="SkyWhaleCircuit.WhereAt"/> only ever reads
    /// island waypoints.</param>
    /// <param name="IsTransit">Whether this point is a crossing rather than a
    /// visit. See the type remarks.</param>
    public readonly record struct SkyWhaleWaypoint(
        IslandId IslandId, double X, double Y, double Z, RegionId Region, bool IsTransit)
    {
        /// <summary>
        /// An ISLAND waypoint that has not been placed on a route yet - no zone, not
        /// a crossing. The four-argument shape is kept because every caller that
        /// describes one island still says exactly this much, and
        /// <see cref="SkyWhaleRoute"/> is the only thing that has any business
        /// deciding which zone a point ends up in.
        /// </summary>
        public SkyWhaleWaypoint(IslandId islandId, double x, double y, double z)
            : this(islandId, x, y, z, default, false)
        {
        }
    }

    /// <summary>
    /// WHERE THE WHALE IS IN THE WORLD, in zones rather than in metres: which zone
    /// it is over now, which zone it goes to next and when.
    ///
    /// ONE STRUCT FROM ONE EVALUATION, for the reason
    /// <see cref="SkyWhaleMotion.WorldTransformAt"/> gives about pose and heading:
    /// three separate accessors would be correct today and would rot the moment one
    /// of them was called against a different clock, and this is precisely the
    /// answer the boot log and the map note are built out of.
    /// </summary>
    /// <param name="Region">The zone it is over, or the default when it is between
    /// zones.</param>
    /// <param name="InTransit">Whether it is on an open-sky crossing.</param>
    /// <param name="NextRegion">The zone it enters next - while in transit, the one
    /// it is crossing towards. The default when the route has only one zone.</param>
    /// <param name="NextRegionIsland">The island it will be over when it enters
    /// <see cref="NextRegion"/> - where a player of that zone should stand.</param>
    /// <param name="SecondsToNextRegion">How long until it does.</param>
    /// <param name="NextIsland">The next island of any zone it passes over.</param>
    /// <param name="SecondsToNextIsland">How long until it does.</param>
    public readonly record struct SkyWhaleWhereabouts(
        RegionId Region,
        bool InTransit,
        RegionId NextRegion,
        IslandId NextRegionIsland,
        double SecondsToNextRegion,
        IslandId NextIsland,
        double SecondsToNextIsland);

    /// <summary>
    /// THE PATH, and why it is this shape.
    ///
    /// The brief is ONE animal in the whole world, migrating from zone to zone -
    /// entering a player's sky for a minute or two, working through that zone's
    /// islands, and then leaving for another cell of the map entirely. Everything
    /// below is WAREBORN TUNING: retail's sky whale had no movement controller, no
    /// spawner and no flock - it was cut - so there is no Bossa design here to be
    /// faithful to, and this file must not be read as one.
    ///
    /// THE SHAPE: ONE CLOSED, UNIFORM CATMULL-ROM SPLINE through every island in
    /// the world, zone by zone, with the crossings between zones resampled into
    /// segments the same size as the ones inside a zone. The ORDER is
    /// <see cref="SkyWhaleRoute"/>'s job and is argued there; this file is the
    /// curve and the clock. Four decisions, each with an alternative that was
    /// rejected:
    ///
    /// <list type="number">
    /// <item>THROUGH THE ISLANDS, not around them. Catmull-Rom INTERPOLATES its
    ///   control points, so at lap fraction i/N the whale is exactly over waypoint
    ///   i. A Bezier or a B-spline only approaches them, which would make "does the
    ///   whale visit my island" a question about a tolerance instead of an
    ///   identity. The visit is the feature; it should be exact.</item>
    /// <item>ONE CURVE FOR THE WHOLE WORLD, not a per-zone circuit plus a scripted
    ///   departure. This is the decision the single-whale rework turns on. A whale
    ///   that flew a closed ring in one zone and then "left" would need a departure
    ///   EVENT - a moment at which the ring stops being the path - and that event
    ///   is state: it has a time, it has to be persisted or re-derived, and the
    ///   blend out of the ring has to match the ring's tangent or the animal
    ///   pivots. Making the crossing another SEGMENT OF THE SAME SPLINE removes
    ///   the event entirely. There is no hand-off, so there is no hand-off to get
    ///   wrong: the pose is still a single closed form of the clock, and C1
    ///   continuity at a zone boundary is the same property it is at any other
    ///   knot.</item>
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
    /// together than their neighbours. That was accepted for a region-sized ring
    /// and it is what makes <see cref="SkyWhaleRoute"/>'s resampling of the
    /// zone-to-zone crossings MANDATORY rather than tidy: an unresampled 9 km
    /// crossing sitting next to 1.5 km island hops would be given the same slice of
    /// the lap as its neighbours and the whale would cross it at six times its own
    /// speed. <c>SkyWhaleMotionTests</c> prints and pins the resulting speed band.
    /// <see cref="CircuitSeconds"/> is derived from the CHORD length, and is honest
    /// about being an average.
    ///
    /// PURE, TOTAL AND ALLOCATION-FREE once built. <see cref="PositionAt"/> and
    /// <see cref="TangentAt"/> read four control points and evaluate a cubic; there
    /// is no clock, no entropy, no integration and no remembered pose.
    /// </summary>
    public sealed class SkyWhaleCircuit
    {
        private readonly SkyWhaleWaypoint[] _waypoints;

        private SkyWhaleCircuit(string routeId, SkyWhaleWaypoint[] waypoints,
            double lengthMetres, double circuitSeconds, double phaseFraction)
        {
            RouteId = routeId;
            _waypoints = waypoints;
            LengthMetres = lengthMetres;
            CircuitSeconds = circuitSeconds;
            PhaseFraction = phaseFraction;
        }

        /// <summary>
        /// The route's stable name. NOT a region any more, and that rename is the
        /// point: the whale belongs to the world, not to a cell of it, and a
        /// property still called <c>Region</c> would be the first thing to mislead
        /// a reader about which of the two designs this is.
        /// </summary>
        public string RouteId { get; }

        /// <summary>The route, in travel order. Never fewer than three points.</summary>
        public IReadOnlyList<SkyWhaleWaypoint> Waypoints => _waypoints;

        /// <summary>How many of those points are island VISITS rather than crossings.</summary>
        public int IslandCount
        {
            get
            {
                int count = 0;
                foreach (SkyWhaleWaypoint waypoint in _waypoints)
                {
                    if (!waypoint.IsTransit) count++;
                }
                return count;
            }
        }

        /// <summary>Every zone the route passes through, in travel order, without repeats.</summary>
        public IReadOnlyList<RegionId> Regions
        {
            get
            {
                List<RegionId> regions = new List<RegionId>();
                foreach (SkyWhaleWaypoint waypoint in _waypoints)
                {
                    if (waypoint.IsTransit) continue;
                    if (regions.Count == 0 || regions[regions.Count - 1] != waypoint.Region)
                    {
                        regions.Add(waypoint.Region);
                    }
                }
                // The route is closed, so the last block and the first are the same
                // zone whenever the tour wrapped inside one.
                if (regions.Count > 1 && regions[0] == regions[regions.Count - 1])
                {
                    regions.RemoveAt(regions.Count - 1);
                }
                return regions;
            }
        }

        /// <summary>
        /// The CHORD length of the route, in metres - the sum of the straight legs
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
        /// How long one lap of the WORLD takes, in seconds:
        /// <see cref="LengthMetres"/> divided by
        /// <see cref="SkyWhalePolicy.MetresPerSecond"/>. A CONSEQUENCE of how big
        /// the world is rather than a constant, which is the same choice
        /// <see cref="IslandFaunaMovement.MantaMetresPerSecond"/> documents: a fixed
        /// lap time would make the whale supersonic in a large world and becalmed in
        /// a small one. With one whale this is also the interval between two
        /// consecutive visits to the SAME island - the rarity the brief asked for is
        /// this number and nothing else, and <c>SkyWhalePlanTests</c> pins it.
        /// </summary>
        public double CircuitSeconds { get; }

        /// <summary>Where on the lap the whale starts. See <see cref="SkyWhalePolicy.PhaseFractionFor"/>.</summary>
        public double PhaseFraction { get; }

        /// <summary>
        /// Builds the route's curve from waypoints ALREADY IN TRAVEL ORDER, or
        /// returns null when the world cannot carry one.
        ///
        /// ORDER IS NOT THIS TYPE'S JOB any more. It was, while a circuit was one
        /// region's ring and "sort by bearing about the centroid" was the whole
        /// answer; a world route has to decide the order of the ZONES as well as of
        /// the islands inside them, and doing that here would have buried the
        /// migration's only real design decision inside a spline evaluator. It lives
        /// in <see cref="SkyWhaleRoute"/>, which is pure and separately tested, and
        /// this constructor takes what it produced verbatim - the same discipline
        /// the map projection already follows.
        ///
        /// NULL RATHER THAN THROW, and both null cases are "this world has no
        /// whale", which is a quieter world and nothing worse: fewer than
        /// <see cref="SkyWhalePolicy.MinimumIslands"/> waypoints, or a degenerate
        /// route of zero length. A server must not fail to boot because a district
        /// selection happened to name two islands.
        /// </summary>
        public static SkyWhaleCircuit? Build(
            string routeId,
            IEnumerable<SkyWhaleWaypoint> travelOrder,
            double metresPerSecond = SkyWhalePolicy.MetresPerSecond,
            double? phaseFraction = null)
        {
            if (string.IsNullOrWhiteSpace(routeId))
            {
                throw new ArgumentException("a route id must not be empty", nameof(routeId));
            }
            if (travelOrder == null) throw new ArgumentNullException(nameof(travelOrder));
            if (metresPerSecond <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(metresPerSecond),
                    "a whale that does not move has no route");
            }

            SkyWhaleWaypoint[] ordered = travelOrder.ToArray();
            if (ordered.Length < SkyWhalePolicy.MinimumIslands)
            {
                return null;
            }

            double length = ChordLengthOf(ordered);
            if (!(length > 0.0) || double.IsNaN(length) || double.IsInfinity(length))
            {
                return null;
            }

            return new SkyWhaleCircuit(routeId, ordered, length,
                length / metresPerSecond,
                phaseFraction ?? SkyWhalePolicy.PhaseFractionFor(routeId));
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
        /// seeded at different points in their boot would disagree about where the
        /// same animal is.
        /// </summary>
        public double LapAt(double elapsedSeconds) =>
            Fraction((elapsedSeconds / CircuitSeconds) + PhaseFraction);

        /// <summary>
        /// WHICH ISLAND THE WHALE REACHES NEXT, and how many seconds away it is.
        ///
        /// This exists for the boot log, and the reason is the one
        /// <c>IslandFaunaService</c> learned the hard way when it started naming
        /// its populated islands: "one whale on a 46-island route" tells an operator
        /// the seeding worked and tells a PLAYER nothing at all, and a feature
        /// nobody can find is indistinguishable from one that is broken. With ONE
        /// whale in the world that is no longer merely the worst case, it is the
        /// whole risk of the feature: most zones have no whale most of the time by
        /// design, so the server has to say where to stand and when or the change
        /// reads as a regression.
        ///
        /// The whale is exactly over waypoint i at lap i/N (Catmull-Rom
        /// interpolates its control points), so this is arithmetic rather than a
        /// search. TRANSIT points are skipped: they are over open sky and nobody
        /// can stand under them.
        /// </summary>
        public (IslandId IslandId, double Seconds) NextArrivalAfter(double elapsedSeconds)
        {
            double lap = LapAt(elapsedSeconds);
            int n = _waypoints.Length;
            // The next INDEX strictly ahead, wrapping. Exactly on a knot counts as
            // arrived, so the answer is the one after it rather than "in 0 s".
            int next = (int)Math.Floor(lap * n) + 1;
            for (int step = 0; step < n && _waypoints[((next % n) + n) % n].IsTransit; step++)
            {
                next++;
            }
            int index = ((next % n) + n) % n;
            return (_waypoints[index].IslandId, SecondsToKnot(lap, next, n));
        }

        /// <summary>
        /// WHICH ZONE IT IS IN, WHICH ZONE IT GOES TO NEXT, AND WHEN - the answer
        /// the boot log and both maps are written out of, and the one question a
        /// single migrating whale makes worth asking at all.
        ///
        /// IN A ZONE means the current SEGMENT runs between two island waypoints of
        /// the same zone. Anything else - a leg out of a zone, a resampled crossing,
        /// a leg into the next zone - is IN TRANSIT, and that is the honest reading:
        /// the moment the animal leaves the last island of a cell it is no longer
        /// over that cell in any sense a player would recognise, whichever rock the
        /// next control point happens to be anchored to.
        ///
        /// ENTERING A ZONE is defined as reaching its first ISLAND, not as crossing
        /// some notional cell boundary. A cell boundary is a line on the map file
        /// that nobody can see; an island is a place a player can stand and look up
        /// from, which is what the countdown is for.
        /// </summary>
        public SkyWhaleWhereabouts WhereAt(double elapsedSeconds)
        {
            double lap = LapAt(elapsedSeconds);
            int n = _waypoints.Length;
            double s = lap * n;
            int i = (int)Math.Floor(s);
            if (i >= n) i = n - 1;
            if (i < 0) i = 0;

            SkyWhaleWaypoint from = _waypoints[i];
            SkyWhaleWaypoint to = _waypoints[(i + 1) % n];
            bool inRegion = !from.IsTransit && !to.IsTransit && from.Region == to.Region;
            RegionId region = inRegion ? from.Region : default;

            int nextIsland = i + 1;
            for (int step = 0; step < n && _waypoints[(nextIsland % n + n) % n].IsTransit; step++)
            {
                nextIsland++;
            }

            // The first island knot ahead that belongs to a DIFFERENT zone. While in
            // transit `region` is the default, which no real zone equals, so the
            // first island ahead wins - it is the zone being crossed towards.
            int nextRegion = i + 1;
            bool found = false;
            for (int step = 0; step < n; step++)
            {
                SkyWhaleWaypoint candidate = _waypoints[(nextRegion % n + n) % n];
                if (!candidate.IsTransit && !(inRegion && candidate.Region == region))
                {
                    found = true;
                    break;
                }
                nextRegion++;
            }

            SkyWhaleWaypoint island = _waypoints[(nextIsland % n + n) % n];
            SkyWhaleWaypoint entry = _waypoints[(nextRegion % n + n) % n];
            return new SkyWhaleWhereabouts(
                Region: region,
                InTransit: !inRegion,
                // A one-zone world has no next zone, and saying so is better than
                // naming the zone it is already in and calling that a migration.
                NextRegion: found ? entry.Region : default,
                NextRegionIsland: found ? entry.IslandId : default,
                SecondsToNextRegion: found ? SecondsToKnot(lap, nextRegion, n) : 0.0,
                NextIsland: island.IslandId,
                SecondsToNextIsland: SecondsToKnot(lap, nextIsland, n));
        }

        /// <summary>How long until the whale is exactly over knot <paramref name="knot"/>.</summary>
        private double SecondsToKnot(double lap, int knot, int n) =>
            Fraction(((double)knot / n) - lap) * CircuitSeconds;

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
        ///
        /// NOTHING HERE KNOWS ABOUT ZONES, and that is deliberate: the migration is
        /// entirely a property of the CONTROL POINTS, which are data on the wire, so
        /// the browser mirror did not have to change at all to fly it.
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
            if (n < SkyWhalePolicy.MinimumIslands)
            {
                throw new ArgumentException(
                    "a closed route needs at least "
                    + SkyWhalePolicy.MinimumIslands + " waypoints", nameof(ring));
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
