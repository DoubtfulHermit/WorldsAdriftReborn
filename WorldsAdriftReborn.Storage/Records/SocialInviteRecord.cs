namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// The four states a <see cref="SocialInviteRecord"/> can be in.
    ///
    /// The literals are not ours to choose: they are the exact strings the
    /// client's <c>SocialGroupParsers.CheckStatusType</c> switches on, and it
    /// THROWS on anything else rather than ignoring it - so one bad value breaks
    /// the whole invite list, not one row of it. Kept as constants next to the
    /// record so the repository, the service and the schema CHECK all quote the
    /// same source.
    /// </summary>
    public static class SocialInviteStatus
    {
        public const string New = "new";
        public const string Accepted = "accepted";
        public const string Rejected = "rejected";
        public const string Cancelled = "cancelled";
    }

    /// <summary>
    /// The kind of group an invite is into. Again the client's own vocabulary
    /// (<c>CheckSocialGroupType</c>), and again it throws on a third value.
    /// </summary>
    public static class SocialTargetType
    {
        public const string Crew = "crew_member";
        public const string Alliance = "alliance_member";
    }

    /// <summary>
    /// One row of <c>social_invites</c>: an offer of membership, in whichever
    /// direction it was made.
    /// </summary>
    /// <param name="InviterUid">
    /// Null means this is an APPLICATION - somebody asking to join - rather than
    /// an invite. That is not a convention of ours: the client decides which of
    /// the two it is by testing exactly this field for null
    /// (<c>CheckMembershipRequestType</c>), so representing it any other way here
    /// would create a second source of truth that could disagree with the wire.
    /// </param>
    public sealed record SocialInviteRecord(
        string InviteId,
        string TargetId,
        string TargetType,
        Guid CharacterUid,
        Guid? InviterUid,
        string Message,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
