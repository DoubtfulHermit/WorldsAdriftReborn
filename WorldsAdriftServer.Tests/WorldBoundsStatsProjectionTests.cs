using System;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftServer.Admin;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    public sealed class WorldBoundsStatsProjectionTests
    {
        [Fact]
        public void Policy_default_matches_the_preserved_release_map_extent()
        {
            JObject releaseMap = JObject.Parse(ReleaseWorldMap.Json);

            Assert.Equal(RetailWorldBoundsPolicy.ReleaseWorldEdgeLengthMetres,
                (double)releaseMap["worldEdgeLength"]!);
        }

        [Fact]
        public void Older_ship_domain_projects_world_bounds_as_explicitly_absent()
        {
            GameShipDomainStat ship = GameShipDomainStat.Parse(new JObject());
            JObject bounds = (JObject)ship.Json["worldBounds"]!;

            Assert.False((bool)bounds["present"]!);
            Assert.False((bool)bounds["enabled"]!);
            Assert.Equal(0, (double)bounds["edgeLengthMetres"]!);
            Assert.Equal(0, (int)bounds["referenceSubsteps"]!);
            JObject clock = (JObject)ship.Json["fixedClock"]!;
            Assert.False((bool)clock["present"]!);
            Assert.False((bool)clock["enabled"]!);
        }

        [Fact]
        public void Fixed_clock_projection_is_allowlisted_and_bounded()
        {
            GameShipDomainStat ship = GameShipDomainStat.Parse(JObject.Parse(@"{
              ""fixedClock"": {
                ""present"": true, ""enabled"": true, ""stepMs"": 20,
                ""catchUpCap"": 25, ""completedSteps"": 1234,
                ""droppedSteps"": -5, ""pressureEvents"": 2,
                ""remainderSeconds"": 0.019, ""fictionalControl"": true
              }
            }"));
            JObject clock = (JObject)ship.Json["fixedClock"]!;

            Assert.True((bool)clock["present"]!);
            Assert.True((bool)clock["enabled"]!);
            Assert.Equal(20, (int)clock["stepMs"]!);
            Assert.Equal(25, (int)clock["catchUpCap"]!);
            Assert.Equal(1234, (long)clock["completedSteps"]!);
            Assert.Equal(0, (long)clock["droppedSteps"]!);
            Assert.Equal(0.019, (double)clock["remainderSeconds"]!, 6);
            Assert.Null(clock["fictionalControl"]);
        }

        [Fact]
        public void World_bounds_projection_is_allowlisted_and_finite_bounded()
        {
            GameShipDomainStat ship = GameShipDomainStat.Parse(JObject.Parse(@"{
              ""worldBounds"": {
                ""present"": true, ""enabled"": true,
                ""edgeLengthMetres"": 36000,
                ""horizontalPushbackThresholdMetres"": 17600,
                ""horizontalHardLimitMetres"": 17700,
                ""verticalPushbackMetres"": 800,
                ""verticalHardLimitMetres"": 1000,
                ""boundaryDistanceMetres"": -25.5,
                ""pushbackDeltaVxMps"": -3.5,
                ""pushbackDeltaVyMps"": 999999999,
                ""pushbackDeltaVzMps"": 1.25,
                ""hardClamped"": true, ""invalidState"": false,
                ""referenceSubsteps"": 12,
                ""fictionalTeleport"": true
              }
            }"));
            JObject bounds = (JObject)ship.Json["worldBounds"]!;

            Assert.True((bool)bounds["present"]!);
            Assert.True((bool)bounds["enabled"]!);
            Assert.Equal(36_000, (double)bounds["edgeLengthMetres"]!);
            Assert.Equal(-25.5, (double)bounds["boundaryDistanceMetres"]!);
            Assert.Equal(-3.5, (double)bounds["pushbackDeltaVxMps"]!);
            Assert.Equal(10_000, (double)bounds["pushbackDeltaVyMps"]!);
            Assert.Equal(12, (int)bounds["referenceSubsteps"]!);
            Assert.Null(bounds["fictionalTeleport"]);
        }

        [Fact]
        public void Schema_v18_bounds_and_fixed_clock_cross_writer_and_admin_projection_intact()
        {
            var bounds = new ShipWorldBoundsStat(true, 36_000, 17_600, 17_700, 800, 1_000,
                new RetailWorldBoundsTelemetry(true, 73.25, -4, -5, 6, true, true, 12));
            var domain = new ShipDomainStat(
                "ship:9", 9, 1, 1, 240, 1, 0, 0, 0,
                active: true, piloted: false, liveCadenceExpected: true,
                pilotPlayerEntityId: null, aboardPlayerEntityIds: Array.Empty<long>(),
                deckCount: 0, mountedPartCount: 0, subscriberCount: 0,
                worldBounds: bounds,
                fixedClock: new FixedFlightClockStat(true, 20, 25, 100, 3, 2, 0.01));
            string json = new StatsSnapshot(
                0, 0, 0, "raw", 0, "test", 0, 0, 0, 0,
                Array.Empty<PlayerStat>(), shipDomains: new[] { domain },
                worldBounds: new WorldBoundsRuntimeStat(
                    true, 36_000, 17_600, 17_700, 800, 1_000, 0.02)).ToJson();

            GameStatsSnapshot parsed = GameStatsSnapshot.Parse(JObject.Parse(json));
            JObject projected = (JObject)Assert.Single(parsed.ShipDomains).Json["worldBounds"]!;
            JObject fixedClock = (JObject)Assert.Single(parsed.ShipDomains).Json["fixedClock"]!;

            Assert.Equal(19, parsed.SchemaVersion);
            Assert.True(parsed.WorldBounds.Present);
            Assert.True((bool)parsed.WorldBounds.Json["enabled"]!);
            Assert.Equal(0.02, (double)parsed.WorldBounds.Json["referenceStepSeconds"]!);
            Assert.True((bool)projected["enabled"]!);
            Assert.Equal(73.25, (double)projected["boundaryDistanceMetres"]!);
            Assert.Equal(-4, (double)projected["pushbackDeltaVxMps"]!);
            Assert.True((bool)projected["hardClamped"]!);
            Assert.True((bool)projected["invalidState"]!);
            Assert.Equal(12, (int)projected["referenceSubsteps"]!);
            Assert.True((bool)fixedClock["enabled"]!);
            Assert.Equal(100, (long)fixedClock["completedSteps"]!);
            Assert.Equal(3, (long)fixedClock["droppedSteps"]!);
        }
    }
}
