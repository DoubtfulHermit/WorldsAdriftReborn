namespace WorldsAdriftServer.Portal
{
    /// <summary>
    /// The sentence the portal shows after a post, keyed by a short code the
    /// redirect carries in the query string.
    ///
    /// POST-REDIRECT-GET, so the code has to survive a round trip through a URL,
    /// which is why it is a code and not the sentence. A sentence in the query
    /// string would be a sentence an attacker could choose - a page that renders
    /// arbitrary text handed to it by a link is the oldest phishing surface there
    /// is, and "it is HTML-encoded" only stops the markup, not the lie. A closed
    /// vocabulary cannot say anything this file did not write.
    ///
    /// Pure, and every branch is a constant, so the whole table can be asserted -
    /// including the one that matters most, that an UNKNOWN code says nothing at
    /// all rather than falling through to a success.
    /// </summary>
    internal static class PortalNotices
    {
        /// <summary>The query-string key a redirect puts the code in.</summary>
        internal const string Field = "m";

        internal const string CrestSaved = "crest-saved";
        internal const string DescriptionSaved = "description-saved";
        internal const string MotdSaved = "motd-saved";
        internal const string RankSet = "rank-set";
        internal const string MemberRemoved = "member-removed";
        internal const string ApplicantAdmitted = "applicant-admitted";
        internal const string ApplicantDeclined = "applicant-declined";
        internal const string InviteWithdrawn = "invite-withdrawn";
        internal const string PasswordChanged = "password-changed";

        internal const string Expired = "expired";
        internal const string Unreadable = "unreadable";
        internal const string Denied = "denied";
        internal const string Failed = "failed";
        internal const string Gone = "gone";
        internal const string NoChange = "no-change";

        internal const string PasswordMissing = "pw-missing";
        internal const string PasswordMismatch = "pw-mismatch";
        internal const string PasswordUnchanged = "pw-unchanged";
        internal const string PasswordWeak = "pw-weak";
        internal const string PasswordWrong = "pw-wrong";

        /// <summary>
        /// The sentence and whether it is a refusal. Null text for a code this
        /// table does not know - a stray or hand-typed query parameter shows
        /// nothing, which is the only safe default: silence cannot be mistaken for
        /// a save that happened.
        /// </summary>
        internal static (string? Text, bool IsError) For(string? code) => code switch
        {
            CrestSaved => ("Crest saved. It appears in game the next time the alliance panel loads.", false),
            DescriptionSaved => ("Description saved.", false),
            MotdSaved => ("Message of the day saved.", false),
            RankSet => ("Rank changed.", false),
            MemberRemoved => ("That member has been removed from the alliance.", false),
            ApplicantAdmitted => ("They are in - the alliance has a new member.", false),
            ApplicantDeclined => ("Application declined.", false),
            InviteWithdrawn => ("Invitation withdrawn.", false),

            // Named as a consequence, not as a courtesy: the game client holds its
            // own long-lived token and it has just been thrown away, so the player
            // needs to know why the game asks them to sign in again.
            PasswordChanged => ("Password changed. Every other session, including the game client, has been signed out.", false),

            // Says that the CHOICES are gone, not just the form. The old wording
            // ("it has been reloaded - try again") reads as though the thing you
            // submitted is still sitting there, so a leader who had spent a while
            // composing a crest pressed save, saw "try again", and had no idea the
            // selection itself had been thrown away.
            Expired => ("That form had expired, so nothing was saved - leaving a page open"
                + " for a long time does this. The page has been reloaded; make your"
                + " choices again and save.", true),
            Unreadable => ("That request was not readable.", true),
            Denied => ("You do not have permission to do that.", true),
            Failed => ("That could not be saved. Try again shortly.", true),
            Gone => ("That is no longer there - somebody may have answered it first.", true),
            NoChange => ("Nothing changed.", true),

            PasswordMissing => (PasswordChangePolicy.Explain(PasswordChangeFault.Missing), true),
            PasswordMismatch => (PasswordChangePolicy.Explain(PasswordChangeFault.Mismatch), true),
            PasswordUnchanged => (PasswordChangePolicy.Explain(PasswordChangeFault.Unchanged), true),
            PasswordWeak => (PasswordChangePolicy.Explain(PasswordChangeFault.TooWeak), true),
            PasswordWrong => ("That is not your current password.", true),

            _ => (null, false),
        };

        /// <summary>The code for a password fault, so the check and the sentence
        /// cannot drift apart.</summary>
        internal static string CodeFor(PasswordChangeFault fault) => fault switch
        {
            PasswordChangeFault.None => PasswordChanged,
            PasswordChangeFault.Missing => PasswordMissing,
            PasswordChangeFault.Mismatch => PasswordMismatch,
            PasswordChangeFault.Unchanged => PasswordUnchanged,
            PasswordChangeFault.TooWeak => PasswordWeak,
            _ => Failed,
        };

        /// <summary>
        /// The code carried by a URL, or null. Reads only the LAST value of the
        /// key and only up to 40 characters, so a hand-made query string cannot
        /// hand the page an unbounded string to compare.
        /// </summary>
        internal static string? CodeFrom(string? url)
        {
            if (url == null) return null;

            int q = url.IndexOf('?');
            if (q < 0) return null;

            string? found = null;
            foreach (string pair in url.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pair.Substring(0, eq), Field, StringComparison.Ordinal)) continue;

                string value = pair.Substring(eq + 1);
                found = value.Length <= 40 ? value : null;
            }

            return found;
        }
    }
}
