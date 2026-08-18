using Newtonsoft.Json.Linq;
using WorldsAdriftServer.PublicMap;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The per-hull STATIC GEOMETRY endpoint, shared by the operator console and the
    /// public map.
    ///
    /// WHY THIS IS NOT IN THE LIVE POLL. A hull's elevation, its decks and the places
    /// its parts are bolted to do not change from one snapshot to the next; a ship's
    /// POSITION is the only thing that does. The live payload is read every 1.5 s by
    /// an operator and every 3 s by every public viewer, so a drawing that rode it
    /// would be re-sent hundreds of times an hour to say the same thing. Islands
    /// solved the identical problem the identical way: a coastline is served from its
    /// own document, once, and the live feed carries only what moves.
    ///
    /// So the geometry rides the game server's stats FILE - that file is the only
    /// channel the two processes have - and stops there. The login server parses it
    /// (<see cref="GameShipDomainStat.Geometry"/>), keeps it out of the poll, and
    /// hands it over here when a reader opens a ship's card. What DOES ride the poll
    /// is one integer per ship, the geometry revision, so a card that already drew a
    /// hull can tell whether the drawing has changed - which is how a newly mounted
    /// lamp reaches an open page without the parts list being re-sent forever.
    ///
    /// TWO PAGES, TWO SELECTORS, ONE WHITELIST. The console asks by hull entity id;
    /// the public map has no entity ids and asks by the same opaque marker token its
    /// live feed uses, which this resolves by re-deriving the token for each ship.
    /// The public answer is rebuilt by <see cref="PublicMapProjection"/>, so the
    /// privacy boundary of this endpoint is the SAME whitelist as the live feed's -
    /// there is no second place to forget.
    ///
    /// Pure: every input arrives as a parameter, so both branches are testable
    /// without a socket.
    /// </summary>
    internal static class ShipGeometryEndpoint
    {
        /// <summary>The query key the console asks with.</summary>
        internal const string HullKey = "hull";

        /// <summary>The query key the public map asks with.</summary>
        internal const string TokenKey = "id";

        /// <summary>
        /// The value of one query parameter, or null when the URL does not carry it.
        /// Deliberately tiny and deliberately strict: a selector is an opaque
        /// identifier, so anything with a character that is not a digit, a letter,
        /// a dash or a colon is refused rather than decoded - there is no legitimate
        /// selector that needs escaping, and refusing is cheaper to reason about
        /// than unescaping.
        /// </summary>
        internal static string? Selector(string? url, string key)
        {
            if (string.IsNullOrEmpty(url)) return null;
            int q = url!.IndexOf('?');
            if (q < 0 || q + 1 >= url.Length) return null;

            foreach (string pair in url.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pair.Substring(0, eq), key, StringComparison.Ordinal)) continue;

                string value = pair.Substring(eq + 1);
                if (value.Length == 0 || value.Length > 64) return null;
                foreach (char c in value)
                {
                    bool allowed = (c >= '0' && c <= '9')
                        || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                        || c == '-' || c == ':' || c == '_';
                    if (!allowed) return null;
                }
                return value;
            }
            return null;
        }

        /// <summary>
        /// The console's answer: the geometry of the hull with this entity id, as the
        /// game server published it.
        /// </summary>
        internal static JObject ForOperator(GameStatsResult result, string? hullEntityId)
        {
            if (string.IsNullOrEmpty(hullEntityId)) return Refusal("no-ship-selected");
            if (result.State != GameStatsState.Ok || result.Snapshot == null)
                return Refusal("not-reporting");

            foreach (GameShipDomainStat ship in result.Snapshot.ShipDomains)
            {
                string id = ((long?)ship.Json["hullEntityId"] ?? 0)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(id, hullEntityId, StringComparison.Ordinal)) continue;
                return Answer(hullEntityId!, ship.GeometryRevision, ship.Geometry);
            }

            return Refusal("unknown-ship");
        }

        /// <summary>
        /// The public map's answer: the same drawing, reached by the same opaque
        /// token the live feed labels this ship with, and rebuilt through the
        /// anonymizing whitelist.
        ///
        /// The token is re-derived per ship rather than reversed - a hash has no
        /// inverse - which is a linear scan over a handful of ships and keeps the
        /// real domain id and the real entity id on this side of the boundary.
        /// </summary>
        internal static JObject ForPublic(GameStatsResult result, string? token, byte[] salt)
        {
            if (string.IsNullOrEmpty(token)) return Refusal("no-ship-selected");
            if (result.State != GameStatsState.Ok || result.Snapshot == null)
                return Refusal("not-reporting");

            foreach (GameShipDomainStat ship in result.Snapshot.ShipDomains)
            {
                string domainId = (string?)ship.Json["domainId"] ?? "";
                if (!string.Equals(PublicMapProjection.AnonymousId("ship", domainId, salt), token,
                        StringComparison.Ordinal)) continue;
                return Answer(token!, ship.GeometryRevision,
                    PublicMapProjection.ProjectShipGeometry(ship.Geometry));
            }

            return Refusal("unknown-ship");
        }

        private static JObject Answer(string id, long revision, JObject geometry) => new JObject
        {
            ["ok"] = true,
            ["id"] = id,
            ["revision"] = revision,
            ["geometry"] = geometry,
        };

        /// <summary>
        /// A refusal NAMES ITSELF. The card prints the reason, because "no drawing"
        /// and "the game server is down" and "that ship is gone" are three different
        /// things and a blank panel says none of them.
        /// </summary>
        private static JObject Refusal(string reason) => new JObject
        {
            ["ok"] = false,
            ["reason"] = reason,
        };
    }
}
