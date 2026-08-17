using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Wilderness
{
    /// <summary>
    /// That the shrine is actually IN the world - the wiring, not the values.
    /// Everything here is a way the shrine could exist in the code and not exist
    /// in front of a player.
    /// </summary>
    public sealed class WildernessShrineRegistrationTests
    {
        private static WorldEntityRegistry Build(
            bool includeShrine = true, string? districts = null)
        {
            return WorldEntities.Default(
                new EntityIdAllocator(),
                releaseWorldDistricts: districts,
                includeWildernessShrine: includeShrine);
        }

        [Fact]
        public void The_default_world_has_a_shrine_on_haven()
        {
            WorldEntity? shrine = Build().ByKey(WildernessShrine.WorldEntityKey);

            Assert.NotNull(shrine);
            Assert.Equal(WildernessShrine.AssetName, shrine!.AssetName);
            Assert.Equal(WildernessShrine.PositionOn(IslandCatalog.Haven), shrine.Position);
        }

        /// <summary>
        /// It exists whether or not the Wilderness is open. Refusing is the
        /// shrine's job; not being there would read as a bug and would leave the
        /// player with nothing to ask.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("C6")]
        [InlineData("tier1")]
        public void The_shrine_stands_whether_or_not_tier_one_is_registered(string? districts)
        {
            Assert.NotNull(Build(districts: districts).ByKey(WildernessShrine.WorldEntityKey));
        }

        [Fact]
        public void The_shrine_can_be_switched_off_entirely()
        {
            Assert.Null(Build(includeShrine: false).ByKey(WildernessShrine.WorldEntityKey));
        }

        /// <summary>
        /// AfterPlayer, always. Anything BeforePlayer is on the critical path of a
        /// spawn, and a decorative monument has no business being able to delay one.
        /// </summary>
        [Fact]
        public void The_shrine_never_delays_a_spawn()
        {
            Assert.Equal(SpawnOrder.AfterPlayer,
                Build().ByKey(WildernessShrine.WorldEntityKey)!.Order);
        }

        /// <summary>
        /// It must not collide with anything else the server puts on Haven. The
        /// databank has its own test; this one catches the whole set at once, which
        /// is what a future Haven prop would trip over.
        /// </summary>
        [Fact]
        public void The_shrine_does_not_stand_inside_another_haven_entity()
        {
            WorldEntityRegistry registry = Build();
            WorldEntity shrine = registry.ByKey(WildernessShrine.WorldEntityKey)!;

            foreach (WorldEntity other in registry.Registrations)
            {
                if (other.Key == shrine.Key) continue;
                // Terrain and the static ship frame are not point objects; only
                // compare against things that occupy a spot on the ground.
                if (other.AssetName.Contains("Island", StringComparison.Ordinal)) continue;

                double dx = other.Position.MetresX - shrine.Position.MetresX;
                double dy = other.Position.MetresY - shrine.Position.MetresY;
                double dz = other.Position.MetresZ - shrine.Position.MetresZ;
                Assert.True(Math.Sqrt(dx * dx + dy * dy + dz * dz) >= 3.0,
                    "the shrine is inside " + other.Key + " (" + other.AssetName + ")");
            }
        }

        /// <summary>
        /// The registration key must be reachable without allocating an entity id.
        /// This is how the 1210 branch and the interact dispatch recognise the
        /// shrine, and BoundEntityIdFor deliberately never allocates.
        /// </summary>
        [Fact]
        public void The_shrine_is_findable_by_its_stable_key()
        {
            WorldEntityRegistry registry = Build();

            Assert.Null(registry.BoundEntityIdFor(WildernessShrine.WorldEntityKey));
            long id = registry.EntityIdFor(registry.ByKey(WildernessShrine.WorldEntityKey)!);
            Assert.Equal(id, registry.BoundEntityIdFor(WildernessShrine.WorldEntityKey));
            Assert.Equal(WildernessShrine.WorldEntityKey, registry.ByEntityId(id)!.Key);
        }
    }
}
