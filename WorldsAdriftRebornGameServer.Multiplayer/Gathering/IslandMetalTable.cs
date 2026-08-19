using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// WHICH METAL A DEPOSIT ON A GIVEN ISLAND IS MADE OF.
    ///
    /// The maintainer's question was whether a deposit is a stone that yields
    /// different metals rather than a fixed "iron deposit", and the shipped schema
    /// answers it: retail's rock carried the metal as DATA
    /// (MetalRockStateData{ ..., string metalTypeId, int quality, ... }), while the
    /// deposit's own component carries only a `variantId` naming which rock ART to
    /// draw. So the rock is generic and the contents are per-node - and per-island,
    /// because the node's metal was drawn from the island's own table.
    ///
    /// WE ALREADY HAD MOST OF THIS. The 254-island metal table is imported,
    /// provenance-labelled and enforced (IslandSurveyProfile refuses to let an
    /// inferred table claim to be a survey), and ReleaseWorldCatalog already stamps
    /// every one of its 1930 deposits with a metal and a quality drawn from it.
    /// Two places did not: Haven, and the client-handshake spawner - both of which
    /// hardcoded "iron". Since the live server runs Haven only, that meant every
    /// deposit a player could actually reach was iron.
    ///
    /// This is the seam that closes the gap. It is deliberately a lookup, not a
    /// generator: where an island's real table survives it is used verbatim, and
    /// the ONE island that has to be invented is invented in one place, labelled,
    /// and derived from the cohort rather than from taste.
    ///
    /// DETERMINISTIC. No RNG and no clock: the same island and node index always
    /// give the same metal, so a restart reproduces the world and any state keyed
    /// on a deposit's index stays consistent. That is the same property
    /// MetalDeposits.BuildHavenPlacements is careful about.
    ///
    /// Pure: island ids and catalogue rows in, a metal name and a quality out.
    /// </summary>
    public static class IslandMetalTable
    {
        /// <summary>
        /// The metal a deposit falls back to when nothing at all is known about its
        /// island. Iron, because it is the metal every starter recipe asks for and
        /// an unknown island paying an exotic metal would be a worse failure than
        /// one paying the common one.
        /// </summary>
        public const string FallbackMetal = "iron";

        /// <summary>
        /// HAVEN'S METAL SPREAD. **WAREBORN TUNING**, and the only invented table in
        /// this file.
        ///
        /// Haven has no survey row and never will: it is Bossa-authored, not a
        /// Workshop island, so it is absent from the 254-island community survey
        /// (IslandSurveyCatalog.ByIsland(HavenId) is deliberately null). Something
        /// has to be chosen, and the previous choice - iron for every node - was a
        /// defensible one made for a reason worth restating: cycling arbitrary
        /// metals would manufacture lore and make the starter material scarce.
        ///
        /// This keeps that reason and drops the uniformity. The weights are not
        /// taste: they are the TIER-1 COHORT, i.e. how often each metal appears
        /// across the 46 surveyed tier-1 islands - iron 36/46, bronze 20/46, lead
        /// 18/46, tin 9/46, epilar 8/46, copper 4/46 - rounded onto a twenty-slot
        /// ring. That is the same cohort, and the same method, the existing
        /// tools/world-import/metal_inference.py generator used to compose tables
        /// for the 193 islands the survey never recorded, so Haven is inferred the
        /// way every other unsurveyed island already is rather than by a new rule
        /// invented for it.
        ///
        /// Iron is 10 of 20 rather than the cohort's own 38%, and that rounding UP is
        /// the one place the cohort is overridden: this is the starter island, iron
        /// is what its first recipes consume, and a player who has to walk past six
        /// bronze rocks to find their first iron experiences that as scarcity rather
        /// than as variety. Iron is separately pinned to index 0 - the node nearest
        /// the spawn point - so the first rock anyone walks up to is always the metal
        /// they need.
        ///
        /// QUALITY IS DELIBERATELY UNCHANGED at Haven's existing declared 6. The
        /// surveyed tier-1 band is 1..4, so strict cohort fidelity would NERF
        /// Haven's metal the same day quality first started reaching the item, and
        /// bundling a balance cut into a bug fix is how a fix gets blamed for a
        /// regression. Dropping Haven to the tier-1 band is a one-line follow-up
        /// and a maintainer decision, not this file's to make.
        /// </summary>
        public static readonly IReadOnlyList<string> HavenRing = Array.AsReadOnly(new[]
        {
            "iron",   "iron",   "bronze", "lead",
            "iron",   "tin",    "iron",   "bronze",
            "iron",   "lead",   "iron",   "epilar",
            "iron",   "iron",   "lead",   "tin",
            "iron",   "bronze", "copper", "iron",
        });

        /// <summary>
        /// Haven's quality. The value Haven already declared before metals varied;
        /// see the note on <see cref="HavenRing"/> for why it is not the tier-1 band.
        /// </summary>
        public const int HavenQuality = 6;

        /// <summary>
        /// The metal and quality for the <paramref name="nodeIndex"/>-th deposit on
        /// an island, or null when the island is unknown to every catalogue.
        ///
        /// Null rather than a fallback so the CALLER decides what an unknown island
        /// means. A hand-placed test deposit and a client-handshake deposit want
        /// different answers, and silently handing both "iron" here is how the
        /// hardcode this file replaces got written in the first place.
        /// </summary>
        public static SurveyedMetal? DrawFor(IslandId islandId, int nodeIndex)
        {
            if (nodeIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodeIndex), nodeIndex,
                    "a deposit index is a position in an island's node list, never negative");
            }

            if (islandId.Value == IslandCatalog.HavenId.Value)
            {
                return new SurveyedMetal(HavenRing[nodeIndex % HavenRing.Count], HavenQuality);
            }

            IReadOnlyList<SurveyedMetal> metals = SurveyedMetalsFor(islandId);

            return metals.Count == 0 ? null : metals[nodeIndex % metals.Count];
        }

        /// <summary>
        /// An island's effective metal table as the release catalogue holds it -
        /// the survey where one survives, the tier-cohort inference where it does
        /// not, with <c>IslandSurveyProfile.MetalSource</c> recording which.
        /// Empty for an island no catalogue knows.
        /// </summary>
        public static IReadOnlyList<SurveyedMetal> SurveyedMetalsFor(IslandId islandId)
        {
            ReleaseIslandRecord? release = ReleaseWorldCatalog.ByIsland(islandId);

            if (release != null && release.Survey.Metals.Count > 0)
            {
                return release.Survey.Metals;
            }

            IslandSurveyProfile? profile = IslandSurveyCatalog.ByIsland(islandId);

            return profile != null ? profile.Metals : Array.Empty<SurveyedMetal>();
        }

        /// <summary>
        /// The metal name normalised to the spelling <c>itemData.json</c> uses.
        ///
        /// The catalogues carry display capitalisation ("Aluminium"), and the item
        /// database is keyed by the lowercase id ("aluminium"). An itemTypeId the
        /// client's item database has never heard of is a hard client-side NRE, so
        /// the two spellings are reconciled HERE, once, rather than at each of the
        /// several places that reads a table.
        /// </summary>
        public static string ItemTypeIdOf(SurveyedMetal metal)
        {
            if (metal == null)
            {
                throw new ArgumentNullException(nameof(metal));
            }

            return metal.Name.ToLowerInvariant();
        }
    }
}
