using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The wind field: what it is at a place and a moment, and - the half that
    /// matters most - that it is EXACTLY the old constant when nobody has asked
    /// for anything else.
    ///
    /// The recurring hazard this file is written against is that the client draws
    /// a wind we cannot change (the (1,0,-2) fallback, rendered as wind streaks,
    /// foliage sway and flags) while the server owns the wind a ship feels. Every
    /// test below either pins the agreement between those two, or pins the size of
    /// a deliberate disagreement.
    /// </summary>
    public class WindFieldTests
    {
        private static readonly double PublishedX = ShipForceModel.DefaultWindX;
        private static readonly double PublishedZ = ShipForceModel.DefaultWindZ;
        private static readonly double PublishedSpeed = ShipForceModel.DefaultWindSpeedMps;

        // ------------------------------------------------------------------
        // OFF means OFF.
        // ------------------------------------------------------------------

        [Fact]
        public void With_the_field_off_the_wind_is_EXACTLY_the_vector_the_client_draws()
        {
            // Equality, not approximate equality, and deliberately so: this is the
            // arithmetic FlightIntegrator did before WindField existed, and if a
            // refactor moves it by one ulp a live tuning comparison stops being
            // reproducible. If this ever has to become Assert.Equal(.., precision)
            // that is a finding, not a maintenance chore.
            WindSample wind = WindField.SampleAt(
                1234.5, -6789.0, 987.6, PublishedSpeed, WindFieldVariation.None);

            Assert.Equal(PublishedX, wind.WindX);
            Assert.Equal(PublishedZ, wind.WindZ);
        }

        [Fact]
        public void With_the_field_off_the_wind_is_the_same_everywhere_and_at_every_moment()
        {
            WindSample a = WindField.SampleAt(0.0, 0.0, 0.0, 4.0, WindFieldVariation.None);
            WindSample b = WindField.SampleAt(15_000.0, -12_000.0, 86_400.0, 4.0, WindFieldVariation.None);

            Assert.Equal(a.WindX, b.WindX);
            Assert.Equal(a.WindZ, b.WindZ);
            Assert.Equal(4.0, b.SpeedMps, 12);
        }

        [Fact]
        public void The_published_bearing_is_the_clients_own_fallback_vector()
        {
            // The bearing every wind streak, blade of grass and flag on every
            // player's screen is drawn along. If this moves, the map arrows and
            // the ship physics both start lying about the same thing.
            Assert.Equal(
                Math.Atan2(1.0, -2.0), WindField.PublishedBearingRadians, 12);

            WindSample wind = WindField.SampleAt(0.0, 0.0, 0.0, PublishedSpeed);
            Assert.Equal(WindField.PublishedBearingRadians, wind.BearingRadians, 12);
        }

        [Fact]
        public void The_wind_knob_sets_the_MEAN_speed_and_leaves_the_bearing_alone()
        {
            WindSample slow = WindField.SampleAt(0.0, 0.0, 0.0, 2.0);
            WindSample fast = WindField.SampleAt(0.0, 0.0, 0.0, 8.0);

            Assert.Equal(2.0, slow.SpeedMps, 9);
            Assert.Equal(8.0, fast.SpeedMps, 9);
            Assert.Equal(slow.BearingRadians, fast.BearingRadians, 12);
        }

        // ------------------------------------------------------------------
        // Malformed input must leave a ship flying, never NaN it, never becalm it
        // by accident. Same contract as the rest of the flight model.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        public void A_calm_or_malformed_world_has_no_wind_rather_than_a_broken_one(double speed)
        {
            WindSample wind = WindField.SampleAt(0.0, 0.0, 0.0, speed);

            Assert.Equal(0.0, wind.SpeedMps);
            Assert.Equal(0.0, wind.WindX);
            Assert.Equal(0.0, wind.WindZ);
        }

        [Theory]
        [InlineData(double.NaN, 0.0)]
        [InlineData(0.0, double.PositiveInfinity)]
        public void A_malformed_pose_still_gets_the_worlds_mean_wind(double x, double z)
        {
            WindSample wind = WindField.SampleAt(x, z, 0.0, 4.0, new WindFieldVariation(1.0));

            Assert.Equal(4.0, wind.SpeedMps, 9);
            Assert.False(double.IsNaN(wind.WindX));
            Assert.False(double.IsNaN(wind.WindZ));
        }

        [Fact]
        public void A_wind_above_the_clients_own_ceiling_becalms_rather_than_launching_every_hull()
        {
            // PROVED - GlobalWeather.GetWindAt returns Vector3.zero above 100 m/s
            // rather than a stronger wind. Reproduced so a runaway tuning value
            // parks the fleet instead of firing it out of the world.
            Assert.Equal(0.0, WindSample.FromComponents(0.0, 200.0, 0.0).SpeedMps);
            Assert.Equal(100.0, WindSample.FromComponents(0.0, 100.0, 0.0).SpeedMps, 9);
        }

        // ------------------------------------------------------------------
        // The field itself.
        // ------------------------------------------------------------------

        [Fact]
        public void With_the_field_on_the_wind_differs_from_place_to_place()
        {
            var field = new WindFieldVariation(1.0);
            WindSample here = WindField.SampleAt(0.0, 0.0, 0.0, 4.0, field);
            WindSample overThere = WindField.SampleAt(
                WindFieldVariation.CellMetres * 0.25, 0.0, 0.0, 4.0, field);

            // A QUARTER cell, not a half: the veer term is a sine of the cell
            // fraction, so two points half a cell apart both sit on a zero
            // crossing and read identically. That is a property of the model, not
            // a coincidence, and choosing the half here would have made this test
            // pass for the wrong reason on the day the field was deleted.
            Assert.True(
                Math.Abs(here.BearingRadians - overThere.BearingRadians) > 0.05,
                "the wind at two points half a cell apart should not be the same wind");
        }

        [Fact]
        public void With_the_field_on_the_wind_at_one_place_changes_over_time()
        {
            var field = new WindFieldVariation(1.0);
            WindSample now = WindField.SampleAt(0.0, 0.0, 0.0, 4.0, field);
            WindSample later = WindField.SampleAt(
                0.0, 0.0, WindFieldVariation.PeriodSeconds * 0.25, 4.0, field);

            Assert.True(
                Math.Abs(now.BearingRadians - later.BearingRadians) > 0.05,
                "a quarter of the period later the wind should have moved");
        }

        [Fact]
        public void The_field_never_strays_further_from_the_drawn_wind_than_the_tuned_budget()
        {
            // THE LOAD-BEARING TEST. The client draws (1,0,-2) and always will.
            // The whole justification for varying the wind at all is that the
            // divergence stays inside a budget a player can still steer by, so
            // that budget is swept rather than asserted at one convenient point.
            var field = new WindFieldVariation(1.0);
            double worstVeer = 0.0;
            double worstGust = 0.0;
            for (int i = 0; i <= 90; i++)
            {
                for (int j = 0; j <= 90; j++)
                {
                    double x = -18_000.0 + (i * 400.0);
                    double z = -18_000.0 + (j * 400.0);
                    double t = ((i * 91) + j) * 7.3;
                    WindSample wind = WindField.SampleAt(x, z, t, 4.0, field);

                    double veer = Math.Abs(
                        WrapAngle(wind.BearingRadians - WindField.PublishedBearingRadians));
                    worstVeer = Math.Max(worstVeer, veer);
                    worstGust = Math.Max(worstGust, Math.Abs((wind.SpeedMps / 4.0) - 1.0));
                }
            }

            Assert.True(
                worstVeer <= WindFieldVariation.MaxVeerRadians + 1e-9,
                $"worst veer {worstVeer * 180.0 / Math.PI:0.0} deg exceeds the tuned budget");
            Assert.True(
                worstGust <= WindFieldVariation.MaxGustFraction + 1e-9,
                $"worst gust {worstGust:0.000} exceeds the tuned budget");

            // ...and the budget must actually be APPROACHED, or "bounded" is being
            // proved by a field that does nothing.
            Assert.True(worstVeer > WindFieldVariation.MaxVeerRadians * 0.8);
            Assert.True(worstGust > WindFieldVariation.MaxGustFraction * 0.8);
        }

        [Fact]
        public void The_field_scale_is_a_dial_and_not_a_switch()
        {
            double Excursion(double scale)
            {
                double worst = 0.0;
                var field = new WindFieldVariation(scale);
                for (int i = 0; i < 400; i++)
                {
                    WindSample wind = WindField.SampleAt(i * 137.0, i * 311.0, i * 3.1, 4.0, field);
                    worst = Math.Max(worst, Math.Abs(
                        WrapAngle(wind.BearingRadians - WindField.PublishedBearingRadians)));
                }
                return worst;
            }

            Assert.Equal(0.0, Excursion(0.0), 12);
            Assert.True(Excursion(0.25) < Excursion(0.5));
            Assert.True(Excursion(0.5) < Excursion(1.0));
        }

        [Theory]
        [InlineData(-1.0, 0.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 1.0)]
        [InlineData(9.0, 1.0)]
        [InlineData(double.NaN, 0.0)]
        public void The_field_scale_clamps_rather_than_throwing(double given, double expected)
        {
            // It arrives from an environment variable, so rubbish must degrade to
            // a defensible world rather than take the server down.
            Assert.Equal(expected, new WindFieldVariation(given).Scale, 12);
        }

        [Fact]
        public void The_field_is_a_pure_function_so_the_map_can_draw_the_real_thing()
        {
            // The admin map re-evaluates this same closed form in the browser from
            // a clock alone. That is only honest if the server's answer depends on
            // nothing but position and time.
            var field = new WindFieldVariation(1.0);
            WindSample first = WindField.SampleAt(4321.0, -876.0, 543.21, 4.0, field);
            WindSample second = WindField.SampleAt(4321.0, -876.0, 543.21, 4.0, field);

            Assert.Equal(first.WindX, second.WindX);
            Assert.Equal(first.WindZ, second.WindZ);
        }

        // ------------------------------------------------------------------
        // Heading. This is what makes wind a system rather than a speed number.
        // ------------------------------------------------------------------

        [Fact]
        public void A_ship_pointing_downwind_is_carried_at_the_full_wind_speed()
        {
            WindSample wind = WindField.SampleAt(0.0, 0.0, 0.0, 4.0);
            double along = WindField.AlongHeading(in wind, wind.BearingRadians);

            Assert.Equal(4.0, along, 9);
        }

        [Fact]
        public void A_ship_pointing_into_the_wind_is_carried_not_at_all()
        {
            // WIKI, from the wiki's own sailing guide: "If the wind is directly in
            // front of you, your movement will be negligible." Floored at zero
            // rather than negative - see WindField.AlongHeading for why we depart
            // from retail's relative-wind term here.
            WindSample wind = WindField.SampleAt(0.0, 0.0, 0.0, 4.0);
            double along = WindField.AlongHeading(in wind, wind.BearingRadians + Math.PI);

            Assert.Equal(0.0, along);
        }

        [Fact]
        public void A_ship_across_the_wind_gets_part_of_it()
        {
            WindSample wind = WindField.SampleAt(0.0, 0.0, 0.0, 4.0);
            double beamOn = WindField.AlongHeading(in wind, wind.BearingRadians + (Math.PI / 2.0));
            double quartering = WindField.AlongHeading(in wind, wind.BearingRadians + (Math.PI / 4.0));

            Assert.Equal(0.0, beamOn, 9);
            Assert.Equal(4.0 * Math.Cos(Math.PI / 4.0), quartering, 9);
        }

        [Fact]
        public void A_dead_calm_carries_nobody_whatever_they_are_pointing_at()
        {
            Assert.Equal(0.0, WindField.AlongHeading(in WindSample.Calm, 1.23));
        }

        [Fact]
        public void Signed_projection_preserves_the_headwind_a_wall_needs_to_resist_crossing()
        {
            WindSample west = WindSample.FromComponents(-10.0, 0.0, 1.0);

            Assert.Equal(-10.0, WindField.SignedAlongHeading(in west, Math.PI / 2.0), 9);
            Assert.Equal(0.0, WindField.AlongHeading(in west, Math.PI / 2.0), 9);
        }

        // ------------------------------------------------------------------
        // Weather walls: the ONE source of wind variation the client would also
        // draw, and it needs no weather cell. Recovered geometry, pinned.
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0.0, 1.0)]        // on the line
        [InlineData(199.0, 1.0)]      // inside the full-strength core
        [InlineData(300.0, 0.5)]      // halfway down the ramp
        [InlineData(400.0, 0.0)]      // the outer edge
        [InlineData(401.0, 0.0)]      // beyond it
        [InlineData(5000.0, 0.0)]
        public void A_walls_reach_is_the_recovered_two_hundred_then_four_hundred_metres(
            double metres, double expected)
        {
            // PROVED - WallData.GetIntensityAt: 1 inside 200 m, linear to 0 at
            // 400 m. These two distances are the whole reason a wall is a LOCAL
            // feature rather than a world-wide wind, which is the honest limit on
            // "wind walls give you variation without the lattice".
            Assert.Equal(
                expected,
                WeatherWallSegment.IntensityAtSqrDistance(metres * metres),
                6);
        }

        [Fact]
        public void A_wall_replaces_the_ambient_wind_inside_its_core()
        {
            // A north-south Wind Rift down x = 0, blowing 10 m/s off its own line.
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -1000.0, 0.0, 1000.0, WeatherWallType.WindRift, 10.0),
            };

            WindSample inside = WindField.SampleAt(50.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);

            Assert.Equal(1.0, inside.WallIntensity, 6);
            // Perpendicular, away from the line: the point is at +x, so is the wind.
            Assert.Equal(10.0, inside.WindX, 6);
            Assert.Equal(0.0, inside.WindZ, 6);
        }

        [Fact]
        public void Far_from_every_wall_the_wind_is_untouched()
        {
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -1000.0, 0.0, 1000.0, WeatherWallType.WindRift, 10.0),
            };

            WindSample open = WindField.SampleAt(9000.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);
            WindSample noWalls = WindField.SampleAt(9000.0, 0.0, 0.0, 4.0);

            Assert.Equal(0.0, open.WallIntensity);
            Assert.Equal(noWalls.WindX, open.WindX);
            Assert.Equal(noWalls.WindZ, open.WindZ);
        }

        [Fact]
        public void A_wall_serving_no_multiplier_makes_the_windiest_place_in_the_world_dead_calm()
        {
            // ⚠ THE TRAP, pinned as behaviour so nobody serves 1204 alone and
            // discovers it live. Every wall wind in the client is scaled by a
            // GlobalWeatherDataVisualizer.*WindMultiplier static, and those are 0f
            // until 1229 GlobalWallDataState is served with a COMPLETE FloatValues
            // map. Lerp(ambient, zero, 1) is zero: the streaks stop, the grass
            // stops and the sails empty, inside the walls that are meant to be the
            // strongest wind in the world.
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -1000.0, 0.0, 1000.0, WeatherWallType.WindRift, 0.0),
            };

            WindSample inside = WindField.SampleAt(50.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);

            Assert.Equal(0.0, inside.SpeedMps, 9);
        }

        [Fact]
        public void The_ramp_blends_rather_than_stepping_at_a_walls_edge()
        {
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -5000.0, 0.0, 5000.0, WeatherWallType.WindRift, 10.0),
            };

            double previous = double.NaN;
            for (double x = 180.0; x <= 420.0; x += 10.0)
            {
                WindSample wind = WindField.SampleAt(x, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);
                if (!double.IsNaN(previous))
                {
                    // No step bigger than the whole wall wind over one 10 m stride.
                    Assert.True(
                        Math.Abs(wind.SpeedMps - previous) < 1.0,
                        $"wind stepped by {Math.Abs(wind.SpeedMps - previous):0.00} m/s at x={x}");
                }
                previous = wind.SpeedMps;
            }
        }

        [Fact]
        public void A_storm_wall_blows_along_itself_rather_than_off_it()
        {
            // PROVED - WallData.GetWindUnscaled: only a WindRift is perpendicular;
            // StormRift, SandStorm and WorldEndWall all blow along Forward.
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -1000.0, 0.0, 1000.0, WeatherWallType.StormRift, 10.0),
            };

            WindSample inside = WindField.SampleAt(50.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);

            Assert.Equal(0.0, inside.WindX, 6);
            Assert.Equal(10.0, inside.WindZ, 6);
        }

        [Fact]
        public void The_nearest_wall_wins_rather_than_the_strongest_or_the_sum()
        {
            // PROVED - WeatherWalls.GetWallWindAt keeps the smallest SqrDist among
            // walls with any intensity. Two overlapping walls must not add up to a
            // wind neither of them has.
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, -1000.0, 0.0, 1000.0, WeatherWallType.StormRift, 30.0),
                new WeatherWallSegment(60.0, -1000.0, 60.0, 1000.0, WeatherWallType.StormRift, 10.0),
            };

            // x = 55 is 5 m from the second wall and 55 m from the first.
            WindSample wind = WindField.SampleAt(55.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);

            Assert.Equal(10.0, wind.SpeedMps, 6);
        }

        [Fact]
        public void An_empty_wall_list_is_the_production_case_and_costs_nothing()
        {
            WindSample withEmpty = WindField.SampleAt(
                100.0, 200.0, 300.0, 4.0, WindFieldVariation.None, new List<WeatherWallSegment>());
            WindSample withNull = WindField.SampleAt(100.0, 200.0, 300.0, 4.0);

            Assert.Equal(withNull.WindX, withEmpty.WindX);
            Assert.Equal(withNull.WindZ, withEmpty.WindZ);
        }

        [Fact]
        public void A_degenerate_zero_length_wall_does_not_divide_by_zero()
        {
            var walls = new List<WeatherWallSegment>
            {
                new WeatherWallSegment(0.0, 0.0, 0.0, 0.0, WeatherWallType.WindRift, 10.0),
            };

            WindSample wind = WindField.SampleAt(10.0, 0.0, 0.0, 4.0, WindFieldVariation.None, walls);

            Assert.False(double.IsNaN(wind.WindX));
            Assert.False(double.IsNaN(wind.WindZ));
        }

        [Fact]
        public void The_veer_rotates_the_way_it_says_it_does()
        {
            // MUTATION-DRIVEN. Swapping the two signs in the rotation - the
            // classic transcription slip - left every other test in this file
            // green, because "the wind varies" and "the wind stays inside its
            // budget" are both true of a field that veers backwards. A wind that
            // veers the wrong way is a wind that sends a player the wrong side of
            // an island, so the SIGN gets pinned, not just the magnitude.
            var field = new WindFieldVariation(1.0);
            double x = WindFieldVariation.CellMetres * 0.25;
            double expectedVeer = field.VeerAt(x, 0.0, 0.0);
            Assert.True(expectedVeer > 0.01, "pick a probe where the veer is clearly positive");

            WindSample wind = WindField.SampleAt(x, 0.0, 0.0, 4.0, field);

            Assert.Equal(
                WrapAngle(WindField.PublishedBearingRadians + expectedVeer),
                WrapAngle(wind.BearingRadians),
                9);
        }

        [Fact]
        public void The_admin_maps_copy_of_the_model_still_matches_this_one()
        {
            // The wind layer re-implements this field in JavaScript so the
            // browser can draw the REAL wind rather than an illustration. Two
            // copies of a model in two languages is a drift waiting to happen,
            // and nothing else in either suite compares them - halving the
            // browser's cell size passed every test in both projects.
            //
            // Coarse, and the same shape as FlightForceModelWiringTests: it reads
            // the production asset off disk and asserts the constants are
            // literally present. It cannot prove the two expressions agree; it
            // goes red the moment a number is changed on one side only.
            string js = File.ReadAllText(Path.Combine(
                RepoRoot(), "WorldsAdriftServer", "Web", "Assets", "admin-map-wind.js"));

            Assert.Contains(
                "WIND_CELL_M=" + WindFieldVariation.CellMetres.ToString("0", CultureInfo.InvariantCulture),
                js, StringComparison.Ordinal);
            Assert.Contains(
                "WIND_PERIOD_S=" + WindFieldVariation.PeriodSeconds.ToString("0", CultureInfo.InvariantCulture),
                js, StringComparison.Ordinal);
            Assert.Contains("WIND_MAX_VEER=40*Math.PI/180", js, StringComparison.Ordinal);
            Assert.Contains(
                "WIND_MAX_GUST=" + WindFieldVariation.MaxGustFraction.ToString("0.##", CultureInfo.InvariantCulture),
                js, StringComparison.Ordinal);
            Assert.Contains("WIND_PUBLISHED_X=1,WIND_PUBLISHED_Z=-2", js, StringComparison.Ordinal);

            // PROVED geometry, and the same numbers WeatherWallSegment carries.
            Assert.Contains("WIND_WALL_FULL_SQR=40000", js, StringComparison.Ordinal);
            Assert.Contains("WIND_WALL_REACH_SQR=160000", js, StringComparison.Ordinal);

            // The one thing a unit test can say about the DRAWING: SVG y grows
            // downward, so the world's Z must be negated on its way to the
            // screen. Dropping that negation mirrors every barb and no test in
            // either project noticed - only looking at a screenshot did.
            Assert.Contains("uz=s.speed>0?-s.z/s.speed:0", js, StringComparison.Ordinal);
        }

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static double WrapAngle(double radians)
        {
            while (radians > Math.PI)
            {
                radians -= 2.0 * Math.PI;
            }
            while (radians < -Math.PI)
            {
                radians += 2.0 * Math.PI;
            }
            return radians;
        }
    }
}
