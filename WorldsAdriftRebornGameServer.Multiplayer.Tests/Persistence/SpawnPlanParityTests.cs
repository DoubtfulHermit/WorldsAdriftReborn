using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    /// <summary>
    /// SEED-SET PARITY. The whole persistence design rests on one property: a shipyard
    /// or ship re-created at boot must be BYTE-IDENTICAL to one placed/built at runtime,
    /// because the client's interest batch on these entities is all-or-nothing - one
    /// wrong or missing component id drops the entire batch and the entity renders
    /// inert or invisible.
    ///
    /// Parity is guaranteed by construction: BOTH the runtime spawn seams
    /// (PlacementService / BuiltShipSpawner) and the boot restore build their
    /// WorldEntity through these two plan types, so there is only one definition of a
    /// placed deployable's or a built ship's asset + seed set. These tests pin that one
    /// definition to the canonical seed sets the rest of the pipeline trusts.
    /// </summary>
    public class SpawnPlanParityTests
    {
        // -----------------------------------------------------------------
        // Placed deployable (shipyard)
        // -----------------------------------------------------------------

        private static DeployableDef Shipyard()
        {
            Assert.True(Deployables.TryGet(Deployables.ShipyardItemType, out DeployableDef def));
            return def;
        }

        [Fact]
        public void A_restored_deployable_carries_the_deployables_own_seed_set()
        {
            DeployableDef def = Shipyard();
            FixedPointPosition pos = FixedPointPosition.FromMetres(100.0, 5.0, -200.0);

            WorldEntity e = PlacedDeployableSpawnPlan.WorldEntityFor(def, sequence: 7, pos, packedRotation: 12345u);

            // The seed set is the shipyard's own, id-for-id: 190602 + 1205 + editor +
            // interaction + crafting-station. Anything else drops the batch on a client.
            Assert.Equal(def.SeedComponents, e.SeedComponents);
            Assert.Equal(def.AssetName, e.AssetName);
        }

        [Fact]
        public void A_restored_deployable_reproduces_key_transform_rotation_and_order()
        {
            DeployableDef def = Shipyard();
            FixedPointPosition pos = FixedPointPosition.FromMetres(100.0, 5.0, -200.0);

            WorldEntity e = PlacedDeployableSpawnPlan.WorldEntityFor(def, sequence: 7, pos, packedRotation: 12345u);

            Assert.Equal(def.KeyPrefix + ":7", e.Key);
            Assert.Equal(pos, e.Position);
            Assert.Equal(12345u, e.PackedRotation);
            Assert.Equal(SpawnOrder.AfterPlayer, e.Order);
            Assert.Equal(WorldEntities.DefaultAssetContext, e.AssetContext);
        }

        [Fact]
        public void Two_restored_deployables_at_different_sequences_get_distinct_keys()
        {
            DeployableDef def = Shipyard();
            FixedPointPosition pos = FixedPointPosition.FromMetres(0, 0, 0);

            WorldEntity a = PlacedDeployableSpawnPlan.WorldEntityFor(def, 0, pos, 0);
            WorldEntity b = PlacedDeployableSpawnPlan.WorldEntityFor(def, 1, pos, 0);

            Assert.NotEqual(a.Key, b.Key);
        }

        // -----------------------------------------------------------------
        // Built ship (hull + deck)
        // -----------------------------------------------------------------

        // A default one-cell hull yields the six-panel deck (three lateral strips for the
        // lower floor + three for the exposed upper deck) both the runtime build and the
        // restore derive from the same bytes.
        private static System.Collections.Generic.IReadOnlyList<DeckPanel> DefaultPanels()
            => DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

        [Fact]
        public void A_restored_built_ship_carries_the_proven_hull_and_deck_seed_sets()
        {
            FixedPointPosition hullPos = FixedPointPosition.FromMetres(50.0, 0.5, 50.0);

            BuiltShipSpawnPlan.HullAndDecks plan = BuiltShipSpawnPlan.For(sequence: 3, hullPos, DefaultPanels());

            // The hull's set is the proven static test hull's (recognition on); every
            // deck panel's is the proven deck readers. Id-for-id, or the ship renders nothing.
            Assert.Equal(BuiltShipPlacement.HullSeedComponents, plan.Hull.SeedComponents);
            Assert.NotEmpty(plan.Decks);
            foreach (WorldEntity deck in plan.Decks)
            {
                Assert.Equal(BuiltShipPlacement.DeckSeedComponents, deck.SeedComponents);
            }
        }

        [Fact]
        public void A_restored_built_ship_reproduces_positions_keys_and_assets()
        {
            FixedPointPosition hullPos = FixedPointPosition.FromMetres(50.0, 0.5, 50.0);

            BuiltShipSpawnPlan.HullAndDecks plan = BuiltShipSpawnPlan.For(sequence: 3, hullPos, DefaultPanels());

            Assert.Equal(hullPos, plan.Hull.Position);
            Assert.Equal(BuiltShipPlacement.HullKey(3), plan.Hull.Key);
            Assert.Equal(WorldEntities.ShipFrameAssetName, plan.Hull.AssetName);
            Assert.Equal(SpawnOrder.AfterPlayer, plan.Hull.Order);

            // Every deck panel is its own entity with an indexed key, the deck asset, and
            // spawn order; and it sits at the hull plus the panel's hull-local offset.
            for (int i = 0; i < plan.Decks.Count; i++)
            {
                WorldEntity deck = plan.Decks[i];
                Assert.Equal(BuiltShipPlacement.DeckKey(3, i), deck.Key);
                Assert.Equal(Deck.AssetName, deck.AssetName);
                Assert.Equal(SpawnOrder.AfterPlayer, deck.Order);
            }
        }

        // -----------------------------------------------------------------
        // Loose part (crafted, unmounted)
        // -----------------------------------------------------------------

        [Fact]
        public void A_restored_loose_part_carries_the_parts_own_all_or_nothing_seed_set()
        {
            LoosePartDefinition part = LoosePartCatalogue.Lamp;
            FixedPointPosition pos = FixedPointPosition.FromMetres(10.0, 2.0, -5.0);

            WorldEntity e = LoosePartSpawnPlan.For(sequence: 4, pos, part);

            // Id-for-id the part's own seed set (base seven + the lamp's 1108/1236), or the
            // client's all-or-nothing interest batch drops and the part renders inert.
            Assert.Equal(part.SeedComponents, e.SeedComponents);
            Assert.Equal(part.PrefabName, e.AssetName);
            Assert.Equal(LoosePartPlacement.Key(4, part.SchematicId), e.Key);
            Assert.Equal(pos, e.Position);
            Assert.Equal(SpawnOrder.AfterPlayer, e.Order);
        }

        [Fact]
        public void A_restored_loose_part_reproduces_its_persisted_rotation()
        {
            LoosePartDefinition part = LoosePartCatalogue.Lamp;
            FixedPointPosition pos = FixedPointPosition.FromMetres(0, 0, 0);

            // A freshly-crafted loose part chose no facing (identity); a restore threads
            // whatever was persisted so the two are byte-identical.
            Assert.Equal(WorldsAdriftRebornGameServer.Multiplayer.Placement.Quaternion32Packing.Identity,
                LoosePartSpawnPlan.For(0, pos, part).PackedRotation);
            Assert.Equal(778899u,
                LoosePartSpawnPlan.For(0, pos, part, packedRotation: 778899u).PackedRotation);
        }
    }
}
