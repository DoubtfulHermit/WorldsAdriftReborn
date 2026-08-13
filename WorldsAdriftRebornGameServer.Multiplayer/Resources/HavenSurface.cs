using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

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

            FixedPointPosition island = SpawnPolicy.IslandPosition;

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

            return ex;
        }

        /// <summary>
        /// Tree keep-outs: the spawn/camp, ship footprint, and the proven deposit.
        /// The spawn disc also protects the separately registered near-spawn birch.
        /// </summary>
        public static IReadOnlyList<PlacementExclusion> TreeExclusions()
        {
            List<PlacementExclusion> ex = new List<PlacementExclusion>();
            FixedPointPosition island = SpawnPolicy.IslandPosition;

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
            return ex;
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
