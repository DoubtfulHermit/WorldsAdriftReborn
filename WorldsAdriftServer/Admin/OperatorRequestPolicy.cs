using WorldsAdriftRebornGameServer.Multiplayer.Operator;

namespace WorldsAdriftServer.Admin
{
    /// <summary>The outcome of turning request parameters into a dispatchable command.</summary>
    internal readonly struct OperatorRequestOutcome
    {
        internal bool Ok { get; }
        internal OperatorCommand Command { get; }
        internal string Code { get; }
        internal string Reason { get; }

        private OperatorRequestOutcome(bool ok, OperatorCommand command, string code, string reason)
        {
            Ok = ok;
            Command = command;
            Code = code;
            Reason = reason;
        }

        internal static OperatorRequestOutcome Accept(OperatorCommand command) =>
            new OperatorRequestOutcome(true, command, string.Empty, string.Empty);

        internal static OperatorRequestOutcome Refuse(string code, string reason) =>
            new OperatorRequestOutcome(false, default, code, reason);
    }

    /// <summary>
    /// Turns the two or three strings an operator endpoint receives into one fully
    /// specified <see cref="OperatorCommand"/>, or into the refusal that says which
    /// of them was wrong.
    ///
    /// NAME RESOLUTION HAPPENS HERE AND NOWHERE ELSE. A character name is durable
    /// but not unique, and only this process has the character table; the game
    /// server has no way to answer "who is Bligh" and must never be asked to guess.
    /// So a <c>name:</c> selector is resolved to a uid on this side and the uid is
    /// what crosses to the game server - which is also why
    /// <see cref="OperatorCommandWire"/> refuses a name on the wire outright. The
    /// lookup arrives as a delegate rather than as a repository so this stays pure
    /// and every one of its outcomes (found, missing, several) can be asserted
    /// without a database.
    ///
    /// The DESTINATION player of a "teleport A to B" is deliberately NOT resolved
    /// here. It is carried through as a selector and resolved on the game server
    /// against the live roster, because where B is standing is a fact only that
    /// process has, and resolving B here would resolve it against a status file
    /// that is up to a second old.
    /// </summary>
    internal static class OperatorRequestPolicy
    {
        /// <summary>
        /// How a character name is looked up. Returns the canonical uid, or null
        /// for "no such name", or throws nothing - an ambiguous name is the
        /// caller's problem to represent, and the repository this wraps returns at
        /// most one row.
        /// </summary>
        internal delegate string? NameLookup(string characterName);

        internal static OperatorRequestOutcome BuildTeleport(
            string? target, string? destination, NameLookup? lookup)
        {
            OperatorRequestOutcome resolved = ResolveTarget(target, lookup);
            if (!resolved.Ok) return resolved;

            if (!OperatorDestinationPolicy.TryParse(
                    destination, out OperatorDestinationSpec spec, out string error))
            {
                return OperatorRequestOutcome.Refuse(OperatorErrorCodes.BadTarget, error);
            }

            // A named island is checked against the catalogue HERE, not only at
            // dispatch. A typo'd island name would otherwise be accepted, written
            // to the bridge, and refused a quarter of a second later in a result
            // file - which is a much worse place for an operator to read a typo.
            if (spec.Kind == OperatorDestinationKind.Island
                && !OperatorDestinationPolicy.TryFindIsland(spec.Value, out _, out string islandError))
            {
                return OperatorRequestOutcome.Refuse(OperatorErrorCodes.BadTarget, islandError);
            }

            if (spec.Kind == OperatorDestinationKind.Player
                && !OperatorTargetPolicy.TryParse(spec.Value, out _, out string playerError))
            {
                return OperatorRequestOutcome.Refuse(
                    OperatorErrorCodes.BadTarget,
                    "The destination player could not be read: " + playerError);
            }

            return OperatorRequestOutcome.Accept(
                OperatorCommand.Teleport(TargetOf(resolved), spec));
        }

        internal static OperatorRequestOutcome BuildSummonShip(
            string? target, string? hull, NameLookup? lookup)
        {
            OperatorRequestOutcome resolved = ResolveTarget(target, lookup);
            if (!resolved.Ok) return resolved;

            // An absent hull means "the ship they own". It is the default because
            // it is the request the operator actually has ("summon a ship for
            // them"), and because it is the only form that cannot summon the wrong
            // person's ship by mistyping a number.
            string selector = string.IsNullOrWhiteSpace(hull) ? "owned" : hull!;

            if (!OperatorCommandWire.TryParseHull(
                    selector, out OperatorHullSelector parsed, out string error))
            {
                return OperatorRequestOutcome.Refuse(OperatorErrorCodes.BadTarget, error);
            }

            return OperatorRequestOutcome.Accept(
                OperatorCommand.SummonShip(TargetOf(resolved), parsed));
        }

        /// <summary>
        /// Parses the target selector and, when it is a name, turns it into a uid.
        /// The successful outcome carries the resolved target on a placeholder
        /// teleport command; <see cref="TargetOf"/> reads it back.
        /// </summary>
        private static OperatorRequestOutcome ResolveTarget(string? target, NameLookup? lookup)
        {
            if (!OperatorTargetPolicy.TryParse(target, out OperatorTarget parsed, out string error))
            {
                return OperatorRequestOutcome.Refuse(OperatorErrorCodes.BadTarget, error);
            }

            if (parsed.Kind != OperatorTargetKind.CharacterName)
            {
                return OperatorRequestOutcome.Accept(
                    OperatorCommand.Teleport(parsed, OperatorDestinationSpec.SpawnSpec));
            }

            if (lookup == null)
            {
                return OperatorRequestOutcome.Refuse(
                    OperatorErrorCodes.GameUnavailable,
                    "The character store is not reachable, so a name cannot be looked up. "
                    + "Use uid:<guid> or entity:<id>.");
            }

            string? uid = lookup(parsed.Value);
            if (string.IsNullOrWhiteSpace(uid))
            {
                return OperatorRequestOutcome.Refuse(
                    OperatorErrorCodes.TargetNotFound,
                    "No character is named '" + parsed.Value + "'.");
            }

            if (!OperatorTargetPolicy.TryParse("uid:" + uid, out OperatorTarget byUid, out error))
            {
                return OperatorRequestOutcome.Refuse(
                    OperatorErrorCodes.BadTarget,
                    "The character store returned an unusable uid for '" + parsed.Value + "': " + error);
            }

            return OperatorRequestOutcome.Accept(
                OperatorCommand.Teleport(byUid, OperatorDestinationSpec.SpawnSpec));
        }

        private static OperatorTarget TargetOf(OperatorRequestOutcome outcome) =>
            outcome.Command.Target;
    }
}
