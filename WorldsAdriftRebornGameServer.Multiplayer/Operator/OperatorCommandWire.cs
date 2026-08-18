namespace WorldsAdriftRebornGameServer.Multiplayer.Operator
{
    public enum OperatorCommandKind
    {
        None = 0,

        /// <summary>Move one named player to one named place.</summary>
        Teleport,

        /// <summary>Bring a ship to one named player.</summary>
        SummonShip,
    }

    /// <summary>Which hull a summon means.</summary>
    public enum OperatorHullKind
    {
        None = 0,

        /// <summary>An exact hull entity id.</summary>
        Hull,

        /// <summary>
        /// "the ship this player owns" - resolved at execution time against the
        /// live owner index, and refused when it is not exactly one ship.
        /// </summary>
        Owned,
    }

    public readonly record struct OperatorHullSelector(OperatorHullKind Kind, long HullEntityId)
    {
        public static readonly OperatorHullSelector OwnedByTarget =
            new OperatorHullSelector(OperatorHullKind.Owned, 0);

        public static OperatorHullSelector Of(long hullEntityId) =>
            new OperatorHullSelector(OperatorHullKind.Hull, hullEntityId);

        public string ToSelector() => Kind switch
        {
            OperatorHullKind.Hull => "hull:"
                + HullEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OperatorHullKind.Owned => "owned",
            _ => string.Empty,
        };

        public override string ToString() => ToSelector();
    }

    /// <summary>One operator instruction, fully parsed and ready to execute.</summary>
    public readonly record struct OperatorCommand(
        OperatorCommandKind Kind,
        OperatorTarget Target,
        OperatorDestinationSpec Destination,
        OperatorHullSelector Hull)
    {
        public static OperatorCommand Teleport(OperatorTarget target, OperatorDestinationSpec destination) =>
            new OperatorCommand(OperatorCommandKind.Teleport, target, destination, default);

        public static OperatorCommand SummonShip(OperatorTarget target, OperatorHullSelector hull) =>
            new OperatorCommand(OperatorCommandKind.SummonShip, target, default, hull);
    }

    /// <summary>
    /// The EXPLICIT, VERSIONED line format the login server writes and the game
    /// server reads.
    ///
    /// WHY A VERSIONED LINE AND NOT MORE AD-HOC WORDS. The bridge that came before
    /// this one had one grammar per verb, written in
    /// <c>AdminCommandBridge.TryBuild</c> and read in
    /// <c>AdminWorldCommandPolicy.TryParse</c> - two files, two hand-rolled string
    /// formats, and nothing that made them agree except attention. This type is the
    /// single definition of the format: BOTH sides call it, one to
    /// <see cref="TryFormat"/> and one to <see cref="TryParse"/>, and a round-trip
    /// test pins that they are inverses. A field added here therefore cannot be
    /// added to one side only.
    ///
    /// The transport is still the existing one-shot trigger FILE. That was a
    /// deliberate scope decision and not an endorsement: the game server has no
    /// inbound socket of its own, adding one is a much larger change than this
    /// feature warrants, and the file already has a proven consume-once discipline
    /// (read-then-delete) plus a result file the operator surface reads back. What
    /// changed is that the format is now written down, versioned, and shared.
    ///
    /// THE PREFIX. Every line begins <c>wa-op/1</c>. The same file also carries the
    /// older unversioned verbs (<c>reset-resources all</c>, <c>recall-ship</c>, ...),
    /// so the reader tries this parser first and falls through to the legacy one -
    /// the prefix is what makes "is this a new-format line" a decision rather than
    /// a guess, and it is what lets a v2 be introduced later without a flag day.
    ///
    /// ESCAPING. Fields are separated by single spaces and each field is
    /// percent-encoded, because a character name can contain a space and an island
    /// display name usually does. Encoding is applied to the WHOLE field including
    /// its prefix, so a name containing a colon cannot forge a different selector
    /// kind.
    /// </summary>
    public static class OperatorCommandWire
    {
        /// <summary>The line prefix that marks this format and its version.</summary>
        public const string Prefix = "wa-op/1";

        public const string TeleportVerb = "teleport";
        public const string SummonShipVerb = "summon-ship";

        /// <summary>
        /// Renders a command as its wire line. Returns false for a command that is
        /// not fully specified, so a half-built instruction can never reach the
        /// file - an unparseable line on the game server is an operator action that
        /// silently did nothing.
        /// </summary>
        public static bool TryFormat(OperatorCommand command, out string line, out string error)
        {
            line = string.Empty;
            error = string.Empty;

            string target = command.Target.ToSelector();
            if (target.Length == 0)
            {
                error = "The command has no target.";
                return false;
            }

            switch (command.Kind)
            {
                case OperatorCommandKind.Teleport:
                {
                    string destination = command.Destination.ToSpec();
                    if (destination.Length == 0)
                    {
                        error = "The teleport has no destination.";
                        return false;
                    }
                    line = Prefix + " " + TeleportVerb + " " + Encode(target) + " " + Encode(destination);
                    return true;
                }

                case OperatorCommandKind.SummonShip:
                {
                    string hull = command.Hull.ToSelector();
                    if (hull.Length == 0)
                    {
                        error = "The summon names no ship.";
                        return false;
                    }
                    line = Prefix + " " + SummonShipVerb + " " + Encode(target) + " " + Encode(hull);
                    return true;
                }

                default:
                    error = "Unknown operator command.";
                    return false;
            }
        }

        /// <summary>
        /// Whether a line claims to be in this format at all. The reader uses this
        /// to decide between this parser and the legacy verbs; a line that claims
        /// the prefix and then fails to parse must be REPORTED, not silently
        /// retried as a legacy command.
        /// </summary>
        public static bool IsOperatorLine(string? line) =>
            (line ?? string.Empty).TrimStart().StartsWith(Prefix + " ", StringComparison.Ordinal);

        /// <summary>Parses one wire line back into a command.</summary>
        public static bool TryParse(string? line, out OperatorCommand command, out string error)
        {
            command = default;
            error = string.Empty;

            string text = (line ?? string.Empty).Trim();
            string[] fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                error = "empty line";
                return false;
            }

            if (fields[0] != Prefix)
            {
                error = "not a " + Prefix + " line";
                return false;
            }

            if (fields.Length != 4)
            {
                error = "expected '" + Prefix + " <verb> <target> <argument>', got "
                    + fields.Length + " fields";
                return false;
            }

            string verb = fields[1];
            string targetText = Decode(fields[2]);
            string argumentText = Decode(fields[3]);

            if (!OperatorTargetPolicy.TryParse(targetText, out OperatorTarget target, out error))
            {
                return false;
            }

            if (target.Kind == OperatorTargetKind.CharacterName)
            {
                // Names are resolved to a uid before dispatch. One reaching the
                // wire is a bug on the writing side, and it is refused here rather
                // than carried to a resolver that would only refuse it later with
                // less context.
                error = "a character name must be resolved to a uid before dispatch";
                return false;
            }

            switch (verb)
            {
                case TeleportVerb:
                    if (!OperatorDestinationPolicy.TryParse(
                            argumentText, out OperatorDestinationSpec destination, out error))
                    {
                        return false;
                    }
                    command = OperatorCommand.Teleport(target, destination);
                    return true;

                case SummonShipVerb:
                    if (!TryParseHull(argumentText, out OperatorHullSelector hull, out error))
                    {
                        return false;
                    }
                    command = OperatorCommand.SummonShip(target, hull);
                    return true;

                default:
                    error = "unknown verb '" + verb + "'; expected "
                        + TeleportVerb + " or " + SummonShipVerb;
                    return false;
            }
        }

        /// <summary>
        /// Parses a hull selector: <c>hull:&lt;id&gt;</c> for an exact ship, or
        /// <c>owned</c> for "whichever ship this player owns".
        /// </summary>
        public static bool TryParseHull(string? raw, out OperatorHullSelector hull, out string error)
        {
            hull = default;
            error = string.Empty;

            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "No ship was named. Use hull:<entityId> or 'owned'.";
                return false;
            }

            if (string.Equals(text, "owned", StringComparison.OrdinalIgnoreCase))
            {
                hull = OperatorHullSelector.OwnedByTarget;
                return true;
            }

            string value = text;
            if (text.StartsWith("hull:", StringComparison.OrdinalIgnoreCase))
            {
                value = text.Substring("hull:".Length).Trim();
            }

            if (!long.TryParse(value, out long hullEntityId) || hullEntityId <= 0)
            {
                error = "'" + text + "' is not a ship. Use hull:<entityId> or 'owned'.";
                return false;
            }

            hull = OperatorHullSelector.Of(hullEntityId);
            return true;
        }

        private static string Encode(string field) => Uri.EscapeDataString(field);

        private static string Decode(string field)
        {
            try
            {
                return Uri.UnescapeDataString(field);
            }
            catch (Exception)
            {
                // A malformed escape cannot be a valid selector either way; hand
                // the raw text on and let the selector parser produce the refusal
                // that actually names the field.
                return field;
            }
        }
    }
}
