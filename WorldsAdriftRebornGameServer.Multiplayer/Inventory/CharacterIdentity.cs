using System.Text.Json;

namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Digs the selected character's uid out of the customisation map a client
    /// publishes on 1088 PlayerPropertiesState.
    ///
    /// WHY THIS IS WHERE THE UID COMES FROM. The game server has no login
    /// channel: it speaks ENet to a client that already authenticated against
    /// WorldsAdriftServer, and no packet on that wire carries an account. The
    /// one place identity crosses over is the mod's own appearance publish -
    /// WorldsAdriftReborn/Patching/LoadInGame/CharacterCustomisationVisualizer_Patch.cs
    /// adds the key "bossaNetCharacterData" to the customisation map, whose
    /// value is JSON of the client's CharacterCreationData, and that record has
    /// a characterUid field which our own login server fills with a real Guid
    /// (RosterPolicy replaces an invalid one).
    ///
    /// It is therefore NOT the decompiled client we are trusting for this - it
    /// is code in this repository. What has still never been observed is the
    /// round trip actually happening at runtime, which is what the probe in
    /// PlayerPropertiesState_Handler prints. Everything here returns null rather
    /// than guessing, so an absent or malformed uid becomes a volatile
    /// InventoryKey and a loud log line, never a wrong durable key.
    ///
    /// Pure and total: any input, including hostile JSON, yields null rather
    /// than an exception. It runs on a network path.
    /// </summary>
    public static class CharacterIdentity
    {
        /// <summary>
        /// The customisation-map key the mod publishes the character record
        /// under. Named here so the handler and the parser cannot drift.
        /// </summary>
        public const string CharacterDataKey = "bossaNetCharacterData";

        /// <summary>
        /// The field inside that record. Lower-cased first letter, matching the
        /// client's field name exactly; the JSON is produced by
        /// JToken.FromObject on the client type, so the casing is the field's.
        /// </summary>
        public const string CharacterUidField = "characterUid";

        /// <summary>
        /// The character uid published in this customisation map, or null when
        /// the map has no character record, the record is not JSON, the field is
        /// missing, or the value is not a Guid.
        ///
        /// The last case is not hypothetical: upstream ships the placeholder
        /// "valid-UIDs-have-at-least-one-", which passes the client's own
        /// Contains("-") check and is not a Guid. Refusing it here is what keeps
        /// every player who ever saw that placeholder from sharing one inventory.
        /// </summary>
        public static Guid? UidFrom(IReadOnlyDictionary<string, string>? customisation)
        {
            if (customisation == null)
            {
                return null;
            }

            if (!customisation.TryGetValue(CharacterDataKey, out string? json) || string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            string? raw = FieldFrom(json!, CharacterUidField);

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return Guid.TryParse(raw, out Guid uid) ? uid : (Guid?)null;
        }

        /// <summary>
        /// The key to file this player's inventory under: durable when the uid
        /// arrived, session-scoped when it did not. The single place that
        /// decision is made.
        /// </summary>
        public static InventoryKey KeyFor(long entityId, IReadOnlyDictionary<string, string>? customisation)
        {
            Guid? uid = UidFrom(customisation);

            return uid.HasValue ? InventoryKey.ForCharacter(uid.Value) : InventoryKey.ForSession(entityId);
        }

        private static string? FieldFrom(string json, string field)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);

                JsonElement root = document.RootElement;

                // The mod publishes one object. A future publish of the whole
                // saved list would be an array; take its first element rather
                // than failing, because that is the shape CharacterDataLoader
                // stores and it costs three lines to be right about both.
                if (root.ValueKind == JsonValueKind.Array)
                {
                    if (root.GetArrayLength() == 0)
                    {
                        return null;
                    }

                    root = root[0];
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!root.TryGetProperty(field, out JsonElement value))
                {
                    return null;
                }

                return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
