using System;
using System.IO;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class FixedFlightClockTests
    {
        [Fact]
        public void Jitter_accumulates_whole_20ms_steps_without_drift()
        {
            var clock = new FixedFlightClock();
            Assert.Equal(0, clock.Advance(TimeSpan.Zero).Steps);
            Assert.Equal(0, clock.Advance(TimeSpan.FromMilliseconds(7)).Steps);
            Assert.Equal(1, clock.Advance(TimeSpan.FromMilliseconds(21)).Steps);
            Assert.Equal(2, clock.Advance(TimeSpan.FromMilliseconds(61)).Steps);
            FixedFlightStepBatch final = clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Equal(2, final.Steps);
            Assert.Equal(5, final.CompletedSteps);
            Assert.InRange(final.RemainderSeconds, 0.0, 0.0000001);
        }

        [Fact]
        public void Deliberate_stall_is_capped_and_reports_dropped_pressure()
        {
            var clock = new FixedFlightClock(maxCatchUpSteps: 5);
            clock.Advance(TimeSpan.Zero);
            FixedFlightStepBatch batch = clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(5, batch.Steps);
            Assert.Equal(45, batch.DroppedSteps);
            Assert.Equal(45, batch.TotalDroppedSteps);
            Assert.Equal(1, batch.PressureEvents);
            Assert.True(batch.UnderPressure);
            Assert.Equal(0, clock.Advance(TimeSpan.FromSeconds(1)).Steps);
        }

        [Fact]
        public void Different_poll_jitter_produces_the_same_authoritative_state_hash()
        {
            FlightState a = Run(new[] { 12, 12, 12, 12 });
            FlightState b = Run(new[] { 3, 9, 1, 11, 7, 17 });
            Assert.Equal(Hash(a), Hash(b));
        }

        [Fact]
        public void Twelve_physics_steps_still_make_one_network_emission_decision()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(new FlightControlInput(1, 0, 0, 0, 0));
            FlightEmit emit = session.AdvanceFixed(1_000_000, 0.24, 12, 0.02,
                new FlightTuning());
            Assert.True(emit.Emit);
            Assert.True(session.State.Z > 0);
            Assert.Equal(1_000_000, emit.Spec.TimestampMs);
        }

        [Fact]
        public void Zero_step_clock_initialization_does_not_consume_sail_wake()
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            var propulsion = new ShipPropulsion(3094, 0, 2);
            session.WakeForCanvas();
            session.AdvanceFixed(1_000_000, 0.24, 0, 0,
                new FlightTuning(), 2, propulsion: propulsion);
            Assert.True(session.State.IsAtRest);

            session.AdvanceFixed(1_000_240, 0.24, 12, 0.02,
                new FlightTuning(), 2, propulsion: propulsion);
            Assert.False(session.State.IsAtRest);
        }

        [Fact]
        public void Durable_snapshot_round_trips_but_restart_neutralizes_pilot_and_epoch()
        {
            var live = new ShipDomain(70, 3,
                new FlightSession(FlightState.AtRestAt(10, 20, 30)));
            ShipAuthorityToken stale = live.AcquirePilot(100, 80);
            Assert.True(live.TrySetInput(stale, new FlightControlInput(0.75f, 0.2f, 0, 0.3f, 0)));
            live.Flight.AdvanceFixed(1_000_000, 0.24, 12, 0.02, new FlightTuning());
            DurableShipFlightSnapshot durable = DurableShipFlightSnapshot.Capture(
                live.Flight.State, live.Flight.Input, live.Generation.Value,
                wasManned: true, aboardCount: 2, wasDocked: false, unfurledSailCount: 1);

            string path = Path.Combine(Path.GetTempPath(), "wareborn-flight-" + Guid.NewGuid() + ".json");
            try
            {
                Assert.True(AtomicJsonFile.Write(path, durable));
                DurableShipFlightSnapshot loaded = AtomicJsonFile.Read<DurableShipFlightSnapshot>(path)!;
                Assert.True(loaded.TryRead(out FlightState state, out FlightControlInput input));
                Assert.Equal(0.75f, input.Throttle);

                ShipDomain restored = ShipDomain.RestoreAfterProcessRestart(70, 3,
                    new AuthorityGeneration(loaded.AuthorityGeneration), new FlightSession(state));
                Assert.Null(restored.Pilot);
                Assert.True(restored.Flight.Input.IsNeutral);
                Assert.Equal(live.Generation.Value + 1, restored.Generation.Value);
                Assert.False(restored.TrySetInput(stale, new FlightControlInput(1, 0, 0, 0, 0)));
                Assert.Equal(live.Flight.State.X, restored.Flight.State.X, 12);
                Assert.Equal(live.Flight.State.VzMps, restored.Flight.State.VzMps, 12);
                double beforeResumeZ = restored.Flight.State.Z;
                restored.Flight.AdvanceFixed(1_000_240, 0.24, 12, 0.26,
                    new FlightTuning());
                Assert.True(restored.Flight.State.Z > beforeResumeZ,
                    "restored momentum must resume even though stale controls are neutralized");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void Legacy_and_corrupt_snapshots_fail_closed_to_the_pose_seam()
        {
            BuiltShipRecord legacy = JsonSerializer.Deserialize<BuiltShipRecord>(
                "{\"HullX\":1,\"HullY\":2,\"HullZ\":3,\"HullYawRadians\":0.5}")!;
            Assert.Null(legacy.FlightSnapshot);

            var corrupt = new DurableShipFlightSnapshot
            {
                Version = 999,
                AuthorityGeneration = 3,
                X = double.NaN,
            };
            Assert.False(corrupt.TryRead(out _, out _));

            var modern = new WorldStateSnapshot();
            modern.BuiltShips.Add(new BuiltShipRecord
            {
                HullX = 4,
                FlightSnapshot = DurableShipFlightSnapshot.Capture(
                    FlightState.AtRestAt(1, 2, 3, 0.4),
                    new FlightControlInput(0.5f, 0, 0, 0, 0),
                    8, false, 0, false, 2),
            });
            WorldStateSnapshot modernCopy = JsonSerializer.Deserialize<WorldStateSnapshot>(
                JsonSerializer.Serialize(modern))!;
            Assert.NotNull(modernCopy.BuiltShips[0].FlightSnapshot);
            Assert.Equal(8, modernCopy.BuiltShips[0].FlightSnapshot!.AuthorityGeneration);
        }

        [Theory]
        [InlineData(long.MaxValue)]
        [InlineData(long.MaxValue - 1)]
        public void Exhausted_or_nearly_exhausted_authority_epoch_fails_closed(long generation)
        {
            var snapshot = DurableShipFlightSnapshot.Capture(
                FlightState.AtRestAt(1, 2, 3), FlightControlInput.Neutral,
                generation, false, 0, false, 0);

            Assert.False(snapshot.TryRead(out _, out _));
        }

        [Fact]
        public void Truncated_durable_file_is_quarantined_not_partially_loaded()
        {
            string path = Path.Combine(Path.GetTempPath(), "wareborn-flight-bad-" + Guid.NewGuid() + ".json");
            File.WriteAllText(path, "{\"Version\":1,\"X\":");
            try
            {
                Assert.Null(AtomicJsonFile.Read<DurableShipFlightSnapshot>(path));
                Assert.False(File.Exists(path));
                Assert.True(File.Exists(path + ".broken"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".broken")) File.Delete(path + ".broken");
            }
        }

        private static FlightState Run(int[] batches)
        {
            var session = new FlightSession(FlightState.AtRestAt(0, 100, 0));
            session.Man();
            session.SetInput(new FlightControlInput(0.8f, 0.2f, 0, 0.4f, 0));
            long step = 1;
            foreach (int count in batches)
            {
                session.AdvanceFixed(1_000_000 + (step * 20), 0.24, count,
                    step * FixedFlightClock.StepSeconds, new FlightTuning());
                step += count;
            }
            return session.State;
        }

        private static string Hash(FlightState s) => string.Join("|",
            s.X.ToString("R"), s.Y.ToString("R"), s.Z.ToString("R"),
            s.YawRadians.ToString("R"), s.YawRateRadPerSec.ToString("R"),
            s.RollRadians.ToString("R"), s.PitchRadians.ToString("R"),
            s.SpeedCmdMps.ToString("R"), s.VxMps.ToString("R"),
            s.VyMps.ToString("R"), s.VzMps.ToString("R"));
    }
}
