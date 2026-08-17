using WorldsAdriftReborn.Storage;
using WorldsAdriftReborn.Storage.Repositories;

namespace WorldsAdriftRebornGameServer.Game.Crew
{
    /// <summary>
    /// Answers the crew panel's "find a player by name".
    ///
    /// This is the piece that proves crews were never Steam-brokered: the client
    /// searches the SERVER's roster and invites by the id it gets back, rather
    /// than picking from a Steam friends list. The only Steam call the client
    /// makes about people is <c>SteamFriends.GetPersonaName</c> for its own
    /// display name, which is probably why crews felt Steam-driven - the names in
    /// the panel WERE Steam names.
    ///
    /// The search reads the characters table the login server owns. That is a
    /// read across a process boundary, so it is deliberately forgiving: a
    /// database that is down answers "not found" rather than throwing, and the
    /// retail UI already models search as possibly-slow and possibly-empty
    /// (WAUICrewEvents carries ServerIsBusyChanged and RefreshServerCache).
    /// </summary>
    internal static class CrewSearch
    {
        private static CharacterRepository? repository;
        private static bool tried;
        private static bool failureLogged;

        internal static void Answer(string actorUid, string playerName, int requestId)
        {
            if (string.IsNullOrWhiteSpace(playerName))
            {
                CrewPush.SearchResult(actorUid, requestId, playerName ?? string.Empty,
                    string.Empty, found: false);
                return;
            }

            Guid? found = Find(playerName);

            CrewPush.SearchResult(actorUid, requestId, playerName,
                found.HasValue ? CrewPersistence.Key(found.Value) : string.Empty,
                found.HasValue);
        }

        private static Guid? Find(string playerName)
        {
            CharacterRepository? repo = Repository();
            if (repo == null) return null;

            try
            {
                return repo.FindByName(playerName)?.CharacterUid;
            }
            catch (Exception e)
            {
                if (!failureLogged)
                {
                    failureLogged = true;
                    Console.WriteLine("[error] crew search could not read the roster: "
                        + e.Message + ". Searches will answer 'not found' this run.");
                }
                return null;
            }
        }

        private static CharacterRepository? Repository()
        {
            if (tried) return repository;
            tried = true;

            if (!Db.IsConfigured) return null;

            try { repository = new CharacterRepository(new Db()); }
            catch { repository = null; }

            return repository;
        }
    }
}
