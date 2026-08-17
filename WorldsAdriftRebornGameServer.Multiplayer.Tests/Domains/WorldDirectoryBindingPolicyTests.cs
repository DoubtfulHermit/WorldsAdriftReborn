using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Domains
{
    public sealed class WorldDirectoryBindingPolicyTests
    {
        [Fact]
        public void A_fully_bound_directory_defers_nothing()
        {
            WorldDirectory directory = Build(out WorldEntityRegistry entities);

            WorldDirectoryBinding binding = WorldDirectoryBindingPolicy.Resolve(
                directory.Entries, entities.BoundEntityIdFor);

            Assert.Empty(binding.DeferredKeys);
            Assert.Equal(directory.Entries.Count, binding.Bound.Count);
            Assert.Equal(directory.Entries.Count, binding.ExpectedEntityIds.Count());
            Assert.Equal(string.Empty, WorldDirectoryBindingPolicy.DeferralReport(binding));
        }

        /// <summary>
        /// The regression guard. Ownership bootstrap used to demand an entity id for
        /// every directory entry and threw an unhandled exception on the boot path,
        /// so a world whose island terrain had not been bound yet killed the server
        /// at startup. An unbound entry must defer, never throw, and must stay out of
        /// the completeness audit so it cannot be reported as an ownership gap.
        /// </summary>
        [Fact]
        public void An_unbound_entry_is_deferred_and_never_expected()
        {
            WorldDirectory directory = Build(out WorldEntityRegistry entities);
            string unbound = IslandCatalog.Haven.WorldEntityKey;
            Assert.NotNull(directory.ByEntityKey(unbound));

            WorldDirectoryBinding binding = WorldDirectoryBindingPolicy.Resolve(
                directory.Entries,
                key => key == unbound ? (long?)null : entities.BoundEntityIdFor(key));

            Assert.Contains(unbound, binding.DeferredKeys);
            Assert.DoesNotContain(binding.Bound, item => item.Entry.Entity.Key == unbound);
            long? unboundId = entities.BoundEntityIdFor(unbound);
            Assert.NotNull(unboundId);
            Assert.DoesNotContain(unboundId!.Value, binding.ExpectedEntityIds);
            Assert.Equal(directory.Entries.Count - 1, binding.Bound.Count);
        }

        [Fact]
        public void A_ship_group_whose_root_is_unbound_is_deferred_whole()
        {
            WorldDirectory directory = Build(out WorldEntityRegistry entities);
            string root = WorldEntities.ShipFrameKey;
            IReadOnlyList<string> groupKeys = directory.Entries
                .Where(entry => entry.Owner.Kind == WorldOwnerKind.Ship
                    && entry.Owner.Id == root)
                .Select(entry => entry.Entity.Key)
                .ToArray();
            Assert.NotEmpty(groupKeys);

            WorldDirectoryBinding binding = WorldDirectoryBindingPolicy.Resolve(
                directory.Entries,
                key => key == root ? (long?)null : entities.BoundEntityIdFor(key));

            // Not one member survives: a ShipDomain missing its hull is broken, not smaller.
            foreach (string key in groupKeys)
            {
                Assert.Contains(key, binding.DeferredKeys);
                Assert.DoesNotContain(binding.Bound, item => item.Entry.Entity.Key == key);
            }
        }

        [Fact]
        public void The_deferral_report_states_the_count_and_stays_bounded()
        {
            WorldDirectory directory = Build(out WorldEntityRegistry entities);
            var deferred = new HashSet<string>(directory.Entries
                .Where(entry => entry.Owner.Kind == WorldOwnerKind.Region)
                .Select(entry => entry.Entity.Key)
                .Take(20), StringComparer.Ordinal);
            Assert.True(deferred.Count > 8);

            WorldDirectoryBinding binding = WorldDirectoryBindingPolicy.Resolve(
                directory.Entries,
                key => deferred.Contains(key) ? (long?)null : entities.BoundEntityIdFor(key));
            string report = WorldDirectoryBindingPolicy.DeferralReport(binding);

            Assert.Contains(deferred.Count + " world registration(s)", report);
            Assert.Contains("+" + (deferred.Count - 8) + " more", report);

            // Exactly the eight alphabetically-first keys are named; the ninth is not,
            // so the line cannot grow with the world.
            string[] ordered = deferred.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            foreach (string named in ordered.Take(8)) Assert.Contains(named, report);
            Assert.DoesNotContain(ordered[8], report);
        }

        [Fact]
        public void Null_arguments_are_rejected()
        {
            WorldDirectory directory = Build(out WorldEntityRegistry entities);
            Assert.Throws<ArgumentNullException>(() =>
                WorldDirectoryBindingPolicy.Resolve(null!, entities.BoundEntityIdFor));
            Assert.Throws<ArgumentNullException>(() =>
                WorldDirectoryBindingPolicy.Resolve(directory.Entries, null!));
        }

        private static WorldDirectory Build(out WorldEntityRegistry entities)
        {
            entities = WorldEntities.Default(new EntityIdAllocator());
            foreach (WorldEntity entity in entities.Registrations) entities.EntityIdFor(entity);
            IslandRegistry islands = IslandRegistry.CreateDefault();
            return WorldDirectory.Build(entities, islands, RegionRegistry.CreateDefault(islands));
        }
    }
}
