using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A built ship's hull and deck registrations, built in ONE place so the runtime
    /// build-completion spawn and the boot restore cannot diverge.
    ///
    /// The counterpart of <see cref="Placement.PlacedDeployableSpawnPlan"/> for ships:
    /// both <c>Game.Crafting.BuiltShipSpawner.Spawn</c> (runtime) and the boot restore
    /// construct their hull + deck WorldEntities here, from the same
    /// <see cref="BuiltShipPlacement"/> constants, so the hull's all-or-nothing seed
    /// set (190602/1209/1099/1130/8062/8071/4349) and each deck's (190602/1518/1099)
    /// are identical id-for-id on both paths - the only per-ship difference being the
    /// hull bytes (which the serializer resolves per-entity from the built-ship ledger)
    /// and the DERIVED deck panels (which both paths regenerate deterministically from
    /// those same hull bytes via <see cref="DeckGenerator"/>).
    ///
    /// ONE DECK ENTITY PER DERIVED PANEL. Where the static test ship had a single
    /// rectangular deck, a built ship gets one <c>Deck01</c> per panel the client's own
    /// <c>ShipHullPartData.Decks</c> derivation yields for its hull - a floor for every
    /// frame plus the exposed upper deck, sized from the real hull geometry. Each panel
    /// is a separate entity with its own 1518 polygon and its own collider, exactly as
    /// the client's original <c>ShipDeckSpawningVisualizer</c> spawns them.
    ///
    /// Pure and engine-free, so the seed-set parity is asserted natively.
    /// </summary>
    public static class BuiltShipSpawnPlan
    {
        /// <summary>The hull and all derived deck registrations for one built ship.</summary>
        public readonly struct HullAndDecks
        {
            public HullAndDecks(WorldEntity hull, IReadOnlyList<WorldEntity> decks)
            {
                Hull = hull;
                Decks = decks;
            }

            /// <summary>The hull/root entity (carries the per-ship 1209 hull bytes).</summary>
            public WorldEntity Hull { get; }

            /// <summary>
            /// The walkable deck panel entities, one per <see cref="DeckPanel"/>, in the
            /// same order as the panels they were built from - so <c>Decks[i]</c> is the
            /// entity for <c>panels[i]</c> and carries panel key <c>:deck:i</c>.
            /// </summary>
            public IReadOnlyList<WorldEntity> Decks { get; }
        }

        /// <summary>
        /// The hull and deck registrations for a ship built at <paramref name="hullPos"/>,
        /// keyed by <paramref name="sequence"/>, with one deck entity per derived
        /// <paramref name="panels"/> entry. Each deck is placed at the hull position plus
        /// the panel's hull-local offset (the client re-parents it under the hull and the
        /// 190602 branch converts that to a local offset), so a restore from the same hull
        /// bytes reproduces the same standable floors.
        /// </summary>
        public static HullAndDecks For(int sequence, FixedPointPosition hullPos, IReadOnlyList<DeckPanel> panels)
        {
            WorldEntity hull = new WorldEntity(
                BuiltShipPlacement.HullKey(sequence),
                WorldEntities.ShipFrameAssetName,
                WorldEntities.DefaultAssetContext,
                hullPos,
                seedComponents: BuiltShipPlacement.HullSeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer);

            var decks = new List<WorldEntity>(panels.Count);
            for (int i = 0; i < panels.Count; i++)
            {
                ShipVector3 offset = panels[i].HullLocalPositionMetres;
                FixedPointPosition deckPos = new FixedPointPosition(
                    hullPos.X + (long)(offset.X * FixedPointPosition.UnitsPerMetre),
                    hullPos.Y + (long)(offset.Y * FixedPointPosition.UnitsPerMetre),
                    hullPos.Z + (long)(offset.Z * FixedPointPosition.UnitsPerMetre));

                decks.Add(new WorldEntity(
                    BuiltShipPlacement.DeckKey(sequence, i),
                    Deck.AssetName,
                    WorldEntities.DefaultAssetContext,
                    deckPos,
                    seedComponents: BuiltShipPlacement.DeckSeedComponents.ToArray(),
                    order: SpawnOrder.AfterPlayer));
            }

            return new HullAndDecks(hull, decks);
        }
    }
}
