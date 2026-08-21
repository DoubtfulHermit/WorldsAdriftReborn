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

        /// <summary>
        /// The DURABLE identity on a player row - v8 - and the field an operator
        /// command should be addressed to.
        ///
        /// Every other identifier in this row is recycled, so a dashboard that
        /// only publishes entity and peer ids can only offer an operator a target
        /// that may already mean somebody else by the time they click it. It is
        /// always written, "" when unknown, so a reader can tell "this server does
        /// not publish it" from "this player has none yet".
        /// </summary>
        [Fact]
        public void A_player_row_carries_the_durable_character_uid()
        {
            JObject o = JObject.Parse(Snapshot(
                new PlayerStat(7, 0x1f, 1_723_200_000_000, Health(30), null,
                    "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")).ToJson());

            Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                (string?)o["players"]![0]!["characterUid"]);
        }

        [Fact]
        public void A_player_with_no_uid_yet_publishes_an_empty_string_not_a_missing_field()
        {
            JObject o = JObject.Parse(Snapshot(
                new PlayerStat(7, 0x1f, 1_723_200_000_000, Health(30))).ToJson());

            Assert.NotNull(o["players"]![0]!["characterUid"]);
            Assert.Equal("", (string?)o["players"]![0]!["characterUid"]);
        }

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

        /// <summary>
        /// The ship domain's top-level owner is a SECOND SPELLING of the hull's,
        /// not a second copy.
        ///
        /// Two owner fields that can drift apart is the exact hazard this merge
        /// had to avoid: the map's ship detail panel reads
        /// <c>hull.ownerCharacterUid</c> and the operator surface's "summon the
        /// ship this player owns" reads the top-level one, and a ship that
        /// belonged to two different people depending on which panel you opened
        /// would be unexplainable. There is one storage location and this pins it.
        /// </summary>
        [Fact]
        public void The_ship_domains_owner_is_the_hulls_owner_and_cannot_disagree()
        {
            const string owner = "77777777-7777-7777-7777-777777777777";
            ShipHullStat hull = new ShipHullStat(null, owner, docked: false, materials: null);
            ShipDomainStat domain = new ShipDomainStat(
                "ship:83", 83, 4, 91, 240, 35,
                0, 0, 0, active: true, piloted: false, liveCadenceExpected: false,
                pilotPlayerEntityId: null, aboardPlayerEntityIds: Array.Empty<long>(),
                deckCount: 0, mountedPartCount: 0, subscriberCount: 0,
                hull: hull);

            Assert.Equal(owner, domain.OwnerCharacterUid);
            Assert.Equal(domain.Hull.OwnerCharacterUid, domain.OwnerCharacterUid);

            // And both spellings reach the wire agreeing, because a reader may use
            // either one.
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), shipDomains: new[] { domain });
            JObject d = (JObject)((JArray)((JObject)JObject.Parse(snapshot.ToJson())["runtime"]!)
                ["shipDomains"]!)[0];

            Assert.Equal(owner, (string?)d["ownerCharacterUid"]);
            Assert.Equal(owner, (string?)d["hull"]!["ownerCharacterUid"]);
        }

        [Fact]
        public void A_ship_domain_carries_live_force_inputs_and_prediction()
        {
            ShipFlightStat flight = new ShipFlightStat(
                massKg: 3094, mountedSails: 3, unfurledSails: 2,
                windX: 1, windZ: -2, windAngleDegrees: -26.565,
                sailForceNewtons: 715.5, engineForceNewtons: 0,
                propulsionAccelerationMps2: 0.23125,
                predictedTerminalSpeedMps: 5.75);
            ShipDomainStat domain = new ShipDomainStat(
                "ship:83", 83, 4, 91, 240, 35,
                0, 0, 0, active: true, piloted: false, liveCadenceExpected: true,
                pilotPlayerEntityId: null, aboardPlayerEntityIds: Array.Empty<long>(),
                deckCount: 1, mountedPartCount: 3, subscriberCount: 1,
                flight: flight);
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), shipDomains: new[] { domain });

            JObject d = (JObject)((JArray)((JObject)JObject.Parse(snapshot.ToJson())["runtime"]!)
                ["shipDomains"]!)[0];
            JObject f = (JObject)d["flight"]!;
            Assert.True((bool)f["present"]!);
            Assert.Equal(3094, (double)f["massKg"]!);
            Assert.Equal(3, (int)f["mountedSails"]!);
            Assert.Equal(2, (int)f["unfurledSails"]!);
            Assert.Equal(715.5, (double)f["sailForceNewtons"]!);
            Assert.Equal(5.75, (double)f["predictedTerminalSpeedMps"]!);
        }

        /// <summary>
        /// A hull whose bytes are missing has NO SHAPE but still has an OWNER.
        ///
        /// Those are different facts kept in different places, and conflating them
        /// would make "summon the ship this player owns" answer "they own nothing"
        /// about a ship that exists, is owned and flies - the silent wrong answer
        /// the whole operator surface is built to refuse to give.
        /// </summary>
        [Fact]
        public void A_hull_with_no_silhouette_still_reports_its_owner()
        {
            const string owner = "88888888-8888-8888-8888-888888888888";
            ShipHullStat hull = new ShipHullStat(null, owner, docked: true, materials: null);

            Assert.False(hull.Present);
            Assert.Equal(owner, hull.OwnerCharacterUid);
        }

        // ---- the per-hull geometry block (v11) --------------------------------

        /// <summary>The live 60-byte hull, decoded, as the two views and a mounted helm.</summary>
        private static ShipHullStat LiveHullStat(params Multiplayer.Ship.ShipPartMark[] parts)
        {
            const string hex =
                "020000000000e80000180000e8008e18008e0000000000ffff0000e80000180000e8"
                + "00001800000000000001e80000180000e8007218007200000000";
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            Assert.True(Multiplayer.Ship.ShipPlanModel.TryDecode(bytes, out Multiplayer.Ship.ShipPlanModel? plan, out string? e), e);
            return new ShipHullStat(
                Multiplayer.Ship.ShipMapSilhouette.Of(plan), "owner", docked: false, materials: null,
                profile: Multiplayer.Ship.ShipMapProfile.Of(plan), parts: parts);
        }

        private static JObject HullJson(ShipHullStat hull)
        {
            ShipDomainStat domain = new ShipDomainStat(
                "ship:83", 83, 4, 91, 240, 35,
                0, 0, 0, active: true, piloted: false, liveCadenceExpected: false,
                pilotPlayerEntityId: null, aboardPlayerEntityIds: Array.Empty<long>(),
                deckCount: 0, mountedPartCount: 0, subscriberCount: 0, hull: hull);
            StatsSnapshot snapshot = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), shipDomains: new[] { domain });
            return (JObject)((JArray)((JObject)JObject.Parse(snapshot.ToJson())["runtime"]!)
                ["shipDomains"]!)[0]!["hull"]!;
        }

        /// <summary>
        /// The elevation, the decks and the mounted parts reach the wire in the
        /// hull's own metres. This is the contract the login server parses and the
        /// ship card draws; if the names or the units move, both ends break at once.
        /// </summary>
        [Fact]
        public void The_hull_geometry_block_carries_the_elevation_decks_and_parts()
        {
            JObject hull = HullJson(LiveHullStat(
                new Multiplayer.Ship.ShipPartMark(Multiplayer.Ship.ShipPartKinds.Helm, "Helm", 0, 3.4, 1)));
            JObject geometry = (JObject)hull["geometry"]!;

            Assert.True((bool)geometry["present"]!);
            Assert.Equal(0.0, (double)geometry["floorMetres"]!);
            Assert.Equal(3.4, (double)geometry["headMetres"]!);
            Assert.Equal(3.4, (double)geometry["heightMetres"]!);
            Assert.Equal(3, (int)geometry["sectionCount"]!);
            // Three stations, two edges, flat z,y.
            Assert.Equal(12, ((JArray)geometry["profile"]!).Count);

            JObject deck = (JObject)((JArray)geometry["decks"]!).Single();
            Assert.Equal(0, (int)deck["deckNumber"]!);
            Assert.Equal(3.4, (double)deck["planeMetres"]!);
            Assert.Equal(-6.0, (double)deck["sternZMetres"]!);
            Assert.Equal(2.0, (double)deck["bowZMetres"]!);

            JObject part = (JObject)((JArray)geometry["parts"]!).Single();
            Assert.Equal("helm", (string)part["kind"]!);
            Assert.Equal("Helm", (string)part["title"]!);
            Assert.Equal(3.4, (double)part["y"]!);
            Assert.Equal(1.0, (double)part["z"]!);
        }

        /// <summary>
        /// THE REVISION NAMES THE DRAWING. It must be stable while the drawing is,
        /// and must move the moment anything on it does - that is the whole reason
        /// the geometry can stay out of the live poll: a reader refetches when, and
        /// only when, this number changes.
        /// </summary>
        [Fact]
        public void The_geometry_revision_is_stable_until_the_drawing_changes()
        {
            Multiplayer.Ship.ShipPartMark helm = new Multiplayer.Ship.ShipPartMark(Multiplayer.Ship.ShipPartKinds.Helm, "Helm", 0, 3.4, 1);
            long first = (long)HullJson(LiveHullStat(helm))["geometryRevision"]!;
            long again = (long)HullJson(LiveHullStat(helm))["geometryRevision"]!;
            Assert.Equal(first, again);
            Assert.True(first > 0, "a hull with a drawing must have a revision");

            // A part MOVED by a metre is a different drawing.
            long moved = (long)HullJson(LiveHullStat(
                new Multiplayer.Ship.ShipPartMark(Multiplayer.Ship.ShipPartKinds.Helm, "Helm", 0, 3.4, 2)))["geometryRevision"]!;
            Assert.NotEqual(first, moved);

            // A part ADDED is a different drawing.
            long added = (long)HullJson(LiveHullStat(helm,
                new Multiplayer.Ship.ShipPartMark(Multiplayer.Ship.ShipPartKinds.Lamp, "Lamp", 2, 3.4, 1)))["geometryRevision"]!;
            Assert.NotEqual(first, added);
        }

        /// <summary>
        /// A hull with no shape publishes an ABSENT geometry block, not a missing
        /// one. Absence must read as "an older game server"; an empty object that
        /// says present:false reads as "this ship's shape is unavailable", which is
        /// the true thing and the thing the card prints.
        /// </summary>
        [Fact]
        public void An_undecodable_hull_publishes_an_absent_geometry_block_rather_than_none()
        {
            JObject hull = HullJson(new ShipHullStat(null, "owner", docked: true, materials: null));
            JObject geometry = (JObject)hull["geometry"]!;

            Assert.False((bool)hull["present"]!);
            Assert.False((bool)geometry["present"]!);
            Assert.Empty((JArray)geometry["profile"]!);
            Assert.Empty((JArray)geometry["decks"]!);
            Assert.Empty((JArray)geometry["parts"]!);
            Assert.Equal(0.0, (double)geometry["heightMetres"]!);
        }

        /// <summary>
        /// A hull whose SHAPE is unavailable can still say where its parts are. The
        /// two facts have different availability, exactly as the owner does, and a
        /// card that hid the helm because it could not draw the hull would be losing
        /// information it actually has.
        /// </summary>
        [Fact]
        public void A_shapeless_hull_still_reports_the_parts_mounted_on_it()
        {
            JObject geometry = (JObject)HullJson(new ShipHullStat(
                null, "owner", docked: true, materials: null,
                profile: null,
                parts: new[] { new Multiplayer.Ship.ShipPartMark(Multiplayer.Ship.ShipPartKinds.Sail, "Sail", 0, 3.4, -1) }))
                ["geometry"]!;

            Assert.False((bool)geometry["present"]!);
            JObject part = (JObject)((JArray)geometry["parts"]!).Single();
            Assert.Equal("sail", (string)part["kind"]!);
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
