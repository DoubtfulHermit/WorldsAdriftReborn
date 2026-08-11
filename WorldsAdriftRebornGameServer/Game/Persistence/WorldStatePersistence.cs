using System;
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

        /// <summary>Records a newly built ship and writes the document atomically.</summary>
        internal static void RecordBuiltShip(FixedPointPosition hullPosition, byte[] hullBytes)
        {
            WorldStateSnapshot snapshot = Snapshot();

            snapshot.BuiltShips.Add(new BuiltShipRecord
            {
                HullX = hullPosition.X,
                HullY = hullPosition.Y,
                HullZ = hullPosition.Z,
                HullBytes = hullBytes ?? Array.Empty<byte>(),
            });

            Save();
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

            int deployables = 0;
            foreach (PlacedDeployableRecord record in snapshot.PlacedDeployables)
            {
                if (placement.RestorePlacedDeployable(record).HasValue)
                {
                    deployables++;
                }
            }

            int ships = 0;
            foreach (BuiltShipRecord record in snapshot.BuiltShips)
            {
                if (BuiltShipSpawner.Restore(record).HasValue)
                {
                    ships++;
                }
            }

            Console.WriteLine("[info] world persistence: restored " + deployables + "/"
                + snapshot.PlacedDeployables.Count + " placed deployable(s) and " + ships + "/"
                + snapshot.BuiltShips.Count + " built ship(s) from " + FilePath
                + " (they will be served to every joining client via the spawn plan).");
        }

        private static void Save()
        {
            AtomicJsonFile.Write(FilePath, Snapshot());
        }
    }
}
