using System;
using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Pure wire policy for client-only distant island shells. The request is
    /// carried in AssetType. V1 lets the retail loader cache the named island
    /// bundle; v2 carries a complete compact outline and is intercepted before
    /// bundle loading.
    ///
    /// Keep this file net35/C# 7.3 compatible: the client mod links this exact
    /// source, while the native tests compile it in the multiplayer assembly.
    /// </summary>
    public static class IslandDistantShellProtocol
    {
        public const string EnabledEnvVar = "WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED";
        public const string RequestPrefix = "wareborn.island-shell.v1";
        public const string ProceduralRequestPrefix = "wareborn.island-shell.v2";
        public const string ReadyPrefix = "wareborn.island-shell-ready.v1";

        public static bool EnabledFrom(string value)
        {
            if (value == null || value.Trim().Length == 0) return false;
            value = value.Trim();
            return value == "1"
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        public static string Request(string islandId, long entityId,
            long x, long y, long z)
        {
            return Encode(RequestPrefix, islandId, entityId, x, y, z);
        }

        public static string Ready(string islandId, long entityId,
            long x, long y, long z)
        {
            return Encode(ReadyPrefix, islandId, entityId, x, y, z);
        }

        /// <summary>Encodes a compact radial outline; no island bundle is required.</summary>
        public static string ProceduralRequest(string islandId, long entityId,
            long x, long y, long z, double minY, double maxY,
            System.Collections.Generic.IList<IslandShellPoint> outline)
        {
            if (outline == null || outline.Count < 3 || outline.Count > 32)
                throw new ArgumentException("shell outline must contain 3..32 points", "outline");
            string value = Encode(ProceduralRequestPrefix, islandId, entityId, x, y, z)
                + "|" + minY.ToString("0.0", CultureInfo.InvariantCulture)
                + "|" + maxY.ToString("0.0", CultureInfo.InvariantCulture) + "|";
            for (int i = 0; i < outline.Count; i++)
            {
                if (i > 0) value += ";";
                value += outline[i].X.ToString("0.0", CultureInfo.InvariantCulture) + ","
                    + outline[i].Z.ToString("0.0", CultureInfo.InvariantCulture);
            }
            return value;
        }

        public static bool TryParseRequest(string value, out IslandDistantShellSpec spec)
        {
            return TryParse(value, RequestPrefix, out spec);
        }

        public static bool TryParseReady(string value, out IslandDistantShellSpec spec)
        {
            return TryParse(value, ReadyPrefix, out spec);
        }

        public static bool TryParseProceduralRequest(string value,
            out IslandDistantShellSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('|');
            if (parts.Length != 9) return false;
            IslandDistantShellSpec baseSpec;
            if (!TryParse(string.Join("|", parts, 0, 6), ProceduralRequestPrefix,
                    out baseSpec)) return false;
            double minY, maxY;
            if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out minY)
                || !double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out maxY)
                || maxY <= minY) return false;
            string[] encodedPoints = parts[8].Split(';');
            if (encodedPoints.Length < 3 || encodedPoints.Length > 32) return false;
            IslandShellPoint[] outline = new IslandShellPoint[encodedPoints.Length];
            for (int i = 0; i < encodedPoints.Length; i++)
            {
                string[] coordinates = encodedPoints[i].Split(',');
                double px, pz;
                if (coordinates.Length != 2
                    || !double.TryParse(coordinates[0], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out px)
                    || !double.TryParse(coordinates[1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out pz)) return false;
                outline[i] = new IslandShellPoint(px, pz);
            }
            spec = new IslandDistantShellSpec(baseSpec.IslandId, baseSpec.EntityId,
                baseSpec.X, baseSpec.Y, baseSpec.Z, minY, maxY, outline);
            return true;
        }

        private static string Encode(string prefix, string islandId, long entityId,
            long x, long y, long z)
        {
            if (string.IsNullOrEmpty(islandId) || islandId.IndexOf('|') >= 0)
                throw new ArgumentException("island id cannot contain the shell delimiter", "islandId");
            return prefix + "|" + islandId + "|"
                + entityId.ToString(CultureInfo.InvariantCulture) + "|"
                + x.ToString(CultureInfo.InvariantCulture) + "|"
                + y.ToString(CultureInfo.InvariantCulture) + "|"
                + z.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParse(string value, string prefix,
            out IslandDistantShellSpec spec)
        {
            spec = null;
            if (string.IsNullOrEmpty(value)) return false;
            string[] parts = value.Split('|');
            long entityId;
            long x;
            long y;
            long z;
            if (parts.Length != 6 || parts[0] != prefix
                || string.IsNullOrEmpty(parts[1])
                || !long.TryParse(parts[2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out entityId)
                || entityId <= 0
                || !long.TryParse(parts[3], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out x)
                || !long.TryParse(parts[4], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out y)
                || !long.TryParse(parts[5], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out z))
                return false;

            spec = new IslandDistantShellSpec(parts[1], entityId, x, y, z);
            return true;
        }
    }

    public sealed class IslandShellPoint
    {
        public IslandShellPoint(double x, double z) { X = x; Z = z; }
        public double X { get; private set; }
        public double Z { get; private set; }
    }

    public sealed class IslandDistantShellSpec
    {
        public IslandDistantShellSpec(string islandId, long entityId,
            long x, long y, long z)
            : this(islandId, entityId, x, y, z, 0, 0, null)
        {
        }

        public IslandDistantShellSpec(string islandId, long entityId,
            long x, long y, long z, double minY, double maxY,
            IslandShellPoint[] outline)
        {
            IslandId = islandId;
            EntityId = entityId;
            X = x;
            Y = y;
            Z = z;
            MinY = minY;
            MaxY = maxY;
            Outline = outline;
        }

        public string IslandId { get; private set; }
        public long EntityId { get; private set; }
        public long X { get; private set; }
        public long Y { get; private set; }
        public long Z { get; private set; }
        public double MinY { get; private set; }
        public double MaxY { get; private set; }
        public IslandShellPoint[] Outline { get; private set; }
    }
}
