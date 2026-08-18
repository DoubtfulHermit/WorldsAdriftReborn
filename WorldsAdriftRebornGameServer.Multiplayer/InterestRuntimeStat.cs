namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One island's resources as held by ONE peer: the checkout unit of the
    /// resource interest system, counted rather than listed because a node id
    /// means nothing to an operator and five hundred of them per peer would be
    /// most of the snapshot.
    /// </summary>
    public readonly struct InterestPeerIslandStat
    {
        public string IslandId { get; }
        public int CheckedOut { get; }

        public InterestPeerIslandStat(string islandId, int checkedOut)
        {
            IslandId = islandId ?? string.Empty;
            CheckedOut = checkedOut < 0 ? 0 : checkedOut;
        }
    }

    /// <summary>
    /// What the interest systems have CURRENTLY streamed to one peer: which
    /// islands' resources they hold (and how many nodes of each), how many
    /// creatures, and which ship domains are in their checkout ledger. Terrain
    /// holdings are deliberately NOT here - they already ride the terrain
    /// section per peer, in richer lifecycle detail, and a second copy would be
    /// a second thing to disagree.
    /// </summary>
    public readonly struct InterestPeerStat
    {
        public long PlayerEntityId { get; }
        public IReadOnlyList<InterestPeerIslandStat> ResourceIslands { get; }
        public int FaunaCheckedOut { get; }
        public IReadOnlyList<string> ShipDomainIds { get; }

        public InterestPeerStat(
            long playerEntityId,
            IReadOnlyList<InterestPeerIslandStat>? resourceIslands,
            int faunaCheckedOut,
            IReadOnlyList<string>? shipDomainIds)
        {
            PlayerEntityId = playerEntityId;
            ResourceIslands = resourceIslands ?? Array.Empty<InterestPeerIslandStat>();
            FaunaCheckedOut = faunaCheckedOut < 0 ? 0 : faunaCheckedOut;
            ShipDomainIds = shipDomainIds ?? Array.Empty<string>();
        }

        /// <summary>Every resource node this peer holds, summed across islands.</summary>
        public int ResourceCheckedOut
        {
            get
            {
                int total = 0;
                foreach (InterestPeerIslandStat island in ResourceIslands)
                {
                    total += island.CheckedOut;
                }
                return total;
            }
        }
    }

    /// <summary>
    /// THE INTEREST PICTURE OF ONE BOOT (schema v10+): the radii, budgets and
    /// gates every streaming decision is made with, and what each peer holds
    /// right now.
    ///
    /// Every number here is a READ of the running configuration - the same
    /// parsed values the services actually decide with - never a restated
    /// default. That is the point of publishing them: the operator console must
    /// describe the deployment in front of it, and a server tuned by env vars
    /// must move the console with it. The boot facts this replaces lived only
    /// in log lines like "[resource-interest] ... load 600 m / unload 800 m",
    /// which stopped being consultable the moment the scrollback did.
    ///
    /// Like <see cref="ShipMapRuntimeStat"/>, a server that predates this
    /// section reports nothing and the reader renders "not reported" - and a
    /// server that HAS it but has a system off reports <c>enabled:false</c>
    /// with its configured radii, because "off" and "unknown" are different
    /// operator answers.
    /// </summary>
    public readonly struct InterestRuntimeStat
    {
        /// <summary>A server that reports no interest telemetry at all.</summary>
        public static InterestRuntimeStat Off => default;

        public InterestRuntimeStat(
            bool resourcesEnabled,
            double resourceLoadRadiusMetres,
            double resourceUnloadRadiusMetres,
            int resourcePerPeerBudget,
            double resourceConnectRadiusMetres,
            bool faunaEnabled,
            double faunaLoadRadiusMetres,
            double faunaUnloadRadiusMetres,
            double shipLoadRadiusMetres,
            double shipUnloadRadiusMetres,
            double terrainConnectRadiusMetres,
            bool loadBarrier,
            int spawnPaceMs,
            IReadOnlyList<InterestPeerStat>? peers)
        {
            Present = true;
            ResourcesEnabled = resourcesEnabled;
            ResourceLoadRadiusMetres = resourceLoadRadiusMetres;
            ResourceUnloadRadiusMetres = resourceUnloadRadiusMetres;
            ResourcePerPeerBudget = resourcePerPeerBudget < 0 ? 0 : resourcePerPeerBudget;
            ResourceConnectRadiusMetres = resourceConnectRadiusMetres;
            FaunaEnabled = faunaEnabled;
            FaunaLoadRadiusMetres = faunaLoadRadiusMetres;
            FaunaUnloadRadiusMetres = faunaUnloadRadiusMetres;
            ShipLoadRadiusMetres = shipLoadRadiusMetres;
            ShipUnloadRadiusMetres = shipUnloadRadiusMetres;
            TerrainConnectRadiusMetres = terrainConnectRadiusMetres;
            LoadBarrier = loadBarrier;
            SpawnPaceMs = spawnPaceMs < 0 ? 0 : spawnPaceMs;
            _peers = peers ?? Array.Empty<InterestPeerStat>();
        }

        // Nullable behind the property so default(InterestRuntimeStat) - the
        // "no telemetry" value - still has a walkable empty list.
        private readonly IReadOnlyList<InterestPeerStat>? _peers;

        /// <summary>False on a default value: the section was never built.</summary>
        public bool Present { get; }

        // Resources: island-keyed checkout to the island's envelope.
        public bool ResourcesEnabled { get; }
        public double ResourceLoadRadiusMetres { get; }
        public double ResourceUnloadRadiusMetres { get; }
        public int ResourcePerPeerBudget { get; }

        /// <summary>The connect-time spatial step: what is in the immutable connect plan.</summary>
        public double ResourceConnectRadiusMetres { get; }

        // Wildlife: island-keyed like resources, with a per-peer creature cap
        // that rides the fauna section (it is that system's number).
        public bool FaunaEnabled { get; }
        public double FaunaLoadRadiusMetres { get; }
        public double FaunaUnloadRadiusMetres { get; }

        // Ship domains: spatial distance to the hull.
        public double ShipLoadRadiusMetres { get; }
        public double ShipUnloadRadiusMetres { get; }

        /// <summary>The terrain step of the connect plan (the terrain load radius).</summary>
        public double TerrainConnectRadiusMetres { get; }

        /// <summary>
        /// Whether WAREBORN_LOAD_BARRIER armed the loading barrier this boot.
        /// Operationally load-bearing: without it no release island can become
        /// a terrain candidate, which reads as a world of dead islands (see
        /// docs/research/findings-island-resource-interest.md).
        /// </summary>
        public bool LoadBarrier { get; }

        /// <summary>The live WAREBORN_SPAWN_PACE_MS spacing between AfterPlayer entity releases.</summary>
        public int SpawnPaceMs { get; }

        public IReadOnlyList<InterestPeerStat> Peers => _peers ?? Array.Empty<InterestPeerStat>();
    }
}
