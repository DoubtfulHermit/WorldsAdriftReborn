using System;
using System.Collections.Generic;
using System.IO;
using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Game.Placement;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;

namespace WorldsAdriftRebornGameServer.Game.Persistence
{
    /// <summary>
    /// The shared, server-owned WORLD state that survives a restart: the deployables
    /// players have placed (shipyards) and the ships they have built. It is the
    /// game-server counterpart of the login server's roster persistence - the same
    /// atomic-JSON discipline (<see cref="AtomicJsonFile"/>), one document under the
    /// same <c>WAREBORN_DATA_DIR</c> root the login server uses, so an operator has one
    /// place to look for where state lives.
    ///
    /// WHY WORLD STATE AND NOT PER-PLAYER STATE. A placed shipyard and a built ship are
    /// SHARED entities every client sees; they key on nothing player-specific and must
    /// be re-created ONCE at boot, before any client connects, so the connect-time
    /// spawn plan serves them to everyone exactly as it serves the static test ship.
    /// Per-player state (inventory, designs, progression, position) keys on character
    /// uid and belongs in its own per-character store - that is a separate seam
    /// (InventoryService.BindIdentity already models it) and is deliberately NOT here.
    ///
    /// THE SAVE POINTS are the two spawn seams themselves - a deployable is recorded the
    /// instant it is placed, a ship the instant it is built - so there is no separate
    /// "remember to save" step. Placements are rare (a human positioning a structure),
    /// so rewriting the whole small document on each one is far simpler to make
    /// crash-safe than a per-entity file and costs nothing measurable.
    ///
    /// NOT thread-safe, deliberately: every writer runs on the single server poll loop,
    /// like every other ledger in this server.
    /// </summary>
    internal static class WorldStatePersistence
    {
        private const string FileName = "world-state.json";

        private static WorldStateSnapshot? _snapshot;

        /// <summary>
        /// Root of all persisted state, read the same way as the login server's
        /// JsonFileStore so both processes agree without configuration: override with
        /// <c>WAREBORN_DATA_DIR</c>, else a "data" folder next to the server binary.
        /// </summary>
        private static string DataDir
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable("WAREBORN_DATA_DIR");

                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured!;
                }

