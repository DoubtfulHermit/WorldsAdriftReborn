using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The data-driven deployable table is what makes placement generic. These tests
    /// pin the two things the rest of the pipeline trusts: the shipyard stays exactly
    /// as it was proven (asset "Shipyard", seeds 190602+1205), and the CRITICAL safety
    /// invariant - a deployable may only seed a component id that has a
    /// ComponentsSerializer branch, so no placement ever drops its whole seed batch
    /// (190602 included) and spawns at the origin.
    /// </summary>
    public class DeployablesTests
    {
        [Fact]
        public void Shipyard_is_the_one_fully_backed_deployable()
        {
            Assert.True(Deployables.TryGet("shipyard", out DeployableDef def));
            Assert.Equal("Shipyard", def.AssetName);
            Assert.True(def.HasBackedState);
            Assert.True(def.AssetVerified);
            // The shipyard renders deployed (190602 + 1205), carries the hull-editor
            // state (1206) the client needs to construct its editor, AND its console
            // shows an interact prompt that opens the ship-build UI (1210 + the
            // crafting-station pair 1004/1005). Every id has a ComponentsSerializer branch.
            Assert.Equal(
                new uint[]
                {
                    Deployables.TransformStateComponentId,
                    Deployables.ShipyardStateComponentId,
                    Deployables.ShipHullEditorStateComponentId,
                    Deployables.InteractiveStateComponentId,
                    Deployables.CraftingStationGSimStateComponentId,
                    Deployables.CraftingStationClientStateComponentId,
                },
                def.SeedComponents.ToArray());
        }

        [Fact]
        public void Shipyard_seeds_the_console_interaction_components()
        {
            // The three ids that make the centre console interactive and open the UI.
            // If any is dropped, either the prompt never appears (1210) or
            // CraftingStationBehaviour never enables (1004/1005) and interacting opens
            // nothing.
            Assert.True(Deployables.TryGet("shipyard", out DeployableDef def));
            Assert.Contains(Deployables.InteractiveStateComponentId, def.SeedComponents);
            Assert.Contains(Deployables.CraftingStationGSimStateComponentId, def.SeedComponents);
            Assert.Contains(Deployables.CraftingStationClientStateComponentId, def.SeedComponents);
        }

        [Theory]
        [InlineData("makeshiftStorage", "MakeshiftStorage")]
        [InlineData("storageContainer", "ContainerMedium")]
        [InlineData("campFire", "Campfire")]
        [InlineData("cupboard", "Cupboard")]
        [InlineData("barrel", "Barrel01")]
        public void A_known_deployable_resolves_to_its_asset(string itemType, string asset)
        {
            Assert.True(Deployables.IsDeployable(itemType));
            Assert.True(Deployables.TryGet(itemType, out DeployableDef def));
            Assert.Equal(asset, def.AssetName);
        }

        [Fact]
        public void Every_deployable_seeds_the_transform_component()
        {
            // 190602 is the one field that places anything; every deployable MUST carry
            // it or it spawns at the origin.
            foreach (DeployableDef def in Deployables.All)
            {
                Assert.Contains(Deployables.TransformStateComponentId, def.SeedComponents);
            }
        }

        /// <summary>
        /// The set of component ids that have a ComponentsSerializer branch and so may
        /// appear in a seed batch. The safety invariant below is asserted against this:
        /// a seed push is all-or-nothing, so an id NOT in this set drops the WHOLE batch
        /// (190602 included) and the deployable spawns inert at the world origin.
        /// </summary>
        private static readonly uint[] IdsWithSerializerBranch =
        {
            Deployables.TransformStateComponentId,            // 190602
            Deployables.ShipyardStateComponentId,             // 1205
            Deployables.ShipHullEditorStateComponentId,       // 1206
            Deployables.InteractiveStateComponentId,          // 1210
            Deployables.CraftingStationGSimStateComponentId,  // 1004
            Deployables.CraftingStationClientStateComponentId,// 1005
        };

        [Fact]
        public void Every_seeded_component_has_a_serializer_branch()
        {
            // THE SAFETY INVARIANT. Every id any deployable seeds must have a
            // ComponentsSerializer branch, or its all-or-nothing batch is dropped and it
            // spawns at the origin. Guards a future row from listing an id before its
            // branch exists (the trap the old transform-ALONE test approximated).
            foreach (DeployableDef def in Deployables.All)
            {
                foreach (uint id in def.SeedComponents)
                {
                    Assert.Contains(id, IdsWithSerializerBranch);
                }
            }
        }

        [Fact]
        public void A_plain_deployable_seeds_the_transform_ALONE()
        {
            // Everything that is neither the shipyard (ledger-backed 1205 state) nor a
            // crafting station (the 1004/1005/1210 interact set) still seeds 190602 alone -
            // a container/lamp/campfire renders at its transform in prefab-default state.
            foreach (DeployableDef def in Deployables.All
                .Where(d => !d.HasBackedState && !d.IsCraftingStation))
            {
                Assert.Equal(
                    new[] { Deployables.TransformStateComponentId },
                    def.SeedComponents.ToArray());
            }
        }

        [Fact]
        public void Only_a_backed_or_crafting_station_deployable_carries_extra_seeds()
        {
            // The converse: a non-transform seed is allowed ONLY on the shipyard
            // (HasBackedState) or a crafting station (IsCraftingStation). A new deployable
            // with extra seeds must declare one of the two - the reminder to add its
            // serializer branch / ledger wiring before it can carry more than 190602.
            foreach (DeployableDef def in Deployables.All.Where(d => d.SeedComponents.Count > 1))
            {
                Assert.True(def.HasBackedState || def.IsCraftingStation,
                    def.ItemTypeId + " carries extra seed components but is neither HasBackedState nor IsCraftingStation");
            }
        }

        [Fact]
        public void AssemblyStation_places_and_opens_the_parts_UI_via_its_interact_seeds()
        {
            // The Assembly Station is the generic crafting station: its LOADABLE world
            // prefab is "CraftingStation" (client bundle "CraftingStation_unityclient") -
            // NOT "AssemblyStation", which is only a UI/quest label with no deployable
            // prefab, so naming it that gave the client a PlacingPrefab it could not load
            // and the placement preview never confirmed. Flagged IsCraftingStation, seeded
            // with EXACTLY the interact set that makes CraftingStationBehaviour (1004+1005)
            // and InteractiveObjectVisualizer (1210) enable - plus 190602 to place it. It
            // must NOT carry the shipyard-only 1205/1206/1207, or the client would mis-open
            // ship-build instead of the parts tab.
            Assert.True(Deployables.TryGet("assemblyStation", out DeployableDef def));
            Assert.Equal("CraftingStation", def.AssetName);
            Assert.True(def.AssetVerified);
            Assert.True(def.IsCraftingStation);
            Assert.False(def.HasBackedState);
            Assert.Equal(
                new uint[]
                {
                    Deployables.TransformStateComponentId,
                    Deployables.CraftingStationGSimStateComponentId,
                    Deployables.CraftingStationClientStateComponentId,
                    Deployables.InteractiveStateComponentId,
                },
                def.SeedComponents.ToArray());
            Assert.DoesNotContain(Deployables.ShipyardStateComponentId, def.SeedComponents);
            Assert.DoesNotContain(Deployables.ShipHullEditorStateComponentId, def.SeedComponents);
        }

        [Fact]
        public void The_shipyard_is_not_flagged_a_generic_crafting_station()
        {
            // The shipyard opens ship-build, not the parts tab, so it is NOT an
            // IsCraftingStation deployable even though it too carries 1004/1005/1210.
            // The two are recorded in separate ledgers so their interacts never cross.
            Assert.True(Deployables.TryGet("shipyard", out DeployableDef yard));
            Assert.False(yard.IsCraftingStation);
        }

        [Theory]
        [InlineData("sail")]
        [InlineData("deck")]
        [InlineData("iron")]
        [InlineData("guitar")]
        [InlineData("mantaSteak")]
        [InlineData(null)]
        [InlineData("")]
        public void A_non_deployable_is_not_placeable(string? itemType)
        {
            Assert.False(Deployables.IsDeployable(itemType));
            Assert.False(Deployables.TryGet(itemType, out _));
        }

        [Fact]
        public void Every_deployable_has_a_unique_nonempty_key_prefix()
        {
            var prefixes = Deployables.All.Select(d => d.KeyPrefix).ToList();
            Assert.All(prefixes, p => Assert.False(string.IsNullOrWhiteSpace(p)));
            Assert.Equal(prefixes.Count, prefixes.Distinct().Count());
        }
    }
}
