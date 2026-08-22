using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Domains
{
    public sealed class LocalDomainHostTests
    {
        private static IslandDomain Island(string island, string region) =>
            new(new IslandId(island), new RegionId(region));

        [Fact]
        public void Island_domain_identity_and_members_are_stable()
        {
            IslandDomain domain = Island("haven", "haven-region");
            var host = new LocalDomainHost();
            host.Register(domain);
            host.Assign(12, domain.Id);
            host.Assign(10, domain.Id);

            Assert.Equal(new SimulationDomainId("island:haven"), domain.Id);
            Assert.Equal(SimulationDomainKind.Island, domain.Kind);
            Assert.Equal(new long[] { 10, 12 }, domain.EntityIds);
        }

        [Fact]
        public void Host_enforces_unique_ownership_and_explicit_globals()
        {
            var host = new LocalDomainHost();
            IslandDomain haven = Island("haven", "haven-region");
            IslandDomain trades = Island("trades", "trades-region");
            host.Register(haven);
            host.Register(trades);
            host.Assign(10, haven.Id);
            host.MarkGlobal(99);

            Assert.Throws<InvalidOperationException>(() => host.Assign(10, trades.Id));
            Assert.Throws<InvalidOperationException>(() => host.Assign(99, haven.Id));
            Assert.Equal(haven.Id, host.OwnerOf(10));
            Assert.True(host.IsGlobal(99));
        }

        [Fact]
        public void Move_is_atomic_and_requires_the_expected_source()
        {
            var host = new LocalDomainHost();
            IslandDomain haven = Island("haven", "haven-region");
            IslandDomain trades = Island("trades", "trades-region");
            host.Register(haven);
            host.Register(trades);
            host.Assign(10, haven.Id);

            Assert.Throws<InvalidOperationException>(() => host.Move(10, trades.Id, haven.Id));
            Assert.Equal(haven.Id, host.OwnerOf(10));
            host.Move(10, haven.Id, trades.Id);
            Assert.Equal(trades.Id, host.OwnerOf(10));
            Assert.DoesNotContain(10, haven.EntityIds);
            Assert.Contains(10, trades.EntityIds);
        }

        [Fact]
        public void Synchronize_validates_before_replacing_live_membership()
        {
            var host = new LocalDomainHost();
            IslandDomain haven = Island("haven", "haven-region");
            IslandDomain trades = Island("trades", "trades-region");
            host.Register(haven);
            host.Register(trades);
            host.Assign(10, haven.Id);
            host.Assign(20, trades.Id);

            Assert.Throws<InvalidOperationException>(() => host.Assign(20, haven.Id));
            host.Synchronize(haven);
            Assert.Equal(haven.Id, host.OwnerOf(10));
            Assert.Equal(trades.Id, host.OwnerOf(20));
        }

        [Fact]
        public void Enumeration_and_completeness_report_are_deterministic()
        {
            var host = new LocalDomainHost();
            IslandDomain z = Island("z", "z-region");
            IslandDomain a = Island("a", "a-region");
            host.Register(z);
            host.Register(a);
            host.Assign(1, a.Id);
            host.MarkGlobal(2);

            Assert.Equal(new[] { a.Id, z.Id }, host.Domains.Select(x => x.Id));
            DomainOwnershipSummary complete = host.EnsureComplete(new long[] { 2, 1 });
            Assert.Empty(complete.UnownedEntityIds);
            Assert.Throws<InvalidOperationException>(() => host.EnsureComplete(new long[] { 1, 2, 3 }));
        }

        [Fact]
        public void Loose_part_mount_detach_and_remount_never_diverges()
        {
            var host = new LocalDomainHost();
            IslandDomain island = Island("haven", "haven-region");
            var ship = new ShipDomain(70, 0,
                new FlightSession(FlightState.AtRestAt(0, 0, 0)));
            host.Register(island);
            host.Register(ship);
            host.Assign(80, island.Id);

            host.Move(80, island.Id, ship.Id);
            Assert.Equal(ship.Id, host.OwnerOf(80));
            Assert.Contains(80, ship.EntityIds);
            Assert.DoesNotContain(80, island.EntityIds);

            host.Move(80, ship.Id, island.Id);
            Assert.Equal(island.Id, host.OwnerOf(80));
            Assert.DoesNotContain(80, ship.EntityIds);
            Assert.Contains(80, island.EntityIds);

            host.Move(80, island.Id, ship.Id);
            host.Synchronize(ship);
            Assert.Equal(ship.Id, host.OwnerOf(80));
            Assert.Contains(80, ship.EntityIds);
        }

        [Fact]
        public void Feature_gated_static_ship_is_ownership_only_not_a_live_ship_domain()
        {
            var liveShips = new ShipDomainRegistry();
            var host = new LocalDomainHost();
            var staticShip = new StaticShipDomain(70, new long[] { 71, 72, 73 });

            host.Register(staticShip);

            Assert.Null(liveShips.ByHull(70));
            Assert.IsType<StaticShipDomain>(host.ById(SimulationDomainId.ForShip(70)));
            Assert.Equal(new long[] { 70, 71, 72, 73 }, staticShip.EntityIds);
            Assert.All(staticShip.EntityIds,
                entityId => Assert.Equal(staticShip.Id, host.OwnerOf(entityId)));
            host.Synchronize(staticShip);
            Assert.Equal(new long[] { 70, 71, 72, 73 }, staticShip.EntityIds);
        }

        [Fact]
        public void Completeness_audit_detects_domain_mutation_outside_the_host()
        {
            var host = new LocalDomainHost();
            var ship = new ShipDomain(70, 0,
                new FlightSession(FlightState.AtRestAt(0, 0, 0)));
            host.Register(ship);

            ship.ReplaceMembers(new long[] { 71 }, Array.Empty<long>());

            DomainOwnershipSummary report = host.Inspect(new long[] { 70, 71 });
            Assert.Contains(71, report.UnownedEntityIds);
            Assert.Contains(report.Inconsistencies, message => message.Contains("domain ship:70 contains 71"));
            Assert.Throws<InvalidOperationException>(() => host.EnsureComplete(new long[] { 70, 71 }));
        }

        [Fact]
        public void Synchronize_repairs_reverse_membership_after_live_domain_change()
        {
            var host = new LocalDomainHost();
            var ship = new ShipDomain(70, 0,
                new FlightSession(FlightState.AtRestAt(0, 0, 0)));
            ship.ReplaceMembers(new long[] { 71 }, new long[] { 72 });
            host.Register(ship);

            ship.ReplaceMembers(new long[] { 73 }, new long[] { 74 });
            host.Synchronize(ship);

            Assert.Null(host.OwnerOf(71));
            Assert.Null(host.OwnerOf(72));
            Assert.Equal(ship.Id, host.OwnerOf(73));
            Assert.Equal(ship.Id, host.OwnerOf(74));
            DomainOwnershipSummary report = host.EnsureComplete(new long[] { 70, 73, 74 });
            Assert.Empty(report.Inconsistencies);
        }

        [Fact]
        public void Remove_domain_clears_only_its_reverse_index_members()
        {
            var host = new LocalDomainHost();
            IslandDomain haven = Island("haven", "haven-region");
            IslandDomain trades = Island("trades", "trades-region");
            host.Register(haven);
            host.Register(trades);
            for (long id = 1; id <= 100; id++)
                host.Assign(id, id <= 50 ? haven.Id : trades.Id);

            Assert.True(host.RemoveDomain(haven.Id));

            for (long id = 1; id <= 50; id++) Assert.Null(host.OwnerOf(id));
            for (long id = 51; id <= 100; id++) Assert.Equal(trades.Id, host.OwnerOf(id));
            Assert.False(host.RemoveDomain(haven.Id));
        }
    }
}
