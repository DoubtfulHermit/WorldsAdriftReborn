namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>accounts</c>.
    ///
    /// Nothing in this folder names a game type. CharacterCreationData and
    /// friends live in WorldsAdriftServer, and conversion happens in a thin
    /// adapter there - so this library never has to be rebuilt because the
    /// client's JSON shape moved, and the game server can reference it without
    /// dragging the login server's object model along.
    /// </summary>
    /// <param name="AccountId">
    /// Database identity, assigned on insert. Never shown to a player and never
    /// sent to the client: what travels on the wire is the session token.
    /// </param>
    /// <param name="UsernameKey">Lowercased lookup key.</param>
    /// <param name="Username">The form the player typed, for display.</param>
    /// <param name="DisplayName">
    /// What goes back as screenName. Never empty: the client reads it with no
    /// null guard and an absent one ends in the QUIT dialog.
    /// </param>
    /// <param name="PasswordHash">pbkdf2$sha256$210000$salt$hash.</param>
    /// <param name="SteamUserKey">
    /// The linked 64-bit SteamID, or null. At most one account may hold any given
    /// value; the database enforces it.
    /// </param>
    public sealed record AccountRecord(
        long AccountId,
        string UsernameKey,
        string Username,
        string DisplayName,
        string PasswordHash,
        string? SteamUserKey,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLoginAt);
}
