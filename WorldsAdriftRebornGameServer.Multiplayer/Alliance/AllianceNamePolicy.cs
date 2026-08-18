using System.Text.RegularExpressions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Alliance
{
    /// <summary>
    /// Whether a string is a name this server will let an alliance have.
    ///
    /// The rules are RECOVERED from the client's own pre-flight check,
    /// <c>StringFormatHelper.CheckRules</c> (Travellers.UI.Utility, :138-176),
    /// which runs on CREATE before a single byte leaves the machine and throws
    /// <c>InvalidAllianceNameException</c> - a dialog, not a request. So in normal
    /// play the server never sees a name that breaks them.
    ///
    /// That is precisely why they are re-stated here rather than trusted. The
    /// client-side check is the only one that exists, and a request built by
    /// anything other than the retail UI - a replayed request, a modified client,
    /// a future tool of ours - reaches the endpoint unfiltered. A name that the
    /// client would refuse to type is a name the client may also refuse to
    /// RENDER sensibly, and it would be stored forever.
    ///
    /// The character rules are reproduced exactly. The LENGTH bounds are not, and
    /// cannot be: the client reads them from
    /// <c>SocialInputCharacterLimitsTable[ALLIANCE_NAME].MIN/MAX</c>, whose row
    /// data lived in Bossa's remote GameDB and is not in the shipped install. See
    /// <see cref="MinLength"/>.
    ///
    /// Pure: a string in, a verdict out. No storage, no uniqueness - "is this name
    /// taken" is a question for the store and answers with a different error code.
    /// </summary>
    public static class AllianceNamePolicy
    {
        /// <summary>
        /// WAREBORN TUNING, not recovered. Retail's minimum came from GameDB and
        /// left no trace in the decompile.
        ///
        /// Deliberately looser than any plausible retail value. The client checks
        /// first and shows the player the real bounds it read from GameDB, so the
        /// server's only job is to refuse what never went through that check. A
        /// server bound TIGHTER than the client's would reject a name the player
        /// was told was fine, which is the one failure mode worth avoiding.
        /// </summary>
        public const int MinLength = 1;

        /// <summary>WAREBORN TUNING, as above. Long enough that no client-accepted
        /// name can hit it, short enough to keep a row bounded.</summary>
        public const int MaxLength = 64;

        // Anchored to the whole string via IsMatch semantics identical to the
        // client's: it asks "does a forbidden character appear ANYWHERE", not "is
        // the whole string legal", and those differ for the empty string.
        private static readonly Regex ForbiddenCharacter = new Regex("[^A-Z a-z']", RegexOptions.Compiled);
        private static readonly Regex CapitalAfterLetter = new Regex("[a-z][A-Z]", RegexOptions.Compiled);
        private static readonly Regex DoubleSpace = new Regex("[ ][ ]", RegexOptions.Compiled);
        private static readonly Regex DoubleApostrophe = new Regex("['][']", RegexOptions.Compiled);

        /// <summary>
        /// True when the retail client would have let a player type this name.
        ///
        /// Note what is NOT here: trimming, case folding, or any repair. The client
        /// sends what the player typed and compares names by string elsewhere, so a
        /// name is either acceptable as written or it is refused. Silently
        /// "fixing" it would store something the player did not choose and did not
        /// see.
        /// </summary>
        public static bool IsAcceptable(string? name)
        {
            if (name == null) return false;
            if (name.Length < MinLength || name.Length > MaxLength) return false;

            if (ForbiddenCharacter.IsMatch(name)) return false;
            if (CapitalAfterLetter.IsMatch(name)) return false;

            // Leading and trailing checks are index-based in the client too, which
            // is safe there only because the length check returned early. Same
            // ordering is kept here for the same reason.
            if (name[0] == ' ' || name[name.Length - 1] == ' ') return false;
            if (name[0] == '\'' || name[name.Length - 1] == '\'') return false;

            if (DoubleSpace.IsMatch(name)) return false;
            if (DoubleApostrophe.IsMatch(name)) return false;

            return true;
        }

        /// <summary>
        /// The key two alliance names are compared on for uniqueness.
        ///
        /// Case-insensitive and culture-invariant. The client's error vocabulary
        /// has <c>duplicate_alliance_name</c>, so uniqueness is retail's rule, but
        /// nothing in the decompile says how it compared - the check lived in the
        /// dead service. Case-insensitive is CHOSEN (WAREBORN TUNING): two
        /// alliances called "Rat Corp" and "rat corp" are indistinguishable in a
        /// list that has no other identifier on it, and a player who joined the
        /// wrong one has no way to tell.
        ///
        /// Invariant rather than current-culture because the server's locale must
        /// not decide whether two players may share a name - this machine runs a
        /// German locale, where a Turkish-style dotted-I rule would not apply but a
        /// future one might.
        /// </summary>
        public static string UniquenessKey(string name) =>
            (name ?? string.Empty).ToLowerInvariant();
    }
}
