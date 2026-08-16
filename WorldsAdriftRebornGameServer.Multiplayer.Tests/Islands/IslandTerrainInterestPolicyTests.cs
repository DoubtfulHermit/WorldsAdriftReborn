using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandTerrainInterestPolicyTests
    {
        private const long MentalId = 201;
        private const long CopperId = 202;

        [Fact]
        public void Continuous_interest_is_armed_once_not_on_every_final_sentinel_poll()
        {
            Assert.True(IslandTerrainInterestPolicy.ShouldArmContinuous(
                alreadyComplete: false));
            Assert.False(IslandTerrainInterestPolicy.ShouldArmContinuous(
                alreadyComplete: true));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("banana", false)]
        [InlineData("1", true)]
        [InlineData("true", true)]
        [InlineData("YES", true)]
        public void Feature_is_strictly_opt_in(string? value, bool expected) =>
            Assert.Equal(expected, IslandTerrainInterestPolicy.EnabledFrom(value));

        [Fact]
        public void Extracted_envelope_measures_from_geometry_not_origin()
        {
            IslandDefinition island = IslandCatalog.MentalFacility;
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.Require(island.Id);
            FixedPointPosition point = island.LocalToGlobal(176.8, 0, 0);

            Assert.True(envelope.Contains(point, island));
            Assert.True(DistanceSquared(point, island.GlobalOrigin) > 30_000);
        }

        [Fact]
        public void Every_first_region_terrain_has_an_evidenced_envelope()
        {
            foreach (IslandDefinition island in IslandCatalog.FirstRegionTerrain)
                Assert.NotNull(IslandTerrainEnvelopes.ByIsland(island.Id));
        }

        [Fact]
        public void Envelope_rejects_mismatched_island_identity()
        {
            IslandTerrainEnvelope envelope = IslandTerrainEnvelopes.Require(
                IslandCatalog.MentalFacilityId);
            Assert.Throws<ArgumentException>(() =>
                envelope.DistanceSquaredTo(IslandCatalog.Haven.GlobalOrigin, IslandCatalog.Haven));
        }

        [Fact]
        public void Approaching_geometry_queues_terrain_add()
        {
            IReadOnlyList<TerrainStreamAction> actions = Reconcile(
                IslandCatalog.MentalFacility.LocalToGlobal(177.0, 0, 0),
                loaded: Array.Empty<long>());

            Assert.Contains(new TerrainStreamAction(TerrainStreamActionKind.Add,
                MentalId, IslandCatalog.MentalFacilityId), actions);
            Assert.DoesNotContain(actions, x => x.EntityId == CopperId);
        }

        [Fact]
        public void Hysteresis_keeps_loaded_terrain_between_radii()
        {
            FixedPointPosition point = IslandCatalog.MentalFacility.LocalToGlobal(350, 0, 0);
            IReadOnlyList<TerrainStreamAction> actions = Reconcile(point,
                new[] { MentalId }, load: 100, unload: 300);
            Assert.Empty(actions);
        }

        [Fact]
        public void Beyond_unload_radius_queues_remove()
        {
            FixedPointPosition point = IslandCatalog.MentalFacility.LocalToGlobal(600, 0, 0);
            IReadOnlyList<TerrainStreamAction> actions = Reconcile(point,
                new[] { MentalId }, load: 100, unload: 300);
            Assert.Equal(new TerrainStreamAction(TerrainStreamActionKind.Remove,
                MentalId, IslandCatalog.MentalFacilityId), Assert.Single(actions));
        }

        [Fact]
        public void Confirmed_ground_is_not_inferred_from_nearest_position_and_is_protected()
        {
            FixedPointPosition far = IslandCatalog.MentalFacility.LocalToGlobal(5000, 0, 0);
            Assert.Empty(Reconcile(far, new[] { MentalId },
                ground: IslandCatalog.MentalFacilityId, load: 100, unload: 300));

            Assert.Single(Reconcile(far, new[] { MentalId },
                ground: null, load: 100, unload: 300));
        }

        [Fact]
        public void Requested_destination_adds_even_while_far_away()
        {
            IReadOnlyList<TerrainStreamAction> actions = Reconcile(
                IslandCatalog.Haven.GlobalOrigin, Array.Empty<long>(),
                destination: IslandCatalog.MentalFacilityId, load: 100, unload: 300);
            Assert.Equal(MentalId, Assert.Single(actions).EntityId);
        }

        [Fact]
        public void Destination_must_be_ready_before_source_can_remove()
        {
            FixedPointPosition atCopper = IslandCatalog.BetrayalCopperKing.GlobalOrigin;
            IReadOnlyList<TerrainStreamAction> before = Reconcile(atCopper,
                new[] { MentalId }, destination: IslandCatalog.BetrayalCopperKingId,
                load: 100, unload: 300);
            Assert.Single(before);
            Assert.Equal(TerrainStreamActionKind.Add, before[0].Kind);

            IReadOnlyList<TerrainStreamAction> after = Reconcile(atCopper,
                new[] { MentalId, CopperId }, destination: IslandCatalog.BetrayalCopperKingId,
                load: 100, unload: 300);
            Assert.Single(after);
            Assert.Equal(TerrainStreamActionKind.Remove, after[0].Kind);
            Assert.Equal(MentalId, after[0].EntityId);
        }

        [Fact]
        public void Adds_are_ordered_before_removes_during_handoff()
        {
            // Force destination and no loaded destination means the source remains.
            // Once ready, only removal remains; no frame exists where source leaves first.
            var before = Reconcile(IslandCatalog.BetrayalCopperKing.GlobalOrigin,
                new[] { MentalId }, destination: IslandCatalog.BetrayalCopperKingId,
                load: 100, unload: 300);
            Assert.All(before, x => Assert.Equal(TerrainStreamActionKind.Add, x.Kind));
        }

        [Fact]
        public void Resource_drain_gate_blocks_terrain_removal()
        {
            bool asked = false;
            var actions = IslandTerrainInterestPolicy.Reconcile(
                IslandCatalog.Haven.GlobalOrigin, Candidates(), new HashSet<long> { MentalId },
                null, null, 100, 300,
                island => { asked = true; return false; });
            Assert.True(asked);
            Assert.Empty(actions);
        }

        [Fact]
        public void Two_peer_loaded_sets_are_independent()
        {
            FixedPointPosition far = IslandCatalog.Haven.GlobalOrigin;
            var first = Reconcile(far, new[] { MentalId }, load: 100, unload: 300);
            var second = Reconcile(far, Array.Empty<long>(), load: 100, unload: 300);
            Assert.Contains(first, x => x.Kind == TerrainStreamActionKind.Remove);
            Assert.DoesNotContain(second, x => x.EntityId == MentalId
                && x.Kind == TerrainStreamActionKind.Remove);
        }

        [Theory]
        [InlineData(null, 30000)]
        [InlineData("bad", 30000)]
        [InlineData("1", 10000)]
        [InlineData("30000", 30000)]
        [InlineData("999999", 120000)]
        public void Asset_ack_timeout_is_bounded(string? value, int expectedMs) =>
            Assert.Equal(expectedMs,
                IslandTerrainInterestPolicy.AssetAckTimeoutFrom(value).TotalMilliseconds);

        [Fact]
        public void Asset_ack_requires_exact_peer_type_name_and_context()
        {
            Assert.True(IslandTerrainInterestPolicy.ExactAssetAck(
                7, "notNeeded?", "1143725558@Island", "notNeeded?",
                7, "notNeeded?", "1143725558@Island", "notNeeded?"));
            Assert.False(IslandTerrainInterestPolicy.ExactAssetAck(
                7, "notNeeded?", "1143725558@Island", "notNeeded?",
                8, "notNeeded?", "1143725558@Island", "notNeeded?"));
            Assert.False(IslandTerrainInterestPolicy.ExactAssetAck(
                7, "notNeeded?", "1143725558@Island", "notNeeded?",
                7, "notNeeded?", "950242829@Island", "notNeeded?"));
        }

        [Fact]
        public void Asset_retry_does_not_move_total_fallback_deadline()
        {
            TimeSpan started = TimeSpan.FromSeconds(10);
            Assert.True(IslandTerrainInterestPolicy.AssetRetryDue(started,
                started + TimeSpan.FromSeconds(5)));
            Assert.False(IslandTerrainInterestPolicy.AssetFallbackDue(started,
                started + TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30)));
            Assert.True(IslandTerrainInterestPolicy.AssetFallbackDue(started,
                started + TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void Remove_requires_channel_and_correlated_protocol_proof(
            bool channel, bool correlatedAck, bool expected) =>
            Assert.Equal(expected,
                IslandTerrainInterestPolicy.MayRemove(channel, correlatedAck));

        private static IReadOnlyList<TerrainStreamAction> Reconcile(
            FixedPointPosition center,
            IEnumerable<long> loaded,
            IslandId? ground = null,
            IslandId? destination = null,
            double load = 100,
            double unload = 300) =>
            IslandTerrainInterestPolicy.Reconcile(center, Candidates(),
                new HashSet<long>(loaded), ground, destination, load, unload);

        private static TerrainStreamCandidate[] Candidates() => new[]
        {
            new TerrainStreamCandidate(MentalId, IslandCatalog.MentalFacility,
                IslandTerrainEnvelopes.Require(IslandCatalog.MentalFacilityId)),
            new TerrainStreamCandidate(CopperId, IslandCatalog.BetrayalCopperKing,
                IslandTerrainEnvelopes.Require(IslandCatalog.BetrayalCopperKingId)),
        };

        private static double DistanceSquared(FixedPointPosition a, FixedPointPosition b)
        {
            double x = a.MetresX - b.MetresX;
            double y = a.MetresY - b.MetresY;
            double z = a.MetresZ - b.MetresZ;
            return x * x + y * y + z * z;
        }
    }
}
