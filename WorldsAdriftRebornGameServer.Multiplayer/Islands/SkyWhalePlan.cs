using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE whale, as a pair of wire identities.
    ///
    /// The CALLER IS PART OF THE ANIMAL, not a separate feature, which is why it
    /// gets an id here rather than from somewhere else: a call is meaningless
    /// without the whale it belongs to, and pairing the ids means a log line naming
    /// 2_200_000_000 and one naming 2_200_000_001 are visibly the same creature.
    ///
    /// NO REGION. There used to be one, and its removal is the whole rework: the
    /// animal belongs to the world and its zone is a function of the clock
    /// (<see cref="SkyWhaleCircuit.WhereAt"/>), not a field. A record that still
    /// carried a region would be the first place a reader - or a future feature -
    /// would go looking for "the whale's cell", and would get an answer that stopped
    /// being true a quarter of an hour after boot.
    /// </summary>
    /// <param name="EntityId">The animal. Inside the whale band; see
    /// <see cref="SkyWhalePolicy.FirstWhaleEntityId"/>.</param>
    /// <param name="CallEntityId">Its invisible caller, always
    /// <see cref="EntityId"/> + 1.</param>
    /// <param name="RouteId">The route it flies, named after the cells it covers.
    /// See <see cref="SkyWhaleRoute.RouteIdFor"/> - the map joins its published
    /// geometry to the live whale on exactly this string.</param>
    public readonly record struct SkyWhale(long EntityId, long CallEntityId, string RouteId);

    /// <summary>The world's whale and the route it flies.</summary>
    public readonly record struct SkyWhalePlacement(SkyWhale Whale, SkyWhaleCircuit Circuit);

    /// <summary>
    /// WHERE THE WORLD'S ONE WHALE FLIES, and where its route's waypoints are.
    ///
    /// PURE AND TOTAL, and for the reason
    /// <see cref="IslandFaunaPolicy.PopulationFor"/> gives: this is a function of
    /// the selected island set and NOTHING else - no clock, no entropy, no
    /// accumulated state - so a restarted server re-derives a byte-identical route
    /// and byte-identical entity ids, and a reconnecting player is not handed a
    /// whale whose id used to mean something else. Nothing is persisted because
    /// nothing needs to be.
    ///
    /// A ZONE IS A MAPFILE CELL. That is not this file's invention: it is exactly
    /// the grouping <see cref="RegionRegistry.CreateReleaseWorld"/> already turns
    /// into <c>release-&lt;cell&gt;-region</c>, and
    /// <see cref="SkyWhalePolicy.RegionIdForCell"/> is the one place the name is
    /// formed so the two cannot drift apart. Haven is deliberately not a zone here:
    /// it has one island and carries no surveyed release record.
    ///
    /// EVERY CELL IS ON THE ROUTE, including one that carries a single island. That
    /// is a change: a cell used to need three islands to have a whale of its own and
    /// was otherwise silently skipped, whereas the world route simply strings its
    /// islands in with the rest. The three-waypoint floor is now the WORLD's, and it
    /// is a structural property of a closed spline rather than a per-cell budget -
    /// see <see cref="SkyWhalePolicy.MinimumIslands"/>.
    /// </summary>
    public static class SkyWhalePlan
    {
        /// <summary>
        /// One island's waypoint, in WORLD metres: laterally over the centre of its
        /// terrain envelope, vertically <see cref="SkyWhalePolicy.AltitudeAboveIslandMetres"/>
        /// above its HIGHEST terrain.
        ///
        /// Over the ENVELOPE CENTRE rather than over the island's origin, because
        /// an island's pivot is an arbitrary point in its bundle and several of the
        /// release catalogue's are well outside the rock - the same reason
        /// <see cref="IslandTerrainEnvelope"/> exists at all. Above MaxY rather than
        /// above the walkable band, because the whale must clear the island's PEAK,
        /// not its landing point.
        /// </summary>
        public static SkyWhaleWaypoint WaypointFor(ReleaseIslandRecord island)
        {
            if (island == null) throw new ArgumentNullException(nameof(island));

            IslandTerrainEnvelope envelope = island.Envelope;
            IslandDefinition definition = island.Definition;
            return new SkyWhaleWaypoint(
                definition.Id,
                definition.GlobalOrigin.MetresX + ((envelope.MinX + envelope.MaxX) / 2.0),
                definition.GlobalOrigin.MetresY + envelope.MaxY
                    + SkyWhalePolicy.AltitudeAboveIslandMetres,
                definition.GlobalOrigin.MetresZ + ((envelope.MinZ + envelope.MaxZ) / 2.0));
        }

        /// <summary>
        /// The world's zones and their island waypoints, in CELL ORDER - the input
        /// <see cref="SkyWhaleRoute.Build"/> then re-orders geographically.
        ///
        /// Cell order is ordinal on the cell id. Ordinal rather than culture-aware
        /// for the same reason every other stable-ordering comparison on this server
        /// is: a locale must not be able to reshape the world. Note that it does NOT
        /// decide the travel order - the route orders zones by bearing about the
        /// world centroid, so that a3 does not hand over to b2 across the diagonal -
        /// it only makes this function's output independent of dictionary iteration.
        /// </summary>
        public static IReadOnlyList<SkyWhaleZone> ZonesOf(
            IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));

            List<SkyWhaleZone> zones = new List<SkyWhaleZone>();
            foreach (IGrouping<string, ReleaseIslandRecord> cell in islands
                .GroupBy(record => record.CellId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                zones.Add(new SkyWhaleZone(
                    SkyWhalePolicy.RegionIdForCell(cell.Key),
                    cell.Select(WaypointFor).ToArray()));
            }
            return zones;
        }

        /// <summary>
        /// The world's whale, or null when the selected world cannot carry one.
        ///
        /// ONE placement, not a list, and the signature is the design: there is one
        /// whale, its entity ids are the bottom of the band and never move, and no
        /// caller can be written that iterates over "the whales" and quietly starts
        /// working again if a second one ever appears. If a second whale is ever
        /// wanted it should be a deliberate change to this signature, not a list
        /// that silently grew.
        /// </summary>
        public static SkyWhalePlacement? Build(IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));

            // NAMED AFTER THE CELLS IT COVERS, because the route IS a function of
            // them - see SkyWhaleRoute.RouteIdFor for the map join this protects.
            string routeId = SkyWhaleRoute.RouteIdFor(
                islands.Select(record => record.CellId));

            SkyWhaleCircuit? circuit = SkyWhaleCircuit.Build(
                routeId, SkyWhaleRoute.Build(ZonesOf(islands)));
            if (circuit == null)
            {
                return null;
            }

            return new SkyWhalePlacement(
                new SkyWhale(
                    SkyWhalePolicy.FirstWhaleEntityId,
                    SkyWhalePolicy.FirstWhaleEntityId + 1,
                    routeId),
                circuit);
        }

        /// <summary>
        /// How many zones the selected world has. Reported at boot beside the route's
        /// own zone list so an operator is told what the migration covers rather than
        /// left to count waypoints.
        /// </summary>
        public static int RegionCount(IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));
            return islands
                .GroupBy(record => record.CellId, StringComparer.OrdinalIgnoreCase)
                .Count();
        }
    }
}
