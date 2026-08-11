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
    /// Phase 3 will replace the body with the real world spawn: allocate a hull/root
    /// entity, seed 190602/1209(hull bytes)/1130-at-rest/recognition 8062/8071/4349,
    /// generate deck entities, and optionally dock it via 1205.dockedShipId - all per
    /// docs/research/findings-ship-craft-build.md "Phase 3". None of that happens yet.
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
                + " hull byte(s) ready). Phase 3 will spawn the built ship here.");

            // Phase 3: spawn the built ship here.
            // - allocate a hull/root entity id (stable across peers)
            // - seed 190602 TransformState, 1209 CustomShipHullState(savedHullBytes),
            //   1130 SSPPredictedMotionState at rest, recognition 8062/8071/4349
            // - generate deck entities (1518 ShipDeckState) from the same hull plan
            // - optionally set 1205.dockedShipId on the shipyard
        }
    }
}
