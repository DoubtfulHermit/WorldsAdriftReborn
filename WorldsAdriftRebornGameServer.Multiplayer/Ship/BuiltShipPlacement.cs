using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pure, engine-free policy for WHERE a freshly-built ship materialises and
    /// WHAT registration keys + seed component sets its hull and deck carry. Phase 3
    /// of the ship-craft work: a completed blueprint build spawns a real, boardable
    /// hull+deck next to the shipyard that built it.
    ///
    /// This half is deliberately pure - no ENet, no Improbable types, no game install -
    /// so the offset arithmetic and the all-or-nothing seed sets are asserted on
    /// natively (BuiltShipPlacementTests) rather than by staring at a running client.
    /// The impure half - allocate the entity ids, broadcast AddEntity + seeds to every
    /// connected peer, record the per-ship hull bytes - lives in
    /// <c>Game.Crafting.BuiltShipSpawner</c> in the server assembly.
    ///
    /// THE SEED SETS ARE THE TEST SHIP'S, PARAMETERISED. A built hull carries EXACTLY
    /// the component set the proven static test hull carries
    /// (<see cref="WorldEntities.HullSeedComponents"/> with recognition on: 190602,
    /// 1209, 1099, 1130, 8062, 8071, 4349) - the only difference is that its 1209
    /// CustomShipHullState.hullData is the PLAYER'S saved design rather than the global
    /// minimum hull, which the serializer resolves per-entity from the built-ship
    /// ledger. The client's interest batch on a ship hull is ALL-OR-NOTHING, so the
    /// set must match the proven one id-for-id or the whole batch drops and the ship
    /// renders invisible. A built deck carries the proven deck's readers:
    /// <see cref="DeckSeedComponents"/> (190602 position, 1518 polygon, 1099 material).
    ///
    /// STATIC THIS PHASE. The hull is seeded with ONE at-rest 1130 control point (by
    /// the serializer, from the hull's own registered position) and no motion stream;
    /// the deck is spawned WORLD-ABSOLUTE on top of the hull (its key is not a
    /// bolted-part key, so the 190602 branch seeds it absolute rather than
    /// hull-relative). That is a solid, standable floor for a ship that does not move.
    /// Making a built deck ride a MOVING built hull (the hull-relative parent + carry
    /// path the static test ship uses via <see cref="Deck.HierarchyKey"/>) is a Phase 4
    /// concern and needs the bolted-part parent resolution to name the BUILT hull, not
    /// the single global <see cref="WorldEntities.ShipFrameKey"/> it names today.
    /// </summary>
    public static class BuiltShipPlacement
    {
        /// <summary>
        /// The registration-key prefix a built ship's hull and deck are allocated their
        /// shared entity ids from. A per-build sequence number is appended so every
        /// built ship gets its own stable ids on every peer; the suffix distinguishes
        /// the hull entity from the deck entity within one build.
        /// </summary>
        public const string KeyPrefix = "built-ship";

        /// <summary>The hull/root entity's registration key for build number <paramref name="sequence"/>.</summary>
        public static string HullKey(int sequence)
        {
            return KeyPrefix + ":" + sequence + ":hull";
        }

        /// <summary>
        /// The walkable deck entity's registration key for build number <paramref name="sequence"/>.
        /// Retained for the legacy single-deck path; dynamic decks use the indexed
        /// <see cref="DeckKey(int,int)"/> overload, one key per derived panel.
        /// </summary>
        public static string DeckKey(int sequence)
        {
            return KeyPrefix + ":" + sequence + ":deck";
        }

        /// <summary>
        /// The registration key of derived deck PANEL <paramref name="panelIndex"/> for
        /// build number <paramref name="sequence"/>: <c>built-ship:{sequence}:deck:{panelIndex}</c>.
        /// Every panel is its own world entity so the client builds one collider per
        /// panel exactly as its own <c>ShipDeckSpawningVisualizer</c> would. The index is
        /// the panel's position in <see cref="DeckGenerator.Generate"/>'s deterministic
        /// output, so the same hull bytes regenerate the same keys on a restore.
        /// </summary>
        public static string DeckKey(int sequence, int panelIndex)
        {
            return KeyPrefix + ":" + sequence + ":deck:" + panelIndex;
        }

        /// <summary>The suffix a built ship's HULL registration key ends with.</summary>
        private const string HullSuffix = ":hull";

        /// <summary>The suffix a built ship's LEGACY single-deck registration key ends with.</summary>
        private const string DeckSuffix = ":deck";

        /// <summary>The infix marking a built ship's INDEXED deck-panel registration key.</summary>
        private const string DeckInfix = ":deck:";

        /// <summary>
        /// The HULL registration key sibling of a built ship's DECK key, or null if
        /// <paramref name="deckKey"/> is not a built-ship deck key.
        ///
        /// THE PART-MOUNT MAKE-OR-BREAK. A player can only place a ship part on a
        /// surface that is a genuine Unity CHILD of the ship root: the client's
        /// <c>AttachToShip</c> refuses to send <c>1070 PlacePart</c> unless
        /// <c>spatialOsEntity.HasParentEntity(shipEntity)</c>, and that walks the Unity
        /// <c>transform.parent</c> chain (EntityX.HasParentEntity), NOT the SpatialOS
        /// interest graph. Our built ship spawns the hull and the deck as TWO separate
        /// entities and, this phase, seeds the deck WORLD-ABSOLUTE (parent absent), so
        /// the deck is not a Unity child of the hull and the player has no valid surface
        /// to place onto - mounting fails silently client-side. Making the built deck a
        /// Unity child of its built hull (190602 parent = Parent(hullId, "deck"), the
        /// same real hierarchy key the static test deck uses, resolved in the
        /// serializer) is what turns it into a placeable surface. This helper is the
        /// pure hull-id resolution: a built deck's hull is its sibling by SEQUENCE, so
        /// <c>built-ship:N:deck</c> -&gt; <c>built-ship:N:hull</c>, string-for-string,
        /// with no ledger lookup - asserted natively rather than by staring at a client.
        /// </summary>
        public static string? HullKeyForDeckKey(string? deckKey)
        {
            if (deckKey == null
                || !deckKey.StartsWith(KeyPrefix + ":", System.StringComparison.Ordinal))
            {
                return null;
            }

            // An INDEXED panel key "built-ship:N:deck:M": the hull is the sibling before
            // ":deck:", so strip everything from the infix on. Checked first because a
            // ":deck:M" key also ends with a digit, not the legacy ":deck".
            int infix = deckKey.IndexOf(DeckInfix, System.StringComparison.Ordinal);
            if (infix >= 0)
            {
                return deckKey.Substring(0, infix) + HullSuffix;
            }

            // The legacy singular key "built-ship:N:deck".
            if (deckKey.EndsWith(DeckSuffix, System.StringComparison.Ordinal))
            {
                return deckKey.Substring(0, deckKey.Length - DeckSuffix.Length) + HullSuffix;
            }

            return null;
        }

        /// <summary>
        /// The hull's own body height in metres. A one-cell frame is "roughly 12 m
        /// across, 4 m fore-to-aft and 3.4 m tall" at the client's fixed ShipScale 2
        /// (<see cref="ShipHull"/>), and the hull's deck plane is at the hull entity's
        /// own local y = 0 with nothing hanging below it - so the whole hull body sits
        /// BETWEEN y = 0 and y = +3.4 above its registration. Named so the hover height
        /// below is derived from geometry, not a bare literal.
        /// </summary>
        public const double HullBodyHeightMetres = 3.4;

        /// <summary>
        /// The clearance gap left between the shipyard (and the console + dome the
        /// player builds at) and the underside of the docked hull. A couple of metres:
        /// enough that the ship visibly floats above the yard and clears the console
        /// geometry, small enough that the ship still reads as docked TO this yard
        /// rather than drifting off high above it.
        /// </summary>
        public const double HoverClearanceMetres = 2.6;

        /// <summary>
        /// How far ABOVE the shipyard's own registered Y the built hull hovers: the
        /// hull's deck plane (its local y = 0) is raised this many metres so the whole
        /// hull body floats clear of the yard, reading as a ship DOCKED ABOVE the
        /// shipyard (as in WA), not one sitting on the ground beside it.
        ///
        /// Derived from geometry rather than picked blind: the hull body is
        /// <see cref="HullBodyHeightMetres"/> (3.4 m) tall and the deck plane is its
        /// lowest point, so raising the deck plane by 3.4 m alone would leave the hull
        /// body starting exactly at the yard's top; adding <see cref="HoverClearanceMetres"/>
        /// (2.6 m) opens a visible float gap that also clears the console/dome. The
        /// resulting ~6 m is a modest "docked above" height - a few metres, not way up.
        /// Documented and separate so a live client showing it too low (clipping the
        /// dome) or too high (detached) is one edit.
        /// </summary>
        public const double HoverHeightMetres = HullBodyHeightMetres + HoverClearanceMetres;

        /// <summary>
        /// Where a ship built at <paramref name="shipyard"/> materialises: centred
        /// HORIZONTALLY on the shipyard (same X and Z) and raised <see cref="HoverHeightMetres"/>
        /// so the hull hovers a modest height directly ABOVE the yard - docked above it,
        /// not beside it. A pure function of the shipyard position so the hull, its
        /// at-rest 1130 and its deck (kept centred on the hull by <see cref="Deck.OnHull"/>)
        /// all derive from one place and the arithmetic is asserted in tests.
        /// </summary>
        public static FixedPointPosition HullNextTo(FixedPointPosition shipyard)
        {
            return new FixedPointPosition(
                shipyard.X,
                shipyard.Y + (long)(HoverHeightMetres * FixedPointPosition.UnitsPerMetre),
                shipyard.Z);
        }

        /// <summary>
        /// Where the built deck materialises: centred on its hull by
        /// <see cref="Deck.OnHull"/> (the deck polygon is origin-centred, so the deck
        /// centre coincides with the hull centre), so a player standing on it is over
        /// the hull. Seeded WORLD-ABSOLUTE this phase (the static ship does not move),
        /// which the 190602 branch does for any entity whose key is not a bolted-part
        /// key.
        /// </summary>
        public static FixedPointPosition DeckOn(FixedPointPosition hull)
        {
            return Deck.OnHull(hull);
        }

        /// <summary>
        /// The hull bytes a built ship's 1209 should carry: the player's saved design
        /// when it decodes as a ShipPlan, else the minimum starter hull. Pure, so the
        /// "validate or fall back" decision is asserted natively; the spawner records the
        /// result in the built-ship ledger and logs which branch it took.
        ///
        /// The fallback is not cosmetic caution: 1209's client-side <c>ShipPlan.Load</c>
        /// THROWS on a null/empty/corrupt blob, into the CLIENT's log where we cannot see
        /// it, and the visible result is a hull that renders nothing. Validating here and
        /// substituting the known-good minimum hull keeps a bad design from producing an
        /// invisible ship. A FRESH minimum-hull array per fallback call (ShipHull's own
        /// contract) so no two ships can share a mutable hull buffer.
        /// </summary>
        public static byte[] ResolveHullBytes(byte[]? saved, out bool usedFallback)
        {
            if (ShipPlanModel.TryDecode(saved, out ShipPlanModel? _, out string? _))
            {
                usedFallback = false;
                return saved!;
            }

            usedFallback = true;
            return ShipHull.MinimumHullData();
        }

        /// <summary>
        /// The hull's full proactive seed set - EXACTLY the proven static test hull's,
        /// recognition included, so the all-or-nothing interest batch matches id-for-id.
        /// The only per-build difference is the CONTENTS of 1209, resolved by the
        /// serializer from the built-ship ledger; the id list is identical.
        /// </summary>
        public static IReadOnlyList<uint> HullSeedComponents => WorldEntities.HullSeedComponents(recogniseShip: true);

        /// <summary>
        /// The deck's proactive seed set: 190602 TransformState (position), 1518
        /// ShipDeckState (the polygon), 1099 SalvageAndRepairState (the one Wood
        /// material ShipDeckVisualizer.OnEnable indexes without which it throws). These
        /// three are the client's ShipDeckVisualizer readers plus the transform; every
        /// id has a ComponentsSerializer branch, so the all-or-nothing batch survives
        /// and the client builds a solid BoxCollider deck.
        ///
        /// NOT 8066: the deck's ShipDeckVisualizer does not read it, and this phase's
        /// static deck needs no hull membership to be a floor. When flight arrives the
        /// deck gains the hull-relative parent + 8066 pointing at the BUILT hull.
        /// </summary>
        public static readonly IReadOnlyList<uint> DeckSeedComponents =
            new uint[] { 190602, 1518, 1099 };
    }
}
