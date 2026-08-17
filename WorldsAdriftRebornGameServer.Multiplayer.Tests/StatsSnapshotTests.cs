using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The cross-process file contract. These assertions parse the emitted JSON
    /// with the SAME library the login server uses to read it, so a change that
    /// would break the reader breaks a test here first. (Newtonsoft is pulled in
    /// by the test project only; the production Multiplayer assembly stays
    /// dependency-free and hand-builds the JSON.)
    /// </summary>
    public class StatsSnapshotTests
    {
        private static EnetPeerHealth Health(uint rtt) =>
            new EnetPeerHealth(
                state: EnetPeerHealthPolicy.StateConnected,
                roundTripTimeMs: rtt,
                roundTripTimeVarianceMs: 12,
                packetsSent: 1290,
                packetsLost: 3,
                reliableDataInTransit: 1448,
                mtu: 1400);

        private static StatsSnapshot Snapshot(params PlayerStat[] players) =>
            new StatsSnapshot(
                bootTimeUnixMs: 1_723_200_000_000,
                generatedAtUnixMs: 1_723_200_123_000,
                uptimeSeconds: 123,
                relayMode: "v2@20Hz",
                relayHz: 20,
                build: "abc1234",
                totalConnects: 5,
                totalDisconnects: 3,
                currentOnline: players.Length,
                peakOnline: 4,
                players: players);

        [Fact]
        public void Empty_snapshot_is_valid_json_with_the_expected_scalars()
        {
            JObject o = JObject.Parse(Snapshot().ToJson());

            Assert.Equal(StatsSnapshot.SchemaVersion, (int)o["schemaVersion"]!);
            Assert.Equal(1_723_200_000_000, (long)o["bootTimeUnixMs"]!);
            Assert.Equal(123, (long)o["uptimeSeconds"]!);
            Assert.Equal("v2@20Hz", (string)o["relayMode"]!);
            Assert.Equal(20, (int)o["relayHz"]!);
            Assert.Equal("abc1234", (string)o["build"]!);
            Assert.Equal(5, (long)o["totalConnects"]!);
            Assert.Equal(3, (long)o["totalDisconnects"]!);
            Assert.Equal(0, (int)o["currentOnline"]!);
            Assert.Equal(4, (int)o["peakOnline"]!);
            Assert.False((bool)o["wireHealthWarning"]!);
            Assert.False((bool)o["secondIslandRegistered"]!);
            Assert.Equal(0, (int)o["firstRegionTerrainCount"]!);
            Assert.Empty((JArray)o["players"]!);
        }

        [Fact]
        public void Snapshot_reports_actual_first_region_terrain_count()
        {
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), firstRegionTerrainCount: 1);

            Assert.Equal(1, snapshot.FirstRegionTerrainCount);
            Assert.Equal(1, (int)JObject.Parse(snapshot.ToJson())["firstRegionTerrainCount"]!);
        }

        [Fact]
        public void Snapshot_reports_actual_second_island_registry_readiness()
        {
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), secondIslandRegistered: true);

            Assert.True(snapshot.SecondIslandRegistered);
            Assert.True((bool)JObject.Parse(snapshot.ToJson())["secondIslandRegistered"]!);
        }

        [Fact]
        public void A_healthy_player_serialises_its_entity_peer_and_health()
        {
            PlayerStat p = new PlayerStat(3, 0x2f00, 1_723_200_100_000, Health(48));
            JObject o = JObject.Parse(Snapshot(p).ToJson());

            JArray players = (JArray)o["players"]!;
            Assert.Single(players);

            JObject pj = (JObject)players[0];
            Assert.Equal(3, (long)pj["entityId"]!);
            Assert.Equal("0x2f00", (string)pj["peerId"]!);
            Assert.Equal(1_723_200_100_000, (long)pj["connectedAtUnixMs"]!);

            JObject health = (JObject)pj["health"]!;
            Assert.Equal(48, (int)health["rttMs"]!);
            Assert.Equal(12, (int)health["rttVarianceMs"]!);
            Assert.Equal(3, (int)health["packetsLost"]!);
            Assert.Equal(1290, (int)health["packetsSent"]!);
            Assert.Equal(1448, (int)health["inFlightBytes"]!);
            Assert.False((bool)health["spiral"]!);
        }

        [Fact]
        public void A_player_position_is_explicit_world_xyz_and_unknown_stays_null()
        {
            PlayerStat known = new PlayerStat(3, 0x2f00, 0, null,
                FixedPointPosition.FromMetres(14734.5, -55.25, 15208.75));
            PlayerStat unknown = new PlayerStat(7, 0x9900, 0, null);
            JArray players = (JArray)JObject.Parse(Snapshot(known, unknown).ToJson())["players"]!;

            JObject position = (JObject)players[0]["position"]!;
            Assert.Equal(14734.5, (double)position["x"]!, 3);
            Assert.Equal(-55.25, (double)position["y"]!, 3);
            Assert.Equal(15208.75, (double)position["z"]!, 3);
            Assert.Equal(JTokenType.Null, players[1]["position"]!.Type);
        }

        [Fact]
        public void An_unreadable_peer_serialises_health_as_null_not_zeros()
        {
            PlayerStat p = new PlayerStat(7, 0x9900, 1_723_200_050_000, null);
            JObject o = JObject.Parse(Snapshot(p).ToJson());

            JToken health = ((JObject)((JArray)o["players"]!)[0])["health"]!;
            Assert.Equal(JTokenType.Null, health.Type);
        }

        [Fact]
        public void A_spiralling_peer_raises_the_flag_and_marks_itself()
        {
            PlayerStat p = new PlayerStat(3, 0x2f00, 1_723_200_100_000,
                Health(StatsSnapshotPolicy.SpiralRttMs + 1));

            StatsSnapshot snap = Snapshot(p);
            Assert.True(snap.WireHealthWarning);

            JObject o = JObject.Parse(snap.ToJson());
            Assert.True((bool)o["wireHealthWarning"]!);
            Assert.True((bool)((JObject)((JArray)o["players"]!)[0])["health"]!["spiral"]!);
        }

        [Fact]
        public void One_spiralling_peer_among_healthy_ones_still_raises_the_flag()
        {
            StatsSnapshot snap = Snapshot(
                new PlayerStat(1, 0x1000, 0, Health(40)),
                new PlayerStat(2, 0x2000, 0, Health(StatsSnapshotPolicy.SpiralRttMs + 200)),
                new PlayerStat(3, 0x3000, 0, Health(50)));

            Assert.True(snap.WireHealthWarning);
        }

        [Fact]
        public void Exactly_the_threshold_is_not_yet_a_spiral()
        {
            Assert.False(StatsSnapshotPolicy.IsSpiralRtt(StatsSnapshotPolicy.SpiralRttMs));
            Assert.True(StatsSnapshotPolicy.IsSpiralRtt(StatsSnapshotPolicy.SpiralRttMs + 1));
        }

        [Fact]
        public void Build_and_relay_strings_with_specials_stay_valid_json()
        {
            StatsSnapshot snap = new StatsSnapshot(
                0, 0, 0,
                relayMode: "raw\"mode\\\n",
                relayHz: 0,
                build: "tag with \"quotes\" and \\slash",
                0, 0, 0, 0,
                Array.Empty<PlayerStat>());

            // The whole point: it must round-trip through a real parser.
            JObject o = JObject.Parse(snap.ToJson());
            Assert.Equal("raw\"mode\\\n", (string)o["relayMode"]!);
            Assert.Equal("tag with \"quotes\" and \\slash", (string)o["build"]!);
        }

        [Fact]
        public void Ship_domain_snapshot_reports_only_real_local_runtime_state()
        {
            var domain = new ShipDomainStat(
                "ship:83", 83, 4, 91, 240, 35,
                17200.5, -310.25, -1100.75,
                active: true, piloted: true, liveCadenceExpected: true,
                pilotPlayerEntityId: 12, aboardPlayerEntityIds: new long[] { 12, 18 },
                deckCount: 8, mountedPartCount: 3, subscriberCount: 2);
            var topology = new RuntimeDomainStat(
                "ship:83", "ship", "Ship 83", "local:primary", "island:haven",
                entityCount: 12, active: true, warningCount: 0,
                17200.5, -310.25, -1100.75);
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), shipDomains: new[] { domain },
                runtimeDomains: new[] { topology }, runtimeOwnedEntityCount: 72,
                runtimeGlobalEntityCount: 1, runtimeUnownedEntityCount: 0,
                runtimeOwnershipIssueCount: 0);

            JObject root = JObject.Parse(snapshot.ToJson());
            JObject runtime = (JObject)root["runtime"]!;
            Assert.Equal("local-single-process", (string)runtime["hostMode"]!);
            JObject d = (JObject)((JArray)runtime["shipDomains"]!)[0];
            Assert.Equal(83, (long)d["hullEntityId"]!);
            Assert.Equal(4, (long)d["authorityGeneration"]!);
            Assert.Equal(91, (long)d["replicationSequence"]!);
            Assert.Equal(12, (long)d["pilotPlayerEntityId"]!);
            Assert.Equal(2, ((JArray)d["aboardPlayerEntityIds"]!).Count);
            Assert.False((bool)d["staleDelivery"]!);
            Assert.False((bool)d["aboardCheckoutWarning"]!);
            Assert.Equal("local:primary", (string)runtime["hostId"]!);
            Assert.Equal(72, (int)runtime["ownedEntityCount"]!);
            Assert.Equal(1, (int)runtime["globalEntityCount"]!);
            Assert.Equal(0, (int)runtime["ownershipIssueCount"]!);
            JObject topologyNode = (JObject)((JArray)runtime["domains"]!)[0];
            Assert.Equal("ship", (string)topologyNode["kind"]!);
            Assert.Equal("island:haven", (string)topologyNode["affinityDomainId"]!);
            Assert.Equal(12, (int)topologyNode["entityCount"]!);
            Assert.Null(runtime["worker"]);
            Assert.Null(runtime["migrations"]);
        }

        [Fact]
        public void Domain_warning_policy_flags_only_live_staleness_and_checkout_gaps()
        {
            Assert.False(ShipDomainStatPolicy.IsDeliveryStale(false, -1, 240));
            Assert.False(ShipDomainStatPolicy.IsDeliveryStale(true, 960, 240));
            Assert.True(ShipDomainStatPolicy.IsDeliveryStale(true, 1001, 240));
            Assert.True(ShipDomainStatPolicy.IsDeliveryStale(true, -1, 240));
            Assert.False(ShipDomainStatPolicy.HasAboardCheckoutGap(1, 1));
            Assert.True(ShipDomainStatPolicy.HasAboardCheckoutGap(2, 1));
        }
    }
}