                return Path.Combine(AppContext.BaseDirectory, "data");
            }
        }

        /// <summary>The world-state document's full path, for the startup banner.</summary>
        internal static string FilePath => Path.Combine(DataDir, FileName);

        /// <summary>
        /// The live in-memory snapshot, loaded from disk once on first touch. A missing
        /// or unreadable file is an empty world, not an error - a fresh server has no
        /// placed structures.
        /// </summary>
        private static WorldStateSnapshot Snapshot()
        {
            if (_snapshot == null)
            {
                _snapshot = AtomicJsonFile.Read<WorldStateSnapshot>(FilePath) ?? new WorldStateSnapshot();
            }

            return _snapshot;
        }

        /// <summary>Records a newly placed deployable and writes the document atomically.</summary>
        internal static void RecordPlacedDeployable(
            string itemTypeId,
            FixedPointPosition position,
            uint packedRotation,
            string ownerCharacterUid)
        {
            WorldStateSnapshot snapshot = Snapshot();

            snapshot.PlacedDeployables.Add(new PlacedDeployableRecord
            {
                ItemTypeId = itemTypeId,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                PackedRotation = packedRotation,
                OwnerCharacterUid = ownerCharacterUid ?? "",
            });

            Save();
        }

        /// <summary>
        /// Drops a placed deployable's record - it was PACKED back into inventory
        /// (station pickup) - so the next boot never respawns it. Matched by item
        /// type + the EXACT placed position, the same stable key the boot restore
        /// uses to re-dock ships to their yard (two deployables never share one
        /// spot). A no-op when no such record exists; returns whether one was
        /// removed, so the caller can log a mismatch loudly.
        /// </summary>
        internal static bool RemovePlacedDeployable(string itemTypeId, FixedPointPosition position)
        {
            WorldStateSnapshot snapshot = Snapshot();

            int removed = snapshot.PlacedDeployables.RemoveAll(r =>
                r.ItemTypeId == itemTypeId
                && r.X == position.X && r.Y == position.Y && r.Z == position.Z);

            if (removed > 0)
            {
                Save();
            }

            return removed > 0;
        }

        /// <summary>
        /// Records a newly built ship and writes the document atomically. Returns the
        /// ship's PERSISTENT INDEX (its position in the append-only BuiltShips list), which
        /// a mounted part references its ship by - stable across restart because ships are
        /// only ever appended and restored in that same order.
        /// </summary>
        internal static int RecordBuiltShip(FixedPointPosition hullPosition, byte[] hullBytes,
            string? ownerCharacterUid = null, FixedPointPosition? shipyardPosition = null)
        {
            WorldStateSnapshot snapshot = Snapshot();

            snapshot.BuiltShips.Add(new BuiltShipRecord
            {
                HullX = hullPosition.X,
                HullY = hullPosition.Y,
                HullZ = hullPosition.Z,
                HullBytes = hullBytes ?? Array.Empty<byte>(),
                // The builder = the shipyard's owner, threaded so a built ship's record is
                // OWNED like its yard and the deployables rather than left blank. The
                // shipyard's own 1205 registration (from the yard owner) is what actually
                // grants the client build access; this keeps the ship's persisted record
                // consistent and round-trips the owner across restart.
                OwnerCharacterUid = ownerCharacterUid ?? "",
                // The building shipyard's position, so restore can re-DOCK this ship to the
                // deployable that reappears there - a docked ship is what makes the yard
                // report ACTIVE (IsShipyardActive() == DockedShip != null). Zero when unknown.
                ShipyardX = shipyardPosition?.X ?? 0,
                ShipyardY = shipyardPosition?.Y ?? 0,
                ShipyardZ = shipyardPosition?.Z ?? 0,
            });

            Save();
            return snapshot.BuiltShips.Count - 1;
        }

        /// <summary>
        /// Makes a built ship's departure from its current shipyard restart-durable.
        /// The ship stays at the same persistent index because mounted-part records use
        /// that index; only the obsolete build-time dock link is cleared.
        /// </summary>
        internal static void ClearBuiltShipDock(int persistentIndex)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (persistentIndex < 0 || persistentIndex >= snapshot.BuiltShips.Count)
            {
                Console.WriteLine("[warning] world persistence: cannot clear dock link for invalid built-ship index "
                    + persistentIndex + ".");
                return;
            }

            BuiltShipRecord record = snapshot.BuiltShips[persistentIndex];
            if (!record.ShipyardPosition().HasValue)
            {
                return;
            }

            record.ClearShipyardDock();
            Save();
        }

        /// <summary>Persists the flight session's authoritative pose at a stable ship index.</summary>
        internal static void UpdateBuiltShipPose(int persistentIndex,
            FixedPointPosition position, double yawRadians)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (persistentIndex < 0 || persistentIndex >= snapshot.BuiltShips.Count) return;
            BuiltShipRecord record = snapshot.BuiltShips[persistentIndex];
            if (record.HullPosition() == position
                && System.Math.Abs(record.HullYawRadians - yawRadians) < 0.000001) return;
            record.UpdatePose(position, yawRadians);
            Save();
        }

        /// <summary>Atomically persists a captured pose and its empty-yard dock link.</summary>
        internal static void DockBuiltShip(int persistentIndex, FixedPointPosition hullPosition,
            double yawRadians, FixedPointPosition shipyardPosition)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (persistentIndex < 0 || persistentIndex >= snapshot.BuiltShips.Count) return;
            BuiltShipRecord record = snapshot.BuiltShips[persistentIndex];
            record.UpdatePose(hullPosition, yawRadians);
            record.DockTo(shipyardPosition);
            Save();
        }

        /// <summary>
        /// Records a LOOSE part and writes the document atomically. UPSERT by <c>PartUid</c>:
        /// a part re-persisted as loose (e.g. lifted off a ship) replaces its prior loose
        /// record rather than adding a second, so the part can never restore twice.
        /// </summary>
        internal static void RecordLoosePart(LoosePartRecord record)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (!string.IsNullOrEmpty(record.PartUid))
            {
                snapshot.LooseParts.RemoveAll(r => r.PartUid == record.PartUid);
            }
            snapshot.LooseParts.Add(record);
            Save();
        }

        /// <summary>
        /// Drops a loose part's record by its stable <c>PartUid</c> - called when it becomes
        /// MOUNTED (it is re-expressed as a <see cref="MountedPartRecord"/>) so the same part
        /// is never both loose and mounted in the save. A no-op when no such record exists.
        /// </summary>
        internal static bool RemoveLoosePart(string partUid)
        {
            if (string.IsNullOrEmpty(partUid))
            {
                return true;
            }

            WorldStateSnapshot snapshot = Snapshot();
            LoosePartRecord? removed = snapshot.LooseParts.Find(r => r.PartUid == partUid);
            if (removed != null)
            {
                snapshot.LooseParts.Remove(removed);
                if (!Save())
                {
                    snapshot.LooseParts.Add(removed);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Records a part MOUNTED onto a built ship and writes the document atomically.
        /// UPSERT by <c>PartUid</c>: any prior mount record for the same part is replaced, so
        /// lifting a mounted part and re-placing it elsewhere updates its one record rather
        /// than adding a second - the part can never restore twice.
        /// </summary>
        internal static void RecordMountedPart(MountedPartRecord record)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (!string.IsNullOrEmpty(record.PartUid))
            {
                snapshot.MountedParts.RemoveAll(r => r.PartUid == record.PartUid);
            }
            snapshot.MountedParts.Add(record);
            Save();
        }

        /// <summary>
        /// Updates ONE mounted sail's persisted furl state in place, keyed by its stable
        /// <c>PartUid</c> - called on every furl/unfurl toggle so a restart restores the
        /// rigging a player left. A no-op when no such record exists (a sail mounted on
        /// the non-persisted static hull is session-only, same rule as its mount).
        /// </summary>
        internal static void UpdateMountedSailState(string? partUid, bool unfurled)
        {
            if (string.IsNullOrEmpty(partUid))
            {
                return;
            }

            WorldStateSnapshot snapshot = Snapshot();
            MountedPartRecord? record = snapshot.MountedParts.Find(r => r.PartUid == partUid);
            if (record != null && record.SailUnfurled != unfurled)
            {
                record.SailUnfurled = unfurled;
                Save();
            }
        }

        /// <summary>
        /// Updates ONE mounted lamp's persisted switch in place, keyed by its stable
        /// <c>PartUid</c> - the lamp twin of <see cref="UpdateMountedSailState"/>. The
        /// record stores the INVERTED bit (LampOff) so a legacy record defaults to ON;
        /// this takes the natural "is it on" and flips it once, here.
        /// </summary>
        internal static void UpdateMountedLampState(string? partUid, bool on)
        {
            if (string.IsNullOrEmpty(partUid))
            {
                return;
            }

            WorldStateSnapshot snapshot = Snapshot();
            MountedPartRecord? record = snapshot.MountedParts.Find(r => r.PartUid == partUid);
            if (record != null && record.LampOff != !on)
            {
                record.LampOff = !on;
                Save();
            }
        }

        /// <summary>
        /// Drops a mounted part's record by its stable <c>PartUid</c> - called when it is
        /// LIFTED OFF a ship and becomes loose again (re-expressed as a
        /// <see cref="LoosePartRecord"/>). A no-op when no such record exists.
        /// </summary>
        internal static bool RemoveMountedPart(string partUid)
        {
            if (string.IsNullOrEmpty(partUid))
            {
                return true;
            }

            WorldStateSnapshot snapshot = Snapshot();
            MountedPartRecord? removed = snapshot.MountedParts.Find(r => r.PartUid == partUid);
            if (removed != null)
            {
                snapshot.MountedParts.Remove(removed);
                if (!Save())
                {
                    // Keep the in-memory snapshot consistent with the unchanged file.
                    // A later successful save must never accidentally make a failed
                    // dismantle durable.
                    snapshot.MountedParts.Add(removed);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Commits the durable half of frame salvage in one atomic JSON write: retain a
        /// stable-index ship tombstone, remove every mount on it, and upsert those same
        /// parts as loose. A crash can therefore never restore both the hull and its drops,
        /// or lose the parts between several independent saves.
        /// </summary>
        internal static bool SalvageBuiltShip(int persistentIndex, IReadOnlyList<LoosePartRecord> droppedParts)
        {
            WorldStateSnapshot snapshot = Snapshot();
            if (persistentIndex < 0 || persistentIndex >= snapshot.BuiltShips.Count
                || snapshot.BuiltShips[persistentIndex].Salvaged)
            {
                return false;
            }

            BuiltShipRecord ship = snapshot.BuiltShips[persistentIndex];
            FixedPointPosition? oldDock = ship.ShipyardPosition();
            var oldMounted = new List<MountedPartRecord>(snapshot.MountedParts);
            var oldLoose = new List<LoosePartRecord>(snapshot.LooseParts);
            ship.Salvaged = true;
            ship.ClearShipyardDock();
            snapshot.MountedParts.RemoveAll(r => r.BuiltShipIndex == persistentIndex);
            foreach (LoosePartRecord part in droppedParts)
            {
                if (!string.IsNullOrEmpty(part.PartUid))
                {
                    snapshot.LooseParts.RemoveAll(r => r.PartUid == part.PartUid);
                }
                snapshot.LooseParts.Add(part);
            }
            if (!Save())
            {
                ship.Salvaged = false;
                if (oldDock.HasValue) ship.DockTo(oldDock.Value);
                snapshot.MountedParts = oldMounted;
                snapshot.LooseParts = oldLoose;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Re-creates every persisted deployable and built ship as a world entity,
        /// BEFORE the connect-time spawn plan is computed, so a joining client is served
        /// them exactly like the static world entities. Runs once at boot, on the poll
        /// loop, before any peer connects.
        ///
        /// Both spawn paths (this and the runtime one) share the same monotonic sequence
        /// counters, and this runs first, so restored registration keys never collide
        /// with the ones a later runtime placement allocates.
        /// </summary>
        internal static void RestoreOnBoot(PlacementService placement)
        {
            WorldStateSnapshot snapshot = Snapshot();

            // 1. DEPLOYABLES (shipyards, stations) - each carries its owner, so its 1205
            //    serve seeds registeredCharacterUids and the placer is recognised as owner.
            //    Map each restored deployable by its EXACT position so a built ship can be
            //    re-docked to the shipyard it was built at (step 2) - position is a stable,
            //    exact key across restart (two deployables never share one spot).
            int deployables = 0;
            Dictionary<(long, long, long), long> deployableIdByPos = new Dictionary<(long, long, long), long>();
            foreach (PlacedDeployableRecord record in snapshot.PlacedDeployables)
            {
                long? entityId = placement.RestorePlacedDeployable(record);
                if (entityId.HasValue)
                {
                    deployables++;
                    FixedPointPosition p = record.Position();
                    deployableIdByPos[(p.X, p.Y, p.Z)] = entityId.Value;
                }
            }

            // 2. BUILT SHIPS - restored IN ORDER, so the Nth restored hull is the ship a
            //    mount record's BuiltShipIndex==N references. Remember index -> fresh hull id
            //    both locally (to attach mounts below) and in the BuiltShips ledger (so a
            //    NEW mount committed on a restored ship this session persists correctly).
            int ships = 0;
            long?[] hullByIndex = new long?[snapshot.BuiltShips.Count];
            for (int i = 0; i < snapshot.BuiltShips.Count; i++)
            {
                if (snapshot.BuiltShips[i].Salvaged)
                {
                    Console.WriteLine("[info] world persistence: built-ship index " + i
                        + " is a salvaged tombstone; skipping its restore.");
                    continue;
                }
                long? hullEntityId = BuiltShipSpawner.Restore(snapshot.BuiltShips[i]);
                hullByIndex[i] = hullEntityId;
                if (hullEntityId.HasValue)
                {
                    BuiltShips.SetPersistentIndex(hullEntityId.Value, i);
                    ships++;

                    // RE-DOCK: link this restored hull back to the shipyard it was built
                    // at, so the yard's 1205 DockedShipId reports it and the client sees it
                    // ACTIVE. Without this the restored yard has no docked ship and reports
                    // "the nearby shipyard is inactive" (IsShipyardActive() == DockedShip
                    // != null). Matched by the shipyard's exact persisted position.
                    FixedPointPosition? yardPos = snapshot.BuiltShips[i].ShipyardPosition();
                    if (yardPos.HasValue
                        && deployableIdByPos.TryGetValue((yardPos.Value.X, yardPos.Value.Y, yardPos.Value.Z),
                            out long shipyardEntityId))
                    {
                        BuiltShips.SetDocked(shipyardEntityId, hullEntityId.Value);
                        Console.WriteLine("[info] world persistence: re-docked restored hull " + hullEntityId.Value
                            + " to its shipyard entity " + shipyardEntityId + " (yard now reports ACTIVE).");
                    }
                    else
                    {
                        Console.WriteLine("[info] world persistence: restored hull " + hullEntityId.Value
                            + " has no re-dockable shipyard (legacy record or yard gone); it stays UN-DOCKED.");
                    }
                }
            }

            // 3. MOUNTED PARTS - re-spawned ALREADY ATTACHED to their restored ship's fresh
            //    hull id, BEFORE loose parts so the spawn plan orders hull -> mounted part.
            int mounts = 0;
            foreach (MountedPartRecord record in snapshot.MountedParts)
            {
                if (record.BuiltShipIndex < 0 || record.BuiltShipIndex >= hullByIndex.Length)
                {
                    Console.WriteLine("[warning] world persistence: mounted part '" + record.PartUid
                        + "' references built-ship index " + record.BuiltShipIndex
                        + " which no longer exists; skipping its restore.");
                    continue;
                }

                long? hullEntityId = hullByIndex[record.BuiltShipIndex];
                if (!hullEntityId.HasValue)
                {
                    Console.WriteLine("[warning] world persistence: mounted part '" + record.PartUid
                        + "' rides built-ship index " + record.BuiltShipIndex
                        + " which failed to restore; skipping.");
                    continue;
                }

                if (LoosePartSpawner.RestoreMounted(record, hullEntityId.Value).HasValue)
                {
                    mounts++;
                }
            }

            // 4. LOOSE PARTS - crafted-but-unmounted parts, re-spawned at their world-
            //    absolute spawn spot. Last, so the plan serves ship structure first.
            //
            // Older builds materialised every output at one fixed point. Preserve the
            // paid-for records but fan exact coordinate collisions into deterministic
            // neighbouring slots before registration. Save the migrated coordinates so
            // this is one-time repair, not motion on every reboot.
            var occupiedLoosePositions = new List<FixedPointPosition>();
            int separatedLooseParts = 0;
            foreach (LoosePartRecord record in snapshot.LooseParts)
            {
                FixedPointPosition original = record.Position();
                FixedPointPosition separated = Multiplayer.Ship.LoosePartPlacement.FirstAvailableFrom(
                    original, occupiedLoosePositions);
                if (!separated.Equals(original))
                {
                    record.X = separated.X;
                    record.Y = separated.Y;
                    record.Z = separated.Z;
                    separatedLooseParts++;
                }
                occupiedLoosePositions.Add(separated);
            }
            if (separatedLooseParts > 0)
            {
                Save();
                Console.WriteLine("[info] world persistence: separated " + separatedLooseParts
                    + " coincident loose crafted output(s); no records or materials were discarded.");
            }

            int looseParts = 0;
            foreach (LoosePartRecord record in snapshot.LooseParts)
            {
                if (LoosePartSpawner.Restore(record).HasValue)
                {
                    looseParts++;
                }
            }

            Console.WriteLine("[info] world persistence: restored " + deployables + "/"
                + snapshot.PlacedDeployables.Count + " placed deployable(s), " + ships + "/"
                + snapshot.BuiltShips.Count + " built ship(s), " + mounts + "/"
                + snapshot.MountedParts.Count + " mounted part(s) and " + looseParts + "/"
                + snapshot.LooseParts.Count + " loose part(s) from " + FilePath
                + " (they will be served to every joining client via the spawn plan).");
        }

        private static bool Save()
        {
            return AtomicJsonFile.Write(FilePath, Snapshot());
        }
    }
}
