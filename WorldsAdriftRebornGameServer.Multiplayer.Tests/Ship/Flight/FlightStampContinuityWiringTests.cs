using System;
using System.Collections.Generic;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// The turn-vibration correction end to end: the session honours the opt-in,
    /// the service passes it, the steering latch is untouched, and no new per-part
    /// publication was invented to get there.
    /// </summary>
    public class FlightStampContinuityWiringTests
    {
        private const double Step = ShipMotionPolicy.SendIntervalSeconds;
        private const long StepMs = 240;
        private static readonly FlightTuning Tuning = new FlightTuning();

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
            {
                dir = dir.Parent;
            }
            Assert.NotNull(dir);
            return dir!.FullName;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static FlightControlInput HardLeft() =>
            new FlightControlInput(1f, 0f, 0f, -1f, 0f);

        /// <summary>
        /// One hull held in a full turn, driven exactly as the legacy branch of
        /// <c>ShipFlightService.Tick</c> drives it - one <c>Advance</c> per due
        /// cadence tick - but with the poll landing late by a different amount
        /// each time, which is what the real ENet loop does.
        /// </summary>
        private static (List<long> Stamps, List<double> Yaws) FlyATurn(
            bool stampContinuity, IReadOnlyList<long> jitterMs)
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(HardLeft());

            var stamps = new List<long>();
            var yaws = new List<double>();
            long ideal = 5_000_000;
            foreach (long jitter in jitterMs)
            {
                ideal += StepMs;
                FlightEmit emit = session.Advance(
                    ideal + jitter, Step, Tuning, stampContinuity: stampContinuity);
                Assert.True(emit.Emit);
                stamps.Add(emit.Spec.TimestampMs);
                yaws.Add(session.State.YawRadians);
            }
            return (stamps, yaws);
        }

        private static readonly long[] RealisticJitter = { 0, 37, 4, 41, 12, 48, 3, 29, 19, 44 };

        [Fact]
        public void Simulated_yaw_advances_by_the_same_amount_on_every_point()
        {
            // The premise of the whole diagnosis: the legacy path integrates a
            // CONSTANT step, so any unevenness on the wire is a stamping artefact
            // and never a physics one.
            (_, List<double> yaws) = FlyATurn(stampContinuity: false, RealisticJitter);

            var deltas = new List<double>();
            for (int i = 1; i < yaws.Count; i++)
            {
                deltas.Add(yaws[i] - yaws[i - 1]);
            }
            // Skip the ramp: the turn rate eases in, then holds.
            for (int i = deltas.Count / 2 + 1; i < deltas.Count; i++)
            {
                Assert.Equal(deltas[^1], deltas[i], 9);
            }
        }

        [Fact]
        public void Off_the_wire_interval_wobbles_under_poll_jitter()
        {
            (List<long> stamps, _) = FlyATurn(stampContinuity: false, RealisticJitter);

            var deltas = new List<long>();
            for (int i = 1; i < stamps.Count; i++)
            {
                deltas.Add(stamps[i] - stamps[i - 1]);
            }
            Assert.Contains(deltas, d => d != StepMs);
        }

        [Fact]
        public void On_the_wire_interval_matches_the_simulated_interval_exactly()
        {
            (List<long> stamps, _) = FlyATurn(stampContinuity: true, RealisticJitter);

            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.Equal(StepMs, stamps[i] - stamps[i - 1]);
            }
        }

        [Fact]
        public void On_every_point_still_clears_the_client_reject_floor()
        {
            (List<long> stamps, _) = FlyATurn(stampContinuity: true, RealisticJitter);

            for (int i = 1; i < stamps.Count; i++)
            {
                Assert.True(ShipMotionPolicy.IsLegalSeparation(stamps[i - 1], stamps[i]));
            }
        }

        [Fact]
        public void The_opt_in_changes_only_the_stamp_never_the_pose()
        {
            (List<long> offStamps, List<double> offYaws) =
                FlyATurn(stampContinuity: false, RealisticJitter);
            (List<long> onStamps, List<double> onYaws) =
                FlyATurn(stampContinuity: true, RealisticJitter);

            Assert.Equal(offYaws, onYaws);
            Assert.NotEqual(offStamps, onStamps);
        }

        [Fact]
        public void Default_is_off_so_an_unset_server_is_byte_identical()
        {
            FlightSession baseline = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            baseline.Man();
            baseline.SetInput(HardLeft());
            FlightSession explicitOff = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            explicitOff.Man();
            explicitOff.SetInput(HardLeft());

            long now = 5_000_000;
            for (int i = 0; i < 8; i++)
            {
                now += StepMs + (i * 7);
                FlightEmit a = baseline.Advance(now, Step, Tuning);
                FlightEmit b = explicitOff.Advance(now, Step, Tuning, stampContinuity: false);
                Assert.Equal(a.Spec.TimestampMs, b.Spec.TimestampMs);
                Assert.Equal(a.PackedRotation, b.PackedRotation);
            }
        }

        // ---- the steering latch, which this correction must not touch ----

        [Fact]
        public void The_server_never_recentres_a_held_steering_axis()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(HardLeft());

            long now = 5_000_000;
            for (int i = 0; i < 40; i++)
            {
                now += StepMs;
                session.Advance(now, Step, Tuning, stampContinuity: true);
                Assert.Equal(-1f, session.Input.AxisYaw);
            }
            Assert.True(session.State.YawRateRadPerSec != 0.0,
                "a latched steering axis must keep the hull turning");
        }

        [Fact]
        public void Only_an_explicit_new_input_moves_the_latched_axis()
        {
            FlightSession session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(HardLeft());
            long now = 5_000_000;
            now += StepMs;
            session.Advance(now, Step, Tuning, stampContinuity: true);

            // The client diff-suppresses an unchanged 1111, so "no packet" means
            // "still held" and the server must not decay it.
            for (int i = 0; i < 20; i++)
            {
                now += StepMs;
                session.Advance(now, Step, Tuning, stampContinuity: true);
            }
            Assert.Equal(-1f, session.Input.AxisYaw);

            session.SetInput(new FlightControlInput(1f, 0f, 0f, 1f, 0f));
            Assert.Equal(1f, session.Input.AxisYaw);
        }

        // ---- source contracts ----

        [Fact]
        public void The_service_gates_stamp_continuity_off_by_default_and_wires_the_legacy_branch()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

            Assert.Contains("WAREBORN_FLIGHT_STAMP_CONTINUITY", service, StringComparison.Ordinal);
            Assert.Contains("Environment.GetEnvironmentVariable(\"WAREBORN_FLIGHT_STAMP_CONTINUITY\") == \"1\"",
                service, StringComparison.Ordinal);
            Assert.Contains("stampContinuity: StampContinuityEnabled", service, StringComparison.Ordinal);

            // It must ride the LEGACY publisher only; the fixed-step branch already
            // phase-locks and passing both would be a half-applied rule.
            Assert.DoesNotContain("stampContinuity: true", service, StringComparison.Ordinal);
        }

        [Fact]
        public void The_correction_invents_no_new_per_part_publication()
        {
            string service = Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

            // Active flight still publishes exactly one hull pose authority and the
            // unchanged mounted-member wakes. Nothing about the stamp fix may add a
            // hull 190602 beside the 1130 stream - that was the fault the
            // single-hull-pose-authority correction removed.
            Assert.Contains("rootAuxiliary: null", service, StringComparison.Ordinal);
            Assert.DoesNotContain("rootAuxiliary: wake", service, StringComparison.Ordinal);
        }

        [Fact]
        public void A_control_point_still_carries_no_angular_velocity()
        {
            // This is WHY an uneven stamp is a turn-rate error: the client can
            // hermite the position with real tangents but has only the endpoint
            // attitudes to slerp between. If a future change ever puts angular
            // velocity on the wire, this test should be revisited deliberately.
            string integrator = Source("WorldsAdriftRebornGameServer.Multiplayer",
                "Ship", "Flight", "FlightIntegrator.cs");
            int start = integrator.IndexOf("public static ShipControlPointSpec ToControlPoint",
                StringComparison.Ordinal);
            Assert.True(start > 0);
            string body = integrator.Substring(start, 400);

            Assert.Contains("state.VxMps", body, StringComparison.Ordinal);
            Assert.DoesNotContain("YawRate", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Roll", body, StringComparison.Ordinal);
        }
    }
}
