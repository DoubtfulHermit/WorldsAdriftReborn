namespace WorldsAdriftServer.Social
{
    /// <summary>
    /// Who a social request is allowed to act as.
    ///
    /// Pure, and separate from the handler, because this is the whole security
    /// surface of the social API and it is the kind of rule that is easy to get
    /// subtly wrong and expensive to notice: every crew action names its target
    /// in the URL, so a server that trusted the URL would let anyone disband
    /// anyone's crew by typing a different uid.
    ///
    /// The request carries two claims and they are not equal in weight:
    ///
    ///   - <c>Security</c> is the session token. It is unforgeable by a player
    ///     and it is the only thing that establishes WHICH ACCOUNT is calling.
    ///   - <c>CharacterUid</c> is just a string the client put in a header. It
    ///     establishes nothing on its own.
    ///
    /// So the rule is: resolve the account from Security, then require that the
    /// claimed character is one of THAT ACCOUNT'S characters. A character uid that
    /// belongs to somebody else is refused even though it is a perfectly valid
    /// uid, which is exactly the case a uid-shaped check would wave through.
    /// </summary>
    internal static class SocialIdentityPolicy
    {
        /// <summary>
        /// The outcome of authorising a request: either an authorised character,
        /// or the error code to answer with.
        /// </summary>
        internal readonly struct Outcome
        {
            internal Guid Character { get; }
            internal string? ErrorCode { get; }

            internal bool Authorized => ErrorCode == null;

            private Outcome(Guid character, string? errorCode)
            {
                Character = character;
                ErrorCode = errorCode;
            }

            internal static Outcome Allow(Guid character) => new Outcome(character, null);
            internal static Outcome Refuse(string errorCode) => new Outcome(Guid.Empty, errorCode);
        }

        /// <summary>
        /// Decides whether a caller may act as the character it claims.
        /// </summary>
        /// <param name="hasLiveSession">
        /// Whether the Security header resolved to a live session. False covers
        /// both a missing header and an expired token; the client's vocabulary has
        /// separate codes for those and we use them.
        /// </param>
        /// <param name="hasSecurityHeader">
        /// Whether a Security header was present at all. The client OMITS the
        /// header entirely rather than sending an empty one when it has no token
        /// (SocialRequest.DecorateRequest guards on null), so its absence is a
        /// meaningful, distinguishable state and deserves 'no_auth_token' rather
        /// than a generic auth failure.
        /// </param>
        /// <param name="claimedCharacterUid">The CharacterUid header, verbatim.</param>
        /// <param name="charactersOnAccount">Every character uid the session's account owns.</param>
        internal static Outcome Authorize(
            bool hasSecurityHeader,
            bool hasLiveSession,
            string? claimedCharacterUid,
            IReadOnlyCollection<Guid> charactersOnAccount)
        {
            if (!hasSecurityHeader) return Outcome.Refuse(SocialErrorCodes.NoAuthToken);
            if (!hasLiveSession) return Outcome.Refuse(SocialErrorCodes.AuthFailed);

            if (string.IsNullOrWhiteSpace(claimedCharacterUid))
            {
                return Outcome.Refuse(SocialErrorCodes.InvalidEntityId);
            }

            if (!Guid.TryParse(claimedCharacterUid, out Guid claimed))
            {
                return Outcome.Refuse(SocialErrorCodes.InvalidEntityId);
            }

            // The load-bearing line. Ownership, not shape.
            foreach (Guid owned in charactersOnAccount)
            {
                if (owned == claimed) return Outcome.Allow(claimed);
            }

            return Outcome.Refuse(SocialErrorCodes.AuthFailed);
        }

        /// <summary>
        /// Whether a request may read another character's data.
        ///
        /// The client only ever asks about itself on the crew path, but the
        /// endpoints are shaped to take any uid, so the question has to be
        /// answered rather than assumed. Reads of a DIFFERENT character are
        /// allowed - a crew member list is inherently other people's names, and
        /// the retail UI shows exactly that - while WRITES are checked against the
        /// authorised character by their own call sites.
        ///
        /// This exists as a named function, rather than as an absent check, so
        /// that "reads are public" is a decision somebody made once and can find,
        /// instead of an oversight.
        /// </summary>
        internal static bool MayRead(Guid authorized, Guid subject)
        {
            _ = authorized;
            _ = subject;
            return true;
        }
    }
}
