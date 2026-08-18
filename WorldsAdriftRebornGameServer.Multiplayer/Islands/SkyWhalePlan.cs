using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One region's whale, as a pair of wire identities.
    ///
    /// The CALLER IS PART OF THE ANIMAL, not a separate feature, which is why it
    /// gets an id here rather than from somewhere else: a call is meaningless
    /// without the whale it belongs to, and pairing the ids means a log line naming
    /// 2_200_000_004 and one naming 2_200_000_005 are visibly the same creature.
    /// </summary>
    /// <param name="EntityId">The animal. Inside the whale band; see
    /// <see cref="SkyWhalePolicy.FirstWhaleEntityId"/>.</param>
    /// <param name="CallEntityId">Its invisible caller, always
    /// <see cref="EntityId"/> + 1.</param>
    /// <param name="Region">The region it never leaves.</param>
    public readonly record struct SkyWhale(long EntityId, long CallEntityId, RegionId Region);

    /// <summary>One region's whale and the circuit it flies.</summary>
    public readonly record struct SkyWhalePlacement(SkyWhale Whale, SkyWhaleCircuit Circuit);

    /// <summary>
    /// WHICH REGIONS GET A WHALE, and where its circuit's waypoints are.
    ///
    /// PURE AND TOTAL, and for the reason
    /// <see cref="IslandFaunaPolicy.PopulationFor"/> gives: this is a function of
    /// the selected island set and NOTHING else - no clock, no entropy, no
    /// accumulated state - so a restarted server re-derives byte-identical entity
    /// ids in byte-identical order, and a reconnecting player is not handed a whale
    /// whose id used to mean something else. Nothing is persisted because nothing
    /// needs to be.
    ///
    /// A REGION IS A MAPFILE CELL. That is not this file's invention: it is
    /// exactly the grouping <see cref="RegionRegistry.CreateReleaseWorld"/> already
    /// turns into <c>release-&lt;cell&gt;-region</c>, and
    /// <see cref="SkyWhalePolicy.RegionIdForCell"/> is the one place the name is
    /// formed so the two cannot drift apart. Haven is deliberately not a region
    /// here: it has one island, carries no surveyed release record, and a circuit
    /// through one point is not a circuit.
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
        /// Every whale the selected world carries, one per region that can hold a
        /// circuit, in CELL ORDER.
        ///
        /// Cell order is ordinal on the cell id, and it is what pins the entity ids:
        /// the nth region gets <c>FirstWhaleEntityId + n * EntityIdsPerWhale</c>.
        /// Ordinal rather than culture-aware for the same reason every other
        /// stable-ordering comparison on this server is - a locale must not be able
        /// to renumber the world.
        ///
        /// A region with fewer than <see cref="SkyWhalePolicy.MinimumIslandsPerRegion"/>
        /// islands STILL CONSUMES ITS ID BLOCK even though it carries no whale. That
        /// looks wasteful and is deliberate: it means adding a district to the
        /// rollout cannot renumber the whale of a district that was already there,
        /// which is the same "ids are a pure function of the catalogue, not of the
        /// selection" property the fauna plan holds.
        /// </summary>
        public static IReadOnlyList<SkyWhalePlacement> Build(
            IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));

            List<SkyWhalePlacement> placements = new List<SkyWhalePlacement>();
            int index = 0;
            foreach (IGrouping<string, ReleaseIslandRecord> cell in islands
                .GroupBy(record => record.CellId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                RegionId region = SkyWhalePolicy.RegionIdForCell(cell.Key);
                long entityId = SkyWhalePolicy.FirstWhaleEntityId
                    + ((long)index * SkyWhalePolicy.EntityIdsPerWhale);
                index++;

                SkyWhaleCircuit? circuit = SkyWhaleCircuit.Build(
                    region, cell.Select(WaypointFor));
                if (circuit == null)
                {
                    continue;
                }

                placements.Add(new SkyWhalePlacement(
                    new SkyWhale(entityId, entityId + 1, region), circuit));
            }
            return placements.AsReadOnly();
        }

        /// <summary>
        /// How many regions the selected world has, whether or not each can carry a
        /// whale. Reported at boot beside the seeded count so an operator is told
        /// "3 of 4" rather than left to notice a silent region.
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
