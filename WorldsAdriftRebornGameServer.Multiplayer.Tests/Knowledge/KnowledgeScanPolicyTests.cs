using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The GAIN half of the knowledge loop: scanning a databank the first time pays a
    /// chunk of knowledge, a repeat pays nothing, a non-databank owes no response.
    /// Pure - no ENet, no game types.
    /// </summary>
    public class KnowledgeScanPolicyTests
    {
        [Fact]
        public void First_scan_of_a_databank_grants_the_chunk_to_both_pools()
        {
            ScanGrant g = KnowledgeScanPolicy.Evaluate(
                targetIsScannableDatabank: true,
                alreadyScanned: false,
                knowledge: 1,
                lifetimeKnowledge: 1,
                grantAmount: 50);

            Assert.Equal(ScanGrantOutcome.Granted, g.Outcome);
            Assert.Equal(51, g.NewKnowledge);
            Assert.Equal(51, g.NewLifetimeKnowledge);
            Assert.Equal(50, g.KnowledgeGained);
        }

        [Fact]
        public void One_scan_puts_a_player_past_the_cheapest_meaningful_unlock()
        {
            // 50 knowledge clears "Shipbuilding" (cost 20) with room to spare - the
            // whole point of a databank being a big chunk.
            ScanGrant g = KnowledgeScanPolicy.Evaluate(true, false, 1, 1, Databanks.GrantAmount);
            Assert.True(g.NewKnowledge >= 20);
        }

        [Fact]
        public void A_repeated_scan_grants_nothing_and_changes_no_counter()
        {
            ScanGrant g = KnowledgeScanPolicy.Evaluate(
                targetIsScannableDatabank: true,
                alreadyScanned: true,
                knowledge: 51,
                lifetimeKnowledge: 51,
                grantAmount: 50);

            Assert.Equal(ScanGrantOutcome.Repeated, g.Outcome);
            Assert.Equal(51, g.NewKnowledge);
            Assert.Equal(51, g.NewLifetimeKnowledge);
            Assert.Equal(0, g.KnowledgeGained);
        }

        [Fact]
        public void A_non_databank_target_owes_no_response()
        {
            ScanGrant g = KnowledgeScanPolicy.Evaluate(
                targetIsScannableDatabank: false,
                alreadyScanned: false,
                knowledge: 1,
                lifetimeKnowledge: 1,
                grantAmount: 50);

            Assert.Equal(ScanGrantOutcome.NotScannable, g.Outcome);
            Assert.Equal(1, g.NewKnowledge);
            Assert.Equal(0, g.KnowledgeGained);
        }

        [Fact]
        public void A_negative_grant_is_clamped_to_zero()
        {
            ScanGrant g = KnowledgeScanPolicy.Evaluate(true, false, 10, 10, -5);
            Assert.Equal(ScanGrantOutcome.Granted, g.Outcome);
            Assert.Equal(10, g.NewKnowledge);
            Assert.Equal(0, g.KnowledgeGained);
        }
    }
}
