namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server's map from "an entity a player can stand on" to "the ship that
    /// entity belongs to". It is how <see cref="AboardPolicy"/> turns a raw 1073
    /// <c>relativeTo</c> entity id into a ship identity.
    ///
    /// WHY THE SERVER OWNS THIS AND NOT THE CLIENT. A player on a deck is NOT
    /// parented; the client publishes 1073 with <c>relativeTo</c> = the entity id
    /// of whatever it is standing on (VERIFIED in
    /// ClientAuthoritativePlayerMovement.CollectDataHighFrequency:
    /// <c>update.SetRelativeTo(localRelativeGroundObject.GetSpatialOsEntity()
    /// .EntityId)</c>). For our single bare hull that ground object IS the hull -
    /// the player stands on the hull's own beam frame / virtual deck, so
    /// relativeTo == the hull entity id. Once decks or other parts are bolted on,
    /// the ground object becomes the Deck01/part entity, and THAT part's id maps
    /// to the same hull here. So the membership is (surface entity id -&gt; ship
    /// root entity id), and a hull registers itself as its own surface.
    ///
    /// The server can build this from its OWN spawn decisions - it allocated
    /// every ship entity id and wrote every 8066 link - so it never has to trust
    /// or even read the client's 8066 to answer "which ship is this".
    ///
    /// Pure, and NOT thread-safe on purpose, in the mold of
    /// <see cref="WorldEntityRegistry"/>: the server is one poll loop.
    /// </summary>
    public sealed class ShipMembership
    {
        private readonly Dictionary<long, long> _surfaceToRoot = new Dictionary<long, long>();

        /// <summary>
        /// Records that standing on <paramref name="surfaceEntityId"/> means being
        /// aboard the ship rooted at <paramref name="shipRootEntityId"/>. A hull
        /// registers itself (surface == root); a deck or other standable part
        /// registers itself against its hull.
        ///
        /// Idempotent for the same pair, because every joining client walks the
        /// identical spawn plan but there is one ship: re-registering the same
        /// surface at the same root is a no-op and returns false, exactly like
        /// <c>NodeRegistry.Register</c>. Re-registering a surface at a DIFFERENT
        /// root throws - that is a spawn bug (one deck cannot belong to two ships),
        /// not a race to tolerate.
        /// </summary>
        public bool Register(long surfaceEntityId, long shipRootEntityId)
        {
            if (_surfaceToRoot.TryGetValue(surfaceEntityId, out long existingRoot))
            {
                if (existingRoot == shipRootEntityId)
                {
                    return false;
                }
                throw new ArgumentException(
                    "ship surface entity " + surfaceEntityId + " is already registered to ship root "
                    + existingRoot + " and cannot be re-registered to " + shipRootEntityId,
                    nameof(shipRootEntityId));
            }

            _surfaceToRoot.Add(surfaceEntityId, shipRootEntityId);
            return true;
        }

        /// <summary>
        /// The ship root a standable entity belongs to, or null if the id is not a
        /// ship surface - which is what the island, a tree, or empty air looks like
        /// from here, and is precisely the "not aboard" answer.
        /// </summary>
        public long? RootOf(long surfaceEntityId)
        {
            return _surfaceToRoot.TryGetValue(surfaceEntityId, out long root) ? root : (long?)null;
        }

        /// <summary>
        /// Removes a surface when a mounted part detaches or is salvaged. Returns
        /// false when it was not registered. An expected root prevents stale
        /// lifecycle work from removing a surface that has since joined another ship.
        /// </summary>
        public bool Unregister(long surfaceEntityId, long? expectedRoot = null)
        {
            if (!_surfaceToRoot.TryGetValue(surfaceEntityId, out long root)
                || (expectedRoot.HasValue && root != expectedRoot.Value))
            {
                return false;
            }
            return _surfaceToRoot.Remove(surfaceEntityId);
        }

        /// <summary>Whether any ship surface is registered at all. For the aboard glue's fast-out.</summary>
        public bool IsEmpty => _surfaceToRoot.Count == 0;

        /// <summary>Every registered ship root, deduplicated. For diagnostics.</summary>
        public IReadOnlyCollection<long> Roots()
        {
            HashSet<long> roots = new HashSet<long>();
            foreach (long root in _surfaceToRoot.Values)
            {
                roots.Add(root);
            }
            return roots;
        }
    }
}
