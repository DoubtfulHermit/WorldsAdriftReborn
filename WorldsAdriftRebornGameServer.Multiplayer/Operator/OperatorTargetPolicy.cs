namespace WorldsAdriftRebornGameServer.Multiplayer.Operator
{
    /// <summary>
    /// How an operator names ONE player, and what happens when that name does not
    /// pick out exactly one.
    ///
    /// There are three identifiers for a player in this server and they have
    /// different lifetimes, which is the whole reason this type exists rather than
    /// a long parameter:
    ///
    ///  * <see cref="OperatorTargetKind.CharacterUid"/> is DURABLE. It is the same
    ///    value across sessions, reboots and reconnects; it is the key ship
    ///    ownership, inventory and the shipyard registration all join on.
    ///  * <see cref="OperatorTargetKind.EntityId"/> and
    ///    <see cref="OperatorTargetKind.PeerId"/> are PER-SESSION. They are what
    ///    the stats file publishes and what a dashboard row naturally carries, and
    ///    they are recycled: entity 7 after a reconnect is a different human than
    ///    entity 7 was a minute ago.
    ///  * <see cref="OperatorTargetKind.CharacterName"/> is durable but NOT
    ///    guaranteed unique, and is resolved on the login server (which owns the
    ///    character table) into a uid before anything is sent to the game server.
    ///
    /// So the rule this file encodes is: an operator command carries the selector
    /// the operator typed, resolution happens against a roster snapshot taken at
    /// execution time, and a selector that matches zero OR MORE THAN ONE row is
    /// REFUSED with the reason rather than resolved to "the first one". Acting on
    /// the wrong player is the failure mode that matters here; nothing in this file
    /// is allowed to guess.
    ///
    /// Pure: strings and a roster in, a decision out. No sockets, no clock, no
    /// database.
    /// </summary>
    public enum OperatorTargetKind
    {
        /// <summary>Unparseable. Never resolves.</summary>
        None = 0,

        /// <summary>A character uid GUID. Durable across sessions.</summary>
        CharacterUid,

        /// <summary>A player entity id. Valid only for this session.</summary>
        EntityId,

        /// <summary>An ENet peer handle, as the stats file's "0x..." string. Session-only.</summary>
        PeerId,

        /// <summary>
        /// A character screen name. Durable but not unique; the login server turns
        /// it into a uid before dispatch, so the game server never sees this kind.
        /// </summary>
        CharacterName,
    }

    /// <summary>One parsed operator target selector.</summary>
    public readonly record struct OperatorTarget(OperatorTargetKind Kind, string Value)
    {
        /// <summary>
        /// The canonical single-token wire form, always with its explicit prefix.
        /// An unprefixed selector is accepted from a human but never emitted: the
        /// wire is read by a machine and must not depend on shape-sniffing.
        /// </summary>
        public string ToSelector() => Kind switch
        {
            OperatorTargetKind.CharacterUid => "uid:" + Value,
            OperatorTargetKind.EntityId => "entity:" + Value,
            OperatorTargetKind.PeerId => "peer:" + Value,
            OperatorTargetKind.CharacterName => "name:" + Value,
            _ => string.Empty,
        };

        public override string ToString() => ToSelector();
    }

    /// <summary>
    /// One row of the live roster a target is resolved against. Deliberately a
    /// plain value with no engine types so the resolution rules can be asserted
    /// natively; the game server projects its real player table into these and the
    /// login server projects the stats file into the same shape.
    /// </summary>
    public readonly record struct OperatorPlayer(
        long EntityId,
        string PeerId,
        string CharacterUid,
        bool HasPosition,
        double X,
        double Y,
        double Z);

    /// <summary>Why a target could not be acted on, in words an operator can act on.</summary>
    public enum OperatorTargetFailure
    {
        None = 0,

        /// <summary>The text was not a selector at all.</summary>
        Unparseable,

        /// <summary>A well-formed selector that matches nobody currently in world.</summary>
        NotFound,

        /// <summary>A well-formed selector that matches more than one player.</summary>
        Ambiguous,

        /// <summary>
        /// A name selector reached the resolver. Names are resolved to a uid by the
        /// login server; the game server has no character table and must not guess.
        /// </summary>
        NameNotResolvable,
    }

    /// <summary>The outcome of resolving one selector against one roster.</summary>
    public readonly record struct OperatorTargetResolution(
        bool Resolved,
        OperatorPlayer Player,
        OperatorTargetFailure Failure,
        string Reason)
    {
        public static OperatorTargetResolution Ok(OperatorPlayer player) =>
            new OperatorTargetResolution(true, player, OperatorTargetFailure.None, string.Empty);

        public static OperatorTargetResolution Fail(OperatorTargetFailure failure, string reason) =>
            new OperatorTargetResolution(false, default, failure, reason);
    }

    public static class OperatorTargetPolicy
    {
        /// <summary>
        /// Parses one selector token.
        ///
        /// Accepted, in order of preference:
        /// <code>
        ///   uid:&lt;guid&gt;        durable character uid
        ///   entity:&lt;n&gt;        session player entity id
        ///   peer:0x&lt;hex&gt;      session ENet peer handle
        ///   name:&lt;text&gt;       character screen name (login server resolves)
        ///   &lt;guid&gt;            bare uid, unambiguous by shape
        ///   &lt;n&gt;               bare positive integer, read as an ENTITY id
        /// </code>
        ///
        /// A bare integer means ENTITY and not peer, because that is what the
        /// existing bridge already meant by an unqualified number and what the
        /// stats rows lead with. A bare anything-else is refused rather than
        /// guessed at as a name: "Bob" and a typo'd uid look identical from here,
        /// and one of those two readings moves the wrong human.
        /// </summary>
        public static bool TryParse(string? raw, out OperatorTarget target, out string error)
        {
            target = default;
            error = string.Empty;

            string text = (raw ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                error = "No target was given. Name a player with uid:<guid>, entity:<id>, "
                    + "peer:0x<hex> or name:<character name>.";
                return false;
            }

            int colon = text.IndexOf(':');
            if (colon > 0)
            {
                string prefix = text.Substring(0, colon).Trim().ToLowerInvariant();
                string value = text.Substring(colon + 1).Trim();

                switch (prefix)
                {
                    case "uid":
                    case "character":
                        if (!Guid.TryParse(value, out Guid uid))
                        {
                            error = "'" + value + "' is not a character uid.";
                            return false;
                        }
                        target = new OperatorTarget(
                            OperatorTargetKind.CharacterUid, CanonicalUid(uid));
                        return true;

                    case "entity":
                        if (!long.TryParse(value, out long entityId) || entityId <= 0)
                        {
                            error = "'" + value + "' is not a player entity id.";
                            return false;
                        }
                        target = new OperatorTarget(
                            OperatorTargetKind.EntityId,
                            entityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        return true;

                    case "peer":
                        if (!TryCanonicalPeer(value, out string peer))
                        {
                            error = "'" + value + "' is not a peer handle; expected 0x followed by hex.";
                            return false;
                        }
                        target = new OperatorTarget(OperatorTargetKind.PeerId, peer);
                        return true;

                    case "name":
                        if (value.Length == 0)
                        {
                            error = "No character name was given.";
                            return false;
                        }
                        target = new OperatorTarget(OperatorTargetKind.CharacterName, value);
                        return true;
                }

                error = "'" + prefix + ":' is not a target kind; use uid:, entity:, peer: or name:.";
                return false;
            }

            if (Guid.TryParse(text, out Guid bareUid))
            {
                target = new OperatorTarget(OperatorTargetKind.CharacterUid, CanonicalUid(bareUid));
                return true;
            }

            if (long.TryParse(text, out long bareEntity) && bareEntity > 0)
            {
                target = new OperatorTarget(
                    OperatorTargetKind.EntityId,
                    bareEntity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }

            error = "'" + text + "' is not a target. Prefix it: uid:<guid>, entity:<id>, "
                + "peer:0x<hex> or name:<character name>.";
            return false;
        }

        /// <summary>
        /// Resolves a parsed selector against a roster snapshot.
        ///
        /// Every kind refuses on zero matches and on more than one, and the two
        /// refusals say different things because they need different fixes: NOT
        /// FOUND means refresh the list, AMBIGUOUS means say which one.
        /// </summary>
        public static OperatorTargetResolution Resolve(
            OperatorTarget target,
            IReadOnlyList<OperatorPlayer> roster)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            switch (target.Kind)
            {
                case OperatorTargetKind.CharacterUid:
                    return Single(
                        roster,
                        player => string.Equals(
                            CanonicalUidText(player.CharacterUid), target.Value,
                            StringComparison.Ordinal),
                        "character uid " + target.Value);

                case OperatorTargetKind.EntityId:
                    return Single(
                        roster,
                        player => player.EntityId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture) == target.Value,
                        "player entity " + target.Value);

                case OperatorTargetKind.PeerId:
                    return Single(
                        roster,
                        player => TryCanonicalPeer(player.PeerId, out string canonical)
                            && canonical == target.Value,
                        "peer " + target.Value);

                case OperatorTargetKind.CharacterName:
                    return OperatorTargetResolution.Fail(
                        OperatorTargetFailure.NameNotResolvable,
                        "A character name must be turned into a uid before dispatch; "
                        + "the game server has no character table.");

                default:
                    return OperatorTargetResolution.Fail(
                        OperatorTargetFailure.Unparseable, "That target could not be read.");
            }
        }

        private static OperatorTargetResolution Single(
            IReadOnlyList<OperatorPlayer> roster,
            Func<OperatorPlayer, bool> matches,
            string described)
        {
            OperatorPlayer found = default;
            int count = 0;
            foreach (OperatorPlayer player in roster)
            {
                if (!matches(player)) continue;
                count++;
                if (count == 1) found = player;
            }

            if (count == 1) return OperatorTargetResolution.Ok(found);

            if (count == 0)
            {
                return OperatorTargetResolution.Fail(
                    OperatorTargetFailure.NotFound,
                    "No player in world matches " + described
                    + "; refresh the player list and choose again.");
            }

            return OperatorTargetResolution.Fail(
                OperatorTargetFailure.Ambiguous,
                count + " players match " + described
                + "; name one exactly with entity:<id> instead.");
        }

        /// <summary>The canonical lower-case dashed uid text this server compares on.</summary>
        public static string CanonicalUid(Guid uid) => uid.ToString("D").ToLowerInvariant();

        /// <summary>
        /// The canonical form of a uid that arrived as text, or "" when it is not a
        /// uid at all. A player row with no uid (a volatile session key, before the
        /// character uid has arrived) must never accidentally equal another one, so
        /// "" is returned and "" is compared with Ordinal - two blank uids do match
        /// each other, which is why the AMBIGUOUS refusal above matters.
        /// </summary>
        public static string CanonicalUidText(string? uid) =>
            Guid.TryParse((uid ?? string.Empty).Trim(), out Guid parsed)
                ? CanonicalUid(parsed)
                : string.Empty;

        /// <summary>
        /// The canonical "0x" + lower-case hex peer form, matching what the stats
        /// file writes and what the server logs print.
        /// </summary>
        public static bool TryCanonicalPeer(string? raw, out string canonical)
        {
            canonical = string.Empty;
            string text = (raw ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }
            if (text.Length == 0 || text.Length > 16) return false;

            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }

            canonical = "0x" + text.ToLowerInvariant().TrimStart('0');
            if (canonical == "0x") canonical = "0x0";
            return true;
        }
    }
}
