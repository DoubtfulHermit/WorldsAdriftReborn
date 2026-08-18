using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;
using WorldsAdriftServer.PublicMap;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// The contract of the public map: the anonymizing whitelist in
    /// <see cref="PublicMapProjection"/> is the ONLY thing standing between
    /// the authenticated operator feed and a page anyone on the internet can
    /// open, so these tests are written to bite.
    ///
    /// The centrepiece is the leak corpus: a production-shaped stats file
    /// seeded with a sentinel in EVERY sensitive position - including fields
    /// the game server does not write today but plausibly will (account
    /// names, character uids, ship owners, display names) - pushed through
    /// the real parse-project-serialize path, and string-searched. If someone
    /// widens the projection carelessly, or replaces the whitelist with a
    /// pass-through, these assertions fail by name.
    /// </summary>
    public class PublicMapProjectionTests
    {
        private static readonly DateTimeOffset Now =
            DateTimeOffset.FromUnixTimeMilliseconds(1_723_200_123_000);

        /// <summary>Fixed so tokens are reproducible within a test.</summary>
        private static readonly byte[] SaltA = FilledSalt(0x2a);
        private static readonly byte[] SaltB = FilledSalt(0x7b);

        private static byte[] FilledSalt(byte value)
        {
            byte[] salt = new byte[32];
            Array.Fill(salt, value);
            return salt;
        }

        // ---- the leak corpus ------------------------------------------------

        /// <summary>
        /// Every sensitive value seeded below, by name. The test asserts each
        /// one is absent from the serialized public payload; keeping them in
        /// one list means adding a new sensitive field to the corpus is one
        /// line, and forgetting to assert on it is impossible.
        /// </summary>
        private static readonly string[] SensitiveSentinels =
        {
            // identity the file carries TODAY
            "PEER-SENTINEL",          // player peerId
            "987654321",              // player entityId (also pilot/aboard refs)
            "918273645",              // ship hullEntityId (also inside domainId)
            "86420",                  // rttMs
            "7654321",                // packetsSent
            "1723100000111",          // connectedAtUnixMs
            // identity a FUTURE schema might add - the whitelist must already drop it
            "ACCOUNT-SENTINEL",       // account name
            "CHARUID-SENTINEL",       // character uid
            "NAME-SENTINEL",          // player display name
            "SHIPNAME-SENTINEL",      // ship name
            "OWNER-SENTINEL",         // ship owner uid
            "HULLOWNER-SENTINEL",     // the owner uid the hull block really carries
            "ROOT-SENTINEL",          // an operator field at the root
            "FAUNA-SENTINEL",         // an unexpected field inside fauna
            "ECOLOGY-SENTINEL",       // an unexpected field inside the v9 ecology block
            "424242",                 // the ecology's worldSeed: an operator knob, admin-only
            // the v11 hull GEOMETRY block, served from its own endpoint but
            // through this same whitelist
            "PARTTITLE-SENTINEL",     // the catalogue title of a mounted part
            "PARTOWNER-SENTINEL",     // the uid of whoever bolted a part on
            "GEOMETRY-SENTINEL",      // an unexpected field inside the geometry block
        };

        private const string CorpusJson = @"{
          ""schemaVersion"":7,
          ""bootTimeUnixMs"":1723100000000,
          ""generatedAtUnixMs"":1723200120000,
          ""uptimeSeconds"":100120,
          ""relayMode"":""v2@20Hz"",""relayHz"":20,""build"":""abc1234"",
          ""totalConnects"":9,""totalDisconnects"":8,
          ""currentOnline"":1,""peakOnline"":3,
          ""operatorNote"":""ROOT-SENTINEL-keep-off-the-public-feed"",
          ""players"":[{
            ""entityId"":987654321,
            ""peerId"":""PEER-SENTINEL-203.0.113.9:7779"",
            ""connectedAtUnixMs"":1723100000111,
            ""accountName"":""ACCOUNT-SENTINEL-ravenmoor"",
            ""characterUid"":""CHARUID-SENTINEL-77c0ffee"",
            ""displayName"":""NAME-SENTINEL-Skysong"",
            ""health"":{""rttMs"":86420,""rttVarianceMs"":5,""packetsLost"":2,
                       ""packetsSent"":7654321,""inFlightBytes"":64,""spiral"":false},
            ""position"":{""x"":1234.5,""y"":-321.25,""z"":987.75}
          }],
          ""runtime"":{
            ""hostMode"":""local-single-process"",""hostId"":""local:primary"",
            ""shipDomains"":[{
              ""domainId"":""ship:918273645"",
              ""hullEntityId"":918273645,
              ""shipName"":""SHIPNAME-SENTINEL-Voidchaser"",
              ""ownerCharacterUid"":""OWNER-SENTINEL-deadbeef"",
              ""yawRadians"":1.25,""yawRateRadPerSec"":0.05,
              ""vxMps"":3.5,""vyMps"":0.25,""vzMps"":-2.75,
              ""hull"":{""present"":true,""docked"":false,
                ""ownerCharacterUid"":""HULLOWNER-SENTINEL-c0ffee"",
                ""beamMetres"":8.5,""keelMetres"":22.25,""deckPlaneMetres"":1.5,
                ""bowLocalZMetres"":11.5,""sternLocalZMetres"":-10.75,
                ""cellCount"":42,""hullDeckCount"":3,""sectionCount"":7,
                ""keelIsLongestAxis"":true,
                ""woodId"":""pine"",""woodQuality"":3,
                ""metalId"":""iron"",""metalQuality"":2,
                ""outline"":[0,11.5,4.25,3,4.25,-10.75,-4.25,-10.75,-4.25,3],
                ""geometryRevision"":1234567,
                ""geometry"":{""present"":true,
                  ""floorMetres"":0,""headMetres"":6.8,""heightMetres"":6.8,
                  ""sectionCount"":7,
                  ""shipwright"":""GEOMETRY-SENTINEL-unexpected"",
                  ""profile"":[-10.75,6.8,0,6.8,11.5,6.8,11.5,0,0,0,-10.75,0],
                  ""decks"":[{""deckNumber"":0,""floorMetres"":0,""planeMetres"":3.4,
                             ""sternZMetres"":-10.75,""bowZMetres"":11.5}],
                  ""parts"":[{""kind"":""helm"",""title"":""PARTTITLE-SENTINEL-Wheel of Ravenmoor"",
                              ""ownerCharacterUid"":""PARTOWNER-SENTINEL-deadbeef"",
                              ""x"":0,""y"":3.4,""z"":6.5}]}},
              ""authorityGeneration"":5,""replicationSequence"":2000,
              ""cadenceMs"":240,""deliveryAgeMs"":100,
              ""x"":17220.5,""y"":-310.75,""z"":-1084.25,
              ""headingDegrees"":137.5,
              ""active"":true,""piloted"":true,""liveCadenceExpected"":true,
              ""pilotPlayerEntityId"":987654321,
              ""aboardPlayerEntityIds"":[987654321],
              ""deckCount"":6,""mountedPartCount"":4,""subscriberCount"":1,
              ""staleDelivery"":false,""aboardCheckoutWarning"":false
            }]
          },
          ""fauna"":{
            ""enabled"":true,""clockSeconds"":86401.125,""liveCount"":460,
            ""budget"":4000,""demand"":460,""perPeerBudget"":24,""poseIntervalMs"":250,
            ""islands"":[
              {""islandId"":""release-a"",""mantaRays"":4,""jellyFish"":6,
               ""keeper"":""FAUNA-SENTINEL-unexpected""},
              {""islandId"":""release-b"",""mantaRays"":5,""jellyFish"":8}
            ],
            ""ecology"":{
              ""enabled"":true,""worldSeed"":424242,
              ""islands"":[
                {""islandId"":""release-a"",""quietFactor"":1.0,
                 ""mantaCapacity"":5,""jellyCapacity"":7,
                 ""mantaExpressed"":5,""jellyExpressed"":7,
                 ""mantaPhase"":""Bloom"",""mantaPhaseFraction"":0.25,
                 ""jellyPhase"":""Collapse"",""jellyPhaseFraction"":0.75,
                 ""warden"":""ECOLOGY-SENTINEL-unexpected"",
                 ""groups"":[{""species"":""manta"",""index"":0,""bloom"":0,
                   ""members"":5,""behaviour"":""Dive"",""epochSeconds"":120.5,
                   ""durationSeconds"":300,""toBloom"":1}],
                 ""blooms"":[{""species"":""manta"",""index"":0,""amplitude"":0.5,
                   ""sigma"":40.5,""annulusRadius"":445.25,""radialDrift"":18.5,
                   ""angularDrift"":0.3,""omegaRadial"":0.011,""omegaAngular"":0.007,
                   ""omegaMigration"":0.003,""phaseRadial"":2.1,""phaseAngular"":4.9,
                   ""baseAngle"":1.2}]}
              ]}
            }
        }";

        /// <summary>Runs the corpus through the REAL read path, not a shortcut.</summary>
        private static GameStatsResult ReadCorpus()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "wareborn-pubmap-test-" + Guid.NewGuid().ToString("n") + ".json");
            File.WriteAllText(path, CorpusJson);
            try
            {
                GameStatsResult result = GameStats.ReadFrom(path, Now);
                Assert.Equal(GameStatsState.Ok, result.State);
                return result;
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string ProjectCorpus() =>
            PublicMapProjection.Serialize(
                PublicMapProjection.Project(ReadCorpus(), SaltA));

        // ---- the leak test --------------------------------------------------

        [Fact]
        public void NoSeededSensitiveValueSurvivesTheProjection()
        {
            string json = ProjectCorpus();
            foreach (string sentinel in SensitiveSentinels)
            {
                Assert.DoesNotContain(sentinel, json, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void TheLeakTestItselfBites()
        {
            // Guard against the corpus rotting: serialize the ADMIN-side parse
            // of the same file and confirm the sentinels the file carries in
            // schema-known positions ARE visible there. A corpus that no
            // longer contains its own sentinels proves nothing.
            GameStatsResult result = ReadCorpus();
            GamePlayerStat player = Assert.Single(result.Snapshot!.Players);
            Assert.Contains("PEER-SENTINEL", player.PeerId, StringComparison.Ordinal);
            Assert.Equal(987654321, player.EntityId);
            Assert.Equal(86420u, player.RttMs);
            GameShipDomainStat ship = Assert.Single(result.Snapshot!.ShipDomains);
            Assert.Contains("918273645", (string?)ship.Json["domainId"]);
        }

        // ---- what the public feed DOES carry --------------------------------

        [Fact]
        public void PositionsAndLivenessSurvive()
        {
            JObject o = JObject.Parse(ProjectCorpus());
            Assert.True((bool?)o["reporting"]);
            Assert.Equal(1, (int?)o["currentOnline"]);

            JObject player = (JObject)((JArray)o["players"]!).Single();
            Assert.Equal(1234.5, (double?)player["x"]);
            Assert.Equal(-321.25, (double?)player["y"]);
            Assert.Equal(987.75, (double?)player["z"]);

            JObject ship = (JObject)((JArray)o["ships"]!).Single();
            Assert.Equal(17220.5, (double?)ship["x"]);
            Assert.Equal(-1084.25, (double?)ship["z"]);
            Assert.True((bool?)ship["active"]);
            Assert.Equal(6, (int?)ship["deckCount"]);
            // What is bolted to a ship is a fact about the ship, and the card
            // draws every one of them - a tile saying zero beside ten marks
            // would be the map contradicting itself.
            Assert.Equal(4, (int?)ship["mountedPartCount"]);
            // The renderer keys ships by hullEntityId, so it is present - but
            // it is the opaque token, not the entity id the server knows.
            Assert.Equal((string?)ship["id"], (string?)ship["hullEntityId"]);
            Assert.DoesNotContain("918273645", (string?)ship["hullEntityId"]!, StringComparison.Ordinal);

            // The silhouette survives: it is a shape in the world, not a name.
            JObject hull = (JObject)ship["hull"]!;
            Assert.True((bool?)hull["present"]);
            Assert.Equal(10, ((JArray)hull["outline"]!).Count);
            Assert.Equal(8.5, (double?)hull["beamMetres"]);
            Assert.Equal("pine", (string?)hull["woodId"]);
        }

        [Fact]
        public void ShipHeadingRidesThroughWhenTheSnapshotCarriesIt()
        {
            // headingDegrees is a whitelisted SLOT for the in-flight ships
            // work. The corpus seeds it; today's GameShipDomainStat does not
            // yet forward it, and this test accepts both worlds: absent is
            // fine, present must be the seeded number - never anything else.
            JObject ship = (JObject)((JArray)JObject.Parse(ProjectCorpus())["ships"]!).Single();
            JToken? heading = ship["headingDegrees"];
            if (heading != null && heading.Type != JTokenType.Null)
            {
                Assert.Equal(137.5, (double?)heading);
            }
        }

        [Fact]
        public void FaunaRosterAndClockSurviveButOperatorTuningDoesNot()
        {
            JObject fauna = (JObject)JObject.Parse(ProjectCorpus())["fauna"]!;
            Assert.True((bool?)fauna["present"]);
            Assert.True((bool?)fauna["enabled"]);
            Assert.Equal(86401.125, (double?)fauna["clockSeconds"]);
            Assert.Equal(460, (int?)fauna["liveCount"]);

            JArray islands = (JArray)fauna["islands"]!;
            Assert.Equal(2, islands.Count);
            Assert.Equal("release-a", (string?)islands[0]!["islandId"]);
            Assert.Equal(4, (int?)islands[0]!["mantaRays"]);
            Assert.Equal(6, (int?)islands[0]!["jellyFish"]);

            // Capacity tuning is the operator's business, not the public's.
            Assert.Null(fauna["budget"]);
            Assert.Null(fauna["demand"]);
            Assert.Null(fauna["perPeerBudget"]);
            Assert.Null(fauna["poseIntervalMs"]);
        }

        [Fact]
        public void EcologyGeometrySurvivesButTheSeedDoesNot()
        {
            // The v9 ecology is ADMITTED: bloom paths are world geometry (the
            // fauna equivalent of a coastline) and the counts carry no identity.
            // The worldSeed is an operator knob and stays admin-only - the
            // browser derives nothing from it, since the blooms arrive as
            // published numbers.
            JObject ecology = (JObject)JObject.Parse(ProjectCorpus())["fauna"]!["ecology"]!;
            Assert.True((bool?)ecology["enabled"]);
            Assert.Null(ecology["worldSeed"]);

            JObject island = (JObject)((JArray)ecology["islands"]!).Single();
            Assert.Equal("release-a", (string?)island["islandId"]);
            Assert.Equal(1.0, (double?)island["quietFactor"]);
            Assert.Equal(5, (int?)island["mantaCapacity"]);
            Assert.Equal(7, (int?)island["jellyExpressed"]);
            Assert.Equal("Collapse", (string?)island["jellyPhase"]);
            Assert.Equal(0.75, (double?)island["jellyPhaseFraction"]);
            Assert.Null(island["warden"]);

            JObject group = (JObject)((JArray)island["groups"]!).Single();
            Assert.Equal("manta", (string?)group["species"]);
            Assert.Equal(5, (int?)group["members"]);
            // The full (behaviour, epoch) descriptor: what the public page
            // animates a dive or migration from.
            Assert.Equal("Dive", (string?)group["behaviour"]);
            Assert.Equal(120.5, (double?)group["epochSeconds"]);
            Assert.Equal(300.0, (double?)group["durationSeconds"]);
            Assert.Equal(1, (int?)group["toBloom"]);

            JObject bloom = (JObject)((JArray)island["blooms"]!).Single();
            Assert.Equal(40.5, (double?)bloom["sigma"]);
            Assert.Equal(445.25, (double?)bloom["annulusRadius"]);
            Assert.Equal(0.011, (double?)bloom["omegaRadial"]);
        }

        // ---- the whitelist is exact -----------------------------------------

        [Fact]
        public void PublicMarkerShapesAreExactlyTheWhitelist()
        {
            // The tripwire for careless widening: a NEW key on a public marker
            // fails here by name and must be added deliberately - here AND to
            // the leak corpus above.
            JObject o = JObject.Parse(ProjectCorpus());

            JObject player = (JObject)((JArray)o["players"]!).Single();
            Assert.Equal(new[] { "id", "hasPosition", "x", "y", "z" },
                player.Properties().Select(p => p.Name).ToArray());

            JObject ship = (JObject)((JArray)o["ships"]!).Single();
            string[] shipKeys = ship.Properties().Select(p => p.Name).ToArray();
            Assert.Equal(
                new[] { "hullEntityId", "id", "x", "y", "z", "active", "deckCount",
                        "mountedPartCount", "yawRadians", "yawRateRadPerSec",
                        "vxMps", "vyMps", "vzMps", "hull" },
                shipKeys.Where(k => k != "headingDegrees").ToArray());

            // The hull block is the silhouette the public map draws. Every key
            // here describes the SHIP; the moment one describes a person, this
            // list is what makes it a deliberate act.
            Assert.Equal(
                new[] { "present", "docked", "beamMetres", "keelMetres", "deckPlaneMetres",
                        "bowLocalZMetres", "sternLocalZMetres", "cellCount", "hullDeckCount",
                        "sectionCount", "keelIsLongestAxis", "woodId", "woodQuality",
                        "metalId", "metalQuality", "outline", "geometryRevision" },
                ((JObject)ship["hull"]!).Properties().Select(p => p.Name).ToArray());

            Assert.Equal(
                new[] { "reporting", "state", "ageSeconds", "stale", "currentOnline",
                        "fauna", "players", "ships", "shipModel" },
                o.Properties().Select(p => p.Name).ToArray());

            // The dead-reckoning model: physics constants only. The map cannot
            // draw a moving hull without them and none of them is anybody's.
            Assert.Equal(
                new[] { "present", "accelMps2", "maxSpeedMps", "windowSeconds",
                        "maxWindowSeconds", "toleratedErrorMetres" },
                ((JObject)o["shipModel"]!).Properties().Select(p => p.Name).ToArray());
        }

        // ---- anonymous ids --------------------------------------------------

        [Fact]
        public void AnonymousIdsAreStableWithinASaltAndUnlinkableAcrossSalts()
        {
            string a1 = PublicMapProjection.AnonymousId("player", 987654321, SaltA);
            string a2 = PublicMapProjection.AnonymousId("player", 987654321, SaltA);
            string b = PublicMapProjection.AnonymousId("player", 987654321, SaltB);

            Assert.Equal(a1, a2);          // a marker glides between polls
            Assert.NotEqual(a1, b);        // and cannot be correlated across restarts
            Assert.DoesNotContain("987654321", a1, StringComparison.Ordinal);
            Assert.Equal(12, a1.Length);
        }

        [Fact]
        public void AnonymousIdsSeparateKinds()
        {
            // A player and a ship sharing a numeric id must not share a token,
            // or the two anonymized streams could re-identify each other.
            Assert.NotEqual(
                PublicMapProjection.AnonymousId("player", 42, SaltA),
                PublicMapProjection.AnonymousId("ship", 42, SaltA));
        }

        // ---- degraded states ------------------------------------------------

        [Fact]
        public void MissingStatsFileProjectsToANonReportingPayload()
        {
            JObject o = PublicMapProjection.Project(GameStatsResult.Missing(), SaltA);
            Assert.False((bool?)o["reporting"]);
            Assert.Equal("missing", (string?)o["state"]);
            Assert.Empty((JArray)o["players"]!);
            Assert.Empty((JArray)o["ships"]!);
        }

        // ---- the per-hull geometry endpoint ---------------------------------

        /// <summary>The public geometry answer for the corpus's one ship.</summary>
        private static JObject PublicGeometry()
        {
            GameStatsResult result = ReadCorpus();
            string token = PublicMapProjection.AnonymousId(
                "ship", (string?)Assert.Single(result.Snapshot!.ShipDomains).Json["domainId"] ?? "", SaltA);
            return ShipGeometryEndpoint.ForPublic(result, token, SaltA);
        }

        [Fact]
        public void TheGeometryEndpointLeaksNothingTheLiveFeedWouldNot()
        {
            // The endpoint is a SECOND doorway out of the snapshot, so it gets
            // the same corpus treatment the live feed does. If it ever forwards
            // the parsed object instead of rebuilding it, these fail by name.
            string json = PublicMapProjection.Serialize(PublicGeometry());
            foreach (string sentinel in SensitiveSentinels)
            {
                Assert.DoesNotContain(sentinel, json, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void TheGeometryLeakTestItselfBites()
        {
            // The corpus really does carry the geometry sentinels in the
            // positions the endpoint reads, and the OPERATOR answer shows them -
            // otherwise the test above would pass on an empty payload.
            GameStatsResult result = ReadCorpus();
            string operatorJson = ShipGeometryEndpoint
                .ForOperator(result, "918273645").ToString();
            Assert.Contains("PARTTITLE-SENTINEL", operatorJson, StringComparison.Ordinal);
            // ...but even the operator answer is a rebuild, so an unexpected
            // field inside the block never reaches either page.
            Assert.DoesNotContain("GEOMETRY-SENTINEL", operatorJson, StringComparison.Ordinal);
            Assert.DoesNotContain("PARTOWNER-SENTINEL", operatorJson, StringComparison.Ordinal);
        }

        [Fact]
        public void TheGeometryShapeIsExactlyTheWhitelist()
        {
            JObject answer = PublicGeometry();
            Assert.Equal(new[] { "ok", "id", "revision", "geometry" },
                answer.Properties().Select(p => p.Name).ToArray());
            Assert.True((bool?)answer["ok"]);
            Assert.Equal(1234567L, (long?)answer["revision"]);

            JObject geometry = (JObject)answer["geometry"]!;
            Assert.Equal(
                new[] { "present", "floorMetres", "headMetres", "heightMetres",
                        "sectionCount", "profile", "decks", "parts" },
                geometry.Properties().Select(p => p.Name).ToArray());

            JObject deck = (JObject)((JArray)geometry["decks"]!).Single();
            Assert.Equal(
                new[] { "deckNumber", "floorMetres", "planeMetres", "sternZMetres", "bowZMetres" },
                deck.Properties().Select(p => p.Name).ToArray());

            // A part on the public feed is a KIND at a PLACE. The title the
            // operator sees is dropped rather than clamped: the public card
            // labels a mark from its own vocabulary, so no string that
            // originated outside this projection has to be trusted.
            JObject part = (JObject)((JArray)geometry["parts"]!).Single();
            Assert.Equal(new[] { "kind", "x", "y", "z" },
                part.Properties().Select(p => p.Name).ToArray());
            Assert.Equal("helm", (string?)part["kind"]);
            Assert.Equal(6.5, (double?)part["z"]);
        }

        [Fact]
        public void TheGeometryEndpointRefusesByNameRatherThanBlankly()
        {
            // Three different nothings, and the card prints which: a blank
            // panel would say none of them.
            GameStatsResult result = ReadCorpus();
            Assert.Equal("no-ship-selected",
                (string?)ShipGeometryEndpoint.ForPublic(result, null, SaltA)["reason"]);
            Assert.Equal("unknown-ship",
                (string?)ShipGeometryEndpoint.ForPublic(result, "deadbeefcafe", SaltA)["reason"]);
            Assert.Equal("not-reporting",
                (string?)ShipGeometryEndpoint.ForPublic(
                    GameStatsResult.Missing(), "deadbeefcafe", SaltA)["reason"]);
            Assert.Equal("unknown-ship",
                (string?)ShipGeometryEndpoint.ForOperator(result, "1")["reason"]);
        }

        [Fact]
        public void TheGeometryTokenIsTheSameOneTheLiveFeedPublishes()
        {
            // The public card asks with the token it was drawn with; if the two
            // ever derived differently, clicking a ship would answer
            // "unknown-ship" forever.
            JObject ship = (JObject)((JArray)JObject.Parse(ProjectCorpus())["ships"]!).Single();
            GameStatsResult result = ReadCorpus();
            JObject answer = ShipGeometryEndpoint.ForPublic(
                result, (string?)ship["hullEntityId"], SaltA);
            Assert.True((bool?)answer["ok"]);
            Assert.Equal((long?)ship["hull"]!["geometryRevision"], (long?)answer["revision"]);
        }

        [Fact]
        public void AQuerySelectorIsReadOrRefusedButNeverDecoded()
        {
            Assert.Equal("918273645",
                ShipGeometryEndpoint.Selector("/admin/api/ship-geometry?hull=918273645", "hull"));
            Assert.Equal("a1b2c3d4e5f6",
                ShipGeometryEndpoint.Selector("/map/ship?other=1&id=a1b2c3d4e5f6", "id"));
            Assert.Null(ShipGeometryEndpoint.Selector("/map/ship", "id"));
            Assert.Null(ShipGeometryEndpoint.Selector("/map/ship?id=", "id"));
            Assert.Null(ShipGeometryEndpoint.Selector("/map/ship?hull=1", "id"));
            // Anything a selector has no business containing is refused rather
            // than unescaped: there is no legitimate id that needs a percent.
            Assert.Null(ShipGeometryEndpoint.Selector("/map/ship?id=%2e%2e%2f", "id"));
            Assert.Null(ShipGeometryEndpoint.Selector("/map/ship?id=" + new string('x', 65), "id"));
        }

        [Fact]
        public void TheShipGeometryRouteIsClaimedByThePublicMapNamespace()
        {
            Assert.Equal(PublicMapRoute.ShipGeometry,
                PublicMapRoutes.Match("GET", "/map/ship?id=a1b2c3d4e5f6"));
            Assert.Equal(PublicMapRoute.ShipGeometry, PublicMapRoutes.Match("HEAD", "/map/ship"));
            // Still no write surface anywhere under /map.
            Assert.Equal(PublicMapRoute.NotFound, PublicMapRoutes.Match("POST", "/map/ship"));
        }

        [Fact]
        public void SerializationHtmlEscapesLikeTheAdminPayload()
        {
            // Same EscapeHtml discipline as the admin bootstrap, so the same
            // string is safe inlined into a <script> block in Phase B.
            string json = PublicMapProjection.Serialize(
                new JObject { ["state"] = "</script><script>alert(1)" });
            Assert.DoesNotContain("</script>", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
