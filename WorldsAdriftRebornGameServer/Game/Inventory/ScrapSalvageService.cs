using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Inventory
{
    /// <summary>
    /// THE SALVAGE BUTTON, SERVED. Turns one <c>1082 tryToConsume</c> on a
    /// <c>scrapItem-*</c> into the metal, wood or fuel that scrap was worth.
    ///
    /// This is the missing half of a loop whose other half is already in
    /// production: 409 loot containers hand scrap out, the client has always drawn
    /// SALVAGE on it (<c>InventoryTooltipPopup.cs:113</c>) and always sent the
    /// request, and this server has always refused it with "no consumable effects".
    /// Nothing new goes on the wire - <c>1082</c> already arrives, <c>1081</c>
    /// already answers, and the <c>8060</c> toast is the same one a mined rock
    /// fires.
    ///
    /// WHY THE DECISION IS NOT HERE. Everything that can be wrong about a payout -
    /// which tier, which yields, how they are cut into piles, and above all whether
    /// consume-and-grant is atomic - lives in the pure
    /// <see cref="ScrapSalvagePolicy"/>, where it is unit-tested on Linux with no
    /// game install. What is left here is the three things the pure project is not
    /// allowed to know: the item database, the peer push, and the HUD toast.
    ///
    /// WHY NO PUSH OF ITS OWN. The 1082 handler pushes 1081 unconditionally for any
    /// request it receives, accepted or refused, because the client clears
    /// IsWaitingForServer on nothing else. A push here as well would send the whole
    /// inventory twice and write the database twice for one click.
    ///
    /// <para>NOT EXPOSED TO THE DefaultModel TRAP. This serves 1081 nowhere new: it
    /// mutates the inventory of the player's OWN entity, which is bound by the
    /// checkout path long before a SALVAGE click can arrive, and it never calls
    /// <c>InventoryService.ForEntity</c> on anything else. The gauntlet trap needs a
    /// NON-player entity to reach ForEntity without a specific model first.</para>
    /// </summary>
    internal static class ScrapSalvageService
    {
        /// <summary>
        /// Salvages one item out of a player's own inventory. Returns whether it
        /// paid.
        ///
        /// Every refusal is logged with its reason, because the player-visible
        /// symptom of all of them is identical - the item is still there - and
        /// "nothing happened and nobody knows why" is the failure mode this whole
        /// area was rebuilt to remove.
        /// </summary>
        internal static bool TrySalvage( long playerEntityId, int itemId )
        {
            InventoryModel model = InventoryService.ForEntity(playerEntityId);

            // Read before the payout: a successful salvage removes the item, and the
            // toast's reason string wants to name what was taken apart.
            string salvaged = model.ById(itemId)?.ItemTypeId ?? "scrap";

            SalvageResult result = ScrapSalvagePolicy.Salvage(
                model,
                itemId,
                ScrapSalvagePolicy.DefaultTier,
                InventoryWire.ScrapRewards,
                InventoryWire.Footprints,
                InventoryWire.StackMaxOf,
                () => InventoryService.NextItemId(playerEntityId));

            if (!result.Paid)
            {
                Console.WriteLine("[info] refusing to salvage item " + itemId + " for entity "
                    + playerEntityId + ": " + Explain(result.Outcome)
                    + ". The inventory will be re-pushed so the panel does not stick.");
                return false;
            }

            Console.WriteLine("[salvage] entity " + playerEntityId + " salvaged item " + itemId
                + " at tier " + result.Tier + " -> " + Describe(result.Yields) + ".");

            // The toast is fired only for a payout that has already landed in the
            // model. A player told "Salvaged Lead x140" who then finds nothing in the
            // panel is the one outcome worth more care than any other here, which is
            // why the grant is atomic and this loop runs after it, never beside it.
            foreach (SalvageYield yield in result.Yields)
            {
                SalvageFeedback.Send(playerEntityId, yield.ItemTypeId, yield.Amount,
                    "salvaged " + salvaged);
            }

            return true;
        }

        /// <summary>
        /// A refusal in words. Named per outcome rather than "could not salvage",
        /// because these five have completely different causes and only the log can
        /// tell them apart.
        /// </summary>
        private static string Explain( SalvageOutcome outcome ) => outcome switch
        {
            SalvageOutcome.ItemNotHeld =>
                "the inventory holds no item with that id (a second SALVAGE click on"
                + " something already salvaged looks exactly like this, and is harmless)",
            SalvageOutcome.NotScrap =>
                "it is not a scrapItem-* type, so the client should never have offered SALVAGE on it",
            SalvageOutcome.NotInGrid => "it is worn or stashed, and neither is in the grid",
            SalvageOutcome.NoRewardBlock =>
                "its itemData.json row carries no rewards block, so there is nothing recovered to pay",
            SalvageOutcome.NoRoom =>
                "the payout does not fit in the remaining grid. NOTHING was consumed - the scrap is still there",
            _ => outcome.ToString(),
        };

        private static string Describe( IReadOnlyList<SalvageYield> yields )
        {
            List<string> parts = new(yields.Count);

            foreach (SalvageYield yield in yields)
            {
                parts.Add(yield.Amount + "x " + yield.ItemTypeId + " (q" + yield.Quality + ")");
            }

            return string.Join(", ", parts);
        }
    }
}
