using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Turns an inventory into the opaque JSON that goes in a database column,
    /// and back.
    ///
    /// It lives in this project, not in the storage library, for the same reason
    /// CharacterRecord carries its cosmetics as a data_json string: the database
    /// stores the payload, it does not understand it. The columns are the things
    /// something queries; the item list is the thing only the game server can
    /// read. That split is what stops an item-format change from being a schema
    /// migration.
    ///
    /// Round-tripping is total in one direction only, on purpose. Writing always
    /// produces valid JSON. Reading a payload that is corrupt, truncated or from
    /// a future version returns null rather than throwing, because the caller's
    /// correct response is "log it and seed a fresh inventory", not "refuse to
    /// let this player into the world".
    /// </summary>
    public static class InventorySnapshot
    {
        /// <summary>
        /// Stamped into every payload. Bump it when the item shape changes in a
        /// way an older reader would misread; <see cref="Read"/> refuses a
        /// version it does not know rather than silently mis-parsing.
        /// </summary>
        public const int CurrentVersion = 1;

        private static readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            // Off, so the payload reads the same as the field names below and a
            // human debugging a bad row is not translating casing in their head.
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        /// <summary>The JSON payload for an inventory. Never null, never throws.</summary>
        public static string Write(InventoryModel model)
        {
            Payload payload = new Payload
            {
                Version = CurrentVersion,
                Width = model.Width,
                Height = model.Height,
                HasBelt = model.HasBelt,
                BeltRow = model.BeltRow,
                Items = model.Items.Select(ToRow).ToList(),
            };

            return JsonSerializer.Serialize(payload, options);
        }

        /// <summary>
        /// The inventory a payload describes, or null when it is unreadable.
        ///
        /// The grid dimensions are read back rather than assumed: a player who
        /// was given a bigger grid keeps it, and more importantly the client
        /// reads width/height ONCE at checkout, so restoring the wrong
        /// dimensions puts items outside a grid that will never be resized.
        /// </summary>
        public static InventoryModel? Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            Payload? payload;

            try
            {
                payload = JsonSerializer.Deserialize<Payload>(json!, options);
            }
            catch (JsonException)
            {
                return null;
            }

            if (payload == null || payload.Version != CurrentVersion)
            {
                return null;
            }

            if (payload.Width <= 0 || payload.Height <= 0)
            {
                return null;
            }

            InventoryModel model = new InventoryModel(payload.Width, payload.Height, payload.HasBelt, payload.BeltRow);

            if (payload.Items != null)
            {
                foreach (Row row in payload.Items)
                {
                    if (row == null || string.IsNullOrEmpty(row.ItemTypeId))
                    {
                        // One bad row must not cost the player the other
                        // nineteen items.
                        continue;
                    }

                    model.Add(FromRow(row));
                }
            }

            return model;
        }

        private static Row ToRow(InventoryItem item)
        {
            return new Row
            {
                ItemId = item.ItemId,
                ItemTypeId = item.ItemTypeId,
                Amount = item.Amount,
                SlotType = item.SlotType,
                UtilitySlotNum = item.UtilitySlotNum,
                X = item.X,
                Y = item.Y,
                Rotated = item.Rotated,
                HotBarSlotNum = item.HotBarSlotNum,
                TimeToBuild = item.TimeToBuild,
                Quality = item.Quality,
                LockBoxItem = item.LockBoxItem,
                Meta = item.Meta == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(item.Meta.ToDictionary(p => p.Key, p => p.Value)),
                Rarity = item.Rarity,
            };
        }

        private static InventoryItem FromRow(Row row)
        {
            return new InventoryItem(
                row.ItemId,
                row.ItemTypeId!,
                row.Amount,
                // A payload with a missing or nonsense slotType would otherwise
                // blank the whole panel on the next login. Fall back to the one
                // value that is always safe.
                InventoryPolicy.IsLegalSlotType(row.SlotType) ? row.SlotType! : InventoryItem.NotWorn,
                row.UtilitySlotNum,
                row.X,
                row.Y,
                row.Rotated,
                row.HotBarSlotNum,
                row.TimeToBuild,
                row.Quality,
                row.LockBoxItem,
                row.Meta ?? new Dictionary<string, string>(),
                row.Rarity);
        }

        private sealed class Payload
        {
            public int Version { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }

            public bool HasBelt { get; set; }

            public int BeltRow { get; set; }

            public List<Row>? Items { get; set; }
        }

        private sealed class Row
        {
            public int ItemId { get; set; }

            public string? ItemTypeId { get; set; }

            public int Amount { get; set; }

            public string? SlotType { get; set; }

            public int UtilitySlotNum { get; set; }

            public int X { get; set; }

            public int Y { get; set; }

            public bool Rotated { get; set; }

            public int HotBarSlotNum { get; set; }

            public int TimeToBuild { get; set; }

            public int Quality { get; set; }

            public bool LockBoxItem { get; set; }

            public Dictionary<string, string>? Meta { get; set; }

            public int? Rarity { get; set; }
        }
    }
}
