namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// One row of a scrap item's <c>rewards</c> block, already parsed out of its
    /// tier key. RECOVERED: every field comes verbatim from
    /// <c>Game/Items/Config/itemData.json</c>.
    ///
    /// <para><paramref name="Tier"/> is the integer part of the key and
    /// <paramref name="Ordinal"/> the fractional part - <c>"4"</c> is
    /// (tier 4, ordinal 0) and <c>"4.1"</c> is (tier 4, ordinal 1).</para>
    /// </summary>
    public readonly record struct ScrapReward(
        int Tier, int Ordinal, string ItemTypeId, int Amount, int Quality);

    /// <summary>One material payout: what to put in the bag, how much, at what quality.</summary>
    public readonly record struct SalvageYield(string ItemTypeId, int Amount, int Quality);

    /// <summary>Why a salvage did or did not pay. Every refusal is named so a log line can say which.</summary>
    public enum SalvageOutcome
    {
        /// <summary>The scrap was consumed and every yield landed.</summary>
        Paid,

        /// <summary>The item id names nothing in this inventory.</summary>
        ItemNotHeld,

        /// <summary>Held, but its type is not <c>scrapItem-*</c> - the client would not have offered SALVAGE.</summary>
        NotScrap,

        /// <summary>Worn or stashed. Neither is in the grid, so neither is salvageable.</summary>
        NotInGrid,

        /// <summary>A real scrap item that carries no <c>rewards</c> block at all (e.g. a Founder's Tome).</summary>
        NoRewardBlock,

        /// <summary>The payout would not fit. NOTHING was consumed - see the atomicity note on <see cref="Salvage"/>.</summary>
        NoRoom,
    }

    /// <summary>
    /// The outcome of one SALVAGE click, plus what it paid.
    ///
    /// <see cref="Yields"/> is the payout AGGREGATED PER MATERIAL - one entry per
    /// distinct (material, quality) - not the per-grid-stack breakdown. It is what a
    /// toast should say: a player who salvaged 400 lead wants to read "Salvaged
    /// Lead x400" once, not "x99" five times, even though the grid holds five piles.
    /// </summary>
    public sealed record SalvageResult(
        SalvageOutcome Outcome, int Tier, IReadOnlyList<SalvageYield> Yields)
    {
        public bool Paid => Outcome == SalvageOutcome.Paid;
    }

    /// <summary>Looks a scrap type's parsed reward rows up. False for a type with no block.</summary>
    public delegate bool ScrapRewardLookup(string itemTypeId, out IReadOnlyList<ScrapReward> rewards);

    /// <summary>
    /// WHAT A PIECE OF SCRAP IS WORTH, AND HOW IT REACHES THE BAG.
    ///
    /// This is the missing half of a loop already running in production: 409 loot
    /// containers hand out <c>scrapItem-*</c>, the client already draws SALVAGE on
    /// every one of them (<c>acs/Travellers.UI.PlayerInventory/InventoryTooltipPopup.cs:113</c>
    /// - <c>ItemTypeId.StartsWith("scrapItem-")</c>, PROVED), and the request already
    /// reaches us as <c>1082 tryToConsume</c>. Until this existed the server refused
    /// it, so scrap was a souvenir.
    ///
    /// WHAT IS RECOVERED AND WHAT IS OURS
    /// ----------------------------------
    ///   * RECOVERED - the whole reward table. 134 rows of itemData.json carry a
    ///     <c>rewards</c> object keyed by island tier:
    ///     <c>"3": { "a": 80, "q": 6, "item": "titanium" }</c>. The material, the
    ///     amount and the quality are read verbatim and are never scaled, rolled or
    ///     rounded here. All 21 distinct yield ids are Metal, Wood or Fuel - there is
    ///     no cloth, leather, glass or pigment anywhere in the table, and nothing may
    ///     be added to it (see docs/plans/resource-economy.md Phase 5 step 4).
    ///
    ///   * RECOVERED - that a <c>.1</c>/<c>.2</c> key is a SECOND yield at the SAME
    ///     tier, not a sub-tier. Evidence: all 23 rows that carry one also carry its
    ///     base key (zero orphans), and the sub-key's material NEVER equals the base
    ///     key's - <c>scrapItem-crackedminingdrill</c> tier 3 is bronze 125 AND fuel
    ///     40. So <see cref="Salvage"/> pays every ordinal at the chosen tier. An
    ///     earlier draft of the plan said to resolve tier n to "the highest key whose
    ///     integer part is n", which would have silently dropped the base yield of
    ///     every one of those 23 items.
    ///
    ///   * WAREBORN TUNING - the tier CLAMP. A scrap item only has rows for the tiers
    ///     it was authored for, and a player can carry a tier-1 relic to a tier-4
    ///     island. Refusing there would make the item permanently unsalvageable, which
    ///     is the exact failure this phase exists to remove, so a requested tier is
    ///     clamped into the item's own tier set: the highest authored tier at or below
    ///     the request, else the lowest authored tier. Nothing in the data says this.
    ///
    ///   * WAREBORN TUNING - the SPLIT INTO STACKS. Amounts run to 400 while every
    ///     material's <c>stacksize</c> is 99, so one row cannot be one grid pile. The
    ///     TOTAL paid is exactly the recovered amount; how it is cut into piles is
    ///     ours, and it is the smallest number of full stacks plus a remainder.
    ///
    /// ATOMICITY, which is the one thing that must not be got wrong. Consume and
    /// grant happen together or not at all. The client sets IsWaitingForServer before
    /// it sends and clears it only on a 1081 update, and it has no rollback - so a
    /// salvage that ate the scrap and then failed to place the metal is silent,
    /// permanent item loss. Every mutation below is staged on a COPY of the model and
    /// written back only when the whole payout fits.
    /// </summary>
    public static class ScrapSalvagePolicy
    {
        /// <summary>
        /// The prefix the CLIENT gates its SALVAGE button on, character for
        /// character (<c>InventoryTooltipPopup.cs:113</c>). Anything else never
        /// produces a SALVAGE click, so anything else is refused here too - the two
        /// gates must not disagree.
        /// </summary>
        public const string ScrapPrefix = "scrapItem-";

        /// <summary>
        /// The item-meta key carrying the island tier a piece of scrap came off.
        ///
        /// It rides the existing free-form <c>meta</c> dictionary that
        /// <c>InventoryService.Grant</c> already accepts and persists, DELIBERATELY:
        /// a new column would force the game server and the login server to deploy
        /// together, and a split deploy has already destroyed a character's
        /// progression once.
        /// </summary>
        public const string SourceTierMetaKey = "sourceTier";

        /// <summary>The tier assumed when nothing says otherwise. WAREBORN TUNING.</summary>
        public const int DefaultTier = 1;

        /// <summary>Whether an item type is one the client would offer SALVAGE on.</summary>
        public static bool IsScrap(string? itemTypeId) =>
            !string.IsNullOrEmpty(itemTypeId)
            && itemTypeId!.StartsWith(ScrapPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Splits a <c>rewards</c> key into its tier and its ordinal. <c>"2"</c> is
        /// (2, 0); <c>"2.1"</c> is (2, 1). False for anything that is not one of
        /// those two shapes, so an unrecognised key is dropped rather than guessed at.
        ///
        /// Parsed by hand rather than with <c>double.Parse</c> on purpose: a decimal
        /// parse is culture-sensitive, and on a German-locale host "4.1" reads as
        /// forty-one.
        /// </summary>
        public static bool TryParseTierKey(string? key, out int tier, out int ordinal)
        {
            tier = 0;
            ordinal = 0;

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            int dot = key!.IndexOf('.');

            if (dot < 0)
            {
                return int.TryParse(key, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out tier);
            }

            string head = key.Substring(0, dot);
            string tail = key.Substring(dot + 1);

            if (tail.Contains('.'))
            {
                return false;
            }

            return int.TryParse(head, System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture, out tier)
                   && int.TryParse(tail, System.Globalization.NumberStyles.None,
                       System.Globalization.CultureInfo.InvariantCulture, out ordinal);
        }

        /// <summary>
        /// The tier this item will actually be paid at: the highest tier it has rows
        /// for at or below <paramref name="requested"/>, else its lowest. Null only
        /// when it has no rows at all. WAREBORN TUNING - see the class remarks.
        /// </summary>
        public static int? ResolveTier(IReadOnlyList<ScrapReward>? rewards, int requested)
        {
            if (rewards == null || rewards.Count == 0)
            {
                return null;
            }

            int? atOrBelow = null;
            int lowest = int.MaxValue;

            foreach (ScrapReward reward in rewards)
            {
                if (reward.Tier < lowest)
                {
                    lowest = reward.Tier;
                }

                if (reward.Tier <= requested && (atOrBelow == null || reward.Tier > atOrBelow.Value))
                {
                    atOrBelow = reward.Tier;
                }
            }

            return atOrBelow ?? lowest;
        }

        /// <summary>
        /// Every yield authored for one tier, in ordinal order - the base row first,
        /// then its second and third yields. RECOVERED, verbatim.
        /// </summary>
        public static IReadOnlyList<SalvageYield> YieldsFor(IReadOnlyList<ScrapReward>? rewards, int tier)
        {
            List<ScrapReward> matching = new();

            if (rewards != null)
            {
                foreach (ScrapReward reward in rewards)
                {
                    if (reward.Tier == tier && reward.Amount > 0 && !string.IsNullOrEmpty(reward.ItemTypeId))
                    {
                        matching.Add(reward);
                    }
                }
            }

            matching.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));

            List<SalvageYield> yields = new(matching.Count);

            foreach (ScrapReward reward in matching)
            {
                yields.Add(new SalvageYield(reward.ItemTypeId, reward.Amount, reward.Quality));
            }

            return yields;
        }

        /// <summary>
        /// Cuts a payout into grid piles no larger than the material's own stack
        /// ceiling. WAREBORN TUNING as to the cutting; the TOTAL is the recovered
        /// amount and this must never change it.
        ///
        /// A type whose ceiling reads as unstackable (<c>stacksize &lt;= 1</c>, which
        /// includes the database's -1 default) is passed through as one pile: cutting
        /// it into ones would be worse, not better.
        /// </summary>
        public static IReadOnlyList<SalvageYield> IntoStacks(
            IReadOnlyList<SalvageYield> yields, Func<string, int> stackMax)
        {
            if (stackMax == null) throw new ArgumentNullException(nameof(stackMax));

            List<SalvageYield> stacks = new();

            foreach (SalvageYield yield in yields)
            {
                int ceiling = stackMax(yield.ItemTypeId);

                if (ceiling <= 1 || yield.Amount <= ceiling)
                {
                    stacks.Add(yield);
                    continue;
                }

                int left = yield.Amount;

                while (left > 0)
                {
                    int take = Math.Min(ceiling, left);
                    stacks.Add(new SalvageYield(yield.ItemTypeId, take, yield.Quality));
                    left -= take;
                }
            }

            return stacks;
        }

        /// <summary>
        /// ONE SALVAGE CLICK, end to end: work out what the scrap is worth, and pay
        /// it - or change nothing at all and say why.
        ///
        /// <paramref name="requestedTier"/> is the tier the caller believes the scrap
        /// came from; it is clamped into what the item actually has rows for.
        ///
        /// The model is mutated ONLY on <see cref="SalvageOutcome.Paid"/>. Everything
        /// is staged on <see cref="InventoryModel.Copy"/> and committed with
        /// <see cref="InventoryModel.Reset"/>, so a full bag costs the player nothing.
        /// <paramref name="nextItemId"/> may still be called on a refused attempt -
        /// ids are a monotonic counter and skipping some is harmless, whereas reusing
        /// one makes an existing item vanish from the panel with no error.
        /// </summary>
        public static SalvageResult Salvage(
            InventoryModel model,
            int itemId,
            int requestedTier,
            ScrapRewardLookup rewards,
            ItemFootprintLookup footprints,
            Func<string, int> stackMax,
            Func<int> nextItemId)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            if (footprints == null) throw new ArgumentNullException(nameof(footprints));
            if (stackMax == null) throw new ArgumentNullException(nameof(stackMax));
            if (nextItemId == null) throw new ArgumentNullException(nameof(nextItemId));

            InventoryItem? item = model.ById(itemId);

            if (item == null)
            {
                // Two SALVAGE clicks in flight, or a stale panel. Idempotent by
                // construction: the second one finds nothing and pays nothing.
                return Refused(SalvageOutcome.ItemNotHeld);
            }

            if (!IsScrap(item.ItemTypeId))
            {
                return Refused(SalvageOutcome.NotScrap);
            }

            if (item.IsWorn || item.IsStashed)
            {
                return Refused(SalvageOutcome.NotInGrid);
            }

            if (!rewards(item.ItemTypeId, out IReadOnlyList<ScrapReward> table) || table == null || table.Count == 0)
            {
                return Refused(SalvageOutcome.NoRewardBlock);
            }

            int? tier = ResolveTier(table, TierOf(item, requestedTier));

            if (tier == null)
            {
                return Refused(SalvageOutcome.NoRewardBlock);
            }

            IReadOnlyList<SalvageYield> owed = YieldsFor(table, tier.Value);

            if (owed.Count == 0)
            {
                return Refused(SalvageOutcome.NoRewardBlock);
            }

            InventoryModel staged = model.Copy();

            // The scrap goes first, and that is not only bookkeeping: a 5x3 relic
            // frees fifteen cells the metal it becomes is then allowed to use.
            staged.Remove(itemId);

            foreach (SalvageYield stack in IntoStacks(owed, stackMax))
            {
                InventoryItem? placed = InventoryPolicy.TryStackInto(
                    staged, stack.ItemTypeId, stack.Amount, stack.Quality, stackMax(stack.ItemTypeId));

                placed ??= InventoryPolicy.TryGrant(
                    staged, nextItemId(), stack.ItemTypeId, stack.Amount, stack.Quality,
                    new Dictionary<string, string>(), rarity: null, footprints);

                if (placed == null)
                {
                    // Unknown material, or no room. Either way the staged copy is
                    // discarded and the player still has their scrap.
                    return new SalvageResult(SalvageOutcome.NoRoom, tier.Value, Array.Empty<SalvageYield>());
                }
            }

            model.Reset(staged.Items);

            return new SalvageResult(SalvageOutcome.Paid, tier.Value, owed);
        }

        /// <summary>
        /// The tier to pay this particular item at: what its own <c>meta</c> recorded
        /// when it was created, else the caller's guess.
        ///
        /// The stamp is what makes a multi-tier relic pay the right QUALITY - a
        /// Tonking Puck is 45 aluminium at quality 6, 5 or 10 depending on which
        /// island it came off, and only the container that held it knows which.
        /// </summary>
        public static int TierOf(InventoryItem item, int fallback) =>
            TierFromMeta(item?.Meta, fallback);

        /// <summary>The recorded source tier in an item's meta, or <paramref name="fallback"/>.</summary>
        public static int TierFromMeta(IReadOnlyDictionary<string, string>? meta, int fallback)
        {
            if (meta != null
                && meta.TryGetValue(SourceTierMetaKey, out string? recorded)
                && int.TryParse(recorded, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int tier)
                && tier > 0)
            {
                return tier;
            }

            return fallback;
        }

        private static SalvageResult Refused(SalvageOutcome outcome) =>
            new SalvageResult(outcome, 0, Array.Empty<SalvageYield>());
    }
}
