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
            Assert.Equal(
                new uint[] { Deployables.TransformStateComponentId, Deployables.ShipyardStateComponentId },
                def.SeedComponents.ToArray());
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

        [Fact]
        public void A_deployable_without_backed_state_seeds_the_transform_ALONE()
        {
            // THE SAFETY INVARIANT. A seed batch is all-or-nothing: an id with no
            // ComponentsSerializer branch drops the WHOLE batch. Only the shipyard's
            // 1205 has a branch, so any other deployable that listed a second seed id
            // would silently place itself at the world origin. Guard against a future
            // row doing that before its serializer branch exists.
            foreach (DeployableDef def in Deployables.All.Where(d => !d.HasBackedState))
            {
                Assert.Equal(
                    new[] { Deployables.TransformStateComponentId },
                    def.SeedComponents.ToArray());
            }
        }

        [Fact]
        public void A_backed_deployable_is_the_only_one_carrying_extra_seeds()
        {
            // The converse: the only deployable allowed a non-transform seed today is
            // one flagged HasBackedState (the shipyard). If a new backed deployable is
            // added, it needs its own ComponentsSerializer branch - this test is the
            // reminder to add it.
            foreach (DeployableDef def in Deployables.All.Where(d => d.SeedComponents.Count > 1))
            {
                Assert.True(def.HasBackedState,
                    def.ItemTypeId + " carries extra seed components but is not flagged HasBackedState");
                Assert.Equal("shipyard", def.ItemTypeId);
            }
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
