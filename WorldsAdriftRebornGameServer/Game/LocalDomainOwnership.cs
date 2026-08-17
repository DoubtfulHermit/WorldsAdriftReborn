using WorldsAdriftRebornGameServer.Game.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Domains;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>Phase 4A boot/lifecycle glue. It owns no simulation scheduling.</summary>
    internal static class LocalDomainOwnership
    {
        internal static DomainOwnershipSummary Bootstrap(
            LocalDomainHost host,
            WorldDirectory directory,
            WorldEntityRegistry entities,
            ShipDomainRegistry shipDomains,
            IslandRegistry islands,
            RegionRegistry regions)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));
            if (regions == null) throw new ArgumentNullException(nameof(regions));

            foreach (IslandDefinition island in islands.All)
            {
                RegionDefinition region = regions.ByIsland(island.Id)
                    ?? throw new InvalidOperationException("island '" + island.Id + "' has no region");
                host.Register(new IslandDomain(island.Id, region.Id));
            }

            // Resolve every entry ONCE. An entry whose AddEntityOp has not run has
            // no id to own and is deferred, not fatal; see WorldDirectoryBindingPolicy.
            WorldDirectoryBinding binding = WorldDirectoryBindingPolicy.Resolve(
                directory.Entries, entities.BoundEntityIdFor);
            string deferralReport = WorldDirectoryBindingPolicy.DeferralReport(binding);
            if (deferralReport.Length > 0)
                Console.WriteLine("[domain-host] " + deferralReport);

            foreach (IGrouping<string, BoundDirectoryEntry> group in binding.Bound
                .Where(item => item.Entry.Owner.Kind == WorldOwnerKind.Ship)
                .GroupBy(item => item.Entry.Owner.Id, StringComparer.Ordinal))
            {
                // The policy already deferred any group whose root is unbound, so a
                // missing id here is a real inconsistency rather than a pending spawn.
                long hullEntityId = entities.BoundEntityIdFor(group.Key)
                    ?? throw new InvalidOperationException("ship root '" + group.Key + "' has no bound entity id");
                ShipDomain? liveDomain = shipDomains.ByHull(hullEntityId);
                bool legacyStatic = string.Equals(group.Key, WorldEntities.ShipFrameKey,
                    StringComparison.Ordinal);
                if (liveDomain == null && !legacyStatic)
                    throw new InvalidOperationException("built ship root '" + group.Key
                        + "' has no live ShipDomain registered");
                long[] decks = BuiltShips.DecksForHull(hullEntityId).ToArray();
                var deckSet = new HashSet<long>(decks);
                long[] otherMembers = group
                    .Select(item => item.EntityId)
                    .Where(id => id != hullEntityId && !deckSet.Contains(id))
                    .ToArray();
                ILocalSimulationDomain domain;
                if (liveDomain != null)
                {
                    liveDomain.ReplaceMembers(decks, otherMembers);
                    domain = liveDomain;
                }
                else
                {
                    domain = new StaticShipDomain(hullEntityId, otherMembers);
                }
                if (host.ById(domain.Id) == null) host.Register(domain);
                else host.Synchronize(domain);
            }

            foreach (BoundDirectoryEntry item in binding.Bound)
            {
                WorldDirectoryEntry entry = item.Entry;
                long entityId = item.EntityId;
                if (entry.Owner.Kind == WorldOwnerKind.Global)
                {
                    host.MarkGlobal(entityId);
                }
                else if (entry.Owner.Kind == WorldOwnerKind.Region)
                {
                    IslandId islandId = entry.IslandId
                        ?? throw new InvalidOperationException("region-owned '" + entry.Entity.Key + "' has no island");
                    var domainId = SimulationDomainId.ForIsland(islandId);
                    _ = host.ById(domainId)
                        ?? throw new InvalidOperationException("island domain '" + domainId + "' is missing");
                    host.Assign(entityId, domainId);
                }
            }

            long[] expected = binding.ExpectedEntityIds.ToArray();
            DomainOwnershipSummary summary = host.EnsureComplete(expected);
            Console.WriteLine("[domain-host] local-single-process islands=" + summary.IslandDomainCount
                + " ships=" + summary.ShipDomainCount + " owned=" + summary.OwnedEntityCount
                + " globals=" + summary.GlobalEntityCount + " unowned=0 duplicates=0."
                + " Ownership only; gameplay services retain their existing poll-loop order.");
            return summary;
        }

        internal static void MoveToIsland(LocalDomainHost host, long entityId,
            FixedPointPosition position)
        {
            IslandId islandId = IslandResourceInterestPolicy.ClosestIsland(
                position, WorldsAdriftRebornGameServer.IslandTopology.All);
            SimulationDomainId destination = SimulationDomainId.ForIsland(islandId);
            if (host.ById(destination) is not IslandDomain)
                throw new InvalidOperationException("island domain '" + destination + "' is not hosted");
            SimulationDomainId? current = host.OwnerOf(entityId);
            if (current.HasValue && current.Value != destination)
                host.Move(entityId, current.Value, destination);
            else if (!current.HasValue)
                host.Assign(entityId, destination);
        }

        internal static void MoveToShip(LocalDomainHost host, long entityId, long hullEntityId)
        {
            SimulationDomainId destination = SimulationDomainId.ForShip(hullEntityId);
            if (host.ById(destination) is not ShipDomain)
                throw new InvalidOperationException("ship domain '" + destination + "' is not hosted");
            SimulationDomainId? current = host.OwnerOf(entityId);
            if (current.HasValue && current.Value != destination)
            {
                host.Move(entityId, current.Value, destination);
            }
            else if (!current.HasValue)
                host.Assign(entityId, destination);
        }

        internal static void RemoveEntity(LocalDomainHost host, long entityId)
        {
            SimulationDomainId? owner = host.OwnerOf(entityId);
            if (!owner.HasValue) return;
            host.Unassign(entityId, owner.Value);
        }
    }
}
