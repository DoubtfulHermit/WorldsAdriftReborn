using Npgsql;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftReborn.Storage.Schema;

namespace WorldsAdriftReborn.Storage.Repositories
{
    /// <summary>
    /// Per-account character rosters.
    ///
    /// This is the only table two processes touch, and only one of them writes:
    /// the login server owns it, and the game server SELECTs from it to resolve a
    /// characterUid. So there is no cross-process transaction to get wrong and no
    /// reverse write path at all.
    ///
    /// Note the name collides with WorldsAdriftServer.Persistence.CharacterRepository,
    /// which is the JSON-backed one this eventually replaces. Different namespace,
    /// and the adapter that converts to and from CharacterCreationData lives over
    /// there - nothing in this library names a game type.
    /// </summary>
    public sealed class CharacterRepository
    {
        private readonly Db db;

        public CharacterRepository(Db db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        private const string Columns =
            "character_uid, account_id, name, slot_index, is_empty_slot, data_json, "
            + "created_at, updated_at";

        /// <summary>
        /// The account's roster, in slot order. Ordered by the database rather
        /// than by the caller because the order is what the client renders and an
        /// unordered SELECT would shuffle the character list between requests.
        /// </summary>
        public IReadOnlyList<CharacterRecord> ListForAccount(long accountId)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM characters WHERE account_id = @account_id "
                + "ORDER BY slot_index;";
            command.Parameters.AddWithValue("account_id", accountId);

            List<CharacterRecord> roster = new List<CharacterRecord>();

            using NpgsqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                roster.Add(Read(reader));
            }

            return roster;
        }

        /// <summary>
        /// One character by uid, whoever owns it. This is the game server's only
        /// query: it has a characterUid off the wire and needs to know whose it
        /// is.
        /// </summary>
        public CharacterRecord? Find(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText =
                "SELECT " + Columns + " FROM characters WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            using NpgsqlDataReader reader = command.ExecuteReader();

            return reader.Read() ? Read(reader) : null;
        }

