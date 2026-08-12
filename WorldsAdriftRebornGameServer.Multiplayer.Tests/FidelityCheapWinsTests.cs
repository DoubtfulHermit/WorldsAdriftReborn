using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The pure decision halves of the three "seed the finished value -> seed the
    /// in-progress value then flip" fidelity fixes: the shipyard fold-out, the crafted-part
    /// materialize dissolve, the timed station craft, and the scan-note text.
    /// </summary>
    // Shares the static DatabankLedger with Knowledge/DatabanksTests. xUnit runs
    // test CLASSES in parallel, so without a shared collection the two race on that
    // global state: one class's Clear() lands in the middle of the other's
    // Register/ScanDataFor pair and the assert fails for reasons that have nothing
    // to do with either test. Latent since both classes existed - it surfaced when
    // an unrelated new test class shifted the scheduling.
    [Collection(DatabankLedgerCollection.Name)]
    public class FidelityCheapWinsTests
    {
        // ---- 3.1 Shipyard fold-out ------------------------------------------------

        [Fact]
        public void Live_placement_seeds_deployed_false_and_schedules_the_fold_out()
        {
            // A live placement must PLAY the fold-out: deployed=false at seed, and a flip is due.
            Assert.False(ShipyardDeployPolicy.InitialDeployed(livePlacement: true));
            Assert.True(ShipyardDeployPolicy.AnimatesFoldOut(livePlacement: true));
        }

        [Fact]
        public void Boot_restore_stays_deployed_true_and_never_re_animates()
        {
            // A restored yard was already deployed last session: snap (deployed=true), no flip.
            Assert.True(ShipyardDeployPolicy.InitialDeployed(livePlacement: false));
            Assert.False(ShipyardDeployPolicy.AnimatesFoldOut(livePlacement: false));
        }

        [Fact]
        public void The_fold_out_flip_always_ends_deployed()
        {
            Assert.True(ShipyardDeployPolicy.DeployedAfterFlip);
        }

        [Fact]
        public void Deploy_seconds_falls_back_to_the_default_and_honours_a_valid_override()
        {
            Assert.Equal(ShipyardDeployPolicy.DefaultDeploySeconds, ShipyardDeployPolicy.DeploySeconds(null));
            Assert.Equal(ShipyardDeployPolicy.DefaultDeploySeconds, ShipyardDeployPolicy.DeploySeconds("  "));
            Assert.Equal(ShipyardDeployPolicy.DefaultDeploySeconds, ShipyardDeployPolicy.DeploySeconds("junk"));
            Assert.Equal(ShipyardDeployPolicy.DefaultDeploySeconds, ShipyardDeployPolicy.DeploySeconds("-4"));
            Assert.Equal(5.5f, ShipyardDeployPolicy.DeploySeconds("5.5"));
        }

        // ---- 3.2 / 6.2 Crafted-part materialize ----------------------------------

        [Fact]
        public void A_fresh_loose_part_seeds_spawning_true_with_a_full_timer()
        {
            CraftableSpawnState s = CraftableSpawnPolicy.Materializing(2.0f);
            Assert.True(s.Spawning);
            Assert.Equal(2.0f, s.TimeLeft);
            Assert.Equal(2.0f, s.TotalTime); // timeLeft == totalTime -> dissolve starts at progress 0
        }

        [Fact]
        public void A_settled_loose_part_is_spawning_false_so_it_is_liftable()
        {
            // The MANDATORY flip target: spawning=false makes the part non-kinematic + pickable.
            Assert.False(CraftableSpawnPolicy.Done.Spawning);
            Assert.Equal(0f, CraftableSpawnPolicy.Done.TimeLeft);
            Assert.Equal(0f, CraftableSpawnPolicy.Done.TotalTime);
        }

        [Fact]
        public void Materialize_seconds_falls_back_to_the_default_and_honours_a_valid_override()
        {
            Assert.Equal(CraftableSpawnPolicy.DefaultMaterializeSeconds, CraftableSpawnPolicy.MaterializeSeconds(null));
            Assert.Equal(CraftableSpawnPolicy.DefaultMaterializeSeconds, CraftableSpawnPolicy.MaterializeSeconds("0"));
            Assert.Equal(3.25f, CraftableSpawnPolicy.MaterializeSeconds("3.25"));
        }

        // ---- 6.1 Timed station craft ----------------------------------------------

        [Fact]
        public void A_station_craft_is_held_open_for_at_least_the_floor()
        {
            // A placeholder 0-second recipe still holds the aperture open visibly.
            Assert.Equal(StationCraftTimePolicy.MinCraftingSeconds, StationCraftTimePolicy.Seconds(0));
            Assert.Equal(StationCraftTimePolicy.MinCraftingSeconds, StationCraftTimePolicy.Seconds(-5));
            // A real longer craft time wins.
            Assert.Equal(12, StationCraftTimePolicy.Seconds(12));
            Assert.True(StationCraftTimePolicy.MinCraftingSeconds >= 1); // never fires instantly
        }

        // ---- 5.1 Scan note text ---------------------------------------------------

        [Fact]
        public void The_scan_note_is_well_formed_ScannableData_json_with_title_and_body()
        {
            string json = ScannableNote.Json("Ancient Databank", "A cache of knowledge.");
            JObject parsed = JObject.Parse(json); // valid JSON (client parses it with JsonUtility)
            Assert.Equal("Ancient Databank", (string?)parsed["title"]);
            Assert.Equal("A cache of knowledge.", (string?)parsed["description"]);
        }

        [Fact]
        public void The_scan_note_escapes_quotes_and_newlines_so_a_note_cannot_break_the_json()
        {
            string json = ScannableNote.Json("A \"quoted\" ruin", "line1\nline2\\end");
            JObject parsed = JObject.Parse(json); // must not throw
            Assert.Equal("A \"quoted\" ruin", (string?)parsed["title"]);
            Assert.Equal("line1\nline2\\end", (string?)parsed["description"]);
        }

        [Fact]
        public void A_registered_databank_serves_its_note_json_and_a_non_databank_serves_nothing()
        {
            DatabankLedger.Clear();
            Assert.True(DatabankLedger.Register(7001, 50, "Ancient Databank", "Lore body."));
            Assert.False(DatabankLedger.Register(7001, 99, "x", "y")); // idempotent, keeps the first note
            Assert.Equal(50, DatabankLedger.GrantFor(7001));

            JObject note = JObject.Parse(DatabankLedger.ScanDataFor(7001));
            Assert.Equal("Ancient Databank", (string?)note["title"]);
            Assert.Equal("Lore body.", (string?)note["description"]);

            Assert.Equal("", DatabankLedger.ScanDataFor(9999)); // not a databank -> no note owed
            DatabankLedger.Clear();
        }

        [Fact]
        public void The_two_argument_register_still_works_and_yields_a_parseable_empty_note()
        {
            DatabankLedger.Clear();
            Assert.True(DatabankLedger.Register(8001, 25)); // legacy 2-arg call
            JObject note = JObject.Parse(DatabankLedger.ScanDataFor(8001)); // still non-null / parseable
            Assert.Equal("", (string?)note["title"]);
            DatabankLedger.Clear();
        }
    }
}
