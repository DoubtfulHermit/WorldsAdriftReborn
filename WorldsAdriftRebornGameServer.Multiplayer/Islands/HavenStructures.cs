using System.Globalization;
using System.Reflection;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE AUTHORED STRUCTURES ALREADY STANDING ON HAVEN, so that anything this
    /// server puts on Haven can be checked against them BEFORE a player walks into
    /// it.
    ///
    /// WHY THIS EXISTS. The Wilderness shrine's first placement, island-local
    /// (176, 4.90, 16), was chosen against the surface table alone and its nearest
    /// authored structure was 13.7 m away. The prefab standing there was 40 m wide,
    /// so it was driven straight through the ruined metal camp: on 2026-08-18 a
    /// player logged in inside it and had to be rescued with the admin teleport.
    /// A distance to terrain is not a clearance; nothing but a check against the
    /// props themselves is.
    ///
    /// WHAT IS IN IT. The 253 <c>Ruins (Miscellaneous)</c> and <c>Ruins (Saborian)</c>
    /// placements - the metal camp's ~178 platforms, walkways, ladders, pipes and
    /// girders, plus the Saborian brick and ornamental pieces. Those are the only
    /// authored props on Haven a player can stand on, walk under or be wedged
    /// inside. Rocks, foliage, grass and VFX emitters are deliberately EXCLUDED:
    /// they are scenery, a monument may overlap one without trapping anybody, and
    /// including them makes every spot on the island fail.
    ///
    /// PROVENANCE: RECOVERED. Read from the embedded
    /// <c>Resources/haven-structure-props.txt</c>, projected from
    /// docs/research/world-data/haven/haven-props-resolved.json - the fully
    /// resolved 1,285-placement Haven prop list, itself derived from the
    /// 1,347-entry IslandProps/guidlut table (docs/research/findings-haven.md).
    /// Same embedded-resource shape as <see cref="Ship.ClientEntityPrefabs"/> and
    /// the Haven surface table, so this assembly stays dependency-free.
    /// </summary>
    public static class HavenStructures
    {
        /// <summary>One authored structure placement, island-local metres.</summary>
        public readonly struct Prop
        {
            public Prop(double x, double y, double z, string asset)
            {
                X = x; Y = y; Z = z; Asset = asset;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }

            /// <summary>Its IslandProps path, e.g. <c>Ruins (Miscellaneous)/Metal/Platform 06</c>.</summary>
            public string Asset { get; }
        }

        private static readonly IReadOnlyList<Prop> Props = Load();

        /// <summary>Every authored structure placement on Haven, island-local.</summary>
        public static IReadOnlyList<Prop> All => Props;

        private static IReadOnlyList<Prop> Load()
        {
            var list = new List<Prop>();
            Assembly asm = typeof(HavenStructures).Assembly;
            string? resource = null;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("haven-structure-props.txt", StringComparison.Ordinal))
                {
                    resource = name;
                    break;
                }
            }
            // An empty list would make every clearance check PASS, which is the
            // dangerous direction. HavenStructuresTests asserts the real count, so
            // a packaging mistake fails a test rather than shipping a shrine back
            // inside the camp.
            if (resource == null) return list;

            using Stream stream = asm.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;
                string[] parts = trimmed.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)) continue;
                list.Add(new Prop(x, y, z, parts.Length > 3 ? parts[3] : string.Empty));
            }
            return list;
        }

        /// <summary>
        /// Horizontal distance from an island-local point to the nearest authored
        /// structure, metres. <see cref="double.MaxValue"/> when the table is
        /// empty, which only happens if the embedded resource went missing.
        ///
        /// HORIZONTAL and not 3D on purpose: the camp's platforms are stacked up to
        /// 25 m over the same footprint, so a 3D distance would happily call a spot
        /// "clear" that has a walkway directly overhead.
        /// </summary>
        public static double ClearanceAt(double localX, double localZ)
        {
            double best = double.MaxValue;
            for (int i = 0; i < Props.Count; i++)
            {
                double dx = Props[i].X - localX;
                double dz = Props[i].Z - localZ;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// How many authored structures stand within <paramref name="radiusMetres"/>
        /// horizontally of a point and between <paramref name="belowMetres"/> under
        /// and <paramref name="aboveMetres"/> over its height - i.e. "is anything
        /// hanging over this spot".
        ///
        /// The ruined metal camp is a MULTI-STOREY structure; a placement can have
        /// a clean horizontal distance to the nearest column and still be under a
        /// platform 19 m up, which is where the Haven spawn point itself sits.
        /// </summary>
        public static int CountNear(
            double localX, double localY, double localZ,
            double radiusMetres, double belowMetres, double aboveMetres)
        {
            int n = 0;
            for (int i = 0; i < Props.Count; i++)
            {
                double dx = Props[i].X - localX;
                double dz = Props[i].Z - localZ;
                if (Math.Sqrt(dx * dx + dz * dz) > radiusMetres) continue;
                double dy = Props[i].Y - localY;
                if (dy < -belowMetres || dy > aboveMetres) continue;
                n++;
            }
            return n;
        }
    }
}
