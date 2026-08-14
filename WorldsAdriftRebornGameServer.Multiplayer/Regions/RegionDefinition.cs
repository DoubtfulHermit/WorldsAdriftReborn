using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Regions
{
    /// <summary>
    /// Stable topology only: which evidenced islands belong to a region. It
    /// deliberately contains no worker, scheduler or authority state.
    /// </summary>
    public sealed class RegionDefinition
    {
        public RegionDefinition(RegionId id, string displayName, IEnumerable<IslandId> islandIds)
        {
            if (string.IsNullOrWhiteSpace(id.Value))
                throw new ArgumentException("a region definition must have a non-empty id", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("a region display name must not be empty", nameof(displayName));
            if (islandIds == null)
                throw new ArgumentNullException(nameof(islandIds));

            List<IslandId> members = new(islandIds);
            if (members.Count == 0)
                throw new ArgumentException("a region must contain at least one island", nameof(islandIds));

            members.Sort();
            for (int i = 1; i < members.Count; i++)
            {
                if (members[i - 1] == members[i])
                    throw new ArgumentException(
                        "island '" + members[i] + "' is listed more than once", nameof(islandIds));
            }

            Id = id;
            DisplayName = displayName;
            IslandIds = members.AsReadOnly();
        }

        public RegionId Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<IslandId> IslandIds { get; }
    }
}
