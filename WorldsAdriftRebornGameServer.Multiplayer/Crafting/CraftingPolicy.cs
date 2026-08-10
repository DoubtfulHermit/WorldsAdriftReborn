using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// Resolves an itemTypeId to its material category ("Metal", "Wood", ...), or
    /// returns false for a type the item database has never heard of.
    ///
    /// A delegate, like <see cref="ItemFootprintLookup"/>, so the pure policy
    /// never needs the game's ItemHelper. False, not a blank category, because an
    /// unknown type must fall through to an itemTypeId match and nothing else -
    /// never accidentally satisfy a category requirement.
    /// </summary>
    public delegate bool MaterialCategoryLookup(string itemTypeId, out string category);

    /// <summary>One material a single craft removed, for logging and assertions.</summary>
    public readonly record struct ConsumedMaterial(string ItemTypeId, int Amount);

    /// <summary>The result of a crafting transaction: what it did, or why it did nothing.</summary>
    public sealed class CraftOutcome
    {
        private CraftOutcome(bool ok, string reason, int outputItemId, IReadOnlyList<ConsumedMaterial> consumed)
        {
            Ok = ok;
            Reason = reason;
            OutputItemId = outputItemId;
            Consumed = consumed;
        }

        /// <summary>Whether materials were consumed and the output granted.</summary>
        public bool Ok { get; }

        /// <summary>A wire-safe, player-facing reason, empty on success. Sent as CraftingValidationFailed.</summary>
        public string Reason { get; }

        /// <summary>The id assigned to the granted output, or -1 on failure.</summary>
        public int OutputItemId { get; }

        /// <summary>Every material stack the craft drew down, or an empty list on failure.</summary>
        public IReadOnlyList<ConsumedMaterial> Consumed { get; }

        internal static CraftOutcome Fail(string reason) =>
            new CraftOutcome(false, reason, -1, System.Array.Empty<ConsumedMaterial>());

        internal static CraftOutcome Success(int outputItemId, IReadOnlyList<ConsumedMaterial> consumed) =>
            new CraftOutcome(true, string.Empty, outputItemId, consumed);
    }

    /// <summary>
    /// The whole personal-crafting transaction, as pure rules over an
    /// <see cref="InventoryModel"/>: does the bag satisfy a recipe, what does one
    /// craft consume, and what does it grant.
    ///
    /// Why a policy module and not ifs in the 1003 handler: the client validates
    /// nothing the server sends and several crafting failure modes brick the UI
    /// silently (a null schematic NREs the sheet, a slot-length mismatch throws
    /// mid-refresh). The rules therefore live in one place a test can point at,
    /// and the transaction is ATOMIC - it works on a copy and commits only when
    /// every requirement is met AND the output has a home, so a half-consumed bag
    /// can never reach the wire.
    /// </summary>
    public static class CraftingPolicy
    {
        /// <summary>
        /// Whether a material satisfies a requirement. Mirrors the client's
        /// InventoryItemManager.IsSameMaterialType exactly: a requirement name
        /// matches the material's CATEGORY or its itemTypeId, plus the special
        /// case where any Wood or Metal satisfies the literal "Wood/Metal".
        /// </summary>
        public static bool Matches(string requirementName, string itemTypeId, string category)
        {
            if (string.IsNullOrEmpty(requirementName))
            {
                return false;
            }

            if (string.Equals(category, requirementName, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(itemTypeId, requirementName, StringComparison.Ordinal))
            {
                return true;
            }

            return (string.Equals(category, "Wood", StringComparison.Ordinal)
                    || string.Equals(category, "Metal", StringComparison.Ordinal))
                && string.Equals(requirementName, "Wood/Metal", StringComparison.Ordinal);
        }

        /// <summary>
        /// How many units of a matching material sit in the grid for one
        /// requirement, ignoring worn and stashed items.
        ///
        /// Used by the handler to decide how full a craft slot should show while
        /// the player is filling it. It is deliberately per-requirement and so
        /// over-counts a stack that could satisfy two requirements at once; that
        /// is only a display hint. The authoritative shared-material accounting
        /// happens in <see cref="TryCraft"/>, which draws every requirement down a
        /// single running copy.
        /// </summary>
        public static int AvailableFor(InventoryModel model, MaterialCategoryLookup categoryLookup, string requirementName)
        {
            int sum = 0;

            foreach (InventoryItem item in model.Items)
            {
                if (item.IsWorn || item.IsStashed)
                {
                    continue;
                }

                if (!categoryLookup(item.ItemTypeId, out string category))
                {
                    category = string.Empty;
                }

                if (Matches(requirementName, item.ItemTypeId, category))
                {
                    sum += item.Amount;
                }
            }

            return sum;
        }

        /// <summary>
        /// Whether the bag currently satisfies every requirement, accounting for
        /// materials shared between requirements. No mutation; the reason is the
        /// same string <see cref="TryCraft"/> would fail with.
        /// </summary>
        public static bool CanCraft(SchematicRecord schematic, InventoryModel model, MaterialCategoryLookup categoryLookup, out string reason)
        {
            InventoryModel work = model.Copy();
            return TryConsume(schematic, work, categoryLookup, out reason, out _);
        }

        /// <summary>
        /// Validate the recipe against the bag, consume the materials, and grant
        /// the output - all or nothing.
        ///
        /// <paramref name="nextItemId"/> is a factory rather than a plain int so
        /// no item id is spent when the craft is rejected: it is called exactly
        /// once, immediately before the grant, after every requirement has
        /// already been shown to be met.
        /// </summary>
        public static CraftOutcome TryCraft(
            SchematicRecord schematic,
            InventoryModel model,
            MaterialCategoryLookup categoryLookup,
            Func<int> nextItemId,
            int outputQuality,
            IReadOnlyDictionary<string, string> outputMeta,
            int? outputRarity,
            ItemFootprintLookup footprints)
        {
            if (schematic == null)
            {
                return CraftOutcome.Fail("no schematic selected");
            }

            if (string.IsNullOrEmpty(schematic.ItemType))
            {
                return CraftOutcome.Fail("recipe '" + schematic.SchematicId + "' has no output item");
            }

            InventoryModel work = model.Copy();

            if (!TryConsume(schematic, work, categoryLookup, out string reason, out List<ConsumedMaterial> consumed))
            {
                return CraftOutcome.Fail(reason);
            }

            int outputItemId = nextItemId();

            InventoryItem? granted = InventoryPolicy.TryGrant(
                work,
                outputItemId,
                schematic.ItemType,
                Math.Max(1, schematic.AmountToCraft),
                outputQuality,
                outputMeta,
                outputRarity,
                footprints);

            if (granted == null)
            {
                // Unknown output type, or no free cell. The bag is untouched
                // because everything above happened on the copy.
                return CraftOutcome.Fail("could not place output '" + schematic.ItemType + "' (unknown type or no free space)");
            }

            model.Reset(work.Items);
            return CraftOutcome.Success(outputItemId, consumed);
        }

        /// <summary>
        /// Draws every requirement down <paramref name="work"/> in order, so a
        /// material that could satisfy two requirements is only spent once. On
        /// any shortfall it stops and reports, leaving <paramref name="work"/>
        /// partially drawn down - which is why callers always hand it a copy.
        /// </summary>
        private static bool TryConsume(
            SchematicRecord schematic,
            InventoryModel work,
            MaterialCategoryLookup categoryLookup,
            out string reason,
            out List<ConsumedMaterial> consumed)
        {
            consumed = new List<ConsumedMaterial>();

            foreach (CraftingRequirement requirement in schematic.CraftingRequirements)
            {
                int need = requirement.AmountRequired;

                if (need <= 0)
                {
                    continue;
                }

                // Snapshot per requirement so a Remove during this loop does not
                // disturb the enumeration, and so the next requirement sees the
                // already-drawn-down state.
                foreach (InventoryItem item in new List<InventoryItem>(work.Items))
                {
                    if (need <= 0)
                    {
                        break;
                    }

                    if (item.IsWorn || item.IsStashed)
                    {
                        continue;
                    }

                    if (!categoryLookup(item.ItemTypeId, out string category))
                    {
                        category = string.Empty;
                    }

                    if (!Matches(requirement.Name, item.ItemTypeId, category))
                    {
                        continue;
                    }

                    int take = Math.Min(need, item.Amount);

                    if (take >= item.Amount)
                    {
                        work.Remove(item.ItemId);
                    }
                    else
                    {
                        work.Replace(item with { Amount = item.Amount - take });
                    }

                    consumed.Add(new ConsumedMaterial(item.ItemTypeId, take));
                    need -= take;
                }

                if (need > 0)
                {
                    reason = "not enough '" + requirement.Name + "' (need " + requirement.AmountRequired + ")";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }
    }
}
