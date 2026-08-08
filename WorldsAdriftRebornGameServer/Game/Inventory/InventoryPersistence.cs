using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// Reads and writes inventories in Postgres, and NEVER lets the database
    /// stop the game.
    ///
    /// This is the first thing the game server persists anywhere - the project
    /// did not reference WorldsAdriftReborn.Storage at all before this - so the
    /// failure mode to design for is not a lost item, it is a server that
    /// refuses to let anybody play because a database is down or was never set
    /// up. Every method here therefore swallows storage failures, logs them once
    /// per kind, and returns as though there were simply nothing stored.
    ///
    /// The consequences of that choice, stated plainly so nobody has to
    /// rediscover them:
    /// - No database configured: everything works exactly as it did before this
    ///   workstream, in-memory for the length of a session.
    /// - Database configured but broken: same, plus one loud line per failure
    ///   kind, and the LAST GOOD stored inventory stays in the database
    ///   untouched rather than being overwritten with whatever the session had.
    ///
    /// The one thing it will not do is write under a key that is not durable -
    /// see <see cref="Save"/>.
    /// </summary>
    internal sealed class InventoryPersistence
    {
        private readonly InventoryRepository? repository;
        private bool loadFailureLogged;
        private bool saveFailureLogged;

        /// <summary>Whether there is a database to talk to at all.</summary>
        internal bool Enabled => repository != null;

        /// <summary>Why persistence is off, or null when it is on.</summary>
        internal string? DisabledReason { get; }

        internal InventoryPersistence()
        {
            if (!Db.IsConfigured)
            {
                // Not an error. The default connection string points at a
                // loopback database that most contributors do not have, and
                // guessing that it is there would produce a connection timeout
                // on the join path of every single player.
                DisabledReason = Db.ConnectionStringVariable + " is not set";
                return;
            }

            try
            {
                Db db = new Db();
                db.EnsureSchema();
                repository = new InventoryRepository(db);
            }
            catch (Exception e)
            {
                // Configured but unreachable, or the migration failed. Naming it
                // here, once, at start-up is what stops it being discovered as
                // "my stuff does not save" three sessions later.
                DisabledReason = e.Message;
                repository = null;
            }
        }

        /// <summary>
        /// The stored inventory for a durable key, or null when there is none,
        /// the key is not durable, or the database could not be read.
        ///
        /// The three cases are deliberately indistinguishable to the caller,
        /// because the caller's response is the same for all three: seed the
        /// defaults. They are distinguishable in the log, which is where the
        /// difference matters.
        /// </summary>
        internal InventoryModel? Load( InventoryKey key )
        {
            Guid? uid = key.CharacterUid;

            if (repository == null || !key.IsDurable || !uid.HasValue)
            {
                return null;
            }

            try
            {
                InventoryRecord? record = repository.Find(uid.Value);

                if (record == null)
                {
                    return null;
                }

                InventoryModel? model = InventorySnapshot.Read(record.DataJson);

                if (model == null)
                {
                    // A payload we cannot parse is worse than none: silently
                    // seeding defaults over it would be indistinguishable from a
                    // wipe. Say so, and let the next save overwrite it.
                    Console.WriteLine("[error] stored inventory for " + key
                        + " is unreadable; seeding defaults. The row is NOT deleted.");
                    return null;
                }

                return model;
            }
            catch (Exception e)
            {
                if (!loadFailureLogged)
                {
                    loadFailureLogged = true;
                    Console.WriteLine("[error] could not read inventories from the database: " + e.Message
                        + ". Play continues; nothing will be restored this run.");
                }

                return null;
            }
        }

        /// <summary>
        /// Writes an inventory, or does nothing at all when the key is not
        /// durable.
        ///
        /// REFUSING A VOLATILE KEY IS THE POINT. A session key is derived from an
        /// entity id, and entity ids are never reused, so persisting under one
        /// would fill the database with rows no login could ever find while
        /// looking exactly like working persistence. Returns whether anything
        /// was written so the caller can say which happened.
        /// </summary>
        internal bool Save( InventoryKey key, InventoryModel model )
        {
            Guid? uid = key.CharacterUid;

            if (repository == null || !key.IsDurable || !uid.HasValue)
            {
                return false;
            }

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                repository.Save(new InventoryRecord(uid.Value, InventorySnapshot.Write(model), now, now));
                return true;
            }
            catch (Exception e)
            {
                if (!saveFailureLogged)
                {
                    saveFailureLogged = true;
                    Console.WriteLine("[error] could not write inventories to the database: " + e.Message
                        + ". Play continues; this session will not be saved.");
                }

                return false;
            }
        }
    }
}
