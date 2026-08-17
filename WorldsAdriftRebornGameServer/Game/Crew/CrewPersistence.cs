using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Repositories;
using WorldsAdriftRebornGameServer.Multiplayer.Crew;

namespace WorldsAdriftRebornGameServer.Game.Crew
{
    /// <summary>
    /// Reads and writes crews in Postgres, and NEVER lets the database stop the
    /// game. The same failure policy as the inventory, progression and position
    /// stores: failures are swallowed, logged once per kind, and reported as
    /// "nothing stored", so a database that is down degrades to session-scoped
    /// crews rather than refusing players entry.
    /// </summary>
    internal sealed class CrewPersistence
    {
        private readonly CrewRepository? repository;
        private bool loadFailureLogged;
        private bool saveFailureLogged;

        internal bool Enabled => repository != null;
        internal string? DisabledReason { get; }

        internal CrewPersistence()
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
                repository = new CrewRepository(db);
            }
            catch (Exception e)
            {
                DisabledReason = e.Message;
                repository = null;
            }
        }

        /// <summary>
        /// Rebuilds the whole ledger at boot. Membership is replayed in join
        /// order, which is why the repository returns it ordered: succession
        /// depends on that order and a shuffled replay would silently change who
        /// inherits a crew.
        /// </summary>
        internal void LoadInto(CrewLedger ledger)
        {
            if (repository == null) return;

            try
            {
                IReadOnlyList<CrewRecord> crews = repository.AllCrews();
                IReadOnlyList<CrewMemberRecord> members = repository.AllMembers();

                foreach (CrewRecord crew in crews)
                {
                    ledger.Create(crew.CrewId, Key(crew.LeaderUid), crew.NumSlots);
                }

                foreach (CrewMemberRecord member in members)
                {
                    string uid = Key(member.CharacterUid);
                    ledger.Join(uid, member.CrewId);
                    if (member.Slot.HasValue) ledger.TakeSlot(uid, member.Slot.Value);
                }

                if (crews.Count > 0)
                {
                    Console.WriteLine("[info] restored " + crews.Count + " crew(s) with "
                        + members.Count + " member(s) from the database.");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("[error] could not read crews from the database: " + e.Message
                    + ". Play continues; crews start empty this run.");
            }
        }

        internal void SaveCrew(string crewId, Guid leaderUid, int numSlots)
        {
            if (repository == null) return;
            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                repository.SaveCrew(new CrewRecord(crewId, leaderUid, numSlots, now, now));
            }
            catch (Exception e) { SaveFailed(e); }
        }

        internal void SaveMember(Guid characterUid, string crewId, int joinOrder, int? slot)
        {
            if (repository == null) return;
            try
            {
                repository.SaveMember(new CrewMemberRecord(
                    characterUid, crewId, joinOrder, slot, DateTimeOffset.UtcNow));
            }
            catch (Exception e) { SaveFailed(e); }
        }

        internal void RemoveMember(Guid characterUid)
        {
            if (repository == null) return;
            try { repository.RemoveMember(characterUid); }
            catch (Exception e) { SaveFailed(e); }
        }

        internal void DeleteCrew(string crewId)
        {
            if (repository == null) return;
            try { repository.DeleteCrew(crewId); }
            catch (Exception e) { SaveFailed(e); }
        }

        private void SaveFailed(Exception e)
        {
            if (saveFailureLogged) return;
            saveFailureLogged = true;
            Console.WriteLine("[error] could not write a crew change to the database: "
                + e.Message + ". Play continues; crew changes will not survive a restart.");
            _ = loadFailureLogged;
        }

        /// <summary>
        /// The ledger keys on the same durable string the inventory and
        /// progression use, so a crew member and their inventory are the same
        /// player by construction rather than by convention.
        /// </summary>
        internal static string Key(Guid characterUid) =>
            Multiplayer.Inventory.InventoryKey.ForCharacter(characterUid).Value;

        internal static Guid? UidFromKey(string key)
        {
            int colon = key.LastIndexOf(':');
            string tail = colon >= 0 ? key.Substring(colon + 1) : key;
            return Guid.TryParse(tail, out Guid uid) ? uid : (Guid?)null;
        }
    }
}
