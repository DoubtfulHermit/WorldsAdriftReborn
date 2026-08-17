using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The cadence and the timestamp that keep a bolted part's follow-visualizer
    /// AWAKE. The part sleeps one second after its last 190602 change and only wakes
    /// on the next one, so a heartbeat that is too slow, or a timestamp that does not
    /// advance, is a deck that stops following the hull mid-flight - a failure a
    /// running client would show only as a player sliding off a moving ship. These
    /// pin both properties natively.
    /// </summary>
    public class ShipPartMotionPolicyTests
    {
        [Fact]
        public void The_heartbeat_is_strictly_below_the_one_second_sleep_with_margin()
        {
            // The client's FixedUpdateLerpLocalTransformBehaviour sleeps after 1 s.
            // A wake must land inside every sleep window; 0.5 s leaves a full extra
            // wake of slack. Anything >= 1 s could let a part nod off between wakes.
            Assert.True(ShipPartMotionPolicy.HeartbeatIntervalSeconds < 1.0);
            Assert.True(ShipPartMotionPolicy.HeartbeatIntervalSeconds <= 0.5);
            Assert.True(ShipPartMotionPolicy.HeartbeatIntervalSeconds > 0.0);
        }

        [Fact]
        public void The_wake_carries_the_transform_state_component_id()
        {
            // 190602 - the same component the seed places the part with; a value
            // update on it is what fires PropertyUpdated -> WakeUp.
            Assert.Equal(190602u, ShipPartMotionPolicy.TransformStateComponentId);
        }

        [Fact]
        public void The_stamp_strictly_increases_across_every_index_a_session_reaches()
        {
            // Monotonicity is the ONE property the client's interpolator needs: it
            // discards a stamp that does not advance, which would silently stop waking
            // the part. Walk a long session's worth of wakes and assert every step up.
            double step = ShipPartMotionPolicy.HeartbeatIntervalSeconds;
            float previous = ShipPartMotionPolicy.StampFor(0, step);
            for (long i = 1; i <= 200_000; i++)
            {
                float current = ShipPartMotionPolicy.StampFor(i, step);
                Assert.True(current > previous,
                    "stamp did not increase at index " + i + " (" + previous + " -> " + current + ")");
                previous = current;
            }
        }

        [Fact]
        public void The_first_wake_sits_at_the_synthetic_epoch()
        {
            // Index 0 is the origin - the same small positive epoch the 1073 relay
            // seeds - so the child's timeline starts just ahead of the receiver's
            // playback clock rather than at zero.
            Assert.Equal(ShipPartMotionPolicy.SeedStampSeconds,
                ShipPartMotionPolicy.StampFor(0, ShipPartMotionPolicy.HeartbeatIntervalSeconds));
        }

        [Fact]
        public void Two_mounts_on_one_relative_child_let_the_parent_sampling_reach_the_second()
        {
            // Fix 2 (findings-mount-placement.md section 2). A Parent(hull,"~") part is sampled
            // by the client at the PARENT hull's 190602 timestamp. Model TWO mounts on the SAME
            // part: each stamps the child (StampFor) AND, under the fix, advances the hull to
            // the same time (ParentStampFor). The interpolator can only select a child sample
            // once the requested (parent) time reaches it, so the second sample is reachable
            // ONLY if the hull timeline advanced with the child.
            double step = ShipPartMotionPolicy.HeartbeatIntervalSeconds;

            const long firstMount = 1;
            const long secondMount = 2;

            float childFirst = ShipPartMotionPolicy.StampFor(firstMount, step);
            float childSecond = ShipPartMotionPolicy.StampFor(secondMount, step);

            // The two child samples are distinct and advancing - the second sits strictly ahead.
            Assert.True(childSecond > childFirst);

            // THE FIX: the hull shares the child's clock, so the parent-sampling time after the
            // second mount reaches (equals) the second child sample and it becomes selectable.
            float parentAfterSecond = ShipPartMotionPolicy.ParentStampFor(secondMount, step);
            Assert.Equal(childSecond, parentAfterSecond);
            Assert.True(parentAfterSecond >= childSecond,
                "parent-sampling time " + parentAfterSecond + " never reaches the second child sample " + childSecond);
            Assert.True(ShipPartMotionPolicy.ParentSamplingReaches(secondMount, step));

            // FAILS-BEFORE: the old built hull's 190602 was a SEED frozen at timestamp 0 (its
            // 1130 motion never advanced that stamp), so the parent kept requesting time 0, the
            // child interpolator returned its FIRST sample, and the second was never selected -
            // re-position was a visible no-op. This is exactly the assertion that fails under
            // the old behaviour and passes under the fix.
            const float frozenHullSeedTimestamp = 0f;
            Assert.True(frozenHullSeedTimestamp < childSecond);
            Assert.False(frozenHullSeedTimestamp >= childSecond,
                "a hull frozen at seed timestamp 0 must NOT reach a positive child sample");
        }

        [Fact]
        public void The_parent_hull_stamp_shares_the_child_clock_at_every_index()
        {
            // The coherent-timeline invariant, walked across a session: the hull's 190602 stamp
            // equals the child's for the same mount, so the parent always reaches the newest
            // child sample rather than lagging behind on a separate clock.
            double step = ShipPartMotionPolicy.HeartbeatIntervalSeconds;
            for (long i = 0; i <= 10_000; i++)
            {
                Assert.Equal(ShipPartMotionPolicy.StampFor(i, step), ShipPartMotionPolicy.ParentStampFor(i, step));
                Assert.True(ShipPartMotionPolicy.ParentSamplingReaches(i, step));
            }
        }

        [Fact]
        public void Bolted_parts_are_exactly_the_registered_parts_never_the_hull()
        {
            // "Which parts" the heartbeat wakes: the deck (on by default) and, when
            // enabled, the helm/engine/sail - but never the hull, and never a tree or
            // the island. With the extra parts on, all four parts are present.
            WorldEntityRegistry withDeck = WorldEntities.Default(new EntityIdAllocator());
            var deckOnlyKeys = withDeck.BoltedParts().Select(p => p.Key).ToList();
            Assert.Contains(WorldEntities.DeckKey, deckOnlyKeys);
            Assert.Contains(WorldEntities.HelmKey, deckOnlyKeys);      // helm is always on
            Assert.DoesNotContain(WorldEntities.ShipFrameKey, deckOnlyKeys);
            Assert.DoesNotContain(WorldEntities.IslandKey, deckOnlyKeys);

            WorldEntityRegistry withParts = WorldEntities.Default(new EntityIdAllocator(), includeExtraParts: true);
            var allKeys = withParts.BoltedParts().Select(p => p.Key).ToList();
            Assert.Contains(WorldEntities.EngineKey, allKeys);
            Assert.Contains(WorldEntities.SailKey, allKeys);
            Assert.All(allKeys, k => Assert.True(WorldEntities.IsBoltedPartKey(k)));
        }

        [Fact]
        public void No_bolted_parts_are_reported_when_the_deck_is_off_and_extras_are_off()
        {
            // Deck off + extras off (helm still on): only the helm remains a part, and
            // with everything ship-part off there is nothing to wake.
            WorldEntityRegistry noDeck = WorldEntities.Default(new EntityIdAllocator(), includeDeck: false);
            var keys = noDeck.BoltedParts().Select(p => p.Key).ToList();
            Assert.DoesNotContain(WorldEntities.DeckKey, keys);
            Assert.Contains(WorldEntities.HelmKey, keys);
        }
    }
}
