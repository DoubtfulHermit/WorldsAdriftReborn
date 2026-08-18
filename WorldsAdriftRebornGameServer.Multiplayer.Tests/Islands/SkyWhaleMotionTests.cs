using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;
using Xunit.Abstractions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE POSE AND THE CALL: that the animal faces where it is going, that the
    /// whole thing is replayable from the clock, and that the call schedule is the
    /// step function the RECOVERED client rule forces it to be.
    /// </summary>
    public class SkyWhaleMotionTests
    {
        private readonly ITestOutputHelper _output;

        public SkyWhaleMotionTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// THE world's route, over the real preserved catalogue. There is one whale
        /// and one route now, so every test below runs against the same curve the
        /// server flies rather than against one cell of it.
        /// </summary>
        private static SkyWhaleCircuit Wilderness() =>
            SkyWhalePlan.Build(ReleaseWorldRolloutPolicy.Select("tier1"))!.Value.Circuit;

        [Fact]
        public void The_whale_faces_the_way_it_is_travelling()
        {
            // The mantas shipped flying SIDEWAYS for a while because the position
            // was computed and the rotation was left at the client's identity
            // sentinel. Assert the relationship rather than the quaternion: the
            // rotation's own forward axis must be the direction of travel.
            SkyWhaleCircuit circuit = Wilderness();
            for (int step = 0; step < 64; step++)
            {
                double t = step * (circuit.CircuitSeconds / 64.0);
                FaunaTransform pose = SkyWhaleMotion.WorldTransformAt(circuit, t);
                (double fx, double fy, double fz) =
                    IslandFaunaOrientation.ForwardOf(pose.Rotation);
                (double tx, double ty, double tz) = circuit.TangentAtTime(t);
                double length = Math.Sqrt((tx * tx) + (ty * ty) + (tz * tz));
                double dot = ((tx / length) * fx) + ((ty / length) * fy) + ((tz / length) * fz);
                Assert.Equal(1.0, dot, 5);
            }
        }

        [Fact]
        public void The_rotation_is_a_unit_quaternion_at_every_instant()
        {
            SkyWhaleCircuit circuit = Wilderness();
            for (int step = 0; step < 200; step++)
            {
                FaunaRotation r = SkyWhaleMotion.WorldTransformAt(
                    circuit, step * 7.3).Rotation;
                double norm = (r.W * r.W) + (r.X * r.X) + (r.Y * r.Y) + (r.Z * r.Z);
                Assert.Equal(1.0, norm, 5);
                Assert.False(double.IsNaN(norm));
            }
        }

        [Fact]
        public void A_restarted_server_replays_the_identical_path()
        {
            // The property the whole closed-form design exists for: nothing is
            // integrated, accumulated or remembered, so the same elapsed second
            // gives the same pose in a fresh process. A month of uptime is included
            // because an error in a divisor of elapsed seconds is multiplied by how
            // long the server has been up.
            SkyWhaleCircuit first = Wilderness();
            SkyWhaleCircuit second = Wilderness();
            foreach (double t in new[] { 0.0, 1.0, 617.25, 3_600.0, 86_400.0, 2_592_000.0 })
            {
                Assert.Equal(SkyWhaleMotion.WorldTransformAt(first, t),
                    SkyWhaleMotion.WorldTransformAt(second, t));
            }
        }

        [Fact]
        public void An_unwatched_whale_is_exactly_where_it_would_have_been()
        {
            // Skipping the computation while nobody is looking cannot make the
            // animal drift, because the pose is a function of absolute elapsed time
            // rather than of how often it was asked for.
            SkyWhaleCircuit circuit = Wilderness();
            FaunaTransform straightTo = SkyWhaleMotion.WorldTransformAt(circuit, 5_000.0);
            for (double t = 0.0; t < 5_000.0; t += 250.0)
            {
                SkyWhaleMotion.WorldTransformAt(circuit, t);
            }
            Assert.Equal(straightTo, SkyWhaleMotion.WorldTransformAt(circuit, 5_000.0));
        }

        [Fact]
        public void The_call_index_is_a_step_function_of_the_clock()
        {
            // It has to be a step function: BigCallVisualiser refuses any coords
            // update more than a metre from where it already is (RECOVERED), so a
            // call cannot be slid along - it is an EVENT at a fixed place, and the
            // index is the only thing the service compares.
            SkyWhaleCircuit circuit = Wilderness();
            double interval = SkyWhalePolicy.CallIntervalSeconds;

            Assert.Equal(0L, SkyWhaleMotion.CallAt(circuit, 0.0).Index);
            Assert.Equal(0L, SkyWhaleMotion.CallAt(circuit, interval - 0.001).Index);
            Assert.Equal(1L, SkyWhaleMotion.CallAt(circuit, interval).Index);
            Assert.Equal(30L, SkyWhaleMotion.CallAt(circuit, (interval * 30.0) + 1.0).Index);
        }

        [Fact]
        public void A_calls_station_is_where_the_whale_was_when_it_began()
        {
            SkyWhaleCircuit circuit = Wilderness();
            double interval = SkyWhalePolicy.CallIntervalSeconds;
            for (long index = 0; index < 12; index++)
            {
                SkyWhaleCall call = SkyWhaleMotion.CallAt(circuit, (index * interval) + 17.0);
                Assert.Equal(index, call.Index);
                Assert.Equal(SkyWhaleMotion.WorldPositionAt(circuit, index * interval),
                    call.Position);
            }
        }

        [Fact]
        public void Successive_calls_audibly_approach_rather_than_repeating_in_place()
        {
            // The reason the interval is 120 s rather than the cut client's own
            // 25-45 s: a listener inside the call radius should get a SEQUENCE that
            // moves. At the tuned speed each call lands roughly two kilometres
            // further along the path than the last - never on top of it, and never
            // so far that the two are unrelated events.
            SkyWhaleCircuit circuit = Wilderness();
            double interval = SkyWhalePolicy.CallIntervalSeconds;
            for (long index = 0; index < 8; index++)
            {
                double gap = Math.Sqrt(SkyWhaleMotion.DistanceSquared(
                    SkyWhaleMotion.CallAt(circuit, index * interval).Position,
                    SkyWhaleMotion.CallAt(circuit, (index + 1) * interval).Position));
                _output.WriteLine("call " + index + " -> " + (index + 1) + ": "
                    + gap.ToString("0") + " m");
                Assert.InRange(gap, 500.0, 2.0 * interval * SkyWhalePolicy.MetresPerSecond);
            }
        }

        [Fact]
        public void The_uniform_parameterisation_costs_a_bounded_amount_of_speed_variation()
        {
            // THE STATED COST of choosing uniform rather than centripetal
            // Catmull-Rom, measured rather than hand-waved. SkyWhalePolicy's
            // 18 m/s is an AVERAGE over the lap; the instantaneous speed is faster
            // across the long open legs and slower through a cluster of islands.
            // That is a feature - an animal that dawdles among the rocks - but it
            // must stay inside a band a player would read as one creature, and a
            // future retuning that widened it to, say, five times would be a bug
            // this pins.
            SkyWhaleCircuit circuit = Wilderness();
            double slowest = double.MaxValue, fastest = 0.0;
            const double Step = 0.5;
            for (double t = 0.0; t < circuit.CircuitSeconds; t += Step)
            {
                double moved = Math.Sqrt(SkyWhaleMotion.DistanceSquared(
                    SkyWhaleMotion.WorldPositionAt(circuit, t),
                    SkyWhaleMotion.WorldPositionAt(circuit, t + Step))) / Step;
                slowest = Math.Min(slowest, moved);
                fastest = Math.Max(fastest, moved);
            }
            _output.WriteLine("instantaneous speed " + slowest.ToString("0.0") + " - "
                + fastest.ToString("0.0") + " m/s about a "
                + SkyWhalePolicy.MetresPerSecond.ToString("0") + " m/s average");
            Assert.InRange(slowest, 4.0, SkyWhalePolicy.MetresPerSecond);
            Assert.InRange(fastest, SkyWhalePolicy.MetresPerSecond,
                SkyWhalePolicy.MetresPerSecond * 3.0);
        }

        [Fact]
        public void The_call_is_heard_well_before_the_animal_is_visible()
        {
            // THE FEATURE, as a number rather than as a claim. "Hear it before you
            // see it" is the ratio between the two radii, and at the tuned speed it
            // is worth this many seconds of warning on a head-on approach.
            double lead = (SkyWhalePolicy.DefaultCallRadiusMetres
                - SkyWhalePolicy.DefaultLoadRadiusMetres) / SkyWhalePolicy.MetresPerSecond;
            _output.WriteLine("warning before the whale checks out: "
                + lead.ToString("0") + " s");
            Assert.True(SkyWhalePolicy.DefaultCallRadiusMetres
                > SkyWhalePolicy.DefaultLoadRadiusMetres * 2.0,
                "the call radius must exceed the visual radius or the feature is only packets");
            Assert.InRange(lead, 60.0, 600.0);
        }

        [Fact]
        public void The_pose_is_encoded_the_way_the_client_decodes_it()
        {
            // FixedPointPosition.FromMetres truncates toward zero at 4096 units per
            // metre, exactly as the client does. A whale is 173 m long, so a quarter
            // of a millimetre is not the issue; matching the client's arithmetic is.
            SkyWhaleCircuit circuit = Wilderness();
            (double x, double y, double z) = circuit.PositionAtTime(1234.5);
            Assert.Equal(FixedPointPosition.FromMetres(x, y, z),
                SkyWhaleMotion.WorldPositionAt(circuit, 1234.5));
        }
    }
}
