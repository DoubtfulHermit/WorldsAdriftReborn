using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Persistence
{
    /// <summary>
    /// "Log out where you were, log back in there."
    ///
    /// This is deliberately NOT a different spawn point. The player's first
    /// transform is a seed served during the connect burst, and re-seeding 190602
    /// on a live entity is the out-of-world teleport that MirrorSendPolicy exists
    /// to prevent. Worse, the character uid the position is keyed by does not
    /// arrive until the client publishes 1088, which is AFTER the seed has already
    /// been served - at the moment the server chooses a position it does not yet
    /// know whose position to choose.
    ///
    /// So the restore is a teleport, not a spawn: the player is seeded at the
    /// usual spawn point, and once identity arrives they are moved by the same
    /// proven 190607 path the operator trigger and the fall rescue use, including
    /// its terrain-readiness deferral. The visible cost is a moment at the spawn
    /// point before the move lands. Removing that means getting identity onto the
    /// wire earlier, which is a client protocol change and is not done here.
    /// </summary>
    internal static class PlayerPositionService
    {
        private static readonly PlayerPositionPersistence Persistence = new PlayerPositionPersistence();

        /// <summary>
        /// The durable character uid each live entity is bound to. Its own map,
        /// for the same reason the progression service keeps one: the disconnect
        /// order of the services must not let one lose the key another still needs.
        /// </summary>
        private static readonly Dictionary<long, Guid> EntityUid = new Dictionary<long, Guid>();

        /// <summary>What was last written, so standing still never writes again.</summary>
        private static readonly Dictionary<long, FixedPointPosition> LastSaved = new Dictionary<long, FixedPointPosition>();
        private static readonly Dictionary<long, int?> LastSavedShipIndex =
            new Dictionary<long, int?>();

        /// <summary>Entities already restored this session; the move happens once.</summary>
        private static readonly HashSet<long> Restored = new HashSet<long>();

        internal static void ReportPersistenceState()
        {
            if (Persistence.Enabled)
            {
                Console.WriteLine("[info] logout-position persistence is ON (Postgres): players"
                    + " return to where they left, not to the spawn point.");
            }
            else
            {
                Console.WriteLine("[warning] logout-position persistence is OFF ("
                    + Persistence.DisabledReason + "). Every player will start at the spawn point.");
            }
        }

        /// <summary>
        /// Binds an entity to its character and, if that character has a stored
        /// position, moves them there. Called from the 1088 handler, the only
        /// place the uid appears. Safe to call repeatedly: the move happens once
        /// per entity, so a second 1088 cannot yank a player who has since walked
        /// away back to where they logged out.
        /// </summary>
        internal static void BindIdentity(long entityId,
            IReadOnlyDictionary<string, string> customisation)
        {
            Guid? uid = CharacterIdentity.UidFrom(customisation);
            if (!uid.HasValue) return;

            EntityUid[entityId] = uid.Value;

            if (!Restored.Add(entityId)) return;

            StoredPlayerPosition? durable = Persistence.Load(uid.Value);
            FixedPointPosition? stored = ResolveRestorePosition(durable, out string anchorDetail);
            PositionRestoreVerdict verdict = PlayerPositionPolicy.Decide(
                stored, SpawnPolicy.PlayerSpawnPosition);

            // The identity half of the story only. The VERDICT is printed by the
            // restore path itself, which is the one that knows whether the ground
            // was there, so the two lines never say the same thing twice.
            Console.WriteLine("[info] logout position for character:" + uid.Value.ToString("D")
                + " (entity " + entityId + "): "
                + (stored.HasValue
                    ? "stored at (" + stored.Value.MetresX.ToString("0.#") + ", "
                        + stored.Value.MetresY.ToString("0.#") + ", "
                        + stored.Value.MetresZ.ToString("0.#") + ") m" + anchorDetail + "."
                    : "nothing stored."));

            if (verdict == PositionRestoreVerdict.Restore)
            {
                // Do not re-save what we just read: LastSaved is seeded with the
                // restored value so an immediate periodic tick is a no-op. Seeded
                // even when the restore is later refused for terrain reasons, and
                // that is correct: the player is then at the spawn point, which is
                // far from the stored value, so the next periodic tick writes their
                // real location instead of quietly keeping the old one.
                LastSaved[entityId] = stored!.Value;
                LastSavedShipIndex[entityId] = durable?.ShipAnchor?.BuiltShipIndex;
            }

            // Handed on WHATEVER the verdict, including "nothing stored". The
            // second half of the decision - is the ground at that point actually on
            // this player's client yet - needs the terrain ledger and the teleport
            // machinery, so it lives there; SpawnRestorePolicy composes both halves
            // and this service does not second-guess it.
            WorldsAdriftRebornGameServer.Teleports.RestoreLoggedOutPosition(
                entityId, stored, verdict);
        }

        /// <summary>
        /// Writes this entity's current position if it has moved far enough to be
        /// worth a row. Called on a slow cadence from the poll loop, because a
        /// server that is killed never runs the disconnect path and a player who
        /// crashes out should still come back near where they were.
        /// </summary>
        internal static void SaveIfMoved(long entityId, FixedPointPosition current,
            long? aboardHullEntityId = null)
        {
            if (!EntityUid.TryGetValue(entityId, out Guid uid)) return;

            Multiplayer.Ship.ShipLogoutAnchor? anchor = CaptureAnchor(aboardHullEntityId, current);
            LastSaved.TryGetValue(entityId, out FixedPointPosition last);
            bool everSaved = LastSaved.ContainsKey(entityId);
            LastSavedShipIndex.TryGetValue(entityId, out int? lastShipIndex);
            bool anchorChanged = !LastSavedShipIndex.ContainsKey(entityId)
                || lastShipIndex != anchor?.BuiltShipIndex;
            if (!anchorChanged
                && !PlayerPositionPolicy.ShouldSave(everSaved ? last : (FixedPointPosition?)null, current))
                return;

            if (Persistence.Save(uid, current, anchor))
            {
                LastSaved[entityId] = current;
                LastSavedShipIndex[entityId] = anchor?.BuiltShipIndex;
            }
        }

        /// <summary>
        /// The final write, on the way out. Separate from <see cref="SaveIfMoved"/>
        /// because it ignores the movement threshold: the last few paces before a
        /// disconnect are exactly the ones worth keeping.
        /// </summary>
        internal static bool SaveOnLeave(long entityId, FixedPointPosition current,
            long? aboardHullEntityId = null)
        {
            if (!EntityUid.TryGetValue(entityId, out Guid uid)) return false;
            return Persistence.Save(uid, current, CaptureAnchor(aboardHullEntityId, current));
        }

        /// <summary>
        /// A character's stored position, for anyone who needs to ask about a
        /// player who is not the one in front of them.
        ///
        /// The Wilderness shrine asks it of a crew's LEADER, who may well be
        /// offline: "which island does this crew live on" has to be answerable
        /// without them logged in, or a crew could only ever regroup while
        /// everybody happened to be present. Reading it here rather than opening a
        /// second connection keeps one component owning the table.
        /// </summary>
        internal static FixedPointPosition? StoredFor(Guid uid) => Persistence.Load(uid)?.World;

        /// <summary>
        /// Writes a character's position directly, outside the movement threshold.
        ///
        /// Used by graduation to record a Wilderness island as somebody's home -
        /// including a crewmate who is offline, which is the whole reason it is not
        /// enough to let the periodic save catch up. For a LIVE entity it also
        /// seeds the last-saved mark, so the next periodic tick does not
        /// immediately write the position the player is being moved AWAY from and
        /// undo the record.
        /// </summary>
        internal static bool Record(Guid uid, FixedPointPosition where)
        {
            if (!Persistence.Save(uid, where)) return false;

            foreach (KeyValuePair<long, Guid> bound in EntityUid)
                if (bound.Value == uid)
                {
                    LastSaved[bound.Key] = where;
                    LastSavedShipIndex[bound.Key] = null;
                }
            return true;
        }

        /// <summary>Drops every trace of an entity. Call AFTER the final save.</summary>
        internal static void Forget(long entityId)
        {
            EntityUid.Remove(entityId);
            LastSaved.Remove(entityId);
            LastSavedShipIndex.Remove(entityId);
            Restored.Remove(entityId);
        }

        private static Multiplayer.Ship.ShipLogoutAnchor? CaptureAnchor(
            long? hullEntityId, FixedPointPosition playerWorld)
        {
            if (!hullEntityId.HasValue) return null;
            int? index = Game.Crafting.BuiltShips.PersistentIndexFor(hullEntityId.Value);
            (FixedPointPosition hullWorld, uint hullRotation) =
                Game.ShipInteractionEligibility.HullWorldPose(hullEntityId.Value);
            return Multiplayer.Ship.ShipRelativeLogoutPolicy.Capture(
                index, playerWorld, hullWorld, hullRotation);
        }

        private static FixedPointPosition? ResolveRestorePosition(
            StoredPlayerPosition? durable, out string detail)
        {
            detail = "";
            if (!durable.HasValue) return null;
            Multiplayer.Ship.ShipLogoutAnchor? anchor = durable.Value.ShipAnchor;
            if (!anchor.HasValue) return durable.Value.World;

            long? hull = Game.Crafting.BuiltShips.HullForPersistentIndex(
                anchor.Value.BuiltShipIndex);
            if (!hull.HasValue)
            {
                detail = " (ship anchor unavailable; using world fallback)";
                return durable.Value.World;
            }

            (FixedPointPosition hullWorld, uint hullRotation) =
                Game.ShipInteractionEligibility.HullWorldPose(hull.Value);
            FixedPointPosition? resolved = Multiplayer.Ship.ShipRelativeLogoutPolicy.Resolve(
                anchor.Value, hullWorld, hullRotation);
            if (!resolved.HasValue)
            {
                detail = " (invalid ship anchor; using world fallback)";
                return durable.Value.World;
            }

            detail = " aboard durable ship " + anchor.Value.BuiltShipIndex;
            return resolved.Value;
        }
    }
}
