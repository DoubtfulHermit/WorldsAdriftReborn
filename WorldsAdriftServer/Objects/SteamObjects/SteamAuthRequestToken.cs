namespace WorldsAdriftServer.Objects.SteamObjects
{
    internal class SteamCredential
    {
        public string platformId { get; set; }
        public string secret { get; set; }
        public string userKey { get; set; }
    }
    internal class SteamAuthRequestToken
    {
        public string appId { get; set; }
        public SteamCredential steamCredential { get; set; }

        /// <summary>
        /// The username/password the player typed into the game's own login form.
        ///
        /// The client has always sent this - LandingScreen.LoginFromForm calls
        /// BossaNetBootstrap.AuthenticateWithBossaNet, which adds a second credential
        /// object to the SAME /authenticate body as the Steam one. We simply never had
        /// a property to deserialize it into, so it was silently discarded.
        ///
        /// platformId is the literal "bossa"; userKey is the username; secret is the
        /// password, in cleartext (see the transport note in
        /// docs/research/findings-auth-protocol.md).
        /// </summary>
        public SteamCredential bossaCredential { get; set; }
    }
}
