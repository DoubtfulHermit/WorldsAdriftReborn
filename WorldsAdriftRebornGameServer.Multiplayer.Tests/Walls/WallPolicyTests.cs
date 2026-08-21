using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Walls;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Walls
{
    /// <summary>
    /// THE DECISIONS. The flag, the key scheme, the seed order, the blueprint
    /// widening and the interest stance - each one a thing that would otherwise have
    /// been written inline in the game-server assembly, where nothing but a string
    /// match could guard it.
    /// </summary>
    public class WallPolicyTests
    {
        // ====================================================================
        // THE FLAG - default OFF, and off means NOTHING on the wire
        // ====================================================================

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("off")]
        [InlineData("no")]
        [InlineData("maybe")]
        [InlineData("2")]
        public void The_feature_is_off_unless_asked_for(string? raw)
        {
            Assert.False(WallPolicy.Enabled(raw));
        }

        [Theory]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData("yes")]
        [InlineData("On")]
        [InlineData(" 1 ")]
        public void The_feature_turns_on_for_the_same_vocabulary_as_WAREBORN_STORMS(string raw)
        {
            // Two adjacent weather features answering to different spellings of "yes"
            // is a trap; this pins them the same.
            Assert.True(WallPolicy.Enabled(raw));
        }

        [Fact]
        public void The_env_var_is_the_documented_one()
        {
            Assert.Equal("WAREBORN_WALLS", WallPolicy.EnabledEnvVar);
            Assert.Equal("WAREBORN_WALL_TYPES", WallPolicy.TypesEnvVar);
        }

        [Fact]
        public void With_the_feature_off_not_one_entity_is_registered()
        {
            // This is the whole "off is byte-identical on the wire" claim: no
            // registration means no entity id, no AddEntityOp, no asset request and no
            // component seed.
            Assert.Empty(WorldWalls.All(enabled: false));
            Assert.Empty(WorldWalls.All(enabled: false, typesEnv: "0,1,3,5"));
        }

        [Fact]
        public void With_the_feature_on_all_44_walls_are_registered()
        {
            List<WorldEntity> walls = WorldWalls.All(enabled: true).ToList();
            Assert.Equal(44, walls.Count);
        }

        // ====================================================================
        // THE TYPE LEVER - the ambient-bolt mitigation
        // ====================================================================

        [Fact]
        public void An_unset_type_list_means_every_type()
        {
            Assert.Equal(6, WallPolicy.SelectedTypes(null).Count);
            Assert.Equal(6, WallPolicy.SelectedTypes("").Count);
        }

        [Fact]
        public void A_type_list_selects_exactly_those_types()
        {
            IReadOnlyCollection<WallType> chosen = WallPolicy.SelectedTypes("0, 3 ,5");
            Assert.Equal(
                new[] { WallType.WindRift, WallType.SandStorm, WallType.WorldEndWall }.OrderBy(t => t),
                chosen.OrderBy(t => t));
        }

        [Fact]
        public void An_unparseable_type_list_falls_back_to_every_type_not_to_none()
        {
            // A typo must never stop a server booting, and it must never silently
            // empty a feature the operator asked for. "banana" is a mistake; the
            // documented default is the right recovery.
            Assert.Equal(6, WallPolicy.SelectedTypes("banana").Count);
            Assert.Equal(6, WallPolicy.SelectedTypes("9,-1,").Count);

            // A list with SOME junk keeps the good entries rather than throwing them away.
            Assert.Single(WallPolicy.SelectedTypes("1,banana"));
        }

        [Fact]
        public void The_type_lever_actually_drops_the_storm_rifts_from_the_spawn_plan()
        {
            List<WorldEntity> served = WorldWalls.All(enabled: true, typesEnv: "0,3,5").ToList();
            Assert.Equal(33, served.Count);
        }

        // ====================================================================
        // KEYS
        // ====================================================================

        [Fact]
        public void A_wall_key_round_trips_to_its_wall_id()
        {
            Assert.Equal("wall-17", WallPolicy.KeyFor(17));
            Assert.Equal(17, WallPolicy.WallIdFor("wall-17"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("tree-release-650186469-3")]
        [InlineData("island-release-650186469")]
        [InlineData("wilderness-shrine-chamber")]
        [InlineData("wall-")]
        [InlineData("wall-debug-3")]
        [InlineData("wall--1")]
        [InlineData("wall-3.5")]
        [InlineData("wall- 3")]
        public void Anything_that_is_not_exactly_a_wall_key_resolves_to_no_wall(string? key)
        {
            Assert.Null(WallPolicy.WallIdFor(key));
        }

        // ====================================================================
        // THE 8065 WIDENING - the edit that could have changed every entity
        // ====================================================================

        [Theory]
        [InlineData(null)]
        [InlineData("island-haven")]
        [InlineData("tree-haven")]
        [InlineData("ship-frame")]
        [InlineData("wall-debug-3")]
        public void Every_entity_that_is_not_a_wall_still_gets_the_literal_Player(string? key)
        {
            Assert.Equal("Player", WallPolicy.BlueprintNameFor(key));
        }

        [Fact]
        public void Only_a_wall_gets_the_WallSegment_blueprint()
        {
            Assert.Equal("WallSegment", WallPolicy.BlueprintNameFor("wall-0"));
            Assert.Equal("WallSegment", WallPolicy.BlueprintNameFor(WallPolicy.KeyFor(43)));
        }

        // ====================================================================
        // THE PREFAB - the silent-[Require] and unresolvable-prefab hazards
        // ====================================================================

        [Fact]
        public void The_client_can_actually_resolve_the_prefab_we_name()
        {
            // An AddEntityOp naming a prefab the client cannot load is an invisible
            // entity and no log line. The census is extracted from the shipped client
            // assets, so this is the real answer, not a hope.
            Assert.True(WorldsAdriftRebornGameServer.Multiplayer.Ship.ClientEntityPrefabs.CanResolve(WallPolicy.PrefabName));
        }

        [Fact]
        public void Every_registered_wall_names_that_prefab_and_the_default_context()
        {
            foreach (WorldEntity wall in WorldWalls.All(enabled: true))
            {
                Assert.Equal(WallPolicy.PrefabName, wall.AssetName);
                Assert.Equal(WorldEntities.DefaultAssetContext, wall.AssetContext);
                Assert.Equal(SpawnOrder.AfterPlayer, wall.Order);
            }
        }

        // ====================================================================
        // THE SEED ORDER - the hazard that would leave a wall in the wrong place
        // ====================================================================

        [Fact]
        public void A_wall_seeds_its_transform_BEFORE_its_wall_state()
        {
            // WallSegmentVisualizer.OnEnable reads transform.position and hands it to
            // WeatherWalls.Register, which captures the wall's endpoints ONCE and
            // never revisits them. The position is applied by a different behaviour
            // (StaticLocalTransformBehaviour, [Require] TransformStateReader) and the
            // AddEntityOp carries no position at all. If 1204 resolved first, the wall
            // would register wherever the prefab was instantiated and stay there for
            // the entity's whole life, with no log line. SendAddComponentOp preserves
            // list order, so this order IS the mitigation.
            foreach (WorldEntity wall in WorldWalls.All(enabled: true))
            {
                Assert.Equal(new uint[] { 190602, 1204 }, wall.SeedComponents.ToArray());
            }
        }

        [Fact]
        public void The_seed_list_contains_nothing_that_could_drop_the_batch()
        {
            // The seed batch goes out with failOnComponentInitError: true, so one id
            // without a ComponentsSerializer branch drops the WHOLE batch and leaves a
            // rendered, inert wall. Two ids, both of which have branches, is the
            // smallest set that satisfies the prefab's local-mode transform stack plus
            // the visualiser's single [Require].
            Assert.Equal(2, WallPolicy.SeedComponents.Count);
        }

        // ====================================================================
        // INTEREST - stated as a decision, not left as an oversight
        // ====================================================================

        [Fact]
        public void Walls_are_deliberately_NOT_spatially_streamed()
        {
            // A key outside IsStreamedResourceKey is broadcast to every client
            // eagerly. For a wall that is the only correct answer: our interest radius
            // is 120 m, a wall influences a client from 800 m, and WeatherWalls
            // registers on OnEnable - so an interest-gated wall would always check out
            // after the player was already inside it.
            Assert.False(WallPolicy.IsStreamed);
            Assert.False(ResourceInterestPolicy.IsStreamedResourceKey(WallPolicy.KeyFor(0)));

            // Control, same call: a tree IS streamed, so the assertion above is about
            // walls and not about a broken predicate.
            Assert.True(ResourceInterestPolicy.IsStreamedResourceKey("tree-release-650186469-3"));
        }

        // ====================================================================
        // WHAT MUST NEVER BE SERVED
        // ====================================================================

        [Fact]
        public void The_wall_seed_never_mentions_1229()
        {
            // 1229 GlobalWallDataState carries wind/gust/torque scalars whose retail
            // values are unrecoverable, Debug.LogErrors per missing key, and silently
            // skips a wall type's whole torque table on a miss. Half of it is worse
            // than none of it.
            Assert.DoesNotContain(1229u, WallPolicy.SeedComponents);
        }

        [Fact]
        public void Component_1204_is_not_a_forbidden_component()
        {
            // The precondition the research states: nothing about this feature is
            // fighting ComponentAbsencePolicy.
            Assert.False(ComponentAbsencePolicy.IsKnownAbsent(
                WallPolicy.WallSegmentStateComponentId));
        }

        // ====================================================================
        // THE BOOT LINE
        // ====================================================================

        [Fact]
        public void The_boot_line_names_the_variable_when_off_and_the_bolt_cost_when_on()
        {
            Assert.Contains("WAREBORN_WALLS", WorldWalls.Describe(enabled: false));
            Assert.Contains("OFF", WorldWalls.Describe(enabled: false));

            string on = WorldWalls.Describe(enabled: true);
            Assert.Contains("44 of 44", on);
            Assert.Contains("11 storm rift", on);
            Assert.Contains("km of storm wall", on);
        }
    }
}
