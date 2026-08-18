using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Operator;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE SEAM between an operator's command line and the machinery that already
    /// moves players and ships.
    ///
    /// It makes no decisions worth the name. WHO a selector means is
    /// <see cref="OperatorTargetPolicy"/>; WHERE a destination is is
    /// <see cref="OperatorDestinationPolicy"/> over the surveyed release catalogue;
    /// WHICH ship a summon brings is <see cref="OperatorSummonPolicy"/>; whether an
    /// arrival is safe yet is the terrain gate inside
    /// <see cref="TeleportService.DispatchTo"/>. All four are pure and tested
    /// natively. What is left here - reading the live player table, reading a uid
    /// off an entity, reading a stored row, calling the wire - is the part that
    /// cannot be tested without a server, which is exactly why it is kept this
    /// thin.
    ///
    /// EVERY PATH RETURNS A SENTENCE. The operator is reading the result over HTTP
    /// with no log in front of them, so "false" is never enough: a refusal that
    /// does not say what to do instead is a bug report waiting to be filed.
    /// </summary>
    internal static class OperatorCommandExecutor
    {
        /// <summary>The label operator-initiated teleports carry in the log, at both ends.</summary>
        internal const string TeleportReason = "operator-teleport";

        /// <summary>The label an operator ship summon carries in the log.</summary>
        internal const string SummonReason = "operator-summon";

        internal static bool Execute(OperatorCommand command, out string message)
        {
            switch (command.Kind)
            {
                case OperatorCommandKind.Teleport:
                    return Teleport(command, out message);
                case OperatorCommandKind.SummonShip:
                    return Summon(command, out message);
                default:
                    message = "Unsupported operator command.";
                    return false;
            }
        }

        // ---- roster --------------------------------------------------------

        /// <summary>
        /// The live player table, projected into the plain rows the pure resolution
        /// rules take. Built fresh per command on purpose: a roster cached even for
        /// a second is a roster in which somebody has already disconnected, and
        /// resolving against a stale one is how a command reaches the wrong body.
        /// </summary>
        internal static IReadOnlyList<OperatorPlayer> Roster()
        {
            List<OperatorPlayer> roster = new List<OperatorPlayer>();
            foreach ((ulong peerId, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                bool hasPosition = WorldsAdriftRebornGameServer.ResourceInterest.TryCenterFor(
                    peerId, out FixedPointPosition position);
                roster.Add(new OperatorPlayer(
                    entityId,
                    "0x" + peerId.ToString("x"),
                    CharacterOwnership.UidForEntity(entityId),
                    hasPosition,
                    hasPosition ? position.MetresX : 0.0,
                    hasPosition ? position.MetresY : 0.0,
                    hasPosition ? position.MetresZ : 0.0));
            }
            return roster;
        }

        private static ulong? PeerOf(long entityId)
        {
            foreach ((ulong peerId, long candidate) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (candidate == entityId) return peerId;
            }
            return null;
        }

        private static bool IsAboardAShip(long entityId)
        {
            ulong? peerId = PeerOf(entityId);
            return peerId.HasValue
                && WorldsAdriftRebornGameServer.Aboard.IsAboardAnything(peerId.Value);
        }

        // ---- teleport ------------------------------------------------------

        private static bool Teleport(OperatorCommand command, out string message)
        {
            IReadOnlyList<OperatorPlayer> roster = Roster();

            OperatorTargetResolution target = OperatorTargetPolicy.Resolve(command.Target, roster);
            if (!target.Resolved)
            {
                message = target.Reason;
                return false;
            }

            if (!TryDestination(command.Destination, target.Player, roster,
                    out TeleportDestination destination, out message))
            {
                return false;
            }

            // The same registration guard the trigger-file path applies, and for the
            // same reason: an island whose terrain entity was never spawned this
            // boot can only ever be a fall, and the deferral below would otherwise
            // spend its whole budget waiting for terrain that is never coming.
            if (!TeleportPolicy.RequiredTerrainIsRegistered(
                    destination,
                    key => WorldsAdriftRebornGameServer.WorldEntities.ByKey(key) != null))
            {
                message = "Island terrain '" + destination.RequiredWorldEntityKey
                    + "' is not registered on this boot, so nobody can be sent there. "
                    + "Widen " + ReleaseWorldRolloutPolicy.EnvVar + " and restart first.";
                return false;
            }

            IReadOnlyList<string> warnings = OperatorSafetyPolicy.TeleportWarnings(
                command.Destination.Kind,
                destination.LandsOnLoadedGround,
                destination.RequiredWorldEntityKey != null,
                IsAboardAShip(target.Player.EntityId));

            if (!WorldsAdriftRebornGameServer.Teleports.DispatchTo(
                    target.Player.EntityId, destination, TeleportReason))
            {
                message = "Entity " + target.Player.EntityId
                    + " could not be moved; they are still loading, their peer went away, "
                    + "or the destination terrain is not hosted here. See the game-server log.";
                return false;
            }

            message = "Moving player entity " + target.Player.EntityId + " to "
                + destination.Name + " " + destination.Position + ". The teleport is SENT or "
                + "DEFERRED until that terrain is on their client; arrival is confirmed "
                + "separately in the log." + Suffix(warnings);
            return true;
        }

        /// <summary>
        /// Turns a parsed destination spec into the exact destination the teleport
        /// machinery takes, resolving the parts that need the live world.
        /// </summary>
        private static bool TryDestination(
            OperatorDestinationSpec spec,
            OperatorPlayer target,
            IReadOnlyList<OperatorPlayer> roster,
            out TeleportDestination destination,
            out string error)
        {
            destination = default;
            error = string.Empty;

            switch (spec.Kind)
            {
                case OperatorDestinationKind.Spawn:
                    destination = TeleportPolicy.SafeDestination;
                    return true;

                case OperatorDestinationKind.Coordinate:
                    destination = TeleportPolicy.CoordDestination(spec.X, spec.Y, spec.Z);
                    return true;

                case OperatorDestinationKind.Island:
                {
                    if (!OperatorDestinationPolicy.TryFindIsland(spec.Value, out IslandId island, out error))
                    {
                        return false;
                    }
                    return OperatorDestinationPolicy.TryIslandDestination(
                        island,
                        WorldsAdriftRebornGameServer.IslandTopology.ById(island),
                        TeleportReason,
                        out destination,
                        out error);
                }

                case OperatorDestinationKind.Home:
                {
                    string uid = target.CharacterUid;
                    if (OperatorTargetPolicy.CanonicalUidText(uid).Length == 0)
                    {
                        error = "That player has no character uid on this server yet, so they "
                            + "have no recorded home. Name an island instead.";
                        return false;
                    }

                    IslandId? home = WildernessGraduationService.HomeOf(uid);
                    if (home == null)
                    {
                        error = "Character " + uid + " has no recorded home island - they have "
                            + "not graduated from Haven, or their last stored position was in "
                            + "open sky. Name an island instead.";
                        return false;
                    }

                    return OperatorDestinationPolicy.TryIslandDestination(
                        home.Value,
                        WorldsAdriftRebornGameServer.IslandTopology.ById(home.Value),
                        TeleportReason,
                        out destination,
                        out error);
                }

                case OperatorDestinationKind.Player:
                {
                    if (!OperatorTargetPolicy.TryParse(spec.Value, out OperatorTarget other, out error))
                    {
                        return false;
                    }

                    OperatorTargetResolution resolved = OperatorTargetPolicy.Resolve(other, roster);
                    if (!resolved.Resolved)
                    {
                        error = "The DESTINATION player could not be resolved: " + resolved.Reason;
                        return false;
                    }

                    if (resolved.Player.EntityId == target.EntityId)
                    {
                        error = "That would send a player to themselves.";
                        return false;
                    }

                    if (!resolved.Player.HasPosition)
                    {
                        error = "Player entity " + resolved.Player.EntityId + " has no known world "
                            + "position yet, so there is nowhere to send anyone to.";
                        return false;
                    }

                    FixedPointPosition beside = OperatorSafetyPolicy.BesidePlayer(
                        FixedPointPosition.FromMetres(
                            resolved.Player.X, resolved.Player.Y, resolved.Player.Z));

                    // No RequiredWorldEntityKey, and honestly so: the destination is
                    // wherever a moving player happens to be, which this server
                    // cannot attribute to an island without a terrain query. The
                    // arrival is therefore ungated - the same bargain the ad-hoc
                    // coordinate makes - and the warning says so.
                    destination = new TeleportDestination(
                        TeleportReason,
                        beside,
                        landsOnLoadedGround: false,
                        "beside player entity " + resolved.Player.EntityId
                        + " at their current position");
                    return true;
                }

                default:
                    error = "That destination could not be read.";
                    return false;
            }
        }

        // ---- summon --------------------------------------------------------

        private static bool Summon(OperatorCommand command, out string message)
        {
            IReadOnlyList<OperatorPlayer> roster = Roster();

            OperatorTargetResolution target = OperatorTargetPolicy.Resolve(command.Target, roster);
            if (!target.Resolved)
            {
                message = target.Reason;
                return false;
            }

            if (!target.Player.HasPosition)
            {
                message = "Player entity " + target.Player.EntityId + " has no known world position "
                    + "yet, so there is nowhere to put a ship. Wait for them to finish loading.";
                return false;
            }

            OperatorSummonChoice choice = OperatorSummonPolicy.Choose(
                command.Hull, target.Player.CharacterUid, Hulls());
            if (!choice.Ok)
            {
                message = choice.Reason;
                return false;
            }

            FixedPointPosition centre = FixedPointPosition.FromMetres(
                target.Player.X, target.Player.Y, target.Player.Z);
            FixedPointPosition drop = AdminShipRecallPolicy.DestinationAbove(centre);

            if (!WorldsAdriftRebornGameServer.Flight.TryAdminRecall(
                    choice.HullEntityId, drop, out string error))
            {
                message = error;
                return false;
            }

            List<string> notes = new List<string>();
            if (choice.OwnershipMismatch) notes.Add(choice.Reason);

            IReadOnlyList<long> bystanders = OperatorSafetyPolicy.BystandersNear(
                drop, target.Player.EntityId, roster);
            if (bystanders.Count > 0)
            {
                notes.Add("Player entities " + string.Join(", ", bystanders)
                    + " are within " + OperatorSafetyPolicy.SummonBystanderRadiusMetres.ToString("0")
                    + " m of where the hull appears.");
            }

            message = "Summoned hull " + choice.HullEntityId + " exactly "
                + AdminShipRecallPolicy.HeightAbovePlayerMetres.ToString("0")
                + " m above player entity " + target.Player.EntityId
                + ". Ownership is unchanged." + Suffix(notes);
            return true;
        }

        /// <summary>
        /// Every live built hull with the character uid it belongs to.
        ///
        /// The owner is read from <c>BuiltShips</c>, which is the SAME index the
        /// 1205 shipyard registration and the boot restore write, rather than from
        /// anything reconstructed here. Ship identity in this server is keyed on
        /// the character uid and the shipyard's is not; that asymmetry has already
        /// produced one class of "the ship says it is not mine" bug, and the only
        /// defence is that there stays exactly one reader of the owner.
        /// </summary>
        internal static IReadOnlyList<OperatorHull> Hulls()
        {
            List<OperatorHull> hulls = new List<OperatorHull>();
            foreach (long hullEntityId in Crafting.BuiltShips.AllHullIds())
            {
                hulls.Add(new OperatorHull(
                    hullEntityId, Crafting.BuiltShips.OwnerFor(hullEntityId)));
            }
            return hulls;
        }

        private static string Suffix(IReadOnlyList<string> notes) =>
            notes.Count == 0 ? string.Empty : " " + string.Join(" ", notes);
    }
}
