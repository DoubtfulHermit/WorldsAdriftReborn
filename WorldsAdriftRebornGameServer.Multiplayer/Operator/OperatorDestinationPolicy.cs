using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;

namespace WorldsAdriftRebornGameServer.Multiplayer.Operator
{
    /// <summary>Where an operator may send somebody.</summary>
    public enum OperatorDestinationKind
    {
        None = 0,

        /// <summary>A catalogued island, by stable id or by Bossa's display name.</summary>
        Island,

        /// <summary>Explicit world metres. No ground is promised.</summary>
        Coordinate,

        /// <summary>Wherever another player is standing right now.</summary>
        Player,

        /// <summary>The target's own recorded home / graduation island.</summary>
        Home,

        /// <summary>The Haven spawn point - the one destination with evidenced ground.</summary>
        Spawn,
    }

    /// <summary>
    /// One parsed destination, still unresolved. The VALUES are kept as text
    /// because resolution needs the world (which islands are registered this boot,
    /// where a player is standing, what a character's stored position is) and this
    /// type must stay free of it.
    /// </summary>
    public readonly record struct OperatorDestinationSpec(
        OperatorDestinationKind Kind,
        string Value,
        double X,
        double Y,
        double Z)
    {
        public static OperatorDestinationSpec OfIsland(string key) =>
            new OperatorDestinationSpec(OperatorDestinationKind.Island, key, 0, 0, 0);

        public static OperatorDestinationSpec OfPlayer(string selector) =>
            new OperatorDestinationSpec(OperatorDestinationKind.Player, selector, 0, 0, 0);

        public static OperatorDestinationSpec OfCoordinate(double x, double y, double z) =>
            new OperatorDestinationSpec(OperatorDestinationKind.Coordinate, string.Empty, x, y, z);

        public static readonly OperatorDestinationSpec HomeSpec =
            new OperatorDestinationSpec(OperatorDestinationKind.Home, string.Empty, 0, 0, 0);

        public static readonly OperatorDestinationSpec SpawnSpec =
            new OperatorDestinationSpec(OperatorDestinationKind.Spawn, string.Empty, 0, 0, 0);

        /// <summary>
        /// The canonical single-token wire form. Coordinates are written with
        /// round-trip precision ("R") because a truncated metre here is a metre of
        /// terrain a player lands inside of.
        /// </summary>
        public string ToSpec() => Kind switch
        {
            OperatorDestinationKind.Island => "island:" + Value,
            OperatorDestinationKind.Player => "player:" + Value,
            OperatorDestinationKind.Coordinate => "coord:"
                + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
                + Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            OperatorDestinationKind.Home => "home",
            OperatorDestinationKind.Spawn => "spawn",
            _ => string.Empty,
        };

        public override string ToString() => ToSpec();
    }

    /// <summary>
    /// Parsing and island lookup for operator destinations. Pure; the parts that
    /// need the live world (a player's position, a character's stored home, which
    /// terrain is registered) are the caller's, and are handed in.
    ///
    /// The islands come from <see cref="ReleaseWorldCatalog"/> - all 254 records,
    /// each with a surveyed landing point - rather than from
    /// <see cref="TeleportPolicy.Destinations"/>, which is the five hand-derived
    /// places the trigger file could name. That is the whole difference between
    /// "teleport to one of three allowlisted islands" and "teleport anyone
    /// anywhere": the survey data for the entire world already exists and already
    /// has a stand-off applied, and the safety comes from the terrain gate at
    /// dispatch, not from the shortness of a list.
    /// </summary>
    public static class OperatorDestinationPolicy
    {
        /// <summary>
        /// Parses a destination spec.
        ///
        /// <code>
        ///   island:&lt;id&gt;              e.g. island:mental-facility
        ///   island:&lt;display name&gt;     e.g. island:Mental Facility
        ///   coord:&lt;x&gt;,&lt;y&gt;,&lt;z&gt;        world metres
        ///   player:&lt;selector&gt;         another player, by any target selector
        ///   home                       the target's recorded home island
        ///   spawn | haven              the Haven spawn point
        /// </code>
        ///
        /// A bare unprefixed word is tried as an ISLAND, because that is the only
        /// reading that cannot silently mean something else - "haven" and "spawn"
        /// are matched first and explicitly.
        /// </summary>
        public static bool TryParse(string? raw, out OperatorDestinationSpec spec, out string error)
        {
            spec = default;
            error = string.Empty;

            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "No destination was given. Use island:<name>, coord:<x>,<y>,<z>, "
                    + "player:<selector>, home or spawn.";
                return false;
            }

