using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// The set of places the shrine may send someone, and the evidence behind each
    /// one. Everything here is about NOT dropping a player into the void, so the
    /// assertions are about provenance and registration rather than about numbers
    /// being pretty.
    /// </summary>
    public sealed class WildernessCatalogTests
    {
        private static IslandDefinition Island(string asset) =>
            ReleaseWorldCatalog.All.Single(record => record.Definition.Id.Value.EndsWith(asset,
                StringComparison.Ordinal)).Definition;

        private static IReadOnlyList<IslandDefinition> TierOneDefinitions() =>
            ReleaseWorldRolloutPolicy.Select("tier1").Select(record => record.Definition).ToArray();

        [Fact]
        public void The_wilderness_is_the_46_tier_one_islands()
        {
            Assert.Equal(46, WildernessCatalog.All.Count);
            Assert.All(WildernessCatalog.All, record => Assert.Equal(1, record.CellTier));
            Assert.Equal(new[] { "A2", "A3", "B2", "B3" },
                WildernessCatalog.All.Select(record => record.CellId).Distinct()
                    .OrderBy(id => id, StringComparer.Ordinal));
        }

        /// <summary>
        /// The safety claim, stated once for the whole world: every island the
        /// shrine can pick has a MEASURED landing sample with measured neighbours.
        /// The server has no terrain query, so this is the strongest thing it can
        /// say about solid ground, and it has to be true of all 46 rather than of
        /// the four somebody happened to look at.
        /// </summary>
        [Fact]
        public void Every_wilderness_island_has_an_evidenced_landing_point()
        {
            Assert.All(WildernessCatalog.All, record =>
            {
                IslandLandingPoint pad = record.Landing;
                Assert.True(pad.UpwardNormal >= 0.98,
                    record.Definition.DisplayName + " landing normal " + pad.UpwardNormal);
                Assert.True(pad.SupportingColumns >= 6,
                    record.Definition.DisplayName + " support " + pad.SupportingColumns);
                Assert.True(pad.WorstStepMetres <= 2.5,
                    record.Definition.DisplayName + " step " + pad.WorstStepMetres);
            });
        }

        /// <summary>
        /// A landing point is a point ON the island, not near it. Checked against
        /// the island's own extracted collision envelope, which is a different
        /// source from the surface table the point came from, so agreeing is
        /// evidence rather than tautology.
        /// </summary>
        [Fact]
        public void Every_landing_point_is_inside_its_own_island_envelope()
        {
            foreach (ReleaseIslandRecord record in WildernessCatalog.All)
            {
                IslandLandingPoint pad = record.Landing;
                IslandTerrainEnvelope envelope = record.Envelope;
                Assert.InRange(pad.LocalX, envelope.MinX, envelope.MaxX);
                Assert.InRange(pad.LocalZ, envelope.MinZ, envelope.MaxZ);
                Assert.InRange(pad.LocalY, envelope.MinY, envelope.MaxY);
            }
        }

        /// <summary>
        /// The already-shipped Mental Facility destination and the generated
        /// wilderness landing must name the SAME rock. Two coordinates for one
        /// island is how a "why did it put me over there" bug is born, so the
        /// generator pins the reviewed point rather than out-voting it.
        /// </summary>
        [Fact]
        public void Mental_facility_keeps_the_landing_point_that_was_already_reviewed()
        {
            ReleaseIslandRecord record = ReleaseWorldCatalog.Require(IslandCatalog.MentalFacility.Id);

            Assert.True(record.Landing.Reviewed);
            Assert.Equal(120.00, record.Landing.LocalX, 2);
            Assert.Equal(34.26, record.Landing.LocalY, 2);
            Assert.Equal(-16.00, record.Landing.LocalZ, 2);

            WildernessDestination destination =
                WildernessCatalog.For(IslandCatalog.MentalFacility.Id)!.Value;
            TeleportPolicy.TryResolve(TeleportPolicy.MentalFacilityName,
                out TeleportDestination named);

            // To within one Q52.12 unit - 0.24 mm - and no closer, on purpose. The
            // two arrive by the world's two legitimate encodings: TeleportPolicy
            // adds the offsets in METRES and truncates once, IslandDefinition
            // .LocalToGlobal truncates the origin and the local offset separately
            // and adds, which is what the client and the pre-registry pipeline do.
            // Demanding bit equality would be demanding one of them stop matching
            // the thing it was written to match.
            Assert.True(Math.Abs(named.Position.X - destination.Position.X) <= 1);
            Assert.True(Math.Abs(named.Position.Y - destination.Position.Y) <= 1);
            Assert.True(Math.Abs(named.Position.Z - destination.Position.Z) <= 1);
        }

        [Fact]
        public void Open_keeps_only_registered_tier_one_islands()
        {
            IslandRegistry registry = IslandRegistry.CreateReleaseWorld("tier1");

            IReadOnlyList<WildernessDestination> open = WildernessCatalog.Open(registry.All);

            // 46 tier-1 islands; Haven is registered too and is not one of them.
            Assert.Equal(46, open.Count);
            Assert.DoesNotContain(open, destination => destination.IslandId == IslandCatalog.Haven.Id);
        }

        /// <summary>
        /// The refusal case, at the catalogue level: a boot that registered a
        /// tier-3 district has NOWHERE in the Wilderness, and must say so rather
        /// than fall back to a tier-1 island whose terrain nobody spawned.
        /// </summary>
        [Fact]
        public void Open_is_empty_when_no_tier_one_district_is_registered()
        {
            IslandRegistry registry = IslandRegistry.CreateReleaseWorld("C6");

            Assert.Empty(WildernessCatalog.Open(registry.All));
        }

        [Fact]
        public void Open_is_ordered_by_island_id_whatever_order_islands_were_registered()
        {
            IReadOnlyList<IslandDefinition> forwards = TierOneDefinitions();
            IReadOnlyList<IslandDefinition> backwards = forwards.Reverse().ToArray();

            Assert.Equal(
                WildernessCatalog.Open(forwards).Select(destination => destination.IslandId.Value),
                WildernessCatalog.Open(backwards).Select(destination => destination.IslandId.Value));
            Assert.Equal(
                WildernessCatalog.Open(forwards).Select(destination => destination.IslandId.Value)
                    .OrderBy(value => value, StringComparer.Ordinal),
                WildernessCatalog.Open(forwards).Select(destination => destination.IslandId.Value));
        }

        [Fact]
        public void A_destination_is_the_island_origin_plus_the_pad_plus_the_stand_off()
        {
            ReleaseIslandRecord record = WildernessCatalog.All[0];

            WildernessDestination destination = WildernessCatalog.For(record.Definition.Id)!.Value;

            Assert.Equal(
                record.Definition.LocalToGlobal(
                    record.Landing.LocalX,
                    record.Landing.LocalY + WildernessCatalog.StandOffMetres,
                    record.Landing.LocalZ),
                destination.Position);
            Assert.Equal(record.Definition.WorldEntityKey, destination.WorldEntityKey);
        }

        [Fact]
        public void A_non_tier_one_island_has_no_wilderness_destination()
        {
            Assert.Null(WildernessCatalog.For(IslandCatalog.AnchorageIsle.Id));
            Assert.Null(WildernessCatalog.For(IslandCatalog.Haven.Id));
        }

        /// <summary>
        /// The conversion into the existing teleport type must keep the island name
        /// on it. That field is what makes the teleport path request the terrain
        /// and what lets the landing pin it as confirmed ground; drop it and the
        /// arrival silently becomes the unguarded kind that
        /// <see cref="SpawnRestorePolicy"/> exists because of.
        /// </summary>
        [Fact]
        public void The_teleport_destination_carries_the_islands_terrain_key()
        {
            WildernessDestination destination = WildernessCatalog.All
                .Select(record => WildernessCatalog.For(record.Definition.Id)!.Value).First();

            TeleportDestination teleport =
                WildernessCatalog.AsTeleportDestination(destination, WildernessShrine.TeleportReason);

            Assert.Equal(destination.WorldEntityKey, teleport.RequiredWorldEntityKey);
            Assert.Equal(destination.Position, teleport.Position);
            Assert.False(teleport.LandsOnLoadedGround);
        }
    }
}
