using System;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// THE ONE DRAIN SEAM for ship-blueprint teardown. A ship-blueprint build physically
    /// REMOVES items from the bag the instant they are reserved into a material slot
    /// (<see cref="ShipBlueprintTransaction.AddItem"/>). Every path that DROPS a build
    /// before it is crafted - the player re-selecting or clearing the blueprint, or
    /// disconnecting - must therefore return those reserved-but-uncrafted items to the
    /// inventory FIRST, or they are destroyed. This routes all of those through one place.
    ///
    /// A build that is mid-craft (<see cref="ShipBlueprintBuild.IsCrafting"/>) keeps its
    /// materials: they are consumed for real, and <see cref="ShipBlueprintTransaction.ReturnAll"/>
    /// refuses them (returns 0), so a teardown of a crafting build correctly returns nothing.
    /// </summary>
    internal static class ShipBuildTeardown
    {
        /// <summary>
        /// F3 - interactive re-select / clear. Return this build's UNCRAFTED reserved
        /// materials to the player's inventory and PUSH the authoritative 1081 (which
        /// un-greys the client's optimistically greyed inventory), BEFORE the build is
        /// replaced or cleared in the store. Returns how many items were returned (0 while
        /// crafting or when nothing is loaded).
        /// </summary>
        internal static int DrainBuildBackToInventory(long playerEntityId, ShipBlueprintBuild build)
        {
            InventoryModel inventory = InventoryService.ForEntity(playerEntityId);
            int returned = ShipBlueprintTransaction.ReturnAll(build, inventory);
            if (returned > 0)
            {
                InventoryPush.Push(playerEntityId,
                    "returned " + returned + " ship-blueprint material(s) on blueprint switch/clear");
            }
            return returned;
        }

        /// <summary>
        /// F2 - disconnect. Return every UNCRAFTED reserved material of EVERY build this
        /// player holds (on any shipyard) back into their inventory, BEFORE the inventory
        /// is saved and dropped by the disconnect path. No push here: the imminent
        /// <see cref="InventoryService.Forget"/> persists the mutated model and the peer is
        /// leaving, so a send is pointless (and would target a half-torn-down peer). Order
        /// matters - this MUST run before the inventory save, or the bag is persisted
        /// already depleted. Returns the total number of items returned.
        /// </summary>
        internal static int DrainAllForPlayerOnDisconnect(long playerEntityId)
        {
            int total = 0;
            foreach ((long shipyardId, ShipBlueprintBuild build) in ShipBlueprintBuildStore.BuildsOf(playerEntityId))
            {
                InventoryModel inventory = InventoryService.ForEntity(playerEntityId);
                int returned = ShipBlueprintTransaction.ReturnAll(build, inventory);
                total += returned;
                if (returned > 0)
                {
                    Console.WriteLine("[info] ship-build teardown: returned " + returned
                        + " uncrafted material(s) from a build on shipyard " + shipyardId
                        + " to entity " + playerEntityId + " before the disconnect inventory save.");
                }
            }
            return total;
        }
    }
}
