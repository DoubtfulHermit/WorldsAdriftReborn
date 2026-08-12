namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The one place the process-wide player web-session set is held, so the login
    /// handler that issues a cookie and the download handler that checks it share
    /// the same map. The mirror of <see cref="WorldsAdriftServer.Admin.AdminConfig"/>'s
    /// <c>Sessions</c> property for the player side.
    ///
    /// Nothing here is credential state - there is no player password to load, that
    /// lives in Postgres and is checked per-attempt through
    /// <see cref="WorldsAdriftServer.Persistence.Accounts"/> - so this is a single
    /// shared instance and no more.
    /// </summary>
    internal static class PlayerAuth
    {
        /// <summary>Process-wide live player web sessions.</summary>
        internal static PlayerSessions Sessions { get; } = new PlayerSessions();
    }
}
