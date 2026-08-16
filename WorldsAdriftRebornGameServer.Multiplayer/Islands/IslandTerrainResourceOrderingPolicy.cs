namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Pure ordering rules between an island's terrain root and its streamed
    /// resources. A resource may never be added or served before its owning terrain
    /// is ready, and terrain may only leave after all of that island's resources do.
    /// </summary>
    public static class IslandTerrainResourceOrderingPolicy
    {
        public static bool MayAddResource(bool terrainInterestEnabled, bool terrainReady) =>
            !terrainInterestEnabled || terrainReady;

        public static bool MayServeResourceComponents(
            bool resourceCheckoutAllows,
            bool terrainInterestEnabled,
            bool terrainReady) =>
            resourceCheckoutAllows
            && (!terrainInterestEnabled || terrainReady);

        /// <summary>
        /// Replaces normal spatial work while a terrain removal is waiting. Adds for
        /// that island are cancelled and every loaded resource on it is removed;
        /// unrelated-island work retains its order. Removes precede retained work so
        /// a large add queue cannot pin terrain indefinitely.
        /// </summary>
        public static IReadOnlyList<ResourceStreamAction> DrainBeforeTerrainRemoval(
            IEnumerable<ResourceStreamAction> pending,
            ISet<long> loaded,
            IReadOnlyDictionary<long, IslandId> resourceIslands,
            IslandId island)
        {
            if (pending == null) throw new ArgumentNullException(nameof(pending));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));
            if (resourceIslands == null) throw new ArgumentNullException(nameof(resourceIslands));

            List<ResourceStreamAction> result = loaded
                .Where(id => resourceIslands.TryGetValue(id, out IslandId owner) && owner == island)
                .OrderBy(id => id)
                .Select(id => new ResourceStreamAction(ResourceStreamActionKind.Remove, id))
                .ToList();

            HashSet<long> alreadyQueued = result.Select(action => action.EntityId).ToHashSet();
            foreach (ResourceStreamAction action in pending)
            {
                bool belongs = resourceIslands.TryGetValue(action.EntityId, out IslandId owner)
                    && owner == island;
                if (belongs)
                {
                    // Island-local adds are cancelled. Its loaded resources already
                    // have one canonical removal above, so duplicate removes vanish.
                    continue;
                }
                if (alreadyQueued.Add(action.EntityId)) result.Add(action);
            }
            return result;
        }

        public static bool IsDrained(
            ISet<long> loaded,
            IReadOnlyDictionary<long, IslandId> resourceIslands,
            IslandId island) =>
            !loaded.Any(id => resourceIslands.TryGetValue(id, out IslandId owner) && owner == island);
    }
}
