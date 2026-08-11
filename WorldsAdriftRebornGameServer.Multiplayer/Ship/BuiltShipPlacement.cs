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

        /// <summary>The walkable deck entity's registration key for build number <paramref name="sequence"/>.</summary>
        public static string DeckKey(int sequence)
        {
            return KeyPrefix + ":" + sequence + ":deck";
        }

        /// <summary>
        /// How far to the side (+X, port-starboard) of the shipyard the hull centre
        /// sits. The one-cell hull is 12 m wide (X) at the client's fixed ShipScale 2,
        /// so its near edge lands (10 - 6) = 4 m from the shipyard's centre - clear of
        /// the console the player just used to build it. Documented and separate so a
        /// live client showing overlap (or too far to reach) is one edit.
        /// </summary>
        public const double SideOffsetMetres = 10.0;

        /// <summary>
        /// How far ABOVE the shipyard's own registered Y the hull's deck plane sits.
        /// The hull's deck plane is at the hull entity's own local y = 0 and nothing on
        /// a one-cell plan hangs below it, so this is the height of the step up onto the
        /// ship - the same 0.5 m stand-off the static test hull uses over its ground
        /// vertex. A metre would clear the terrain more comfortably but might be too
        /// tall to walk up; zero would z-fight the ground.
        /// </summary>
        public const double UpMetres = 0.5;

        /// <summary>
        /// Where a ship built at <paramref name="shipyard"/> materialises: its hull
        /// centre, offset <see cref="SideOffsetMetres"/> to the side and raised
        /// <see cref="UpMetres"/> so the deck plane clears the ground. A pure function
        /// of the shipyard position so the hull, its at-rest 1130 and its deck all
        /// derive from one place and the arithmetic is asserted in tests.
        /// </summary>
        public static FixedPointPosition HullNextTo(FixedPointPosition shipyard)
        {
            return new FixedPointPosition(
                shipyard.X + (long)(SideOffsetMetres * FixedPointPosition.UnitsPerMetre),
                shipyard.Y + (long)(UpMetres * FixedPointPosition.UnitsPerMetre),
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
