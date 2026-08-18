using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The client's own range rule, and the one thing it can do that nothing else
    /// in this server models: make a seeded 1210 radius UNREACHABLE FROM EVERY
    /// POSITION IN THE WORLD, silently.
    ///
    /// The numbers here are the decompiled ones - a 0.5 m penalty added to the
    /// measured distance before a strict less-than against the radius.
    /// </summary>
    public sealed class InteractReachTests
    {
        [Fact]
        public void The_client_penalty_is_the_half_metre_PlayerLookingAt_adds()
        {
            Assert.Equal(0.5f, InteractReach.LookRangePenaltyMetres);
        }

        [Fact]
        public void Standing_on_the_visualiser_needs_a_radius_over_the_penalty_alone()
        {
            // distance 0 + 0.5 < radius. A radius of exactly 0.5 fails: the
            // client's comparison is strict.
            Assert.False(InteractReach.IsReachable(0.5f, 0f, 0f));
            Assert.True(InteractReach.IsReachable(0.51f, 0f, 0f));
        }

        [Fact]
        public void A_zero_radius_reaches_nothing()
        {
            // The classic "no prompt appears" seed. Pinned here so the failure has
            // a name rather than being rediscovered on a live client.
            Assert.False(InteractReach.IsReachable(0f, 0f, 0f));
            Assert.Equal(0f, InteractReach.MaxHeightAbove(0f));
        }

        [Fact]
        public void Distance_is_the_three_dimensional_one_not_the_horizontal_one()
        {
            // 3-4-5. A radius of 5.4 leaves the player 0.1 m inside; 5.5 puts them
            // exactly on the boundary, which the client refuses.
            Assert.True(InteractReach.IsReachable(5.6f, 4f, 3f));
            Assert.False(InteractReach.IsReachable(5.5f, 4f, 3f));
        }

        [Fact]
        public void Vertical_offset_alone_can_exhaust_the_whole_radius()
        {
            // THE SHRINE BUG, in the abstract: a visualizer 3.204 m below the
            // surface the player stands on, with a 3 m radius, is unreachable even
            // standing directly on top of it.
            Assert.False(InteractReach.IsReachable(3.0f, 0f, 3.204f));
            Assert.True(InteractReach.MaxHeightAbove(3.0f) < 3.204f);
        }

        [Fact]
        public void A_derived_radius_actually_covers_the_point_it_was_derived_from()
        {
            float radius = InteractReach.RadiusToCover(5.57f, 3.204f);

            Assert.True(InteractReach.IsReachable(radius, 5.57f, 3.204f));
            // Rounded up to a readable tenth, and not padded beyond one.
            Assert.Equal(7.0f, radius);
            Assert.True(radius - InteractReach.MinimumRadiusFor(5.57f, 3.204f) <= 0.1f);
        }

        [Fact]
        public void An_exact_tenth_still_clears_the_strict_comparison()
        {
            // MinimumRadiusFor(0, 0.5) is exactly 1.0; ceiling would return the
            // boundary itself, which the client rejects.
            float radius = InteractReach.RadiusToCover(0f, 0.5f);

            Assert.True(radius > InteractReach.MinimumRadiusFor(0f, 0.5f));
            Assert.True(InteractReach.IsReachable(radius, 0f, 0.5f));
        }

        /// <summary>
        /// The three radii this server has been seeding all along, checked against
        /// the prefab offsets they are actually used at. They were fine - which is
        /// exactly why copying 3 m onto a prefab with a buried visualizer looked
        /// safe.
        /// </summary>
        [Theory]
        // nugget and helm: InteractiveObjectVisualizer sits on the prefab ROOT, so
        // the visualizer transform IS the entity origin (offset 0) and a player
        // standing on the ground beside it is level with it.
        [InlineData(3.0f, 0.0f)]
        // placed shipyard: the console's visualizer is on the Crafting_Station
        // child at local +1.299 m, i.e. ABOVE the player's feet, which shortens the
        // reach but never exhausts it.
        [InlineData(3.0f, 1.299f)]
        public void The_existing_three_metre_seeds_still_reach_their_own_prefabs(
            float radius, float verticalOffsetMetres)
        {
            Assert.True(InteractReach.IsReachable(radius, 0f, verticalOffsetMetres));
            // ...and with room to stand a stride away, not only dead on top of it.
            Assert.True(InteractReach.IsReachable(radius, 1.5f, verticalOffsetMetres));
        }
    }
}
