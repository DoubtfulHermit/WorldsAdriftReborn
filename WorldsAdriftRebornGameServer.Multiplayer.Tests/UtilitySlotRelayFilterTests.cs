using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The 6910 change-detection relay. Every case here is a frame the client
    /// actually puts on the wire given the VERIFIED FinishAndSend_ResolveDiff
    /// behaviour: a bool-flip carries only the changed bool, a held-active utility
    /// carries a health field only (no bool), a static utility carries nothing.
    /// The filter's whole job is to turn ~170/s of that into on/off events.
    /// </summary>
    public class UtilitySlotRelayFilterTests
    {
        private const long Entity = 42L;

        // ------------------------------------------------------------------
        // The bufferbloat case: health-only frames must never be relayed.
        // ------------------------------------------------------------------

        [Fact]
        public void A_health_only_update_carrying_no_bool_is_never_relayed()
        {
            var filter = new UtilitySlotRelayFilter();
            // The ~170/s spam: ResolveDiff cleared the bools (unchanged), leaving a
            // health float only. On the wire that is a 6910 update with no bool.
            UtilitySlotRelayDecision d = filter.Decide(Entity, head: null, body: null, feet: null);
            Assert.False(d.Relay);
        }

        [Fact]
        public void A_thousand_health_only_frames_produce_zero_relays()
        {
            var filter = new UtilitySlotRelayFilter();
            for (int i = 0; i < 1000; i++)
            {
                Assert.False(filter.Decide(Entity, null, null, null).Relay);
            }
        }

        // ------------------------------------------------------------------
        // The deliverable: a glider deploy is one relayed event.
        // ------------------------------------------------------------------

        [Fact]
        public void Deploying_the_body_utility_relays_once_and_carries_the_full_triple()
        {
            var filter = new UtilitySlotRelayFilter();
            // Body flip arrives alone (head/feet cleared as unchanged).
            UtilitySlotRelayDecision d = filter.Decide(Entity, head: null, body: true, feet: null);

            Assert.True(d.Relay);
            Assert.True(d.Body);   // wings open
            Assert.False(d.Head);  // filled from the all-inactive baseline
            Assert.False(d.Feet);
        }

        [Fact]
        public void A_deploy_then_the_health_drain_that_follows_relays_exactly_once()
        {
            var filter = new UtilitySlotRelayFilter();

            Assert.True(filter.Decide(Entity, null, true, null).Relay); // deploy

            // Everything after is health drain: no bool on the wire.
            for (int i = 0; i < 500; i++)
            {
                Assert.False(filter.Decide(Entity, null, null, null).Relay);
            }
        }

        [Fact]
        public void Deploy_then_retract_is_two_relays()
        {
            var filter = new UtilitySlotRelayFilter();

            UtilitySlotRelayDecision deploy = filter.Decide(Entity, null, true, null);
            Assert.True(deploy.Relay);
            Assert.True(deploy.Body);

            for (int i = 0; i < 50; i++) filter.Decide(Entity, null, null, null); // gliding

            UtilitySlotRelayDecision retract = filter.Decide(Entity, null, false, null);
            Assert.True(retract.Relay);
            Assert.False(retract.Body); // wings closed
        }

        // ------------------------------------------------------------------
        // Redundant / no-op frames.
        // ------------------------------------------------------------------

        [Fact]
        public void Re_sending_a_bool_that_matches_what_was_last_relayed_does_not_relay_again()
        {
            var filter = new UtilitySlotRelayFilter();
            Assert.True(filter.Decide(Entity, null, true, null).Relay);   // deploy
            Assert.False(filter.Decide(Entity, null, true, null).Relay);  // same value again
        }

        [Fact]
        public void A_first_update_that_matches_the_all_inactive_seed_default_is_not_relayed()
        {
            var filter = new UtilitySlotRelayFilter();
            // Remote already spawns all-inactive; body:false changes nothing.
            Assert.False(filter.Decide(Entity, null, false, null).Relay);
            // ...but a later genuine deploy still relays (baseline was recorded).
            Assert.True(filter.Decide(Entity, null, true, null).Relay);
        }

        // ------------------------------------------------------------------
        // Independent slots and the merge.
        // ------------------------------------------------------------------

        [Fact]
        public void Head_and_feet_transitions_relay_independently_and_preserve_the_other_slots()
        {
            var filter = new UtilitySlotRelayFilter();

            UtilitySlotRelayDecision body = filter.Decide(Entity, null, true, null);
            Assert.True(body.Relay);

            // A head utility comes on while the body is still active: the relayed
            // triple must keep Body=true (merged), not reset it.
            UtilitySlotRelayDecision head = filter.Decide(Entity, head: true, body: null, feet: null);
            Assert.True(head.Relay);
            Assert.True(head.Head);
            Assert.True(head.Body); // preserved through the merge
            Assert.False(head.Feet);
        }

        [Fact]
        public void An_update_setting_all_three_bools_at_once_relays_the_exact_triple()
        {
            var filter = new UtilitySlotRelayFilter();
            UtilitySlotRelayDecision d = filter.Decide(Entity, head: true, body: false, feet: true);
            Assert.True(d.Relay);
            Assert.True(d.Head);
            Assert.False(d.Body);
            Assert.True(d.Feet);
        }

        // ------------------------------------------------------------------
        // Per-entity isolation and lifecycle.
        // ------------------------------------------------------------------

        [Fact]
        public void Two_entities_are_tracked_independently()
        {
            var filter = new UtilitySlotRelayFilter();
            Assert.True(filter.Decide(1L, null, true, null).Relay);
            // Entity 2 has its own baseline; the same deploy is new to it.
            Assert.True(filter.Decide(2L, null, true, null).Relay);
            // Neither relays a redundant repeat.
            Assert.False(filter.Decide(1L, null, true, null).Relay);
            Assert.False(filter.Decide(2L, null, true, null).Relay);
        }

        [Fact]
        public void Forgetting_an_entity_resets_it_to_the_seed_baseline()
        {
            var filter = new UtilitySlotRelayFilter();
            Assert.True(filter.Decide(Entity, null, true, null).Relay);
            Assert.False(filter.Decide(Entity, null, true, null).Relay); // held

            filter.Forget(Entity);

            // After a reconnect the remote is a fresh all-inactive rig, so the
            // same deploy must relay again.
            Assert.True(filter.Decide(Entity, null, true, null).Relay);
        }
    }
}
