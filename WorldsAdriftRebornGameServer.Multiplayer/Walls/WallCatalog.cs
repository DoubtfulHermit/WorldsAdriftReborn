using System.Reflection;
using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// THE 44 WEATHER WALLS OF THE RELEASE MAP, turned from endpoint pairs into the
    /// midpoint/direction/half-length form <c>1204 WallSegmentState</c> wants.
    ///
    /// The data is the release MapFile's own <c>Walls</c> table, projected into an
    /// embedded <c>release-wall-segments.json</c> by
    /// <c>tools/world-import/generate-release-wall-segments.py</c>. That table is the
    /// COMPLETE retail payload for a wall: <c>WorldEditorWallData.WallStoreData</c>
    /// carried exactly {x1, z1, x2, z2, Type} and no tuning at all
    /// (findings-storm-walls.md section 9.3), which is also why there is no
    /// server-side wall tuning anywhere in this feature.
    ///
    /// THE ARITHMETIC IS HERE, NOT IN THE GENERATOR, deliberately. Half-length and
    /// direction are the two things a wall feature gets wrong, and a wrong number
    /// baked into a generated file is a number nobody re-derives. In C# it is pinned
    /// by <c>WallCatalogTests</c> against hand-worked values.
    ///
    /// COORDINATE FRAME. The MapFile's x/z ARE this server's world metres, with no
    /// offset: <c>ReleaseWorldCatalog</c> feeds the very same wamap x/y/z straight
    /// into <c>FixedPointPosition.FromMetres</c> to place all 254 islands
    /// (ReleaseWorldCatalog.cs:119-122). A wall and the islands it separates are
    /// therefore in the same frame by construction, not by a conversion that could
    /// be off.
    /// </summary>
    public static class WallCatalog
    {
        private const string ResourceSuffix = "release-wall-segments.json";

        /// <summary>
        /// The Y every wall's transform is seeded at, in metres.
        ///
        /// WAREBORN DECISION, and a free one: the source geometry is 2D and NOTHING
        /// in the client reads a wall's Y. Every wall distance is XZ-only, the
        /// renderer's ceiling is a shader constant, and an ambient bolt randomises
        /// its own height over [-1000, 800] independently of the segment
        /// (WallSegmentSeed remarks cite the lines). Zero is chosen because it is the
        /// world datum the island field is centred on, so a wall drawn on the
        /// operator map and a wall in the world are trivially comparable.
        /// </summary>
        public const double WallYMetres = 0.0;

        private static readonly IReadOnlyList<WallSegmentSeed> Loaded = Load();

        /// <summary>Every wall in the release map, ordered by <see cref="WallSegmentSeed.WallId"/>.</summary>
        public static IReadOnlyList<WallSegmentSeed> All => Loaded;

        /// <summary>The wall with this id, or null. Ids are the source array's indices.</summary>
        public static WallSegmentSeed? ById(int wallId)
        {
            foreach (WallSegmentSeed wall in Loaded)
            {
                if (wall.WallId == wallId)
                {
                    return wall;
                }
            }
            return null;
        }

        /// <summary>Every wall of one kind, ordered by id.</summary>
        public static IReadOnlyList<WallSegmentSeed> OfType(WallType type)
        {
            List<WallSegmentSeed> matches = new();
            foreach (WallSegmentSeed wall in Loaded)
            {
                if (wall.Type == type)
                {
                    matches.Add(wall);
                }
            }
            return matches;
        }

        /// <summary>
        /// Total length in metres of the storm rifts in <paramref name="walls"/> - the
        /// ONE number that decides what serving walls costs a client's frame budget.
        ///
        /// <c>WeatherWalls.EvaluateLength</c> sums every REGISTERED storm rift into
        /// <c>TotalStormWallLength</c>, and <c>LightningVisualInstancesManager</c>
        /// spawns ambient bolts at
        /// <c>_fakeLightningPerSecondPerKilometer * TotalStormWallLength / 1000</c>
        /// per second - world-wide, before any frustum culling, whether or not a
        /// single rift is anywhere near the player. Serving all 11 rifts pins that at
        /// ~53 km permanently for everyone. This function exists so the number can be
        /// printed in the boot banner and asserted in a test rather than discovered
        /// on someone's GPU.
        /// </summary>
        public static double StormWallLengthMetres(IEnumerable<WallSegmentSeed> walls)
        {
            double total = 0.0;
            foreach (WallSegmentSeed wall in walls)
            {
                if (wall.Type == WallType.StormRift)
                {
                    total += wall.LengthMetres;
                }
            }
            return total;
        }

        private static IReadOnlyList<WallSegmentSeed> Load()
        {
            Assembly assembly = typeof(WallCatalog).Assembly;
            string? resource = null;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                {
                    resource = name;
                    break;
                }
            }

            if (resource == null)
            {
                // FAIL EMPTY, not throw. A packaging mistake must cost the world its
                // walls, not its boot: this assembly's static initialisers run on the
                // spawn-plan path. An empty catalogue registers no wall entities,
                // which is exactly the feature-off state. WallCatalogTests asserts the
                // resource is really there, so an empty list cannot ship.
                return Array.Empty<WallSegmentSeed>();
            }

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using JsonDocument document = JsonDocument.Parse(stream);
            List<WallSegmentSeed> walls = new();
            foreach (JsonElement item in document.RootElement.GetProperty("walls").EnumerateArray())
            {
                walls.Add(SeedFrom(
                    item.GetProperty("id").GetInt32(),
                    item.GetProperty("type").GetInt32(),
                    item.GetProperty("x1").GetDouble(),
                    item.GetProperty("z1").GetDouble(),
                    item.GetProperty("x2").GetDouble(),
                    item.GetProperty("z2").GetDouble()));
            }
            walls.Sort((a, b) => a.WallId.CompareTo(b.WallId));
            return walls;
        }

        /// <summary>
        /// The endpoint-pair -> wire-form conversion, exposed so it can be tested
        /// against hand-worked numbers without going through the embedded file.
        /// </summary>
        public static WallSegmentSeed SeedFrom(int wallId, int type, double x1, double z1, double x2, double z2)
        {
            double dx = x2 - x1;
            double dz = z2 - z1;
            double length = Math.Sqrt((dx * dx) + (dz * dz));
            if (length <= 0.0)
            {
                throw new ArgumentException(
                    "wall " + wallId + " is degenerate: its endpoints coincide, so it has no direction and "
                    + "WallData.Forward would be NaN on the client", nameof(x2));
            }

            return new WallSegmentSeed(
                wallId,
                (WallType)type,
                FixedPointPosition.FromMetres((x1 + x2) / 2.0, WallYMetres, (z1 + z2) / 2.0),
                dx / length,
                0.0,
                dz / length,
                // HALF-length. See WallSegmentSeed's remarks - this single factor of
                // two is the difference between a wall and a wall twice as long.
                (float)(length / 2.0));
        }
    }
}
