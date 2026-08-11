using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;

namespace WorldsAdriftRebornGameServer.Game.Knowledge
{
    /// <summary>
    /// Reads and writes player knowledge in Postgres, and NEVER lets the database
    /// stop the game. The exact analogue of InventoryPersistence, down to the
    /// failure policy: every method swallows storage failures, logs them once per
    /// kind, and returns as though there were simply nothing stored, so a database
    /// that is down or was never configured degrades to the old in-memory-only
    /// behaviour rather than refusing players entry.
    ///
    /// The one thing it will not do is write under a key that is not a real
    /// character uid - see <see cref="Save"/>.
    /// </summary>
    internal sealed class ProgressionPersistence
    {
        private readonly ProgressionRepository? repository;
        private bool loadFailureLogged;
        private bool saveFailureLogged;

        /// <summary>Whether there is a database to talk to at all.</summary>
        internal bool Enabled => repository != null;

        /// <summary>Why persistence is off, or null when it is on.</summary>
        internal string? DisabledReason { get; }

        internal ProgressionPersistence()
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
                repository = new ProgressionRepository(db);
            }
            catch (Exception e)
            {
                DisabledReason = e.Message;
                repository = null;
            }
        }

        /// <summary>
        /// The stored progression for a character, or null when there is none,
        /// the database could not be read, or the payload will not parse. The
        /// three are deliberately indistinguishable to the caller, whose response
        /// is the same for all: keep the live state.
        /// </summary>
        internal ProgressionState? Load(Guid characterUid)
        {
            if (repository == null)
            {
                return null;
            }

            try
            {
                ProgressionRecord? record = repository.Find(characterUid);

                if (record == null)
                {
                    return null;
                }

                ProgressionState? state = ProgressionSnapshot.Read(record.DataJson);

                if (state == null)
                {
                    Console.WriteLine("[error] stored progression for character:" + characterUid.ToString("D")
                        + " is unreadable; keeping the live state. The row is NOT deleted.");
                    return null;
                }

                return state;
            }
            catch (Exception e)
            {
                if (!loadFailureLogged)
                {
                    loadFailureLogged = true;
                    Console.WriteLine("[error] could not read progression from the database: " + e.Message
                        + ". Play continues; nothing will be restored this run.");
                }

                return null;
            }
        }

        /// <summary>
        /// Writes a character's progression. Returns whether anything was written
        /// so the caller can say which happened.
        /// </summary>
        internal bool Save(Guid characterUid, ProgressionState state)
        {
            if (repository == null)
            {
                return false;
            }

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                repository.Save(new ProgressionRecord(characterUid, ProgressionSnapshot.Write(state), now, now));
                return true;
            }
            catch (Exception e)
            {
                if (!saveFailureLogged)
                {
                    saveFailureLogged = true;
                    Console.WriteLine("[error] could not write progression to the database: " + e.Message
                        + ". Play continues; this session's knowledge will not be saved.");
                }

                return false;
            }
        }
    }
}
