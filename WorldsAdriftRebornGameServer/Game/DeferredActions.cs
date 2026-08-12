using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// A tiny one-shot "do this after N seconds" queue DRAINED ON THE MAIN POLL LOOP
    /// (<c>DeferredActions.Tick()</c> runs beside <c>TickTreeHarvest</c> / <c>ShipFerry.Tick</c>),
    /// so every scheduled action fires on the same single thread that owns the peer set and
    /// the world-entity registry - no background <c>System.Threading.Timer</c>, no cross-thread
    /// peer enumeration.
    ///
    /// This is how the fidelity "seed the in-progress value, then flip when done" fixes flip:
    ///   * the shipyard fold-out flips 1205 <c>deployed</c> false->true after the clip;
    ///   * a crafted loose part flips 1013 <c>spawning</c> true->false after the dissolve;
    ///   * a station craft completes (spawn + <c>CraftingCompleted</c>) after its craft time.
    /// Each is per-entity and one-shot (the entry is removed when it fires) - NOT a per-frame
    /// relay - which is exactly the multiplayer-safe shape the audit asks for.
    ///
    /// Actions may be KEYED so a still-pending one can be cancelled (a player who leaves
    /// mid-station-craft). An unkeyed action cannot be cancelled and simply fires when due.
    /// The <see cref="Tick"/> drainer copies the due actions out before invoking them, so an
    /// action may safely schedule another.
    /// </summary>
    internal static class DeferredActions
    {
        private sealed class Entry
        {
            internal DateTime DueUtc;
            internal Action Do = () => { };
            internal object? Key;
        }

        private static readonly object Gate = new object();
        private static readonly List<Entry> Pending = new List<Entry>();

        /// <summary>Run <paramref name="action"/> once, on the main loop, after <paramref name="seconds"/>.</summary>
        internal static void After(double seconds, Action action) => Schedule(seconds, action, key: null);

        /// <summary>
        /// Run <paramref name="action"/> once after <paramref name="seconds"/>, tagged with
        /// <paramref name="key"/> so a later <see cref="Cancel"/> can drop it if it has not
        /// fired yet. A fresh schedule under an existing key does NOT replace the old one -
        /// callers that need at-most-one guard that themselves (station craft does).
        /// </summary>
        internal static void AfterKeyed(object key, double seconds, Action action) => Schedule(seconds, action, key);

        private static void Schedule(double seconds, Action action, object? key)
        {
            if (action == null)
            {
                return;
            }
            Entry entry = new Entry
            {
                DueUtc = DateTime.UtcNow.AddSeconds(seconds < 0 ? 0 : seconds),
                Do = action,
                Key = key
            };
            lock (Gate)
            {
                Pending.Add(entry);
            }
        }

        /// <summary>Drop every not-yet-fired action carrying <paramref name="key"/>.</summary>
        internal static void Cancel(object key)
        {
            if (key == null)
            {
                return;
            }
            lock (Gate)
            {
                Pending.RemoveAll(e => Equals(e.Key, key));
            }
        }

        /// <summary>
        /// Fire every action whose delay has elapsed, on the calling (main-loop) thread.
        /// Cheap when idle: one <c>DateTime.UtcNow</c> compare over a usually-empty list. A
        /// throwing action is logged and dropped so one bad flip cannot take the loop down.
        /// </summary>
        internal static void Tick()
        {
            List<Action>? due = null;
            DateTime now = DateTime.UtcNow;
            lock (Gate)
            {
                for (int i = Pending.Count - 1; i >= 0; i--)
                {
                    if (Pending[i].DueUtc <= now)
                    {
                        (due ??= new List<Action>()).Add(Pending[i].Do);
                        Pending.RemoveAt(i);
                    }
                }
            }

            if (due == null)
            {
                return;
            }

            foreach (Action action in due)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Console.WriteLine("[warning] deferred action threw and was dropped: " + e.Message);
                }
            }
        }

        /// <summary>Test/reset seam: forget every pending action.</summary>
        internal static void Clear()
        {
            lock (Gate)
            {
                Pending.Clear();
            }
        }
    }
}
