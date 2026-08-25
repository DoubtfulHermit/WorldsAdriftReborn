using Newtonsoft.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    public sealed class DurableVectorFlightStateTests
    {
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
