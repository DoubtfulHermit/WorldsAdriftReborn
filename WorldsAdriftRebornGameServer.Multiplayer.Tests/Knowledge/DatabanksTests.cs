using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>The databank prefab name, grant chunk, placement and server ledger.</summary>
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
    }
}
