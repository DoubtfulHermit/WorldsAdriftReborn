using System;
using System.Collections.Generic;
using System.Threading;
using Bossa.Travellers.Craftingstation;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The server-side build timer for a ship blueprint. When CRAFT is accepted, the
    /// handler starts one of these; after the recipe's craftingTime it clears the
    /// blueprint, re-pushes an authoritative 1271 with isCrafting=false, and calls the
    /// Phase 3 completion seam.
    ///
    /// The isCrafting flag on 1271 is the CONFIRMED observable: it drives the shipyard
    /// atomizer VFX on/off in <c>ShipBlueprintCraftingBehaviour</c>. So starting the
    /// craft turns the VFX on, and completion turns it off - visible with no client mod.
    ///
    /// MULTIPLAYER SAFETY: one timer per (shipyard, player) build, event-driven, fires
    /// exactly once. The completion push is addressed only to the acting peer - the
    /// shipyard is shared, so a broadcast would flip another player's crafting VFX. It
    /// is not a stream and not relayed.
    /// </summary>
    internal static class ShipBuildTimerService
    {
        // Timers are held so they are not garbage-collected before they fire, and so a
        // player who leaves mid-build can have theirs cancelled.
        private static readonly object Gate = new object();
        private static readonly Dictionary<(long shipyard, long player), Timer> Running =
            new Dictionary<(long, long), Timer>();

        /// <summary>Floor so a zero/negative craftingTime cannot fire instantly or hang.</summary>
        private const int MinCraftingSeconds = 1;

        /// <summary>
        /// Start the build timer for a craft that has just been accepted
        /// (<see cref="ShipBlueprintTransaction.StartCraft"/> returned Started, so
        /// <c>build.IsCrafting</c> is already true). Fires once after the recipe time.
        /// </summary>
        internal static void Start(ENetPeerHandle peer, long shipyardEntityId, long playerEntityId,
            ShipBlueprintBuild build)
        {
            int seconds = Math.Max(MinCraftingSeconds, build.CraftingTime);
            var key = (shipyardEntityId, playerEntityId);

            Timer timer = new Timer(_ => Complete(peer, shipyardEntityId, playerEntityId),
                null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);

            lock (Gate)
            {
                if (Running.TryGetValue(key, out Timer? old))
                {
                    old.Dispose();
                }
                Running[key] = timer;
            }

            Console.WriteLine("[info] ship build STARTED on shipyard " + shipyardEntityId
                + " for player " + playerEntityId + "; timer " + seconds + "s (isCrafting=true).");
        }

        /// <summary>Cancel a player's build timers when they leave, so a stale peer is never used.</summary>
        internal static void ForgetPlayer(long playerEntityId)
        {
            lock (Gate)
            {
                List<(long, long)> toRemove = new List<(long, long)>();
                foreach (KeyValuePair<(long shipyard, long player), Timer> entry in Running)
                {
                    if (entry.Key.player == playerEntityId)
                    {
                        entry.Value.Dispose();
                        toRemove.Add(entry.Key);
                    }
                }
                foreach ((long, long) k in toRemove)
                {
                    Running.Remove(k);
                }
            }
        }

        private static void Complete(ENetPeerHandle peer, long shipyardEntityId, long playerEntityId)
        {
            var key = (shipyardEntityId, playerEntityId);
            lock (Gate)
            {
                if (Running.TryGetValue(key, out Timer? t))
                {
                    t.Dispose();
                    Running.Remove(key);
                }
            }

            ShipBlueprintBuild? build = ShipBlueprintBuildStore.Get(shipyardEntityId, playerEntityId);
            if (build == null)
            {
                // The build was cleared (blueprint deselected, player left) before the
                // timer fired. Nothing to complete.
                Console.WriteLine("[info] ship build timer fired for shipyard " + shipyardEntityId
                    + " / player " + playerEntityId + " but no build remains; skipping completion.");
                return;
            }

            // The hull bytes to hand Phase 3: the player's currently loaded design, or
            // their first saved frame. Phase 2 does not yet map a blueprint id to a
            // specific hull, so this is the best-available design.
            byte[] hullBytes = ResolveHullBytes(playerEntityId);

            // Clear the consumed materials and stop crafting.
            build.DrainAllLoaded();
            build.IsCrafting = false;

            // Authoritative 1271 to the acting peer only: isCrafting=false (atomizer VFX
            // off) and the now-empty material bill.
            try
            {
                ShipBlueprintCraftingState.Update crafting = new ShipBlueprintCraftingState.Update();
                crafting.SetBlueprintId(new Improbable.Collections.Option<string>(build.BlueprintId));
                crafting.SetSchematics(ShipBlueprintSchematicMapper.ToSchematics(build));
                crafting.SetCraftingTime(build.CraftingTime);
                crafting.SetIsCrafting(false);
                SendOPHelper.SendComponentUpdateOp(peer, shipyardEntityId,
                    new List<uint> { 1271 }, new List<object> { crafting });
            }
            catch (Exception e)
            {
                // A disconnected peer must not take the server down; the build still
                // completes for Phase 3 purposes.
                Console.WriteLine("[warning] ship build completion could not push 1271 for shipyard "
                    + shipyardEntityId + " / player " + playerEntityId + ": " + e.Message);
            }

            // Hand off to Phase 3, carrying WHAT THE PLAYER PAID IN so the finished
            // ship remembers its own substance instead of defaulting to birch+iron.
            ShipBuildCompletion.OnBuilt(
                shipyardEntityId, playerEntityId, hullBytes, build.LoadedMaterials());
        }

        private static byte[] ResolveHullBytes(long playerEntityId)
        {
            PlayerShipDesigns designs = ShipDesignStore.For(playerEntityId);
            if (designs.WorkingHull != null)
            {
                return (byte[])designs.WorkingHull.Clone();
            }
            if (designs.Slots.Count > 0)
            {
                return (byte[])designs.Slots[0].Data.Clone();
            }
            return Array.Empty<byte>();
        }
    }
}
