using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The one interactable ship part: a Helm01 on the deck, carrying the "Man"
    /// verb. Like the hull it is mostly VALUES that fail silently if wrong - a
    /// zero interaction radius is a prompt that never appears, a wrong deck offset
    /// is a helm floating off the ship - so they are pinned here rather than by
    /// staring at a client that was never launched.
    /// </summary>
    public class HelmTests
    {
        [Fact]
        public void The_helm_asks_for_the_bare_helm_prefab()
        {
            // "Helm01", not "Helm01_unityclient" - the client appends its own
            // worker suffix.
            Assert.Equal("Helm01", Helm.AssetName);
            Assert.Equal(Helm.AssetName, WorldEntities.Helm().AssetName);
            Assert.Equal(WorldEntities.DefaultAssetContext, WorldEntities.Helm().AssetContext);
        }

        [Fact]
        public void The_man_prompt_has_a_non_zero_radius_or_it_never_appears()
        {
            // InteractiveObjectVisualizer.OnEnable resolves the baked verb to an
            // InteractionEntry; a zero radius is a prompt with no trigger distance.
            Assert.True(Helm.ManRadius > 0f);
            Assert.True(Helm.ManTimeToUse >= 0f);
        }

        [Fact]
        public void The_helm_sits_on_the_hulls_deck_derived_from_the_hull_not_pasted()
        {
            // The helm is a SEPARATE entity with its own global 190602 - not
            // parented to the hull - so its position must track the hull's. It is
            // the hull registration plus the deck offset, and this asserts the
            // arithmetic so the two literals cannot drift apart.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            FixedPointPosition helm = WorldEntities.Helm().Position;

            Assert.Equal(hull.X, helm.X);
            Assert.Equal(hull.Y + (long)(Helm.DeckUpMetres * FixedPointPosition.UnitsPerMetre), helm.Y);
            Assert.Equal(hull.Z + (long)(Helm.DeckForwardMetres * FixedPointPosition.UnitsPerMetre), helm.Z);

            // OnDeckOf is a pure function of the hull position.
            Assert.Equal(helm, Helm.OnDeckOf(hull));
        }

        [Fact]
        public void The_helm_stays_within_reach_of_the_deck_centre()
        {
            // The forward offset must be small enough that the helm is still on the
            // ~4 m fore-to-aft one-cell deck and inside its own Man radius of the
            // hull centre, or "walk up to it" stops being a straight line from where
            // the player boards.
            Assert.True(System.Math.Abs(Helm.DeckForwardMetres) <= 2.0);
            Assert.True(System.Math.Abs(Helm.DeckForwardMetres) <= Helm.ManRadius);
        }

        [Fact]
        public void The_helm_spawns_after_the_player_and_seeds_nothing_unprompted()
        {
            // Like the nugget: no pushed seed list. Its 1210 (Man) and 8066 are
            // served best-effort when the client asks over interest, so there is no
            // all-or-nothing batch to drop.
            WorldEntity helm = WorldEntities.Helm();
            Assert.Equal(SpawnOrder.AfterPlayer, helm.Order);
            Assert.Empty(helm.SeedComponents);
        }

        [Fact]
        public void The_helm_is_in_the_default_registry_after_the_hull()
        {
            // Registration ORDER matters: the hull must be registered (and so get
            // its shared id first) before the helm, whose 8066 names the hull by id.
            WorldEntityRegistry registry = WorldEntities.Default(new EntityIdAllocator());

            Assert.NotNull(registry.ByKey(WorldEntities.HelmKey));

            int hullIndex = registry.Registrations
                .ToList().FindIndex(e => e.Key == WorldEntities.ShipFrameKey);
            int helmIndex = registry.Registrations
                .ToList().FindIndex(e => e.Key == WorldEntities.HelmKey);

            Assert.True(hullIndex >= 0 && helmIndex >= 0);
            Assert.True(hullIndex < helmIndex);
        }

        [Fact]
        public void The_helm_gets_its_own_id_and_its_own_position_from_the_registry()
        {
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);

            long hullId = registry.EntityIdFor(registry.ByKey(WorldEntities.ShipFrameKey)!);
            long helmId = registry.EntityIdFor(registry.ByKey(WorldEntities.HelmKey)!);

            Assert.NotEqual(hullId, helmId);
            Assert.Equal(SeededEntityKind.World, registry.KindOf(helmId));
            Assert.Equal(WorldEntities.Helm().Position, registry.TransformSeedFor(helmId));
        }
    }
}
