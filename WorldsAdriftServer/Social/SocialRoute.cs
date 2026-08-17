namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The social requests this server recognises.
    ///
    /// Named after the client method that issues each one rather than after the
    /// URL, because the URLs are inconsistent in ways the names are not - see
    /// <see cref="SocialRoute"/> for the catalogue.
    /// </summary>
    internal enum SocialRouteKind
    {
        None = 0,

        /// <summary>GET memberships/character/{characterUid}</summary>
        CharacterMemberships,

        /// <summary>GET screenname/find/{searchTerm}</summary>
        CharacterSearch,

        /// <summary>GET memberships/invites/character/{characterUid}</summary>
        InvitesForCharacter,

        /// <summary>POST memberships/invite</summary>
        SendInvite,

        /// <summary>PUT memberships/invite/accept/{inviteUid}/{characterUid}/{region}</summary>
        AcceptInvite,

        /// <summary>PUT memberships/invite/reject/{inviteUid}/{characterUid}</summary>
        RejectInvite,

        /// <summary>PUT memberships/invite/cancel/{inviteUid}/{characterUid}/{region}</summary>
        CancelInvite,

        /// <summary>POST crews</summary>
        CreateCrew,

        /// <summary>GET crew/{region}/{crewUid}</summary>
        GetCrew,

        /// <summary>DELETE crew/{region}/{crewUid}</summary>
        DisbandCrew,

        /// <summary>GET memberships/crew/{crewUid}</summary>
        CrewMembers,

        /// <summary>GET memberships/invites/crew/{crewUid}</summary>
        CrewInvites,

        /// <summary>DELETE memberships/crew/{crewUid}/{characterUid}</summary>
        RemoveCrewMember,

        /// <summary>GET alliances/{region}</summary>
        ListAlliances,

        /// <summary>GET alliance/search/{region}?term=...</summary>
        SearchAlliances,
    }

    /// <summary>
    /// A parsed social request: which endpoint, and the path segments it carried.
    ///
    /// Pure. It takes a method and a raw URL and nothing else, so every route in
    /// the reconstructed contract can be pinned by a unit test without a socket,
    /// a database or a client. That matters more here than usual: these paths
    /// were recovered by reading a decompiler's output, and the ONLY defence
    /// against a transcription slip is that the shape is written down twice - once
    /// as a matcher, once as a test - and the two agree.
    ///
    /// The catalogue is in docs/research/findings-social-api.md with file:line
    /// citations into the decompile. Two irregularities are load-bearing and are
    /// reproduced deliberately rather than tidied:
    ///
    ///   - reject has NO region segment while accept and cancel do
    ///     (SocialServerImpl.cs:85/93/101);
    ///   - the crew member list returns a bare array where its neighbours return
    ///     {"items": [...]} - that is a response concern, not a routing one, but
    ///     it is the same class of asymmetry.
    /// </summary>
    internal sealed class SocialRoute
    {
        internal SocialRouteKind Kind { get; }

        /// <summary>The captured path segments, already percent-decoded.</summary>
        internal IReadOnlyList<string> Segments { get; }

        private SocialRoute(SocialRouteKind kind, params string[] segments)
        {
            Kind = kind;
            Segments = segments;
        }

        private static readonly SocialRoute NoMatch = new SocialRoute(SocialRouteKind.None);

        /// <summary>
        /// True when the URL is in the social namespace at all, whether or not it
        /// resolves to a route we serve.
        ///
        /// This is the difference between "not a social request, let the next
        /// handler look at it" and "a social request we do not implement, which
        /// must be answered rather than fall through to a 404 page". The
        /// distinction is not cosmetic: the client treats ANY non-200 as a
        /// transport failure and pops "Issue connecting to server" at the player
        /// (HttpHelper.cs:120), so an unimplemented social endpoint has to be
        /// refused deliberately, in-band, on a 200.
        /// </summary>
        internal static bool IsSocialUrl(string url)
        {
            string[] segments = Split(url);
            if (segments.Length == 0) return false;

            switch (segments[0])
            {
                case "memberships":
                case "crew":
                case "crews":
                case "screenname":
                case "alliance":
                case "alliances":
                case "rank":
                case "ranks":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Resolves a method and URL to a route, or <see cref="SocialRouteKind.None"/>.
        /// </summary>
        internal static SocialRoute Parse(string method, string url)
        {
            if (method == null || url == null) return NoMatch;

            string[] s = Split(url);
            if (s.Length == 0) return NoMatch;

            string verb = method.ToUpperInvariant();

            switch (s[0])
            {
                case "memberships":
                    return ParseMemberships(verb, s);

                // Singular: one crew. Plural: the collection you POST to. The
                // client really does use both, for the same resource.
                case "crew" when s.Length == 3 && verb == "GET":
                    return new SocialRoute(SocialRouteKind.GetCrew, s[1], s[2]);
                case "crew" when s.Length == 3 && verb == "DELETE":
                    return new SocialRoute(SocialRouteKind.DisbandCrew, s[1], s[2]);

                case "crews" when s.Length == 1 && verb == "POST":
                    return new SocialRoute(SocialRouteKind.CreateCrew);

                case "screenname" when s.Length >= 3 && s[1] == "find" && verb == "GET":
                    // The search term is everything after "find", rejoined. A
                    // character name containing a slash would otherwise be
                    // truncated here, and the client does not escape the name
                    // separately from the path - see IsSocialUrl's note and the
                    // %2F caveat in the findings document.
                    return new SocialRoute(
                        SocialRouteKind.CharacterSearch,
                        string.Join("/", s, 2, s.Length - 2));

                case "alliances" when s.Length == 2 && verb == "GET":
                    return new SocialRoute(SocialRouteKind.ListAlliances, s[1]);

                case "alliance" when s.Length == 3 && s[1] == "search" && verb == "GET":
                    return new SocialRoute(SocialRouteKind.SearchAlliances, s[2]);

                default:
                    return NoMatch;
            }
        }

        private static SocialRoute ParseMemberships(string verb, string[] s)
        {
            // memberships/character/{uid}
            if (s.Length == 3 && s[1] == "character" && verb == "GET")
                return new SocialRoute(SocialRouteKind.CharacterMemberships, s[2]);

            // memberships/crew/{crewUid}
            if (s.Length == 3 && s[1] == "crew" && verb == "GET")
                return new SocialRoute(SocialRouteKind.CrewMembers, s[2]);

            // memberships/crew/{crewUid}/{characterUid}
            if (s.Length == 4 && s[1] == "crew" && verb == "DELETE")
                return new SocialRoute(SocialRouteKind.RemoveCrewMember, s[2], s[3]);

            // memberships/invite
            if (s.Length == 2 && s[1] == "invite" && verb == "POST")
                return new SocialRoute(SocialRouteKind.SendInvite);

            if (s.Length >= 2 && s[1] == "invite" && verb == "PUT")
            {
                // accept and cancel carry a trailing region segment; reject does
                // not. Matching on a MINIMUM length rather than an exact one means
                // a future region-bearing reject would still resolve, and an
                // absent region on accept - which we never use - does not 404.
                if (s.Length >= 5 && s[2] == "accept")
                    return new SocialRoute(SocialRouteKind.AcceptInvite, s[3], s[4]);
                if (s.Length >= 5 && s[2] == "reject")
                    return new SocialRoute(SocialRouteKind.RejectInvite, s[3], s[4]);
                if (s.Length >= 5 && s[2] == "cancel")
                    return new SocialRoute(SocialRouteKind.CancelInvite, s[3], s[4]);
            }

            if (s.Length == 4 && s[1] == "invites" && verb == "GET")
            {
                if (s[2] == "character")
                    return new SocialRoute(SocialRouteKind.InvitesForCharacter, s[3]);
                if (s[2] == "crew")
                    return new SocialRoute(SocialRouteKind.CrewInvites, s[3]);
            }

            return NoMatch;
        }

        /// <summary>
        /// Splits a URL into decoded path segments, dropping the query string.
        ///
        /// It decodes AFTER splitting on a literal slash and then splits AGAIN,
        /// which looks redundant and is not. The client builds its character
        /// search as Uri.EscapeDataString("screenname/find/NAME") -
        /// SocialServerImpl.cs:42 - escaping the separators along with the name.
        /// Whether those %2F survive Unity's Mono System.Uri canonicalisation
        /// cannot be determined by reading the decompile, so the request may
        /// arrive as /screenname/find/NAME or as /screenname%2Ffind%2FNAME. Both
        /// have to route to the same place, and the second only does so if the
        /// decoded form is re-split.
        /// </summary>
        private static string[] Split(string url)
        {
            if (string.IsNullOrEmpty(url)) return Array.Empty<string>();

            int query = url.IndexOf('?');
            string path = query >= 0 ? url.Substring(0, query) : url;

            string[] rough = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> segments = new List<string>(rough.Length);

            foreach (string piece in rough)
            {
                string decoded = Uri.UnescapeDataString(piece);
                foreach (string inner in decoded.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    segments.Add(inner);
                }
            }

            return segments.ToArray();
        }

        /// <summary>
        /// The value of a query parameter, or null.
        ///
        /// Only <c>?term=</c> is ever read, but it is written generically so the
        /// next caller does not hand-roll a second parser.
        /// </summary>
        internal static string? QueryValue(string url, string name)
        {
            if (url == null || name == null) return null;

            int query = url.IndexOf('?');
            if (query < 0) return null;

            foreach (string pair in url.Substring(query + 1).Split('&'))
            {
                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                if (!string.Equals(key, name, StringComparison.Ordinal)) continue;
                return equals < 0 ? string.Empty : Uri.UnescapeDataString(pair.Substring(equals + 1));
            }

            return null;
        }
    }
}
