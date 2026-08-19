using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Loot;

namespace WorldsAdriftRebornGameServer.Game.Loot
{
    /// <summary>
    /// PUTS THE LOOT IN THE CHEST - the one glue seam between the pure roll
    /// (<see cref="LootTable"/>) and the inventory store the 1081 serve reads.
    ///
    /// THE BUG THIS EXISTS TO PREVENT, stated first because it is not obvious and
    /// it is silent. The 1081 branch in <c>ComponentsSerializer</c> is already
    /// entity-generic: it calls <c>InventoryService.ForEntity(entityId)</c> with no
    /// check that the entity is a player. But <c>ForEntity</c>'s create-factory is
    /// <c>InventoryWire.DefaultModel</c>, which is the PLAYER STARTER KIT - a 10x18
    /// belt grid pre-filled with the four gauntlets and the stash. Serve 1081 on a
    /// container without binding it first and the first thing a player ever finds in
    /// a ruin is a set of gauntlets, in a grid shaped like their own inventory.
    ///
    /// So the rule is: <see cref="Ensure"/> runs BEFORE anything asks
    /// <c>ForEntity</c> about a container. It binds the entity to its session key
    /// with a CONTAINER factory, and <c>InventoryStore.Bind</c> calls a factory at
    /// most once per key - so the roll happens exactly once, a re-checkout re-serves
    /// the same items, and a second peer opening the same chest sees what the first
    /// one sees.
    ///
    /// WHY A SESSION KEY. A container's contents are not durable yet; making them so
    /// is Phase 2 of docs/plans/loot-containers.md and it is the phase that needs a
    /// schema migration. Under a session key a chest works perfectly for the length
    /// of a session and refills on restart, which is honest and costs nothing - and
    /// <see cref="InventoryKey.IsDurable"/> being false is exactly what stops the
    /// persistence layer from writing a chest into a player's row.
    ///
    /// WHY IT REPORTS WHAT IT COULD NOT PLACE. The recovered scrap footprints run up
    /// to 5x3 and the container grid is 10x6, so a bad roll can genuinely run out of
    /// room. Dropping an item silently would make a chest quietly poorer than the
    /// table says it is, which is unfalsifiable from the outside; a log line makes it
    /// a tuning question instead of a mystery.
    /// </summary>
    internal static class LootStock
    {
        /// <summary>
        /// Binds and fills this container's inventory if it has never been bound.
        /// A no-op for an entity that is not a registered container, and for a
        /// container that already has one. Returns true when it stocked something.
        /// </summary>
        internal static bool Ensure(long entityId)
        {
            if (!LootContainerLedger.IsContainer(entityId))
            {
                // ORDERING GUARD. Activation is supposed to have run long before
                // anything asks for this entity's 1081, and by construction it has:
                // ResourceInterestService binds an id for every streamed resource in
                // its constructor, ActivateBoundResources then registers all of them
                // at boot, and the non-streamed path activates inside AddWorldEntity
                // itself. Both routes precede any client component interest.
                //
                // The guard is here anyway because the failure it prevents is
                // PERMANENT and SILENT. If a container ever reached the 1081 branch
                // unactivated, ForEntity would bind it to InventoryWire.DefaultModel -
                // the player starter kit - and InventoryStore.Bind runs its factory
                // at most once per key, so that chest would hold four gauntlets for
                // the rest of the session with no way to correct it. Self-healing
                // from the registry key costs one lookup on a path that only runs at
                // checkout, and the warning says an invariant moved.
                if (!TryAdoptFromRegistry(entityId))
                {
                    return false;
                }
            }

            if (InventoryService.KeyOf(entityId) != null)
            {
                // Already bound - either stocked by an earlier serve, or emptied by a
                // player. Either way its contents are now the store's business.
                return false;
            }

            IReadOnlyList<LootDrop> drops = LootContainerLedger.ContentsOf(entityId);
            int placed = InventoryService.BindContainer(
                entityId,
                LootContainers.GridWidth,
                LootContainers.GridHeight,
                LootContainers.HasBelt,
                LootContainers.BeltRow,
                drops,
                // The island tier this chest belongs to, stamped onto every item it
                // holds. Salvaging reads it back to pay the right quality; without it
                // a relic looted on a tier-4 island would pay its tier-1 numbers.
                LootContainerLedger.TierOf(entityId));

            if (placed < drops.Count)
            {
                Console.WriteLine("[loot] container " + entityId + " ('"
                    + (LootContainerLedger.KeyOf(entityId) ?? "?") + "') rolled " + drops.Count
                    + " items but only " + placed + " fit its "
                    + LootContainers.GridWidth + "x" + LootContainers.GridHeight
                    + " grid; the rest were dropped. Lower LootTable.MaxItems or widen the grid.");
            }
            else
            {
                Console.WriteLine("[loot] stocked container " + entityId + " ('"
                    + (LootContainerLedger.KeyOf(entityId) ?? "?") + "', tier "
                    + LootContainerLedger.TierOf(entityId) + ") with " + placed + " items.");
            }

            return placed > 0;
        }

        /// <summary>
        /// Registers a container the ledger has somehow not seen, from its world
        /// registration key. Returns false for anything that is not a loot container.
        /// </summary>
        private static bool TryAdoptFromRegistry(long entityId)
        {
            string? key = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(entityId)?.Key;
            if (!LootContainers.IsLootKey(key))
            {
                return false;
            }

            int tier = Multiplayer.Islands.ReleaseWorldLoot.TierForKey(key)
                ?? LootScrapTable.MinTier;
            LootContainerLedger.Register(entityId, key!, tier);

            Console.WriteLine("[warning] [loot] container '" + key + "' (entity " + entityId
                + ") was asked for its inventory before WorldResourceActivation had"
                + " registered it. Adopted at tier " + tier + " so it does not open onto"
                + " the player starter kit - but the activation ordering has changed and"
                + " that is worth understanding rather than leaving to this guard.");
            return true;
        }
    }
}
