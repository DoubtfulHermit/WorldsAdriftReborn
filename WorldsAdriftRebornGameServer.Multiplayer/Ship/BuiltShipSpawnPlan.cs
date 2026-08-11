using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A built ship's hull and deck registrations, built in ONE place so the runtime
    /// build-completion spawn and the boot restore cannot diverge.
    ///
    /// The counterpart of <see cref="Placement.PlacedDeployableSpawnPlan"/> for ships:
    /// both <c>Game.Crafting.BuiltShipSpawner.Spawn</c> (runtime) and the boot restore
    /// construct their hull+deck WorldEntities here, from the same
    /// <see cref="BuiltShipPlacement"/> constants, so the hull's all-or-nothing seed
    /// set (190602/1209/1099/1130/8062/8071/4349) and the deck's (190602/1518/1099)
    /// are identical id-for-id on both paths - the only per-ship difference being the
    /// hull bytes, which the serializer resolves per-entity from the built-ship ledger.
    ///
    /// Pure and engine-free, so the seed-set parity is asserted natively.
    /// </summary>
    public static class BuiltShipSpawnPlan
    {
        /// <summary>The hull and deck registrations for build number <paramref name="sequence"/> at <paramref name="hullPos"/>.</summary>
        public readonly struct HullAndDeck
        {
            public HullAndDeck(WorldEntity hull, WorldEntity deck)
            {
                Hull = hull;
                Deck = deck;
            }

            /// <summary>The hull/root entity (carries the per-ship 1209 hull bytes).</summary>
            public WorldEntity Hull { get; }

            /// <summary>The walkable deck entity, seeded world-absolute on top of the hull.</summary>
            public WorldEntity Deck { get; }
        }

        /// <summary>
        /// The hull and deck registrations for a ship built at <paramref name="hullPos"/>,
        /// keyed by <paramref name="sequence"/>. The deck position is derived from the
        /// hull position exactly as at build time
        /// (<see cref="BuiltShipPlacement.DeckOn"/>), so a restore reproduces the same
        /// standable floor.
        /// </summary>
        public static HullAndDeck For(int sequence, FixedPointPosition hullPos)
        {
            FixedPointPosition deckPos = BuiltShipPlacement.DeckOn(hullPos);

            WorldEntity hull = new WorldEntity(
                BuiltShipPlacement.HullKey(sequence),
                WorldEntities.ShipFrameAssetName,
                WorldEntities.DefaultAssetContext,
                hullPos,
                seedComponents: BuiltShipPlacement.HullSeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer);

            WorldEntity deck = new WorldEntity(
                BuiltShipPlacement.DeckKey(sequence),
                Deck.AssetName,
                WorldEntities.DefaultAssetContext,
                deckPos,
                seedComponents: BuiltShipPlacement.DeckSeedComponents.ToArray(),
                order: SpawnOrder.AfterPlayer);

            return new HullAndDeck(hull, deck);
        }
    }
}
