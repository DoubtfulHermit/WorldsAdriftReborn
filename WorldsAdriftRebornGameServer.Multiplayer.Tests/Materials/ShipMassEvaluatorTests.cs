using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    public sealed class ShipMassEvaluatorTests
    {
        private static ShipMassInput Input(long hullId = 3639,
            HullMaterials? materials = null, bool planDecoded = true,
            int cells = 6, int decks = 2,
            double halfX = 6.0, double halfY = 1.5, double halfZ = 9.0,
            string? overrideRaw = null, params ShipMassPartInput[] parts) =>
            new ShipMassInput(hullId, materials ?? new HullMaterials("birch", 5, "iron", 5),
                planDecoded, cells, decks, halfX, halfY, halfZ, overrideRaw, parts);

        private static ShipMassPartInput Part(long id, string itemType,
            string prefab = "", string attachment = "deck",
            double x = 0, double y = 0, double z = 0) =>
            new ShipMassPartInput(id, itemType, prefab, attachment, x, y, z);

        // ------------------------------------------------------------------
        // The typed per-part table
        // ------------------------------------------------------------------

        [Fact]
        public void A_wing_weighs_the_community_measured_range_midpoint()
        {
            PartMassVerdict wing = ShipMassEvaluator.PartMass("proceduralWing", "proceduralWingDefault", "wing");
            Assert.Equal(25.0, wing.MassKg);
            Assert.Equal(MassProvenance.WARebornTuning, wing.Provenance);
        }

        [Fact]
        public void An_engine_weighs_the_community_measured_range_midpoint()
        {
            PartMassVerdict engine = ShipMassEvaluator.PartMass("engine", "proceduralEngineDefault", "engine");
            Assert.Equal(58.5, engine.MassKg);
            Assert.Equal(MassProvenance.WARebornTuning, engine.Provenance);
        }

        [Fact]
        public void A_medium_panel_uses_the_recovered_large_panel_relation_interpolated_and_labelled()
        {
            PartMassVerdict panel = ShipMassEvaluator.PartMass("mediumPanel", "PanelMedium", "deck");
            // 40 recovered large-panel units x assumed half share x recovered iron 0.39 kg/unit.
            Assert.Equal(7.8, panel.MassKg, 9);
            Assert.Equal(MassProvenance.Approximation, panel.Provenance);
        }

        [Fact]
        public void A_lost_part_stays_at_the_flat_default_and_is_labelled_an_approximation()
        {
            foreach (string itemType in new[]
            {
                "lamp", "trunk", "sail", "powerGenerator", "fuelGauge", "atlasSkyCore",
                "window", "helm", "headingIndicator", "airspeedIndicator", "altimeter",
                "barPipe", "railing",
            })
            {
                PartMassVerdict verdict = ShipMassEvaluator.PartMass(itemType, "", "deck");
                Assert.Equal(ShipMassEvaluator.DefaultPartMassKg, verdict.MassKg);
                Assert.Equal(MassProvenance.Approximation, verdict.Provenance);
            }
        }

        [Fact]
        public void Unrecognised_or_null_input_gets_the_labelled_default_never_a_throw()
        {
            PartMassVerdict verdict = ShipMassEvaluator.PartMass(null, null, null);
            Assert.Equal(50.0, verdict.MassKg);
            Assert.Equal(MassProvenance.Approximation, verdict.Provenance);
        }

        // ------------------------------------------------------------------
        // THE HULL-3639 REGRESSION FIXTURE - the live ship the handover measured.
        // Old flat model: 3094 kg hull + 19 x 50 kg = 4044 kg. The truthful
        // snapshot moves only the three evidence-backed types (2 wings, 1 engine,
        // 1 medium panel); the fifteen LOST parts stay at the labelled 50 kg.
        // ------------------------------------------------------------------

        private static ShipMassPartInput[] Hull3639Parts() => new[]
        {
            Part(3701, "lamp", "Lamp01"),
            Part(3702, "trunk", "Trunk01"),
            Part(3703, "sail", "Sail01"),
            Part(3704, "sail", "Sail01"),
            Part(3705, "proceduralWing", "proceduralWingDefault", "wing", x: -3, y: 0, z: 1),
            Part(3706, "proceduralWing", "proceduralWingDefault", "wing", x: 3, y: 0, z: 1),
            Part(3707, "powerGenerator", "powerGenerator01"),
            Part(3708, "mediumPanel", "PanelMedium"),
            Part(3709, "fuelGauge", "FuelGauge01"),
            Part(3710, "fuelGauge", "FuelGauge01"),
            Part(3711, "atlasSkyCore", "CoreMain"),
            Part(3712, "window", "Window01"),
            Part(3713, "helm", "Helm01"),
            Part(3714, "headingIndicator", "HeadingIndicator01"),
            Part(3715, "airspeedIndicator", "AirspeedIndicator01"),
            Part(3716, "altimeter", "Altimeter01"),
            Part(3717, "barPipe", "BarPipe"),
            Part(3718, "railing", "Railing01"),
            Part(3719, "engine", "proceduralEngineDefault", "engine", x: 0, y: 0, z: -4),
        };

        /// <summary>
        /// Internal so the vector-runtime propulsion-parity facts can drive the
        /// SAME live regression fixture rather than a second composition.
        /// </summary>
        internal static ShipMassSnapshot Hull3639() =>
            ShipMassEvaluator.Build(Input(parts: Hull3639Parts()), previous: null);

        [Fact]
        public void Hull_3639_structural_mass_reproduces_the_pinned_3094_kg()
        {
            // (6 cells x 2000 + 2 decks x 500) x (birch 0.20 x 0.8 + iron 0.39 x 0.2).
            ShipMassSnapshot snapshot = Hull3639();
            Assert.Equal(3094.0, snapshot.HullStructuralMassKg, 6);
            Assert.Equal(MassProvenance.WARebornTuning, snapshot.HullProvenance);
        }

        [Fact]
        public void Hull_3639_explains_every_one_of_its_nineteen_part_contributions()
        {
            ShipMassSnapshot snapshot = Hull3639();
            Assert.Equal(19, snapshot.MountedParts.Count);

            var expected = new Dictionary<long, (string Kind, double MassKg, MassProvenance Provenance)>
            {
                [3701] = (ShipPartKinds.Lamp, 50.0, MassProvenance.Approximation),
                [3702] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3703] = (ShipPartKinds.Sail, 50.0, MassProvenance.Approximation),
                [3704] = (ShipPartKinds.Sail, 50.0, MassProvenance.Approximation),
                [3705] = (ShipPartKinds.Wing, 25.0, MassProvenance.WARebornTuning),
                [3706] = (ShipPartKinds.Wing, 25.0, MassProvenance.WARebornTuning),
                [3707] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3708] = (ShipPartKinds.Other, ShipMassEvaluator.MediumPanelMassKg, MassProvenance.Approximation),
                [3709] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3710] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3711] = (ShipPartKinds.Core, 50.0, MassProvenance.Approximation),
                [3712] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3713] = (ShipPartKinds.Helm, 50.0, MassProvenance.Approximation),
                [3714] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3715] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3716] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3717] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3718] = (ShipPartKinds.Other, 50.0, MassProvenance.Approximation),
                [3719] = (ShipPartKinds.Engine, 58.5, MassProvenance.WARebornTuning),
            };
            foreach (MountedPartMassEntry entry in snapshot.MountedParts)
            {
                (string kind, double massKg, MassProvenance provenance) = expected[entry.EntityId];
                Assert.Equal(kind, entry.PartKind);
                Assert.Equal(massKg, entry.MassKg, 9);
                Assert.Equal(provenance, entry.Provenance);
            }
        }

        [Fact]
        public void Hull_3639_totals_replace_the_old_4044_flat_total_with_an_explained_3960()
        {
            ShipMassSnapshot snapshot = Hull3639();
            // 15 LOST parts x 50 + 2 wings x 25 + 1 engine 58.5 + 1 medium panel 7.8.
            double expectedMounted = 15 * 50.0 + 2 * ShipMassEvaluator.WingMassKg
                + ShipMassEvaluator.EngineMassKg + ShipMassEvaluator.MediumPanelMassKg;
            Assert.Equal(866.3, expectedMounted, 6);
            Assert.Equal(expectedMounted, snapshot.TotalMountedMassKg, 9);
            Assert.Equal(snapshot.HullStructuralMassKg + snapshot.TotalMountedMassKg,
                snapshot.TotalFlightMassKg, 12);
            Assert.Equal(3960.3, snapshot.TotalFlightMassKg, 6);
            // The retired flat model, kept visible so the operator can see the delta.
            Assert.Equal(4044.0, snapshot.LegacyFlatTotalMassKg, 6);
        }

        [Fact]
        public void Hull_3639_total_equals_the_sum_of_its_own_entries()
        {
            ShipMassSnapshot snapshot = Hull3639();
            Assert.Equal(snapshot.MountedParts.Sum(p => p.MassKg), snapshot.TotalMountedMassKg, 12);
        }

        // ------------------------------------------------------------------
        // Ordering, identity, evidence fields
        // ------------------------------------------------------------------

        [Fact]
        public void Mounted_parts_come_out_ascending_by_entity_id_whatever_order_they_arrive_in()
        {
            ShipMassPartInput[] shuffled = Hull3639Parts()
                .OrderByDescending(p => p.EntityId % 7).ToArray();
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(parts: shuffled), previous: null);
            long previousId = long.MinValue;
            foreach (MountedPartMassEntry entry in snapshot.MountedParts)
            {
                Assert.True(entry.EntityId > previousId, "MountedParts must ascend by EntityId");
                previousId = entry.EntityId;
            }
        }

        [Fact]
        public void Stable_part_key_derives_from_item_type_then_prefab_and_evidence_records_the_item_type()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(Input(parts: new[]
            {
                Part(1, "proceduralWing", "proceduralWingDefault", "wing"),
                new ShipMassPartInput(2, null, "Lamp01", "deck", 0, 0, 0),
                new ShipMassPartInput(3, null, null, null, 0, 0, 0),
            }), previous: null);
            Assert.Equal("proceduralwing", snapshot.MountedParts[0].StablePartKey);
            Assert.Equal("proceduralWing", snapshot.MountedParts[0].MaterialEvidence);
            Assert.Equal("lamp01", snapshot.MountedParts[1].StablePartKey);
            Assert.Equal("", snapshot.MountedParts[1].MaterialEvidence);
            Assert.Equal("unknown", snapshot.MountedParts[2].StablePartKey);
        }

        // ------------------------------------------------------------------
        // WAREBORN_SHIP_MASS override semantics - preserved exactly
        // ------------------------------------------------------------------

        [Fact]
        public void A_valid_override_replaces_the_hull_mass_only_and_is_labelled_tuning()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(overrideRaw: "1200", parts: Part(1, "trunk")), previous: null);
            Assert.Equal(1200.0, snapshot.HullStructuralMassKg);
            Assert.Equal(MassProvenance.WARebornTuning, snapshot.HullProvenance);
            Assert.Equal(1250.0, snapshot.TotalFlightMassKg);
        }

        [Theory]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("-5")]
        [InlineData("0")]
        [InlineData("1000000")]
        public void An_invalid_or_out_of_range_override_is_ignored(string overrideRaw)
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(overrideRaw: overrideRaw), previous: null);
            Assert.Equal(3094.0, snapshot.HullStructuralMassKg, 6);
        }

        [Fact]
        public void An_undecodable_plan_falls_back_to_the_reference_hull_mass_labelled_approximation()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(planDecoded: false), previous: null);
            Assert.Equal(HullMassCalculator.ReferenceHullMassKg, snapshot.HullStructuralMassKg);
            Assert.Equal(MassProvenance.Approximation, snapshot.HullProvenance);
        }

        // ------------------------------------------------------------------
        // COM / inertia - approximate by construction
        // ------------------------------------------------------------------

        [Fact]
        public void Centre_of_mass_leans_toward_the_heavy_mounted_side_and_stays_marked_approximate()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(Input(parts: new[]
            {
                Part(1, "engine", "proceduralEngineDefault", "engine", x: 4.0),
            }), previous: null);
            // Hull 3094 kg at origin + engine 58.5 kg at x=4: COM = 58.5*4/3152.5.
            Assert.Equal(58.5 * 4.0 / (3094.0 + 58.5), snapshot.CentreOfMassApprox.X, 6);
            Assert.True(snapshot.DiagonalInertiaApproxKgM2.X > 0.0);
            Assert.True(snapshot.InertiaIsApproximation);
        }

        [Fact]
        public void Unknown_hull_geometry_leaves_com_and_inertia_at_zero_but_still_approximate()
        {
            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                Input(halfX: 0.0, halfY: 0.0, halfZ: 0.0, parts: Part(1, "trunk")), previous: null);
            Assert.Equal(0.0, snapshot.CentreOfMassApprox.X);
            Assert.Equal(0.0, snapshot.DiagonalInertiaApproxKgM2.Y);
            Assert.True(snapshot.InertiaIsApproximation);
        }

        // ------------------------------------------------------------------
        // Revision / Fingerprint - the acceptance contract
        // ------------------------------------------------------------------

        [Fact]
        public void Two_builds_over_identical_inputs_produce_identical_fingerprints()
        {
            ShipMassSnapshot first = Hull3639();
            ShipMassSnapshot second = Hull3639();
            Assert.Equal(first.Fingerprint, second.Fingerprint);
            Assert.Equal(first.TotalFlightMassKg, second.TotalFlightMassKg);
        }

        [Fact]
        public void A_rebuild_over_unchanged_inputs_keeps_the_revision()
        {
            ShipMassSnapshot first = Hull3639();
            ShipMassSnapshot second = ShipMassEvaluator.Build(
                Input(parts: Hull3639Parts()), previous: first);
            Assert.Equal(1, first.Revision);
            Assert.Equal(1, second.Revision);
        }

        [Fact]
        public void A_mount_change_bumps_the_revision_and_changes_the_fingerprint()
        {
            ShipMassSnapshot before = Hull3639();
            ShipMassPartInput[] withExtra = Hull3639Parts()
                .Append(Part(3720, "trunk", "Trunk01")).ToArray();
            ShipMassSnapshot after = ShipMassEvaluator.Build(
                Input(parts: withExtra), previous: before);
            Assert.Equal(2, after.Revision);
            Assert.NotEqual(before.Fingerprint, after.Fingerprint);
            Assert.Equal(before.TotalFlightMassKg + 50.0, after.TotalFlightMassKg, 9);
        }

        [Fact]
        public void A_material_change_bumps_the_revision_through_the_hull_mass()
        {
            ShipMassSnapshot birchIron = Hull3639();
            ShipMassSnapshot oakSteel = ShipMassEvaluator.Build(
                Input(materials: new HullMaterials("oak", 5, "steel", 5), parts: Hull3639Parts()),
                previous: birchIron);
            Assert.Equal(2, oakSteel.Revision);
            Assert.NotEqual(birchIron.Fingerprint, oakSteel.Fingerprint);
        }

        [Fact]
        public void Fresh_runtime_entity_ids_for_the_same_ship_keep_the_fingerprint_but_serve_the_new_ids()
        {
            ShipMassSnapshot before = Hull3639();
            ShipMassPartInput[] reminted = Hull3639Parts()
                .Select(p => p with { EntityId = p.EntityId + 10_000 }).ToArray();
            ShipMassSnapshot after = ShipMassEvaluator.Build(
                Input(parts: reminted), previous: before);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Equal(1, after.Revision);
            Assert.True(after.TryPartMassKg(13719, out double engine));
            Assert.Equal(58.5, engine);
            Assert.False(after.TryPartMassKg(3719, out _));
        }

        [Fact]
        public void Reminted_ids_in_a_permuted_mount_order_keep_the_fingerprint_for_identical_content()
        {
            // Boot restore re-mints runtime ids in persisted LAST-MOUNT order,
            // which need not match craft order: the ascending-EntityId entry
            // sequence after a restart is a PERMUTATION of the original, not a
            // uniform shift. Reversing the id assignment models that - the
            // sorted entry sequence comes out exactly backwards.
            ShipMassSnapshot before = Hull3639();
            ShipMassPartInput[] reminted = Hull3639Parts()
                .Select(p => p with { EntityId = 20_000 - p.EntityId }).ToArray();
            ShipMassSnapshot after = ShipMassEvaluator.Build(
                Input(parts: reminted), previous: before);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Equal(1, after.Revision);
            // The new ids are still served: the engine (was 3719) now answers at 16281.
            Assert.True(after.TryPartMassKg(16_281, out double engine));
            Assert.Equal(58.5, engine);
        }

        [Fact]
        public void A_genuinely_different_composition_changes_the_fingerprint_despite_canonical_ordering()
        {
            // Same part count, same ids - one lamp becomes a second engine. The
            // canonical sort must not flatten a real content change.
            ShipMassSnapshot before = Hull3639();
            ShipMassPartInput[] parts = Hull3639Parts();
            parts[0] = Part(3701, "engine", "proceduralEngineDefault", "engine");
            ShipMassSnapshot after = ShipMassEvaluator.Build(
                Input(parts: parts), previous: before);
            Assert.NotEqual(before.Fingerprint, after.Fingerprint);
            Assert.Equal(2, after.Revision);
        }
    }
}
