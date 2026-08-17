using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Persistence
{
    /// <summary>
    /// Reads and writes where a character logged out, and NEVER lets the database
    /// stop the game. The exact analogue of InventoryPersistence and
    /// ProgressionPersistence, down to the failure policy: storage failures are
    /// swallowed, logged once per kind, and reported as "nothing stored", so a
    /// database that is down degrades to the old always-spawn-at-Haven behaviour
    /// rather than refusing players entry.
    /// </summary>
    internal sealed class PlayerPositionPersistence
    {
        private readonly PositionRepository? repository;
        private bool loadFailureLogged;
        private bool saveFailureLogged;

        internal bool Enabled => repository != null;

        internal string? DisabledReason { get; }

        internal PlayerPositionPersistence()
        {
            if (!Db.IsConfigured)
            {
                DisabledReason = Db.ConnectionStringVariable + " is not set";
                return;
            }

            try
            {
                Db db = new Db();
                db.EnsureSchema();
                repository = new PositionRepository(db);
            }
            catch (Exception e)
            {
                DisabledReason = e.Message;
                repository = null;
            }
        }

        /// <summary>
        /// Where a character logged out, or null when there is none or the read
        /// failed. Both mean the same thing to the caller: use the spawn point.
        /// </summary>
        internal FixedPointPosition? Load(Guid characterUid)
        {
            if (repository == null) return null;

            try
            {
                PositionRecord? record = repository.Find(characterUid);
                return record == null
                    ? (FixedPointPosition?)null
                    : new FixedPointPosition(record.X, record.Y, record.Z);
            }
            catch (Exception e)
            {
                if (!loadFailureLogged)
                {
                    loadFailureLogged = true;
                    Console.WriteLine("[error] could not read a logout position from the database: "
                        + e.Message + ". Play continues; players will start at the spawn point.");
                }

                return null;
            }
        }

        /// <summary>
        /// Writes where a character is now. Returns whether anything was written
        /// so the caller can say which happened.
        /// </summary>
        internal bool Save(Guid characterUid, FixedPointPosition position)
        {
            if (repository == null) return false;

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                repository.Save(new PositionRecord(characterUid,
                    position.X, position.Y, position.Z, now, now));
                return true;
            }
            catch (Exception e)
            {
                if (!saveFailureLogged)
                {
                    saveFailureLogged = true;
                    Console.WriteLine("[error] could not write a logout position to the database: "
                        + e.Message + ". Play continues; this session's position will not be saved.");
                }

                return false;
            }
        }
    }
}
