namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>sessions</c>.
    ///
    /// <paramref name="Token"/> is a bearer credential, not an identifier: it is
    /// repeated on every roster call and whoever sees it is that account until it
    /// expires. Do not log it.
    /// </summary>
    public sealed record SessionRecord(
        string Token,
        long AccountId,
        DateTimeOffset IssuedAt,
        DateTimeOffset LastSeenAt,
        DateTimeOffset ExpiresAt);
}
