using WorldsAdriftServer.Emblems;

namespace WorldsAdriftServer.Portal
{
    /// <summary>
    /// Everything the account portal draws, as data.
    ///
    /// The page renders THIS and asks nothing else - no repository, no ledger, no
    /// clock. That is what lets the whole portal be asserted from a test with no
    /// database: build a view, render it, read the markup. It is also the line
    /// that keeps a permission decision out of the markup. Every "may I" below is
    /// already a boolean by the time it arrives, decided once by
    /// <see cref="PortalPermissions"/> against the same ledger the handler will
    /// re-check the post against.
    ///
    /// WHAT IS DELIBERATELY NOT HERE: character uids appear only where a form has
    /// to post one back, session tokens never, and the account's e-mail is not a
    /// column this server stores at all. The portal is authenticated end to end
    /// and none of it is reachable without a live cookie, but the shape still
    /// carries only what a page needs.
    /// </summary>
    internal sealed record PortalView(
        string Username,
        string DisplayName,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastLoginAt,
        string PatchVersion,
        string PatchBuild,
        IReadOnlyList<CharacterCard> Characters,
        string Csrf,
        string? Notice,
        bool NoticeIsError);

    /// <summary>One character, with whatever groups it belongs to.</summary>
    internal sealed record CharacterCard(
        CharacterSheet Sheet,
        CrewCard? Crew,
        AllianceCard? Alliance);

    /// <summary>
    /// The player's crew, read-only.
    ///
    /// READ-ONLY ON PURPOSE, and it is not an omission. A crew is the in-world
    /// party: retail drives it from the Social Sheet, membership changes are
    /// pushed to a live game server that holds its own ledger, and a portal that
    /// disbanded a crew out from under a running session would be changing rows
    /// the other process has already read. The alliance is different - the login
    /// server is its only writer (see <c>Accounts.Alliances</c>) - which is
    /// exactly why the alliance card below has controls and this one does not.
    /// </summary>
    /// <param name="Name">
    /// Derived, because a crew has no name of its own: "&lt;leader&gt;'s crew",
    /// the same sentence <c>SocialService.CrewDataFor</c> sends the game client,
    /// so the portal and the Social Sheet call the same crew the same thing.
    /// </param>
    internal sealed record CrewCard(
        string CrewId,
        string Name,
        int Slots,
        IReadOnlyList<CrewMemberRow> Members);

    internal sealed record CrewMemberRow(
        string Name,
        bool IsLeader,
        bool IsYou,
        int? Slot);

    /// <summary>The player's alliance, and what this character may do to it.</summary>
    internal sealed record AllianceCard(
        Guid AllianceId,
        Guid ActingCharacterUid,
        string Name,
        string Description,
        string MessageOfTheDay,
        string YourRank,
        IReadOnlyList<string> YourPermissions,
        bool YouAreTheFounder,
        IReadOnlyList<AllianceMemberRow> Members,
        IReadOnlyList<AllianceRankRow> Ranks,
        IReadOnlyList<RequestRow> Applications,
        IReadOnlyList<RequestRow> Invitations,
        EmblemSpec Emblem,
        bool EmblemBuilt,
        string? ExternalEmblemUrl,
        AllianceRights Rights);

    /// <param name="MayBoot">
    /// Already decided by
    /// <see cref="WorldsAdriftRebornGameServer.Multiplayer.Alliance.AlliancePolicy.MayBoot"/>
    /// for THIS row, not by "does the actor hold edit_members". The two differ on
    /// the founder's row and on the actor's own, and a page that drew the button
    /// from the coarser answer would offer two buttons that always fail.
    /// </param>
    internal sealed record AllianceMemberRow(
        Guid CharacterUid,
        string Name,
        string RankName,
        Guid RankId,
        bool IsFounder,
        bool IsYou,
        bool MayBoot,
        bool MaySetRank);

    /// <param name="Editable">
    /// False for the two default ranks. They are the slots the client fills its
    /// Leader and BasicMember fields from, so they are listed but never offered
    /// as a destination the founder can delete out from under.
    /// </param>
    internal sealed record AllianceRankRow(
        Guid RankId,
        string Name,
        bool Editable,
        bool IsDefaultLeader,
        IReadOnlyList<string> Permissions);

    /// <summary>
    /// One outstanding application or invitation.
    ///
    /// The two directions share a row because they share a table and a lifetime;
    /// which one it is is the presence of an inviter, exactly as the client's own
    /// <c>CheckMembershipRequestType</c> decides it.
    /// </summary>
    internal sealed record RequestRow(
        string InviteId,
        string CharacterName,
        string Message,
        DateTimeOffset At);
}
