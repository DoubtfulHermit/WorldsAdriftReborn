using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The PURE, engine-free record of which built ship is docked at which shipyard,
    /// as a BIDIRECTIONAL one-to-one association. A shipyard's <c>1205
    /// ShipyardState.DockedShipId</c> is SINGULAR (one ship per yard), so this is a
    /// pair of mirrored maps, not lists: shipyard-&gt;hull (what does this yard hold)
    /// and hull-&gt;shipyard (which yard is this hull docked at).
    ///
    /// WHY IT EXISTS AS ITS OWN MODULE. The forward direction (shipyard-&gt;hull) was
    /// already needed by the 1205 serve branch (<c>ShipyardState.DockedShipId</c>) and
    /// the one-ship-per-yard CRAFT gate. The REVERSE direction (hull-&gt;shipyard) is what
    /// the docked-ship build-access work needs: the built hull's own <c>1114
    /// DockableState</c> must report the shipyard it is docked at
    /// (<c>DockableState.DockEntityId</c>), so the client's <c>DockableVisualizer</c>
    /// (baked on the ShipFrame prefab, ShipPreprocessor.cs:103, [Require]s 1114) enables
    /// and the shipyard's <c>ShipyardVisualizer.OnDockedShipChanged</c> resolves a real,
    /// enabled dockable - the condition <c>PlayerScannerTool.IsShipyardActive</c>
    /// (Shipyard.DockedShip != null) gates the crafted-part lift on.
    ///
    /// Kept a PURE instance class (not a static ledger) so the association logic - and
    /// especially that the two directions stay consistent through set/clear/overwrite -
    /// is unit-tested natively rather than on a running client. The single process-wide
    /// instance the serializer and spawner share is <see cref="Shared"/>; tests build
    /// their own <c>new ShipDockRegistry()</c> for isolation.
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and every writer
    /// (build completion, undock trigger) is drained on it, like the rest of the
    /// craft/placement ledgers.
    /// </summary>
    public sealed class ShipDockRegistry
    {
        /// <summary>
        /// The one process-wide instance the runtime shares (built-ship spawner writes,
        /// the 1205/1114 serve branches read). Tests do NOT use this - they construct an
        /// isolated <c>new ShipDockRegistry()</c> so static state cannot leak between them.
        /// </summary>
        public static ShipDockRegistry Shared { get; } = new ShipDockRegistry();

        private readonly Dictionary<long, long> _hullByShipyard = new Dictionary<long, long>();
        private readonly Dictionary<long, long> _shipyardByHull = new Dictionary<long, long>();

        /// <summary>
        /// Records that <paramref name="hullEntityId"/> is now the ship docked at
        /// <paramref name="shipyardEntityId"/>, replacing any previous association on
        /// EITHER side so the two maps can never disagree (a yard that re-docks a new
        /// hull drops the old hull's reverse entry; a hull that moves yards drops the old
        /// yard's forward entry).
        /// </summary>
        public void SetDocked(long shipyardEntityId, long hullEntityId)
        {
            // Break any stale pairing on both sides before writing the new one.
            if (_hullByShipyard.TryGetValue(shipyardEntityId, out long oldHull))
            {
                _shipyardByHull.Remove(oldHull);
            }
            if (_shipyardByHull.TryGetValue(hullEntityId, out long oldYard))
            {
                _hullByShipyard.Remove(oldYard);
            }

            _hullByShipyard[shipyardEntityId] = hullEntityId;
            _shipyardByHull[hullEntityId] = shipyardEntityId;
        }

        /// <summary>
        /// The hull entity id docked at a shipyard, or 0 (an INVALID EntityId) when the
        /// yard is empty - exactly the value the 1205 <c>ShipyardState.DockedShipId</c>
        /// seed/update wants for "no ship docked".
        /// </summary>
        public long DockedShipFor(long shipyardEntityId)
        {
            return _hullByShipyard.TryGetValue(shipyardEntityId, out long hullId) ? hullId : 0;
        }

        /// <summary>
        /// The shipyard entity id a built hull is docked at, or 0 (an INVALID EntityId)
        /// when the hull is not docked - exactly the value the hull's 1114
        /// <c>DockableState.DockEntityId</c> serve wants.
        /// </summary>
        public long ShipyardForHull(long hullEntityId)
        {
            return _shipyardByHull.TryGetValue(hullEntityId, out long yardId) ? yardId : 0;
        }

        /// <summary>Whether a shipyard already holds a built/docked ship (CRAFT gate).</summary>
        public bool IsShipyardOccupied(long shipyardEntityId)
        {
            return _hullByShipyard.ContainsKey(shipyardEntityId);
        }

        /// <summary>Whether a built hull is docked at any shipyard (the 1114 serve gate).</summary>
        public bool IsHullDocked(long hullEntityId)
        {
            return _shipyardByHull.ContainsKey(hullEntityId);
        }

        /// <summary>
        /// Clears a shipyard's docked-ship association (both directions) so a new build is
        /// allowed again, returning the hull entity id that WAS docked (or 0 if empty).
        /// </summary>
        public long ClearDocked(long shipyardEntityId)
        {
            if (_hullByShipyard.TryGetValue(shipyardEntityId, out long hullId))
            {
                _hullByShipyard.Remove(shipyardEntityId);
                _shipyardByHull.Remove(hullId);
                return hullId;
            }
            return 0;
        }

        /// <summary>Every shipyard that currently holds a docked ship (debug undock: clear all).</summary>
        public IReadOnlyCollection<long> OccupiedShipyards => new List<long>(_hullByShipyard.Keys);
    }
}
