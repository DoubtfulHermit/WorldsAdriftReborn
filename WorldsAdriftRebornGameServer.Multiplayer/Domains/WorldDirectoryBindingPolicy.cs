using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Domains
{
    /// <summary>One directory entry that already has an entity id.</summary>
    public sealed class BoundDirectoryEntry
    {
        public BoundDirectoryEntry(WorldDirectoryEntry entry, long entityId)
        {
            Entry = entry;
            EntityId = entityId;
        }

        public WorldDirectoryEntry Entry { get; }
        public long EntityId { get; }
    }

    /// <summary>
    /// The directory split into what can be assigned to a domain right now and
    /// what cannot be assigned YET.
    /// </summary>
    public sealed class WorldDirectoryBinding
    {
        public WorldDirectoryBinding(IReadOnlyList<BoundDirectoryEntry> bound,
            IReadOnlyList<string> deferredKeys)
        {
            Bound = bound;
            DeferredKeys = deferredKeys;
        }

        /// <summary>Entries whose AddEntityOp has run; safe to own and to expect.</summary>
        public IReadOnlyList<BoundDirectoryEntry> Bound { get; }

        /// <summary>
        /// Keys with no entity id yet. They are deliberately excluded from BOTH
        /// assignment and the completeness audit: an id that does not exist cannot
        /// be owned, and demanding it would fail an audit that is meant to catch
        /// real ownership gaps.
        /// </summary>
        public IReadOnlyList<string> DeferredKeys { get; }

        public IEnumerable<long> ExpectedEntityIds =>
            Bound.Select(entry => entry.EntityId);
    }

    /// <summary>
    /// Resolves world-directory entries against the entity registry at ownership
    /// bootstrap time.
    ///
    /// An unbound id is NOT a fault. <c>WorldEntityRegistry.BoundEntityIdFor</c>
    /// documents null as "the key is not registered OR its AddEntityOp has not run
    /// yet", and every other caller in the server treats it as a nullable question.
    /// Bootstrap used to be the one exception: it demanded an id for every entry
    /// and threw an unhandled exception on the boot path, so a world whose entities
    /// are not all pre-bound killed the process at startup instead of starting with
    /// less to own. A deferred entry acquires its domain later through the normal
    /// runtime paths that already assign ownership when an entity actually appears.
    ///
    /// The deferral is reported, never silent: a world that defers entries it should
    /// have bound is a real signal, and hiding it would trade one failure mode for a
    /// quieter one.
    /// </summary>
    public static class WorldDirectoryBindingPolicy
    {
        public static WorldDirectoryBinding Resolve(
            IEnumerable<WorldDirectoryEntry> entries, Func<string, long?> boundIdFor)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (boundIdFor == null) throw new ArgumentNullException(nameof(boundIdFor));

            var bound = new List<BoundDirectoryEntry>();
            var deferred = new List<string>();
            foreach (WorldDirectoryEntry entry in entries)
            {
                string key = entry.Entity.Key;
                long? entityId = boundIdFor(key);
                if (entityId.HasValue) bound.Add(new BoundDirectoryEntry(entry, entityId.Value));
                else deferred.Add(key);
            }

            // A ship domain is built from its root outward. If the root itself has
            // no id yet, the whole group is deferred rather than half-registered:
            // a ShipDomain that is missing its hull is not a smaller ship, it is a
            // broken one.
            var deferredRoots = new HashSet<string>(bound
                .Where(item => item.Entry.Owner.Kind == WorldOwnerKind.Ship)
                .Select(item => item.Entry.Owner.Id)
                .Where(rootKey => !boundIdFor(rootKey).HasValue), StringComparer.Ordinal);
            if (deferredRoots.Count > 0)
            {
                foreach (BoundDirectoryEntry item in bound
                    .Where(item => item.Entry.Owner.Kind == WorldOwnerKind.Ship
                        && deferredRoots.Contains(item.Entry.Owner.Id)))
                    deferred.Add(item.Entry.Entity.Key);
                bound.RemoveAll(item => item.Entry.Owner.Kind == WorldOwnerKind.Ship
                    && deferredRoots.Contains(item.Entry.Owner.Id));
            }

            return new WorldDirectoryBinding(bound, deferred);
        }

        /// <summary>A bounded, stable operator line; never dumps an unbounded list.</summary>
        public static string DeferralReport(WorldDirectoryBinding binding, int maxKeys = 8)
        {
            if (binding.DeferredKeys.Count == 0) return string.Empty;
            IEnumerable<string> shown = binding.DeferredKeys.OrderBy(key => key,
                StringComparer.Ordinal).Take(maxKeys);
            int remaining = binding.DeferredKeys.Count - Math.Min(maxKeys, binding.DeferredKeys.Count);
            return binding.DeferredKeys.Count + " world registration(s) have no entity id yet"
                + " and are not owned at boot: " + string.Join(", ", shown)
                + (remaining > 0 ? " (+" + remaining + " more)" : string.Empty)
                + ". They take a domain when their AddEntityOp runs.";
        }
    }
}
