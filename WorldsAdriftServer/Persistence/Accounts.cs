using NetCoreServer;
using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// The login server's one connection to the account database, and the one
    /// place a request is turned into "which account is this?".
    ///
    /// Repositories are cheap wrappers over a connection string - Npgsql pools
    /// the actual sockets - so these are built once and shared. Nothing here
    /// holds a connection open between requests.
    /// </summary>
    internal static class Accounts
    {
        /// <summary>
        /// The header the client puts its auth token in, on every request after
        /// /authenticate: character list, slot reservation, character save and
        /// Enter World all call SetHeader("Security", GameClientAuthToken)
        /// (BossaNetBootstrap.cs:289, CharacterSelectionHandler.cs:324).
        ///
        /// That token is whatever we returned as "token" from /authenticate, so
        /// it is our session token coming back to us. It is the ONLY channel
        /// carrying identity: the character URLs hardcode "steam/1234" for every
        /// player (CharacterSelectionHandler.cs:93/143/218), so the path cannot
        /// tell two accounts apart and the header is what makes per-account
        /// rosters possible at all.
        /// </summary>
        internal const string SecurityHeader = "Security";

        private static readonly object gate = new object();
        private static Db? db;
        private static AccountRepository? accounts;
        private static SessionRepository? sessions;
        private static CharacterRepository? characters;
        private static ServerConfigRepository? serverConfig;
        private static CrewRepository? crews;
        private static SocialInviteRepository? socialInvites;

        internal static Db Database => Ensure().db;
        internal static AccountRepository Repository => Ensure().accounts;
        internal static SessionRepository Sessions => Ensure().sessions;
        internal static CharacterRepository Characters => Ensure().characters;
        internal static ServerConfigRepository ServerConfig => Ensure().serverConfig;

        /// <summary>
        /// Crews, shared with the game server through the same tables.
        ///
        /// The login server is a SECOND writer here, which v6 did not anticipate:
        /// the retail Social Sheet drives crews over HTTP, so create, invite, boot,
        /// leave and disband all arrive on this process. See
        /// docs/research/findings-social-api.md.
        /// </summary>
        internal static CrewRepository Crews => Ensure().crews;

        /// <summary>Crew and alliance invites - schema v7, written only here.</summary>
        internal static SocialInviteRepository SocialInvites => Ensure().socialInvites;

        /// <summary>
        /// Opens the database and applies the schema. Called once at startup so
        /// a bad connection string is a loud failure on the console rather than
        /// a player staring at a login form that never answers.
        /// </summary>
        internal static void Initialize()
        {
            // EnsureSchema returns the version it left the database at, not a
            // count of scripts run - it is a no-op on an up-to-date database.
            int version = Database.EnsureSchema();

            Console.WriteLine("[info] account database ready at schema v" + version + "; "
                + Repository.Count() + " account(s) registered.");
        }

        private static (Db db, AccountRepository accounts, SessionRepository sessions, CharacterRepository characters, ServerConfigRepository serverConfig, CrewRepository crews, SocialInviteRepository socialInvites) Ensure()
        {
            lock (gate)
            {
                if (db == null)
                {
                    db = new Db();
                    accounts = new AccountRepository(db);
                    sessions = new SessionRepository(db);
                    characters = new CharacterRepository(db);
                    serverConfig = new ServerConfigRepository(db);
                    crews = new CrewRepository(db);
                    socialInvites = new SocialInviteRepository(db);
                }

                return (db!, accounts!, sessions!, characters!, serverConfig!, crews!, socialInvites!);
            }
        }

        /// <summary>
        /// The account behind a request, or null if its Security header carries
        /// no live session.
        ///
        /// Callers must treat null as "refuse", not as "fall back to a shared
        /// roster" - a fallback here is how one player ends up looking at
        /// another player's characters.
        /// </summary>
        internal static AccountRecord? Resolve(HttpRequest request)
        {
            string? token = HeaderValue(request, SecurityHeader);

            SessionRecord? session = Sessions.Resolve(token, DateTimeOffset.UtcNow);
            if (session == null)
            {
                return null;
            }

            return Repository.FindById(session.AccountId);
        }

        /// <summary>
        /// NetCoreServer exposes headers only as an indexed pair list, and header
        /// names are case-insensitive on the wire.
        /// </summary>
        internal static string? HeaderValue(HttpRequest request, string name)
        {
            for (int i = 0; i < request.Headers; i++)
            {
                (string header, string value) = request.Header(i);
                if (string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
