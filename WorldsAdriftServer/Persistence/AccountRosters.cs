using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Helper.CharacterSelection;
using WorldsAdriftServer.Objects.CharacterSelection;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// One character roster per account, stored in Postgres.
    ///
    /// This replaces the JSON-file repository that kept a single shared roster
    /// for the whole deployment, because the login server had no way to tell two
    /// players apart. It now can - see <see cref="Accounts.SecurityHeader"/> - so
    /// the roster is keyed on the account and the file is only read once more, to
    /// migrate the characters that already exist.
    ///
    /// Every decision about the shape of a roster still belongs to
    /// <see cref="RosterPolicy"/>. This type owns the storage and nothing else.
    /// </summary>
    internal static class AccountRosters
    {
        /// <summary>
        /// The account that inherits the pre-accounts shared roster, by username.
        ///
        /// Before accounts existed there was exactly one roster for the whole
        /// deployment, sitting in characters/roster.json. Those characters belong
        /// to somebody, and without this they would appear to have vanished the
        /// moment accounts shipped. Set this to the operator's own username
        /// before the first login; leave it unset on a fresh deployment.
        ///
        /// It only ever fires for an account with an empty roster, so it cannot
        /// overwrite characters somebody has since created.
        /// </summary>
        internal const string LegacyOwnerVariable = "WAREBORN_LEGACY_ROSTER_OWNER";

        private static readonly object gate = new object();

        /// <summary>
        /// The roster to send to the client, normalised and persisted.
        ///
        /// A new account gets a single empty slot - the create-a-character row -
        /// rather than the two ready-made travellers upstream handed out. Those
        /// existed because there was no character creation worth trusting; there
        /// now is, and seeding strangers into a personal roster is worse than an
        /// empty one.
        /// </summary>
        internal static List<CharacterCreationData> Load(
            AccountRecord account,
            string serverIdentifier)
        {
            lock (gate)
            {
                List<CharacterCreationData> stored = Read(account.AccountId);

                if (stored.Count == 0)
                {
                    stored = InheritLegacyRoster(account);
                }

                List<CharacterCreationData> roster = RosterPolicy.Normalize(
                    stored,
                    serverIdentifier,
                    () => Character.GenerateNewCharacter(serverIdentifier, "New Traveller"));

                Write(account.AccountId, roster);

                return roster;
            }
        }

        /// <summary>
        /// Applies one save and returns the resulting roster. The caller must
        /// send this back: the client re-parses the save response through the
        /// same reader it uses for the character list (LobbySystem.cs:429-435)
        /// and only leaves the creation screen if that parse succeeds.
        /// </summary>
        internal static List<CharacterCreationData> Save(
            AccountRecord account,
            CharacterCreationData incoming,
            string serverIdentifier)
        {
            lock (gate)
            {
                List<CharacterCreationData> roster = RosterPolicy.Upsert(
                    Read(account.AccountId),
                    incoming,
                    serverIdentifier,
                    () => Character.GenerateNewCharacter(serverIdentifier, "New Traveller"));

                Write(account.AccountId, roster);

                Console.WriteLine("[info] saved character '" + incoming?.Name + "' ("
                    + incoming?.characterUid + ") for account '" + account.Username
                    + "'; roster now holds "
                    + roster.Count(c => !RosterPolicy.IsEmptySlot(c)) + " character(s).");

                return roster;
            }
        }

        private static List<CharacterCreationData> Read(long accountId)
        {
            return Accounts.Characters
                .ListForAccount(accountId)
                .Select(CharacterAdapter.ToGameData)
                .Where(c => c != null)
                .Select(c => c!)
                .ToList();
        }

        private static void Write(long accountId, List<CharacterCreationData> roster)
        {
            Accounts.Characters.ReplaceRoster(
                accountId,
                CharacterAdapter.ToRecords(roster, accountId, DateTimeOffset.UtcNow));
        }

        /// <summary>
        /// Hands the pre-accounts shared roster to its owner, once.
        ///
        /// Returns an empty list for everybody else, which is what a new account
        /// should start with.
        /// </summary>
        private static List<CharacterCreationData> InheritLegacyRoster(AccountRecord account)
        {
            string? owner = Environment.GetEnvironmentVariable(LegacyOwnerVariable);

            if (string.IsNullOrWhiteSpace(owner))
            {
                return new List<CharacterCreationData>();
            }

            if (!string.Equals(
                    AccountPolicy.NormalizeUsername(owner),
                    account.UsernameKey,
                    StringComparison.Ordinal))
            {
                return new List<CharacterCreationData>();
            }

            List<CharacterCreationData>? legacy = JsonFileStore.Read<List<CharacterCreationData>>(
                JsonFileStore.PathFor("characters", "roster.json"));

            if (legacy == null || legacy.Count == 0)
            {
                Console.WriteLine("[info] " + LegacyOwnerVariable + " names '" + owner
                    + "' but there is no stored roster.json to inherit.");
                return new List<CharacterCreationData>();
            }

            Console.WriteLine("[info] account '" + account.Username + "' inherits the "
                + legacy.Count(c => !RosterPolicy.IsEmptySlot(c))
                + " character(s) from the pre-accounts roster. The file is left in place; "
                + "unset " + LegacyOwnerVariable + " once this has run.");

            return legacy;
        }
    }
}
