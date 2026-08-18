namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>alliances</c>.
    /// </summary>
    /// <param name="AllianceId">
    /// A real GUID, unlike a crew id.
    ///
    /// That is not a style choice. The client runs every alliance id it sends
    /// through <c>SocialHelper.SanitizeGuid</c>
    /// (AllianceServerImpl.cs:26/33/86/104/123/130/137, and <c>ValidateGuid</c> at
    /// :153/175), which requires a hyphen and then constructs a
    /// <c>System.Guid</c> - so an id shaped like the crews' <c>"crew:{guid}"</c>
    /// would throw a <c>FormatException</c> inside the client before any request
    /// was sent, and the player would see a dialog with no server involved at all.
    /// </param>
    /// <param name="EmblemUrl">
    /// The alliance crest, as a URL to a publicly readable image.
    ///
    /// RECOVERED and read-only: <c>emblemUrl</c> is a field on the client's
    /// AllianceDataModel that the client GETs with <c>SpriteDownloader</c> and
    /// NEVER sends. Neither <c>POST alliance</c> nor <c>PATCH alliance</c> carries
    /// it, and there is no picker, uploader or crest builder anywhere in the
    /// decompile. Empty is the normal value and renders the client's own local
    /// placeholder sprite. See docs/research/findings-social-api.md.
    /// </param>
    public sealed record AllianceRecord(
        Guid AllianceId,
        string Region,
        string Name,
        string Description,
        string MessageOfTheDay,
        string EmblemUrl,
        Guid LeaderUid,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    /// <summary>
    /// One row of <c>alliance_ranks</c> - the client's RankDataModel.
    /// </summary>
    /// <param name="Editable">
    /// Combined with <paramref name="RankType"/> this is how the client decides
    /// which rank is the founder's and which is the one people join on:
    /// <c>isDefaultLeaderRank = rankType == "leader" &amp;&amp; !editable</c>
    /// (SocialGroupParsers.cs:126-127). Both defaults must therefore be stored
    /// NOT editable, or the alliance panel loses its leader and its basic member.
    /// </param>
    /// <param name="Permissions">
    /// The client's permission literals, comma-separated.
    ///
    /// A text column rather than a Postgres array or a join table because this is
    /// a closed vocabulary of seven strings that is not ours to extend, is never
    /// queried by, and goes out on the wire as a JSON array either way. A join
    /// table would add a migration for every constraint it could not express.
    /// </param>
    /// <param name="SortOrder">
    /// The order ranks are emitted in. The client builds its rank lookup by
    /// iterating the array we send, so this is the order the founder sees.
    /// </param>
    public sealed record AllianceRankRecord(
        Guid RankId,
        Guid AllianceId,
        string Name,
        bool Editable,
        string RankType,
        string MembershipType,
        string Permissions,
        int SortOrder);

    /// <summary>
    /// One row of <c>alliance_members</c> - the client's AllianceMembershipDataModel.
    /// </summary>
    /// <param name="OfficerNote">
    /// The PUBLIC note. Named for the read side, which is where the mismatch
    /// bites: the client PATCHes it as <c>publicOfficerNote</c> and reads it back
    /// as <c>officerNote</c>, and then maps it onto a view-model field called
    /// <c>PublicNote</c> (SocialGroupParsers.cs:109). Three names, one value.
    /// </param>
    /// <param name="PrivateOfficerNote">
    /// The private note - <c>privateOfficerNote</c> in BOTH directions, and mapped
    /// onto the view model's <c>OfficerNote</c>. The swap is retail's, not ours.
    /// </param>
    public sealed record AllianceMemberRecord(
        Guid CharacterUid,
        Guid AllianceId,
        Guid RankId,
        string OfficerNote,
        string PrivateOfficerNote,
        int JoinOrder,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
