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
        public void Dismount_settles_to_rest_then_repeats_then_goes_quiet()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));

            long now = 1_000_000;
            Drive(session, ref now, 20); // up to speed
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
        public void After_rest_a_keepalive_point_still_goes_out()
        {
            // The late-joiner correction: a client that connects after the flight
            // seeds this hull at SPAWN, and only a live point moves it. So the
            // session must never go permanently silent once it has flown.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));
            long now = 1_000_000;
            Drive(session, ref now, 10);
            session.Dismount();
            Drive(session, ref now, 60); // settle + repeats + quiet

            // A keepalive interval later, one more point.
            now += (long)(Tuning.RestKeepaliveSeconds * 1000) + StepMs;
            FlightEmit keepalive = session.Advance(now, Step, Tuning);

            Assert.True(keepalive.Emit, "the keepalive must emit after the configured gap");
            Assert.True(keepalive.Spec.Arrived);
        }

        [Fact]
        public void A_manned_idle_session_drops_to_the_keepalive_cadence()
        {
            // A pilot standing at the helm without touching anything must not
            // cost 4 Hz forever.
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();

            long now = 1_000_000;
            List<FlightEmit> first = Drive(session, ref now, 60);

            Assert.True(first.Count <= FlightSession.RestRepeats + 2,
                "an idle manned helm emitted " + first.Count + " points in 60 ticks");
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
            session.Dismount();
            Collect(40);            // settle + rest + quiet
            now += 30_000;          // half a minute of silence
            Collect(1);             // keepalive
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
        public void Manning_clears_stale_input_from_the_previous_pilot()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(Throttle(1f));
            session.Dismount();
            session.Man();

            Assert.True(session.Input.IsNeutral,
                "a fresh pilot must not inherit the previous pilot's throttle");
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
        public void Manning_your_own_helm_again_is_the_dismount_toggle()
        {
            PilotSeats seats = new PilotSeats();
            seats.TryMan(5, 20, 10);

            Assert.Equal(ManOutcome.StopPiloting, seats.TryMan(5, 20, 10));
            Assert.Null(seats.PilotOf(10));
            Assert.Null(seats.SeatOf(5));
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
