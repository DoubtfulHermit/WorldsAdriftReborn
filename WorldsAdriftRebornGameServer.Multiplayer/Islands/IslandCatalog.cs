namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>The evidenced island definitions shipped by this server.</summary>
    public static class IslandCatalog
    {
        public const string HavenTerrainAssetName = "1431299145@Island";
        public const string DefaultTerrainAssetContext = "notNeeded?";
        public static readonly IslandId HavenId = new IslandId("haven");
        public static readonly IslandId TradesChallengeId = new IslandId("the-trades-challenge");
        public static readonly IslandId AnchorageIsleId = new IslandId("anchorage-isle");
        public static readonly IslandId OldMilitaryAcademyId =
            new IslandId("the-old-military-academy");
        public static readonly IslandId ShatteredMausoleumId =
            new IslandId("shattered-mausoleum");
        public static readonly IslandId MentalFacilityId = new IslandId("mental-facility");
        public static readonly IslandId BetrayalCopperKingId =
            new IslandId("betrayal-of-the-copper-king");
        public static readonly IslandId HighlandsHillsId = new IslandId("highlands-hills");
        public static readonly IslandId LandManForgotId =
            new IslandId("the-land-that-man-forgot");
        public static readonly IslandId DrunkRavenInnId = new IslandId("drunkraven-inn");
        public static readonly IslandId BeautifulWildlandsId =
            new IslandId("beautiful-wildlands");
        public static readonly IslandId TheThreeId = new IslandId("the-three");
        public static readonly IslandId RoxboroughIsleId = new IslandId("roxborough-isle");
        public static readonly IslandId CampsDauratsId = new IslandId("camps-daurats");
        public static readonly IslandId TriphalionCityId = new IslandId("triphalion-city");
        public static readonly IslandId SplitpeakPassId = new IslandId("splitpeak-pass");
        public static readonly IslandId CrimsonParadiseId = new IslandId("crimson-paradise");

        /// <summary>Haven instance #5 from the preserved Bossa WAMap.</summary>
        public static readonly IslandDefinition Haven = new IslandDefinition(
            HavenId,
            "Haven",
            EntityIdAllocator.IslandKey,
            new FixedPointPosition(69650145, -1305269, -4645549),
            HavenTerrainAssetName,
            DefaultTerrainAssetContext,
            SpawnOrder.BeforePlayer);

        /// <summary>
        /// The closest distinct release-world island to the active Haven instance.
        /// Position and asset are from the preserved Bossa MapFile; the matching
        /// client bundle and extracted collision surface are both present locally.
        /// </summary>
        public static readonly IslandDefinition TradesChallenge = new IslandDefinition(
            TradesChallengeId,
            "The Trades Challenge",
            "island-the-trades-challenge",
            new FixedPointPosition(54286560, -791844, -8077469),
            "1206286558@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The next release-world C6 terrain candidate after The Trades Challenge.</summary>
        public static readonly IslandDefinition AnchorageIsle = new IslandDefinition(
            AnchorageIsleId,
            "Anchorage Isle",
            "island-anchorage-isle",
            new FixedPointPosition(53748326, -1229240, 1475919),
            "650186469@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The release-world C6 island north of Anchorage Isle.</summary>
        public static readonly IslandDefinition OldMilitaryAcademy = new IslandDefinition(
            OldMilitaryAcademyId,
            "The Old Military Academy",
            "island-the-old-military-academy",
            new FixedPointPosition(58796532, 277533, 7307445),
            "1673355094@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>The southern release-world C6 terrain candidate.</summary>
        public static readonly IslandDefinition ShatteredMausoleum = new IslandDefinition(
            ShatteredMausoleumId,
            "Shattered Mausoleum",
            "island-shattered-mausoleum",
            new FixedPointPosition(58660618, -2158603, -19035735),
            "949069116@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>
        /// Four surveyed Saborian tier-1 islands in release district B3. Their
        /// positions/assets are the preserved release MapFile; their tier, culture,
        /// revival-chamber and databank facts come from the final Cardinal survey.
        /// Rollout order is deliberately smallest client bundle first.
        /// </summary>
        public static readonly IslandDefinition MentalFacility = new IslandDefinition(
            MentalFacilityId,
            "Mental Facility",
            "island-mental-facility",
            new FixedPointPosition(34121298, 990124, 34175648),
            "1143725558@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition BetrayalCopperKing = new IslandDefinition(
            BetrayalCopperKingId,
            "Betrayal of the Copper King",
            "island-betrayal-of-the-copper-king",
            new FixedPointPosition(31506652, 580855, 40190030),
            "950242829@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition HighlandsHills = new IslandDefinition(
            HighlandsHillsId,
            "Highlands Hills",
            "island-highlands-hills",
            new FixedPointPosition(38919041, 516457, 38365766),
            "1206946500@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition LandManForgot = new IslandDefinition(
            LandManForgotId,
            "The Land that Man Forgot",
            "island-the-land-that-man-forgot",
            new FixedPointPosition(40357265, 37785, 29935290),
            "942473835@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition DrunkRavenInn = new IslandDefinition(
            DrunkRavenInnId,
            "DrunkRaven Inn",
            "island-drunkraven-inn",
            new FixedPointPosition(33756291, 1415496, 18067083),
            "924807150@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition BeautifulWildlands = new IslandDefinition(
            BeautifulWildlandsId,
            "Beautiful Wildlands",
            "island-beautiful-wildlands",
            new FixedPointPosition(30901057, 39425, 22887514),
            "742077672@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition TheThree = new IslandDefinition(
            TheThreeId,
            "The Three",
            "island-the-three",
            new FixedPointPosition(20839827, -667437, 22841061),
            "1129983108@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition RoxboroughIsle = new IslandDefinition(
            RoxboroughIsleId,
            "Roxborough Isle",
            "island-roxborough-isle",
            new FixedPointPosition(29404985, 445557, 28800561),
            "1483206813@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition CampsDaurats = new IslandDefinition(
            CampsDauratsId,
            "Camps Daurats",
            "island-camps-daurats",
            new FixedPointPosition(24433521, -948307, 38831677),
            "1319380815@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition TriphalionCity = new IslandDefinition(
            TriphalionCityId,
            "Triphalion City",
            "island-triphalion-city",
            new FixedPointPosition(22536482, 205446, 30035513),
            "1675054039@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition SplitpeakPass = new IslandDefinition(
            SplitpeakPassId,
            "Splitpeak Pass",
            "island-splitpeak-pass",
            new FixedPointPosition(25630871, 388610, 16943796),
            "966489234@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        public static readonly IslandDefinition CrimsonParadise = new IslandDefinition(
            CrimsonParadiseId,
            "Crimson Paradise",
            "island-crimson-paradise",
            new FixedPointPosition(39857582, -721753, 22106451),
            "938282702@Island",
            DefaultTerrainAssetContext,
            SpawnOrder.AfterPlayer);

        /// <summary>
        /// The evidenced first progression-region terrain in rollout order. Haven is
        /// the tutorial anchor; the remaining twelve entries are optional tier-1 B3
        /// <see cref="SpawnOrder.AfterPlayer"/> candidates selected as a prefix.
        /// The geographically closer C6 islands above are tier 3 and deliberately do
        /// not participate in this list. The first four slots preserve the already
        /// staged rollout contract; the remaining eight are ordered by increasing
        /// client bundle size so expansion can continue one island at a time.
        /// </summary>
        public static readonly IReadOnlyList<IslandDefinition> FirstRegionTerrain =
            Array.AsReadOnly(new[]
            {
                Haven,
                MentalFacility,
                BetrayalCopperKing,
                HighlandsHills,
                LandManForgot,
                DrunkRavenInn,
                BeautifulWildlands,
                TheThree,
                RoxboroughIsle,
                CampsDaurats,
                TriphalionCity,
                SplitpeakPass,
                CrimsonParadise,
            });

        /// <summary>
        /// Every island written out by hand in this file, rollout list or not.
        /// It exists so a lookup that must recognise ANY named island - "which
        /// island is this stored position on", say - cannot silently miss the four
        /// C6 definitions that <see cref="FirstRegionTerrain"/> deliberately
        /// excludes. Rollout order is <see cref="FirstRegionTerrain"/>'s job; this
        /// list makes no claim about order or about what is registered this boot.
        /// </summary>
        public static readonly IReadOnlyList<IslandDefinition> AllNamed =
            Array.AsReadOnly(new[]
            {
                Haven,
                TradesChallenge,
                AnchorageIsle,
                OldMilitaryAcademy,
                ShatteredMausoleum,
                MentalFacility,
                BetrayalCopperKing,
                HighlandsHills,
                LandManForgot,
                DrunkRavenInn,
                BeautifulWildlands,
                TheThree,
                RoxboroughIsle,
                CampsDaurats,
                TriphalionCity,
                SplitpeakPass,
                CrimsonParadise,
            });
    }
}
