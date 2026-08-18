using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// The 1211 -> graduation route, and the reason it gives when it refuses.
    ///
    /// This exists because of a live session on 2026-08-18: a player held E on the
    /// shrine, the server was receiving 1211 at frame rate, and nothing happened -
    /// with no log line anywhere saying whether an interaction had even arrived.
    /// Every case below is a distinct failure that used to be indistinguishable
    /// from every other.
    /// </summary>
    public sealed class ShrineInteractRoutingTests
    {
        private const int Activate = 1;

        [Fact]
        public void The_shrines_own_key_with_its_own_verb_uses_it()
        {
            Assert.Equal(ShrineInteractOutcome.Use,
                ShrineInteractRouting.Decide(true, Activate, WildernessShrine.WorldEntityKey));
        }

        /// <summary>
        /// PROVED from the prefab: `Respawner01_unityclient` has no
        /// InteractiveObjectVerbOverrider anywhere in its hierarchy and its root
        /// visualizer's serialized Verb is 1, so Activate is what a live client
        /// sends however the prompt labels it.
        /// </summary>
        [Fact]
        public void Activate_is_the_verb_a_live_client_sends()
        {
            Assert.True(WildernessShrine.Accepts(WildernessShrine.VerbActivate));
            Assert.Equal(1, WildernessShrine.VerbActivate);
        }

        [Fact]
        public void An_interaction_on_anything_else_is_not_a_refusal()
        {
            Assert.Equal(ShrineInteractOutcome.NotTheShrine,
                ShrineInteractRouting.Decide(true, Activate, "helm-haven"));
            Assert.Equal(ShrineInteractOutcome.NotTheShrine,
                ShrineInteractRouting.Decide(true, Activate, null));

            // ...and must not produce a log line, or every E press in the world
            // writes one.
            Assert.False(ShrineInteractRouting.IsAboutTheShrine(ShrineInteractOutcome.NotTheShrine));
        }

        /// <summary>
        /// The CHAMBER is a different entity with a different key, and it must never
        /// route: it advertises no interaction at all, and its own buried plate is
        /// the thing this whole feature proved unreachable.
        /// </summary>
        [Fact]
        public void The_chamber_is_not_the_shrine()
        {
            Assert.Equal(ShrineInteractOutcome.NotTheShrine,
                ShrineInteractRouting.Decide(true, Activate, WildernessChamber.WorldEntityKey));
            Assert.NotEqual(WildernessChamber.WorldEntityKey, WildernessShrine.WorldEntityKey);
        }

        [Fact]
        public void A_peer_cannot_fire_the_shrine_for_somebody_elses_entity()
        {
            ShrineInteractOutcome outcome =
                ShrineInteractRouting.Decide(false, Activate, WildernessShrine.WorldEntityKey);

            Assert.Equal(ShrineInteractOutcome.NotOwner, outcome);
            Assert.True(ShrineInteractRouting.IsAboutTheShrine(outcome));
            Assert.Contains("REFUSED", ShrineInteractRouting.Explain(outcome));
        }

        [Fact]
        public void A_verb_the_shrine_does_not_advertise_is_refused_out_loud()
        {
            // PickUp (2) and Craft (5) are routed elsewhere by the dispatcher.
            ShrineInteractOutcome outcome =
                ShrineInteractRouting.Decide(true, 2, WildernessShrine.WorldEntityKey);

            Assert.Equal(ShrineInteractOutcome.WrongVerb, outcome);
            Assert.True(ShrineInteractRouting.IsAboutTheShrine(outcome));
            Assert.Contains("REFUSED", ShrineInteractRouting.Explain(outcome));
        }

        /// <summary>
        /// EVERY outcome that touched the shrine has to be explainable. A new
        /// outcome added without a sentence is a new way to fail silently.
        /// </summary>
        [Theory]
        [InlineData(ShrineInteractOutcome.Use)]
        [InlineData(ShrineInteractOutcome.NotOwner)]
        [InlineData(ShrineInteractOutcome.WrongVerb)]
        public void Every_shrine_outcome_says_something(ShrineInteractOutcome outcome)
        {
            Assert.False(string.IsNullOrWhiteSpace(ShrineInteractRouting.Explain(outcome)));
            Assert.True(ShrineInteractRouting.IsAboutTheShrine(outcome));
        }
    }
}
