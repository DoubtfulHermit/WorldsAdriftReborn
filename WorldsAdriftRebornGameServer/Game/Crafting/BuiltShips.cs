using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// The server's record of every ship a player has BUILT this session, keyed by the
    /// world-entity ids its hull and deck were spawned as. It is the ship-craft
    /// counterpart of <c>Placement.PlacedShipyards</c>: <see cref="BuiltShipSpawner"/>
    /// writes one hull entry (with the design's hull bytes) and one deck entry when a
    /// build completes, and <c>ComponentsSerializer</c> reads them back to serve
    /// per-entity truth the single global test ship's branches cannot:
    ///
    ///   * the 1209 branch serves a BUILT hull its OWN <see cref="HullBytesFor"/> (the
    ///     player's saved design) instead of the global minimum hull, so different
    ///     builds render as different ships;
    ///   * the 1099 branch treats a built hull like the test hull (empty materials,
    ///     not salvageable) and a built deck like the test deck (one Wood material, so
    ///     ShipDeckVisualizer.OnEnable does not IndexOutOfRange on an empty list).
    ///
    /// In-memory only, exactly like the node/shipyard/databank ledgers: "persistent"
    /// for this milestone means "visible to every connected client until the server
    /// restarts". A restart-durable built-ship ledger is the documented follow-on.
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop and the build
    /// timer's completion callback is drained on it, like every other writer here.
    /// </summary>
    internal static class BuiltShips
    {
        private static readonly Dictionary<long, byte[]> HullBytesByEntityId = new Dictionary<long, byte[]>();

        /// <summary>
        /// Every built deck panel's entity id mapped to its IMMUTABLE 1518 vertex loop -
        /// the panel geometry <see cref="DeckGenerator"/> derived for it, in the deck
        /// entity's own local space (centroid-relative, raw ShipPlan units, pre-scale).
        /// This is static seed data for the life of the ship, not an update stream: the
        /// 1518 serialize branch reads it once per checkout, keyed by the deck's id, so
        /// each panel serves its OWN polygon instead of the one global static rectangle.
        /// The presence of a key also answers <see cref="IsBuiltDeck"/>.
        /// </summary>
        private static readonly Dictionary<long, IReadOnlyList<ShipVector3>> DeckVerticesByEntityId =
            new Dictionary<long, IReadOnlyList<ShipVector3>>();
        // A deck's authored hull-local transform is immutable.  Do not derive it
        // later by subtracting the CURRENT hull registry position from the deck's
        // original world registration: flight/recall relocates the hull seed but
        // deliberately leaves child registrations alone.  Recomputing after a
        // recall was measured shifting every rebuilt deck down by 11 metres.
        private static readonly Dictionary<long, FixedPointPosition> DeckLocalOffsetByEntityId =
            new Dictionary<long, FixedPointPosition>();
        /// <summary>
        /// WHAT EACH BUILT HULL IS MADE OF, keyed by the hull's live entity id.
        /// Populated by the runtime build (from what the craft actually consumed) and
        /// by the boot restore (from the persisted record). A hull with no entry - or
        /// a hull restored from a record written before materials were recorded - is
        /// read as <see cref="Multiplayer.Materials.HullMaterials.Legacy"/>, i.e. the
        /// birch-and-iron the server used to hardcode, so nothing about an existing
        /// ship changes.
        /// </summary>
        private static readonly Dictionary<long, Multiplayer.Materials.HullMaterials> MaterialsByHull =
            new Dictionary<long, Multiplayer.Materials.HullMaterials>();

        private static readonly Dictionary<long, long> HullByDeck = new Dictionary<long, long>();
        private static readonly Dictionary<long, List<long>> DecksByHull = new Dictionary<long, List<long>>();

        /// <summary>
        /// A built hull's PERSISTENT index - its position in the persisted
        /// <c>WorldStateSnapshot.BuiltShips</c> list - keyed by the hull's live entity id.
        /// This is the durable, cross-restart handle a MOUNTED part references its ship by:
        /// the live hull entity id changes every boot, but the index into the append-only,
        /// restore-in-order ship list does not. Populated by the runtime build (from the
        /// index RecordBuiltShip returned) and by the boot restore (the iteration index),
        /// so a mount committed on a restored ship persists against the right index too.
        /// </summary>
        private static readonly Dictionary<long, int> PersistentIndexByHull = new Dictionary<long, int>();

        /// <summary>
        /// A built hull's OWNER character uid, keyed by the hull's live entity id. This is
        /// the identifier Gate B (ship ownership) compares against: the client's
        /// <c>HostileItemPlacingPredicate</c> asks <c>ShipVisualizer.IsShipOwner(SelectedCharacterUid)</c>,
        /// and <c>SelectedCharacterUid</c> is the character uid, so the 8062/4349 owner
        /// serve branches seed THIS value for a built hull that has an owner and an empty
        /// list otherwise. Populated by the runtime build (the shipyard's owner) and the
        /// boot restore (the persisted <c>BuiltShipRecord.OwnerCharacterUid</c>), so an
        /// owned ship stays owned across restart. In-memory only, like the rest of this
        /// ledger; the durable copy lives in the persisted record.
        /// </summary>
        private static readonly Dictionary<long, string> OwnerByHull = new Dictionary<long, string>();

        /// <summary>
        /// The shipyard&lt;-&gt;built-ship dock association, delegated to the PURE
        /// <see cref="Multiplayer.Ship.ShipDockRegistry"/> so the one-to-one, two-way
        /// bookkeeping (and, new for build-access, the hull-&gt;shipyard REVERSE lookup the
        /// hull's 1114 DockableState serve needs) is unit-tested natively. A shipyard's
        /// 1205 <c>ShipyardState.DockedShipId</c> is SINGULAR (one ship per yard); the
        /// registry keeps the forward and reverse maps consistent. This class keeps its
        /// former API so the 1205 branch, the spawner and the undock trigger are
        /// unchanged.
        /// </summary>
        private static Multiplayer.Ship.ShipDockRegistry Docks => Multiplayer.Ship.ShipDockRegistry.Shared;
        private static int _sequence;

        /// <summary>
        /// A never-reused suffix for a built ship's registration keys. The key is what
        /// the world-entity registry allocates a shared entity id from, so it must be
        /// unique for the life of the process; a monotonic counter is the simplest
        /// thing that cannot collide across builds.
        /// </summary>
        internal static int NextSequence()
        {
            return _sequence++;
        }

        /// <summary>
        /// Records a newly built hull's entity id and the hull bytes its 1209 must
        /// serve. The bytes are stored as given (the spawner has already validated and,
        /// if needed, fallen back to the minimum hull), so a serve is a straight lookup.
        /// </summary>
        internal static void RegisterHull(long hullEntityId, byte[] hullBytes)
        {
            HullBytesByEntityId[hullEntityId] = hullBytes;
        }

        /// <summary>
        /// Records a newly built deck PANEL's entity id and the 1518 vertex loop its
        /// ShipDeckState must serve. The vertices come from <see cref="DeckGenerator"/>
        /// (already centroid-relative and pre-scale, as the client expects), so both the
        /// 1099 material branch (via <see cref="IsBuiltDeck"/>) and the 1518 polygon
        /// branch (via <see cref="DeckVerticesFor"/>) are straight lookups.
        /// </summary>
        internal static void RegisterDeck(long hullEntityId, long deckEntityId,
            IReadOnlyList<ShipVector3> localVertices, FixedPointPosition localOffset)
        {
            DeckVerticesByEntityId[deckEntityId] = localVertices;
            DeckLocalOffsetByEntityId[deckEntityId] = localOffset;
            HullByDeck[deckEntityId] = hullEntityId;
            if (!DecksByHull.TryGetValue(hullEntityId, out List<long>? decks))
            {
                decks = new List<long>();
                DecksByHull[hullEntityId] = decks;
            }
            decks.Add(deckEntityId);
        }

        internal static IReadOnlyList<long> DecksForHull(long hullEntityId) =>
            DecksByHull.TryGetValue(hullEntityId, out List<long>? decks)
                ? new List<long>(decks)
                : System.Array.Empty<long>();

        /// <summary>The built hull which owns a deck panel, or null for a non-deck entity.</summary>
        internal static long? HullForDeck(long deckEntityId) =>
            HullByDeck.TryGetValue(deckEntityId, out long hullEntityId)
                ? hullEntityId
                : null;

        /// <summary>Retires one salvaged hull and every deck ledger entry beneath it.</summary>
        internal static IReadOnlyList<long> UnregisterShip(long hullEntityId)
        {
            IReadOnlyList<long> decks = DecksForHull(hullEntityId);
            foreach (long deckId in decks)
            {
                DeckVerticesByEntityId.Remove(deckId);
                DeckLocalOffsetByEntityId.Remove(deckId);
                HullByDeck.Remove(deckId);
            }
            DecksByHull.Remove(hullEntityId);
            HullBytesByEntityId.Remove(hullEntityId);
            PersistentIndexByHull.Remove(hullEntityId);
            OwnerByHull.Remove(hullEntityId);
            MaterialsByHull.Remove(hullEntityId);
            return decks;
        }

        /// <summary>
        /// The 1518 vertex loop a built deck panel must serve, or null if the id is not a
        /// built deck (the caller then serves the global static <c>Deck.LocalVertices</c>).
        /// </summary>
        internal static IReadOnlyList<ShipVector3>? DeckVerticesFor(long entityId)
        {
            return DeckVerticesByEntityId.TryGetValue(entityId, out IReadOnlyList<ShipVector3>? v) ? v : null;
        }

        /// <summary>Whether this entity id is a built ship's hull.</summary>
        internal static bool IsBuiltHull(long entityId)
        {
            return HullBytesByEntityId.ContainsKey(entityId);
        }

        /// <summary>Whether this entity id is a built ship's deck panel.</summary>
        internal static bool IsBuiltDeck(long entityId)
        {
            return DeckVerticesByEntityId.ContainsKey(entityId);
        }

        /// <summary>
        /// Immutable authored offset of a built deck from its hull.  This remains
        /// valid after the hull's live registry seed is relocated by flight or an
        /// operator recall.
        /// </summary>
        internal static FixedPointPosition? LocalOffsetForDeck(long entityId) =>
            DeckLocalOffsetByEntityId.TryGetValue(entityId, out FixedPointPosition offset)
                ? offset
                : null;

        /// <summary>
        /// The hull bytes a built hull's 1209 CustomShipHullState must serve, or null if
        /// the id is not a built hull (the caller then serves the global minimum hull).
        /// </summary>
        internal static byte[]? HullBytesFor(long entityId)
        {
            return HullBytesByEntityId.TryGetValue(entityId, out byte[]? bytes) ? bytes : null;
        }

        /// <summary>How many ships have been built this session.</summary>
        internal static int Count => HullBytesByEntityId.Count;

        /// <summary>
        /// Records a built hull's persistent index (its position in the persisted
        /// <c>BuiltShips</c> list). Called by the runtime build and the boot restore so a
        /// mount committed on this hull can be persisted against the ship's durable index.
        /// </summary>
        internal static void SetPersistentIndex(long hullEntityId, int index)
        {
            PersistentIndexByHull[hullEntityId] = index;
        }

        /// <summary>
        /// The persistent index of the built ship whose live hull is <paramref name="hullEntityId"/>,
        /// or null when the hull is not a persisted built ship (e.g. the static test ship) and
        /// therefore has no durable mount target.
        /// </summary>
        internal static int? PersistentIndexFor(long hullEntityId)
        {
            return PersistentIndexByHull.TryGetValue(hullEntityId, out int index) ? index : (int?)null;
        }

        /// <summary>
        /// Records a built hull's OWNER character uid (Gate B). Called by the runtime
        /// build (owner = the shipyard's owner) and the boot restore (owner = the
        /// persisted record's owner), so the 8062/4349 serve branches can seed ownership.
        /// A null/empty uid is stored as empty (an unowned hull).
        /// </summary>
        internal static void SetOwner(long hullEntityId, string ownerCharacterUid)
        {
            OwnerByHull[hullEntityId] = ownerCharacterUid ?? "";
        }

        /// <summary>
        /// The owner character uid of a built hull, or empty string when the hull is not a
        /// built hull or has no recorded owner (the caller then seeds an UNOWNED, empty
        /// owner list). Never null.
        /// </summary>
        internal static string OwnerFor(long hullEntityId)
        {
            return OwnerByHull.TryGetValue(hullEntityId, out string? uid) ? uid : "";
        }

        /// <summary>
        /// Records what a built hull is made of. Called by the runtime build with the
        /// materials the craft consumed, and by the boot restore with the persisted
        /// ones. A null is stored as the legacy pair rather than left absent, so the
        /// 1099/1257/1121 serve branches never have to special-case it.
        /// </summary>
        internal static void SetMaterials(long hullEntityId, Multiplayer.Materials.HullMaterials? materials)
        {
            MaterialsByHull[hullEntityId] =
                (materials ?? Multiplayer.Materials.HullMaterials.Legacy).OrLegacy();
        }

        /// <summary>
        /// What a built hull is made of. Never null: an unrecorded hull reads as the
        /// birch-and-iron every ship built before this feature actually is.
        /// </summary>
        internal static Multiplayer.Materials.HullMaterials MaterialsFor(long hullEntityId)
        {
            return MaterialsByHull.TryGetValue(hullEntityId, out Multiplayer.Materials.HullMaterials? m)
                ? m
                : Multiplayer.Materials.HullMaterials.Legacy;
        }

        /// <summary>
        /// What the ship a DECK belongs to is made of, so a deck matches its hull.
        /// Falls back to the legacy pair for an unparented or unknown deck.
        /// </summary>
        internal static Multiplayer.Materials.HullMaterials MaterialsForDeck(long deckEntityId)
        {
            long? hull = HullForDeck(deckEntityId);
            return hull.HasValue ? MaterialsFor(hull.Value) : Multiplayer.Materials.HullMaterials.Legacy;
        }

        // ------------------------------------------------------------------
        // ONE SHIP PER SHIPYARD (1205 ShipyardState.DockedShipId is singular).
        // ------------------------------------------------------------------

        /// <summary>
        /// Records that <paramref name="hullEntityId"/> is now the ship docked at
        /// <paramref name="shipyardEntityId"/>. Called by the spawner once a build
        /// completes, so the 1205 serve branch reports it and a further CRAFT on that
        /// yard is refused until it is cleared.
        /// </summary>
        internal static void SetDocked(long shipyardEntityId, long hullEntityId)
        {
            Docks.SetDocked(shipyardEntityId, hullEntityId);
        }

        /// <summary>
        /// The hull entity id docked at a shipyard, or 0 (an INVALID EntityId) when the
        /// yard is empty - exactly the value the 1205 <c>ShipyardState.DockedShipId</c>
        /// seed/update wants for "no ship docked".
        /// </summary>
        internal static long DockedShipFor(long shipyardEntityId)
        {
            return Docks.DockedShipFor(shipyardEntityId);
        }

        /// <summary>
        /// The shipyard entity id a built hull is docked at, or 0 (an INVALID EntityId)
        /// when it is not docked. The hull's own 1114 <c>DockableState.DockEntityId</c>
        /// serve reports this so the client's DockableVisualizer enables and the shipyard
        /// sees an active docked ship for the crafted-part lift.
        /// </summary>
        internal static long ShipyardForHull(long hullEntityId)
        {
            return Docks.ShipyardForHull(hullEntityId);
        }

        /// <summary>Whether a shipyard already holds a built/docked ship (CRAFT gate).</summary>
        internal static bool IsShipyardOccupied(long shipyardEntityId)
        {
            return Docks.IsShipyardOccupied(shipyardEntityId);
        }

        /// <summary>Whether this built hull is docked at some shipyard (the 1114 serve gate).</summary>
        internal static bool IsHullDocked(long hullEntityId)
        {
            return Docks.IsHullDocked(hullEntityId);
        }

        /// <summary>
        /// Clears a shipyard's docked-ship association so a new build is allowed again,
        /// returning the hull entity id that WAS docked (or 0 if the yard was empty).
        /// Used by the debug undock trigger; the caller then re-pushes 1205 with an
        /// invalid DockedShipId so CRAFT is permitted.
        /// </summary>
        internal static long ClearDocked(long shipyardEntityId)
        {
            return Docks.ClearDocked(shipyardEntityId);
        }

        /// <summary>Every shipyard that currently holds a docked ship (debug undock: clear all).</summary>
        internal static IReadOnlyCollection<long> OccupiedShipyards => Docks.OccupiedShipyards;
    }
}
