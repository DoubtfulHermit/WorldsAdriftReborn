namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Turns the emblem builder's posted form into either a target and a spec, or
    /// a reason it is not one.
    ///
    /// It exists as its own type for the reason every other <c>*Policy</c> in this
    /// server does: the handler needs an HttpSession, a cookie, a live account and
    /// a database to run at all, so anything decided inside it is decided behind a
    /// seam no test can reach. Here the decision is pure - a dictionary in, an
    /// outcome out - so every malformed, missing, out-of-range and hostile field
    /// combination is assertable with no socket.
    ///
    /// There is very little to decide, and that is the design working. Because an
    /// emblem is six indices into closed tables, "validate the input" is a bounds
    /// check; there is no file to sniff, no MIME type to whitelist, no XML to
    /// strip entities from, no path to keep inside a directory and no remote host
    /// to refuse. The two GUIDs are the only free-form values on the whole form
    /// and both are parsed, not pattern-matched.
    /// </summary>
    internal static class EmblemFormPolicy
    {
        internal readonly struct Outcome
        {
            internal bool Ok { get; }

            /// <summary>Why it was refused. Empty when <see cref="Ok"/>.</summary>
            internal string Reason { get; }

            /// <summary>Which alliance the change is aimed at.</summary>
            internal Guid AllianceId { get; }

            /// <summary>
            /// Which of the account's characters is acting.
            ///
            /// Required rather than inferred, because alliance membership is per
            /// CHARACTER and an account holds up to five of them. Inferring "the
            /// one that happens to be in an alliance" would silently pick a
            /// different actor the day a player's second character joined one.
            /// </summary>
            internal Guid CharacterUid { get; }

            internal EmblemSpec Spec { get; }

            private Outcome(bool ok, string reason, Guid allianceId, Guid characterUid, EmblemSpec spec)
            {
                Ok = ok;
                Reason = reason;
                AllianceId = allianceId;
                CharacterUid = characterUid;
                Spec = spec;
            }

            internal static Outcome Accept(Guid allianceId, Guid characterUid, EmblemSpec spec) =>
                new Outcome(true, string.Empty, allianceId, characterUid, spec);

            internal static Outcome Refuse(string reason) =>
                new Outcome(false, reason, Guid.Empty, Guid.Empty, default);
        }

        internal const string AllianceField = "alliance";
        internal const string CharacterField = "character";
        internal const string ShapeField = "shape";
        internal const string DivisionField = "division";
        internal const string ChargeField = "charge";
        internal const string FieldColourField = "field";
        internal const string DetailColourField = "detail";
        internal const string ChargeColourField = "chargeColour";

        internal static Outcome Read(IReadOnlyDictionary<string, string>? form)
        {
            if (form == null) return Outcome.Refuse("The form was empty.");

            if (!TryGuid(form, AllianceField, out Guid allianceId))
            {
                return Outcome.Refuse("That alliance id is not readable.");
            }

            if (!TryGuid(form, CharacterField, out Guid characterUid))
            {
                return Outcome.Refuse("That character id is not readable.");
            }

            if (!TryIndex(form, ShapeField, out int shape)
                || !TryIndex(form, DivisionField, out int division)
                || !TryIndex(form, ChargeField, out int charge)
                || !TryIndex(form, FieldColourField, out int field)
                || !TryIndex(form, DetailColourField, out int detail)
                || !TryIndex(form, ChargeColourField, out int chargeColour))
            {
                return Outcome.Refuse("One of the emblem choices was missing or not a number.");
            }

            if (!EmblemSpec.TryCreate(shape, division, charge, field, detail, chargeColour,
                    out EmblemSpec spec))
            {
                // Refused, not clamped: an out-of-range index means the form did
                // not come from the builder, and quietly substituting a valid
                // choice would look like the builder ignoring what was picked.
                return Outcome.Refuse("One of the emblem choices is not one this builder offers.");
            }

            return Outcome.Accept(allianceId, characterUid, spec);
        }

        private static bool TryGuid(IReadOnlyDictionary<string, string> form, string key, out Guid value)
        {
            value = Guid.Empty;
            return form.TryGetValue(key, out string? text)
                && Guid.TryParse(text, out value)
                && value != Guid.Empty;
        }

        private static bool TryIndex(IReadOnlyDictionary<string, string> form, string key, out int value)
        {
            value = -1;

            if (!form.TryGetValue(key, out string? text)) return false;
            if (text == null || text.Length == 0 || text.Length > 4) return false;

            return int.TryParse(
                text,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }
    }
}
