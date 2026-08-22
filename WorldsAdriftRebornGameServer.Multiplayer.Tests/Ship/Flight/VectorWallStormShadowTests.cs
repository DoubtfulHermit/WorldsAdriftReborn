using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public class VectorWallStormShadowTests
    {
        private static readonly VectorWallSegment NorthSouthWind =
            new(1, VectorWallType.WindRift, V(0, 0, -1000), V(0, 0, 1000));

        private static ShadowVector3 V(double x, double y, double z) => new(x, y, z);

        private static VectorWallTypeTuning Enabled(
            double horizontal = 20.0,
            double vertical = 0.0,
            double gust = 0.0,
            double torque = 0.0,
            bool damage = false,
            int interval = 10,
            double damageFraction = 0.0) =>
            new(true, horizontal, vertical, gust, gust, torque, 0.0, 0.9,
                damage, interval, damageFraction);

        private static VectorWallStormTuning Tuning(params (VectorWallType, VectorWallTypeTuning)[] values) =>
            new(values.ToDictionary(value => value.Item1, value => value.Item2));

        private static VectorWallStormInput Input(
            ShadowVector3? position = null,
            ShadowVector3? velocity = null,
            ShadowVector3? forward = null,
            double mass = 1000.0,
            long tick = 100,
            double dt = 0.02,
            ShadowVector3? scalar = null) =>
            new("ship-8", position ?? V(100, 20, 0), velocity ?? ShadowVector3.Zero,
                forward ?? ShadowVector3.Forward, ShadowVector3.Zero, mass,
                ShadowVector3.Zero, tick, dt, scalar ?? ShadowVector3.Zero);

        private static VectorWallStormShadowResult Evaluate(VectorWallStormInput input,
            IReadOnlyList<VectorWallSegment> segments, VectorWallStormTuning tuning,
            IReadOnlyList<VectorWallGustPulse>? pulses = null,
            IReadOnlyList<VectorWallDamageTarget>? targets = null)
        {
            Assert.True(VectorWallStormShadow.TryEvaluate(input, segments, tuning, pulses, targets,
                out VectorWallStormShadowResult result));
            return result;
        }

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(199.999, 1.0)]
        [InlineData(200.0, 1.0)]
        [InlineData(300.0, 0.5)]
        [InlineData(399.999, 0.000005)]
        [InlineData(400.0, 0.0)]
        [InlineData(400.001, 0.0)]
        public void Recovered_physics_bands_are_continuous(double metres, double expected)
        {
            Assert.Equal(expected, VectorWallStormShadow.PhysicsIntensity(metres), 6);
        }

        [Fact]
        public void Visual_and_physical_ranges_remain_truthfully_distinct()
        {
            Assert.Equal(0.0, VectorWallStormShadow.PhysicsIntensity(600.0));
            Assert.True(VectorWallStormShadow.VisualIntensity(600.0) > 0.0);
            Assert.Equal(0.0, VectorWallStormShadow.VisualIntensity(800.0));

            VectorWallStormShadowResult result = Evaluate(
                Input(position: V(600, 5000, 0)), new[] { NorthSouthWind }, new VectorWallStormTuning());
            Assert.Single(result.Samples);
            Assert.False(result.Samples[0].SelectedForDrag);
            Assert.Equal(0.0, result.TotalForceLocal.Magnitude);
        }

        [Theory]
        [InlineData(0.0, 1.0)]
        [InlineData(1000.0, 0.8125)]
        [InlineData(2000.0, 0.625)]
        [InlineData(4000.0, 0.25)]
        [InlineData(40000.0, 0.25)]
        public void Recovered_mass_attenuation_is_a_soft_saturating_ramp(double mass, double expected)
        {
            Assert.Equal(expected, VectorWallStormShadow.MassAttenuation(mass), 8);
        }

        [Fact]
        public void Default_tuning_observes_visuals_but_cannot_apply_mechanics_or_damage()
        {
            VectorWallStormShadowResult result = Evaluate(Input(), new[] { NorthSouthWind },
                new VectorWallStormTuning(),
                new[] { new VectorWallGustPulse(1, VectorWallGustSize.Big, 90, V(2, 0, 0), 1) },
                new[] { new VectorWallDamageTarget("sail-1", VectorWallDamageTargetKind.Sail) });

            Assert.Single(result.Samples);
            Assert.Equal(0.0, result.TotalForceLocal.Magnitude);
            Assert.Equal(0.0, result.TotalTorqueLocal.Magnitude);
            Assert.Empty(result.DamageIntents);
            Assert.Equal(1, result.RejectedPulses);
        }

        [Fact]
        public void Intent_identifiers_reject_control_characters_and_ambiguous_delimiters()
        {
            Assert.False((Input() with { ShipId = "ship-8\nforged" }).IsValid);
            Assert.False(new VectorWallDamageTarget("part/../../other",
                VectorWallDamageTargetKind.Engine).IsValid);
            Assert.True(new VectorWallDamageTarget("ship:8.part-12_engine",
                VectorWallDamageTargetKind.Engine).IsValid);
        }

        [Theory]
        [InlineData(VectorWallType.WindRift, 100.0, 0.0, 0.0, 1)]
        [InlineData(VectorWallType.WindRift, -100.0, 0.0, 0.0, -1)]
        [InlineData(VectorWallType.StormRift, 100.0, 0.0, 0.0, 1)]
        [InlineData(VectorWallType.SandStorm, 100.0, 0.0, 0.0, 1)]
        [InlineData(VectorWallType.WorldEndWall, 100.0, 0.0, 0.0, 1)]
        [InlineData(VectorWallType.Typhon, 100.0, 0.0, 0.0, 1)]
        [InlineData(VectorWallType.IceStorm, 100.0, 0.0, 0.0, -1)]
        public void Every_wall_type_has_the_recovered_force_direction(VectorWallType type,
            double x, double y, double z, int expectedSign)
        {
            VectorWallSegment segment = new(4, type, V(0, 0, -1000), V(0, 0, 1000));
            VectorWallTypeTuning value = type == VectorWallType.WindRift
                ? Enabled(horizontal: 20, vertical: 0)
                : Enabled(horizontal: 20);
            VectorWallStormShadowResult result = Evaluate(Input(position: V(x, y, z)),
                new[] { segment }, Tuning((type, value)));

            double observed = type == VectorWallType.WindRift
                ? result.WallDragForceLocal.X
                : type == VectorWallType.IceStorm
                    ? result.WallDragForceLocal.Y
                    : result.WallDragForceLocal.Z;
            Assert.Equal(expectedSign, Math.Sign(observed));
        }

        [Fact]
        public void Wind_rift_vertical_wind_and_downward_gust_are_separate_vectors()
        {
            VectorWallStormInput input = Input(tick: 25, dt: 0.01);
            VectorWallGustPulse pulse = new(1, VectorWallGustSize.Big, 0, V(2, 0, 0), 123);
            VectorWallStormShadowResult result = Evaluate(input, new[] { NorthSouthWind },
                Tuning((VectorWallType.WindRift, Enabled(horizontal: 0, vertical: -15, gust: 1000))),
                new[] { pulse });

            Assert.True(result.WallDragForceLocal.Y < 0.0);
            Assert.Equal(-1000.0, result.GustForceLocal.Y, 6);
            Assert.True(result.GustTorqueLocal.Z < 0.0);
            Assert.Equal(0.0, result.AlignmentTorqueLocal.Magnitude);
        }

        [Fact]
        public void Gust_envelope_is_deterministic_and_frame_partition_independent()
        {
            Assert.Equal(0.0, VectorWallStormShadow.GustEnvelope(0, 0, 0.02));
            Assert.Equal(1.0, VectorWallStormShadow.GustEnvelope(25, 0, 0.01), 9);
            Assert.Equal(1.0, VectorWallStormShadow.GustEnvelope(50, 0, 0.005), 9);
            Assert.Equal(0.0, VectorWallStormShadow.GustEnvelope(50, 0, 0.01));
            Assert.Equal(
                VectorWallStormShadow.GustDirection(VectorWallType.StormRift, 999),
                VectorWallStormShadow.GustDirection(VectorWallType.StormRift, 999));
            Assert.Equal(1.0,
                VectorWallStormShadow.GustDirection(VectorWallType.StormRift, 999).Magnitude, 9);
            Assert.Equal(ShadowVector3.Zero,
                VectorWallStormShadow.GustDirection(VectorWallType.WorldEndWall, 999));
        }

        [Fact]
        public void Small_and_big_gust_strengths_are_explicit_not_an_implicit_multiplier()
        {
            var distinct = new VectorWallTypeTuning(true, 0, 0, 100, 400, 0, 0, 0.9,
                false, 10, 0);
            VectorWallStormInput input = Input(tick: 25, dt: 0.01);
            VectorWallStormShadowResult small = Evaluate(input, new[] { NorthSouthWind },
                Tuning((VectorWallType.WindRift, distinct)),
                new[] { new VectorWallGustPulse(1, VectorWallGustSize.Small, 0, ShadowVector3.Zero, 1) });
            VectorWallStormShadowResult big = Evaluate(input, new[] { NorthSouthWind },
                Tuning((VectorWallType.WindRift, distinct)),
                new[] { new VectorWallGustPulse(1, VectorWallGustSize.Big, 0, ShadowVector3.Zero, 1) });

            Assert.Equal(100.0, small.GustForceLocal.Magnitude, 6);
            Assert.Equal(400.0, big.GustForceLocal.Magnitude, 6);
        }

        [Fact]
        public void Storm_yaw_is_strong_crosswall_zero_when_aligned_and_windrift_has_none()
        {
            VectorWallSegment storm = new(2, VectorWallType.StormRift,
                V(0, 0, -1000), V(0, 0, 1000));
            VectorWallStormTuning tuning = Tuning(
                (VectorWallType.StormRift, Enabled(torque: 5000)),
                (VectorWallType.WindRift, Enabled(torque: 5000)));

            VectorWallStormShadowResult cross = Evaluate(Input(forward: V(1, 0, 0)),
                new[] { storm }, tuning);
            VectorWallStormShadowResult aligned = Evaluate(Input(forward: V(0, 0, 1)),
                new[] { storm }, tuning);
            VectorWallStormShadowResult wind = Evaluate(Input(forward: V(1, 0, 0)),
                new[] { NorthSouthWind }, tuning);

            Assert.Equal(5000.0, Math.Abs(cross.AlignmentTorqueLocal.Y), 6);
            Assert.Equal(0.0, aligned.AlignmentTorqueLocal.Magnitude);
            Assert.Equal(0.0, wind.AlignmentTorqueLocal.Magnitude);
        }

        [Fact]
        public void Nearest_wall_drives_drag_but_nearest_of_each_type_can_stack_effects()
        {
            VectorWallSegment storm = new(2, VectorWallType.StormRift,
                V(50, 0, -1000), V(50, 0, 1000));
            VectorWallStormTuning tuning = Tuning(
                (VectorWallType.WindRift, Enabled(horizontal: 30, gust: 1000)),
                (VectorWallType.StormRift, Enabled(horizontal: 10, gust: 1000, torque: 5000)));
            var pulses = new[]
            {
                new VectorWallGustPulse(1, VectorWallGustSize.Small, 0, V(2, 0, 0), 1),
                new VectorWallGustPulse(2, VectorWallGustSize.Small, 0, V(-2, 0, 0), 2),
            };

            VectorWallStormShadowResult result = Evaluate(Input(position: V(10, 0, 0), tick: 25, dt: 0.01),
                new[] { storm, NorthSouthWind }, tuning, pulses);

            Assert.True(result.Samples.Single(sample => sample.WallId == 1).SelectedForDrag);
            Assert.False(result.Samples.Single(sample => sample.WallId == 2).SelectedForDrag);
            Assert.All(result.Samples, sample => Assert.True(sample.SelectedForTypeEffects));
            Assert.Equal(2, result.Samples.Count);
            Assert.True(result.GustForceLocal.Magnitude > 0.0);
            Assert.NotEqual(0.0, result.GustTorqueLocal.Magnitude);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.7853981633974483)]
        [InlineData(1.5707963267948966)]
        [InlineData(2.356194490192345)]
        [InlineData(3.141592653589793)]
        [InlineData(3.9269908169872414)]
        [InlineData(4.71238898038469)]
        [InlineData(5.497787143782138)]
        public void All_eight_headings_produce_finite_comparison_telemetry(double heading)
        {
            ShadowVector3 forward = V(Math.Sin(heading), 0, Math.Cos(heading));
            VectorWallStormShadowResult result = Evaluate(Input(forward: forward,
                    scalar: V(1, 2, 3), velocity: V(15, 0, -4)),
                new[] { NorthSouthWind }, Tuning((VectorWallType.WindRift, Enabled())));

            Assert.True(result.Comparison.DeltaLocal.IsFinite);
            Assert.Equal(V(1, 2, 3), result.Comparison.ScalarWallForceLocal);
        }

        [Fact]
        public void Lightning_intent_is_deterministic_idempotent_and_only_inside_300m()
        {
            VectorWallSegment storm = new(9, VectorWallType.StormRift,
                V(0, 0, -1000), V(0, 0, 1000));
            VectorWallStormTuning tuning = Tuning((VectorWallType.StormRift,
                Enabled(damage: true, interval: 10, damageFraction: 0.02)));
            var targets = new[]
            {
                new VectorWallDamageTarget("hull-a", VectorWallDamageTargetKind.HullPart),
                new VectorWallDamageTarget("engine-b", VectorWallDamageTargetKind.Engine),
            };
            VectorWallDamageIntent? found = null;
            long foundTick = -1;
            for (long tick = 0; tick < 10; tick++)
            {
                VectorWallStormShadowResult candidate = Evaluate(Input(position: V(299.999, 0, 0), tick: tick),
                    new[] { storm }, tuning, targets: targets);
                if (candidate.DamageIntents.Count == 1)
                {
                    found = candidate.DamageIntents[0];
                    foundTick = tick;
                }
            }

            Assert.True(found.HasValue);
            VectorWallStormShadowResult replay = Evaluate(Input(position: V(299.999, 0, 0), tick: foundTick),
                new[] { storm }, tuning, targets: targets);
            Assert.Single(replay.DamageIntents);
            Assert.Equal(found.Value, replay.DamageIntents[0]);
            Assert.StartsWith("wall:9:", found.Value.IntentId);

            VectorWallStormShadowResult edge = Evaluate(Input(position: V(300.001, 0, 0), tick: foundTick),
                new[] { storm }, tuning, targets: targets);
            Assert.Empty(edge.DamageIntents);
        }

        [Fact]
        public void Wind_and_sand_intents_only_choose_the_recovered_exposure_targets()
        {
            VectorWallSegment sand = new(3, VectorWallType.SandStorm,
                V(40, 0, -1000), V(40, 0, 1000));
            VectorWallStormTuning tuning = Tuning(
                (VectorWallType.WindRift, Enabled(damage: true, interval: 10, damageFraction: 0.01)),
                (VectorWallType.SandStorm, Enabled(damage: true, interval: 10, damageFraction: 0.01)));
            var targets = new[]
            {
                new VectorWallDamageTarget("sail", VectorWallDamageTargetKind.Sail),
                new VectorWallDamageTarget("wing", VectorWallDamageTargetKind.Wing),
                new VectorWallDamageTarget("engine", VectorWallDamageTargetKind.Engine),
                new VectorWallDamageTarget("hull", VectorWallDamageTargetKind.HullPart),
            };
            var seen = new List<VectorWallDamageIntent>();
            for (long tick = 0; tick < 10; tick++)
            {
                seen.AddRange(Evaluate(Input(position: V(10, 0, 0), tick: tick),
                    new[] { NorthSouthWind, sand }, tuning, targets: targets).DamageIntents);
            }

            Assert.Equal(2, seen.Count);
            Assert.Equal("sail", seen.Single(intent =>
                intent.Kind == VectorWallDamageIntentKind.WindRiftSailExposure).TargetEntityId);
            Assert.Contains(seen.Single(intent =>
                    intent.Kind == VectorWallDamageIntentKind.SandStormPartExposure).TargetEntityId,
                new[] { "wing", "engine" });
            Assert.DoesNotContain(seen, intent => intent.TargetEntityId == "hull");
        }

        [Fact]
        public void Malformed_inputs_tuning_and_caps_fail_closed()
        {
            Assert.False(VectorWallStormShadow.TryEvaluate(Input(mass: double.NaN),
                Array.Empty<VectorWallSegment>(), new VectorWallStormTuning(), null, null, out _));
            Assert.False(VectorWallStormShadow.TryEvaluate(Input(dt: 0.5),
                Array.Empty<VectorWallSegment>(), new VectorWallStormTuning(), null, null, out _));
            Assert.False(VectorWallStormShadow.TryEvaluate(Input(),
                Array.Empty<VectorWallSegment>(),
                new VectorWallStormTuning(dragExponent: 20), null, null, out _));
            Assert.False(VectorWallStormShadow.TryEvaluate(Input(),
                Enumerable.Range(0, VectorWallStormShadowLimits.MaxWallSegments + 1)
                    .Select(i => new VectorWallSegment(i, VectorWallType.WindRift,
                        V(i, 0, 0), V(i, 0, 100))).ToArray(),
                new VectorWallStormTuning(), null, null, out _));

            VectorWallSegment invalid = new(5, VectorWallType.WindRift,
                V(double.NaN, 0, 0), V(0, 0, 0));
            VectorWallStormShadowResult result = Evaluate(Input(), new[] { invalid },
                new VectorWallStormTuning(),
                new[] { new VectorWallGustPulse(99, VectorWallGustSize.Big, 0, V(0, 0, 0), 0) },
                new[] { new VectorWallDamageTarget("", VectorWallDamageTargetKind.Sail) });
            Assert.Equal(1, result.RejectedSegments);
            Assert.Equal(1, result.RejectedPulses);
            Assert.Equal(1, result.RejectedTargets);
            Assert.True(result.TotalForceLocal.IsFinite);
        }

        [Fact]
        public void Duplicate_wall_and_target_ids_are_not_weighted_or_double_charged()
        {
            VectorWallSegment duplicate = NorthSouthWind with { Second = V(0, 0, 900) };
            var targets = new[]
            {
                new VectorWallDamageTarget("sail", VectorWallDamageTargetKind.Sail),
                new VectorWallDamageTarget("sail", VectorWallDamageTargetKind.Sail),
            };
            VectorWallStormTuning tuning = Tuning((VectorWallType.WindRift,
                Enabled(damage: true, interval: 10, damageFraction: 0.01)));
            int intents = 0;
            int rejectedTargets = -1;
            int rejectedWalls = -1;
            for (long tick = 0; tick < 10; tick++)
            {
                VectorWallStormShadowResult result = Evaluate(Input(tick: tick),
                    new[] { NorthSouthWind, duplicate }, tuning, targets: targets);
                intents += result.DamageIntents.Count;
                rejectedTargets = result.RejectedTargets;
                rejectedWalls = result.RejectedSegments;
            }

            Assert.Equal(1, intents);
            Assert.Equal(1, rejectedTargets);
            Assert.Equal(1, rejectedWalls);
        }

        [Fact]
        public void Maximum_supported_wall_set_is_bounded_and_deterministically_ordered()
        {
            VectorWallSegment[] walls = Enumerable.Range(0, VectorWallStormShadowLimits.MaxWallSegments)
                .Select(i => new VectorWallSegment(i, (VectorWallType)(i % 6),
                    V(i - 64, 0, -1000), V(i - 64, 0, 1000))).Reverse().ToArray();
            VectorWallStormTuning tuning = Tuning(Enum.GetValues<VectorWallType>()
                .Select(type => (type, Enabled(horizontal: 10))).ToArray());

            VectorWallStormShadowResult result = Evaluate(Input(position: V(0, 0, 0)), walls, tuning);

            Assert.Equal(VectorWallStormShadowLimits.MaxWallSegments, result.Samples.Count);
            Assert.Equal(Enumerable.Range(0, VectorWallStormShadowLimits.MaxWallSegments),
                result.Samples.Select(sample => sample.WallId));
            Assert.True(result.TotalForceLocal.IsFinite);
            Assert.True(result.TotalTorqueLocal.IsFinite);
        }
    }
}
