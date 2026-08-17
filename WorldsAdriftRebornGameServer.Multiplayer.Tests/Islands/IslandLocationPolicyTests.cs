using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// Which island a stored world position belongs to. This is the question the
    /// logout restore never asked, which is why it teleported a player onto an
    /// island that was not on their client yet.
    /// </summary>
    public sealed class IslandLocationPolicyTests
    {
        private static (IslandDefinition, IslandTerrainEnvelope) Pair(IslandDefinition island) =>
            (island, IslandTerrainEnvelopes.Require(island.Id));

        [Fact]
        public void A_point_at_an_island_origin_is_on_that_island()
        {
            IslandLocation location = IslandLocationPolicy.Locate(
                IslandCatalog.Haven.GlobalOrigin,
                new[] { Pair(IslandCatalog.Haven), Pair(IslandCatalog.MentalFacility) });

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.Equal(IslandCatalog.HavenId, location.Island!.Id);
            Assert.Equal(0.0, location.MetresFromTerrain);
        }

        /// <summary>
        /// The exact bug, reduced: the user's character logged out on Shattered
        /// Mausoleum, 4.4 km from spawn. That point must resolve to Mausoleum and
        /// not to Haven, because Haven's terrain being loaded says nothing at all
        /// about whether Mausoleum's is.
        /// </summary>
        [Fact]
        public void A_point_on_a_distant_optional_island_resolves_to_that_island_not_to_haven()
        {
            IslandDefinition mausoleum = IslandCatalog.ShatteredMausoleum;
            FixedPointPosition standing = mausoleum.LocalToGlobal(0.0, 0.0, 0.0);

            IslandLocation location = IslandLocationPolicy.Locate(
                standing, IslandLocationPolicy.KnownWorld());

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.Equal(IslandCatalog.ShatteredMausoleumId, location.Island!.Id);
            Assert.NotEqual(IslandCatalog.HavenId, location.Island!.Id);
        }

        /// <summary>
        /// Standing on the highest point of an island puts a character capsule
        /// slightly ABOVE the collision AABB. That must still read as "on this
        /// island" or every hilltop logout would be refused.
        /// </summary>
        [Fact]
        public void Standing_just_above_the_collision_envelope_still_counts_as_on_the_island()
        {
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.Require(IslandCatalog.HavenId);
            FixedPointPosition onThePeak = IslandCatalog.Haven.LocalToGlobal(
                0.0, envelope.MaxY + 2.0, 0.0);

            IslandLocation location = IslandLocationPolicy.Locate(
                onThePeak, new[] { Pair(IslandCatalog.Haven) });

            Assert.Equal(IslandLocationKind.OnKnownTerrain, location.Kind);
            Assert.True(location.MetresFromTerrain > 0.0);
            Assert.True(location.MetresFromTerrain <= IslandLocationPolicy.GroundSlackMetres);
        }

        /// <summary>
        /// A player on their ship in open air has no terrain to wait for. Reporting
        /// OpenSky is what stops the restore refusing every ship crew for a hazard
        /// that is not terrain-shaped.
        /// </summary>
        [Fact]
        public void A_point_far_from_every_island_is_open_sky()
        {
            FixedPointPosition adrift = IslandCatalog.Haven.LocalToGlobal(0.0, 3000.0, 0.0);

            IslandLocation location = IslandLocationPolicy.Locate(
                adrift, IslandLocationPolicy.KnownWorld());

            Assert.Equal(IslandLocationKind.OpenSky, location.Kind);
            Assert.Null(location.Island);
            Assert.Equal("open sky", location.Name);
        }

        [Fact]
        public void With_no_candidates_at_all_every_point_is_open_sky()
        {
            IslandLocation location = IslandLocationPolicy.Locate(
                IslandCatalog.Haven.GlobalOrigin,
                Array.Empty<(IslandDefinition, IslandTerrainEnvelope)>());

            Assert.Equal(IslandLocationKind.OpenSky, location.Kind);
        }

        /// <summary>
        /// The answer must not depend on which order the catalogue happened to
        /// enumerate in, or the same stored row would resolve differently between
        /// boots.
        /// </summary>
        [Fact]
        public void The_nearest_island_wins_regardless_of_enumeration_order()
        {
            FixedPointPosition onMental = IslandCatalog.MentalFacility.LocalToGlobal(0.0, 0.0, 0.0);
            (IslandDefinition, IslandTerrainEnvelope)[] forwards =
            {
                Pair(IslandCatalog.Haven),
                Pair(IslandCatalog.MentalFacility),
                Pair(IslandCatalog.CrimsonParadise),
            };
            (IslandDefinition, IslandTerrainEnvelope)[] backwards =
            {
                Pair(IslandCatalog.CrimsonParadise),
                Pair(IslandCatalog.MentalFacility),
                Pair(IslandCatalog.Haven),
            };

            Assert.Equal(IslandCatalog.MentalFacilityId,
                IslandLocationPolicy.Locate(onMental, forwards).Island!.Id);
            Assert.Equal(IslandCatalog.MentalFacilityId,
                IslandLocationPolicy.Locate(onMental, backwards).Island!.Id);
        }

        /// <summary>
        /// The known world must cover the whole map, not this boot's registered
        /// topology - otherwise an island that is not rolled out today reads as
        /// open sky and the restore drops the player into nothing.
        /// </summary>
        [Fact]
        public void The_known_world_covers_every_named_island_and_the_release_catalogue()
        {
            List<IslandId> ids = IslandLocationPolicy.KnownWorld()
                .Select(pair => pair.Island.Id).ToList();

            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Contains(IslandCatalog.HavenId, ids);
            Assert.Contains(IslandCatalog.ShatteredMausoleumId, ids);
            Assert.True(ids.Count >= ReleaseWorldCatalog.All.Count);
            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                Assert.Contains(record.Definition.Id, ids);
            }
        }
    }
}
