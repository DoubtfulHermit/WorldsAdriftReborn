using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// WHICH islands the shrine is allowed to send somebody to, and WHERE on each
    /// of them they land.
    ///
    /// THE TIER-1 EQUIVALENCE. "The Wilderness" is Bossa's Tier-1 band, which is
    /// exactly the four MapFile cells A2, A3, B2 and B3 and exactly the 46 islands
    /// in them - <see cref="ReleaseWorldRolloutPolicy"/> documents the equivalence
    /// and ReleaseWorldTierSelectionTests pins both halves of it. This type reads
    /// each record's own <c>cellTier</c> rather than re-listing the cells, so a
    /// future catalogue regeneration that moved an island between cells could not
    /// leave the shrine pointing at a stale list.
    ///
    /// REGISTERED, NOT MERELY KNOWN. The catalogue knows all 46; a given boot may
    /// have registered none of them (production ran <c>C6</c> for a long time) or
    /// all of them (<c>WAREBORN_RELEASE_WORLD_DISTRICTS=tier1</c>). Every entry
    /// point here takes THIS BOOT'S registered islands and intersects, because
    /// sending a player to terrain the server never registered is a fall into the
    /// void with extra steps.
    /// </summary>
    public static class WildernessCatalog
    {
        /// <summary>The MapFile cell tier "the Wilderness" means.</summary>
        public const int WildernessTier = 1;

        /// <summary>
        /// How far above the measured surface sample the player is placed, metres.
        ///
        /// The same 2.00 m used by every hand-derived destination this server
        /// already ships - Haven's spawn point, Trades Challenge, Mental Facility -
        /// and for the same reason: the sample is a collision-mesh VERTEX and a
        /// character capsule has to stand on top of it, not inside it. Keeping one
        /// number means "how high do we place people" has one answer.
        /// </summary>
        public const double StandOffMetres = 2.00;

        /// <summary>Every Tier-1 record in the preserved world, ordered by island id.</summary>
        public static IReadOnlyList<ReleaseIslandRecord> All { get; } =
            ReleaseWorldCatalog.All
                .Where(record => record.CellTier == WildernessTier)
                .OrderBy(record => record.Definition.Id)
                .ToArray();

        /// <summary>
        /// The destination for one Tier-1 island, or null when the island is not
        /// Tier 1 at all. Never null for a Tier-1 island: the catalogue carries a
        /// landing point for every one of the 254 records, and
        /// WildernessCatalogTests pins that.
        /// </summary>
        public static WildernessDestination? For(IslandId islandId)
        {
            ReleaseIslandRecord? record = ReleaseWorldCatalog.ByIsland(islandId);
            if (record == null || record.CellTier != WildernessTier) return null;
            return DestinationFor(record);
        }

        /// <summary>
        /// The landing point for ANY catalogued island, Tier 1 or not.
        ///
        /// <see cref="For"/> is the shrine's door and applies the Wilderness tier
        /// rule deliberately; this is the same landing arithmetic with that rule
        /// lifted, for the operator surface, which may send a player to a tier-3
        /// island the shrine would never draw. It is the SAME method underneath
        /// rather than a second copy: the stand-off, the local-to-global conversion
        /// and the provenance sentence must not be able to drift between "where the
        /// shrine puts you" and "where an operator puts you".
        ///
        /// <paramref name="registered"/> is this boot's registry definition when
        /// there is one, for the reason <see cref="Open"/> gives: the registry is
        /// what the terrain entity was actually spawned from.
        /// </summary>
        public static WildernessDestination? Landing(
            IslandId islandId, IslandDefinition? registered = null)
        {
            ReleaseIslandRecord? record = ReleaseWorldCatalog.ByIsland(islandId);
            return record == null ? null : DestinationFor(record, registered);
        }

        /// <summary>
        /// The Tier-1 destinations available on THIS boot: the intersection of the
        /// Wilderness with the islands whose terrain is actually registered.
        ///
        /// Ordered by island id, not by registration order, so
        /// <see cref="WildernessGraduationPolicy"/>'s injected index means the same
        /// island on every server that registered the same set. An unordered list
        /// would make a "random" island depend on dictionary enumeration, which is
        /// the kind of thing that is stable right up until it is not.
        /// </summary>
        public static IReadOnlyList<WildernessDestination> Open(
            IEnumerable<IslandDefinition> registered)
        {
            if (registered == null) throw new ArgumentNullException(nameof(registered));

            List<WildernessDestination> open = new();
            HashSet<IslandId> seen = new();
            foreach (IslandDefinition island in registered)
            {
                if (island == null || !seen.Add(island.Id)) continue;
                ReleaseIslandRecord? record = ReleaseWorldCatalog.ByIsland(island.Id);
                if (record == null || record.CellTier != WildernessTier) continue;

                // Take the REGISTERED definition's origin, not the catalogue's own.
                // They agree today, and a test says so; but the registry is what the
                // terrain entity was actually spawned from, so if they ever
                // disagreed the registry is the one the player's client believes.
                open.Add(DestinationFor(record, island));
            }
            open.Sort((left, right) => left.IslandId.CompareTo(right.IslandId));
            return open;
        }

        /// <summary>
        /// The (island, envelope) pairs for a set of destinations, in the shape
        /// <see cref="IslandLocationPolicy.Locate"/> wants. Used to answer "is this
        /// stored logout position on a Wilderness island, and which one" without
        /// giving that policy a second way to enumerate the world.
        /// </summary>
        public static IEnumerable<(IslandDefinition Island, IslandTerrainEnvelope Envelope)> Envelopes(
            IEnumerable<WildernessDestination> destinations)
        {
            if (destinations == null) throw new ArgumentNullException(nameof(destinations));
            foreach (WildernessDestination destination in destinations)
            {
                ReleaseIslandRecord? record = ReleaseWorldCatalog.ByIsland(destination.IslandId);
                if (record != null) yield return (record.Definition, record.Envelope);
            }
        }

        /// <summary>
        /// Converts a wilderness arrival into the teleport type the existing
        /// machinery moves players with.
        ///
        /// <c>landsOnLoadedGround</c> is FALSE and that is deliberate, not
        /// pessimism. It is the same call <c>logout-restore</c> makes: the server
        /// has no terrain query, so however well-evidenced a surface sample is,
        /// this server is not entitled to claim there is ground on a particular
        /// client at a particular moment. What protects the arrival is the
        /// <see cref="WildernessDestination.WorldEntityKey"/> below - it makes the
        /// teleport path request that terrain for that peer and refuse to send
        /// until the peer has acked it - not a boolean.
        /// </summary>
        public static TeleportDestination AsTeleportDestination(
            WildernessDestination destination, string name)
        {
            return new TeleportDestination(
                name,
                destination.Position,
                landsOnLoadedGround: false,
                destination.DisplayName + " (" + destination.CellId + "): " + destination.Provenance,
                destination.WorldEntityKey);
        }

        private static WildernessDestination DestinationFor(
            ReleaseIslandRecord record, IslandDefinition? registered = null)
        {
            IslandDefinition definition = registered ?? record.Definition;
            IslandLandingPoint pad = record.Landing;
            return new WildernessDestination(
                definition.Id,
                definition.DisplayName,
                record.CellId,
                definition.WorldEntityKey,
                definition.LocalToGlobal(pad.LocalX, pad.LocalY + StandOffMetres, pad.LocalZ),
                Describe(pad));
        }

        private static string Describe(IslandLandingPoint pad)
        {
            return (pad.Reviewed ? "hand-reviewed" : "generated")
                + " surface sample at island-local ("
                + pad.LocalX.ToString("0.##") + ", " + pad.LocalY.ToString("0.##") + ", "
                + pad.LocalZ.ToString("0.##") + ") m, normal ny " + pad.UpwardNormal.ToString("0.###")
                + ", " + pad.SupportingColumns + " level neighbours within "
                + pad.WorstStepMetres.ToString("0.##") + " m, plus a "
                + StandOffMetres.ToString("0.00") + " m stand-off";
        }
    }
}
