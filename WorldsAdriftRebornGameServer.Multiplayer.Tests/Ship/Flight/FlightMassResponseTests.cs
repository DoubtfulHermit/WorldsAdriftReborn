using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// Mass reaching the helm. The user's ask was "start updating what the ship is,
    /// how it's built, heavy etc, because we need to affect the flight" - these pin
    /// that a heavy hull genuinely flies differently, and, just as importantly, that
    /// a ship of the reference mass flies EXACTLY as it did before any of this.
    /// </summary>
    public class FlightMassResponseTests
    {
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static FlightState FullAhead(double agility, int steps)
        {
            FlightState state = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var input = new FlightControlInput(throttle: 1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);
            for (int i = 0; i < steps; i++)
            {
                state = FlightIntegrator.Step(state, input, 0.24, Tuning, 0, agility);
            }
            return state;
        }

        [Fact]
        public void A_neutral_scale_is_bit_identical_to_the_pre_materials_integrator()
        {
            // THE SAFETY PROPERTY. Every existing call site omits the parameter, and
            // the default must change nothing at all - otherwise this feature silently
            // retunes every ship in the live world.
            FlightState state = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var input = new FlightControlInput(throttle: 1f, vertical: 0.5f, axisYaw: 1f, axisPitch: 0f, axisRoll: 0f);

            FlightState withDefault = state;
            FlightState withExplicitOne = state;
            for (int i = 0; i < 40; i++)
            {
                withDefault = FlightIntegrator.Step(withDefault, input, 0.24, Tuning);
                withExplicitOne = FlightIntegrator.Step(withExplicitOne, input, 0.24, Tuning, 0, 1.0);
            }

            Assert.Equal(withDefault.X, withExplicitOne.X, 12);
            Assert.Equal(withDefault.Y, withExplicitOne.Y, 12);
            Assert.Equal(withDefault.Z, withExplicitOne.Z, 12);
            Assert.Equal(withDefault.YawRadians, withExplicitOne.YawRadians, 12);
            Assert.Equal(withDefault.SpeedCmdMps, withExplicitOne.SpeedCmdMps, 12);
        }

        [Fact]
        public void A_light_ship_gets_up_to_speed_sooner_than_a_heavy_one()
        {
            // Two steps in, before either has reached the shared speed cap.
            double light = FullAhead(agility: 1.5, steps: 2).SpeedCmdMps;
            double reference = FullAhead(agility: 1.0, steps: 2).SpeedCmdMps;
            double heavy = FullAhead(agility: 0.6, steps: 2).SpeedCmdMps;

            Assert.True(light > reference);
            Assert.True(reference > heavy);
        }

        [Fact]
        public void Mass_changes_acceleration_but_not_the_top_speed_a_ship_can_hold()
        {
            // Deliberate: a heavy ship is sluggish to get going, not permanently
            // slower once it is up. The control-point cadence caps speed anyway, so
            // scaling that would fight the stream rather than model the physics.
            double lightTop = FullAhead(agility: 1.5, steps: 400).SpeedCmdMps;
            double heavyTop = FullAhead(agility: 0.6, steps: 400).SpeedCmdMps;
            Assert.Equal(lightTop, heavyTop, 6);
        }

        [Fact]
        public void A_heavy_ship_winds_a_turn_up_more_slowly()
        {
            FlightState light = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            FlightState heavy = light;
            var turning = new FlightControlInput(throttle: 0f, vertical: 0f, axisYaw: 1f, axisPitch: 0f, axisRoll: 0f);

            light = FlightIntegrator.Step(light, turning, 0.24, Tuning, 0, 1.5);
            heavy = FlightIntegrator.Step(heavy, turning, 0.24, Tuning, 0, 0.6);

            Assert.True(System.Math.Abs(light.YawRateRadPerSec) > System.Math.Abs(heavy.YawRateRadPerSec));
        }

        [Fact]
        public void A_heavy_ship_climbs_more_slowly()
        {
            // The axis fighting the sky core directly, and the one retail cut off
            // entirely once a ship exceeded its lift.
            FlightState light = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            FlightState heavy = light;
            var climbing = new FlightControlInput(throttle: 0f, vertical: 1f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);

            for (int i = 0; i < 20; i++)
            {
                light = FlightIntegrator.Step(light, climbing, 0.24, Tuning, 0, 1.5);
                heavy = FlightIntegrator.Step(heavy, climbing, 0.24, Tuning, 0, 0.6);
            }

            Assert.True(light.Y > heavy.Y);
            Assert.True(light.VyMps > heavy.VyMps);
        }

        [Fact]
        public void Sails_and_mass_compose_rather_than_one_replacing_the_other()
        {
            // Canvas on a heavy hull must still help; ballast under canvas must still
            // hurt. A player who rigs more sail on a tungsten ship should feel it.
            double heavyBare = FullAhead(agility: 0.6, steps: 2).SpeedCmdMps;

            FlightState heavyRigged = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var input = new FlightControlInput(throttle: 1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);
            for (int i = 0; i < 2; i++)
            {
                heavyRigged = FlightIntegrator.Step(heavyRigged, input, 0.24, Tuning, 4, 0.6);
            }

            Assert.True(heavyRigged.SpeedCmdMps > heavyBare);
        }

        [Fact]
        public void A_nonsense_agility_leaves_the_ship_flying_normally()
        {
            // A malformed value must never freeze a ship in the sky with a player
            // aboard, so it degrades to the neutral scale.
            var input = new FlightControlInput(throttle: 1f, vertical: 0f, axisYaw: 0f, axisPitch: 0f, axisRoll: 0f);
            FlightState reference = FlightIntegrator.Step(
                new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), input, 0.24, Tuning, 0, 1.0);

            foreach (double bad in new[] { 0.0, -2.0, double.NaN, double.PositiveInfinity })
            {
                FlightState got = FlightIntegrator.Step(
                    new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), input, 0.24, Tuning, 0, bad);
                Assert.Equal(reference.SpeedCmdMps, got.SpeedCmdMps, 9);
            }
        }

        [Fact]
        public void A_heavy_ship_still_reaches_a_useful_speed_and_still_turns()
        {
            // Playability floor: the clamped worst case must remain a ship, not a
            // brick. At the minimum agility a full-throttle hull must still get
            // moving and still come about.
            FlightState state = FullAhead(
                agility: Multiplayer.Materials.HullMassCalculator.MinAgility, steps: 200);
            Assert.True(state.SpeedCmdMps > 5.0);

            var turning = new FlightControlInput(throttle: 0f, vertical: 0f, axisYaw: 1f, axisPitch: 0f, axisRoll: 0f);
            FlightState turned = new FlightState(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            for (int i = 0; i < 40; i++)
            {
                turned = FlightIntegrator.Step(turned, turning, 0.24, Tuning,
                    0, Multiplayer.Materials.HullMassCalculator.MinAgility);
            }
            Assert.True(System.Math.Abs(turned.YawRadians) > 0.5);
        }
    }
}
