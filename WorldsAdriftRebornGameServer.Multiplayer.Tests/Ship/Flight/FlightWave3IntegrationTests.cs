using System.Text;
using System.Text.Json;
using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>Cross-track gates for fixed flight, fuel, wall shadow and workers.</summary>
    public sealed class FlightWave3IntegrationTests
    {
        [Fact]
        public void Restart_preserves_generator_tanks_but_never_resurrects_latched_combustion()
        {
            var live = new ShipDomain(70, 0,
                new FlightSession(FlightState.AtRestAt(10, 20, 30)));
            ShipAuthorityToken token = live.AcquirePilot(100, 80);
            Assert.True(live.TrySetInput(token,
                new FlightControlInput(0.5f, 0, 0, 0, 0)));
            live.Flight.AdvanceFixed(1_000_000, 0.24, 12, 0.02,
                new FlightTuning());
            Assert.True(live.ReleasePilot(token, abandoned: false));
            Assert.Equal(0.5f, live.Flight.Input.Throttle);

            var fuel = new ShipFuelLedger();
            Assert.True(fuel.RegisterAt(701, 70, 100, 80));
            Assert.True(fuel.RegisterAt(702, 70, 100, 20));
            fuel.SetDemand(70, new HullPropulsionDemand(live.Flight.Input.Throttle, 2));
            fuel.Burn(seconds: 2, burnPerSecond: 5);
            Assert.Equal(90, fuel.Read(70).Level);

            var durableWorld = new WorldStateSnapshot();
            durableWorld.BuiltShips.Add(new BuiltShipRecord
            {
                FlightSnapshot = DurableShipFlightSnapshot.Capture(
                    live.Flight.State, live.Flight.Input, live.Generation.Value,
                    wasManned: false, aboardCount: 0, wasDocked: false,
                    unfurledSailCount: 0),
            });
            durableWorld.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "generator-a",
                BuiltShipIndex = 0,
                ItemType = "powerGenerator",
                GeneratorFuel = fuel.CaptureGenerator(701),
            });
            durableWorld.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "generator-b",
                BuiltShipIndex = 0,
                ItemType = "powerGenerator01",
                GeneratorFuel = fuel.CaptureGenerator(702),
            });

            WorldStateSnapshot loaded = JsonSerializer.Deserialize<WorldStateSnapshot>(
                JsonSerializer.Serialize(durableWorld))!;
            DurableShipFlightSnapshot flight = loaded.BuiltShips[0].FlightSnapshot!;
            Assert.True(flight.TryRead(out FlightState state,
                out FlightControlInput preRestartEvidence));
            Assert.Equal(0.5f, preRestartEvidence.Throttle);

            ShipDomain restored = ShipDomain.RestoreAfterProcessRestart(
                70, 0, new AuthorityGeneration(flight.AuthorityGeneration),
                new FlightSession(state));
            Assert.True(restored.Flight.Input.IsNeutral);
            Assert.Equal(live.Generation.Value + 1, restored.Generation.Value);

            var restoredFuel = new ShipFuelLedger();
            foreach ((MountedPartRecord record, long entityId) in
                loaded.MountedParts.Zip(new long[] { 1701, 1702 }))
            {
                Assert.NotNull(record.GeneratorFuel);
                Assert.True(record.GeneratorFuel!.TryRestore(100, out FuelReading reading));
                Assert.True(restoredFuel.RegisterAt(entityId, 70,
                    reading.Capacity, reading.Level));
            }
            Assert.Equal(90, restoredFuel.Read(70).Level);
            restoredFuel.SetDemand(70,
                new HullPropulsionDemand(restored.Flight.Input.Throttle, 2));
            restoredFuel.Burn(seconds: 10, burnPerSecond: 100);
            Assert.Equal(90, restoredFuel.Read(70).Level);
        }

        [Fact]
        public void Fixed_clock_tick_produces_deterministic_wall_intents_across_poll_jitter()
        {
            long regularTicks = CompletedTicks(0, 240, 480, 720, 960);
            long jitteredTicks = CompletedTicks(0, 70, 190, 400, 610, 770, 960);
            Assert.Equal(48, regularTicks);
            Assert.Equal(regularTicks, jitteredTicks);

            string[] regular = WallIntentsThrough(regularTicks);
            string[] jittered = WallIntentsThrough(jitteredTicks);
            Assert.NotEmpty(regular);
            Assert.Equal(regular, jittered);
        }

        [Fact]
        public void Reverse_index_snapshot_lifecycle_does_not_promote_a_protocol_model_to_live_owner()
        {
            var local = new ShipDomain(70, 0,
                new FlightSession(FlightState.AtRestAt(0, 100, 0)));
            local.ReplaceMembers(new long[] { 71 }, new long[] { 72 });
            ShipAuthorityToken token = local.AcquirePilot(100, 80);
            Assert.True(local.ReleasePilot(token, abandoned: true));
            ShipDomainSnapshot logical = local.Capture();

            var host = new LocalDomainHost();
            host.Register(local);
            Assert.True(host.RemoveDomain(local.Id));
            ShipDomain restored = ShipDomain.Restore(logical);
            host.Register(restored);
            Assert.Empty(host.EnsureComplete(new long[] { 70, 71, 72 }).Inconsistencies);

            var workerA = new WorkerId("local:primary");
            var workerB = new WorkerId("candidate:b");
            var stamp = new DomainAuthorityStamp(restored.Id, workerA, restored.Generation);
            byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                domain = restored.Id.Value,
                generation = restored.Generation.Value,
                entities = restored.EntityIds,
            }));
            CommittedDomainSnapshot committed = CommittedDomainSnapshot.Create(stamp, 4, payload);
            var recovery = new DomainRecoveryModel(stamp);
            recovery.Commit(committed);
            recovery.Revoke(workerA);
            recovery.BeginRestore(workerB);
            recovery.MarkReady(workerB, committed.Sha256);
            DomainAuthorityStamp candidate = recovery.Promote(workerB);

            Assert.Equal(restored.Generation.Next(), candidate.Generation);
            Assert.Equal(logical.Generation, restored.Generation);
            Assert.Same(restored, host.ById(restored.Id));
            Assert.All(restored.EntityIds,
                entityId => Assert.Equal(restored.Id, host.OwnerOf(entityId)));
        }

        [Fact]
        public void Runtime_has_one_copy_of_shared_primitives_and_no_live_wall_or_worker_wiring()
        {
            string root = RepoRoot();
            string[] runtimeFiles = Directory.EnumerateFiles(root, "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(".Tests", StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                .ToArray();
            string runtime = string.Join("\n", runtimeFiles.Select(File.ReadAllText));
            Assert.Single(Occurrences(runtime, "public readonly struct AuthorityGeneration"));
            Assert.Single(Occurrences(runtime, "public sealed class FixedFlightClock"));
            Assert.Single(Occurrences(runtime, "public readonly struct ShadowVector3"));
            Assert.Single(Occurrences(runtime, "public sealed class ShadowForceAccumulator"));
            Assert.Single(Occurrences(runtime, "public readonly struct HullPropulsionDemand"));

            string game = string.Join("\n", Directory.EnumerateFiles(
                    Path.Combine(root, "WorldsAdriftRebornGameServer"), "*.cs",
                    SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj"
                    + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "bin"
                    + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Select(File.ReadAllText));
            Assert.DoesNotContain("VectorWallStormShadow", game, StringComparison.Ordinal);
            Assert.DoesNotContain("VectorWallDamageIntent", game, StringComparison.Ordinal);
            Assert.DoesNotContain("DomainRecoveryModel", game, StringComparison.Ordinal);
            Assert.DoesNotContain("DomainCommandGate", game, StringComparison.Ordinal);
            Assert.DoesNotContain("CommittedDomainSnapshot", game, StringComparison.Ordinal);
        }

        private static long CompletedTicks(params int[] elapsedMilliseconds)
        {
            var clock = new FixedFlightClock();
            FixedFlightStepBatch batch = default;
            foreach (int elapsed in elapsedMilliseconds)
                batch = clock.Advance(TimeSpan.FromMilliseconds(elapsed));
            Assert.Equal(0, batch.TotalDroppedSteps);
            return batch.CompletedSteps;
        }

        private static string[] WallIntentsThrough(long ticks)
        {
            var wall = new VectorWallSegment(8, VectorWallType.StormRift,
                new ShadowVector3(0, 0, -1000), new ShadowVector3(0, 0, 1000));
            var type = new VectorWallTypeTuning(false, 0, 0, 0, 0, 0, 0, 1,
                true, 10, 0.05);
            var tuning = new VectorWallStormTuning(
                new Dictionary<VectorWallType, VectorWallTypeTuning>
                {
                    [VectorWallType.StormRift] = type,
                });
            var target = new VectorWallDamageTarget("part:12",
                VectorWallDamageTargetKind.Engine);
            var intents = new List<string>();
            for (long tick = 0; tick < ticks; tick++)
            {
                var input = new VectorWallStormInput("ship:70",
                    new ShadowVector3(100, 100, 0), ShadowVector3.Zero,
                    ShadowVector3.Forward, ShadowVector3.Zero, 1000,
                    ShadowVector3.Zero, tick, FixedFlightClock.StepSeconds,
                    ShadowVector3.Zero);
                Assert.True(VectorWallStormShadow.TryEvaluate(input, new[] { wall },
                    tuning, null, new[] { target }, out VectorWallStormShadowResult result));
                intents.AddRange(result.DamageIntents.Select(intent => intent.IntentId));
            }
            return intents.ToArray();
        }

        private static IEnumerable<int> Occurrences(string haystack, string needle)
        {
            int position = 0;
            while ((position = haystack.IndexOf(needle, position,
                       StringComparison.Ordinal)) >= 0)
            {
                yield return position;
                position += needle.Length;
            }
        }

        private static string RepoRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName,
                        "WorldsAdriftReborn.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root.");
        }
    }
}
