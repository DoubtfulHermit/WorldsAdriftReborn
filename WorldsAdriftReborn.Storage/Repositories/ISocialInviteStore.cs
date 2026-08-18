using WorldsAdriftReborn.Storage.Records;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// The invite table as a port, for the same reason
    /// <see cref="IAllianceStore"/> is one.
    ///
    /// Alliance invitations and applications ride the SHARED membership-change
    /// endpoints - <c>POST memberships/invite</c>, <c>POST memberships/join</c>,
    /// and the accept/reject/cancel trio - so an alliance test that cannot reach
    /// this table can only cover half the feature. Extracted with no change to
    /// <see cref="SocialInviteRepository"/>'s behaviour: it simply declares that it
    /// implements what it already did.
    /// </summary>
    public interface ISocialInviteStore
    {
        SocialInviteRecord? Find(string inviteId);

        IReadOnlyList<SocialInviteRecord> ForCharacter(Guid characterUid);

        IReadOnlyList<SocialInviteRecord> ForTarget(string targetId);

        IReadOnlyList<SocialInviteRecord> AllLive();

        /// <summary>False when a live invite already covers the same
        /// (character, target) pair.</summary>
        bool TryInsert(SocialInviteRecord invite);

        /// <summary>False when the invite was already resolved, which is how a
        /// double-click on ACCEPT stops being two joins.</summary>
        bool Resolve(string inviteId, string status, DateTimeOffset at);

        int CancelAllForTarget(string targetId, DateTimeOffset at);
    }
}
