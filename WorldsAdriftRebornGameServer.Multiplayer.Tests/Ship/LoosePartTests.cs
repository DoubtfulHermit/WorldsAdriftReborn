using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The ship-PART craft: crafting ANY CraftingStation-category ship part spawns a
    /// LOOSE, unattached ship-part world entity (not just the lamp). This pins the
    /// PURE half - the exact all-or-nothing seed set (get one id wrong and the
    /// client's interest batch drops and the part is invisible), that the catalogue
    /// covers every CraftingStation recipe the bench shows, that every part resolves
    /// with valid metadata, where the part lands next to the station, and the
    /// world-entity registration - so the parts that only fail on a live client are
    /// asserted natively here.
    /// </summary>
    public class LoosePartTests
    {
        /// <summary>
        /// Every CraftingStation-category recipe the assembly bench shows (the source
        /// of truth is schematicData.json, category "CraftingStation"). The catalogue
        /// MUST spawn a loose part for each; this list is the regression pin so a new
        /// bench recipe that is not wired here fails the coverage test rather than
        /// silently "not supported this phase" on a live client.
        /// </summary>
        private static readonly string[] AllCraftingStationShipParts =
        {
            "helm", "sail", "deck",
            "proceduralEngineDefault", "proceduralWingDefault",
            "atlasSkyCore", "skyCoreAtlasEnhancer", "skyCoreGenerator", "skyCoreAirFilter",
            "skyCoreCoolantSystem", "skyCoreStabiliser", "skyCoreComputer",
            "skyCoreCircuitryNetwork", "skyCoreEfficiencyModule",
            "smallPanel", "mediumPanel", "largePanel", "window", "stairs", "railing", "railingCorner",
            "trunk", "mountedBox", "storageContainer", "shippingContainer",
            "barrel", "cupboard", "horn", "lamp",
            "altimeter", "fuelGauge", "headingIndicator", "artificialHorizon", "airspeedIndicator",
            "powerGenerator", "powerGenerator01", "personalReviver",
        };

        // The ONLY part-specific component ids a row may seed on top of the common base:
        // ids that ComponentsSerializer serves crash-safe. Seeding any other id would
        // drop the whole all-or-nothing interest batch and render the part invisible.
        // 1081 joined the list with ship storage: its branch is entity-generic and
        // preceded by ShipContainerService.Ensure, so it answers a container with the
        // container's own model rather than throwing or handing back the starter kit.
        private static readonly HashSet<uint> ServedFunctionalIds =
            new HashSet<uint> { 1081, 1108, 1236, 1303, 1107, 1518, 1118, 1246, 12281 };

        // A stand-in station position, off the origin so a bug that keeps the origin
        // is visible.
        private static readonly FixedPointPosition Station =
            new FixedPointPosition(70502113, -1273730, -4580013);

        // --- The REAL client entity-prefab set (the invisible-prefab footgun) ----
        //
        // Extracted from the UNMODIFIED client assets (the
        // "entityprefabs/<name>_unityclient" bundle strings in
        // resources.assets/sharedassets*/globalgamemanagers), embedded as
        // Ship/client-entity-prefabs.txt (one lower-case base name per line). A crafted
        // part is only VISIBLE if the client can load its prefab, and it loads by
        // lower-casing the name and appending the worker suffix (LocalAssetBundleLoader
        // does prefabName.ToLower()); so a catalogue prefab name resolves IFF its
        // lower-cased form is in this set. If it is not, the entity spawns but no prefab
        // instantiates and the part is invisible - exactly the bug this pins.
        private static readonly HashSet<string> RealClientEntityPrefabs = LoadRealClientEntityPrefabs();

        private static HashSet<string> LoadRealClientEntityPrefabs()
        {
            Assembly asm = typeof(LoosePartTests).Assembly;
            string name = asm.GetManifestResourceNames()
                .Single(n => n.EndsWith("client-entity-prefabs.txt", StringComparison.Ordinal));
            using Stream stream = asm.GetManifestResourceStream(name)!;
            using StreamReader reader = new StreamReader(stream);
            var set = new HashSet<string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    set.Add(trimmed.ToLowerInvariant());
                }
            }
            return set;
        }

        // --- Prefab names resolve against the REAL client assets ----------------

        [Fact]
        public void The_extracted_real_client_prefab_set_loaded_and_contains_known_good_names()
        {
            // Guard the extraction itself: if the embedded resource failed to load, the
            // membership test below would pass vacuously. Pin a handful of prefabs that
            // are proven to render in the live game (the static ship parts + the lamp).
            Assert.True(RealClientEntityPrefabs.Count > 100,
                "The real client entity-prefab set failed to load (only " + RealClientEntityPrefabs.Count + " names).");
            foreach (string known in new[] { "helm01", "deck01", "sail01", "modularengine", "lamp01" })
            {
                Assert.Contains(known, RealClientEntityPrefabs);
            }
            // And prove the OLD lamp guess is genuinely absent - the whole reason it was invisible.
            Assert.DoesNotContain("lamp", RealClientEntityPrefabs);
        }

        [Fact]
        public void Every_loose_part_prefab_name_is_a_real_client_entity_prefab()
        {
            // THE invisible-part footgun. A prefab name the client cannot load spawns the
            // entity but instantiates nothing, so the crafted part is invisible. Every
            // row must resolve against the real, extracted client entity-prefab set.
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                string key = part.PrefabName.ToLowerInvariant();
                Assert.True(RealClientEntityPrefabs.Contains(key),
                    "Loose part '" + part.SchematicId + "' has prefab '" + part.PrefabName
                    + "' which is NOT a real client entity prefab (no entityprefabs/" + key
                    + "_unityclient) - the crafted part would spawn invisible.");
            }
        }

        [Fact]
        public void The_lamp_prefab_is_corrected_to_the_real_asset_Lamp01()
        {
            // The regression this fix pins: "Lamp" does not resolve (only lamp01 exists),
            // so the lamp default must be the real asset "Lamp01".
            Assert.Equal("Lamp01", LoosePartCatalogue.LampDefaultPrefab);
            Assert.Equal("Lamp01", LoosePartCatalogue.Lamp.PrefabName);
            Assert.Contains("lamp01", RealClientEntityPrefabs);
        }

        // --- The seed set (the invisible-part footgun) --------------------------

        [Fact]
        public void Lamp_seed_set_is_the_ShipPartVisualizer_requires_plus_the_lamp_functional_ids()
        {
            var seeds = LoosePartCatalogue.Lamp.SeedComponents;

            // ShipPartVisualizer [Require]s (renders + liftable): 8066, 1120, 190602,
            // 190601, 1016, 1013. LampVisualizer [Require]s (glows): 1108, 1236, 1099.
            uint[] expected = { 190602, 190601, 1016, 1099, 1013, 1120, 8066, 1246, 1108, 1236 };
            Assert.Equal(expected.OrderBy(x => x), seeds.OrderBy(x => x));
        }

        // --- Recipe -> part mapping ---------------------------------------------

        [Fact]
        public void Catalogue_covers_every_CraftingStation_ship_part_the_bench_shows()
        {
            foreach (string schematicId in AllCraftingStationShipParts)
            {
                Assert.True(LoosePartCatalogue.IsLoosePart(schematicId),
                    "CraftingStation recipe '" + schematicId + "' produces no loose part - crafting it would be rejected as 'not supported'.");
                Assert.NotNull(LoosePartCatalogue.ForSchematic(schematicId));
            }
        }

        [Fact]
        public void Non_ship_part_recipes_and_null_do_not_produce_a_loose_part()
        {
            // Personal/Cooking/Shipyard recipes are NOT loose ship parts.
            Assert.False(LoosePartCatalogue.IsLoosePart("torch"));       // Personal
            Assert.False(LoosePartCatalogue.IsLoosePart("glider"));      // Personal
            Assert.False(LoosePartCatalogue.IsLoosePart("campFire"));    // Cooking
            Assert.False(LoosePartCatalogue.IsLoosePart("shipyard"));    // Personal deployable
            Assert.False(LoosePartCatalogue.IsLoosePart("territory_control_beacon")); // Shipyard-category, not a bolt-on
            Assert.False(LoosePartCatalogue.IsLoosePart(null));

            Assert.Null(LoosePartCatalogue.ForSchematic("torch"));
            Assert.Null(LoosePartCatalogue.ForSchematic(null));
        }

        [Fact]
        public void Every_part_resolves_with_the_1120_metadata_the_client_reads_back()
        {
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(part.SchematicId));
                Assert.False(string.IsNullOrWhiteSpace(part.ItemType), part.SchematicId + " has no itemType");
                Assert.False(string.IsNullOrWhiteSpace(part.Title), part.SchematicId + " has no title");
                Assert.False(string.IsNullOrWhiteSpace(part.PrefabName), part.SchematicId + " has no prefab");
                Assert.False(string.IsNullOrWhiteSpace(part.AttachmentType), part.SchematicId + " has no attachmentType");
                // A recognised BuilderVisualizer.GetAttachmentType string (an unknown
                // one still renders + lifts, degrading only placement snapping to None,
                // but pinning the set catches a typo'd attachment).
                Assert.Contains(part.AttachmentType,
                    new[] { "none", "side", "deck", "wing", "deckGrid", "deckForward", "engine", "shipSurfaces", "coreModule" });
            }
        }

        [Fact]
        public void Every_part_seed_set_is_complete_leads_with_position_and_has_no_duplicates()
        {
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                var seeds = part.SeedComponents;

                // Leads with 190602 - the position every other behaviour composes against.
                Assert.Equal(190602u, seeds.First());

                // Carries the full ShipPartVisualizer render/lift base (+ 1099 salvage).
                foreach (uint baseId in LoosePartDefinition.BaseShipPartComponents)
                {
                    Assert.Contains(baseId, seeds);
                }

                // No duplicate ids (a repeated id would double-serve).
                Assert.Equal(seeds.Count, seeds.Distinct().Count());
            }
        }

        [Fact]
        public void No_part_seeds_a_functional_id_that_is_not_served_crash_safe()
        {
            // The all-or-nothing footgun: a seeded id with no ComponentsSerializer
            // branch drops the whole interest batch and the part spawns invisible.
            // Only 1108/1236/1303/1107 have crash-safe branches today.
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                foreach (uint functionalId in part.PartSpecificComponents)
                {
                    Assert.Contains(functionalId, ServedFunctionalIds);
                }
            }
        }

        [Fact]
        public void Functional_seeds_match_the_part_type()
        {
            // The lamp glows (1108 + 1236); instruments and sky cores wake their
            // damage-gated visualizer (1236); the sail (1303) and horn (1107) wake
            // their own state. Everything else renders inert on the common base alone.
            Assert.Equal(new uint[] { 1108, 1236 }, LoosePartCatalogue.ForSchematic("lamp")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1303 }, LoosePartCatalogue.ForSchematic("sail")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1107 }, LoosePartCatalogue.ForSchematic("horn")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1236 }, LoosePartCatalogue.ForSchematic("altimeter")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1236 }, LoosePartCatalogue.ForSchematic("atlasSkyCore")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1518 }, LoosePartCatalogue.ForSchematic("deck")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1118 }, LoosePartCatalogue.ForSchematic("mediumPanel")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 1118 }, LoosePartCatalogue.ForSchematic("window")!.PartSpecificComponents);

            // Parts left dormant carry NO functional id (render + lift only).
            Assert.Empty(LoosePartCatalogue.ForSchematic("helm")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 12281 }, LoosePartCatalogue.ForSchematic("proceduralEngineDefault")!.PartSpecificComponents);
            Assert.Equal(new uint[] { 12281 }, LoosePartCatalogue.ForSchematic("proceduralWingDefault")!.PartSpecificComponents);
            // Storage containers seed the two [Require]s that decide whether their
            // inventory visualiser ever enables. Asserted per row rather than in a
            // loop so that adding a fifth container without its components is a
            // failure here and not a silently dead prop in the world.
            foreach (string container in new[]
                     { "trunk", "mountedBox", "storageContainer", "shippingContainer" })
            {
                Assert.Equal(
                    ShipContainers.RequiredComponents,
                    LoosePartCatalogue.ForSchematic(container)!.PartSpecificComponents);
            }
        }

        /// <summary>
        /// EVERY container the interaction policy offers an Inventory prompt on must
        /// also seed 1081 + 1236, and vice versa. The two halves live in different
        /// files and either one alone produces the exact failure this whole feature
        /// exists to remove: a prompt with no inventory behind it, or an inventory
        /// with no way to open it. Neither logs anything.
        /// </summary>
        [Fact]
        public void Every_container_seeds_its_requires_and_advertises_its_verb()
        {
            var seeded = new List<string>();
            var prompted = new List<string>();
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                if (ShipContainers.RequiredComponents
                    .All(id => part.PartSpecificComponents.Contains(id)))
                {
                    seeded.Add(part.ItemType);
                }
                if (PartInteractionPolicy.VerbFor(part.ItemType) == PartVerb.Inventory)
                {
                    prompted.Add(part.ItemType);
                }
            }

            Assert.Equal(
                new[] { "mountedBox", "shippingContainer", "storageContainer", "trunk" },
                seeded.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.Equal(
                seeded.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                prompted.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// A container's grid must be able to hold the widest item this server can
        /// produce. A 4-wide trunk would have a column no recovered 5x3 scrap item
        /// could ever occupy, and nothing would say so - the item would simply
        /// refuse to drop.
        /// </summary>
        [Fact]
        public void Every_container_grid_can_hold_the_widest_item_in_the_game()
        {
            foreach (string itemType in ShipContainers.ItemTypes)
            {
                ShipContainers.Grid grid = ShipContainers.GridFor(itemType)!.Value;
                Assert.True(grid.Width >= ShipContainers.MinimumUsableWidth,
                    itemType + " is only " + grid.Width + " cells wide.");
                Assert.True(grid.Height >= ShipContainers.MinimumUsableHeight,
                    itemType + " is only " + grid.Height + " cells tall.");
                // NOT the player's 10x18 belt grid, which is what an unbound
                // container inherits from InventoryWire.DefaultModel.
                Assert.False(grid.Width == 10 && grid.Height == 18,
                    itemType + " has the player starter grid's shape.");
            }
        }

        [Fact]
        public void Every_part_spawn_plan_registers_its_prefab_with_its_full_seed_set()
        {
            // The whole catalogue round-trips through the spawn plan (asset name = its
            // prefab, seed set = its own), not just the lamp.
            FixedPointPosition partPos = LoosePartPlacement.NextTo(Station);
            int seq = 0;
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                WorldEntity reg = LoosePartSpawnPlan.For(seq, partPos, part);
                Assert.Equal(LoosePartPlacement.Key(seq, part.SchematicId), reg.Key);
                Assert.Equal(part.PrefabName, reg.AssetName);
                Assert.Equal(part.SeedComponents, reg.SeedComponents);
                Assert.Equal(SpawnOrder.AfterPlayer, reg.Order);
                seq++;
            }
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
            Assert.Equal("deck", lamp.AttachmentType);
        }

        [Fact]
        public void Helm_mounts_on_the_deck_surface()
        {
            // Regression pin (BUG 1): a helm is a DECK part, so its attachmentType must be
            // "deck" - which the client resolves to the ShipDeck raycast surface (the solid
            // Deck01 collider), placeable across the whole deck. The former best-guess
            // "shipSurfaces" resolved to the Environment layer and only landed on one spot.
            var helm = LoosePartCatalogue.ForSchematic("helm")!;
            Assert.Equal("deck", helm.AttachmentType);
            Assert.Equal(PartMountSurface.ShipDeck, PartMountSurfaces.ForAttachmentType(helm.AttachmentType));
        }

        [Fact]
        public void Sail_mounts_on_the_deck_surface()
        {
            // A sail is a deck-standing MAST (SailVisualizer is a yawing mast, not a hull-
            // side/wing part), so it mounts on the ShipDeck surface exactly like the helm -
            // placeable across the whole deck, not the one incidental Environment spot the
            // former "shipSurfaces" best-guess landed on.
            var sail = LoosePartCatalogue.ForSchematic("sail")!;
            Assert.Equal("deck", sail.AttachmentType);
            Assert.Equal(PartMountSurface.ShipDeck, PartMountSurfaces.ForAttachmentType(sail.AttachmentType));
        }

        [Fact]
        public void Deck_mounted_parts_all_resolve_to_the_ship_deck_surface()
        {
            // Every part whose retail surface is the deck (the ONE usable placement surface
            // our built ship exposes) must resolve to ShipDeck so it is placeable across the
            // whole deck. Pinned as a set so a future catalogue edit that drops one off the
            // deck fails here.
            foreach (string id in new[]
            {
                "helm", "sail", "deck", "stairs", "railing", "railingCorner",
                "trunk", "mountedBox", "storageContainer", "shippingContainer",
                "barrel", "cupboard", "horn", "lamp", "personalReviver",
                "altimeter", "fuelGauge", "headingIndicator", "artificialHorizon", "airspeedIndicator",
                "powerGenerator", "powerGenerator01",
                // The sky-core BASE: the only prefab with authored module sockets
                // (see SkyCoreSocketsTests), so IT stands on the deck and the eight
                // modules snap onto it.
                "atlasSkyCore",
            })
            {
                var part = LoosePartCatalogue.ForSchematic(id)!;
                Assert.Equal(PartMountSurface.ShipDeck, PartMountSurfaces.ForAttachmentType(part.AttachmentType));
            }
        }

        [Fact]
        public void No_catalogue_part_uses_the_broken_generic_ship_surface()
        {
            // Generated ships have no Environment-layer ShipSurfaces skin. Any row
            // authored with that value would regress to incidental frame-only placement.
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                Assert.NotEqual("shipSurfaces", part.AttachmentType);
                Assert.NotEqual(PartMountSurface.ShipSurfaces,
                    PartMountSurfaces.ForAttachmentType(part.AttachmentType));
            }
        }

        [Fact]
        public void Legacy_generic_surface_metadata_migrates_to_the_deck()
        {
            var restored = new LoosePartDefinition(
                "lamp", "lamp", "Lamp", "Lamp01", "shipSurfaces", new uint[] { 1108, 1236 });

            Assert.Equal("deck", restored.AttachmentType);
            Assert.Equal(PartMountSurface.ShipDeck,
                PartMountSurfaces.ForAttachmentType(restored.AttachmentType));
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
        public void Repeated_crafts_get_distinct_nearby_output_slots()
        {
            var occupied = new List<FixedPointPosition>();
            FixedPointPosition first = LoosePartPlacement.NextAvailable(Station, occupied);
            occupied.Add(first);
            FixedPointPosition second = LoosePartPlacement.NextAvailable(Station, occupied);
            occupied.Add(second);
            FixedPointPosition third = LoosePartPlacement.NextAvailable(Station, occupied);
            occupied.Add(third);
            FixedPointPosition fourth = LoosePartPlacement.NextAvailable(Station, occupied);

            Assert.Equal(LoosePartPlacement.NextTo(Station), first);
            Assert.Equal(4, new[] { first, second, third, fourth }.Distinct().Count());
            Assert.Equal(first.X, second.X);
            Assert.True(second.Z > first.Z);
            Assert.True(third.Z < first.Z);
            Assert.True(fourth.X > first.X);
        }

        [Fact]
        public void Persisted_coincident_outputs_are_separated_without_moving_the_first()
        {
            FixedPointPosition first = LoosePartPlacement.NextTo(Station);
            FixedPointPosition repaired = LoosePartPlacement.FirstAvailableFrom(
                first, new[] { first });

            Assert.NotEqual(first, repaired);
            Assert.Equal(first.X, repaired.X);
            Assert.Equal(first.Y, repaired.Y);
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
