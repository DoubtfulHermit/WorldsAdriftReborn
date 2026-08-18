using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>The databank prefab name, grant chunk, placement and server ledger.</summary>
    // Shares the static DatabankLedger with FidelityCheapWinsTests; see the note
    // there. The shared collection serialises the two classes so neither one's
    // Clear() can land inside the other's assertions.
    [Collection(Tests.DatabankLedgerCollection.Name)]
    public class DatabanksTests
    {
        [Fact]
        public void The_prefab_name_is_the_bare_DataBank_001_the_client_can_resolve()
        {
            // VERIFIED: DataBank_001 is in prefab-names.tsv (client AND worker "yes").
            // Bare, because the client appends the worker suffix itself.
            Assert.Equal("DataBank_001", Databanks.AssetName);
            Assert.DoesNotContain("_unity", Databanks.AssetName);
        }

        [Fact]
        public void One_scan_chunk_clears_the_cheapest_meaningful_unlock()
        {
            Assert.True(Databanks.GrantAmount >= 20); // Shipbuilding costs 20
        }

        [Fact]
        public void The_default_placement_is_a_reachable_near_spawn_key()
        {
            Assert.True(Databanks.HavenPlacements.Count >= 1);
            Assert.Equal("databank-0", Databanks.KeyFor(0));
            Assert.True(Databanks.IsDatabankKey("databank-0"));
            Assert.False(Databanks.IsDatabankKey("deposit-0"));
        }

        [Fact]
        public void The_ledger_registers_a_databank_once_and_reports_its_grant()
        {
            DatabankLedger.Clear();
            Assert.True(DatabankLedger.Register(4242, 50));
            Assert.False(DatabankLedger.Register(4242, 99)); // idempotent
            Assert.True(DatabankLedger.IsDatabank(4242));
            Assert.False(DatabankLedger.IsDatabank(9999));
            Assert.Equal(50, DatabankLedger.GrantFor(4242));
            Assert.Equal(0, DatabankLedger.GrantFor(9999));
            DatabankLedger.Clear();
        }

        /// <summary>
        /// The knowledge economy at stock values.
        ///
        /// The grant was 10,000 behind a "TESTING" comment, against a tree whose
        /// cheapest meaningful unlock cost 20 - so one scan bought most of the
        /// tree and four databanks on one island paid out 40,000. Retail's figures
        /// are 25 per databank and 50 for Shipbuilding, so the unlock costs two
        /// scans. Pinned because a generous grant is invisible in play until the
        /// whole progression is already gone.
        /// </summary>
        [Fact]
        public void A_databank_grants_retail_knowledge_and_shipbuilding_costs_two_scans()
        {
            Assert.Equal(25, Databanks.GrantAmount);
            Assert.True(Databanks.GrantAmount * 2 >= 50,
                "two databank scans must cover Shipbuilding's 50");
            Assert.True(Databanks.GrantAmount < 50,
                "one scan must NOT cover it, or there is no spend decision");
        }
    }
}