        /// <summary>
        /// Replaces an account's whole roster with the given rows, in ONE
        /// transaction.
        ///
        /// Whole-roster rather than per-character because that is the shape the
        /// rules already come in: RosterPolicy.Normalize takes a list and returns
        /// the list the client should see, with slots renumbered and the trailing
        /// empty slot placed.
        ///
        /// WHY THIS IS A RECONCILE AND NOT A DELETE-THEN-REINSERT. The obvious
        /// implementation - empty the account's rows, then insert the new ones -
        /// is a DATA-LOSS BUG here, because character_inventories references this
        /// table ON DELETE CASCADE. This method runs on every character-list load
        /// (AccountRosters.Load rewrites the normalised roster each time), so
        /// deleting a surviving character's row - even to reinsert it a
        /// millisecond later under the same uid - CASCADEs their whole inventory
        /// away. A player who did nothing but relog would find their farmed
        /// materials gone, and their characters.created_at reset to "now". So a
        /// character whose uid is still in the roster is UPDATED in place; only a
        /// character genuinely removed from the roster is deleted, and its
        /// inventory cascading with it is then correct - nothing can address it.
        ///
        /// The slot renumber is done without ever letting two live rows share a
        /// slot_index, so the unique index on (account_id, slot_index) is never
        /// transiently violated. That collision-avoidance is the sole reason the
        /// old code emptied the account first; a single spare slot in
        /// 0..MaxCharacters is enough to rotate any permutation through instead.
        ///
        /// One transaction throughout: a durable commit costs milliseconds, so N
        /// autocommits is N times that, and a failure part-way must leave the
        /// previous roster whole rather than half-written.
        /// </summary>
        public void ReplaceRoster(long accountId, IReadOnlyList<CharacterRecord> roster)
        {
            if (roster == null)
            {
                throw new ArgumentNullException(nameof(roster));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();

            // The uids this write keeps. Scoping the DELETE to "everything else"
            // is what lets a survivor's row - and the inventory row that CASCADEs
            // off it - stay put instead of being dropped and reinserted.
            Guid[] keepUids = roster.Select(c => c.CharacterUid).ToArray();

            using (NpgsqlCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText =
                    "DELETE FROM characters WHERE account_id = @account_id "
                    + "AND character_uid <> ALL(@keep);";
                delete.Parameters.AddWithValue("account_id", accountId);
                delete.Parameters.AddWithValue("keep", keepUids);
                delete.ExecuteNonQuery();
            }

            // Move the survivors that stayed to their target slots first, so the
            // upserts below never insert a new row onto a slot a survivor has not
            // yet vacated. Survivors are the rows still present after the delete.
            Dictionary<Guid, int> currentSlots = SurvivorSlots(connection, transaction, accountId);

            Dictionary<Guid, int> targetSlots = new Dictionary<Guid, int>();
            foreach (CharacterRecord character in roster)
            {
                if (currentSlots.ContainsKey(character.CharacterUid))
                {
                    targetSlots[character.CharacterUid] = character.SlotIndex;
                }
            }

            PermuteSurvivorSlots(connection, transaction, accountId, currentSlots, targetSlots);

            foreach (CharacterRecord character in roster)
            {
                // Survivors hit ON CONFLICT DO UPDATE (no cascade, created_at
                // preserved); genuinely-new rows are inserted onto the slots the
                // permutation above left free.
                Insert(connection, transaction, character with { AccountId = accountId });
            }

            transaction.Commit();
        }

        /// <summary>The uid-to-slot map of the rows still present for an account.</summary>
        private static Dictionary<Guid, int> SurvivorSlots(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            long accountId)
        {
            Dictionary<Guid, int> slots = new Dictionary<Guid, int>();

            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT character_uid, slot_index FROM characters WHERE account_id = @account_id;";
            command.Parameters.AddWithValue("account_id", accountId);

            using NpgsqlDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                slots[reader.GetGuid(0)] = reader.GetInt32(1);
            }

            return slots;
        }

        /// <summary>
        /// Walks every surviving row to its target slot without two rows ever
        /// sharing one. A move onto a free slot is applied directly; when every
        /// remaining move is blocked (a cycle, e.g. two characters swapping
        /// slots) one member is first parked on a spare slot to break it. There
        /// is always a spare while fewer than MaxCharacters+1 rows survive, which
        /// a normalised roster guarantees; if one somehow is not, the guard
        /// throws and the transaction rolls back rather than looping.
        /// </summary>
        private static void PermuteSurvivorSlots(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            long accountId,
            Dictionary<Guid, int> current,
            Dictionary<Guid, int> target)
        {
            Dictionary<int, Guid> occupant = new Dictionary<int, Guid>();
            foreach (KeyValuePair<Guid, int> row in current)
            {
                occupant[row.Value] = row.Key;
            }

            int guard = 0;
            int guardLimit = (SchemaScripts.MaxCharacters + 2) * (SchemaScripts.MaxCharacters + 2);

            while (true)
            {
                Guid? mover = null;
                int destination = -1;

                // Prefer a move whose target is already free: it needs no parking.
                foreach (KeyValuePair<Guid, int> want in target)
                {
                    if (current[want.Key] == want.Value)
                    {
                        continue;
                    }

                    if (!occupant.ContainsKey(want.Value))
                    {
                        mover = want.Key;
                        destination = want.Value;
                        break;
                    }
                }

                if (mover == null)
                {
                    // Nobody can reach their target: either everyone is placed,
                    // or the remaining moves form a cycle to be broken by parking.
                    Guid? stuck = null;
                    foreach (KeyValuePair<Guid, int> want in target)
                    {
                        if (current[want.Key] != want.Value)
                        {
                            stuck = want.Key;
                            break;
                        }
                    }

                    if (stuck == null)
                    {
                        return;
                    }

                    mover = stuck;
                    destination = FirstFreeSlot(occupant);
                }

                if (guard++ > guardLimit)
                {
                    throw new InvalidOperationException(
                        "roster slot permutation for account " + accountId + " did not converge.");
                }

                MoveSlot(connection, transaction, accountId, mover.Value, destination, current, occupant);
            }
        }

