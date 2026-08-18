using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>One zone's island waypoints, before anything has decided their order.</summary>
    public readonly record struct SkyWhaleZone(
        RegionId Region, IReadOnlyList<SkyWhaleWaypoint> Islands);

    /// <summary>
    /// THE MIGRATION: the order one whale visits the whole world in.
    ///
    /// This file exists because the world went from four whales to ONE. Four
    /// whales needed only "which order do the islands of a cell go in", and the
    /// answer - bearing about the cell's centroid - fitted inside the spline
    /// evaluator. One whale has to answer "which order do the ZONES go in, where
    /// does it enter each one, and what happens in between", and those are the only
    /// real design decisions in the feature. They live here, pure and separately
    /// tested, and <see cref="SkyWhaleCircuit"/> takes the result verbatim.
    ///
    /// WHAT A PLAYER GETS, because that is what the shape was chosen for. The
    /// route is a FLOWER: four petals, one per MapFile cell, joined by long
    /// open-sky crossings. Inside a petal the whale passes each island of that cell
    /// in turn, exactly as it did before - a bit under half an hour of "it is here,
    /// go and look" - and then it leaves, and that cell has no whale at all for the
    /// rest of the world lap. Seeing it is an event because it is somewhere else
    /// most of the time; it is findable because when it IS in your cell it works
    /// through your islands in order and the server says which one is next.
    ///
    /// THE ALTERNATIVE THAT WAS REJECTED, and why. The obvious "one giant world
    /// circuit" is to sort EVERY island in the world by bearing about the world's
    /// centroid and run a single ring through them. It is two lines shorter and it
    /// is much worse: the islands of one cell are then ordered by their bearing from
    /// the middle of the WORLD rather than from the middle of their own cell, so the
    /// path fans radially in and out of each cell instead of touring it, the whale
    /// crosses the same cell repeatedly on legs that read as random, and "it passes
    /// each island of the region in turn" - the one sentence the old design could
    /// tell a player - stops being true. Grouping by cell first keeps that sentence
    /// and buys the migration on top of it.
    ///
    /// FOUR RULES, each a total function of the island positions, because the whole
    /// feature rests on the route being re-derivable byte-identically after a
    /// restart:
    ///
    /// <list type="number">
    /// <item>ZONES IN ANGULAR ORDER about the world centroid, ties by region id.
    ///   The released cells are a 2x2 block, so each cell occupies roughly one
    ///   quadrant of bearings and this produces a four-cycle around the block in
    ///   which consecutive zones are always edge-adjacent, never diagonal. A tour
    ///   chosen by nearest-neighbour would be a greedy walk whose answer changes
    ///   discontinuously when one island moves - exactly the property a closed-form
    ///   path must not have - and cell-id order (a2, a3, b2, b3) would cross the
    ///   world diagonally between a3 and b2 for no reason at all.</item>
    /// <item>ISLANDS IN ANGULAR ORDER about their OWN zone's centroid, ties by
    ///   island id. Unchanged from the four-whale design and argued the same way:
    ///   <c>atan2</c> about the centroid gives a star-shaped, non-self-intersecting
    ///   tour for ANY scatter of islands, with no search and no tie-breaking
    ///   heuristics.</item>
    /// <item>EACH ZONE'S RING IS ROTATED so that the crossings either side of it
    ///   are as short as they can be - entry scored against the zone behind, exit
    ///   against the zone ahead. It is a ROTATION, never a re-sort, so the tour
    ///   inside the cell is exactly the angular ring rule 2 produced and only its
    ///   starting point moves. Scoring only the entry (the first version of this)
    ///   put the exit on the entry's angular neighbour - back on the side the whale
    ///   had come from - so it crossed the whole cell again to leave and HALF of a
    ///   world lap was spent over open sky. See <c>BestRotation</c>.</item>
    /// <item>THE CROSSINGS ARE RESAMPLED, and this one is a correctness
    ///   requirement rather than a preference. Uniform Catmull-Rom gives every
    ///   segment an EQUAL SLICE OF THE LAP whatever its length, so a 9 km crossing
    ///   sitting between 1.5 km island hops would be flown at six times the
    ///   whale's speed. Splitting each crossing into interior points spaced like
    ///   the zone-internal legs restores an even speed - and because collinear,
    ///   evenly spaced control points make uniform Catmull-Rom exactly linear, the
    ///   crossing is a dead-straight glide at constant speed with a continuous
    ///   tangent at both ends. It cost the curve nothing and the wire a few dozen
    ///   points.</item>
    /// </list>
    ///
    /// EVERYTHING HERE IS WAREBORN TUNING. Retail shipped no whale behaviour at
    /// all - see <see cref="SkyWhalePolicy"/> for what was recovered and what was
    /// not. Nothing in this file should be read as Bossa's design for how a sky
    /// whale migrates, because Bossa shipped none.
    /// </summary>
    public static class SkyWhaleRoute
    {
        /// <summary>
        /// THE ROUTE'S NAME, and why it names the CELLS the route covers.
        ///
        /// The map joins its published geometry to the live whale on this string,
        /// and that join has to be EXACT, because the route is a function of which
        /// cells the server rolled out. A world of four cells and a world of twenty
        /// produce completely different orders, lap times and phases; a single fixed
        /// name would let a map holding the twenty-cell route draw the animal on it
        /// while a four-cell server flew something else entirely - the whale in the
        /// wrong hemisphere, with the map insisting it was live.
        ///
        /// That failure could not happen while there were four whales: a circuit was
        /// one cell's ring and cells are selected whole, so the map's ring for a cell
        /// and the server's ring for the same cell were identical by construction.
        /// One world route destroys that property, and this name is what replaces it.
        /// A server whose cell set the map does not carry is drawn as NO whale and
        /// said so in words, which is the same degradation the map already applies to
        /// a game server that predates the feature.
        ///
        /// Cell ids, lower-cased and ordinally sorted, so the name is a pure function
        /// of the selection and readable in a log: <c>release-route-a2-a3-b2-b3</c>.
        /// </summary>
        public static string RouteIdFor(IEnumerable<string> cellIds)
        {
            if (cellIds == null) throw new ArgumentNullException(nameof(cellIds));
            IEnumerable<string> cells = cellIds
                .Where(cell => !string.IsNullOrWhiteSpace(cell))
                .Select(cell => cell.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(cell => cell, StringComparer.Ordinal);
            string joined = string.Join("-", cells);
            return joined.Length == 0 ? "release-route-empty" : "release-route-" + joined;
        }

        /// <summary>
        /// The whole world's route, in travel order, or an empty list when there is
        /// nothing to fly. Pure and total; see the type remarks for the four rules.
        /// </summary>
        public static IReadOnlyList<SkyWhaleWaypoint> Build(IEnumerable<SkyWhaleZone> zones)
        {
            if (zones == null) throw new ArgumentNullException(nameof(zones));

            List<(RegionId Region, SkyWhaleWaypoint[] Ring, double X, double Z)> rings =
                new List<(RegionId, SkyWhaleWaypoint[], double, double)>();
            foreach (SkyWhaleZone zone in zones)
            {
                SkyWhaleWaypoint[] ring = OrderAroundCentroid(
                    zone.Islands ?? Array.Empty<SkyWhaleWaypoint>());
                if (ring.Length == 0) continue;
                (double centreX, double centreZ) = LateralCentroid(ring);
                rings.Add((zone.Region, ring, centreX, centreZ));
            }
            if (rings.Count == 0) return Array.Empty<SkyWhaleWaypoint>();

            // RULE 1: the zones themselves, in angular order about the world.
            double worldX = 0.0, worldZ = 0.0;
            foreach (var entry in rings)
            {
                worldX += entry.X;
                worldZ += entry.Z;
            }
            worldX /= rings.Count;
            worldZ /= rings.Count;
            rings = rings
                .OrderBy(entry => Math.Atan2(entry.Z - worldZ, entry.X - worldX))
                .ThenBy(entry => entry.Region)
                .ToList();

            // RULE 3: rotate each zone's ring to the cheapest way in and out of it.
            List<SkyWhaleWaypoint> route = new List<SkyWhaleWaypoint>();
            for (int i = 0; i < rings.Count; i++)
            {
                var zone = rings[i];
                var previous = rings[((i - 1) % rings.Count + rings.Count) % rings.Count];
                var next = rings[(i + 1) % rings.Count];

                int entry = BestRotation(zone.Ring,
                    previous.X, previous.Z, next.X, next.Z);
                for (int step = 0; step < zone.Ring.Length; step++)
                {
                    SkyWhaleWaypoint waypoint = zone.Ring[(entry + step) % zone.Ring.Length];
                    route.Add(waypoint with { Region = zone.Region, IsTransit = false });
                }
            }

            // RULE 4: resample the crossings between zones.
            return Resample(route);
        }

        /// <summary>
        /// The ring order inside one zone: bearing about the lateral centroid, ties
        /// broken by island id so the result cannot depend on the order the caller
        /// enumerated in.
        ///
        /// The centroid is the mean of the waypoints rather than of the islands'
        /// AABBs on purpose - the waypoints ARE what is being ordered, so ordering
        /// them about anything else could put a waypoint on the wrong side of the
        /// ring. Y is ignored: this is a lateral tour that rises and falls with the
        /// islands, not a three-dimensional one.
        /// </summary>
        public static SkyWhaleWaypoint[] OrderAroundCentroid(
            IEnumerable<SkyWhaleWaypoint> waypoints)
        {
            if (waypoints == null) throw new ArgumentNullException(nameof(waypoints));
            List<SkyWhaleWaypoint> all = new List<SkyWhaleWaypoint>(waypoints);
            if (all.Count == 0) return Array.Empty<SkyWhaleWaypoint>();

            (double centreX, double centreZ) = LateralCentroid(all);
            return all
                .OrderBy(waypoint => Math.Atan2(waypoint.Z - centreZ, waypoint.X - centreX))
                .ThenBy(waypoint => waypoint.IslandId)
                .ToArray();
        }

        /// <summary>The mean X and Z of a set of waypoints.</summary>
        private static (double X, double Z) LateralCentroid(
            IReadOnlyList<SkyWhaleWaypoint> waypoints)
        {
            double x = 0.0, z = 0.0;
            foreach (SkyWhaleWaypoint waypoint in waypoints)
            {
                x += waypoint.X;
                z += waypoint.Z;
            }
            return (x / waypoints.Count, z / waypoints.Count);
        }

        /// <summary>
        /// WHERE THE TOUR OF ONE ZONE STARTS: the rotation of its ring that makes
        /// the two crossings either side of it as short as possible.
        ///
        /// A ROTATION, NOT A RE-SORT, so the tour inside the zone - the thing a
        /// player of that cell experiences - is exactly the angular ring rule 2
        /// produced; only its starting point moves.
        ///
        /// WHY BOTH ENDS ARE SCORED, which is the correction that made the
        /// migration worth having. Rotating only to the island nearest the PREVIOUS
        /// zone puts the exit on the angular neighbour of the entry - also on the
        /// previous zone's side - so the whale had to cross the whole cell again to
        /// leave it, and half of a lap was spent over open sky. Scoring the entry
        /// against the zone behind AND the exit against the zone ahead picks the
        /// corner of the ring between the two neighbours instead, and cuts the
        /// crossings to roughly the gap between the cells.
        ///
        /// It stays a total function of the geometry: N rotations, each scored by
        /// two lateral distances, ties broken by the entry island's id so two
        /// equally good rotations cannot swap between boots. Both scores are taken
        /// against the neighbouring zones' CENTROIDS rather than against their
        /// chosen entry or exit waypoints, which is what keeps this a function
        /// rather than a recurrence - no zone has to be solved before another one
        /// can be.
        /// </summary>
        private static int BestRotation(SkyWhaleWaypoint[] ring,
            double previousX, double previousZ, double nextX, double nextZ)
        {
            int best = 0;
            double bestCost = double.MaxValue;
            for (int i = 0; i < ring.Length; i++)
            {
                SkyWhaleWaypoint entry = ring[i];
                SkyWhaleWaypoint exit = ring[((i - 1) % ring.Length + ring.Length) % ring.Length];
                double cost = Lateral(entry, previousX, previousZ)
                    + Lateral(exit, nextX, nextZ);
                if (cost < bestCost
                    || (cost == bestCost
                        && entry.IslandId.CompareTo(ring[best].IslandId) < 0))
                {
                    best = i;
                    bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>
        /// The lateral distance from a waypoint to a point - a real distance rather
        /// than a squared one, because <see cref="BestRotation"/> ADDS two of them
        /// and squared metres do not add to anything meaningful.
        /// </summary>
        private static double Lateral(SkyWhaleWaypoint waypoint, double x, double z)
        {
            double dx = waypoint.X - x;
            double dz = waypoint.Z - z;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>
        /// Splits every ZONE-TO-ZONE leg into interior points spaced like the legs
        /// inside a zone, so uniform Catmull-Rom does not fly the crossing at six
        /// times the whale's speed. See rule 4 in the type remarks.
        ///
        /// THE SPACING IS THE MEDIAN ZONE-INTERNAL LEG, not a constant, and that is
        /// the same choice <see cref="SkyWhaleCircuit.CircuitSeconds"/> makes: a
        /// literal metre count would be right for the release catalogue and wrong
        /// for any other world, and the thing being matched is precisely "what the
        /// segments either side of the crossing look like". The MEDIAN rather than
        /// the mean because one unusually isolated island would drag a mean.
        ///
        /// Bounded by <see cref="SkyWhalePolicy.MaxTransitPointsPerLeg"/> so a
        /// pathological world - one island in a cell on the far side of the map -
        /// cannot turn the published route into a megabyte.
        /// </summary>
        private static IReadOnlyList<SkyWhaleWaypoint> Resample(
            IReadOnlyList<SkyWhaleWaypoint> route)
        {
            int n = route.Count;
            List<double> internalLegs = new List<double>();
            for (int i = 0; i < n; i++)
            {
                SkyWhaleWaypoint from = route[i];
                SkyWhaleWaypoint to = route[(i + 1) % n];
                if (from.Region == to.Region) internalLegs.Add(Distance(from, to));
            }
            internalLegs.Sort();
            double spacing = internalLegs.Count == 0
                ? 0.0 : internalLegs[internalLegs.Count / 2];

            List<SkyWhaleWaypoint> resampled = new List<SkyWhaleWaypoint>(n);
            for (int i = 0; i < n; i++)
            {
                SkyWhaleWaypoint from = route[i];
                SkyWhaleWaypoint to = route[(i + 1) % n];
                resampled.Add(from);
                if (from.Region == to.Region || spacing <= 0.0) continue;

                double length = Distance(from, to);
                int pieces = (int)Math.Round(length / spacing);
                if (pieces > SkyWhalePolicy.MaxTransitPointsPerLeg + 1)
                {
                    pieces = SkyWhalePolicy.MaxTransitPointsPerLeg + 1;
                }
                for (int piece = 1; piece < pieces; piece++)
                {
                    double t = (double)piece / pieces;
                    // ANCHORED TO THE NEARER END, so the island-local offset the map
                    // is published in is always the smaller of the two - see the
                    // remarks on SkyWhaleWaypoint.
                    SkyWhaleWaypoint anchor = t <= 0.5 ? from : to;
                    resampled.Add(new SkyWhaleWaypoint(
                        anchor.IslandId,
                        from.X + ((to.X - from.X) * t),
                        from.Y + ((to.Y - from.Y) * t),
                        from.Z + ((to.Z - from.Z) * t))
                    {
                        Region = anchor.Region,
                        IsTransit = true,
                    });
                }
            }
            return resampled;
        }

        private static double Distance(SkyWhaleWaypoint from, SkyWhaleWaypoint to)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double dz = to.Z - from.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }
}
