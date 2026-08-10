using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The hull-relative transform seed for a bolted ship part. The whole point is
    /// that a part FOLLOWS the hull instead of standing still in world space while
    /// the hull's PathFollower drifts out from under it (the "player falls through
    /// the deck" bug). These pin the arithmetic the client composes with the hull's
    /// live position - which a running client would only show as a player sinking
    /// through a moving deck - so it cannot regress unseen.
    /// </summary>
    public class BoltedPartTransformTests
    {
        [Fact]
        public void Local_offset_plus_hull_reconstructs_the_parts_original_global_seed()
        {
            // The invariant the whole fix rests on: (local offset) + hull == the
            // part's old world-absolute seed. So parenting places the part in exactly
            // the same spot the drifting seed did - it just now moves with the hull.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;

            foreach (FixedPointPosition partGlobal in new[]
            {
                Deck.OnHull(hull),
                Helm.OnDeckOf(hull),
                ShipParts.EngineOnHull(hull),
                ShipParts.SailOnHull(hull),
            })
            {
                FixedPointPosition off = BoltedPartTransform.LocalOffset(partGlobal, hull);
                Assert.Equal(partGlobal, new FixedPointPosition(hull.X + off.X, hull.Y + off.Y, hull.Z + off.Z));
            }
        }

        [Fact]
        public void The_offset_is_independent_of_where_the_hull_is()
        {
            // A hull-relative offset must be the SAME no matter where the hull sits -
            // that is precisely what lets the client compose it against the hull's
            // ever-changing live position. Move the hull a long way and the deck's
            // local offset must not budge.
            FixedPointPosition near = new FixedPointPosition(0, 0, 0);
            FixedPointPosition far = new FixedPointPosition(70502113, -1273730, -4580013);

            Assert.Equal(
                BoltedPartTransform.LocalOffset(Deck.OnHull(near), near),
                BoltedPartTransform.LocalOffset(Deck.OnHull(far), far));
        }

        [Fact]
        public void The_deck_offset_is_the_flat_up_only_delta_of_its_on_hull_position()
        {
            // The deck is centred on the hull (origin-centred vertices), so X and Z of
            // the offset are zero and only Y carries the (currently zero) up offset.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            FixedPointPosition off = BoltedPartTransform.LocalOffset(Deck.OnHull(hull), hull);

            Assert.Equal(0, off.X);
            Assert.Equal((long)(Deck.DeckUpMetres * FixedPointPosition.UnitsPerMetre), off.Y);
            Assert.Equal(0, off.Z);
        }

        [Fact]
        public void The_helm_offset_is_one_metre_forward_on_the_deck()
        {
            // The helm sits +1 m fore of the hull centre; nothing else offset.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            FixedPointPosition off = BoltedPartTransform.LocalOffset(Helm.OnDeckOf(hull), hull);

            Assert.Equal(0, off.X);
            Assert.Equal((long)(Helm.DeckUpMetres * FixedPointPosition.UnitsPerMetre), off.Y);
            Assert.Equal((long)(Helm.DeckForwardMetres * FixedPointPosition.UnitsPerMetre), off.Z);
        }

        [Fact]
        public void By_key_agrees_with_the_registration_derived_offset_for_every_part()
        {
            // The keyed helper (used by tests/docs) and the plain subtraction the
            // serializer uses must never disagree - that is what guarantees the
            // registry's global part positions and the seeded hull-relative offsets
            // stay in lockstep.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;

            Assert.Equal(
                BoltedPartTransform.LocalOffset(WorldEntities.Deck01().Position, hull),
                BoltedPartTransform.LocalOffsetFor(WorldEntities.DeckKey, hull));
            Assert.Equal(
                BoltedPartTransform.LocalOffset(WorldEntities.Helm().Position, hull),
                BoltedPartTransform.LocalOffsetFor(WorldEntities.HelmKey, hull));
            Assert.Equal(
                BoltedPartTransform.LocalOffset(WorldEntities.ModularEngine().Position, hull),
                BoltedPartTransform.LocalOffsetFor(WorldEntities.EngineKey, hull));
            Assert.Equal(
                BoltedPartTransform.LocalOffset(WorldEntities.Sail01().Position, hull),
                BoltedPartTransform.LocalOffsetFor(WorldEntities.SailKey, hull));
        }

        [Fact]
        public void By_key_returns_null_for_the_hull_and_for_non_parts()
        {
            // The hull is not bolted to itself, and an unrelated key is not a part.
            FixedPointPosition hull = WorldEntities.ShipFrame().Position;
            Assert.Null(BoltedPartTransform.LocalOffsetFor(WorldEntities.ShipFrameKey, hull));
            Assert.Null(BoltedPartTransform.LocalOffsetFor(WorldEntities.IslandKey, hull));
            Assert.Null(BoltedPartTransform.LocalOffsetFor(null, hull));
        }

        [Fact]
        public void Only_the_deck_gets_a_real_hierarchy_key_the_rest_follow_relatively()
        {
            // The carry fix rests on this one split: the DECK is a real Unity child of
            // the hull (a non-"~" key), so a player on it raycasts the hull's rigidbody
            // and rides the hull's PathFollower; helm/engine/sail only position-FOLLOW
            // the hull via the "~" relative slot and keep their own rigidbody.
            Assert.Equal(Deck.HierarchyKey, BoltedPartTransform.HierarchyKeyFor(WorldEntities.DeckKey));
            Assert.Equal("~", BoltedPartTransform.HierarchyKeyFor(WorldEntities.HelmKey));
            Assert.Equal("~", BoltedPartTransform.HierarchyKeyFor(WorldEntities.EngineKey));
            Assert.Equal("~", BoltedPartTransform.HierarchyKeyFor(WorldEntities.SailKey));
            Assert.Equal("~", BoltedPartTransform.HierarchyKeyFor(null));
        }

        [Fact]
        public void The_deck_hierarchy_key_is_a_plain_word_not_the_relative_slot_and_not_an_offset_slot()
        {
            // Non-"~" so it triggers a real Unity re-parent (not the position-follow),
            // and no leading "#" so it is never a registered TransformOffsetsRegistry
            // slot - it always falls back to the hull ROOT transform.
            Assert.NotEqual("~", Deck.HierarchyKey);
            Assert.False(Deck.HierarchyKey.StartsWith("#"));
            Assert.False(string.IsNullOrEmpty(Deck.HierarchyKey));
        }

        [Fact]
        public void Only_the_deck_is_a_unity_child_so_only_it_is_skipped_by_the_wake()
        {
            // IsUnityChild is what ShipPartMotionService.PublishWake filters on: a real
            // child must NOT be re-woken (re-sending its parent every heartbeat churns an
            // unparent+reparent), while every "~" follower must be.
            Assert.True(BoltedPartTransform.IsUnityChild(WorldEntities.DeckKey));
            Assert.False(BoltedPartTransform.IsUnityChild(WorldEntities.HelmKey));
            Assert.False(BoltedPartTransform.IsUnityChild(WorldEntities.EngineKey));
            Assert.False(BoltedPartTransform.IsUnityChild(WorldEntities.SailKey));
            Assert.False(BoltedPartTransform.IsUnityChild(null));
        }
    }
}
