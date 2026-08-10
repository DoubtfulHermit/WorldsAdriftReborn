using System.Reflection;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Game.Items
{
    /// <summary>
    /// The recipe catalogue, loaded from a file exactly the way
    /// <see cref="ItemHelper"/> loads itemData.json: read once, from
    /// Game/Items/Config/schematicData.json next to the assembly, and cached.
    ///
    /// TWO shapes come out of one file, on purpose:
    ///   - <see cref="RawJson"/> is the file's bytes, served verbatim to the
    ///     client over 1097 SendSchematicData. The client parses the FULL
    ///     SchematicData field set, so nothing the file carries may be dropped -
    ///     which is why the wire never sees a re-serialised object, only the
    ///     original text.
    ///   - <see cref="All"/> is a case-insensitive deserialise into the trimmed
    ///     <see cref="SchematicRecord"/> the server acts on. Unknown JSON fields
    ///     are ignored, so the catalogue can grow (a real recovered recipe set is
    ///     a separate track) without touching this loader.
    ///
    /// The loader assumes nothing about which keys the file holds; it reads
    /// whatever is there and derives <see cref="DefaultSchematicIds"/> from the
    /// keys, so a coordinator can swap the whole file and the server follows.
    /// </summary>
    public static class SchematicHelper
    {
        private static readonly string schematicPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "Game/Items/Config/schematicData.json");

        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private static Dictionary<string, SchematicRecord>? _records;
        private static string? _rawJson;

        /// <summary>The catalogue file's contents, verbatim, for the 1097 wire.</summary>
        public static string RawJson
        {
            get
            {
                EnsureLoaded();
                return _rawJson!;
            }
        }

        /// <summary>Every recipe, keyed by schematicId, as the server acts on it.</summary>
        public static IReadOnlyDictionary<string, SchematicRecord> All
        {
            get
            {
                EnsureLoaded();
                return _records!;
            }
        }

        /// <summary>The recipe with this id, or null when the catalogue has never heard of it.</summary>
        public static SchematicRecord? Get(string schematicId)
        {
            if (string.IsNullOrEmpty(schematicId))
            {
                return null;
            }

            return All.TryGetValue(schematicId, out SchematicRecord? record) ? record : null;
        }

        /// <summary>
        /// The ids to seed into 1079 defaultSchematics so every recipe shows in
        /// the player's book with no learning step. Derived from the file's keys,
        /// never hard-coded, so it tracks whatever the catalogue contains.
        /// </summary>
        public static Improbable.Collections.List<string> DefaultSchematicIds()
        {
            Improbable.Collections.List<string> ids = new Improbable.Collections.List<string>();

            foreach (string id in All.Keys)
            {
                ids.Add(id);
            }

            return ids;
        }

        private static void EnsureLoaded()
        {
            if (_records != null)
            {
                return;
            }

            _rawJson = File.ReadAllText(schematicPath);

            Dictionary<string, SchematicRecord> parsed =
                JsonSerializer.Deserialize<Dictionary<string, SchematicRecord>>(_rawJson, ReadOptions)
                ?? new Dictionary<string, SchematicRecord>();

            // The schematicId inside a record is optional in the file; when it is
            // missing the dictionary key is the source of truth, so backfill it.
            foreach (KeyValuePair<string, SchematicRecord> entry in parsed)
            {
                if (string.IsNullOrEmpty(entry.Value.SchematicId))
                {
                    entry.Value.SchematicId = entry.Key;
                }
            }

            _records = parsed;

            Console.WriteLine("[info] loaded " + _records.Count + " schematic(s) from schematicData.json");
        }
    }
}
