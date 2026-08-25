using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// Drives <see cref="ShipMassSnapshotCachePolicy"/> through the exact call
    /// sequences the game-assembly glue (<c>ShipMassSnapshots</c>) performs -
    /// first demand, cached serve, invalidate-and-rebuild, override change,
    /// retire is a plain dictionary remove and needs no policy. The wiring test
    /// (<see cref="ShipMassSnapshotWiringTests"/>) pins the glue to these same
    /// policy calls, so the sequence mirrored here is the production sequence.
    /// </summary>
    public sealed class ShipMassSnapshotCachePolicyTests
    {
        private static ShipMassPartInput Part(long id, string itemType,
            string prefab = "", string attachment = "deck") =>
            new ShipMassPartInput(id, itemType, prefab, attachment, 0, 0, 0);

        private static ShipMassInput Input(string? overrideRaw = null,
            params ShipMassPartInput[] parts) =>
            new ShipMassInput(3639, new HullMaterials("birch", 5, "iron", 5),
                planDecoded: true, cellCount: 6, deckCount: 2, 6.0, 1.5, 9.0,
                overrideRaw, parts);

        /// <summary>
        /// The glue's For() body over one hull's slot: try to serve, else rebuild
        /// with the policy's continuity previous and store the policy's slot.
        /// </summary>
        private static ShipMassSnapshot ForSequence(ref ShipMassCacheSlot slot,
            string? overrideRaw, ShipMassInput input, out bool rebuilt, out bool news)
        {
            if (ShipMassSnapshotCachePolicy.TryServe(slot, overrideRaw,
                out ShipMassSnapshot cached))
            {
                rebuilt = false;
                news = false;
                return cached;
            }
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(input,
                ShipMassSnapshotCachePolicy.ContinuityPrevious(slot));
            news = ShipMassSnapshotCachePolicy.RevisionIsNews(slot, snapshot);
            slot = ShipMassSnapshotCachePolicy.Stored(overrideRaw, snapshot);
            rebuilt = true;
            return snapshot;
        }

        // ------------------------------------------------------------------
        // Build and serve
        // ------------------------------------------------------------------

        [Fact]
        public void First_demand_builds_and_the_second_serves_the_same_cached_snapshot()
        {
            ShipMassCacheSlot slot = default;
            ShipMassSnapshot first = ForSequence(ref slot, null,
                Input(parts: Part(1, "trunk")), out bool rebuiltFirst, out bool news);
            Assert.True(rebuiltFirst);
            Assert.True(news, "the first build for a hull is always operator news");
            Assert.Equal(1, first.Revision);

            ShipMassSnapshot second = ForSequence(ref slot, null,
                Input(parts: Part(1, "trunk")), out bool rebuiltSecond, out _);
            Assert.False(rebuiltSecond);
            Assert.Same(first, second);
        }

        [Fact]
        public void A_never_built_slot_serves_nothing_and_has_no_continuity_previous()
        {
            ShipMassCacheSlot slot = default;
            Assert.False(ShipMassSnapshotCachePolicy.TryServe(slot, null, out _));
            Assert.False(ShipMassSnapshotCachePolicy.TryServe(slot, "1200", out _));
            Assert.Null(ShipMassSnapshotCachePolicy.ContinuityPrevious(slot));
        }

        // ------------------------------------------------------------------
        // Invalidation sentinel semantics
        // ------------------------------------------------------------------

        [Fact]
        public void An_invalidated_slot_never_serves_but_keeps_its_snapshot_as_the_continuity_previous()
        {
            ShipMassCacheSlot slot = default;
            ShipMassSnapshot built = ForSequence(ref slot, null,
                Input(parts: Part(1, "trunk")), out _, out _);

            slot = ShipMassSnapshotCachePolicy.Invalidated(slot);
            Assert.False(ShipMassSnapshotCachePolicy.TryServe(slot, null, out _));
            Assert.False(ShipMassSnapshotCachePolicy.TryServe(slot, "1200", out _));
            Assert.Same(built, ShipMassSnapshotCachePolicy.ContinuityPrevious(slot));
        }

        [Fact]
        public void The_sentinel_contains_nul_so_no_real_environment_value_can_collide_with_it()
        {
            Assert.Contains('\0', ShipMassSnapshotCachePolicy.InvalidationSentinel);
        }

        [Fact]
        public void Invalidating_a_never_built_slot_changes_nothing()
        {
            ShipMassCacheSlot slot = ShipMassSnapshotCachePolicy.Invalidated(default);
            Assert.Equal(default, slot);
        }

        [Fact]
        public void Invalidate_then_rebuild_over_unchanged_inputs_keeps_the_revision_and_stays_quiet()
        {
            ShipMassCacheSlot slot = default;
            ShipMassSnapshot first = ForSequence(ref slot, null,
                Input(parts: Part(1, "trunk")), out _, out _);

            slot = ShipMassSnapshotCachePolicy.Invalidated(slot);
            ShipMassSnapshot second = ForSequence(ref slot, null,
                Input(parts: Part(1, "trunk")), out bool rebuilt, out bool news);
            Assert.True(rebuilt, "an invalidated slot must force a rebuild");
            Assert.False(news, "a rebuild that proved nothing changed is not operator news");
            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.Equal(1, second.Revision);
        }

        [Fact]
        public void Invalidate_then_a_real_mount_change_bumps_the_revision_and_is_news()
        {
            ShipMassCacheSlot slot = default;
            ForSequence(ref slot, null, Input(parts: Part(1, "trunk")), out _, out _);

            slot = ShipMassSnapshotCachePolicy.Invalidated(slot);
            ShipMassSnapshot after = ForSequence(ref slot, null,
                Input(overrideRaw: null, Part(1, "trunk"),
                    Part(2, "engine", "proceduralEngineDefault", "engine")),
                out _, out bool news);
            Assert.True(news);
            Assert.Equal(2, after.Revision);
        }

        // ------------------------------------------------------------------
        // Override-change detection
        // ------------------------------------------------------------------

        [Fact]
        public void An_override_change_forces_a_rebuild_that_bumps_the_revision_without_any_invalidate()
        {
            ShipMassCacheSlot slot = default;
            ShipMassSnapshot plain = ForSequence(ref slot, null, Input(), out _, out _);
            Assert.Equal(3094.0, plain.HullStructuralMassKg, 6);

            ShipMassSnapshot overridden = ForSequence(ref slot, "1200",
                Input(overrideRaw: "1200"), out bool rebuilt, out bool news);
            Assert.True(rebuilt, "the raw-value comparison keeps the knob live without a restart");
            Assert.True(news);
            Assert.Equal(1200.0, overridden.HullStructuralMassKg);
            Assert.Equal(2, overridden.Revision);

            // The new value now serves from cache; clearing it forces another rebuild.
            ForSequence(ref slot, "1200", Input(overrideRaw: "1200"), out bool rebuiltThird, out _);
            Assert.False(rebuiltThird);
            ShipMassSnapshot cleared = ForSequence(ref slot, null, Input(), out bool rebuiltAgain, out _);
            Assert.True(rebuiltAgain);
            Assert.Equal(3, cleared.Revision);
        }

        // ------------------------------------------------------------------
        // Part-mass fallback selection
        // ------------------------------------------------------------------

        [Fact]
        public void A_mounted_part_the_snapshot_carries_answers_from_the_snapshot()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(parts: Part(7, "engine", "proceduralEngineDefault", "engine")),
                previous: null);
            Assert.Equal(58.5, ShipMassSnapshotCachePolicy.PartMassKg(
                snapshot, 7, "engine", "proceduralEngineDefault", "engine"));
        }

        [Fact]
        public void A_part_the_snapshot_has_not_caught_up_with_falls_back_to_the_typed_table()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(parts: Part(7, "trunk")), previous: null);
            Assert.Equal(ShipMassEvaluator.WingMassKg, ShipMassSnapshotCachePolicy.PartMassKg(
                snapshot, 99, "proceduralWing", "proceduralWingDefault", "wing"));
        }

        [Fact]
        public void A_loose_part_gets_the_typed_table_so_it_weighs_the_same_as_when_mounted()
        {
            Assert.Equal(ShipMassEvaluator.EngineMassKg, ShipMassSnapshotCachePolicy.PartMassKg(
                null, 42, "engine", "proceduralEngineDefault", "engine"));
        }

        [Fact]
        public void An_unknown_entity_gets_the_labelled_flat_default_never_a_throw()
        {
            Assert.Equal(ShipMassEvaluator.DefaultPartMassKg,
                ShipMassSnapshotCachePolicy.PartMassKg(null, 42, null, null, null));
        }

        // ------------------------------------------------------------------
        // Hull half-extents for the COM estimate
        // ------------------------------------------------------------------

        [Fact]
        public void Hull_half_extents_halve_the_measured_metrics_in_beam_deck_keel_order()
        {
            (double x, double y, double z) = ShipMassSnapshotCachePolicy.HullHalfExtents(
                beamMetres: 12.0, deckPlaneMetres: 3.0, keelMetres: 18.0);
            Assert.Equal(6.0, x);
            Assert.Equal(1.5, y);
            Assert.Equal(9.0, z);
        }

        [Fact]
        public void Hull_half_extents_floor_a_degenerate_plan_at_a_quarter_metre()
        {
            (double x, double y, double z) = ShipMassSnapshotCachePolicy.HullHalfExtents(0.0, 0.1, -4.0);
            Assert.Equal(0.25, x);
            Assert.Equal(0.25, y);
            Assert.Equal(0.25, z);
        }
    }
}