        private static int FirstFreeSlot(Dictionary<int, Guid> occupant)
        {
            for (int slot = 0; slot <= SchemaScripts.MaxCharacters; slot++)
            {
                if (!occupant.ContainsKey(slot))
                {
                    return slot;
                }
            }

            throw new InvalidOperationException("no free slot to rotate the roster through.");
        }

        private static void MoveSlot(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            long accountId,
            Guid uid,
            int destination,
            Dictionary<Guid, int> current,
            Dictionary<int, Guid> occupant)
        {
            using NpgsqlCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "UPDATE characters SET slot_index = @slot "
                + "WHERE account_id = @account_id AND character_uid = @uid;";
            command.Parameters.AddWithValue("slot", destination);
            command.Parameters.AddWithValue("account_id", accountId);
            command.Parameters.AddWithValue("uid", uid);
            command.ExecuteNonQuery();

            occupant.Remove(current[uid]);
            occupant[destination] = uid;
            current[uid] = destination;
        }

        /// <summary>
        /// Inserts or updates one character, keyed on its uid.
        ///
        /// created_at is preserved on update - the client re-sends the whole
        /// entry on every save, so taking its word for the creation time would
        /// reset it on every edit.
        /// </summary>
        public void Save(CharacterRecord character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            using NpgsqlConnection connection = db.Open();
            using NpgsqlTransaction transaction = connection.BeginTransaction();

            Insert(connection, transaction, character);

            transaction.Commit();
        }

        /// <summary>Removes one character. Returns false if it was not there.</summary>
        public bool Delete(Guid characterUid)
        {
            using NpgsqlConnection connection = db.Open();
            using NpgsqlCommand command = connection.CreateCommand();

            command.CommandText = "DELETE FROM characters WHERE character_uid = @uid;";
            command.Parameters.AddWithValue("uid", characterUid);

            return command.ExecuteNonQuery() == 1;
        }

        private static void Insert(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CharacterRecord character)
        {
            using NpgsqlCommand command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText =
                "INSERT INTO characters (" + Columns + ") VALUES ("
                + "@uid, @account_id, @name, @slot_index, @is_empty_slot, @data_json, "
                + "@created_at, @updated_at) "
                + "ON CONFLICT (character_uid) DO UPDATE SET "
                + "account_id = excluded.account_id, "
                + "name = excluded.name, "
                + "slot_index = excluded.slot_index, "
                + "is_empty_slot = excluded.is_empty_slot, "
                + "data_json = excluded.data_json, "
                + "updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("uid", character.CharacterUid);
            command.Parameters.AddWithValue("account_id", character.AccountId);
            command.Parameters.AddWithValue("name", character.Name);
            command.Parameters.AddWithValue("slot_index", character.SlotIndex);
            command.Parameters.AddWithValue("is_empty_slot", character.IsEmptySlot);
            command.Parameters.AddWithValue("data_json", character.DataJson);
            command.Parameters.AddWithValue("created_at", Timestamps.ToDb(character.CreatedAt));
            command.Parameters.AddWithValue("updated_at", Timestamps.ToDb(character.UpdatedAt));

            command.ExecuteNonQuery();
        }

        private static CharacterRecord Read(NpgsqlDataReader reader)
        {
            return new CharacterRecord(
                reader.GetGuid(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetBoolean(4),
                reader.GetString(5),
                Timestamps.FromDb(reader.GetDateTime(6)),
                Timestamps.FromDb(reader.GetDateTime(7)));
        }
    }
}
