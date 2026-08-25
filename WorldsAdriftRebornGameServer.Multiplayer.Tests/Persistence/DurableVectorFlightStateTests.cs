using System;
using System.IO;
using Newtonsoft.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    public sealed class DurableVectorFlightStateTests : IDisposable
    {
        private readonly string _dir;

        public DurableVectorFlightStateTests()
        {
            _dir = Path.Combine(Path.GetTempPath(),
                "wareborn-durablevector-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static FlightState BasePose() => new FlightState(
            10, 300, -5, 0.4, 0.02, -0.03, 0.01, 0, 1.0, -0.2, 3.0);

        [Fact]
        public void Capture_and_read_roundtrip_the_vector_state_exactly()
        {
            VectorFlightState original = VectorFlightRuntime.FromFlightState(BasePose())
                with
            {
                CommandLiftForceNewtons = 123.5,
                CommandLiftSmoothingVelocity = -4.25,
            };

            DurableVectorFlightState durable = DurableVectorFlightState.Capture(original);
            Assert.True(durable.TryRead(BasePose(), out VectorFlightState restored));

            Assert.Equal(original.Position, restored.Position);
            Assert.Equal(original.VelocityMps, restored.VelocityMps);
            Assert.Equal(original.AngularVelocityRadPerSec, restored.AngularVelocityRadPerSec);
            Assert.Equal(original.CommandLiftForceNewtons, restored.CommandLiftForceNewtons);
            Assert.Equal(original.CommandLiftSmoothingVelocity, restored.CommandLiftSmoothingVelocity);
        }

        [Fact]
        public void An_unsupported_version_fails_closed()
        {
            DurableVectorFlightState durable = DurableVectorFlightState.Capture(
                VectorFlightRuntime.FromFlightState(BasePose()));
            durable.Version = 99;

            Assert.False(durable.TryRead(BasePose(), out _));
        }

        [Fact]
        public void A_corrupt_quaternion_fails_closed()
        {
            var durable = new DurableVectorFlightState { QW = 0, QX = 0, QY = 0, QZ = 0 };

            Assert.False(durable.TryRead(BasePose(), out _));
        }

        [Fact]
        public void Non_finite_angular_state_fails_closed()
        {
            var durable = new DurableVectorFlightState { AngVyRadPerSec = double.NaN };

            Assert.False(durable.TryRead(BasePose(), out _));
        }

        [Fact]
        public void A_pre_vector_v1_snapshot_still_reads_and_carries_no_vector_state()
        {
            // Exactly what sits on disk today: a version-1 durable flight record
            // with no Vector property at all. The base restore must be untouched.
            string legacyJson = @"{
                ""Version"": 1, ""AuthorityGeneration"": 4, ""WasManned"": true,
                ""AboardCount"": 1, ""WasDocked"": false, ""UnfurledSailCount"": 2,
                ""X"": 10.0, ""Y"": 300.0, ""Z"": -5.0, ""YawRadians"": 0.4,
                ""YawRateRadPerSec"": 0.02, ""RollRadians"": -0.03, ""PitchRadians"": 0.01,
                ""SpeedCmdMps"": 6.0, ""VxMps"": 1.0, ""VyMps"": -0.2, ""VzMps"": 3.0,
                ""Throttle"": 0.5, ""Vertical"": 0.0, ""AxisPitch"": 0.0,
                ""AxisYaw"": 0.1, ""AxisRoll"": 0.0 }";

            DurableShipFlightSnapshot? snapshot =
                JsonConvert.DeserializeObject<DurableShipFlightSnapshot>(legacyJson);

            Assert.NotNull(snapshot);
            Assert.Null(snapshot!.Vector);
            Assert.True(snapshot.TryRead(out FlightState state, out _));
            Assert.Equal(300.0, state.Y);
        }

        [Fact]
        public void A_snapshot_written_with_every_gate_off_carries_no_vector_property_at_all()
        {
            // Byte-identical OFF path: the persistence serializer (System.Text.Json,
            // AtomicJsonFile) must omit the null extension entirely, so a world
            // state written with the gates off matches one written before the
            // field existed.
            DurableShipFlightSnapshot snapshot = DurableShipFlightSnapshot.Capture(
                BasePose(), FlightControlInput.Neutral, authorityGeneration: 3,
                wasManned: false, aboardCount: 0, wasDocked: false, unfurledSailCount: 0);

            string json = System.Text.Json.JsonSerializer.Serialize(snapshot);

            Assert.DoesNotContain("Vector", json);
        }

        [Fact]
        public void The_production_serializer_roundtrips_the_vector_extension_losslessly()
        {
            // The restart path serialises with System.Text.Json through
            // AtomicJsonFile - NOT the Newtonsoft the facts above use - so the
            // lossless-restore proof must go through THAT serializer: full
            // quaternion, body angular velocity, and the invisible lift command
            // smoothing pair, byte-exact doubles after a disk trip.
            VectorFlightState flown = VectorFlightRuntime.FromFlightState(BasePose())
                with
            {
                AngularVelocityRadPerSec = new ShadowVector3(0.011, 0.02, -0.007),
                CommandLiftForceNewtons = 123.5 + Math.PI,
                CommandLiftSmoothingVelocity = -4.25 / 3.0,
            };
            DurableShipFlightSnapshot snapshot = DurableShipFlightSnapshot.Capture(
                BasePose(), new FlightControlInput(0.5f, 0.25f, 0f, 0.1f, 0f),
                authorityGeneration: 3, wasManned: true, aboardCount: 1,
                wasDocked: false, unfurledSailCount: 2);
            snapshot.Vector = DurableVectorFlightState.Capture(flown);

            string path = Path.Combine(_dir, "flight.json");
            Assert.True(AtomicJsonFile.Write(path, snapshot));
            DurableShipFlightSnapshot? restored =
                AtomicJsonFile.Read<DurableShipFlightSnapshot>(path);

            Assert.NotNull(restored);
            Assert.NotNull(restored!.Vector);
            // Version survives the trip.
            Assert.Equal(DurableVectorFlightState.CurrentVersion, restored.Vector!.Version);
            // Raw stored doubles are exact - System.Text.Json's shortest
            // round-trip formatting must lose nothing.
            Assert.Equal(snapshot.Vector.QW, restored.Vector.QW);
            Assert.Equal(snapshot.Vector.QX, restored.Vector.QX);
            Assert.Equal(snapshot.Vector.QY, restored.Vector.QY);
            Assert.Equal(snapshot.Vector.QZ, restored.Vector.QZ);
            Assert.Equal(snapshot.Vector.AngVxRadPerSec, restored.Vector.AngVxRadPerSec);
            Assert.Equal(snapshot.Vector.AngVyRadPerSec, restored.Vector.AngVyRadPerSec);
            Assert.Equal(snapshot.Vector.AngVzRadPerSec, restored.Vector.AngVzRadPerSec);
            Assert.Equal(snapshot.Vector.CommandLiftForceNewtons,
                restored.Vector.CommandLiftForceNewtons);
            Assert.Equal(snapshot.Vector.CommandLiftSmoothingVelocity,
                restored.Vector.CommandLiftSmoothingVelocity);
            // And the state the runtime resumes with is IDENTICAL to the one an
            // in-memory read of the same capture yields: the disk trip added
            // exactly nothing.
            Assert.True(restored.TryRead(out FlightState restoredBase, out _));
            Assert.True(snapshot.TryRead(out FlightState inMemoryBase, out _));
            Assert.True(snapshot.Vector.TryRead(inMemoryBase, out VectorFlightState inMemory));
            Assert.True(restored.Vector.TryRead(restoredBase, out VectorFlightState fromDisk));
            Assert.Equal(inMemory, fromDisk);
        }

        [Fact]
        public void The_production_serializer_omits_a_null_vector_and_restores_it_null()
        {
            DurableShipFlightSnapshot snapshot = DurableShipFlightSnapshot.Capture(
                BasePose(), FlightControlInput.Neutral, authorityGeneration: 3,
                wasManned: false, aboardCount: 0, wasDocked: false, unfurledSailCount: 0);

            string path = Path.Combine(_dir, "scalar-only.json");
            Assert.True(AtomicJsonFile.Write(path, snapshot));

            // The OFF-path byte-identity contract, on the REAL file: no Vector
            // property at all, and the read side restores it as null.
            Assert.DoesNotContain("Vector", File.ReadAllText(path));
            DurableShipFlightSnapshot? restored =
                AtomicJsonFile.Read<DurableShipFlightSnapshot>(path);
            Assert.NotNull(restored);
            Assert.Null(restored!.Vector);
            Assert.True(restored.TryRead(out FlightState state, out _));
            Assert.Equal(300.0, state.Y);
        }

        [Fact]
        public void An_unsupported_vector_version_still_fails_closed_after_the_disk_trip()
        {
            DurableShipFlightSnapshot snapshot = DurableShipFlightSnapshot.Capture(
                BasePose(), FlightControlInput.Neutral, authorityGeneration: 3,
                wasManned: false, aboardCount: 0, wasDocked: false, unfurledSailCount: 0);
            snapshot.Vector = DurableVectorFlightState.Capture(
                VectorFlightRuntime.FromFlightState(BasePose()));
            snapshot.Vector.Version = 99;

            string path = Path.Combine(_dir, "future-version.json");
            Assert.True(AtomicJsonFile.Write(path, snapshot));
            DurableShipFlightSnapshot? restored =
                AtomicJsonFile.Read<DurableShipFlightSnapshot>(path);

            Assert.NotNull(restored!.Vector);
            Assert.Equal(99, restored.Vector!.Version);
            // The base pose still restores; only the vector extension refuses,
            // and the caller falls back to seeding from the scalar state.
            Assert.True(restored.TryRead(out FlightState basePose, out _));
            Assert.False(restored.Vector.TryRead(basePose, out _));
        }

        [Fact]
        public void The_vector_extension_rides_the_base_snapshot_additively()
        {
            DurableShipFlightSnapshot snapshot = DurableShipFlightSnapshot.Capture(
                BasePose(), FlightControlInput.Neutral, authorityGeneration: 3,
                wasManned: false, aboardCount: 0, wasDocked: false, unfurledSailCount: 0);
            snapshot.Vector = DurableVectorFlightState.Capture(
                VectorFlightRuntime.FromFlightState(BasePose()));

            string json = JsonConvert.SerializeObject(snapshot);
            DurableShipFlightSnapshot? roundTripped =
                JsonConvert.DeserializeObject<DurableShipFlightSnapshot>(json);

            Assert.NotNull(roundTripped!.Vector);
            Assert.True(roundTripped.TryRead(out FlightState basePose, out _));
            Assert.True(roundTripped.Vector!.TryRead(basePose, out VectorFlightState vector));
            Assert.Equal(basePose.X, vector.Position.X);
        }
    }
}
