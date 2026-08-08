using Newtonsoft.Json;
using WorldsAdriftReborn.Storage.Records;
using WorldsAdriftServer.Objects.CharacterSelection;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// Converts between the client's character payload and a stored row.
    ///
    /// The storage library deliberately names no game type, so this boundary is
    /// the only place the two shapes meet. Everything the client alone
    /// understands - cosmetics, colours, the tutorial flags - is round-tripped
    /// opaquely in DataJson, so a change to the client's format is not a schema
    /// change.
    /// </summary>
    internal static class CharacterAdapter
    {
        /// <summary>
        /// Turns one roster entry into a row.
        ///
        /// The uid is parsed here rather than stored as text because that parse
        /// has to fail somewhere, and here is the only place it can be handled:
        /// the client's own social code calls new Guid(uid) mid-frame and dies
        /// on anything that is not a Guid. RosterPolicy has already replaced
        /// unusable uids by the time a roster reaches this method, so a failure
        /// here means the policy was bypassed - hence the throw rather than a
        /// quiet substitution that would silently orphan the character.
        /// </summary>
        internal static CharacterRecord ToRecord(
            CharacterCreationData character,
            long accountId,
            int slotIndex,
            DateTimeOffset now)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            if (!Guid.TryParse(character.characterUid, out Guid uid))
            {
                throw new ArgumentException(
                    "Character '" + character.Name + "' has a uid that is not a Guid ('"
                    + character.characterUid + "'). RosterPolicy should have replaced it.",
                    nameof(character));
            }

            return new CharacterRecord(
                uid,
                accountId,
                character.Name ?? string.Empty,
                slotIndex,
                RosterPolicy.IsEmptySlot(character),
                JsonConvert.SerializeObject(character),
                now,
                now);
        }

        /// <summary>
        /// Turns a whole roster into rows, numbering slots by position. Slot
        /// order is what the client renders, and the unique index on
        /// (account_id, slot_index) is what keeps it from drifting.
        /// </summary>
        internal static IReadOnlyList<CharacterRecord> ToRecords(
            IReadOnlyList<CharacterCreationData> roster,
            long accountId,
            DateTimeOffset now)
        {
            List<CharacterRecord> rows = new List<CharacterRecord>(roster.Count);

            for (int i = 0; i < roster.Count; i++)
            {
                rows.Add(ToRecord(roster[i], accountId, i, now));
            }

            return rows;
        }

        /// <summary>
        /// Reads a row back into the shape the client expects. Returns null for a
        /// row whose payload will not parse, so one corrupt character costs that
        /// character rather than the player's whole roster.
        /// </summary>
        internal static CharacterCreationData? ToGameData(CharacterRecord row)
        {
            try
            {
                CharacterCreationData? character =
                    JsonConvert.DeserializeObject<CharacterCreationData>(row.DataJson);

                if (character == null)
                {
                    return null;
                }

                // The row is the authority on the uid: it is the primary key, and
                // the game server resolves a character by it.
                character.characterUid = row.CharacterUid.ToString();
                return character;
            }
            catch (JsonException e)
            {
                Console.WriteLine("[error] character " + row.CharacterUid
                    + " has an unreadable payload and was skipped: " + e.Message);
                return null;
            }
        }
    }
}
