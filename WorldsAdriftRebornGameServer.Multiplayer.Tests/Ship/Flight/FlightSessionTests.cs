using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The session's emission phases and, above all, its TIMESTAMPS: the client
    /// silently drops any pair of control points closer than 0.228 s or out of
    /// order (ControlPoint.ValidateControlPoints), so the legality of every
    /// consecutive pair across every phase transition is asserted here, where it
    /// can fail loudly.
    /// </summary>
    public class FlightSessionTests
    {
        private const double Step = ShipMotionPolicy.SendIntervalSeconds;
        private const long StepMs = 240;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static FlightControlInput Throttle(float value) =>
            new FlightControlInput(value, 0f, 0f, 0f, 0f);

        /// <summary>Drives the session like the service does and collects emissions.</summary>
        private static List<FlightEmit> Drive(FlightSession session, ref long nowMs, int ticks)
        {
            List<FlightEmit> emitted = new List<FlightEmit>();
            for (int i = 0; i < ticks; i++)
            {
                nowMs += StepMs;
                FlightEmit emit = session.Advance(nowMs, Step, Tuning);
                if (emit.Emit)
                {
                    emitted.Add(emit);
                }
            }
            return emitted;
        }

        [Fact]
        public void A_manned_session_with_throttle_emits_every_tick_and_moves()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));

            long now = 1_000_000;
            List<FlightEmit> emitted = Drive(session, ref now, 10);

            Assert.Equal(10, emitted.Count);
            Assert.True(session.State.Z > 0, "full throttle must move the ship along +Z");
            Assert.True(emitted[^1].Spec.Vz > 0, "the wire velocity must show the motion");
        }

        [Fact]
        public void Unfurling_canvas_wakes_a_resting_unmanned_force_session_without_touching_the_helm()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            ShipPropulsion propulsion = new ShipPropulsion(
                massKg: 3094, engineThrustNewtons: 0, unfurledSails: 2);
            session.WakeForCanvas();

            FlightEmit first = session.Advance(
                1_000_000, Step, Tuning, unfurledSails: 2, propulsion: propulsion);

            Assert.True(first.Emit);
            Assert.False(session.State.IsAtRest);
            Assert.True(session.State.SpeedCmdMps > 0.0,
                "canvas must wake the quiet flight session without a helm interaction");
        }

        [Fact]
        public void Input_is_ignored_while_unmanned()
        {
            // A 1111 packet that raced past the dismount must not fly an empty ship.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.SetInput(Throttle(1f));

            long now = 1_000_000;
            Drive(session, ref now, 5);

            Assert.Equal(0.0, session.State.SpeedCmdMps);
            Assert.True(session.State.IsAtRest);
        }

        [Fact]
        public void Explicit_stop_then_dismount_settles_to_rest_then_repeats_then_goes_quiet()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));

            long now = 1_000_000;
            Drive(session, ref now, 20); // up to speed
            session.SetInput(Throttle(0f)); // pilot deliberately parks the lever
            session.Dismount();

            // Settling: max speed / accel = 3 s = ~13 ticks of deceleration, then
            // the rest repeats, then silence. 40 ticks is comfortably past both.
            List<FlightEmit> after = Drive(session, ref now, 40);

            Assert.True(session.State.IsAtRest, "a dismounted ship must come to a stop");
            Assert.True(after.Count < 40, "emission must stop after the rest repeats");
            Assert.True(after[^1].Spec.Arrived, "the final point must be the arrived/at-rest one");
            Assert.Equal(0.0, after[^1].Spec.Vz, 9);
        }

        [Fact]
        public void After_rest_the_session_stays_silent_until_a_real_wake_edge()
        {
            // A late join receives a fresh 1130 seed at the latest persisted hull
            // pose. Sending zero-speed points forever is actively harmful: retail
            // PathFollower's no-pose-change fast path retains its preceding
            // non-zero velocity and a later heartbeat can manufacture a false
            // drift/correction cycle after the ship has halted.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));
            long now = 1_000_000;
            Drive(session, ref now, 10);
            session.SetInput(Throttle(0f));
            session.Dismount();
            Drive(session, ref now, 60); // settle + repeats + quiet

            // Even well beyond the former keepalive interval, no fabricated point.
            now += 30_000 + StepMs;
            FlightEmit quiet = session.Advance(now, Step, Tuning);

            Assert.False(quiet.Emit);

            // A real helm edge still primes the exact resting pose immediately.
            session.Man();
            FlightEmit prime = session.PrimePlayback(now + StepMs, Step);
            Assert.True(prime.Emit);
            Assert.True(prime.Spec.Arrived);
        }

        [Fact]
        public void Client_Hermite_path_never_reverses_during_the_final_settle()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            var propulsion = new ShipPropulsion(
                3094.0, ShipForceModel.DefaultEngineThrustNewtons, 0);
            session.Man();
            session.SetInput(Throttle(1f));

            long now = 1_000_000;
            var points = new List<ShipControlPointSpec>();
            for (int i = 0; i < 60; i++)
            {
                now += StepMs;
                FlightEmit emit = session.AdvanceFixed(now, Step, 12,
                    (now / 1000.0) - 0.22, Tuning,
                    agilityScale: 0.51, propulsion: propulsion,
                    fixedStepSeconds: 0.02);
                if (emit.Emit) points.Add(emit.Spec);
            }

            session.SetInput(Throttle(0f));
            session.Dismount();
            for (int i = 0; i < 500 && !session.State.IsAtRest; i++)
            {
                now += StepMs;
                FlightEmit emit = session.AdvanceFixed(now, Step, 12,
                    (now / 1000.0) - 0.22, Tuning,
                    agilityScale: 0.51, propulsion: propulsion,
                    fixedStepSeconds: 0.02);
                if (emit.Emit) points.Add(emit.Spec);
            }

            double worstAlong = 0.0;
            int worstInterval = -1;
            for (int i = 1; i < points.Count; i++)
            {
                ShipControlPointSpec a = points[i - 1];
                ShipControlPointSpec b = points[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
                double distance = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance <= 1e-12) continue;
                double ux = dx / distance, uy = dy / distance, uz = dz / distance;
                double dt = (b.TimestampMs - a.TimestampMs) / 1000.0;
                for (int sample = 0; sample <= 100; sample++)
                {
                    double t = sample / 100.0;
                    double t2 = t * t;
                    double dh00 = (6 * t2) - (6 * t);
                    double dh10 = (3 * t2) - (4 * t) + 1;
                    double dh01 = (-6 * t2) + (6 * t);
                    double dh11 = (3 * t2) - (2 * t);
                    double vx = ((dh00 * a.X) + (dh10 * dt * a.Vx)
                        + (dh01 * b.X) + (dh11 * dt * b.Vx)) / dt;
                    double vy = ((dh00 * a.Y) + (dh10 * dt * a.Vy)
                        + (dh01 * b.Y) + (dh11 * dt * b.Vy)) / dt;
                    double vz = ((dh00 * a.Z) + (dh10 * dt * a.Vz)
                        + (dh01 * b.Z) + (dh11 * dt * b.Vz)) / dt;
                    double along = (vx * ux) + (vy * uy) + (vz * uz);
                    if (along < worstAlong)
                    {
                        worstAlong = along;
                        worstInterval = i;
                    }
                }
            }

            Assert.True(worstAlong >= -1e-9,
                "client spline reverses at interval " + worstInterval
                + " by " + worstAlong + " m/s");
        }

        [Fact]
        public void A_manned_idle_session_keeps_the_client_playback_buffer_alive()
        {
            // PathFollower starts its halting/spline-correction path as soon as
            // the buffer drains. The slow correction is five seconds for a small
            // yaw, so a held helm must keep publishing even before input begins.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();

            long now = 1_000_000;
            List<FlightEmit> first = Drive(session, ref now, 60);

            Assert.Equal(60, first.Count);
            for (int i = 1; i < first.Count; i++)
                Assert.Equal(StepMs, first[i].Spec.TimestampMs - first[i - 1].Spec.TimestampMs);
        }

        [Fact]
        public void Helm_prime_wakes_playback_without_moving_and_next_point_is_legal()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(10, 20, 30, 0.5));
            Assert.True(session.RequiresPlaybackPrimeOnMan);
            session.Man();

            FlightEmit prime = session.PrimePlayback(1_000_000, Step);
            FlightEmit next = session.Advance(1_000_240, Step, Tuning);

            Assert.True(prime.Emit);
            Assert.Equal(10, prime.Spec.X);
            Assert.Equal(20, prime.Spec.Y);
            Assert.Equal(30, prime.Spec.Z);
            Assert.Equal(0.5, session.State.YawRadians);
            Assert.True(ShipMotionPolicy.IsLegalSeparation(
                prime.Spec.TimestampMs, next.Spec.TimestampMs));
        }

        [Fact]
        public void Moving_hull_refuses_helm_prime_that_would_contradict_velocity()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(10, 20, 30, 0));
            session.Man();
            session.SetInput(new FlightControlInput(
                throttle: 1f, vertical: 0f, axisPitch: 0f, axisYaw: 0f, axisRoll: 0f));

            FlightEmit moving = session.Advance(1_000_000, Step, Tuning);

            Assert.True(moving.Emit);
            Assert.True(session.State.GroundSpeedMps > 0);
            Assert.False(session.RequiresPlaybackPrimeOnMan);

            session.Dismount();
            Assert.False(session.RequiresPlaybackPrimeOnMan);
        }

        [Fact]
        public void Every_consecutive_pair_is_legal_across_all_phases()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            List<long> stamps = new List<long>();
            long now = 1_000_000;

            void Collect(int ticks)
            {
                foreach (FlightEmit emit in Drive(session, ref now, ticks))
                {
                    stamps.Add(emit.Spec.TimestampMs);
                }
            }

            session.Man();
            session.SetInput(Throttle(1f));
            Collect(20);
            session.SetInput(Throttle(0f));
            session.Dismount();
            Collect(40);            // settle + rest + quiet
            now += 30_000;          // half a minute of silence
            Collect(1);             // remains quiet
            session.Man();          // re-man, fly again
            session.SetInput(Throttle(-1f));
            Collect(20);

            Assert.True(stamps.Count > 10);
            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.True(ShipMotionPolicy.IsLegalSeparation(stamps[i - 1], stamps[i]),
                    "pair " + i + ": " + stamps[i - 1] + " -> " + stamps[i]
                    + " violates the client's cadence floor");
            }
        }

        [Fact]
        public void Re_manning_resumes_from_the_flown_pose_not_the_seed()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));
            long now = 1_000_000;
            Drive(session, ref now, 20);
            session.SetInput(Throttle(0f));
            session.Dismount();
            Drive(session, ref now, 40);

            double flownZ = session.State.Z;
            Assert.True(flownZ > 0);

            session.Man();
            Drive(session, ref now, 2);
            Assert.True(session.State.Z >= flownZ, "re-manning must not reset the pose");
        }

        [Fact]
        public void The_idle_bob_keeps_a_manned_resting_ship_breathing()
        {
            // Opt-in only: with WAREBORN_FLIGHT_IDLE_BOB set, a manned idle ship
            // emits every tick (the 4 Hz cost the default avoids), the Y value
            // moves around the base altitude, and the points are honestly
            // not-arrived (they carry a real vertical velocity).
            FlightTuning bob = new FlightTuning(idleBobMetres: 0.3);
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();

            long now = 1_000_000;
            List<FlightEmit> emitted = new List<FlightEmit>();
            List<long> stamps = new List<long>();
            for (int i = 0; i < 30; i++)
            {
                now += StepMs;
                FlightEmit emit = session.Advance(now, Step, bob);
                if (emit.Emit)
                {
                    emitted.Add(emit);
                    stamps.Add(emit.Spec.TimestampMs);
                }
            }

            Assert.Equal(30, emitted.Count);
            Assert.Contains(emitted, e => e.Spec.Y > 100.0 + 0.05);
            Assert.Contains(emitted, e => e.Spec.Y < 100.0 - 0.05);
            Assert.All(emitted, e => Assert.False(e.Spec.Arrived));
            Assert.All(emitted, e => Assert.InRange(e.Spec.Y, 100.0 - 0.3 - 1e-9, 100.0 + 0.3 + 1e-9));
            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.True(ShipMotionPolicy.IsLegalSeparation(stamps[i - 1], stamps[i]));
            }

            // The base altitude itself never moved: dismounting settles back to it.
            Assert.Equal(100.0, session.State.Y, 9);
        }

        [Fact]
        public void A_clean_dismount_latches_forward_throttle_and_releases_steering_and_climb()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(new FlightControlInput(0.75f, 0.8f, -0.4f, 0.6f, -0.7f));
            session.Dismount();

            Assert.Equal(0.75f, session.Input.Throttle);
            Assert.Equal(0f, session.Input.Vertical);
            Assert.Equal(0f, session.Input.AxisPitch);
            Assert.Equal(0f, session.Input.AxisYaw);
            Assert.Equal(0f, session.Input.AxisRoll);
        }

        [Fact]
        public void A_nearly_centred_lever_settles_after_dismount_instead_of_cruising_forever()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(-0.001f));
            session.Dismount();

            Assert.True(session.Input.IsNeutral);
            Assert.True(session.State.IsAtRest);
            Assert.Equal(0f, session.Input.Throttle);
        }

        [Theory]
        [InlineData(1f, 1)]
        [InlineData(-1f, -1)]
        public void A_released_helm_keeps_its_latched_forward_or_reverse_command(float throttle, int expectedDirection)
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(throttle));
            session.Dismount(); // before even one integration tick: exercises the race boundary

            long now = 1_000_000;
            List<FlightEmit> emitted = Drive(session, ref now, 20);

            Assert.False(session.IsManned);
            Assert.Equal(throttle, session.Input.Throttle);
            Assert.Equal(expectedDirection, Math.Sign(session.State.Z));
            Assert.Equal(20, emitted.Count);
            Assert.False(session.State.IsAtRest);
        }

        [Fact]
        public void The_next_pilot_inherits_the_latched_lever_for_delta_merging()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(0.6f));
            session.Dismount();

            session.Man();

            Assert.Equal(0.6f, session.Input.Throttle);
        }

        [Fact]
        public void A_disconnect_abandons_stale_throttle_and_settles_safely()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));
            long now = 1_000_000;
            Drive(session, ref now, 20);

            session.Abandon();
            Drive(session, ref now, 40);

            Assert.True(session.Input.IsNeutral);
            Assert.True(session.State.IsAtRest);

            session.Man();
            Assert.True(session.Input.IsNeutral,
                "a reconnect after an unclean disconnect must not inherit ghost throttle");
        }

        [Fact]
        public void Dock_capture_snaps_pose_and_neutralizes_the_helm()
        {
            var session = new FlightSession(FlightState.AtRestAt(1, 2, 3));
            session.Man();
            session.SetInput(Throttle(1f));

            session.DockAt(10, 20, 30, 0.75);

            Assert.Equal(10, session.State.X);
            Assert.Equal(20, session.State.Y);
            Assert.Equal(30, session.State.Z);
            Assert.Equal(0.75, session.State.YawRadians);
            Assert.True(session.State.IsAtRest);
            Assert.True(session.Input.IsNeutral);
        }

        [Fact]
        public void Emergency_stop_preserves_pose_and_heading_but_clears_all_motion()
        {
            var moving = new FlightState(
                10, 20, 30, 0.75, 0.4, -0.2, 0.1, 8, 4, 1, 7);
            var session = new FlightSession(moving);

            session.EmergencyStop();

            Assert.Equal(10, session.State.X);
            Assert.Equal(20, session.State.Y);
            Assert.Equal(30, session.State.Z);
            Assert.Equal(0.75, session.State.YawRadians);
            Assert.True(session.State.IsAtRest);
            Assert.True(session.Input.IsNeutral);
            Assert.False(session.IsManned);
        }
    }

    public class PilotSeatsTests
    {
        [Fact]
        public void Manning_a_free_helm_seats_the_player()
        {
            PilotSeats seats = new PilotSeats();

            Assert.Equal(ManOutcome.StartPiloting, seats.TryMan(playerEntityId: 5, helmEntityId: 20, hullEntityId: 10));
            Assert.Equal(5, seats.PilotOf(10)!.Value.PlayerEntityId);
            Assert.Equal(10, seats.SeatOf(5)!.Value.HullEntityId);
            Assert.Equal(20, seats.SeatOf(5)!.Value.HelmEntityId);
        }

        [Fact]
        public void Duplicate_man_on_your_own_helm_is_idempotent()
        {
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);

            Assert.Equal(ManOutcome.AlreadyPiloting, seats.TryMan(5, 20, 10));
            Assert.Equal(5, seats.PilotOf(10)!.Value.PlayerEntityId);
            Assert.Equal(10, seats.SeatOf(5)!.Value.HullEntityId);
        }

        [Fact]
        public void A_second_player_is_rejected_while_the_helm_is_held()
        {
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);

            Assert.Equal(ManOutcome.RejectedOccupied, seats.TryMan(6, 20, 10));
            Assert.Equal(5, seats.PilotOf(10)!.Value.PlayerEntityId);
        }

        [Fact]
        public void A_pilot_cannot_take_a_second_hull()
        {
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);

            Assert.Equal(ManOutcome.RejectedAlreadyPiloting, seats.TryMan(5, 21, 11));
            Assert.Equal(10, seats.SeatOf(5)!.Value.HullEntityId);
        }

        [Fact]
        public void Release_frees_the_seat_and_reports_it()
        {
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);

            PilotSeats.Seat? released = seats.Release(5);
            Assert.NotNull(released);
            Assert.Equal(10, released!.Value.HullEntityId);
            Assert.Null(seats.PilotOf(10));
            Assert.Equal(0, seats.Count);

            Assert.Null(seats.Release(5));
        }

        [Fact]
        public void After_a_release_the_helm_is_free_for_the_next_pilot()
        {
            // The disconnect path: the seat must not stay held by a gone player.
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);
            seats.Release(5);

            Assert.Equal(ManOutcome.StartPiloting, seats.TryMan(6, 20, 10));
        }
    }
}