            string lower = text.ToLowerInvariant();
            if (lower == "home")
            {
                spec = OperatorDestinationSpec.HomeSpec;
                return true;
            }
            if (lower == "spawn" || lower == "haven")
            {
                spec = OperatorDestinationSpec.SpawnSpec;
                return true;
            }

            int colon = text.IndexOf(':');
            if (colon > 0)
            {
                string prefix = text.Substring(0, colon).Trim().ToLowerInvariant();
                string value = text.Substring(colon + 1).Trim();

                switch (prefix)
                {
                    case "island":
                        if (value.Length == 0)
                        {
                            error = "No island was named.";
                            return false;
                        }
                        spec = OperatorDestinationSpec.OfIsland(value);
                        return true;

                    case "player":
                        if (value.Length == 0)
                        {
                            error = "No player was named as the destination.";
                            return false;
                        }
                        spec = OperatorDestinationSpec.OfPlayer(value);
                        return true;

                    case "coord":
                        return TryParseCoordinate(value, out spec, out error);
                }

                error = "'" + prefix + ":' is not a destination kind; use island:, coord:, "
                    + "player:, home or spawn.";
                return false;
            }

            spec = OperatorDestinationSpec.OfIsland(text);
            return true;
        }

        private static bool TryParseCoordinate(
            string value, out OperatorDestinationSpec spec, out string error)
        {
            spec = default;
            error = string.Empty;

            string[] parts = value.Split(
                new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                error = "coord: needs exactly three world metres, as coord:<x>,<y>,<z>.";
                return false;
            }

            double[] axes = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out axes[i])
                    || double.IsNaN(axes[i]) || double.IsInfinity(axes[i]))
                {
                    error = "'" + parts[i] + "' is not a number of metres.";
                    return false;
                }
            }

            spec = OperatorDestinationSpec.OfCoordinate(axes[0], axes[1], axes[2]);
            return true;
        }

        /// <summary>
        /// Finds a catalogued island by stable id or by display name.
        ///
        /// Both lookups are case- and whitespace-insensitive and ignore
        /// non-alphanumerics, so "Mental Facility", "mental-facility" and
        /// "mentalfacility" all reach the same island. A display name matching more
        /// than one record is REFUSED as ambiguous rather than resolved: the
        /// preserved world does contain repeated names, and sending a player to
        /// "whichever one sorted first" is exactly the silent wrong answer this
        /// whole surface is trying not to give.
        /// </summary>
        public static bool TryFindIsland(
            string? key, out IslandId island, out string error)
        {
            island = default;
            error = string.Empty;

            string text = (key ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "No island was named.";
                return false;
            }

            ReleaseIslandRecord? exact = ReleaseWorldCatalog.ByIsland(new IslandId(text));
            if (exact != null)
            {
                island = exact.Definition.Id;
                return true;
            }

            string folded = Fold(text);
            ReleaseIslandRecord? found = null;
            int matches = 0;
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                if (Fold(record.Definition.Id.Value) != folded
                    && Fold(record.Definition.DisplayName) != folded)
                {
                    continue;
                }
                matches++;
                if (matches == 1) found = record;
            }

            if (matches == 1 && found != null)
            {
                island = found.Definition.Id;
                return true;
            }

            if (matches > 1)
            {
                error = "'" + text + "' names " + matches
                    + " islands; use the exact island id instead.";
                return false;
            }

            error = "'" + text + "' is not an island this world has. Use the island id "
                + "(for example 'mental-facility') or its exact display name.";
            return false;
        }

        /// <summary>
        /// The teleport destination for a catalogued island, named for the log.
        /// Delegates the landing arithmetic to <see cref="WildernessCatalog"/> so
        /// the operator surface and the shrine cannot disagree about where the
        /// ground on an island is.
        /// </summary>
        public static bool TryIslandDestination(
            IslandId island,
            IslandDefinition? registered,
            string reason,
            out TeleportDestination destination,
            out string error)
        {
            destination = default;
            error = string.Empty;

            WildernessDestination? landing = WildernessCatalog.Landing(island, registered);
            if (landing == null)
            {
                error = "Island '" + island + "' has no surveyed landing point.";
                return false;
            }

            destination = WildernessCatalog.AsTeleportDestination(landing.Value, reason);
            return true;
        }

        /// <summary>
        /// Lower-cased alphanumerics only. Deliberately drops spaces, dashes and
        /// apostrophes, which is the entire difference between the id form and the
        /// display form of most of these names.
        /// </summary>
        private static string Fold(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            System.Text.StringBuilder folded = new System.Text.StringBuilder(text!.Length);
            foreach (char c in text!)
            {
                if (char.IsLetterOrDigit(c)) folded.Append(char.ToLowerInvariant(c));
            }
            return folded.ToString();
        }
    }
}
