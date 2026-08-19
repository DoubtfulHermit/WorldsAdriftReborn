using System.Globalization;
using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Portal
{
    /// <summary>
    /// Why a portal form was refused before anything was looked up.
    ///
    /// These are the SHAPE refusals only - a missing field, an id that is not a
    /// GUID, a word the form does not have. Whether the player is ALLOWED to do
    /// what a well-formed post asks is <see cref="PortalPermissions"/>'s question
    /// and is deliberately not mixed in here: a form that decided permissions
    /// would be a second place permissions are decided.
    /// </summary>
    internal enum PortalFormFault
    {
        None,
        MissingField,
        NotAnId,
        UnknownAction,
        TooLong,
    }

    /// <summary>What a well-formed alliance-details post asked for.</summary>
    internal sealed record DetailsForm(
        PortalFormFault Fault,
        Guid AllianceId,
        Guid CharacterUid,
        string Description,
        string MessageOfTheDay)
    {
        public bool Ok => Fault == PortalFormFault.None;

        public static DetailsForm Bad(PortalFormFault fault) =>
            new DetailsForm(fault, Guid.Empty, Guid.Empty, string.Empty, string.Empty);
    }

    /// <summary>Which of the two things a member post does.</summary>
    internal enum MemberVerb
    {
        SetRank,
        Boot,
    }

    /// <summary>What a well-formed member post asked for.</summary>
    internal sealed record MemberForm(
        PortalFormFault Fault,
        MemberVerb Verb,
        Guid AllianceId,
        Guid CharacterUid,
        Guid TargetUid,
        Guid RankId)
    {
        public bool Ok => Fault == PortalFormFault.None;

        public static MemberForm Bad(PortalFormFault fault) =>
            new MemberForm(fault, MemberVerb.Boot, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
    }

    /// <summary>Which of the three things a request post does.</summary>
    internal enum RequestVerb
    {
        /// <summary>Seat an applicant.</summary>
        Accept,

        /// <summary>Turn an applicant away.</summary>
        Reject,

        /// <summary>Take back an invite the alliance sent.</summary>
        Rescind,
    }

    /// <summary>What a well-formed invite/application post asked for.</summary>
    internal sealed record RequestForm(
        PortalFormFault Fault,
        RequestVerb Verb,
        Guid AllianceId,
        Guid CharacterUid,
        string InviteId)
    {
        public bool Ok => Fault == PortalFormFault.None;

        public static RequestForm Bad(PortalFormFault fault) =>
            new RequestForm(fault, RequestVerb.Reject, Guid.Empty, Guid.Empty, string.Empty);
    }

    /// <summary>
    /// Reads the portal's management forms.
    ///
    /// The sibling of <see cref="Emblems.EmblemFormPolicy"/> and written to the
    /// same rule: the handler does sockets and rows, this does strings, and
    /// nothing here touches a database or a clock so every refusal can be
    /// asserted without one.
    ///
    /// EVERY FORM CARRIES A CHARACTER, and that is the field that matters. The
    /// session is per ACCOUNT and alliance permissions are per CHARACTER, so the
    /// post has to say which of the account's characters is acting - and the
    /// handler then checks that character against the account's own roster before
    /// it is used as an actor. Reading it here rather than inferring it means the
    /// check has something to check.
    /// </summary>
    internal static class PortalFormPolicy
    {
        internal const string AllianceField = "alliance";
        internal const string CharacterField = "character";
        internal const string TargetField = "target";
        internal const string RankField = "rank";
        internal const string InviteField = "invite";
        internal const string ActionField = "action";
        internal const string DescriptionField = "description";
        internal const string MotdField = "motd";

        /// <summary>
        /// The longest description or MOTD the portal will store.
        ///
        /// WAREBORN TUNING. The client's own fields are free text with no length
        /// the decompile states, and the columns are unbounded text, so there is
        /// nothing to recover. A bound exists anyway because these two strings are
        /// echoed to every member of the alliance on every social read, and an
        /// unbounded one is an unbounded response body. Generous enough that no
        /// honest description hits it.
        /// </summary>
        internal const int MaxTextLength = 2000;

        internal static DetailsForm ReadDetails(IReadOnlyDictionary<string, string> form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            if (!TryId(form, AllianceField, out Guid alliance)) return DetailsForm.Bad(PortalFormFault.NotAnId);
            if (!TryId(form, CharacterField, out Guid character)) return DetailsForm.Bad(PortalFormFault.NotAnId);

            // Absent is NOT the same as empty. A field the form did not send must
            // leave the stored value alone - the two are edited under different
            // permissions, and treating a missing key as "" would let somebody
            // with one permission blank the other field.
            string? description = Value(form, DescriptionField);
            string? motd = Value(form, MotdField);

            if (description == null && motd == null) return DetailsForm.Bad(PortalFormFault.MissingField);

            if ((description?.Length ?? 0) > MaxTextLength || (motd?.Length ?? 0) > MaxTextLength)
            {
                return DetailsForm.Bad(PortalFormFault.TooLong);
            }

            return new DetailsForm(
                PortalFormFault.None, alliance, character,
                description ?? string.Empty, motd ?? string.Empty);
        }

        /// <summary>Whether a details post carried the field at all.</summary>
        internal static bool Sent(IReadOnlyDictionary<string, string> form, string field) =>
            form != null && form.ContainsKey(field);

        internal static MemberForm ReadMember(IReadOnlyDictionary<string, string> form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            string? action = Value(form, ActionField);
            MemberVerb verb;

            if (string.Equals(action, "rank", StringComparison.Ordinal)) verb = MemberVerb.SetRank;
            else if (string.Equals(action, "boot", StringComparison.Ordinal)) verb = MemberVerb.Boot;
            else return MemberForm.Bad(PortalFormFault.UnknownAction);

            if (!TryId(form, AllianceField, out Guid alliance)) return MemberForm.Bad(PortalFormFault.NotAnId);
            if (!TryId(form, CharacterField, out Guid character)) return MemberForm.Bad(PortalFormFault.NotAnId);
            if (!TryId(form, TargetField, out Guid target)) return MemberForm.Bad(PortalFormFault.NotAnId);

            Guid rank = Guid.Empty;
            if (verb == MemberVerb.SetRank && !TryId(form, RankField, out rank))
            {
                return MemberForm.Bad(PortalFormFault.NotAnId);
            }

            return new MemberForm(PortalFormFault.None, verb, alliance, character, target, rank);
        }

        internal static RequestForm ReadRequest(IReadOnlyDictionary<string, string> form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));

            string? action = Value(form, ActionField);
            RequestVerb verb;

            if (string.Equals(action, "accept", StringComparison.Ordinal)) verb = RequestVerb.Accept;
            else if (string.Equals(action, "reject", StringComparison.Ordinal)) verb = RequestVerb.Reject;
            else if (string.Equals(action, "rescind", StringComparison.Ordinal)) verb = RequestVerb.Rescind;
            else return RequestForm.Bad(PortalFormFault.UnknownAction);

            if (!TryId(form, AllianceField, out Guid alliance)) return RequestForm.Bad(PortalFormFault.NotAnId);
            if (!TryId(form, CharacterField, out Guid character)) return RequestForm.Bad(PortalFormFault.NotAnId);

            string? invite = Value(form, InviteField);
            if (string.IsNullOrWhiteSpace(invite)) return RequestForm.Bad(PortalFormFault.MissingField);

            // Not a GUID: an invite id is "invite:{guid}", the shape the invite
            // store mints and the only shape it can be looked up by.
            return new RequestForm(PortalFormFault.None, verb, alliance, character, invite!);
        }

        private static bool TryId(IReadOnlyDictionary<string, string> form, string field, out Guid id)
        {
            id = Guid.Empty;
            string? raw = Value(form, field);
            return raw != null
                && Guid.TryParseExact(raw.Trim(), "D", out id);
        }

        private static string? Value(IReadOnlyDictionary<string, string> form, string field) =>
            form.TryGetValue(field, out string? value) ? value : null;
    }

    /// <summary>Why a password change was refused, before the database was asked.</summary>
    internal enum PasswordChangeFault
    {
        None,

        /// <summary>One of the three boxes was empty.</summary>
        Missing,

        /// <summary>The two copies of the new password differ.</summary>
        Mismatch,

        /// <summary>The new password is one <see cref="AccountPolicy"/> refuses to store.</summary>
        TooWeak,

        /// <summary>The "new" password is the one already in use.</summary>
        Unchanged,
    }

    /// <summary>
    /// Whether a password change is well-formed. Pure, and deliberately blind to
    /// whether the CURRENT password is right: that answer costs a PBKDF2 and lives
    /// in the repository, and a rule module that could tell them apart would have
    /// to be handed a hash.
    ///
    /// The weakness check is <see cref="AccountPolicy.IsUsablePassword"/>, the same
    /// one sign-up applies. A second opinion here would let a password through the
    /// portal that the sign-up page refuses, or refuse one it accepts - and either
    /// way a player would meet a rule that exists in exactly one screen.
    /// </summary>
    internal static class PasswordChangePolicy
    {
        internal const string CurrentField = "current";
        internal const string NextField = "next";
        internal const string ConfirmField = "confirm";

        internal static PasswordChangeFault Check(string? current, string? next, string? confirm)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next) || string.IsNullOrEmpty(confirm))
            {
                return PasswordChangeFault.Missing;
            }

            if (!string.Equals(next, confirm, StringComparison.Ordinal))
            {
                return PasswordChangeFault.Mismatch;
            }

            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                // Refused rather than quietly succeeding. A change that changed
                // nothing looks identical to one that worked, and a player who
                // suspects their password is known needs to be sure they moved it.
                return PasswordChangeFault.Unchanged;
            }

            return AccountPolicy.IsUsablePassword(next)
                ? PasswordChangeFault.None
                : PasswordChangeFault.TooWeak;
        }

        /// <summary>The sentence the portal shows for a fault. One place, so the
        /// page and the redirect notice cannot word the same refusal twice.</summary>
        internal static string Explain(PasswordChangeFault fault) => fault switch
        {
            PasswordChangeFault.None => "Password changed.",
            PasswordChangeFault.Missing => "Fill in all three boxes.",
            PasswordChangeFault.Mismatch => "The two new passwords do not match.",
            PasswordChangeFault.Unchanged => "That is the password you already have.",
            PasswordChangeFault.TooWeak =>
                "That password needs between "
                + AccountPolicy.MinPasswordLength.ToString(CultureInfo.InvariantCulture)
                + " and "
                + AccountPolicy.MaxPasswordLength.ToString(CultureInfo.InvariantCulture)
                + " characters.",
            _ => "That password could not be changed.",
        };
    }
}
