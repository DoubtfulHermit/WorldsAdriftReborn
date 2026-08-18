using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The rules that decide how far the operator console may carry a ship past
    /// the last time anyone measured it - and, more importantly, where it must
    /// stop.
    ///
    /// This is the honesty budget of the whole ship overlay. Wildlife is drawn from
    /// a closed form and is therefore exactly right; a ship is drawn from a
    /// measurement that is already seconds old, and the only defensible way to
    /// smooth that is to carry the hull along the velocity the SERVER reported and
    /// to stop before the guess could be larger than the ship.
    /// </summary>
    public class ShipMapMotionTests
    {
        private static ShipMapPose Moving(double vx = 10, double vz = 0, double yawRate = 0) =>
            new ShipMapPose(100, 200, 0.5, vx, vz, yawRate);

        /// <summary>
        /// The window is SOLVED from the error budget, not picked: at the flight
        /// integrator's own default acceleration the bound
        /// <c>0.5*a*t^2 = 20 m</c> gives sqrt(10) seconds. A server tuned to
        /// accelerate harder gets a shorter window, automatically.
        /// </summary>
        [Fact]
        public void The_window_is_solved_from_the_acceleration_limit_and_the_error_budget()
        {
            double window = ShipMapMotion.WindowSecondsFor(FlightTuning.DefaultAccelMps2);

            Assert.Equal(Math.Sqrt(10.0), window, 9);
            Assert.Equal(ShipMapMotion.ToleratedErrorMetres,
                ShipMapMotion.ErrorBoundMetres(FlightTuning.DefaultAccelMps2, window), 9);

            // Twice the acceleration, and the console is allowed to reckon for
            // 1/sqrt(2) as long - the same 20 m, reached sooner.
            Assert.True(ShipMapMotion.WindowSecondsFor(FlightTuning.DefaultAccelMps2 * 2) < window);
        }

        /// <summary>
        /// A pathological or absent tuning must not open the window indefinitely.
        /// Zero acceleration is mathematically a ship that can never deviate, and
        /// a console reckoning a stationary-acceleration hull for an hour would be
        /// drawing a position nobody has confirmed since.
        /// </summary>
        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(0.0001)]
        public void A_nonsense_or_vanishing_acceleration_still_yields_a_bounded_window(double accel)
        {
            double window = ShipMapMotion.WindowSecondsFor(accel);
            Assert.InRange(window, ShipMapMotion.MinWindowSeconds, ShipMapMotion.MaxWindowSeconds);
        }

        /// <summary>Inside the window the hull is carried along the reported velocity, exactly.</summary>
        [Fact]
        public void Inside_the_window_the_hull_is_carried_along_the_reported_velocity()
        {
            ShipMapPose at = ShipMapMotion.PoseAt(Moving(vx: 10, vz: -4, yawRate: 0.2), 2.0, 3.0);

            Assert.Equal(120.0, at.X, 9);
            Assert.Equal(192.0, at.Z, 9);
            Assert.Equal(0.9, at.YawRadians, 9);
        }

        /// <summary>
        /// PAST the window the mark stops. It does not keep gliding on a
        /// measurement nobody has refreshed, and it does not snap back to the last
        /// sample either - it holds the furthest pose the budget allows, which is
        /// where the console then says how old the measurement is.
        /// </summary>
        [Fact]
        public void Past_the_window_the_mark_holds_still_instead_of_gliding_on()
        {
            ShipMapPose measured = Moving(vx: 10);
            ShipMapPose atLimit = ShipMapMotion.PoseAt(measured, 3.0, 3.0);
            ShipMapPose wayPast = ShipMapMotion.PoseAt(measured, 45.0, 3.0);

            Assert.Equal(atLimit.X, wayPast.X, 9);
            Assert.Equal(3.0, ShipMapMotion.Reckoned(45.0, 3.0), 9);
        }

        /// <summary>
        /// A reader with NO model - which is what an older game server leaves the
        /// console holding - reckons nothing and draws the measurement. Zero in,
        /// zero out: the browser mirror has no floor to apply either, and the one
        /// case where the two evaluators should trivially agree must not be the
        /// one where they quietly do not.
        /// </summary>
        [Fact]
        public void With_no_published_window_nothing_is_reckoned_at_all()
        {
            Assert.Equal(0.0, ShipMapMotion.Reckoned(9.0, 0), 9);
            Assert.Equal(100.0, ShipMapMotion.PoseAt(Moving(vx: 10), 9.0, 0).X, 9);
        }

        /// <summary>
        /// A clock that ran backwards between the two hosts draws the measurement,
        /// never a reckoning into the past. The stats bridge crosses a process
        /// boundary and a Wine one; a small negative age is a thing that happens.
        /// </summary>
        [Fact]
        public void A_negative_age_draws_the_measurement_itself()
        {
            ShipMapPose measured = Moving(vx: 10);
            ShipMapPose at = ShipMapMotion.PoseAt(measured, -0.4, 3.0);

            Assert.Equal(measured.X, at.X, 9);
            Assert.Equal(measured.Z, at.Z, 9);
            Assert.Equal(measured.YawRadians, at.YawRadians, 9);
        }

        /// <summary>
        /// A resting hull is drawn where it was MEASURED, with nothing reckoned -
        /// which is most ships most of the time, and the console says so rather
        /// than hedging about every mark equally.
        /// </summary>
        [Fact]
        public void A_hull_at_rest_is_exactly_where_it_was_measured()
        {
            ShipMapPose resting = new ShipMapPose(100, 200, 0.5, 0, 0, 0);

            Assert.True(ShipMapMotion.IsMeasuredExactly(resting));
            Assert.False(ShipMapMotion.IsMeasuredExactly(Moving(vx: 0.0001)));
            Assert.False(ShipMapMotion.IsMeasuredExactly(Moving(vx: 0, yawRate: 0.01)));

            ShipMapPose at = ShipMapMotion.PoseAt(resting, 60.0, 3.0);
            Assert.Equal(100.0, at.X, 9);
            Assert.Equal(200.0, at.Z, 9);
            Assert.Equal(0.5, at.YawRadians, 9);
        }

        /// <summary>
        /// The bound the console prints grows as the square of the reckoned time,
        /// and is zero when nothing has been reckoned. A linear bound would be a
        /// far weaker claim than the integrator actually supports.
        /// </summary>
        [Fact]
        public void The_printed_error_bound_is_the_integrators_own_quadratic()
        {
            Assert.Equal(0.0, ShipMapMotion.ErrorBoundMetres(4.0, 0), 9);
            Assert.Equal(2.0, ShipMapMotion.ErrorBoundMetres(4.0, 1.0), 9);
            Assert.Equal(8.0, ShipMapMotion.ErrorBoundMetres(4.0, 2.0), 9);
            Assert.Equal(18.0, ShipMapMotion.ErrorBoundMetres(4.0, 3.0), 9);
        }
    }
}
