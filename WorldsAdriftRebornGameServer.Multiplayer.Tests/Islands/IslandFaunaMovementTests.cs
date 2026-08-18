using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHY THESE FACTS MATTER. The manta perimeter patrol and the jellyfish
    /// day/night drift ARE the feature: everything else - the population policy,
    /// the bounded registry, the interest scoping - exists only to decide which
    /// creatures get to move and how often their motion is published. If the
    /// geometry is wrong, a manta flies inside the rock, or a kilometre off the
    /// perimeter, or - as actually happened - two hundred metres UNDER the ground
    /// the player is standing on, and no amount of correct bookkeeping above it
    /// helps.
    ///
    /// FOUR properties are asserted rather than assumed.
    ///
    /// THE RECOVERED VERTICAL BAND, which is the fact this file exists for. Retail's
    /// patrol offset is a sine of an orbit angle that is WRAPPED INTO [0,360], so its
    /// argument only covers a quarter period and the offset is never negative: the
    /// patrol occupies the band from the island's vertical MIDPOINT to its TOP. An
    /// earlier reading took it as a full sine about the midpoint, which sent mantas
    /// as far below the island as above it. On a floating island - whose walkable
    /// ground the release catalogue measures at a median 0.755 of AABB height - that
    /// is the difference between wildlife a player can see and wildlife inside the
    /// rock spire. <see cref="Manta_never_flies_below_the_islands_vertical_midpoint"/>
    /// pins it so the regression cannot come back quietly.
    ///
    /// CONTINUITY IN TIME. A closed form is free to teleport and this one used to:
    /// the jelly switched radius and altitude instantly at the day/night boundary.
    /// On the wire a teleport is indistinguishable from a despawn, which is the
    /// complaint the whole feature was reported for, so every path is now asserted
    /// to move in bounded steps across a WHOLE cycle rather than only at samples
    /// chosen to look good.
    ///
    /// GEOMETRY RESCALES: radii come from the envelope's own extents, so the facts
    /// run against a tiny islet, a huge island and a strongly anisotropic one - the
    /// only "different viewport" this headless server has.
    ///
    /// PURITY AND WIRING: LocalPoseAt is a total function of its arguments with no
    /// Random, no DateTime and no accumulation, which is what lets a restarted
    /// server replay the identical path and lets peers agree without syncing state;
    /// and the registry drives this maths through the FaunaPoseFunction delegate,
    /// which nothing else proves fits together.
    /// </summary>
    public sealed class IslandFaunaMovementTests
    {
        private const double Tolerance = 1e-9;
        private const double LooseTolerance = 1e-6;

        // --- Manta perimeter patrol (RECOVERED, acs/PatrolVisualiser.cs)

        [Fact]
        public void Manta_orbit_radius_is_the_recovered_half_diagonal_plus_the_standoff()
        {
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double halfX = (envelope.MaxX - envelope.MinX) / 2.0;
                double halfZ = (envelope.MaxZ - envelope.MinZ) / 2.0;
                double expected = Math.Sqrt((halfX * halfX) + (halfZ * halfZ))
                    + IslandFaunaMovement.MantaLateralStandoffMetres;

                Assert.Equal(expected, IslandFaunaMovement.MantaOrbitRadiusOf(envelope), 9);

                // The half-DIAGONAL, not the larger half-extent: that is what clears
                // the corners of a box rather than clipping through them.
                Assert.True(IslandFaunaMovement.MantaOrbitRadiusOf(envelope)
                    > IslandFaunaMovement.LateralRadiusOf(envelope),
                    "the orbit must clear the island's lateral bounds");
            }
        }

        [Fact]
        public void Manta_school_centre_holds_the_orbit_radius_across_a_whole_revolution()
        {
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double orbit = IslandFaunaMovement.MantaOrbitRadiusOf(envelope);
                double schoolRadius = IslandFaunaSchool.MantaSchoolRadiusMetres;

                foreach (double seconds in Revolution(envelope))
                {
                    // A member is displaced from the school's centre, so the school's
                    // radius is what is exact and the member is within a cluster of it.
                    double distance = LateralDistance(Manta(0), envelope, seconds);
                    Assert.True(Math.Abs(distance - orbit) <= schoolRadius + LooseTolerance,
                        "a school member must stay within the cluster radius of the orbit,"
                            + " was " + distance + " against an orbit of " + orbit);
                }
            }
        }

        [Fact]
        public void Manta_stays_outside_a_tiny_island() => AssertStaysOutside(Tiny());

        [Fact]
        public void Manta_stays_outside_a_huge_island() => AssertStaysOutside(Huge());

        [Fact]
        public void Manta_stays_outside_a_strongly_anisotropic_island() =>
            AssertStaysOutside(Anisotropic());

        [Fact]
        public void Manta_never_flies_below_the_islands_vertical_midpoint()
        {
            // THE RECOVERED FACT. acs/PatrolVisualiser.cs computes
            //   Vector3.up * Mathf.Sin(orbitDegrees * (PI/180f) * 0.25f) * BoundsExtents.y
            // with orbitDegrees wrapped into [0,360] by CreatureReachedPatrol, so the
            // sine's argument covers [0, PI/2] and the offset covers [0, +extents.y].
            // Never negative. A regression to a full sine about the midpoint would put
            // half of every lap under the rock, and this is the assertion that catches it.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double centreY = IslandFaunaMovement.CentreYOf(envelope);
                double half = IslandFaunaMovement.HalfHeightOf(envelope);
                double vertical = IslandFaunaSchool.MantaSchoolVerticalRadiusMetres;

                foreach (double seconds in Revolution(envelope))
                {
                    double y = IslandFaunaMovement.LocalPoseAt(Manta(0), envelope, seconds).Y;
                    Assert.True(y >= centreY - vertical - LooseTolerance,
                        "a manta must never patrol below the island's vertical midpoint;"
                            + " was " + y + " against a midpoint of " + centreY);
                    Assert.True(y <= centreY + half + vertical + LooseTolerance,
                        "a manta must not climb above the island's own top");
                }
            }
        }

        [Fact]
        public void Manta_band_straddles_the_ground_a_player_actually_stands_on()
        {
            // The release catalogue's own landing points sit at a median 0.755 of AABB
            // height. The recovered band is [0.5, 1.0] of that height, so the patrol
            // passes through the player's altitude twice a lap. This is the difference
            // between "there is wildlife" and "there is wildlife you can see".
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double walkable = envelope.MinY + ((envelope.MaxY - envelope.MinY)
                    * IslandFaunaMovement.IslandWalkableHeightFraction);
                bool below = false;
                bool above = false;

                foreach (double seconds in Revolution(envelope))
                {
                    // The SCHOOL's own altitude: a member's cluster offset is a fixed
                    // number of metres and would swamp the band on a test islet only a
                    // few metres tall.
                    double y = IslandFaunaMovement
                        .MantaSchoolCentreAt(Manta(0), envelope, seconds).Y;
                    if (y < walkable) below = true;
                    if (y > walkable) above = true;
                }

                Assert.True(below && above,
                    "the patrol band must cross the walkable altitude, not sit entirely"
                        + " above or below it");
            }
        }

        [Fact]
        public void Manta_travels_at_a_constant_speed_so_a_big_island_takes_a_long_lap()
        {
            // RECOVERED DIRECTION: retail advanced the patrol target when the creature
            // REACHED it, so lap time followed island size. A fixed lap time - which is
            // what this server used to have - makes the largest island's manta move at
            // 23 m/s and the smallest one's crawl.
            double smallLap = IslandFaunaMovement.MantaLapSecondsOf(Tiny());
            double bigLap = IslandFaunaMovement.MantaLapSecondsOf(Huge());
            Assert.True(bigLap > smallLap * 10.0,
                "a much larger island must take a much longer lap at a fixed speed");

            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double lap = IslandFaunaMovement.MantaLapSecondsOf(envelope);
                double expected = 2.0 * Math.PI
                    * IslandFaunaMovement.MantaOrbitRadiusOf(envelope)
                    / IslandFaunaMovement.MantaMetresPerSecond;
                Assert.Equal(expected, lap, 9);

                // And the school centre really does complete exactly one lap in it,
                // returning to the same point rather than merely the same heading.
                (double X, double Y, double Z) start =
                    IslandFaunaMovement.MantaSchoolCentreAt(Manta(0), envelope, 0.0);
                (double X, double Y, double Z) full =
                    IslandFaunaMovement.MantaSchoolCentreAt(Manta(0), envelope, lap);
                (double X, double Y, double Z) half =
                    IslandFaunaMovement.MantaSchoolCentreAt(Manta(0), envelope, lap / 2.0);

                Assert.Equal(start.X, full.X, 6);
                Assert.Equal(start.Y, full.Y, 6);
                Assert.Equal(start.Z, full.Z, 6);
                Assert.True(Math.Abs(half.X - start.X) + Math.Abs(half.Z - start.Z)
                    > IslandFaunaMovement.MantaOrbitRadiusOf(envelope),
                    "half a lap must be most of the way round, not a rounding error");
            }
        }

        [Fact]
        public void School_members_ride_the_school_rather_than_stacking_on_it()
        {
            IslandTerrainEnvelope envelope = Normal();
            foreach (double seconds in Revolution(envelope))
            {
                (double X, double Y, double Z) first =
                    IslandFaunaMovement.LocalPoseAt(Manta(0), envelope, seconds);
                (double X, double Y, double Z) second =
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds);

                double dx = first.X - second.X;
                double dy = first.Y - second.Y;
                double dz = first.Z - second.Z;
                double apart = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                Assert.True(apart > 1.0,
                    "two members of one school must not occupy the same point");
                Assert.True(apart < 4.0 * IslandFaunaSchool.MantaSchoolRadiusMetres,
                    "two members of one school must stay together; a school that spreads"
                        + " past its own diameter is not a school");
            }
        }

        // --- Jellyfish day/night drift (RECOVERED, acs/JellyFishMovement.cs)

        [Fact]
        public void Day_and_night_use_the_recovered_thresholds_and_are_total_for_any_input()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            // RECOVERED EXACTLY: _isDayTime = num > 0.2f && num < 0.8f. Day is the
            // middle 60% of the cycle, NOT an even half - which is why it is recovered
            // rather than assumed.
            Assert.Equal(0.2, IslandFaunaMovement.DayBeginsAtCycleFraction);
            Assert.Equal(0.8, IslandFaunaMovement.DayEndsAtCycleFraction);

            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(0.0));
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(cycle * 0.1));
            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(cycle * 0.5));
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(cycle * 0.9));

            // Total for negative input: a clock that has not started must not throw
            // and must not produce an undefined phase.
            Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(-1.0));
            Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(-cycle * 0.5));

            // Periodic across whole cycles, in both directions.
            for (int lap = -3; lap <= 3; lap++)
            {
                Assert.Equal(FaunaDayPhase.Day,
                    IslandFaunaMovement.PhaseAt((lap * cycle) + (cycle * 0.5)));
                Assert.Equal(FaunaDayPhase.Night,
                    IslandFaunaMovement.PhaseAt((lap * cycle) + (cycle * 0.95)));
            }
        }

        [Fact]
        public void Dayness_is_a_smooth_ramp_that_agrees_with_the_boolean_away_from_dawn()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            Assert.Equal(0.0, IslandFaunaMovement.DaynessAt(0.0), 9);
            Assert.Equal(1.0, IslandFaunaMovement.DaynessAt(cycle * 0.5), 9);
            Assert.Equal(0.0, IslandFaunaMovement.DaynessAt(cycle * 0.95), 9);

            // Bounded, periodic and continuous everywhere - including across the wrap,
            // which is where a naive "cycle < 0.5" formulation breaks.
            double previous = IslandFaunaMovement.DaynessAt(-cycle);
            for (int step = 1; step <= 4000; step++)
            {
                double dayness = IslandFaunaMovement.DaynessAt(-cycle + (step * cycle / 1000.0));
                Assert.InRange(dayness, 0.0, 1.0);
                Assert.True(Math.Abs(dayness - previous) < 0.05,
                    "dayness must ramp, not step");
                previous = dayness;
            }
        }

        [Fact]
        public void Jelly_drifts_out_and_down_by_day_and_rises_to_the_rim_by_night()
        {
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);
                double shoal = IslandFaunaSchool.JellyShoalRadiusMetres;

                double day = cycle * 0.5;
                double night = 0.0;
                Assert.Equal(FaunaDayPhase.Day, IslandFaunaMovement.PhaseAt(day));
                Assert.Equal(FaunaDayPhase.Night, IslandFaunaMovement.PhaseAt(night));

                // The SHOAL's own station, not one drifter's: a jelly cluster is 26 m
                // wide and the test islet is 16 m across, so a member-level assertion
                // would be measuring the cluster rather than the day/night rule.
                (double X, double Y, double Z) dayCentre =
                    IslandFaunaMovement.JellyShoalCentreAt(Jelly(0), envelope, day);
                (double X, double Y, double Z) nightCentre =
                    IslandFaunaMovement.JellyShoalCentreAt(Jelly(0), envelope, night);
                double byDay = LateralOf(dayCentre, envelope);
                double byNight = LateralOf(nightCentre, envelope);

                // RECOVERED: daytime steers laterally AWAY from the island centre.
                Assert.True(byDay > byNight,
                    "by day the jelly moves laterally AWAY from the island centre");
                Assert.True(byDay > lateral,
                    "the day station must sit outside the island's lateral bounds");

                Assert.Equal(lateral * IslandFaunaMovement.JellyDayRadiusRatio, byDay, 6);
                Assert.Equal(lateral * IslandFaunaMovement.JellyNightRadiusRatio, byNight, 6);

                // A member stays within its shoal of that station.
                Assert.True(Math.Abs(LateralDistance(Jelly(0), envelope, day) - byDay)
                    <= shoal + LooseTolerance);

                // RECOVERED: the daytime jelly holds the BoundsMin altitude - the
                // underside of the rock. At night it rises toward the rim.
                Assert.True(nightCentre.Y > dayCentre.Y,
                    "the night shoal must rise above the day station");
                Assert.Equal(envelope.MinY, dayCentre.Y, 6);
            }
        }

        [Fact]
        public void Night_shoal_reaches_the_altitude_a_player_stands_at()
        {
            // The reason the player had never seen a jellyfish: the night station used
            // to be the AABB midpoint, which on a floating island is inside the rock
            // and a hundred metres under the ground.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double walkable = envelope.MinY + ((envelope.MaxY - envelope.MinY)
                    * IslandFaunaMovement.IslandWalkableHeightFraction);
                double nightY = IslandFaunaMovement.JellyShoalCentreAt(Jelly(0), envelope, 0.0).Y;
                Assert.Equal(walkable, nightY, 6);
            }
        }

        // --- Orientation. The wildlife used to fly sideways because every pose went
        // --- out with the client's identity sentinel and no rotation was computed.

        [Fact]
        public void Manta_points_its_nose_along_the_schools_direction_of_travel()
        {
            // The whole bug, as one assertion. RECOVERED: retail steered
            // transform.forward onto the look direction
            // (RigidbodyX.CalculateTorqueForTargetHeading), so +Z is the nose.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                foreach (double seconds in Revolution(envelope))
                {
                    (double X, double Y, double Z) travel = SchoolTravel(envelope, seconds);
                    (double X, double Y, double Z) nose = IslandFaunaOrientation.ForwardOf(
                        IslandFaunaMovement.RotationAt(Manta(0), envelope, seconds));

                    double off = IslandFaunaOrientation.AngleBetween(
                        IslandFaunaOrientation.Flatten(travel), nose);
                    Assert.True(off < 0.05,
                        "a manta must face where it is going; it was "
                            + (off * 180.0 / Math.PI).ToString("0.0") + " degrees off at t=" + seconds);
                }
            }
        }

        [Fact]
        public void Manta_keeps_its_nose_level_even_while_the_patrol_climbs()
        {
            // RECOVERED, and easy to get wrong: MovementController.UpdateAngle does
            // _lookDirection = Vector3.Scale(_lookDirection, new Vector3(1f, 0f, 1f))
            // - it ZEROES the vertical. A retail manta never pitched its nose from
            // its steering vector, so the vertical patrol band must not tilt it.
            IslandTerrainEnvelope envelope = Normal();
            bool climbed = false;

            foreach (double seconds in Revolution(envelope))
            {
                (double X, double Y, double Z) travel = SchoolTravel(envelope, seconds);
                if (Math.Abs(travel.Y) > 0.01) climbed = true;

                (double X, double Y, double Z) nose = IslandFaunaOrientation.ForwardOf(
                    IslandFaunaMovement.RotationAt(Manta(0), envelope, seconds));
                Assert.True(Math.Abs(nose.Y) < 1e-6,
                    "the nose pitched to " + nose.Y + " at t=" + seconds);
            }

            Assert.True(climbed,
                "this island's patrol never changed altitude, so the fact proved nothing");
        }

        [Fact]
        public void Manta_banks_into_its_turn_toward_the_island_it_is_circling()
        {
            // RECOVERED SHAPE: retail tilted the up vector toward the horizontal
            // RIGHT by an amount proportional to yaw effort. On a closed orbit the
            // inside of the turn IS the island, so a correctly banked manta leans its
            // back toward the rock - and a sign error would lean it away, which is
            // the one failure mode that still looks deliberate.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double leaned = 0.0;
                foreach (double seconds in Revolution(envelope))
                {
                    (double X, double Y, double Z) back = IslandFaunaOrientation.UpOf(
                        IslandFaunaMovement.RotationAt(Manta(0), envelope, seconds));
                    (double X, double Y, double Z) centre =
                        IslandFaunaMovement.MantaSchoolCentreAt(Manta(0), envelope, seconds);

                    double toCentreX = IslandFaunaMovement.CentreXOf(envelope) - centre.X;
                    double toCentreZ = IslandFaunaMovement.CentreZOf(envelope) - centre.Z;
                    double lean = (back.X * toCentreX) + (back.Z * toCentreZ);
                    Assert.True(lean > 0.0,
                        "the manta banked AWAY from the island it is circling at t=" + seconds);

                    leaned = Math.Max(leaned, IslandFaunaOrientation.AngleBetween(
                        back, (0.0, 1.0, 0.0)));
                }

                // 1e-5 rather than 0: FaunaRotation stores floats, so the angle
                // recovered from the quaternion carries single-precision error.
                Assert.True(leaned <= IslandFaunaMovement.MantaMaximumBankRadians + 1e-5,
                    "the bank exceeded its own clamp on " + envelope.IslandId
                        + " (" + (leaned * 180.0 / Math.PI).ToString("0.000") + " degrees)");
            }
        }

        [Fact]
        public void Bank_is_visible_on_every_island_the_release_world_actually_has()
        {
            // The synthetic Huge() shape above is an 8 km island - far larger than
            // anything that exists - and it correctly banks about a degree, because
            // bank falls off as speed-over-radius. That is right, but it means the
            // "is it visible" question has to be asked of the REAL catalogue.
            //
            // Across the tier-1 world the orbit radius runs 84 m to 626 m, so this
            // pins both ends: a manta must lean enough to read at the largest island
            // and must not be stuck on its edge at the smallest.
            foreach (ReleaseIslandRecord island in ReleaseWorldRolloutPolicy.Select("tier1"))
            {
                double worst = 0.0;
                foreach (double seconds in Revolution(island.Envelope))
                {
                    worst = Math.Max(worst, IslandFaunaOrientation.AngleBetween(
                        IslandFaunaOrientation.UpOf(
                            IslandFaunaMovement.RotationAt(Manta(0), island.Envelope, seconds)),
                        (0.0, 1.0, 0.0)));
                }

                Assert.True(worst > 5.0 * Math.PI / 180.0,
                    island.Definition.Id + " banks only "
                        + (worst * 180.0 / Math.PI).ToString("0.0") + " degrees - too flat to read");
                Assert.True(worst <= IslandFaunaMovement.MantaMaximumBankRadians + 1e-5,
                    island.Definition.Id + " banks "
                        + (worst * 180.0 / Math.PI).ToString("0.0") + " degrees - past the clamp");
            }
        }

        [Fact]
        public void School_members_face_the_same_way_as_each_other()
        {
            // RECOVERED, not a judgement call. Retail's boid set has an explicit
            // ALIGNMENT rule (mean neighbour velocity, weight 1.5) and a flock-seek
            // rule at weight 15 pulling every member onto ONE attractor: two of the
            // five rules actively drive a common heading and none drives them apart.
            // Taking the heading from the SCHOOL rather than from each member's own
            // tangent is what reproduces that.
            IslandTerrainEnvelope envelope = Normal();
            foreach (double seconds in Revolution(envelope))
            {
                (double X, double Y, double Z) lead = IslandFaunaOrientation.ForwardOf(
                    IslandFaunaMovement.RotationAt(Manta(0), envelope, seconds));

                for (int member = 1; member < 8; member++)
                {
                    (double X, double Y, double Z) other = IslandFaunaOrientation.ForwardOf(
                        IslandFaunaMovement.RotationAt(Manta(member), envelope, seconds));
                    double spread = IslandFaunaOrientation.AngleBetween(lead, other);

                    Assert.True(spread <= (2.0 * IslandFaunaMovement.SchoolHeadingJitterRadians) + 1e-6,
                        "member " + member + " faced " + (spread * 180.0 / Math.PI).ToString("0.00")
                            + " degrees off the school at t=" + seconds);
                }
            }
        }

        [Fact]
        public void School_members_are_not_frozen_into_one_identical_heading()
        {
            // The other half of the same question: a school that is bit-identical
            // across its members reads as a towed model. RECOVERED MAGNITUDE -
            // retail's wander rule is AngleAxis(Mathf.Sin(...), up), and AngleAxis
            // takes DEGREES, so its heading shimmer was at most ONE degree.
            IslandTerrainEnvelope envelope = Normal();
            bool anyDifference = false;

            foreach (double seconds in Revolution(envelope))
            {
                (double X, double Y, double Z) lead = IslandFaunaOrientation.ForwardOf(
                    IslandFaunaMovement.RotationAt(Manta(0), envelope, seconds));
                (double X, double Y, double Z) other = IslandFaunaOrientation.ForwardOf(
                    IslandFaunaMovement.RotationAt(Manta(3), envelope, seconds));
                if (IslandFaunaOrientation.AngleBetween(lead, other) > 1e-4) anyDifference = true;
            }

            Assert.True(anyDifference, "every member held the identical heading forever");
        }

        [Fact]
        public void Jelly_stays_bell_up_and_does_not_swim_nose_first()
        {
            // RECOVERED, and deliberately NOT the manta rule. A jelly ran
            // BasicMovementController, whose entire rotational surface is
            // SetTargetUpDirection plus a raw AddTorque - no heading PID, no look
            // direction, no reference to transform.forward anywhere in it or in
            // JellyFishMovement. Its bell axis was pinned to world up and its yaw was
            // left completely free. Pointing a jelly along its travel would be a
            // BIGGER error than leaving it at identity.
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                double worstTilt = 0.0;
                double widestYawSpread = 0.0;
                (double X, double Y, double Z)? firstOffset = null;

                for (int step = 0; step <= 240; step++)
                {
                    double seconds = step * IslandFaunaMovement.DayNightCycleSeconds / 240.0;
                    FaunaRotation q = IslandFaunaMovement.RotationAt(Jelly(0), envelope, seconds);

                    double tilt = IslandFaunaOrientation.AngleBetween(
                        IslandFaunaOrientation.UpOf(q), (0.0, 1.0, 0.0));
                    worstTilt = Math.Max(worstTilt, tilt);

                    (double X, double Y, double Z) nose = IslandFaunaOrientation.ForwardOf(q);
                    firstOffset ??= nose;
                    widestYawSpread = Math.Max(widestYawSpread,
                        IslandFaunaOrientation.AngleBetween(firstOffset.Value, nose));
                }

                // The bell stays up, rocking only by the recovered few degrees.
                Assert.True(worstTilt <= IslandFaunaMovement.JellyPulseTiltRadians + 1e-6,
                    "a jelly rolled " + (worstTilt * 180.0 / Math.PI).ToString("0.0")
                        + " degrees off vertical on " + envelope.IslandId);

                // And the yaw really is free rather than pinned to anything.
                Assert.True(widestYawSpread > Math.PI / 2.0,
                    "a jelly's yaw is unconstrained in retail and must actually drift");
            }
        }

        [Fact]
        public void Every_creature_rotation_is_a_usable_unit_quaternion_on_the_wire()
        {
            foreach (IslandTerrainEnvelope envelope in EveryShape())
            {
                foreach (FaunaCreature creature in new[] { Manta(0), Manta(5), Jelly(0), Jelly(4) })
                {
                    for (int step = 0; step <= 120; step++)
                    {
                        double seconds = step * IslandFaunaMovement.DayNightCycleSeconds / 120.0;
                        FaunaRotation q = IslandFaunaMovement.RotationAt(creature, envelope, seconds);

                        double length = Math.Sqrt((q.W * q.W) + (q.X * q.X)
                            + (q.Y * q.Y) + (q.Z * q.Z));
                        Assert.True(Math.Abs(length - 1.0) < 1e-5,
                            creature.Species + " produced a non-unit quaternion of " + length);
                        Assert.False(float.IsNaN(q.W) || float.IsNaN(q.X)
                            || float.IsNaN(q.Y) || float.IsNaN(q.Z));
                    }
                }
            }
        }

        [Fact]
        public void Rotation_never_snaps_and_is_a_pure_function_of_its_arguments()
        {
            // Same promise as the position: a heading that jumps reads as a creature
            // flicking round on the spot, and a rotation that is not reproducible
            // would make a restart visibly disagree with itself.
            const double StepSeconds = 0.25;
            IslandTerrainEnvelope envelope = Normal();

            foreach (FaunaCreature creature in new[] { Manta(0), Manta(2), Jelly(0), Jelly(3) })
            {
                FaunaRotation previous = IslandFaunaMovement.RotationAt(creature, envelope, 0.0);
                for (double t = StepSeconds; t <= IslandFaunaMovement.DayNightCycleSeconds; t += StepSeconds)
                {
                    FaunaRotation now = IslandFaunaMovement.RotationAt(creature, envelope, t);

                    Assert.Equal(now, IslandFaunaMovement.RotationAt(creature, envelope, t));

                    double turned = IslandFaunaOrientation.AngleBetween(
                        IslandFaunaOrientation.ForwardOf(previous),
                        IslandFaunaOrientation.ForwardOf(now));
                    Assert.True(turned < 0.35,
                        creature.Species + " snapped " + (turned * 180.0 / Math.PI).ToString("0.0")
                            + " degrees in a quarter second at t=" + t);
                    previous = now;
                }
            }
        }

        // --- Continuity: a closed form is allowed to teleport, and must not

        [Fact]
        public void No_creature_ever_teleports_across_a_whole_day_night_cycle()
        {
            // THE REGRESSION GUARD for the bug that made a jelly jump from the island's
            // underside to its rim in one frame. Sampling at the pose cadence over a
            // whole cycle is the only thing that catches a discontinuity at a phase
            // boundary, because the boundary is exactly where hand-picked samples are
            // not.
            const double StepSeconds = 0.25;
            double cycle = IslandFaunaMovement.DayNightCycleSeconds;

            foreach (IslandTerrainEnvelope envelope in new[] { Normal(), Tiny(), Anisotropic() })
            {
                foreach (FaunaCreature creature in new[] { Manta(0), Manta(3), Jelly(0), Jelly(2) })
                {
                    // Generous but finite: the fastest thing here is a manta at
                    // MantaMetresPerSecond plus the cluster weave, so a quarter second
                    // can never move it more than a few metres.
                    double limit = (IslandFaunaMovement.MantaMetresPerSecond * StepSeconds * 4.0)
                        + 2.0;
                    (double X, double Y, double Z) previous =
                        IslandFaunaMovement.LocalPoseAt(creature, envelope, 0.0);

                    for (double t = StepSeconds; t <= cycle; t += StepSeconds)
                    {
                        (double X, double Y, double Z) now =
                            IslandFaunaMovement.LocalPoseAt(creature, envelope, t);
                        double dx = now.X - previous.X;
                        double dy = now.Y - previous.Y;
                        double dz = now.Z - previous.Z;
                        double moved = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
                        Assert.True(moved <= limit,
                            creature.Species + " teleported " + moved + " m at t=" + t
                                + " on " + envelope.IslandId);
                        previous = now;
                    }
                }
            }
        }

        // --- Determinism, which is the contract the registry is built on

        [Fact]
        public void Local_pose_is_a_pure_function_of_its_arguments()
        {
            IslandTerrainEnvelope envelope = Normal();
            double[] times = Revolution(envelope).ToArray();
            Dictionary<double, (double X, double Y, double Z)> baseline =
                times.ToDictionary(t => t,
                    t => IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, t));

            // Repeated: no accumulation, no clock, no entropy.
            for (int repeat = 0; repeat < 8; repeat++)
            {
                foreach (double seconds in times)
                {
                    Assert.Equal(baseline[seconds],
                        IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
                }
            }

            // Out of order: evaluation order must not change a single value, which is
            // what makes the parallel test scheduler and a cold restart equivalent.
            foreach (double seconds in times.Reverse())
            {
                Assert.Equal(baseline[seconds],
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
            }
            foreach (double seconds in times.Where((_, i) => i % 3 == 1))
            {
                Assert.Equal(baseline[seconds],
                    IslandFaunaMovement.LocalPoseAt(Manta(1), envelope, seconds));
            }
        }

        [Fact]
        public void World_pose_is_exactly_the_island_transform_of_the_local_pose()
        {
            IslandTerrainEnvelope envelope = Normal();
            IslandDefinition island = Island(envelope.IslandId);

            foreach (FaunaCreature creature in new[] { Manta(0), Manta(2), Jelly(1) })
            {
                foreach (double seconds in Revolution(envelope))
                {
                    (double x, double y, double z) =
                        IslandFaunaMovement.LocalPoseAt(creature, envelope, seconds);
                    Assert.Equal(island.LocalToGlobal(x, y, z),
                        IslandFaunaMovement.WorldPoseAt(creature, island, envelope, seconds));
                }
            }
        }

        // --- The registry can actually be driven by this maths

        [Fact]
        public void Registry_driven_by_the_movement_delegate_publishes_the_same_poses()
        {
            // The method group must satisfy the delegate the registry declares; if the
            // signatures ever drift this line stops compiling, which is the point.
            FaunaPoseFunction pose = IslandFaunaMovement.WorldTransformAt;

            IslandTerrainEnvelope envelope = Normal();
            IslandDefinition island = Island(envelope.IslandId);
            FaunaCreature creature = Manta(0);
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = new IslandFaunaRegistry(clock, pose);
            Assert.True(registry.Add(creature, island, envelope));

            foreach (double seconds in new[] { 0.0, 0.25, 1.0, 17.5, 144.0, 1201.0 })
            {
                clock.Elapsed = TimeSpan.FromSeconds(seconds);
                FaunaPose published = Assert.Single(registry.DuePoses());
                Assert.Equal(creature.EntityId, published.EntityId);
                Assert.Equal(
                    IslandFaunaMovement.WorldPoseAt(creature, island, envelope, seconds),
                    published.Position);
                // And the ROTATION rides the same publication, from the same instant.
                Assert.Equal(
                    IslandFaunaMovement.RotationAt(creature, envelope, seconds),
                    published.Rotation);
                clock.Elapsed += registry.PoseInterval;
            }
        }

        /// <summary>The manta patrol clears the island's own lateral bounds at every sample.</summary>
        private static void AssertStaysOutside(IslandTerrainEnvelope envelope)
        {
            double lateral = IslandFaunaMovement.LateralRadiusOf(envelope);
            double orbit = IslandFaunaMovement.MantaOrbitRadiusOf(envelope);
            Assert.True(orbit > lateral);

            foreach (double seconds in Revolution(envelope))
            {
                double distance = LateralDistance(Manta(0), envelope, seconds);
                Assert.True(distance > lateral + Tolerance,
                    "a manta must never be inside the rock on an island of this shape");
            }
        }

        private static double LateralDistance(
            FaunaCreature creature, IslandTerrainEnvelope envelope, double seconds)
        {
            (double x, double _, double z) =
                IslandFaunaMovement.LocalPoseAt(creature, envelope, seconds);
            double dx = x - IslandFaunaMovement.CentreXOf(envelope);
            double dz = z - IslandFaunaMovement.CentreZOf(envelope);
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>The school centre's displacement over one heading sample window.</summary>
        private static (double X, double Y, double Z) SchoolTravel(
            IslandTerrainEnvelope envelope, double seconds)
        {
            (double X, double Y, double Z) before =
                IslandFaunaMovement.MantaSchoolCentreAt(Manta(0), envelope, seconds);
            (double X, double Y, double Z) after = IslandFaunaMovement.MantaSchoolCentreAt(
                Manta(0), envelope, seconds + IslandFaunaMovement.HeadingSampleSeconds);
            return (after.X - before.X, after.Y - before.Y, after.Z - before.Z);
        }

        /// <summary>How far a point sits from the island's lateral centre.</summary>
        private static double LateralOf(
            (double X, double Y, double Z) point, IslandTerrainEnvelope envelope)
        {
            double dx = point.X - IslandFaunaMovement.CentreXOf(envelope);
            double dz = point.Z - IslandFaunaMovement.CentreZOf(envelope);
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        /// <summary>Thirty-six sample times spanning one whole manta revolution of this island.</summary>
        private static IEnumerable<double> Revolution(IslandTerrainEnvelope envelope)
        {
            double lap = IslandFaunaMovement.MantaLapSecondsOf(envelope);
            for (int i = 0; i < 36; i++)
            {
                yield return lap * i / 36.0;
            }
        }

        private static IslandTerrainEnvelope[] EveryShape() =>
            new[] { Normal(), Tiny(), Huge(), Anisotropic() };

        private static IslandTerrainEnvelope Normal() =>
            Box("fauna-normal", 300.0, 300.0, -90.0, 100.0);

        private static IslandTerrainEnvelope Tiny() =>
            Box("fauna-tiny", 6.0, 8.0, -3.0, 4.0);

        private static IslandTerrainEnvelope Huge() =>
            Box("fauna-huge", 4000.0, 3500.0, -1200.0, 900.0);

        private static IslandTerrainEnvelope Anisotropic() =>
            Box("fauna-anisotropic", 900.0, 40.0, -60.0, 30.0);

        /// <summary>An off-centre envelope, so a centre helper that returned zero would fail.</summary>
        private static IslandTerrainEnvelope Box(
            string id, double halfX, double halfZ, double minY, double maxY) =>
            new IslandTerrainEnvelope(new IslandId(id),
                -halfX + 12.5, minY, -halfZ - 7.25,
                halfX + 12.5, maxY, halfZ - 7.25);

        private static FaunaCreature Manta(int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + index,
                FaunaSpecies.MantaRay, new IslandId("fauna-normal"), index, 0, index);

        private static FaunaCreature Jelly(int index) =>
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + 50 + index,
                FaunaSpecies.JellyFish, new IslandId("fauna-normal"), 50 + index, 0, index);

        private static IslandDefinition Island(IslandId id) => new IslandDefinition(
            id, "Fauna Test Island", "island-" + id.Value,
            FixedPointPosition.FromMetres(1000.5, -2000.25, 3000.125),
            "0@Island", IslandCatalog.DefaultTerrainAssetContext, SpawnOrder.AfterPlayer);

        /// <summary>Time the test owns; nothing here sleeps.</summary>
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }
    }
}
