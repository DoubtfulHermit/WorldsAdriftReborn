using System;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// THE PHASE 3 SEAM. When a ship-blueprint build timer completes, this is the one
    /// call that hands off to spawning the physical ship. Phase 2 stops here: the
    /// materials have been consumed and the blueprint is done, and completion is made
    /// OBSERVABLE (logged) with everything Phase 3 needs in hand - the shipyard entity,
    /// the acting player, and the saved hull bytes to expand into a hull/deck.
    ///
    /// Phase 3 spawns the real world ship here: <see cref="BuiltShipSpawner"/> allocates
    /// a hull/root entity + a deck entity next to the shipyard, seeds
    /// 190602/1209(hull bytes)/1130-at-rest/recognition 8062/8071/4349 on the hull and
    /// 190602/1518/1099 on the deck, and broadcasts them to every connected peer - the
    /// SAME kind of entity as the proven static test ship, parameterised on the player's
    /// saved hull bytes and positioned next to the console.
    ///
    /// DOCKING (1205.dockedShipId) is NOT wired: the ship spawns FREE next to the yard.
    /// The dock path resolves a DockableVisualizer from the id and the ship is not a
    /// registered dockable this phase; free-spawn is the low-risk observable and docking
    /// is a documented follow-up.
    /// </summary>
    internal static class ShipBuildCompletion
    {
        /// <summary>
        /// Called once, on the build timer's completion, after the blueprint's reserved
        /// materials have been consumed. <paramref name="savedHullBytes"/> is the
        /// selected design's hull geometry blob, ready to expand into a ShipPlan.
        /// </summary>
        internal static void OnBuilt(long shipyardEntityId, long playerEntityId, byte[] savedHullBytes)
        {
            Console.WriteLine("[info] ship build COMPLETE on shipyard " + shipyardEntityId
                + " for player " + playerEntityId + " (" + (savedHullBytes?.Length ?? 0)
                + " hull byte(s) ready). Spawning the built ship next to the shipyard.");

            // Spawn a real, boardable hull+deck next to the shipyard from the player's
            // saved design. One-time AddEntity + static seeds, then ordinary interest
            // serving - not a stream, not a per-frame re-seed.
            BuiltShipSpawner.Spawn(shipyardEntityId, savedHullBytes ?? Array.Empty<byte>());
        }
    }
}
