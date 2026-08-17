namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// The complete error vocabulary the client understands.
    ///
    /// This list is CLOSED and is not ours to extend. <c>HttpHelper.ParseErrorCode</c>
    /// takes the <c>errorCode</c> we send and looks it up in the client's own
    /// shipped GameDB table (<c>ServerErrorCodesSchema.cs:9-45</c>); a code that is
    /// not in that table renders to the player as the literal string
    /// "Unknown error code: whatever_we_invented". So inventing a code does not
    /// produce a slightly-wrong message, it produces visible debug text in a
    /// dialog box.
    ///
    /// Every constant here was read out of that table. Codes we have no use for
    /// are still listed, because the next person to add an endpoint should be able
    /// to see the whole menu before concluding that none of it fits.
    /// </summary>
    internal static class SocialErrorCodes
    {
        internal const string AllianceAtCapacity = "alliance_at_capacity";
        internal const string AlreadyAMember = "already_a_member";
        internal const string AlreadyInAlliance = "already_in_alliance";
        internal const string AuthFailed = "auth_failed";
        internal const string CrewAtCapacity = "crew_at_capacity";
        internal const string DuplicateAllianceName = "duplicate_alliance_name";

        /// <summary>
        /// Originally "the DynamoDB read failed". We use it for "this server has
        /// no store that can answer that", which is the same class of failure from
        /// the player's side: the backing store cannot serve the request. It is the
        /// only code in the table that means anything of the kind, and the
        /// alternative - a non-200, whose body the client discards - produces a
        /// worse dialog and no code at all.
        /// </summary>
        internal const string StoreUnavailable = "dynamo_read";

        internal const string EmptyUpdatePayload = "empty_update_payload";
        internal const string ExistingInvite = "existing_invite";
        internal const string InvalidEntityId = "invalid_entity_id";
        internal const string InvalidEntityPair = "invalid_entity_pair";
        internal const string InvalidName = "invalid_name";
        internal const string InviteLimitMet = "invite_limit_met";
        internal const string InviteNotFound = "invite_not_found";
        internal const string JsonDeserialization = "json_deserialization";
        internal const string NoAuthToken = "no_auth_token";
        internal const string NoRanksFoundInAlliance = "no_ranks_found_in_alliance";
        internal const string SelfInvite = "self_invite";
        internal const string UneditableRank = "uneditable_rank";
    }
}
