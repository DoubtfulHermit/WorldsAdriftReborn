using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about the ANCHORED metal DEPOSIT this server can place - the real
    /// Worlds Adrift ore mechanic, as opposed to the loose <see cref="MetalNodes"/>
    /// nugget. A deposit is a single SpatialOS entity spawned from the
    /// <c>metal_deposit_entity</c> prefab whose salvage beam breaks the outer CRUST
    /// into pieces where it is hit (12283 MetalRockCrustState.shotPoints +
    /// ShotCrustEvent), then depletes the CORE's health (1016 ItemHealthState) over
    /// ~10 shots, and at zero flags the core destroyed and the crust exploded
    /// (2103 MetalRockCoreState.isDestroyed + 12283 exploded). Fully
    /// server-authoritative; the client is stock.
    ///
    /// WHY A SEPARATE MODULE FROM THE NUGGET. The nugget rolls and its "collection"
    /// is a server-invented sink teleport; the deposit is a static prop with the
    /// game's own authored crust/core loop. They share the <see cref="NodeRegistry"/>
    /// ledger (which already carries shotPoints) and the <see cref="MetalHarvest"/>
    /// shot counter, but everything that differs - the prefab, the variant, the
    /// component set, the depletion feedback - lives here.
    ///
    /// EVERYTHING HERE IS EITHER MEASURED/EXTRACTED OR MARKED AS AN ASSUMPTION, per
    /// the standing caveat that a third of this project's confident static
    /// conclusions have been wrong when run.
    /// </summary>
    public static class MetalDeposits
    {
        /// <summary>
        /// The prefab name on the wire, BARE.
        ///
        /// VERIFIED: <c>metal_deposit_entity</c> is line 328 of
        /// docs/research/loop/data/prefab-names.tsv with BOTH the client and worker
        /// columns "yes", and it is the name strings-scanned out of the shipped
        /// resources.assets (<c>metal_deposit_entity_unityclient</c> /
        /// <c>_unityworker</c>). Sent bare for the same reason "Tree"/"MetalNugget"
        /// are: the client appends the worker suffix itself
        /// (WorkerSpecificPrefabName.GetWorkerSpecificPrefabName), so the bare name
        /// is correct and a "_unityclient" suffix would be doubled and resolve to
        /// nothing.
        /// </summary>
        public const string AssetName = "metal_deposit_entity";

        /// <summary>Registration-key prefix for a placed deposit. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "deposit-";

        /// <summary>The registration key for the deposit at a given index.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>Whether a registration key names a placed deposit.</summary>
        public static bool IsDepositKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// The first/default 1255 variantId - a real <c>MetalDepositVisuals</c> asset id.
        ///
        /// VERIFIED by a strings scan of the shipped
        /// <c>sharedassets0.assets</c>: the <c>MetalDepositsByBiome</c>
        /// BiomeSpecificResourceTable lists, under EVERY biome
        /// (<c>MetalDeposits_Biome01</c> through <c>_Biome04</c>), the three
        /// AssetVariant ids <c>metal_deposit_composite_light_01</c> /
        /// <c>_02</c> / <c>_03</c>. SharedResourceData.MetalDepositVariant looks the
        /// id up case-insensitively for the deposit's biome, so this resolves in any
        /// biome Haven turns out to be. A variantId that does NOT resolve leaves
        /// MetalDepositVisualiser disabled and the entity invisible - so this is the
        /// single most important string in the deposit.
        ///
        /// The shipped release contains three genuinely different meshes. Variant 03
        /// is the tall ~5.1 m formation; variants 01 and 02 are shorter and broader.
        /// Retail selected one variant and replicated its id. This server cycles the
        /// same verified set by stable placement index, so all peers and restarts see
        /// identical geometry without collapsing every field to variant 01.
        /// </summary>
        public const string DefaultVariantId = "metal_deposit_composite_light_01";

        public static readonly IReadOnlyList<string> VariantIds = Array.AsReadOnly(new[]
        {
            DefaultVariantId,
            "metal_deposit_composite_light_02",
            "metal_deposit_composite_light_03",
        });

        /// <summary>
        /// The 1255 variantId for a stable placement index. A non-empty
        /// <c>WAREBORN_DEPOSIT_VARIANT</c> remains a global diagnostic override;
        /// otherwise indices cycle through the three shipped variants.
        /// </summary>
        public static string VariantIdFor(int placementIndex)
        {
            return VariantIdFor(
                placementIndex,
                System.Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_VARIANT"));
        }

        /// <summary>Pure form used to validate configured override behavior.</summary>
        public static string VariantIdFor(int placementIndex, string? configuredOverride)
        {
            if (placementIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(placementIndex));
            return string.IsNullOrWhiteSpace(configuredOverride)
                ? VariantIds[placementIndex % VariantIds.Count]
                : configuredOverride.Trim();
        }

        // ------------------------------------------------------------------
        // The depletion sizing. The client's own crust/core feedback is driven by
        // 1016 HealthPct and by the shotPoints cloud - both of which the server
        // authors - so these numbers ARE the mining feel.
        // ------------------------------------------------------------------

        /// <summary>
        /// 1016 ItemHealthState.maxHealth for a deposit core. With
        /// <see cref="SalvageShootDamage"/> = 200 this is <see cref="ShotsToDeplete"/>
        /// = 10 shots, ~7.5 s of held beam at the client's ~0.75 s MinDeployInterval -
        /// the sizing the research doc measured (findings-metal-deposits.md).
        /// </summary>
        public const int MaxHealth = 2000;

        /// <summary>Health removed per salvage shot. 200 * 10 = <see cref="MaxHealth"/>.</summary>
        public const int SalvageShootDamage = 200;

        /// <summary>Salvage shots to empty one deposit. See <see cref="MaxHealth"/>.</summary>
        public const int ShotsToDeplete = 10;

        /// <summary>
        /// Units of metal a deposit frees when the core is destroyed. Fed to
        /// <see cref="MetalHarvest.Place"/> and granted through
        /// <c>HarvestReward.Award</c> on the single deplete transition. Richer than a
        /// nugget's five because a deposit is ten shots of work; invented, like the
        /// nugget's yield, since the authored scrap-spawn path is gone.
        /// </summary>
        public const int YieldUnits = 12;

        /// <summary>
        /// Current core health for a deposit that has taken <paramref name="hits"/>
        /// salvage shots, clamped to [0, <see cref="MaxHealth"/>]. Pure, so both the
        /// live 1016 broadcast and a late joiner's 1016 seed compute the same value
        /// from the shot count without storing a second number.
        /// </summary>
        public static int HealthAfter(int hits)
        {
            if (hits < 0)
            {
                hits = 0;
            }
            int health = MaxHealth - (hits * SalvageShootDamage);
            return health < 0 ? 0 : health;
        }

        /// <summary>The island every player spawns on; deposits are placed island-local against it.</summary>
        public static readonly FixedPointPosition IslandOrigin = IslandCatalog.Haven.GlobalOrigin;

        /// <summary>
        /// One island-local deposit placement on Haven: a metal type, a quality, and a
        /// surface vertex (island-local metres).
        /// </summary>
        public readonly struct Placement
        {
            public Placement(string metalType, int quality, double localX, double localY, double localZ)
            {
                MetalType = metalType;
                Quality = quality;
                LocalX = localX;
                LocalY = localY;
                LocalZ = localZ;
            }

            public string MetalType { get; }
            public int Quality { get; }
            public double LocalX { get; }
            public double LocalY { get; }
            public double LocalZ { get; }
        }

        /// <summary>
        /// The deposit placements on Haven, island-local metres - a DENSE, reviewed
        /// resource field generated deterministically from the real extracted Haven
        /// LOD0 surface table, NOT a hand-measured handful.
        ///
        /// Index 0 is the PROVEN-mode deposit: island-local (216.0, 4.57, 8.0) - a
        /// measured LOD0 surface vertex the nugget's proven node also uses (8.9 m from
        /// the spawn point, ny = 0.995, well inside the salvager's 10 m aim reach), so
        /// a tester walks a few paces and aims. Everything after it is emitted by
        /// <see cref="Resources.SurfacePlacementGenerator"/> over
        /// <see cref="Resources.HavenSurface.Samples"/> under
        /// <see cref="Resources.HavenSurface.DepositConfig"/>: upward-facing ground
        /// (ny &gt;= 0.92), the FULL extracted altitude range, Poisson-disk thinned
        /// to a 22 m minimum spacing, and kept clear of the spawn, the ship and the
        /// distributed trees. Forty deposits cover the complete ~560 x 290 m terrain
        /// instead of concentrating on the old 1.5..12 m eastern shelf.
        ///
        /// DETERMINISTIC AND STABLE: the generator uses no RNG and no clock, so the
        /// same embedded surface and config produce the identical layout every
        /// restart - mining/persistence state keyed on a deposit's index stays
        /// consistent. Density and spread are tunable via the documented knobs on
        /// <see cref="Resources.HavenSurface"/> (min spacing, height band, normal
        /// threshold, target count, clearances).
        ///
        /// Haven has no surviving per-island community resource row because it was
        /// Bossa-authored, not a Workshop island. Its metal spread therefore comes
        /// from <see cref="Gathering.IslandMetalTable.HavenRing"/> - the surveyed
        /// TIER-1 COHORT's own frequencies, which is how the other 193 unsurveyed
        /// islands were already composed - with iron pinned to index 0 so the first
        /// rock beside the spawn is always the metal the first recipe wants. Read
        /// that field's note before changing any of it.
        /// </summary>
        public static readonly IReadOnlyList<Placement> HavenPlacements = BuildHavenPlacements();

        private static IReadOnlyList<Placement> BuildHavenPlacements()
        {
            IReadOnlyList<Resources.GeneratedPlacement> locals = Resources.HavenSurface.DepositLocals();
            List<Placement> placements = new List<Placement>(locals.Count);
            for (int i = 0; i < locals.Count; i++)
            {
                Resources.GeneratedPlacement p = locals[i];
                placements.Add(new Placement(MetalTypeFor(i), QualityFor(i), p.LocalX, p.LocalY, p.LocalZ));
            }
            return placements;
        }

        /// <summary>
        /// The Haven starter-biome metal for a placement index.
        ///
        /// INDEX 0 IS ALWAYS IRON, unconditionally and before the ring is consulted.
        /// That node is the proven placement 8.9 m from the spawn point, and a new
        /// player walking up to it and finding bronze would make the starter recipe
        /// look broken rather than the world look varied.
        ///
        /// Everything after it comes from the tier-1 cohort ring - see
        /// <see cref="Gathering.IslandMetalTable.HavenRing"/>, which is where the
        /// justification and the WAREBORN TUNING label live.
        /// </summary>
        private static string MetalTypeFor(int index)
        {
            if (index == 0)
            {
                return Gathering.IslandMetalTable.FallbackMetal;
            }

            Islands.SurveyedMetal? draw = Gathering.IslandMetalTable.DrawFor(
                Islands.IslandCatalog.HavenId, index);

            return draw == null
                ? Gathering.IslandMetalTable.FallbackMetal
                : Gathering.IslandMetalTable.ItemTypeIdOf(draw);
        }

        /// <summary>
        /// Haven's quality. One value for every node, deliberately: the surveyed
        /// tier-1 quality band is 1..4, so drawing Haven's quality from the cohort
        /// too would cut the starter island's metal in the same change that first
        /// made quality reach the item at all. See
        /// <see cref="Gathering.IslandMetalTable.HavenQuality"/>.
        /// </summary>
        private static int QualityFor(int index) => Gathering.IslandMetalTable.HavenQuality;

        /// <summary>
        /// The deposit for a registration key ("deposit-N"), or null if the key is not
        /// a deposit's. Deterministic: the same key always maps to the same placement,
        /// so the server can recover a deposit's facts from its <see cref="WorldEntity"/>
        /// at registration time without threading the list through the spawn seam.
        /// </summary>
        public static MetalNode? ByKey(string key)
        {
            MetalNode? release = Resources.ReleaseWorldResources.DepositByKey(key);
            if (release != null)
            {
                return release;
            }
            MetalNode? tradesChallenge = Resources.TradesChallengeResources.DepositByKey(key);
            if (tradesChallenge != null)
            {
                return tradesChallenge;
            }
            if (!IsDepositKey(key))
            {
                return null;
            }
            if (!int.TryParse(key.Substring(KeyPrefix.Length), out int index)
                || index < 0 || index >= HavenPlacements.Count)
            {
                return null;
            }
            return NodeAt(index);
        }

        /// <summary>The deposit at a placement index, as a pure <see cref="MetalNode"/> value.</summary>
        public static MetalNode NodeAt(int index)
        {
            Placement p = HavenPlacements[index];
            return new MetalNode(
                KeyFor(index),
                p.MetalType,
                p.Quality,
                IslandCatalog.Haven.LocalToGlobal(p.LocalX, p.LocalY, p.LocalZ),
                isDeposit: true,
                variantId: VariantIdFor(index));
        }

        /// <summary>
        /// How many seats the understorm re-roll can choose from on Haven - the size of
        /// <see cref="Resources.HavenSurface.DepositPool"/>. Larger than
        /// <see cref="HavenPlacements"/>.Count, which is how many are occupied at once.
        /// </summary>
        public static int RerollSeatCount => Resources.HavenSurface.DepositPool().Count;

        /// <summary>
        /// DEPOSIT <paramref name="index"/>, MOVED TO SEAT <paramref name="seat"/> (S3).
        ///
        /// Everything that makes this deposit THIS deposit is carried across unchanged
        /// - its registration key, its metal type, its quality and its 1255 variant -
        /// and only the position comes from the seat. That is deliberate and it is what
        /// the wiki describes: the understorm changed where resources WERE, not what
        /// they were. It also keeps <see cref="MetalTypeFor"/>'s "index 0 is always
        /// iron" invariant true no matter how the field is shuffled, and keeps a
        /// variant id the client can resolve (an unresolvable one leaves the visualiser
        /// disabled and the entity invisible).
        ///
        /// The seat indexes the pool generated by the SAME placement policy as the boot
        /// layout, so the returned node stands on ground the boot layout would have
        /// accepted, at least <see cref="Resources.HavenSurface.DepositMinSpacing"/>
        /// from every other seat.
        /// </summary>
        public static MetalNode NodeAtSeat(int index, int seat)
        {
            Placement p = HavenPlacements[index];
            Resources.GeneratedPlacement s = Resources.HavenSurface.DepositPool()[seat];
            return new MetalNode(
                KeyFor(index),
                p.MetalType,
                p.Quality,
                IslandCatalog.Haven.LocalToGlobal(s.LocalX, s.LocalY, s.LocalZ),
                isDeposit: true,
                variantId: VariantIdFor(index));
        }

        /// <summary>
        /// The Haven placement index behind a registration key ("deposit-7" =&gt; 7), or
        /// null if the key is not one of Haven's own static deposits. Release-world and
        /// Trades-Challenge deposits deliberately return null: they are placed from
        /// their own catalogues, have no seat pool, and so are not re-rolled.
        /// </summary>
        public static int? HavenIndexOf(string key)
        {
            if (string.IsNullOrEmpty(key) || !IsDepositKey(key)) return null;
            if (Resources.ReleaseWorldResources.DepositByKey(key) != null) return null;
            if (Resources.TradesChallengeResources.DepositByKey(key) != null) return null;
            if (!int.TryParse(key.Substring(KeyPrefix.Length), out int index)) return null;
            if (index < 0 || index >= HavenPlacements.Count) return null;
            return index;
        }

        /// <summary>
        /// WHERE THE DEPOSIT REGISTERED AS <paramref name="key"/> STANDS AFTER
        /// <paramref name="island"/>'s storm number <paramref name="generation"/> - the
        /// WHOLE understorm re-roll decision for one deposit, in one call. Null means
        /// "this deposit does not move", and the caller then leaves it alone.
        ///
        /// ⚠ THIS METHOD EXISTS BECAUSE A MUTATION ESCAPED, and it is the second time
        /// this exact hole has been found in the storm work - see
        /// <c>Islands.IslandResourceScope</c> for S2's version of the same lesson.
        ///
        /// S3's re-roll was first written as a loop in the game server that asked
        /// <c>IslandResourceReroll.SeatsFor</c> for the seats and then indexed it:
        ///
        /// <code>
        /// Nodes.Reseat(entityId, MetalDeposits.NodeAtSeat(index.Value, seats[index.Value]));
        /// </code>
        ///
        /// Changing that one subscript to <c>NodeAtSeat(index.Value, index.Value)</c>
        /// re-seats every deposit onto its OWN seat, so <c>Reseat</c> reports no change,
        /// nothing is broadcast, not one rock moves and no log line is even printed -
        /// and <b>all 4252 tests passed</b>. The game-server assembly has no test
        /// project (it needs a Windows game install to compile against), so the loop was
        /// guarded only by source-reading assertions that the strings
        /// <c>SeatsFor(</c> and <c>NodeAtSeat(</c> appeared somewhere in the file. Both
        /// still appeared. String matching cannot see that a value was computed and
        /// then thrown away.
        ///
        /// So the arithmetic moved HERE, where it is unit-tested, and the game server
        /// no longer has an index to get wrong: it asks for a node and re-seats it.
        /// Keep it that way - if a future change hands the caller a seat number again,
        /// it hands back the same silent failure.
        /// </summary>
        public static MetalNode? RerolledNode(
            Islands.IslandId island, long generation, string key)
        {
            if (generation <= 0) return null;
            if (island.Value != Islands.IslandCatalog.HavenId.Value) return null;

            int? index = HavenIndexOf(key);
            if (index == null) return null;

            int seatCount = RerollSeatCount;
            int occupied = HavenPlacements.Count;
            if (seatCount <= occupied) return null;

            IReadOnlyList<int> seats = Islands.IslandResourceReroll.SeatsFor(
                island, (uint)generation, seatCount, occupied,
                Islands.IslandResourceReroll.PinnedSeats);

            if (index.Value >= seats.Count) return null;

            int seat = seats[index.Value];
            if (seat == index.Value) return null;

            return NodeAtSeat(index.Value, seat);
        }

        /// <summary>
        /// The deposits to place on Haven. <paramref name="count"/> is clamped to
        /// [0, the full table]; index 0 (the proven deposit) is kept for any count
        /// &gt;= 1. One is the cautious first-live default - the coordinate and
        /// variant chains have never been in front of a running client.
        /// </summary>
        public static IReadOnlyList<MetalNode> Haven(int count)
        {
            if (count > HavenPlacements.Count)
            {
                count = HavenPlacements.Count;
            }
            List<MetalNode> nodes = new List<MetalNode>();
            for (int i = 0; i < count; i++)
            {
                nodes.Add(NodeAt(i));
            }
            return nodes;
        }
    }
}
