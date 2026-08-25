using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The vector-authority adoption seam: FlightSession.AdvanceAdopted must run
    /// the SAME emission state machine as the scalar integrator path, over a pose
    /// somebody else committed. One pose holder, one cadence, one rest contract.
    /// </summary>
    public sealed class FlightSessionAdoptedTests
    {
        private static readonly FlightTuning Tuning = new FlightTuning();
        private const double EmitStep = 0.24;

        private static FlightState Moving(double z) => new FlightState(
            0, 300, z, 0, 0, 0, 0, 0, 0, 0, 5.0);

        [Fact]
        public void An_adopted_moving_pose_is_emitted_verbatim()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));

            FlightEmit emit = session.AdvanceAdopted(1000, EmitStep, 12, Moving(1.2),
                Tuning, emitDue: true, phaseLockedEmit: true);

            Assert.True(emit.Emit);
            Assert.Equal(1.2, emit.Spec.Z);
            Assert.Equal(5.0, emit.Spec.Vz);
            Assert.False(emit.Spec.Arrived);
            Assert.Equal(1.2, session.State.Z);
        }

        [Fact]
        public void Intermediate_slices_adopt_state_without_emitting()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));

            FlightEmit emit = session.AdvanceAdopted(1000, EmitStep, 5, Moving(0.5),
                Tuning, emitDue: false, phaseLockedEmit: true);

            Assert.False(emit.Emit);
            Assert.Equal(0.5, session.State.Z);
        }

        [Fact]
        public void Zero_steps_adopt_nothing()
        {
            var session = new FlightSession(Moving(7.0));

            session.AdvanceAdopted(1000, EmitStep, 0, FlightState.AtRestAt(0, 0, 0),
                Tuning, emitDue: false);

            Assert.Equal(7.0, session.State.Z);
        }

        [Fact]
        public void A_settled_unmanned_hull_repeats_rest_points_then_goes_silent()
        {
            var session = new FlightSession(Moving(0));
            FlightState rest = FlightState.AtRestAt(0, 300, 10.0);

            int emitted = 0;
            long now = 1000;
            for (int i = 0; i < FlightSession.RestRepeats + 6; i++)
            {
                FlightEmit emit = session.AdvanceAdopted(now, EmitStep, 12, rest,
                    Tuning, emitDue: true, phaseLockedEmit: true);
                if (emit.Emit)
                {
                    emitted++;
                    Assert.True(emit.Spec.Arrived);
                }
                now += 240;
            }

            // Same budget as the scalar path: the settling point plus the finite
            // repeats, then silence.
            Assert.Equal(FlightSession.RestRepeats + 1, emitted);
        }

        [Fact]
        public void A_manned_idle_hull_keeps_the_full_cadence_exactly_like_the_scalar_path()
        {
            var adopted = new FlightSession(FlightState.AtRestAt(0, 300, 0));
            adopted.Man();
            FlightState rest = FlightState.AtRestAt(0, 300, 0);

            long now = 1000;
            for (int i = 0; i < 10; i++)
            {
                FlightEmit emit = adopted.AdvanceAdopted(now, EmitStep, 12, rest,
                    Tuning, emitDue: true, phaseLockedEmit: true);
                Assert.True(emit.Emit);
                now += 240;
            }
        }

        [Fact]
        public void Emission_stamps_stay_phase_locked_and_monotonic()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 300, 0));

            FlightEmit first = session.AdvanceAdopted(1000, EmitStep, 12, Moving(1),
                Tuning, emitDue: true, phaseLockedEmit: true);
            FlightEmit second = session.AdvanceAdopted(5000, EmitStep, 12, Moving(2),
                Tuning, emitDue: true, phaseLockedEmit: true);

            Assert.Equal(first.Spec.TimestampMs + 240, second.Spec.TimestampMs);
        }
    }
}
