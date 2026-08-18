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
        /// THE FIRST REGRESSION THAT SHIPPED: an interaction volume that never broke
        /// the surface. The client measures range to the
        /// InteractiveObjectVisualizer's OWN transform, so a prefab whose visualizer
        /// sits below the plate can be given a radius that no standable point in the
        /// world satisfies. This is the assertion that catches it: not "is the radius
        /// non-zero" but "can anybody standing on the thing actually see the prompt".
        /// </summary>
        [Fact]
        public void A_player_standing_anywhere_on_the_plate_is_offered_the_prompt()
        {
            // Dead centre.
            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius, 0f, WildernessShrine.PadTopAboveVisualiserMetres));

            // And out at the plate's own edge, which is the harder case.
            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius,
                WildernessShrine.PadHalfWidthMetres,
                WildernessShrine.PadTopAboveVisualiserMetres));

            // The Revival Chamber's plate was 3.204 m above its visualizer; the 3 m
            // radius this shrine first shipped with could not reach it from anywhere.
            Assert.False(InteractReach.IsReachable(3.0f, 0f, 3.204f));
        }

        /// <summary>
        /// The prompt has to meet the player on the walk up, not only once both feet
        /// are on a 1.2 m plate they have to find first. Three metres of walk-up ring
        /// is the difference between "the shrine announced itself" and "I stood on it
        /// and nothing happened".
        /// </summary>
        [Fact]
        public void The_prompt_reaches_a_walk_up_ring_around_the_plate()
        {
            float ring = WildernessShrine.PadHalfWidthMetres + 3.0f;

            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius, ring, WildernessShrine.PadTopAboveVisualiserMetres));
        }

        /// <summary>
        /// The measurements themselves, pinned. They come from the shipped client's
        /// own copy of the prefab and are the only reason the radius above works; an
        /// edit here that is not an actual re-measurement is a bug.
        ///
        /// The ZERO is the load-bearing one: Respawner01's visualizer is on the
        /// prefab ROOT, which is what makes a small radius reach at all.
        /// </summary>
        [Fact]
        public void The_plate_geometry_is_the_measured_prefab_geometry()
        {
            // Visualizer offset 0.00 + plate collider top 0.20.
            Assert.Equal(0.20f, WildernessShrine.PadTopAboveVisualiserMetres, 3);
            // Collision extent: x and z both -0.60 .. +0.60.
            Assert.Equal(0.60f, WildernessShrine.PadHalfWidthMetres, 3);
        }

        /// <summary>
        /// THE SECOND REGRESSION THAT SHIPPED: the shrine was placed inside the
        /// ruined metal camp. Its nearest authored structure was 13.7 m away and the
        /// 40 m prefab standing there was driven through the camp's platforms; on
        /// 2026-08-18 a player logged in inside it and had to be rescued with the
        /// admin teleport.
        ///
        /// This recomputes the clearance from the embedded prop table rather than
        /// trusting a number in a comment, so moving the shrine back into the camp
        /// fails here.
        /// </summary>
        [Fact]
        public void The_shrine_stands_clear_of_everything_already_built_on_haven()
        {
            double clearance = HavenStructures.ClearanceAt(
                WildernessShrine.HavenLocalPlacement.X, WildernessShrine.HavenLocalPlacement.Z);

            Assert.True(clearance >= 15.0,
                "the shrine is " + clearance.ToString("0.0") + " m from an authored Haven structure");

            // The old point, for contrast: it is INSIDE the camp, and this is the
            // check that was missing when it was chosen.
            Assert.True(HavenStructures.ClearanceAt(176.00, 16.00) < 15.0);
        }

        /// <summary>
        /// Horizontal distance is not clearance on Haven. The camp is a multi-storey
        /// ruin - the spawn point itself sits under a platform 19.5 m up - so a
        /// placement also has to have nothing hanging over it.
        /// </summary>
        [Fact]
        public void Nothing_authored_stands_over_the_shrine()
        {
            Assert.Equal(0, HavenStructures.CountNear(
                WildernessShrine.HavenLocalPlacement.X,
                WildernessShrine.HavenLocalPlacement.Y,
                WildernessShrine.HavenLocalPlacement.Z,
                radiusMetres: 8.0, belowMetres: 2.0, aboveMetres: 25.0));
        }

        /// <summary>
        /// The prefab has to be one whose InteractiveObjectVisualizer is on the ROOT.
        /// That is not a style preference: the client measures interaction range to
        /// the visualizer's own transform, and the Revival Chamber failed precisely
        /// because its visualizer was on a child at the bottom of a sealed well. A
        /// zero offset is the property that makes a small radius work.
        /// </summary>
        [Fact]
        public void The_shrine_prefab_carries_its_interaction_on_its_own_origin()
        {
            Assert.Equal("Respawner01", WildernessShrine.AssetName);
            // Visualizer offset zero => the plate top is the only vertical term.
            Assert.True(WildernessShrine.PadTopAboveVisualiserMetres < 1.0f);
        }

        /// <summary>
        /// It has to be findable from where a new player wakes up: a walk, not an
        /// expedition, and on the same shelf rather than up a cliff.
        /// </summary>
        [Fact]
        public void The_shrine_stands_a_short_walk_from_the_haven_spawn_point()
        {
            FixedPointPosition shrine = WildernessShrine.PositionOn(IslandCatalog.Haven);
            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;

            double dx = shrine.MetresX - spawn.MetresX;
            double dz = shrine.MetresZ - spawn.MetresZ;
            double horizontal = Math.Sqrt(dx * dx + dz * dz);

            Assert.InRange(horizontal, 10.0, 50.0);
            // No climb: the server has no pathing and cannot promise a route, so
            // the least it can do is not put the thing on a different level.
            Assert.InRange(Math.Abs(shrine.MetresY - spawn.MetresY), 0.0, 3.0);
        }

        /// <summary>
        /// Toward the island's local origin, not away from it. Retail's own quest
        /// text puts the Revival Chamber "at the center of the island", and the
        /// spawn point sits far out at local x = 208; a shrine placed further out
        /// still would be the one direction that contradicts the source.
        /// </summary>
        [Fact]
        public void The_shrine_is_further_toward_the_island_centre_than_the_spawn_point()
        {
            Assert.True(Math.Abs(WildernessShrine.HavenLocalPlacement.X)
                < Math.Abs(TeleportPolicy.HavenSpawnLocalOffset.X));
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
