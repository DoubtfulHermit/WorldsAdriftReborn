using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// The OBJECT: the prefab it is, where it stands, and the two ways it can be
    /// silently dead on a live client (an unresolvable prefab name, and a 1210 seed
    /// whose verb the prefab did not bake).
    /// </summary>
    public sealed class WildernessShrineTests
    {
        /// <summary>
        /// The prefab must be one the unmodified client can actually resolve. This
        /// is the same census the station-craft gate consults before consuming
        /// materials; a name that is not in it renders NOTHING and logs nothing.
        /// </summary>
        [Fact]
        public void The_shrine_prefab_is_one_the_client_can_resolve()
        {
            Assert.True(ClientEntityPrefabs.CanResolve(WildernessShrine.AssetName),
                WildernessShrine.AssetName + " is not in the client entity-prefab census");
        }

        /// <summary>
        /// The seed batch is all-or-nothing on the wire, so every id in it needs a
        /// ComponentsSerializer branch. Pinned as an exact list rather than a
        /// "contains" so adding a tempting-but-unserialized id (6905
        /// AncientRespawnerState is the obvious one) fails here instead of on a
        /// live client, where it looks like an entity at the world origin.
        /// </summary>
        [Fact]
        public void The_shrine_seeds_only_the_transform_and_the_interaction()
        {
            Assert.Equal(new uint[] { 190602, 1210 }, WildernessShrine.SeedComponents);
        }

        [Fact]
        public void The_shrine_answers_to_every_verb_it_advertises_and_no_others()
        {
            Assert.All(WildernessShrine.Verbs, verb => Assert.True(WildernessShrine.Accepts(verb)));

            // PickUp (2) and Craft (5) are routed elsewhere by the interact
            // dispatcher; a shrine that swallowed them would break station pickup
            // for anybody standing next to it.
            Assert.False(WildernessShrine.Accepts(2));
            Assert.False(WildernessShrine.Accepts(5));
        }

        [Fact]
        public void The_interaction_prompt_has_a_radius_and_a_hold()
        {
            // Both non-zero or InteractiveObjectVisualizer never shows a prompt.
            Assert.True(WildernessShrine.InteractRadius > 0f);
            Assert.True(WildernessShrine.InteractTimeToUse > 0f);
        }

        /// <summary>
        /// It has to be findable from where a new player wakes up: close enough to
        /// walk to without instruction, far enough not to be inside them.
        /// </summary>
        [Fact]
        public void The_shrine_stands_a_short_walk_from_the_haven_spawn_point()
        {
            FixedPointPosition shrine = WildernessShrine.PositionOn(IslandCatalog.Haven);
            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;

            double dx = shrine.MetresX - spawn.MetresX;
            double dz = shrine.MetresZ - spawn.MetresZ;
            double horizontal = Math.Sqrt(dx * dx + dz * dz);

            Assert.InRange(horizontal, 6.0, 25.0);
            // ... and on roughly the same ground, not up a cliff or down a hole.
            Assert.InRange(Math.Abs(shrine.MetresY - spawn.MetresY), 0.0, 4.0);
        }

        /// <summary>
        /// It stands ON Haven, checked against Haven's extracted collision envelope
        /// - a different source from the surface table the point came from.
        /// </summary>
        [Fact]
        public void The_shrine_stands_on_haven()
        {
            IslandLocation location = IslandLocationPolicy.Locate(
                WildernessShrine.PositionOn(IslandCatalog.Haven),
                IslandLocationPolicy.KnownWorld());

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.Equal(IslandCatalog.Haven.Id, location.Island!.Id);
        }

        /// <summary>
        /// It must not be standing inside the databank that is already on Haven.
        /// Both are seeded from island-local constants in different files, so
        /// nothing but a test keeps them apart.
        /// </summary>
        [Fact]
        public void The_shrine_does_not_stand_on_top_of_the_haven_databank()
        {
            FixedPointPosition shrine = WildernessShrine.PositionOn(IslandCatalog.Haven);
            FixedPointPosition databank = Databanks.PositionAt(0);

            double dx = shrine.MetresX - databank.MetresX;
            double dz = shrine.MetresZ - databank.MetresZ;

            Assert.True(Math.Sqrt(dx * dx + dz * dz) >= 4.0);
        }

        [Fact]
        public void The_shrine_is_on_by_default_and_can_be_switched_off()
        {
            Assert.True(WildernessShrine.EnabledFrom(null));
            Assert.True(WildernessShrine.EnabledFrom(""));
            Assert.True(WildernessShrine.EnabledFrom("1"));
            Assert.False(WildernessShrine.EnabledFrom("0"));
            Assert.False(WildernessShrine.EnabledFrom("off"));
            Assert.False(WildernessShrine.EnabledFrom("FALSE"));
        }
    }
}
