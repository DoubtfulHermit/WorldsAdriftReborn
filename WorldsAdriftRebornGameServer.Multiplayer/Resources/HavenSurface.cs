using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// The Haven-specific glue between the extracted surface data and the pure
    /// <see cref="SurfacePlacementGenerator"/>: it loads the embedded LOD0 surface
    /// samples for island 1431299145, holds the reviewed placement CONFIG for that
    /// island, and produces deterministic tree and deposit layouts over the WHOLE
    /// landmass.
    ///
    /// This is the ONLY place that touches an embedded file. The generator and its
    /// config stay pure (no I/O) so they unit-test natively; this class is the thin
    /// loader the standing "pure module + thin glue" rule calls for.
    ///
    /// Generalising to a second resource (fuel deposits, trees) or a second island
    /// is a small addition here: load that island's samples, author its config, call
    /// <see cref="SurfacePlacementGenerator.Generate"/>. The generator does not change.
    /// </summary>
    public static class HavenSurface
    {
        /// <summary>The extracted surface table this Haven layout is derived from.</summary>
        public const string SurfaceResourceName = "haven-surface-1431299145.txt";

        /// <summary>
        /// Haven's Steam Workshop id. Haven is NOT a release-catalogue island, so
        /// this is the only handle anything outside this class has on "which island
        /// these samples describe" - <c>TreeGroundProfiles</c> needs it to tell
        /// Haven's live surface apart from an island that has only baked rows.
        /// </summary>
        public const string WorkshopId = "1431299145";

        // ------------------------------------------------------------------
        // THE TUNABLE KNOBS for Haven's metal-deposit field, in one reviewed place.
        // Density is deliberately GENEROUS - the world should feel resource-rich -
        // and every number here is documented on SurfacePlacementConfig.
        // ------------------------------------------------------------------

        /// <summary>Flatness gate: dot(up, normal) &gt;= this. 0.92 = a stable, near-flat rock seat.</summary>
        public const double DepositMinUpwardNormal = 0.92;

        /// <summary>
        /// Lower safety bound, deliberately wider than Haven's complete extracted
        /// surface (-52.37..96 m). Retail did not impose an altitude band when
        /// surface-sampling resources; the old 1.5..12 m filter is what emptied the
        /// ridges and western half of the island.
        /// </summary>
        public const double ResourceMinHeight = -100.0;

        /// <summary>
        /// Upper safety bound, deliberately wider than Haven's complete extracted
        /// surface. High samples are island terrain, not a reason to discard an
        /// entire biome region.
        /// </summary>
        public const double ResourceMaxHeight = 150.0;

        /// <summary>
        /// Min 3-D distance between deposits, metres - the primary DENSITY knob.
        /// 22 m keeps the large deposit meshes from forming a rock carpet while a
        /// forty-node quota still covers the island's full ~560 x 290 m extent.
        /// </summary>
        public const double DepositMinSpacing = 22.0;

        /// <summary>
        /// Forty deposits: retail's recovered server default for the resource
        /// request, now placed offline because the shipped player client does not
        /// contain the UnityWorker sampler that answered that request.
        /// </summary>
        public const int DepositTargetCount = 40;

        /// <summary>Trees need flatter ground than deposits so trunks stand naturally.</summary>
        public const double TreeMinUpwardNormal = 0.94;

        /// <summary>Minimum distance between tree trunks, metres.</summary>
        public const double TreeMinSpacing = 15.0;

        /// <summary>
        /// A resource-rich but bounded Haven canopy. Eighty trees across the full
        /// surface is enough that no large walkable region is barren without making
        /// the loading barrier or tree simulation pathological.
        /// </summary>
        public const int TreeTargetCount = 80;

        /// <summary>Fuel canisters use stable, reasonably flat seats across Haven.</summary>
        public const double FuelMinUpwardNormal = 0.92;

        /// <summary>Canisters stay sparse enough to remain a find rather than a carpet.</summary>
        public const double FuelMinSpacing = 35.0;

        /// <summary>
        /// Twenty-four canisters across Haven: enough for the combustion loop without
        /// loading the whole island, and small beside the 80 trees / 40 deposits.
        /// </summary>
        public const int FuelTargetCount = 24;

        /// <summary>
        /// Flatness gate for loot containers, and the strictest on the island.
        ///
        /// A rock hides a bad seat; a box-shaped prop with hard edges does not. 0.97
        /// is within 14 degrees of level, which is also the condition that lets
        /// <see cref="LootContainers.SinkMetres"/> be applied straight down instead
        /// of along the full surface normal - see the grounding remarks on
        /// <see cref="LootContainers"/>. Loosening this without carrying the whole
        /// normal through <see cref="GeneratedPlacement"/> would put chest corners
        /// through the terrain.
        /// </summary>
        public const double LootMinUpwardNormal = 0.97;

        /// <summary>
        /// Minimum distance between loot containers, metres. RECOVERED: retail's own
        /// lootable placement pass rejected any candidate within
        /// <c>sqrMagnitude &lt; 400f</c> of an accepted one
        /// (<c>acs/IslandDataBankAndLootableSpawnerVisualizer.cs:64</c>), i.e. 20 m.
        /// This is the one placement constant in the loot pipeline that is not a
        /// guess.
        /// </summary>
        public const double LootMinSpacing = 20.0;

        /// <summary>
        /// WAREBORN TUNING. Haven's container count, hand-set rather than taken from
        /// <see cref="Loot.LootBudget"/> like every release island's, for the same
        /// reason Haven's tree and canister counts are: Haven is the tutorial island
        /// and is tuned by hand, not surveyed. The area formula would award it 2
        /// (90 LOD0 cells), which is too few for the first island a player ever
        /// searches.
        /// </summary>
        public const int LootTargetCount = 10;

        /// <summary>Keep-out radius around the player spawn, metres.</summary>
        public const double SpawnClearance = 6.0;

        /// <summary>Keep-out radius around the ship / shipyard footprint, metres.</summary>
        public const double ShipClearance = 8.0;

        /// <summary>
        /// Keep-out radius around each distributed tree, metres. Modest: a rock and a
        /// tree a few metres apart is fine, they just must not visually merge or
        /// interpenetrate.
        /// </summary>
        public const double TreeClearance = 5.0;

        /// <summary>
        /// The hand-validated "proven" deposit seat: island-local (216, 4.57, 8), the
        /// one coordinate on this island checked against a running client (8.9 m from
        /// spawn, ny = 0.995). Prepended to the field as index 0 and used as an
        /// anchor so nothing generated crowds it.
        /// </summary>
        public static readonly GeneratedPlacement ProvenDepositLocal =
            new GeneratedPlacement(216.0, 4.57, 8.0, 0.995);

        private static IReadOnlyList<SurfaceSample>? _samples;
        private static IReadOnlyList<GeneratedPlacement>? _depositLocals;
        private static IReadOnlyList<GeneratedPlacement>? _treeLocals;
        private static IReadOnlyList<GeneratedPlacement>? _fuelLocals;
        private static IReadOnlyList<GeneratedPlacement>? _lootLocals;

        /// <summary>
        /// The five legacy canister seats, retained first so existing fuel-pod-N
        /// identities and positions never move. Generated whole-island seats append.
        /// </summary>
        public static readonly IReadOnlyList<GeneratedPlacement> LegacyFuelLocals = new[]
        {
            new GeneratedPlacement(192.0, 7.13,   8.0, 0.99),
            // REMOVED 2026-08-18: 33.0 m from the Revival Chamber's axis, i.e. on
            // the ground the user asked to have cleared ("empty the tree etc from
            // it then place the tower here properly"). This table is hand-written
            // and bypasses the generator's exclusions, so it has to be deleted
            // rather than excluded. Was:
            //   new GeneratedPlacement(152.0, 4.71, 0.0, 0.99),
            new GeneratedPlacement(176.0, 6.39, -16.0, 0.99),
            // REMOVED 2026-08-19: 34.4 m from the Revival Chamber's axis once the
            // tower was stood up and moved to (156, 20), i.e. inside the 35 m of
            // ground the building clears. Same reason as the 2026-08-18 deletion
            // below it: this table is hand-written and bypasses the generator's
            // exclusions, so an entry that lands on cleared ground has to be
            // deleted rather than excluded. The generator fills the freed slot
            // elsewhere on the island, so the canister count does not change. Was:
            //   new GeneratedPlacement(128.0, 6.12,   0.0, 0.99),
            new GeneratedPlacement(184.0, 3.10, -32.0, 0.99),
        };

        /// <summary>
        /// The extracted LOD0 surface samples for Haven, loaded once from the
        /// embedded table. ~2,139 candidate points, island-local metres + normal.
        /// </summary>
        public static IReadOnlyList<SurfaceSample> Samples
        {
            get
            {
                if (_samples == null)
                {
                    _samples = LoadSamples();
                }
                return _samples;
            }
        }

        /// <summary>The reviewed metal-deposit placement config for Haven.</summary>
        public static SurfacePlacementConfig DepositConfig()
        {
            return new SurfacePlacementConfig(
                minUpwardNormal: DepositMinUpwardNormal,
                minReachableHeightMetres: ResourceMinHeight,
                maxReachableHeightMetres: ResourceMaxHeight,
                minSpacingMetres: DepositMinSpacing,
                targetCount: DepositTargetCount,
                exclusions: DepositExclusions());
        }

        /// <summary>The reviewed whole-island tree placement config for Haven.</summary>
        public static SurfacePlacementConfig TreeConfig()
        {
            return new SurfacePlacementConfig(
                minUpwardNormal: TreeMinUpwardNormal,
                minReachableHeightMetres: ResourceMinHeight,
                maxReachableHeightMetres: ResourceMaxHeight,
                minSpacingMetres: TreeMinSpacing,
                targetCount: TreeTargetCount,
                exclusions: TreeExclusions());
        }

        /// <summary>The reviewed whole-island fuel-canister config for Haven.</summary>
        public static SurfacePlacementConfig FuelConfig()
        {
            return new SurfacePlacementConfig(
                minUpwardNormal: FuelMinUpwardNormal,
                minReachableHeightMetres: ResourceMinHeight,
                maxReachableHeightMetres: ResourceMaxHeight,
                minSpacingMetres: FuelMinSpacing,
                targetCount: FuelTargetCount,
                exclusions: FuelExclusions());
        }

        /// <summary>
        /// The Revival Chamber's keep-out disc. Nothing this server scatters may be
        /// generated inside the building, and this is where that is enforced: at
        /// GENERATION, so the placement field never contains the point at all.
        ///
        /// It used to be a skip at registration time, which worked but was a lie in
        /// the boot count - Haven reported 1,526 resource entities and delivered
        /// 1,521, with the five missing ones silently dropped. The user asked for the
        /// trees on that shelf to be CLEARED, not hidden, so they are cleared here.
        ///
        /// Radius from <see cref="Wilderness.WildernessChamber.ClearingRadiusMetres"/>,
        /// so the disc can never drift from the building it protects.
        /// </summary>
        private static PlacementExclusion ChamberExclusion(double radiusMetres)
        {
            return new PlacementExclusion(
                Wilderness.WildernessChamber.HavenLocalPlacement.X,
                Wilderness.WildernessChamber.HavenLocalPlacement.Z,
                radiusMetres);
        }

        /// <summary>
        /// The CLEARED APRON, for props a player sees: trees and fuel canisters. A
        /// tree growing out of an ancient tower is what the user was looking at when
        /// they asked for this.
        /// </summary>
        private static PlacementExclusion ChamberClearing() =>
            ChamberExclusion(Wilderness.WildernessChamber.ClearingRadiusMetres);

        /// <summary>
        /// The BUILDING ITSELF, for things a player needs: deposits stay out of the
        /// walls but keep their ground right up to them. Clearing ore to 35 m would
        /// cost the starting island a third of its metal to fix a look.
        /// </summary>
        private static PlacementExclusion ChamberFootprint() =>
            ChamberExclusion(Wilderness.WildernessChamber.ExclusionRadiusMetres);

        private static IReadOnlyList<PlacementExclusion> FuelExclusions()
        {
            List<PlacementExclusion> ex = new List<PlacementExclusion>();
            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;
            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;
            ex.Add(new PlacementExclusion(
                spawn.MetresX - island.MetresX,
                spawn.MetresZ - island.MetresZ,
                SpawnClearance));

            FixedPointPosition ship = WorldEntities.ShipFrameDefaultPosition;
            ex.Add(new PlacementExclusion(
                ship.MetresX - island.MetresX,
                ship.MetresZ - island.MetresZ,
                ShipClearance));

            // Keep everything out of the Revival Chamber - see ChamberExclusion.
            ex.Add(ChamberClearing());
            return ex;
        }

        /// <summary>
        /// The lateral keep-out discs for the deposit field: the player spawn, the
        /// ship footprint and every distributed tree. Derived from the SAME
        /// fixed-point positions those entities are actually placed at (converted to
        /// island-local metres), so the exclusions can never drift from the things
        /// they protect.
        /// </summary>
        public static IReadOnlyList<PlacementExclusion> DepositExclusions()
        {
            List<PlacementExclusion> ex = new List<PlacementExclusion>();

            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;

            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;
            ex.Add(new PlacementExclusion(
                spawn.MetresX - island.MetresX,
                spawn.MetresZ - island.MetresZ,
                SpawnClearance));

            FixedPointPosition ship = WorldEntities.ShipFrameDefaultPosition;
            ex.Add(new PlacementExclusion(
                ship.MetresX - island.MetresX,
                ship.MetresZ - island.MetresZ,
                ShipClearance));

            foreach (GeneratedPlacement tree in TreeLocals())
            {
                ex.Add(new PlacementExclusion(tree.LocalX, tree.LocalZ, TreeClearance));
            }


            // Keep everything out of the Revival Chamber - see ChamberExclusion.
            ex.Add(ChamberFootprint());
            return ex;
        }

        /// <summary>
        /// Tree keep-outs: the spawn/camp, ship footprint, and the proven deposit.
        /// The spawn disc also protects the separately registered near-spawn birch.
        /// </summary>
        public static IReadOnlyList<PlacementExclusion> TreeExclusions()
        {
            List<PlacementExclusion> ex = new List<PlacementExclusion>();
            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;

            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;
            ex.Add(new PlacementExclusion(
                spawn.MetresX - island.MetresX,
                spawn.MetresZ - island.MetresZ,
                SpawnClearance + 6.0));

            FixedPointPosition ship = WorldEntities.ShipFrameDefaultPosition;
            ex.Add(new PlacementExclusion(
                ship.MetresX - island.MetresX,
                ship.MetresZ - island.MetresZ,
                ShipClearance));

            ex.Add(new PlacementExclusion(
                ProvenDepositLocal.LocalX,
                ProvenDepositLocal.LocalZ,
                TreeClearance));

            // Keep everything out of the Revival Chamber - see ChamberExclusion.
            ex.Add(ChamberClearing());
            return ex;
        }

        /// <summary>
        /// Loot-container keep-outs. Deliberately the WIDEST set on the island: a
        /// chest is a thing a player walks up to and stands at, so it must not be
        /// wedged against a tree trunk, inside a rock, on the ship, on the spawn pad
        /// or in the Revival Chamber. Everything already standing is excluded,
        /// because a container is placed last and has no claim on ground another
        /// prop already holds.
        /// </summary>
        public static IReadOnlyList<PlacementExclusion> LootExclusions()
        {
            List<PlacementExclusion> ex = new List<PlacementExclusion>();
            FixedPointPosition island = IslandCatalog.Haven.GlobalOrigin;

            FixedPointPosition spawn = SpawnPolicy.PlayerSpawnPosition;
            ex.Add(new PlacementExclusion(
                spawn.MetresX - island.MetresX,
                spawn.MetresZ - island.MetresZ,
                SpawnClearance));

            FixedPointPosition ship = WorldEntities.ShipFrameDefaultPosition;
            ex.Add(new PlacementExclusion(
                ship.MetresX - island.MetresX,
                ship.MetresZ - island.MetresZ,
                ShipClearance));

            // Every deposit and every tree already standing. DepositExclusions does
            // the same for trees one field over, and for the same reason: the
            // generator has no collision test at all, only these discs.
            foreach (GeneratedPlacement tree in TreeLocals())
            {
                ex.Add(new PlacementExclusion(tree.LocalX, tree.LocalZ, TreeClearance));
            }
            foreach (GeneratedPlacement deposit in DepositLocals())
            {
                ex.Add(new PlacementExclusion(deposit.LocalX, deposit.LocalZ, TreeClearance));
            }

            ex.Add(ChamberClearing());
            return ex;
        }

        /// <summary>The reviewed whole-island loot-container config for Haven.</summary>
        public static SurfacePlacementConfig LootConfig()
        {
            return new SurfacePlacementConfig(
                minUpwardNormal: LootMinUpwardNormal,
                minReachableHeightMetres: ResourceMinHeight,
                maxReachableHeightMetres: ResourceMaxHeight,
                minSpacingMetres: LootMinSpacing,
                targetCount: LootTargetCount,
                exclusions: LootExclusions());
        }

        /// <summary>
        /// Deterministic loot-container seats across Haven's complete surface, as RAW
        /// surface vertices. The <see cref="LootContainers.SinkMetres"/> sink is NOT
        /// applied here - it is applied once, at the
        /// <c>LootContainers.HavenPlacements</c> boundary, so there is exactly one
        /// place in the codebase where a container's height is adjusted and the
        /// release-world seats go through the same arithmetic.
        /// </summary>
        public static IReadOnlyList<GeneratedPlacement> LootLocals()
        {
            if (_lootLocals == null)
            {
                _lootLocals = SurfacePlacementGenerator.Generate(Samples, LootConfig());
            }
            return _lootLocals;
        }

        /// <summary>
        /// Deterministic tree seats across Haven's complete surface. Species are a
        /// separate biome-profile decision; this method only answers WHERE.
        /// </summary>
        public static IReadOnlyList<GeneratedPlacement> TreeLocals()
        {
            if (_treeLocals == null)
            {
                _treeLocals = SurfacePlacementGenerator.Generate(Samples, TreeConfig());
            }
            return _treeLocals;
        }

        /// <summary>
        /// The deterministic Haven metal-deposit layout in island-local metres:
        /// the proven deposit first, then the generated field around it. Computed
        /// once and cached; the same across every restart (pure generator over a
        /// fixed embedded surface and a fixed config).
        /// </summary>
        public static IReadOnlyList<GeneratedPlacement> DepositLocals()
        {
            if (_depositLocals == null)
            {
                GeneratedPlacement[] anchors = { ProvenDepositLocal };
                IReadOnlyList<GeneratedPlacement> generated =
                    SurfacePlacementGenerator.Generate(Samples, DepositConfig(), anchors);

                List<GeneratedPlacement> all = new List<GeneratedPlacement>(1 + generated.Count) { ProvenDepositLocal };
                all.AddRange(generated);
                _depositLocals = all;
            }
            return _depositLocals;
        }

        /// <summary>
        /// Stable fuel seats over the complete island. The legacy five remain at
        /// indices 0..4; generated seats fill the remainder of the target count.
        /// </summary>
        public static IReadOnlyList<GeneratedPlacement> FuelLocals()
        {
            if (_fuelLocals == null)
            {
                IReadOnlyList<GeneratedPlacement> generated =
                    SurfacePlacementGenerator.Generate(Samples, FuelConfig(), LegacyFuelLocals);
                List<GeneratedPlacement> all = new List<GeneratedPlacement>(FuelTargetCount);
                all.AddRange(LegacyFuelLocals);
                all.AddRange(generated);
                _fuelLocals = all;
            }
            return _fuelLocals;
        }

        // ------------------------------------------------------------------

        private static IReadOnlyList<SurfaceSample> LoadSamples()
        {
            Assembly asm = typeof(HavenSurface).Assembly;
            string? resource = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith(SurfaceResourceName, System.StringComparison.Ordinal))
                {
                    resource = name;
                    break;
                }
            }
            if (resource == null)
            {
                throw new FileNotFoundException(
                    "embedded Haven surface table '" + SurfaceResourceName + "' not found in "
                    + asm.GetName().Name);
            }

            List<SurfaceSample> samples = new List<SurfaceSample>();
            using Stream stream = asm.GetManifestResourceStream(resource)!;
            using StreamReader reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                string[] f = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length != 6)
                {
                    continue;
                }
                samples.Add(new SurfaceSample(
                    ParseD(f[0]), ParseD(f[1]), ParseD(f[2]),
                    ParseD(f[3]), ParseD(f[4]), ParseD(f[5])));
            }
            return samples;
        }

        private static double ParseD(string s) =>
            double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
