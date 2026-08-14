using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>Read-only Phase 2 glue. It logs topology and changes no routing.</summary>
    internal static class WorldDirectoryDiagnostics
    {
        internal static WorldDirectory BuildAndLog(WorldEntityRegistry entities)
        {
            IslandRegistry islands = IslandRegistry.CreateDefault();
            RegionRegistry regions = RegionRegistry.CreateDefault(islands);
            var mountedOverrides = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (WorldEntity entity in entities.Registrations)
            {
                long? entityId = entities.BoundEntityIdFor(entity.Key);
                if (!entityId.HasValue) continue;
                MountedParts.Mount? mount = MountedParts.MountFor(entityId.Value);
                if (!mount.HasValue) continue;
                WorldEntity? hull = entities.ByEntityId(mount.Value.HullEntityId);
                if (hull != null) mountedOverrides[entity.Key] = hull.Key;
            }

            WorldDirectory directory = WorldDirectory.Build(
                entities, islands, regions, mountedOverrides);
            int globals = directory.Entries.Count(entry => entry.Owner.Kind == WorldOwnerKind.Global);
            int regionOwned = directory.Entries.Count(entry => entry.Owner.Kind == WorldOwnerKind.Region);
            int shipOwned = directory.Entries.Count(entry => entry.Owner.Kind == WorldOwnerKind.Ship);
            string regionSummary = string.Join(", ", regions.All.Select(region =>
                region.Id + "=" + directory.OwnedBy(WorldOwner.ForRegion(region.Id)).Count));
            int shipDomains = directory.Entries
                .Where(entry => entry.Owner.Kind == WorldOwnerKind.Ship)
                .Select(entry => entry.Owner.Id)
                .Distinct(StringComparer.Ordinal)
                .Count();

            Console.WriteLine("[world-directory] READ-ONLY classified " + directory.Entries.Count
                + " registrations: global=" + globals + ", region=" + regionOwned
                + " (" + regionSummary + "), ship=" + shipOwned + " across " + shipDomains
                + " hull root(s). No spawn/interest/persistence/networking path reads this directory.");
            return directory;
        }
    }
}
