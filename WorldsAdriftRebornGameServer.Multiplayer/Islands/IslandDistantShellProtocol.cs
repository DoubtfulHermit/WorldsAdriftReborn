using System;
using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Pure wire policy for client-only distant island shells. The request is
    /// carried in AssetType so the retail asset loader still caches the normal
    /// island bundle named by AssetLoadRequestOp.Name/Context.
    ///
    /// Keep this file net35/C# 7.3 compatible: the client mod links this exact
    /// source, while the native tests compile it in the multiplayer assembly.
    /// </summary>
    public static class IslandDistantShellProtocol
    {
        public const string EnabledEnvVar = "WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED";
        public const string RequestPrefix = "wareborn.island-shell.v1";
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

        public static bool TryParseRequest(string value, out IslandDistantShellSpec spec)
        {
            return TryParse(value, RequestPrefix, out spec);
        }

        public static bool TryParseReady(string value, out IslandDistantShellSpec spec)
        {
            return TryParse(value, ReadyPrefix, out spec);
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

    public sealed class IslandDistantShellSpec
    {
        public IslandDistantShellSpec(string islandId, long entityId,
            long x, long y, long z)
        {
            IslandId = islandId;
            EntityId = entityId;
            X = x;
            Y = y;
            Z = z;
        }

        public string IslandId { get; private set; }
        public long EntityId { get; private set; }
        public long X { get; private set; }
        public long Y { get; private set; }
        public long Z { get; private set; }
    }
}
