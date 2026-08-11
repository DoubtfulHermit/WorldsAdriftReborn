using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// A debug-only, headless way to FREE a shipyard's dock so a SECOND ship can be
    /// built and tested before flight exists to fly the first one off. Mirrors the
    /// existing file-poll triggers (<c>Placement.PollTrigger</c> / <c>ShipMoveService</c>):
    /// a human writes a shipyard entity id (or an empty file for "all") to
    /// <c>/tmp/wareborn-undock</c>, and on the next poll this clears that yard's
    /// docked-ship association and pushes a live 1205 ShipyardState update with an
    /// invalid DockedShipId, so the client drops the docked ship and the
    /// ONE-ship-per-yard CRAFT gate opens again.
    ///
    /// LEAST-RISKY OPTION ON PURPOSE. It does NOT despawn the built hull/deck entities
    /// (a RemoveEntity broadcast is the risky part); the old ship simply stops being
    /// "docked to that yard" and stays in the world, which is fine for testing a second
    /// build. Clearing the association is the whole requirement: the gate reads
    /// <c>BuiltShips.IsShipyardOccupied</c>, and after a clear that is false again.
    ///
    /// MULTIPLAYER SAFETY: event-driven (one file write -> one clear), not per-frame,
    /// not a stream. The 1205 push is shared world truth about a shared entity, so it
    /// goes to all peers - the same shape as the spawn-time dock push. Debug-only; the
    /// poll is a single cheap <c>File.Exists</c> twice a second.
    /// </summary>
    internal sealed class ShipUndockTrigger
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private const string DefaultTriggerFile = "/tmp/wareborn-undock";

        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private readonly string _triggerFile;

        public ShipUndockTrigger()
        {
            string? configured = Environment.GetEnvironmentVariable("WAREBORN_UNDOCK_FILE");
            _triggerFile = string.IsNullOrWhiteSpace(configured) ? DefaultTriggerFile : configured.Trim();
        }

        /// <summary>The trigger file path, for the startup banner.</summary>
        public string TriggerFile => _triggerFile;

        /// <summary>
        /// Reads and consumes the trigger file if present and the poll interval has
        /// elapsed. A shipyard entity id clears that yard; an empty/blank file clears
        /// EVERY occupied yard. Safe to call every main-loop turn; cheap when idle.
        /// </summary>
        public void PollTrigger()
        {
            if (_sinceLastPoll.Elapsed < PollInterval)
            {
                return;
            }
            _sinceLastPoll.Restart();

            string text;
            try
            {
                if (!File.Exists(_triggerFile))
                {
                    return;
                }

                // Read then delete, so an undock fires exactly once per write and the
                // server cannot get stuck clearing on a loop if anything below throws.
                text = File.ReadAllText(_triggerFile);
                File.Delete(_triggerFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] undock: could not read " + _triggerFile + ": " + e.Message);
                return;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                foreach (long shipyardId in new List<long>(BuiltShips.OccupiedShipyards))
                {
                    Undock(shipyardId);
                }
                return;
            }

            if (long.TryParse(trimmed, out long requested))
            {
                Undock(requested);
            }
            else
            {
                Console.WriteLine("[warning] undock: could not parse a shipyard entity id from '"
                    + trimmed + "'. Write a numeric id, or an empty file to clear all.");
            }
        }

        private static void Undock(long shipyardEntityId)
        {
            long wasDocked = BuiltShips.ClearDocked(shipyardEntityId);
            if (wasDocked == 0)
            {
                Console.WriteLine("[info] undock: shipyard " + shipyardEntityId
                    + " had no docked ship; nothing to clear.");
                return;
            }

            BuiltShipSpawner.PushUndocked(shipyardEntityId);
            Console.WriteLine("[info] undock: cleared docked ship (hull " + wasDocked + ") from shipyard "
                + shipyardEntityId + "; pushed 1205 DockedShipId=invalid. CRAFT is allowed again.");
        }
    }
}
