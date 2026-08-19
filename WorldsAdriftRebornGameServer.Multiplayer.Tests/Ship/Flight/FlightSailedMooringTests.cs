using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// GHOST SHIPS. Making sails a real wind force has a consequence that the
    /// old throttle-multiplier model could not have: canvas pushes whether or not
    /// anybody is aboard. Retail lived with exactly that - abandoned ships drifted,
    /// and retail answered it separately, with `ShipAbandonedBehaviour` sinking
    /// them. We have no equivalent.
    ///
    /// So the question these pin is: what stops a moored ship with its sails up
    /// from quietly sailing out of the world overnight, and taking a per-hull
    /// control-point stream with it? The answer is the session's own liveness gate,
    /// and since that gate was written for a different reason entirely, it is
    /// asserted here before somebody "simplifies" it.
    /// </summary>
    public class FlightSailedMooringTests
    {
        private const double Step = ShipMotionPolicy.SendIntervalSeconds;
        private const long StepMs = 240;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static ShipPropulsion Sailed => new ShipPropulsion(800.0, 0.0, 4);

        [Fact]
        public void A_moored_unmanned_ship_does_not_sail_itself_away()
        {
            // At rest, nobody at the helm, lever centred - four sails or not, the
            // hull must still be exactly where it was left. The session never
            // integrates an at-rest unmanned ship, which is what makes a real wind
            // force safe to add at all.
            FlightSession session = new FlightSession(FlightState.AtRestAt(10, 100, -20));
            long nowMs = 0;

            for (int i = 0; i < 2000; i++)   // ~8 minutes of ticks
            {
                nowMs += StepMs;
                session.Advance(nowMs, Step, Tuning, unfurledSails: 4, agilityScale: 1.0,
                    propulsion: Sailed);
            }

            Assert.Equal(10.0, session.State.X, 9);
            Assert.Equal(100.0, session.State.Y, 9);
            Assert.Equal(-20.0, session.State.Z, 9);
            Assert.True(session.State.IsAtRest);
        }

        [Fact]
        public void A_manned_ship_under_sail_does_get_under_way_with_the_lever_centred()
        {
            // The other half of the same gate: being AT THE HELM is enough. This is
            // the behaviour the maintainer described from retail, and it must not be
            // suppressed by the mooring guard above.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            long nowMs = 0;

            for (int i = 0; i < 600; i++)
            {
                nowMs += StepMs;
                session.Advance(nowMs, Step, Tuning, unfurledSails: 2, agilityScale: 1.0,
                    propulsion: new ShipPropulsion(800.0, 0.0, 2));
            }

            Assert.False(session.State.IsAtRest);
            Assert.True(session.State.SpeedCmdMps > 0.0,
                "a manned ship with canvas up never got moving: " + session.State.SpeedCmdMps);
        }

        [Fact]
        public void A_ship_already_moving_keeps_sailing_after_the_pilot_leaves()
        {
            // Documented rather than prevented: this IS retail's behaviour, and it
            // is the ghost-ship case. It is pinned so that the day somebody adds an
            // abandoned-ship rule they find this test and change it deliberately,
            // rather than discovering the interaction in production.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            long nowMs = 0;
            for (int i = 0; i < 300; i++)
            {
                nowMs += StepMs;
                session.Advance(nowMs, Step, Tuning, 2, 1.0, new ShipPropulsion(800.0, 0.0, 2));
            }
            double zAtRelease = session.State.Z;

            session.Dismount();
            for (int i = 0; i < 300; i++)
            {
                nowMs += StepMs;
                session.Advance(nowMs, Step, Tuning, 2, 1.0, new ShipPropulsion(800.0, 0.0, 2));
            }

            Assert.True(session.State.Z > zAtRelease,
                "the abandoned ship stopped dead, which is not what the wind does");
        }
    }
}
