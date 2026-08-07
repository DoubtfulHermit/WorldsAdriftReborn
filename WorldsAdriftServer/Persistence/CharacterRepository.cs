using WorldsAdriftServer.Helper.CharacterSelection;
using WorldsAdriftServer.Objects.CharacterSelection;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// The stored character roster. Thin glue: it owns the file path and a lock,
    /// and delegates every decision to <see cref="RosterPolicy"/>.
    ///
    /// There is exactly one roster for the whole deployment, not one per account.
    /// The client hardcodes the account portion of the save URL to
    /// "steam/1234" (CharacterSelectionHandler.cs:218) and Steam auth is stubbed,
    /// so the login server genuinely cannot tell two players apart yet. Splitting
    /// this per account is a later change that starts with real platform ids, not
    /// with the storage layout.
    /// </summary>
    internal static class CharacterRepository
    {
        private static readonly object gate = new object();

        private static string RosterPath => JsonFileStore.PathFor("characters", "roster.json");

        /// <summary>
        /// Returns the roster to send to the client, creating and saving a
        /// starter roster the first time the server runs.
        ///
        /// Seeding preserves the upstream behaviour of handing out two ready-made
        /// characters, so there is always a way into the world even if character
        /// creation misbehaves. The difference is that they are now generated
        /// once and kept - upstream re-randomised them on every request, so a
        /// character's face changed each time the list was opened.
        /// </summary>
        internal static List<CharacterCreationData> Load(string serverIdentifier)
        {
            lock (gate)
            {
                List<CharacterCreationData>? stored =
                    JsonFileStore.Read<List<CharacterCreationData>>(RosterPath);

                bool firstRun = stored == null;

                if (firstRun)
                {
                    stored = new List<CharacterCreationData>
                    {
                        Character.GenerateRandomCharacter(serverIdentifier, "Billy Bones"),
                        Character.GenerateRandomCharacter(serverIdentifier, "Long John Silver"),
                    };
                }

                List<CharacterCreationData> roster = RosterPolicy.Normalize(
                    stored,
                    serverIdentifier,
                    () => Character.GenerateNewCharacter(serverIdentifier, "New Traveller"));

                if (firstRun)
                {
                    Console.WriteLine("[info] no stored roster, seeding a new one at " + RosterPath);
                    JsonFileStore.Write(RosterPath, roster);
                }

                return roster;
            }
        }

        /// <summary>
        /// Applies one save from the client and returns the resulting roster.
        /// The caller must send this back: the client re-parses the save response
        /// through the same reader it uses for the character list
        /// (LobbySystem.cs:429-435) and only leaves the creation screen if that
        /// parse succeeds - which is why upstream's "{}" reply left players stuck.
        /// </summary>
        internal static List<CharacterCreationData> Save(
            CharacterCreationData incoming,
            string serverIdentifier)
        {
            lock (gate)
            {
                List<CharacterCreationData>? stored =
                    JsonFileStore.Read<List<CharacterCreationData>>(RosterPath);

                List<CharacterCreationData> roster = RosterPolicy.Upsert(
                    stored,
                    incoming,
                    serverIdentifier,
                    () => Character.GenerateNewCharacter(serverIdentifier, "New Traveller"));

                JsonFileStore.Write(RosterPath, roster);

                Console.WriteLine("[info] saved character '" + incoming?.Name + "' ("
                    + incoming?.characterUid + "); roster now holds "
                    + roster.Count(c => !RosterPolicy.IsEmptySlot(c)) + " character(s).");

                return roster;
            }
        }
    }
}
