using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class HelmTakeoverInputGateTests
    {
        [Fact]
        public void Held_character_controls_are_suppressed_at_a_neutral_helm()
        {
            var gate = new HelmTakeoverInputGate(FlightControlInput.Neutral);

            HelmTakeoverInputDelta first = gate.Filter(1f, 0.99f);
            HelmTakeoverInputDelta repeated = gate.Filter(1f, 0.99f);

            Assert.Null(first.Throttle);
            Assert.Null(first.Vertical);
            Assert.True(first.SuppressedThrottle);
            Assert.True(first.SuppressedVertical);
            Assert.Null(repeated.Throttle);
            Assert.Null(repeated.Vertical);
        }

        [Fact]
        public void Neutral_edge_rearms_then_the_next_command_is_accepted()
        {
            var gate = new HelmTakeoverInputGate(FlightControlInput.Neutral);
            gate.Filter(1f, -1f);

            HelmTakeoverInputDelta released = gate.Filter(0f, 0.005f);
            HelmTakeoverInputDelta commanded = gate.Filter(0.75f, -0.5f);

            Assert.Equal(0f, released.Throttle);
            Assert.Equal(0f, released.Vertical);
            Assert.False(released.SuppressedThrottle);
            Assert.False(released.SuppressedVertical);
            Assert.Equal(0.75f, commanded.Throttle);
            Assert.Equal(-0.5f, commanded.Vertical);
        }

        [Fact]
        public void Delta_omission_does_not_invent_a_neutral_edge()
        {
            var gate = new HelmTakeoverInputGate(FlightControlInput.Neutral);

            HelmTakeoverInputDelta absent = gate.Filter(null, null);
            HelmTakeoverInputDelta held = gate.Filter(1f, 1f);

            Assert.Null(absent.Throttle);
            Assert.Null(absent.Vertical);
            Assert.Null(held.Throttle);
            Assert.Null(held.Vertical);
        }

        [Fact]
        public void Latched_throttle_is_already_armed_but_climb_still_needs_release()
        {
            var latched = new FlightControlInput(0.6f, 0f, 0f, 0f, 0f);
            var gate = new HelmTakeoverInputGate(latched);

            HelmTakeoverInputDelta takeover = gate.Filter(0.8f, 1f);

            Assert.Equal(0.8f, takeover.Throttle);
            Assert.False(takeover.SuppressedThrottle);
            Assert.Null(takeover.Vertical);
            Assert.True(takeover.SuppressedVertical);
        }

        [Fact]
        public void Throttle_and_vertical_rearm_independently()
        {
            var gate = new HelmTakeoverInputGate(FlightControlInput.Neutral);
            gate.Filter(1f, 1f);

            HelmTakeoverInputDelta throttleReleased = gate.Filter(0f, 1f);
            HelmTakeoverInputDelta independent = gate.Filter(0.5f, 1f);

            Assert.Equal(0f, throttleReleased.Throttle);
            Assert.Null(throttleReleased.Vertical);
            Assert.Equal(0.5f, independent.Throttle);
            Assert.Null(independent.Vertical);
        }
    }
}
