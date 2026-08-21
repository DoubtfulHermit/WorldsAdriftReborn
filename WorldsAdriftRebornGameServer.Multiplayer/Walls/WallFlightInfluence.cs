using System.Globalization;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// The release walls projected into the recovered flight-side distance field.
    /// Geometry, direction, the 200/400 m falloff and the mass attenuation are
    /// recovered client behaviour. Strength is not: retail supplied five scalars in
    /// 1229 GlobalWallDataState and no copy survives. Consequently every magnitude
    /// here is an explicit operator value and defaults to zero. Zero produces no
    /// mechanical wall segments rather than silently turning the wall core into calm.
    /// </summary>
    public sealed class WallFlightInfluence
    {
        public const string WindRiftEnvVar = "WAREBORN_WALL_WIND_RIFT_MPS";
        public const string StormRiftEnvVar = "WAREBORN_WALL_STORM_RIFT_MPS";
        public const string SandStormEnvVar = "WAREBORN_WALL_SANDSTORM_MPS";
        public const string WorldEndEnvVar = "WAREBORN_WALL_WORLD_END_MPS";

        private WallFlightInfluence(IReadOnlyList<WeatherWallSegment> segments)
        {
            Segments = segments;
        }

        public IReadOnlyList<WeatherWallSegment> Segments { get; }

        public bool IsEnabled => Segments.Count != 0;

        public static WallFlightInfluence FromEnvironment(bool wallsEnabled,
            Func<string, string?> getenv)
        {
            if (!wallsEnabled)
            {
                return new WallFlightInfluence(Array.Empty<WeatherWallSegment>());
            }

            var speeds = new Dictionary<WallType, double>
            {
                [WallType.WindRift] = ParseSpeed(getenv(WindRiftEnvVar)),
                [WallType.StormRift] = ParseSpeed(getenv(StormRiftEnvVar)),
                [WallType.SandStorm] = ParseSpeed(getenv(SandStormEnvVar)),
                [WallType.WorldEndWall] = ParseSpeed(getenv(WorldEndEnvVar)),
            };
            IReadOnlyCollection<WallType> servedTypes =
                WallPolicy.SelectedTypes(getenv(WallPolicy.TypesEnvVar));
            List<WeatherWallSegment> segments = new();
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                if (!servedTypes.Contains(wall.Type)
                    || !speeds.TryGetValue(wall.Type, out double speed) || speed <= 0.0)
                {
                    continue;
                }

                double dx = wall.OrientationX * wall.HalfLength;
                double dz = wall.OrientationZ * wall.HalfLength;
                segments.Add(new WeatherWallSegment(
                    wall.Midpoint.MetresX - dx,
                    wall.Midpoint.MetresZ - dz,
                    wall.Midpoint.MetresX + dx,
                    wall.Midpoint.MetresZ + dz,
                    (WeatherWallType)(int)wall.Type,
                    speed));
            }
            return new WallFlightInfluence(segments);
        }

        public string Describe()
        {
            if (!IsEnabled)
            {
                return "wall flight influence: OFF (retail 1229 strengths are unrecovered; set "
                    + WindRiftEnvVar + " and/or the other per-type wall speed knobs explicitly)";
            }
            return "wall flight influence: ON for " + Segments.Count
                + " release segment(s); recovered +/-400 m force band, +/-200 m full-strength core";
        }

        private static double ParseSpeed(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out double parsed)
                || !double.IsFinite(parsed) || parsed <= 0.0)
            {
                return 0.0;
            }
            // The shipped client's GlobalWeather.GetWindAt rejects winds above 100
            // m/s. Clamp at the same boundary so the server cannot feel a wind the
            // client would turn into calm.
            return Math.Min(parsed, WindSample.MaxSpeedMps);
        }
    }
}
