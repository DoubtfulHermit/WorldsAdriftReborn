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
        /// THE REGRESSION THAT SHIPPED. The shrine's InteractiveObjectVisualizer is
        /// on the SpawnPad child, 3.204 m BELOW the plate a player stands on, and
        /// the client measures range to that transform. The original 3 m radius -
        /// copied from the nugget, whose visualizer is on its root - described a
        /// sphere whose highest point was still underground. This is the assertion
        /// that would have caught it: not "is the radius non-zero" but "can anybody
        /// standing on the thing actually see the prompt".
        /// </summary>
        [Fact]
        public void A_player_standing_anywhere_on_the_spawn_plate_is_offered_the_prompt()
        {
            // Dead centre.
            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius, 0f, WildernessShrine.PadTopAboveVisualiserMetres));

            // And out at the plate's own edge, which is the harder case.
            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius,
                WildernessShrine.PadHalfWidthMetres,
                WildernessShrine.PadTopAboveVisualiserMetres));

            // The 3 m this used to be could not do either.
            Assert.False(InteractReach.IsReachable(
                3.0f, 0f, WildernessShrine.PadTopAboveVisualiserMetres));
        }

        /// <summary>
        /// The prompt should meet the player on the walk up, not only once both
        /// feet are on the plate - and the radius must be DERIVED from the three
        /// measured numbers, so that correcting a measurement corrects the radius
        /// instead of quietly disagreeing with it.
        /// </summary>
        [Fact]
        public void The_radius_is_the_one_the_measured_geometry_asks_for()
        {
            float derived = InteractReach.RadiusToCover(
                WildernessShrine.PadHalfWidthMetres + WildernessShrine.ApproachRingMetres,
                WildernessShrine.PadTopAboveVisualiserMetres);

            Assert.Equal(derived, WildernessShrine.InteractRadius);

            Assert.True(InteractReach.IsReachable(
                WildernessShrine.InteractRadius,
                WildernessShrine.PadHalfWidthMetres + WildernessShrine.ApproachRingMetres,
                WildernessShrine.PadTopAboveVisualiserMetres));
        }

        /// <summary>
        /// The measurements themselves, pinned. They come from the shipped client's
        /// own copy of the prefab and are the only reason the radius above is what
        /// it is; an edit here that is not an actual re-measurement is a bug.
        /// </summary>
        [Fact]
        public void The_pad_geometry_is_the_measured_prefab_geometry()
        {
            // SpawnPad.localPosition.y = -2.704, top of its collision meshes at
            // prefab-local +0.500.
            Assert.Equal(3.204f, WildernessShrine.PadTopAboveVisualiserMetres, 3);
            // Respawner_Plate local AABB: x and z both -3.57 .. +3.57.
            Assert.Equal(3.57f, WildernessShrine.PadHalfWidthMetres, 3);
        }

        /// <summary>
        /// Activate is now RECOVERED from the prefab rather than guessed, so
        /// whatever else the hedge carries, that one has to be in it.
        /// </summary>
        [Fact]
        public void The_seed_carries_the_verb_the_prefab_actually_bakes()
        {
            Assert.Contains(WildernessShrine.VerbActivate, WildernessShrine.Verbs);
            Assert.True(WildernessShrine.Accepts(WildernessShrine.VerbActivate));
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
