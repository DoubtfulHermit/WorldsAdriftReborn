using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Phase 1 of the ship-PART work: crafting a lamp spawns a LOOSE, unattached
    /// ship-part world entity. This pins the PURE half - the exact all-or-nothing
    /// seed set (get one id wrong and the client's interest batch drops and the part
    /// is invisible), the recipe->part mapping, where the part lands next to the
    /// station, and the world-entity registration - so the parts that only fail on a
    /// live client are asserted natively here.
    /// </summary>
    public class LoosePartTests
    {
        // A stand-in station position, off the origin so a bug that keeps the origin
        // is visible.
        private static readonly FixedPointPosition Station =
            new FixedPointPosition(70502113, -1273730, -4580013);

        // --- The seed set (the invisible-part footgun) --------------------------

        [Fact]
        public void Lamp_seed_set_is_the_ShipPartVisualizer_requires_plus_the_lamp_functional_ids()
        {
            var seeds = LoosePartCatalogue.Lamp.SeedComponents;

            // ShipPartVisualizer [Require]s (renders + liftable): 8066, 1120, 190602,
            // 190601, 1016, 1013. LampVisualizer [Require]s (glows): 1108, 1236, 1099.
            uint[] expected = { 190602, 190601, 1016, 1099, 1013, 1120, 8066, 1108, 1236 };
            Assert.Equal(expected.OrderBy(x => x), seeds.OrderBy(x => x));
        }

        [Fact]
        public void Seed_set_leads_with_190602_the_position_every_other_behaviour_reads_back()
        {
            // The batch is applied in order; the transform must arrive before anything
            // that composes against it, exactly like the hull's seed.
            Assert.Equal(190602u, LoosePartCatalogue.Lamp.SeedComponents.First());
        }

        [Fact]
        public void Base_ship_part_components_are_shared_and_part_specific_ids_are_appended()
        {
            var lamp = LoosePartCatalogue.Lamp;

            foreach (uint baseId in LoosePartDefinition.BaseShipPartComponents)
            {
                Assert.Contains(baseId, lamp.SeedComponents);
            }

            // The lamp's own functional ids are exactly 1108 (LampState) + 1236
            // (IsTooDamagedToWorkState) - a different part type contributes different
            // ones without touching the shared base.
            Assert.Equal(new uint[] { 1108, 1236 }, lamp.PartSpecificComponents);
        }

        [Fact]
        public void Seed_set_has_no_duplicate_ids()
        {
            var seeds = LoosePartCatalogue.Lamp.SeedComponents;
            Assert.Equal(seeds.Count, seeds.Distinct().Count());
        }

        // --- Recipe -> part mapping ---------------------------------------------

        [Fact]
        public void Only_the_lamp_recipe_produces_a_loose_part_this_phase()
        {
            Assert.True(LoosePartCatalogue.IsLoosePart("lamp"));
            Assert.False(LoosePartCatalogue.IsLoosePart("torch"));
            Assert.False(LoosePartCatalogue.IsLoosePart("helm"));
            Assert.False(LoosePartCatalogue.IsLoosePart(null));

            Assert.NotNull(LoosePartCatalogue.ForSchematic("lamp"));
            Assert.Null(LoosePartCatalogue.ForSchematic("torch"));
            Assert.Null(LoosePartCatalogue.ForSchematic(null));
        }

        [Fact]
        public void Lamp_definition_carries_the_1120_metadata_the_client_reads_back()
        {
            var lamp = LoosePartCatalogue.Lamp;

            Assert.Equal("lamp", lamp.SchematicId);
            Assert.Equal("lamp", lamp.ItemType);
            Assert.Equal("Lamp", lamp.Title);
            Assert.False(string.IsNullOrWhiteSpace(lamp.PrefabName));
            // A valid BuilderVisualizer.GetAttachmentType string (anything else safely
            // degrades to None on the client, but a plausible one is worth pinning).
            Assert.Equal("shipSurfaces", lamp.AttachmentType);
        }

        // --- Placement -----------------------------------------------------------

        [Fact]
        public void Part_spawns_beside_and_above_the_station_not_inside_it()
        {
            FixedPointPosition part = LoosePartPlacement.NextTo(Station);

            Assert.Equal(Station.X + (long)(LoosePartPlacement.BesideMetres * FixedPointPosition.UnitsPerMetre), part.X);
            Assert.Equal(Station.Y + (long)(LoosePartPlacement.AboveMetres * FixedPointPosition.UnitsPerMetre), part.Y);
            Assert.Equal(Station.Z, part.Z);

            // It really is offset - not sitting exactly on the station origin.
            Assert.NotEqual(Station.X, part.X);
            Assert.True(part.Y > Station.Y);
        }

        [Fact]
        public void Loose_part_key_is_self_describing_and_unique_per_sequence()
        {
            Assert.Equal("loose-part:0:lamp", LoosePartPlacement.Key(0, "lamp"));
            Assert.Equal("loose-part:7:lamp", LoosePartPlacement.Key(7, "lamp"));
            Assert.NotEqual(LoosePartPlacement.Key(0, "lamp"), LoosePartPlacement.Key(1, "lamp"));
        }

        [Fact]
        public void A_loose_part_key_is_NOT_a_bolted_part_key_so_its_190602_seeds_world_absolute()
        {
            // The 190602 branch seeds hull-relative ONLY for bolted-part keys; a loose
            // part belongs to no ship, so it must fall through to the world-absolute
            // path. If IsBoltedPartKey ever matched a loose key the part would be
            // parented to a hull it is not on.
            string looseKey = LoosePartPlacement.Key(0, "lamp");
            Assert.True(LoosePartPlacement.IsLoosePartKey(looseKey));
            Assert.False(WorldEntities.IsBoltedPartKey(looseKey));
            Assert.False(LoosePartPlacement.IsLoosePartKey(WorldEntities.HelmKey));
        }

        // --- World-entity registration ------------------------------------------

        [Fact]
        public void Spawn_plan_registers_the_part_prefab_with_its_full_seed_set()
        {
            FixedPointPosition partPos = LoosePartPlacement.NextTo(Station);
            WorldEntity part = LoosePartSpawnPlan.For(3, partPos, LoosePartCatalogue.Lamp);

            Assert.Equal("loose-part:3:lamp", part.Key);
            Assert.Equal(LoosePartCatalogue.Lamp.PrefabName, part.AssetName);
            Assert.Equal(WorldEntities.DefaultAssetContext, part.AssetContext);
            Assert.Equal(partPos, part.Position);
            Assert.Equal(LoosePartCatalogue.Lamp.SeedComponents, part.SeedComponents);
            // AfterPlayer: nobody stands on a loose part, so it never delays a spawn.
            Assert.Equal(SpawnOrder.AfterPlayer, part.Order);
        }
    }
}
