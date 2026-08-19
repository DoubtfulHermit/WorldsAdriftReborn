using System.Globalization;
using System.Reflection;

using WorldsAdriftRebornGameServer.Multiplayer.Resources;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE GROUND UNDER EVERY TREE IN THE WORLD, answered from a world position and
    /// nothing else.
    ///
    /// WHY THE KEY IS A POSITION AND NOT A REGISTRATION KEY, which was the obvious
    /// first design. A felled log is grounded inside <see cref="FallingLogs.Drop"/>,
    /// and <c>Drop</c> is already given the tree's world position - it has to be,
    /// because that is where the log appears. Resolving the profile from that same
    /// argument means grounding has NO new input to be wired up, and therefore no
    /// new line of glue in <c>FallingLogService</c> that could be omitted while
    /// every unit test carried on passing. That failure mode is not hypothetical
    /// here: the tree-fall feature before this one shipped invisible for exactly
    /// that reason, because its tests stopped at the pure registry and the one
    /// predicate the service actually evaluated was wrong. A position is also
    /// SELF-CHECKING in a way a key is not - a wrong position puts the log
    /// somewhere the player did not cut, which is impossible to miss.
    ///
    /// TWO SOURCES, ONE ANSWER:
    /// <list type="number">
    /// <item>Islands whose whole extracted surface is embedded in this assembly -
    ///   Haven and the Trades Challenge - are measured LIVE through
    ///   <see cref="LogGrounding.FromSamples"/>. That covers any point on them, not
    ///   just authored seats, and it matters because Haven is the spawn island and
    ///   its eighty trees are generated at boot rather than baked, so there is no
    ///   row to look up.</item>
    /// <item>Every other island uses the baked table, keyed by the nearest authored
    ///   seat. Seats are fifteen metres apart at minimum and a tree stands exactly
    ///   on one, so "nearest within a metre" is an identity lookup rather than an
    ///   approximation, and a position that is NOT a tree seat correctly gets no
    ///   answer.</item>
    /// </list>
    /// The two agree by construction, and a test proves it rather than trusting it:
    /// the Trades Challenge has BOTH an embedded surface and baked rows, so the
    /// generator and this assembly can be held against each other on real data.
    /// Without that gate the baked file would be 332 KB nobody ever checks.
    ///
    /// NULL IS A GOOD ANSWER. A position off any island, on an island whose surface
    /// was never extracted, or not on a seat, has no measured ground, and
    /// <see cref="LogGrounding.Rest"/> degrades that to the flat topple this server
    /// has always done. Grounding may improve a log; it may never be the reason one
    /// fails to appear.
    /// </summary>
    public static class TreeGroundProfiles
    {
        private const string ResourceSuffix = "release-tree-ground-profiles.txt";

        /// <summary>
        /// How close a position must be to an authored seat, in metres, to be
        /// treated as standing on it. One metre: the round trip through
        /// <see cref="IslandDefinition.LocalToGlobal"/> loses at most a quarter of a
        /// millimetre per axis, and the nearest other seat is fifteen metres away,
        /// so anything in between is not a tree.
        /// </summary>
        public const double SeatToleranceMetres = 1.0;

        private static readonly IReadOnlyDictionary<string, sbyte[][]> Baked = Load();

        /// <summary>
        /// Memoised live measurements. CONCURRENT although the game server is a
        /// single poll loop, because this type is also reached from a test suite
        /// that runs classes in parallel, and a torn Dictionary there would surface
        /// as an unrelated flake in whichever test happened to lose the race.
        /// </summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, GroundProfile>
            LiveCache = new(StringComparer.Ordinal);

        /// <summary>How many islands carry baked profiles. For the coverage tests.</summary>
        public static int BakedIslandCount => Baked.Count;

        /// <summary>How many seats carry baked profiles across the whole world.</summary>
        public static int BakedSeatCount => Baked.Values.Sum(seats => seats.Length);

        /// <summary>
        /// The baked profile for one seat, or null if that island or seat has none.
        /// The raw table lookup, exposed for the coverage and agreement tests.
        /// </summary>
        public static GroundProfile? BakedFor(string? workshopId, int seatIndex)
        {
            if (workshopId == null
                || !Baked.TryGetValue(workshopId, out sbyte[][]? seats)
                || seatIndex < 0
                || seatIndex >= seats.Length)
            {
                return null;
            }

            return new GroundProfile(seats[seatIndex]);
        }

        /// <summary>
        /// WHAT THE GROUND DOES AROUND A WORLD POSITION, or null when nothing was
        /// ever measured there. The entry point <see cref="FallingLogs"/> uses.
        /// </summary>
        public static GroundProfile? For(FixedPointPosition worldPosition)
        {
            ReleaseIslandRecord? island = IslandAt(worldPosition);
            if (island == null)
            {
                return HavenProfile(worldPosition);
            }

            IslandDefinition definition = island.Definition;
            double localX = (double)(worldPosition.X - definition.GlobalOrigin.X)
                / FixedPointPosition.UnitsPerMetre;
            double localY = (double)(worldPosition.Y - definition.GlobalOrigin.Y)
                / FixedPointPosition.UnitsPerMetre;
            double localZ = (double)(worldPosition.Z - definition.GlobalOrigin.Z)
                / FixedPointPosition.UnitsPerMetre;

            string workshopId = island.Survey.WorkshopId;

            IReadOnlyList<SurfaceSample>? live = EmbeddedSurfaceFor(workshopId);
            if (live != null)
            {
                return Measure(workshopId, live, localX, localY, localZ);
            }

            int seat = NearestSeat(workshopId, localX, localZ);
            return seat < 0 ? null : BakedFor(workshopId, seat);
        }

        /// <summary>
        /// Haven is not a release-catalogue island, so it is asked for separately -
        /// and it is the one that matters most, because it is where players spawn
        /// and where the first tree anybody fells stands.
        /// </summary>
        private static GroundProfile? HavenProfile(FixedPointPosition worldPosition)
        {
            IslandDefinition haven = IslandCatalog.Haven;
            double localX = (double)(worldPosition.X - haven.GlobalOrigin.X)
                / FixedPointPosition.UnitsPerMetre;
            double localY = (double)(worldPosition.Y - haven.GlobalOrigin.Y)
                / FixedPointPosition.UnitsPerMetre;
            double localZ = (double)(worldPosition.Z - haven.GlobalOrigin.Z)
                / FixedPointPosition.UnitsPerMetre;

            // Haven's extracted surface spans roughly -330..500 m in X and Z; a
            // generous box keeps a position that is nowhere near it from being
            // measured against samples hundreds of metres away.
            if (Math.Abs(localX) > 800.0 || Math.Abs(localZ) > 800.0 || Math.Abs(localY) > 400.0)
            {
                return null;
            }

            return Measure(HavenSurface.WorkshopId, HavenSurface.Samples, localX, localY, localZ);
        }

        /// <summary>
        /// Measures a profile from an island's own embedded surface, memoised on the
        /// position rounded to a decimetre.
        ///
        /// The cache exists because a player takes ONE trunk apart over many cuts and
        /// every piece asks the same question about the same seat; without it each
        /// cut would rescan two thousand samples eight times. It is bounded by the
        /// number of distinct seats on two islands, so it cannot grow without bound
        /// the way a per-log cache could.
        /// </summary>
        private static GroundProfile Measure(string workshopId, IReadOnlyList<SurfaceSample> samples,
            double localX, double localY, double localZ)
        {
            string key = workshopId + "|"
                + Math.Round(localX, 1).ToString("0.0", CultureInfo.InvariantCulture) + "|"
                + Math.Round(localY, 1).ToString("0.0", CultureInfo.InvariantCulture) + "|"
                + Math.Round(localZ, 1).ToString("0.0", CultureInfo.InvariantCulture);

            if (LiveCache.TryGetValue(key, out GroundProfile cached))
            {
                return cached;
            }

            List<(double X, double Y, double Z)> points = new(samples.Count);
            foreach (SurfaceSample sample in samples)
            {
                points.Add((sample.LocalX, sample.LocalY, sample.LocalZ));
            }

            GroundProfile profile = LogGrounding.FromSamples(localX, localY, localZ, points);
            LiveCache[key] = profile;
            return profile;
        }

        /// <summary>
        /// The islands whose whole extracted surface travels inside this assembly.
        /// Adding a third is a csproj line and a case here; nothing else changes.
        /// </summary>
        public static IReadOnlyList<SurfaceSample>? EmbeddedSurfaceFor(string workshopId)
        {
            if (string.Equals(workshopId, HavenSurface.WorkshopId, StringComparison.Ordinal))
            {
                return HavenSurface.Samples;
            }
            if (string.Equals(workshopId, TradesChallengeResources.WorkshopId, StringComparison.Ordinal))
            {
                return TradesChallengeResources.Samples;
            }
            return null;
        }

        /// <summary>
        /// The index of the authored seat a position is standing on, or -1.
        /// Linear over one island's sixty seats, which is cheaper than the hash a
        /// spatial index would need and is called once per felled section.
        /// </summary>
        public static int NearestSeat(string workshopId, double localX, double localZ)
        {
            ReleaseTreeIsland? island = ReleaseTreeCatalog.ForWorkshopId(workshopId);
            if (island == null)
            {
                return -1;
            }

            double bestSquared = SeatToleranceMetres * SeatToleranceMetres;
            int best = -1;

            for (int i = 0; i < island.Points.Count; i++)
            {
                (double x, _, double z) = island.Points[i];
                double dx = x - localX;
                double dz = z - localZ;
                double squared = (dx * dx) + (dz * dz);
                if (squared <= bestSquared)
                {
                    bestSquared = squared;
                    best = i;
                }
            }

            return best;
        }

        private static ReleaseIslandRecord? IslandAt(FixedPointPosition worldPosition)
        {
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                if (record.Envelope.Contains(worldPosition, record.Definition))
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>
        /// Reads the baked table. Format is documented in the file's own header and
        /// deliberately trivial: '#' comments, '@ workshopId seatCount' starts an
        /// island, then one line of eight signed decimetre integers per seat, in
        /// release-tree-placements.json order.
        ///
        /// A MALFORMED LINE THROWS rather than being skipped. This file is generated
        /// and embedded, so a bad line is a build-time mistake, and a server that
        /// boots with half a table would ground half the world and look like a
        /// terrain bug for as long as it took anyone to suspect the data.
        /// </summary>
        private static IReadOnlyDictionary<string, sbyte[][]> Load()
        {
            Assembly assembly = typeof(TreeGroundProfiles).Assembly;
            string resource = assembly.GetManifestResourceNames().Single(name =>
                name.EndsWith(ResourceSuffix, StringComparison.Ordinal));

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);

            Dictionary<string, sbyte[][]> islands = new(StringComparer.Ordinal);
            string? workshopId = null;
            List<sbyte[]>? seats = null;
            int expected = 0;

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                if (trimmed[0] == '@')
                {
                    Commit(islands, workshopId, seats, expected);

                    string[] header = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (header.Length != 3)
                    {
                        throw new InvalidDataException(
                            "a ground-profile island header is '@ <workshopId> <seatCount>', not: " + trimmed);
                    }

                    workshopId = header[1];
                    expected = int.Parse(header[2], CultureInfo.InvariantCulture);
                    seats = new List<sbyte[]>(expected);
                    continue;
                }

                if (seats == null)
                {
                    throw new InvalidDataException(
                        "a ground-profile seat line appeared before any '@' island header");
                }

                string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != GroundProfile.Bearings)
                {
                    throw new InvalidDataException(
                        "a ground-profile seat carries exactly " + GroundProfile.Bearings
                        + " bearings, not " + parts.Length);
                }

                sbyte[] rises = new sbyte[GroundProfile.Bearings];
                for (int i = 0; i < GroundProfile.Bearings; i++)
                {
                    rises[i] = sbyte.Parse(parts[i], CultureInfo.InvariantCulture);
                }
                seats.Add(rises);
            }

            Commit(islands, workshopId, seats, expected);
            return islands;
        }

        private static void Commit(Dictionary<string, sbyte[][]> islands, string? workshopId,
            List<sbyte[]>? seats, int expected)
        {
            if (workshopId == null || seats == null)
            {
                return;
            }
            if (seats.Count != expected)
            {
                throw new InvalidDataException("island " + workshopId + " declared " + expected
                    + " ground-profile seats but carried " + seats.Count);
            }

            islands[workshopId] = seats.ToArray();
        }
    }
}
