namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Which island's terrain a world position needs, if any.
    /// </summary>
    public enum IslandLocationKind
    {
        /// <summary>
        /// No island the server knows the shape of is near this point. Open sky:
        /// a player on a ship, or a point in the void between islands.
        /// </summary>
        OpenSky,

        /// <summary>
        /// The point is inside (or within the ground slack of) one island's
        /// extracted collision envelope. That island's terrain is the ground this
        /// point stands on, and it must be checked out before anybody is put here.
        /// </summary>
        OnKnownTerrain,
    }

    /// <summary>Where a world position is, in island terms.</summary>
    public readonly record struct IslandLocation(
        IslandLocationKind Kind,
        IslandDefinition? Island,
        double MetresFromTerrain)
    {
        public static readonly IslandLocation OpenSky =
            new IslandLocation(IslandLocationKind.OpenSky, null, double.PositiveInfinity);

        /// <summary>The island's display name, or "open sky" when there is none.</summary>
        public string Name => Island == null ? "open sky" : Island.DisplayName;
    }

    /// <summary>
    /// WHICH island a stored world position belongs to - the missing half of
    /// "put this player back where they logged out".
    ///
    /// WHAT THIS CANNOT DO, and does not pretend to. The server has no terrain
    /// query: no raycast, no collider, no loaded height table. It cannot answer
    /// "is this point standing on ground" or "is this point inside a rock". See
    /// <see cref="PlayerPositionPolicy"/>, which documents the same limit for the
    /// same reason.
    ///
    /// WHAT IT CAN DO is strictly coarser and entirely decidable from data we
    /// already extracted: every island has a measured axis-aligned collision
    /// envelope (<see cref="IslandTerrainEnvelopes"/>), so "which island's terrain
    /// bundle would this point be standing on, if it is standing on anything" has
    /// an exact answer. That is enough for the only question the spawn path needs
    /// to ask - WHICH terrain must be checked out before this player is placed -
    /// without ever claiming to know whether the ground is solid at that spot.
    ///
    /// The answer for a point in the void is <see cref="IslandLocationKind.OpenSky"/>,
    /// and OpenSky is deliberately NOT a refusal: a player who logged out on their
    /// ship has no terrain to wait for, and the fall net that already exists is the
    /// only guard there ever was for that case.
    /// </summary>
    public static class IslandLocationPolicy
    {
        /// <summary>
        /// How far outside an envelope a point may be and still count as standing
        /// on that island.
        ///
        /// It is not zero because the envelope is the collision surface's AABB and
        /// a standing player is ABOVE the surface: on a peak, a character capsule
        /// sits a metre or two past <c>MaxY</c>, and a player on an overhanging
        /// ledge or a placed structure can be further. It is not large because the
        /// only cost of a false OpenSky is losing a terrain wait we would have
        /// liked, while the only cost of a false OnKnownTerrain is a bounded wait
        /// for terrain that was going to be streamed anyway. Both failure modes are
        /// mild, which is why a single generous constant is honest here and a
        /// pretend-precise geometric test would not be.
        /// </summary>
        public const double GroundSlackMetres = 60.0;

        /// <summary>
        /// The nearest known island to <paramref name="position"/>, or
        /// <see cref="IslandLocation.OpenSky"/> if the nearest one is further than
        /// <paramref name="slackMetres"/> from its collision envelope.
        ///
        /// Candidates are (definition, envelope) pairs; ties break on island id so
        /// the answer never depends on enumeration order.
        /// </summary>
        public static IslandLocation Locate(
            FixedPointPosition position,
            IEnumerable<(IslandDefinition Island, IslandTerrainEnvelope Envelope)> candidates,
            double slackMetres = GroundSlackMetres)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));

            IslandDefinition? best = null;
            double bestDistanceSquared = double.PositiveInfinity;

            foreach ((IslandDefinition island, IslandTerrainEnvelope envelope) in candidates)
            {
                if (island == null || envelope.IslandId != island.Id) continue;

                double distanceSquared = envelope.DistanceSquaredTo(position, island);
                if (best == null
                    || distanceSquared < bestDistanceSquared
                    || (distanceSquared == bestDistanceSquared && island.Id.CompareTo(best.Id) < 0))
                {
                    best = island;
                    bestDistanceSquared = distanceSquared;
                }
            }

            if (best == null) return IslandLocation.OpenSky;

            double metres = Math.Sqrt(bestDistanceSquared);
            return metres <= slackMetres
                ? new IslandLocation(IslandLocationKind.OnKnownTerrain, best, metres)
                : IslandLocation.OpenSky;
        }

        /// <summary>
        /// Every island in the preserved world whose collision envelope is known,
        /// paired with that envelope. This is the WHOLE map, not this boot's
        /// registered topology, on purpose: a stored position on an island the
        /// server is not hosting today must be recognisable as "that island" so it
        /// can be refused with a reason, rather than mistaken for open sky and
        /// restored into a hole.
        /// </summary>
        public static IEnumerable<(IslandDefinition Island, IslandTerrainEnvelope Envelope)> KnownWorld()
        {
            HashSet<IslandId> seen = new HashSet<IslandId>();

            foreach (IslandDefinition island in IslandCatalog.AllNamed)
            {
                IslandTerrainEnvelope? envelope = IslandTerrainEnvelopes.ByIsland(island.Id);
                if (envelope.HasValue && seen.Add(island.Id))
                {
                    yield return (island, envelope.Value);
                }
            }

            foreach (ReleaseIslandRecord record in ReleaseWorldCatalog.All)
            {
                if (seen.Add(record.Definition.Id))
                {
                    yield return (record.Definition, record.Envelope);
                }
            }
        }
    }
}
