using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// The four first edges, each from a source this server can actually observe.
    /// This is where the adapter's rules live, so this is where they are pinned.
    /// </summary>
    public class WarebornSimulationProjectionTests
    {
        private static readonly SimulationDomainId Haven =
            SimulationDomainId.ForIsland(new IslandId("haven"));
        private static readonly SimulationDomainId Ship = SimulationDomainId.ForShip(893);

        private static WarebornWorldObservation World(
            ObservedIsland[]? islands = null,
            ObservedShip[]? ships = null,
            ObservedPlayer[]? players = null) =>
            new WarebornWorldObservation(islands, ships, players);

        private static ObservedIsland HavenIsland(params long[] owned) =>
            new ObservedIsland("haven", owned);

        [Fact]
        public void Haven_becomes_a_stable_domain_with_the_same_id_the_ownership_host_uses()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection
                .Project(World(islands: new[] { HavenIsland(1, 2, 3) })).Snapshot();

            DomainSnapshot domain = Assert.Single(snapshot.Domains);
            Assert.Equal("island:haven", domain.Id.Value);
            Assert.Equal(Haven, domain.Id);
            Assert.Equal("island", domain.Kind);
            // Three owned entities plus the island's own aggregate stand-in.
            Assert.Equal(4, domain.MemberCount);
            Assert.Contains(domain.Members, m => m.Value == "island:haven");
        }

        [Fact]
        public void The_same_island_observed_twice_does_not_become_two_domains()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection
                .Project(World(islands: new[] { HavenIsland(1), HavenIsland(1) })).Snapshot();
            Assert.Single(snapshot.Domains);
        }

        [Fact]
        public void A_live_ship_becomes_a_second_domain()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1) },
                ships: new[] { new ObservedShip(893, new long[] { 900, 901 }, null, null, false, null, 0) }))
                .Snapshot();

            Assert.Equal(2, snapshot.DomainCount);
            DomainSnapshot ship = snapshot.Domains.Single(d => d.Id == Ship);
            Assert.Equal("ship", ship.Kind);
            Assert.Equal(3, ship.MemberCount);
            Assert.Contains(ship.Members, m => m.Value == "ship:893");
            Assert.Equal("live hull, at rest", ship.Descriptor);
        }

        [Fact]
        public void A_player_aboard_a_ship_produces_a_containment_edge()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                ships: new[] { new ObservedShip(893, null, new long[] { 7 }, null, true, null, 0) },
                players: new[] { new ObservedPlayer(7, 893, null) })).Snapshot();

            InteractionSnapshot edge = Assert.Single(snapshot.Interactions);
            Assert.Equal(InteractionKind.Containment, edge.Kind);
            Assert.Equal("player:7", edge.A.Value);
            Assert.Equal("ship:893", edge.B.Value);
            Assert.Equal(InteractionStrength.VeryStrong, edge.Strength);
            Assert.Equal(InteractionActivity.Active, edge.Activity);
            Assert.True(edge.IsCrossDomain);
        }

        [Fact]
        public void A_parked_ship_carries_its_crew_intermittently_not_actively()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                ships: new[] { new ObservedShip(893, null, new long[] { 7 }, null, false, null, 0) },
                players: new[] { new ObservedPlayer(7, 893, null) })).Snapshot();

            Assert.Equal(InteractionActivity.Intermittent, snapshot.Interactions.Single().Activity);
        }

        [Fact]
        public void A_player_at_the_helm_produces_a_control_edge_beside_the_containment_one()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                ships: new[] { new ObservedShip(893, null, new long[] { 7 }, 7, true, null, 0) },
                players: new[] { new ObservedPlayer(7, 893, null) })).Snapshot();

            Assert.Equal(2, snapshot.InteractionCount);
            InteractionSnapshot control = snapshot.Interactions
                .Single(i => i.Kind == InteractionKind.Control);
            Assert.Equal(InteractionLatencySensitivity.VeryHigh, control.LatencySensitivity);
            Assert.Equal(1.0, control.Pressure);
        }

        [Fact]
        public void A_player_with_resource_checkout_produces_one_aggregate_interest_edge_per_island()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1, 2, 3, 4, 5) },
                players: new[] { new ObservedPlayer(7, null, new[] { "haven" }) })).Snapshot();

            InteractionSnapshot edge = Assert.Single(snapshot.Interactions);
            Assert.Equal(InteractionKind.Interest, edge.Kind);
            // Endpoints are normalised by ordinal, so the island sorts first here.
            Assert.Equal("island:haven", edge.A.Value);
            Assert.Equal("player:7", edge.B.Value);
            // Aggregate at the DOMAIN, never one edge per resource node.
            Assert.Equal(InteractionStrength.Weak, edge.Strength);
            Assert.Equal(Haven, edge.DomainA);
            Assert.Null(edge.DomainB);
        }

        [Fact]
        public void Interest_in_an_island_that_is_not_a_hosted_domain_is_dropped()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1) },
                players: new[] { new ObservedPlayer(7, null, new[] { "not-hosted" }) })).Snapshot();

            Assert.Equal(0, snapshot.InteractionCount);
        }

        [Theory]
        [InlineData(10.0, InteractionStrength.Strong)]
        [InlineData(150.0, InteractionStrength.Strong)]
        [InlineData(151.0, InteractionStrength.Moderate)]
        [InlineData(400.0, InteractionStrength.Moderate)]
        [InlineData(999.0, InteractionStrength.Weak)]
        public void Ship_island_proximity_lands_in_the_expected_band(
            double metres, InteractionStrength expected)
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1) },
                ships: new[] { new ObservedShip(893, null, null, null, true, "haven", metres) }))
                .Snapshot();

            InteractionSnapshot edge = Assert.Single(snapshot.Interactions);
            Assert.Equal(InteractionKind.Proximity, edge.Kind);
            Assert.Equal(expected, edge.Strength);
        }

        [Theory]
        [InlineData(1001.0)]
        [InlineData(double.NaN)]
        [InlineData(-1.0)]
        public void A_ship_far_from_or_nowhere_near_an_island_gets_no_proximity_edge(double metres)
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1) },
                ships: new[] { new ObservedShip(893, null, null, null, true, "haven", metres) }))
                .Snapshot();

            Assert.Equal(0, snapshot.InteractionCount);
        }

        [Fact]
        public void A_parked_ship_next_to_an_island_is_coupled_but_idle()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1) },
                ships: new[] { new ObservedShip(893, null, null, null, false, "haven", 10) }))
                .Snapshot();

            InteractionSnapshot edge = Assert.Single(snapshot.Interactions);
            Assert.Equal(InteractionActivity.Idle, edge.Activity);
            Assert.Equal(0.0, edge.Pressure);
            // Visible in the graph, contributing nothing to pressure.
            Assert.Equal(0, snapshot.ActiveInteractionCount);
            Assert.Equal(0, snapshot.TotalCrossDomainPressure);
        }

        [Fact]
        public void A_peer_aboard_a_hull_who_left_the_player_registry_invents_no_edge()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                ships: new[] { new ObservedShip(893, null, new long[] { 7 }, 7, true, null, 0) },
                players: null)).Snapshot();

            Assert.Equal(0, snapshot.InteractionCount);
        }

        [Fact]
        public void No_environment_edge_is_ever_produced_yet()
        {
            // The wind-wall seam is declared in the enum and unfilled by this
            // adapter. If a future change starts emitting one, this goes red and the
            // author has to come and say so on purpose.
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1, 2) },
                ships: new[] { new ObservedShip(893, new long[] { 900 }, new long[] { 7 }, 7, true, "haven", 20) },
                players: new[] { new ObservedPlayer(7, 893, new[] { "haven" }) })).Snapshot();

            Assert.DoesNotContain(snapshot.Interactions, i => i.Kind == InteractionKind.Environment);
        }

        [Fact]
        public void The_full_scene_reproduces_the_section_11_shape()
        {
            WorldSnapshot snapshot = WarebornSimulationProjection.Project(World(
                islands: new[] { HavenIsland(1, 2, 3) },
                ships: new[] { new ObservedShip(893, new long[] { 900 }, new long[] { 7, 8 }, 7, true, "haven", 60) },
                players: new[]
                {
                    new ObservedPlayer(7, 893, new[] { "haven" }),
                    new ObservedPlayer(8, 893, null),
                })).Snapshot();

            Assert.Equal(2, snapshot.DomainCount);
            // 4 island members + 2 ship members + 2 players.
            Assert.Equal(8, snapshot.EntityCount);
            // 2 containment + 1 control + 1 interest + 1 proximity.
            Assert.Equal(5, snapshot.InteractionCount);

            string[] lines = SimulationDiagnostics.Format(snapshot).ToArray();
            Assert.Equal("[sim] domains=2 entities=8 interactions=5", lines[0]);
            Assert.Contains(lines, l => l.StartsWith("[sim] domain island:haven"));
            Assert.Contains(lines, l => l.StartsWith("[sim] domain ship:893"));
            Assert.Contains(lines, l => l.Contains("kind=island") && l.Contains("members=4"));
            Assert.Contains(lines, l => l.Contains("edge") && l.Contains("kind=Control"));
        }

        [Fact]
        public void Projection_is_order_independent()
        {
            ObservedIsland[] islands = { HavenIsland(3, 1, 2), new ObservedIsland("trades-challenge", new long[] { 9 }) };
            ObservedShip[] ships =
            {
                new ObservedShip(894, null, null, null, false, "haven", 50),
                new ObservedShip(893, null, new long[] { 7 }, 7, true, "haven", 50),
            };
            ObservedPlayer[] players = { new ObservedPlayer(8, null, new[] { "haven" }), new ObservedPlayer(7, 893, null) };

            string Describe(WarebornWorldObservation observation) =>
                string.Join("|", WarebornSimulationProjection.Project(observation).Snapshot()
                    .Interactions.Select(i => i.A.Value + ">" + i.B.Value + ">" + i.Kind + ">" + i.Pressure));

            Assert.Equal(
                Describe(new WarebornWorldObservation(islands, ships, players)),
                Describe(new WarebornWorldObservation(
                    islands.Reverse().ToArray(), ships.Reverse().ToArray(), players.Reverse().ToArray())));
        }
    }
}
