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
