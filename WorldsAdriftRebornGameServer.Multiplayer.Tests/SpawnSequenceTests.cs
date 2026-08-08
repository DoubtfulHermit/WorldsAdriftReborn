using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The order a joining client is walked into the world. Wrong order is
    /// silent: the player is published over geometry that has not streamed in
    /// yet and falls forever, because this server writes no HealthState and so
    /// has no fall damage, and WorldEdgePushback never runs.
    /// </summary>
    public class SpawnSequenceTests
    {
        [Fact]
        public void The_island_entity_is_created_before_the_player_entity()
        {
            Assert.True(SpawnSequence.IslandPrecedesPlayer(SpawnSequence.Steps));
        }

        [Fact]
        public void The_island_bundle_is_requested_before_the_island_entity_is_created()
        {
            List<SpawnStep> steps = SpawnSequence.Steps.ToList();
            Assert.True(steps.IndexOf(SpawnStep.RequestIslandAsset)
                      < steps.IndexOf(SpawnStep.AddIslandEntity));
        }

        [Fact]
        public void Swapping_island_and_player_is_rejected()
        {
            // The guard has to actually reject something, or it is decoration.
            Assert.False(SpawnSequence.IslandPrecedesPlayer(new[]
            {
                SpawnStep.RequestPlayerAsset,
                SpawnStep.RequestIslandAsset,
                SpawnStep.AddPlayerEntity,
                SpawnStep.AddIslandEntity,
            }));
        }

        [Fact]
        public void A_sequence_that_never_loads_the_island_is_rejected()
        {
            Assert.False(SpawnSequence.IslandPrecedesPlayer(new[]
            {
                SpawnStep.RequestPlayerAsset,
                SpawnStep.AddPlayerEntity,
            }));
        }

        [Fact]
        public void Every_step_appears_exactly_once()
        {
            foreach (SpawnStep step in Enum.GetValues<SpawnStep>())
            {
                Assert.Single(SpawnSequence.Steps, s => s == step);
            }
        }

        [Fact]
        public void Asset_requests_wait_for_an_asset_ack_and_entity_adds_for_an_entity_ack()
        {
            // The ack is what gates the next step, and it is also the only
            // throttle on bundle loading anywhere: the client's asset loader is
            // synchronous and unbudgeted.
            Assert.Equal(SpawnAck.AssetLoaded, SpawnSequence.AckFor(SpawnStep.RequestPlayerAsset));
            Assert.Equal(SpawnAck.AssetLoaded, SpawnSequence.AckFor(SpawnStep.RequestIslandAsset));
            Assert.Equal(SpawnAck.EntityAdded, SpawnSequence.AckFor(SpawnStep.AddIslandEntity));
            Assert.Equal(SpawnAck.EntityAdded, SpawnSequence.AckFor(SpawnStep.AddPlayerEntity));
        }

        [Fact]
        public void Every_step_has_an_ack()
        {
            foreach (SpawnStep step in Enum.GetValues<SpawnStep>())
            {
                // Throws if a new step is added without deciding what advances past it.
                SpawnSequence.AckFor(step);
            }
        }
    }
}
