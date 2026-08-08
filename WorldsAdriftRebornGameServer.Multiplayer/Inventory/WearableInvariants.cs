namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// Derives the three parallel arrays of 1280 WearableUtilsState from the
    /// inventory, so they cannot drift out of step with it.
    ///
    /// Derived, never edited in place, because every rule the client enforces
    /// here is enforced by UNGUARDED code that throws EVERY FRAME rather than
    /// once:
    ///
    /// - itemIds, healths and active are indexed positionally, so an `active`
    ///   list shorter than `itemIds` is an IndexOutOfRangeException per frame.
    /// - an id in itemIds that GearWearablesVisualizer never registered is a
    ///   KeyNotFoundException per frame. It registers from 1081, requiring
    ///   slotType != "None" AND a parseable meta["totalHealth"] of at least
    ///   0.01 - so an id here that fails either test is a frame-rate cliff, not
    ///   a missing hat.
    ///
    /// The old equip handler wrote these arrays by hand with a single-element
    /// list, which is why equipping a second garment REPLACED the first. There
    /// is no hand-written path any more.
    /// </summary>
    public static class WearableInvariants
    {
        /// <summary>
        /// The meta key holding an item's maximum health. Mandatory on a worn
        /// item: missing, unparseable or below 0.01 and the client never
        /// registers the item at all.
        /// </summary>
        public const string TotalHealthKey = "totalHealth";

        /// <summary>The smallest total health the client will accept.</summary>
        public const float MinimumTotalHealth = 0.01f;

        /// <summary>
        /// The 1280 arrays for this inventory: one entry per worn item that the
        /// client can actually register, in inventory order.
        ///
        /// An item worn but WITHOUT a usable totalHealth is deliberately left
        /// out of all three arrays rather than given a made-up health. Including
        /// it would be the KeyNotFoundException-per-frame case above; leaving it
        /// out costs a durability bar that never had a value to show.
        /// </summary>
        public static WearableArrays For(InventoryModel model)
        {
            List<int> itemIds = new();
            List<float> healths = new();
            List<bool> active = new();

            foreach (InventoryItem item in model.Items)
            {
                if (!item.IsWorn)
                {
                    continue;
                }

                float? total = TotalHealthOf(item);

                if (total == null)
                {
                    continue;
                }

                itemIds.Add(item.ItemId);
                healths.Add(total.Value);
                active.Add(true);
            }

            return new WearableArrays(itemIds, healths, active);
        }

        /// <summary>
        /// An item's registerable total health, or null when the client would
        /// refuse it.
        /// </summary>
        public static float? TotalHealthOf(InventoryItem item)
        {
            if (item.Meta == null || !item.Meta.TryGetValue(TotalHealthKey, out string? raw))
            {
                return null;
            }

            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float total))
            {
                return null;
            }

            return total >= MinimumTotalHealth ? total : (float?)null;
        }
    }

    /// <summary>The three positionally-indexed lists of 1280, guaranteed equal in length.</summary>
    public sealed record WearableArrays(
        IReadOnlyList<int> ItemIds,
        IReadOnlyList<float> Healths,
        IReadOnlyList<bool> Active)
    {
        /// <summary>How many wearables are described. All three lists are this long.</summary>
        public int Count => ItemIds.Count;

        /// <summary>
        /// Whether the three lists agree in length. The one thing a test can
        /// assert that the client would otherwise assert with an exception per
        /// frame.
        /// </summary>
        public bool IsConsistent => ItemIds.Count == Healths.Count && Healths.Count == Active.Count;
    }
}
