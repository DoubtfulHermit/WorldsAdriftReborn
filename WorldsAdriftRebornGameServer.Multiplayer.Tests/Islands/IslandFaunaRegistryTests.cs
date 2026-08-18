using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHY THIS REGISTRY IS TESTED HARDER THAN THE MATHS ABOVE IT. Fauna is a NEW
    /// HIGH-RATE SENDER and docs/multiplayer.md forbids adding an unbounded relayed
    /// sender - the last one that shipped spent its bandwidth on a desync spiral.
    /// BOUNDED: past the world-wide cap a creature is REFUSED by returning false,
    /// because a full world is an ordinary Tuesday and an exception on the seeding
    /// path would take an island checkout down with it. SILENT WHEN IDLE: nothing
    /// due returns the empty singleton and allocates nothing, because the main loop
    /// turns once per ENet EVENT, so a cadence counted in turns would push hundreds
    /// of updates a second - it is proved here slower than the 20 Hz ship cadence.
    /// RESTART-STABLE: no physics state is stored, so a restarted server re-derives
    /// byte-identical poses for byte-identical ids instead of teleporting every
    /// creature - asserted by replaying one sequence through two fresh registries.
    /// </summary>
    public sealed class IslandFaunaRegistryTests
    {
        // --- Construction: a mis-tuned sender must fail at boot, never on the wire.

        [Fact]
        public void Registry_refuses_to_exist_without_a_clock()
        {
            Assert.Throws<ArgumentNullException>(() => Build(null!));
        }

        [Fact]
        public void Constructor_rejects_every_out_of_range_argument()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(new FakeClock(), maxConcurrent: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(new FakeClock(), poseInterval: TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => Build(new FakeClock(), poseInterval: TimeSpan.FromSeconds(-1)));
        }

        // --- The budget: over the cap a creature is dropped, not an exception thrown.

        [Fact]
        public void Concurrent_cap_refuses_overflow_instead_of_throwing()
        {
            IslandFaunaRegistry registry = Build(new FakeClock(), maxConcurrent: 2);
            FaunaCreature[] creatures = Creatures();

            Assert.Equal(2, registry.MaxConcurrent);
            Assert.True(registry.HasCapacity);
            Assert.True(Add(registry, creatures[0]));
            Assert.True(Add(registry, creatures[1]));

            Assert.False(registry.HasCapacity);
            // The third is simply not seeded - the world is one manta poorer and
            // nothing else happens. An exception here would fail an island checkout.
            Assert.False(Add(registry, creatures[2]));
            Assert.Equal(2, registry.Count);
        }

        [Fact]
        public void A_zero_budget_switches_the_sender_off_entirely()
        {
            IslandFaunaRegistry registry = Build(new FakeClock(), maxConcurrent: 0);
            Assert.False(registry.HasCapacity);
            Assert.False(Add(registry, Creatures()[0]));
            Assert.Equal(0, registry.Count);
            Assert.Empty(registry.DuePoses());
        }

        // --- Silence: nothing due must cost nothing at all.

        [Fact]
        public void An_empty_registry_allocates_nothing()
        {
            IslandFaunaRegistry registry = Build(new FakeClock());

            // Array.Empty is a singleton: same instance means no allocation happened.
            Assert.Same(Array.Empty<FaunaPose>(), registry.DuePoses());
        }

        [Fact]
        public void Nothing_is_pushed_while_the_interval_has_not_elapsed()
        {
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = Build(clock);
            Assert.True(Add(registry, Creatures()[0]));

            // The seed pose goes out immediately; the next one waits a full interval.
            Assert.NotEmpty(registry.DuePoses());
            Assert.Empty(registry.DuePoses());
        }

        // --- Cadence: fauna drifts, so it is deliberately slower than ships and logs.

        [Fact]
        public void Pose_cadence_is_slower_than_the_twenty_hertz_ship_cadence()
        {
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = Build(clock);
            Assert.True(registry.PoseInterval > TimeSpan.FromSeconds(1.0 / 20.0),
                "fauna must not be pushed at the ship/log rate");

            Assert.True(Add(registry, Creatures()[0]));
            Assert.NotEmpty(registry.DuePoses());
            clock.Elapsed = registry.PoseInterval - TimeSpan.FromMilliseconds(1);
            Assert.Empty(registry.DuePoses());
            clock.Elapsed = registry.PoseInterval;
            Assert.NotEmpty(registry.DuePoses());
        }

        // --- Absolute poses: a peer that missed an update converges on the next one.

        [Fact]
        public void Every_pose_is_a_complete_absolute_world_position()
        {
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = Build(clock);
            FaunaCreature creature = Creatures()[0];
            Assert.True(Add(registry, creature));
            FaunaPose first = Assert.Single(registry.DuePoses());
            clock.Elapsed = registry.PoseInterval;
            FaunaPose second = Assert.Single(registry.DuePoses());

            // A delta would be a small offset around zero; both poses instead carry a
            // whole world position, so the later one fully supersedes the earlier one.
            Assert.Equal(creature.EntityId, first.EntityId);
            Assert.Equal(creature.EntityId, second.EntityId);
            AssertAbsolute(first);
            AssertAbsolute(second);
        }

        // --- Entity ids: the fauna band, ascending, and never handed out twice.

        [Fact]
        public void Entity_ids_stay_in_the_fauna_band_and_are_never_reused()
        {
            IslandFaunaRegistry registry = Build(new FakeClock());
            long first = registry.NextEntityId();
            long second = registry.NextEntityId();
            Assert.True(first >= IslandFaunaPolicy.FirstFaunaEntityId);
            Assert.True(first > TreeFall.FirstLogEntityId);
            Assert.True(second > first);
            Assert.True(Add(registry, new FaunaCreature(second, FaunaSpecies.MantaRay, TestIslandId, 0)));
            Assert.True(registry.Remove(second));
            // A packet still in flight for the retired creature must never be able to
            // name a new one, so the counter does not wind back.
            Assert.True(registry.NextEntityId() > second);
        }

        // --- Restart: a rebuilt registry on a fresh clock replays the same world.

        [Fact]
        public void Registry_replays_identical_poses_after_a_server_restart()
        {
            TimeSpan[] steps =
            {
                TimeSpan.Zero, TimeSpan.FromSeconds(1.0), TimeSpan.FromSeconds(2.5),
                TimeSpan.FromSeconds(7.0), TimeSpan.FromSeconds(19.25),
            };
            List<FaunaPose> before = Replay(steps);
            List<FaunaPose> after = Replay(steps);

            Assert.NotEmpty(before);
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.Equal(before[i].EntityId, after[i].EntityId);
                Assert.Equal(before[i].Position, after[i].Position);
            }
        }

        /// <summary>
        /// The pose maths is INJECTED into the registry, so these facts pin cadence,
        /// bounding and identity rather than geometry: the stub answers one fixed
        /// world position near the test island, which is what makes a published pose
        /// absolute rather than a delta. Its parameter list is deliberately omitted -
        /// a C# anonymous method - so it binds whatever arguments the registry hands
        /// the movement layer.
        /// </summary>
        private static IslandFaunaRegistry Build(
            IClock clock, int? maxConcurrent = null, TimeSpan? poseInterval = null) =>
            new IslandFaunaRegistry(clock, delegate { return StubPose; }, maxConcurrent, poseInterval);

        private static readonly dynamic StubPose =
            FixedPointPosition.FromMetres(1210.0, -395.0, 3610.0);

        /// <summary>One whole server lifetime: a fresh clock, a fresh registry, the same schedule.</summary>
        private static List<FaunaPose> Replay(IReadOnlyList<TimeSpan> steps)
        {
            FakeClock clock = new FakeClock();
            IslandFaunaRegistry registry = Build(clock);
            foreach (FaunaCreature creature in Creatures())
            {
                Assert.True(Add(registry, creature));
            }

            List<FaunaPose> poses = new List<FaunaPose>();
            foreach (TimeSpan step in steps)
            {
                clock.Elapsed = step;
                poses.AddRange(registry.DuePoses());
            }
            return poses;
        }

        private static void AssertAbsolute(FaunaPose pose)
        {
            double x = pose.Position.MetresX - TestIsland.GlobalOrigin.MetresX;
            double y = pose.Position.MetresY - TestIsland.GlobalOrigin.MetresY;
            double z = pose.Position.MetresZ - TestIsland.GlobalOrigin.MetresZ;
            Assert.True(Math.Sqrt((x * x) + (y * y) + (z * z)) < 5000.0,
                "a pose must sit near its own island, not somewhere else in the world");
            Assert.True(Math.Abs(pose.Position.MetresX) > 500.0,
                "a pose carrying only island-local metres would be a delta, not a pose");
        }

        private static bool Add(IslandFaunaRegistry registry, FaunaCreature creature) =>
            registry.Add(creature, TestIsland, TestEnvelope);

        private static FaunaCreature[] Creatures() => new[]
        {
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId, FaunaSpecies.MantaRay, TestIslandId, 0),
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + 1, FaunaSpecies.MantaRay, TestIslandId, 1),
            new FaunaCreature(IslandFaunaPolicy.FirstFaunaEntityId + 2, FaunaSpecies.JellyFish, TestIslandId, 2),
        };

        private static readonly IslandId TestIslandId = new IslandId("fauna-registry-test");

        private static readonly IslandDefinition TestIsland = new IslandDefinition(
            TestIslandId, "Fauna Registry Test Island", "island-fauna-registry-test",
            FixedPointPosition.FromMetres(1200.0, -400.0, 3600.0),
            "0@Island", IslandCatalog.DefaultTerrainAssetContext, SpawnOrder.AfterPlayer);

        private static readonly IslandTerrainEnvelope TestEnvelope = new IslandTerrainEnvelope(
            TestIslandId, -240.0, -80.0, -180.0, 240.0, 60.0, 180.0);

        /// <summary>
        /// Time the test owns. Nothing here sleeps: a cadence assertion that waited
        /// on the wall clock would be slow and flaky, and the restart fact needs two
        /// registries driven through the identical elapsed sequence.
        /// </summary>
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }
    }
}
