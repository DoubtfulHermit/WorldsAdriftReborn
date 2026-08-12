using WorldsAdriftRebornGameServer.Multiplayer.Placement;

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
    /// - an id in itemIds for an item the client cannot resolve to a rig
    ///   UtilityItem is ALSO a KeyNotFoundException per frame, and this is the one that
    ///   floods the load-in. GearWearablesVisualizer.RegisterWearables only ever adds an
    ///   id to its _utilityIdToUtility dict when one of
    ///   CharacterCustomisationVisualizer.UtilityItems has a matching ItemTypeId, and
    ///   that array is populated by EXACTLY ONE path: AddCosmetic -> AddUtilityItem, run
    ///   ONLY for the four character slots in _slotTypeToUtilityIds - Utility,
    ///   UtilityHead, UtilityFeet, UtilityHand (VERIFIED CharacterCustomisationVisualizer
    ///   .cs: the AddCosmetic switch at :701-706 and the _slotTypeToUtilityIds map at
    ///   :63-81). So the ONLY worn item types the client can register are ones equipped
    ///   in one of those four utility slots. Everything else worn with a totalHealth meta
    ///   is a per-frame crash:
    ///     * a TOOL-slot item (pistol, torch, guitar, horn, hipLamp, headTorch - all
    ///       characterSlot="Tool"+totalHealth in itemData.json) - CharacterSlotType.Tool
    ///       is NOT in _slotTypeToUtilityIds, so it never becomes a UtilityItem. This is
    ///       the flood that survived the deployable-only fix.
    ///     * a garment (Head/Body/Feet/Face) with a totalHealth meta - a cosmetic, never
    ///       a UtilityItem either.
    ///   AND a utility-slot item still crashes if the client builds no UtilityItem for it,
    ///   which happens when CreateItem finds no customisation prefab (AddCosmetic returns
    ///   early, :684-687) - exactly the case for a DEPLOYABLE/placeable (shipyard, barrel,
    ///   campFire, containers...): all are characterSlot="Utility"+totalHealth so they
    ///   PASS the slot test, but are placed structures with no rig prefab. Every one of
    ///   them is in the Deployables table, so Deployables.IsDeployable is the exact
    ///   "utility slot but no rig UtilityItem" discriminator.
    ///
    ///   Net include-rule (see <see cref="For"/>): worn AND in a utility slot AND not a
    ///   deployable AND has totalHealth. For the shipped data that is the glider and any
    ///   future genuine worn utility with a rig mesh - and nothing else. The 1081/1280
    ///   co-push was never internally inconsistent; the mismatch is with the CLIENT'S rig,
    ///   which only this rule can honour.
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
        /// The four character slots the client turns into a rig UtilityItem - the
        /// ONLY slots whose worn item GearWearablesVisualizer can register. These are
        /// the keys of CharacterCustomisationVisualizer._slotTypeToUtilityIds
        /// (CharacterSlotType.Utility / UtilityHead / UtilityFeet / UtilityHand); an
        /// item worn in any other slot (Tool, Head, Body, Feet, Face, Pet) never gets a
        /// UtilityItem and so must never appear active in 1280. Ordinal because the
        /// client parses slotType with a case-sensitive Enum.Parse.
        /// </summary>
        private static readonly HashSet<string> RegisterableUtilitySlots =
            new(StringComparer.Ordinal) { "Utility", "UtilityHead", "UtilityFeet", "UtilityHand" };

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

                // Only the four UTILITY character slots ever become a client
                // UtilityItem (CharacterCustomisationVisualizer._slotTypeToUtilityIds).
                // A Tool-slot item (pistol/torch/guitar/horn/lamp) or a garment
                // (Head/Body/Feet/Face) is worn (slotType != "None") and may carry a
                // totalHealth meta, but the client builds no UtilityItem for it, so its
                // id in 1280 is a KeyNotFoundException every frame. This is the flood the
                // deployable-only rule missed - Tool-slot held items.
                if (!RegisterableUtilitySlots.Contains(item.SlotType))
                {
                    continue;
                }

                // A DEPLOYABLE/placeable in a utility slot (shipyard, barrel, campFire,
                // containers, lamps - all characterSlot="Utility"+totalHealth) passes the
                // slot test but has NO customisation prefab, so the client's CreateItem
                // returns null and AddCosmetic never builds a UtilityItem for it. Every
                // placeable is in the Deployables table, so this is the exact
                // "utility slot but no rig item" discriminator. Excluded before the
                // totalHealth gate because it WOULD pass it.
                if (Deployables.IsDeployable(item.ItemTypeId))
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
