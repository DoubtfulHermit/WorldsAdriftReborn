using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// The DETERMINISTIC OFFLINE placement generator: it takes an island's extracted
    /// surface samples and a <see cref="SurfacePlacementConfig"/> and returns the
    /// accepted resource placements, applying Worlds Adrift's own acceptance rules -
    /// upward-facing normal, clear reachable ground, and a min-spacing thinning WA's
    /// live sampler lacked. It is resource-TYPE-agnostic: metal deposits today, fuel
    /// deposits and trees later, are all "run this over the same surface with a
    /// different config".
    ///
    /// WHY DETERMINISTIC AND WHY NOT RANDOM-AT-CONNECT. The real WA client sampled
    /// the island live with <c>UnityEngine.Random</c> every spawn. This server has
    /// no Unity, must not run placement while players connect, and needs the SAME
    /// layout every restart so persistence and mining state stay consistent. So
    /// there is no RNG and no clock here: the candidate ORDER is a pure hash of each
    /// point's own coordinates (FNV-1a over quantised local metres), which scatters
    /// acceptance into a blue-noise spread without a seed sequence, and the greedy
    /// min-spacing pass over that fixed order is a pure function of (samples, config).
    /// Same surface + same config =&gt; byte-identical layout, forever.
    /// </summary>
    public static class SurfacePlacementGenerator
    {
        /// <summary>
        /// The accepted placements for <paramref name="samples"/> under
        /// <paramref name="config"/>. Deterministic and stable: no RNG, no clock, no
        /// input mutation.
        /// </summary>
        /// <param name="anchors">
        /// Optional pre-placed points (e.g. a hand-validated "proven" deposit). They
        /// are NOT emitted, but they DO occupy space: no generated placement is
        /// accepted within <see cref="SurfacePlacementConfig.MinSpacingMetres"/> of
        /// one, and they count against <see cref="SurfacePlacementConfig.TargetCount"/>.
        /// This lets a caller prepend a trusted coordinate and have the field fill in
        /// around it without overlapping it.
        /// </param>
        public static IReadOnlyList<GeneratedPlacement> Generate(
            IReadOnlyList<SurfaceSample> samples,
            SurfacePlacementConfig config,
            IReadOnlyList<GeneratedPlacement>? anchors = null)
        {
            // 1. RULE FILTER: upward-facing, reachable height, outside every exclusion.
            //    Exactly the gates the research spec lists, applied here (not baked
            //    into the data) so they are the tested, tunable surface.
            List<SurfaceSample> accepted = new List<SurfaceSample>();
            List<SurfaceSample> filtered = new List<SurfaceSample>();
            for (int i = 0; i < samples.Count; i++)
            {
                SurfaceSample s = samples[i];
                if (s.Ny < config.MinUpwardNormal)
                {
                    continue;
                }
                if (s.LocalY < config.MinReachableHeightMetres || s.LocalY > config.MaxReachableHeightMetres)
                {
                    continue;
                }
                if (config.IsExcluded(s.LocalX, s.LocalZ))
                {
                    continue;
                }
                filtered.Add(s);
            }

            // 2. DETERMINISTIC ORDER: sort by a hash of the point's own quantised
            //    coordinates. Stable (ties broken by the raw coordinates) so the order
            //    is a pure function of the point set, independent of the input order.
            filtered.Sort(CompareByHash);

            // 3. GREEDY MIN-SPACING (Poisson-disk / farthest-point style) over that
            //    fixed order, seeded by the anchors so generated points never crowd
            //    them. accepted-so-far includes anchors for the distance test but only
            //    generated points are emitted.
            double spacingSq = config.MinSpacingMetres * config.MinSpacingMetres;
            List<GeneratedPlacement> emitted = new List<GeneratedPlacement>();

            // Occupied = anchors (not emitted) + everything emitted so far.
            List<(double X, double Y, double Z)> occupied = new List<(double, double, double)>();
            if (anchors != null)
            {
                for (int i = 0; i < anchors.Count; i++)
                {
                    occupied.Add((anchors[i].LocalX, anchors[i].LocalY, anchors[i].LocalZ));
                }
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                if (occupied.Count >= config.TargetCount)
                {
                    break;
                }

                SurfaceSample s = filtered[i];
                if (IsTooClose(occupied, s.LocalX, s.LocalY, s.LocalZ, spacingSq))
                {
                    continue;
                }

                emitted.Add(new GeneratedPlacement(s.LocalX, s.LocalY, s.LocalZ, s.Ny));
                occupied.Add((s.LocalX, s.LocalY, s.LocalZ));
            }

            return emitted;
        }

        private static bool IsTooClose(List<(double X, double Y, double Z)> occupied, double x, double y, double z, double spacingSq)
        {
            for (int i = 0; i < occupied.Count; i++)
            {
                double dx = x - occupied[i].X;
                double dy = y - occupied[i].Y;
                double dz = z - occupied[i].Z;
                if ((dx * dx + dy * dy + dz * dz) < spacingSq)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CompareByHash(SurfaceSample a, SurfaceSample b)
        {
            ulong ha = HashKey(a.LocalX, a.LocalY, a.LocalZ);
            ulong hb = HashKey(b.LocalX, b.LocalY, b.LocalZ);
            int c = ha.CompareTo(hb);
            if (c != 0)
            {
                return c;
            }
            // Deterministic tie-break on the raw coordinates so equal-hash points
            // (astronomically rare) still order stably.
            c = a.LocalX.CompareTo(b.LocalX);
            if (c != 0) return c;
            c = a.LocalY.CompareTo(b.LocalY);
            if (c != 0) return c;
            return a.LocalZ.CompareTo(b.LocalZ);
        }

        /// <summary>
        /// FNV-1a (64-bit) over the point quantised to millimetres. A pure function
        /// of the coordinate - no seed, no state - so the acceptance order is fixed
        /// for a given surface and portable across machines and .NET versions
        /// (integer arithmetic only; no floating-point hash, no string culture).
        /// </summary>
        public static ulong HashKey(double x, double y, double z)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong h = offset;
            h = Fold(h, Quantise(x), prime);
            h = Fold(h, Quantise(y), prime);
            h = Fold(h, Quantise(z), prime);
            return h;
        }

        private static ulong Fold(ulong h, long value, ulong prime)
        {
            ulong v = (ulong)value;
            for (int b = 0; b < 8; b++)
            {
                h ^= (v & 0xFF);
                h *= prime;
                v >>= 8;
            }
            return h;
        }

        private static long Quantise(double metres)
        {
            // Millimetre grid, rounded to the nearest integer. Surface samples are
            // authored to 0.01 m, so a mm grid never collapses two distinct points.
            double mm = metres * 1000.0;
            return (long)System.Math.Round(mm, System.MidpointRounding.AwayFromZero);
        }
    }
}
