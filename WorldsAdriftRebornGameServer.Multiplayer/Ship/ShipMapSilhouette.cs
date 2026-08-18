using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// One hull-local point on the plan view, in world metres. X is across the
    /// beam (positive to starboard), Z is along the keel (positive toward the bow).
    /// </summary>
    public readonly record struct ShipMapPoint(double X, double Z);

    /// <summary>
    /// A ship's TOP-DOWN OUTLINE, derived from the hull the player actually built.
    ///
    /// WHAT THIS IS, EXACTLY. Not a boat drawing scaled to a ship's dimensions: a
    /// ring taken off the hull's own section geometry. A ShipPlan is a lofted hull -
    /// a sparse set of cells keyed (along-ship, vertical), each carrying cross
    /// SECTIONS of four vertices and four curve handles - and the widest point of
    /// each section, on each side, is a point on the hull's silhouette from above.
    /// String those points bow-to-stern down the starboard side and back up the
    /// port side and the ring closes on the real shape: the taper of a narrowed
    /// bow, the bulge of a curve handle pulled outboard, and the RAKE of a section
    /// whose vertices are pushed fore or aft off their own plane. A bounding box
    /// throws all three away; the live 60-byte hull on the running server rakes its
    /// bow and stern by 3.6 m, which a box would flatten into a plain rectangle.
    ///
    /// EVERY FORMULA HERE IS THE CLIENT'S, CITED. The section plane
    /// <c>(sectionNumber - 0.5) * 2</c>, the deck pitch, the fact that a serialised
    /// x IS the absolute half-width, the curve handles sitting at thirds and
    /// offsetting x only, and the fixed hull scale of 2 are all documented and
    /// asserted in <see cref="ShipHullMetrics"/> against the decompiled client -
    /// four independent agreeing citations, listed in that type's remarks. This
    /// type takes its constants from there rather than restating them, for the same
    /// reason the fauna map model reads its constants off the movement: a second
    /// copy of a number is a number that can drift.
    ///
    /// PROVENANCE, in the words the console must use: RECOVERED. The shape is the
    /// player's own hull bytes decoded, at the client's own scale. Nothing about it
    /// is inferred, tuned or approximated.
    ///
    /// Pure, engine-free and total.
    /// </summary>
    public sealed class ShipMapSilhouette
    {
        private ShipMapSilhouette(
            IReadOnlyList<ShipMapPoint> outline,
            int sectionCount,
            ShipHullMetrics metrics)
        {
            Outline = outline;
            SectionCount = sectionCount;
            Metrics = metrics;
        }

        /// <summary>
        /// The closed ring, hull-local metres, in draw order: starboard from the
        /// stern to the bow, then port from the bow back to the stern. The last
        /// point is NOT a repeat of the first - closing the ring is the drawer's
        /// job, and a duplicated point would be a zero-length edge in it.
        ///
        /// Empty when the hull carries no sections at all.
        /// </summary>
        public IReadOnlyList<ShipMapPoint> Outline { get; }

        /// <summary>
        /// How many distinct section planes the hull has - the number of transverse
        /// stations the ring is built from. Two points of <see cref="Outline"/> per
        /// section, so a ring is always an even number of points.
        /// </summary>
        public int SectionCount { get; }

        /// <summary>The measured hull, from <see cref="ShipHullMetrics.Measure"/>.</summary>
        public ShipHullMetrics Metrics { get; }

        /// <summary>Whether anything can be drawn.</summary>
        public bool IsEmpty => Outline.Count < 3;

        /// <summary>A silhouette for a hull with no sections.</summary>
        public static ShipMapSilhouette Empty { get; } = new ShipMapSilhouette(
            Array.Empty<ShipMapPoint>(), 0, new ShipHullMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0));

        /// <summary>
        /// Derive the plan-view outline of a decoded hull plan. Total: a null or
        /// empty plan comes back as <see cref="Empty"/> rather than throwing,
        /// because a hull whose shape cannot be drawn must still be REPORTED - the
        /// console draws it as a plain mark and says the shape is unavailable, which
        /// is far better than a stats snapshot that fails to be written.
        /// </summary>
        public static ShipMapSilhouette Of(ShipPlanModel? plan)
        {
            if (plan == null || plan.Cells.Count == 0)
            {
                return Empty;
            }

            // One entry per section PLANE. A cell's Front section stands at
            // cellNumber + 1 and its Back at cellNumber (acs/ShipCell ctor), and
            // Back is omitted on the wire whenever an astern neighbour exists -
            // that neighbour's Front IS this section. So visiting Front always and
            // Back when present reaches every distinct station exactly once, which
            // is the same traversal ShipHullMetrics.Measure makes.
            SortedDictionary<int, Station> stations = new SortedDictionary<int, Station>();

            foreach (ShipCellModel cell in plan.Cells)
            {
                Fold(stations, cell.Front, cell.CellNumber + 1);
                if (cell.Back != null)
                {
                    Fold(stations, cell.Back, cell.CellNumber);
                }
            }

            if (stations.Count < 2)
            {
                return new ShipMapSilhouette(
                    Array.Empty<ShipMapPoint>(), stations.Count, ShipHullMetrics.Measure(plan));
            }

            // WHICH END OF A RAKED SECTION THE OUTLINE FOLLOWS. A section is not
            // flat in z: its vertices carry their own z offset off the station
            // plane, which is how a hull gets an overhanging prow (the live hull's
            // bow vertices differ by 3.6 m). Seen from above, the silhouette is the
            // OUTER envelope of that rake, so a station in the forward half of the
            // hull contributes its foremost z and one in the after half its
            // aftmost. That is exact where it shows - at the two ends, where the
            // ring turns and nothing else covers the overhang - and sub-metre
            // anywhere in between, where the neighbouring cell's own hull fills the
            // gap either way.
            int first = int.MaxValue, last = int.MinValue;
            foreach (int sectionNumber in stations.Keys)
            {
                if (sectionNumber < first) first = sectionNumber;
                if (sectionNumber > last) last = sectionNumber;
            }
            double midSection = (first + last) / 2.0;

            List<ShipMapPoint> starboard = new List<ShipMapPoint>(stations.Count);
            List<ShipMapPoint> port = new List<ShipMapPoint>(stations.Count);
            foreach (KeyValuePair<int, Station> entry in stations)
            {
                bool forward = entry.Key >= midSection;
                starboard.Add(entry.Value.Point(1, forward));
                port.Add(entry.Value.Point(0, forward));
            }

            // Stern to bow down the starboard side, bow to stern back up the port
            // side: section numbers grow toward the bow (cell numbers do, and a
            // Front section is cellNumber + 1), and SortedDictionary hands them
            // over ascending.
            List<ShipMapPoint> ring = new List<ShipMapPoint>(starboard.Count + port.Count);
            ring.AddRange(starboard);
            for (int i = port.Count - 1; i >= 0; i--)
            {
                ring.Add(port[i]);
            }

            return new ShipMapSilhouette(ring, stations.Count, ShipHullMetrics.Measure(plan));
        }

        /// <summary>
        /// Fold one section into its station, keeping the OUTERMOST point on each
        /// side. Several cells can stand at one station - a two-deck ship has one
        /// per deck - and the silhouette from above is the widest of them, whichever
        /// deck it belongs to.
        /// </summary>
        private static void Fold(SortedDictionary<int, Station> stations,
            ShipSectionModel section, int sectionNumber)
        {
            if (!stations.TryGetValue(sectionNumber, out Station? station))
            {
                station = new Station(sectionNumber);
                stations[sectionNumber] = station;
            }

            for (int side = 0; side < 2; side++)
            {
                ShipVertexModel bottom = section.Bottom[side];
                ShipVertexModel top = section.Top[side];

                station.Offer(side, bottom.X, bottom.Z);
                station.Offer(side, top.X, top.Z);

                // The two curve handles sit at thirds up the bottom->top edge and
                // offset x only (acs/ShipSection.GetCurvePosition), so their z is
                // the plain lerp. They can bulge the beam past either vertex,
                // which is why the beam measurement folds them in too.
                for (int handle = 0; handle < 2; handle++)
                {
                    double t = (handle + 1) / 3.0;
                    double x = bottom.X + (top.X - bottom.X) * t + section.Curve[handle, side];
                    double z = bottom.Z + (top.Z - bottom.Z) * t;
                    station.Offer(side, x, z);
                }
            }
        }

        /// <summary>
        /// One transverse station: the outermost point found on each side, and the
        /// plane they stand on.
        /// </summary>
        private sealed class Station
        {
            private readonly double _planeZ;
            private readonly double[] _x = new double[2];
            private readonly double[] _zMin = new double[2];
            private readonly double[] _zMax = new double[2];
            private readonly bool[] _has = new bool[2];

            public Station(int sectionNumber)
            {
                _planeZ = (sectionNumber - 0.5) * ShipHullMetrics.SectionPitchRaw;
            }

            /// <summary>
            /// Fold one candidate in. The x kept is the one FURTHEST OUTBOARD: a
            /// side's x is signed - side 0 is port and negative, side 1 starboard
            /// and positive - so "further outboard" is the larger magnitude, and
            /// comparing magnitudes rather than signed values is what lets one
            /// branch serve both sides. The z is kept as a RANGE rather than
            /// paired with the winning x, because the fore-aft extent of a raked
            /// section is a property of the whole section, not of whichever vertex
            /// happened to be widest.
            /// </summary>
            public void Offer(int side, double x, double z)
            {
                if (double.IsNaN(x) || double.IsNaN(z))
                {
                    return;
                }

                if (!_has[side])
                {
                    _has[side] = true;
                    _x[side] = x;
                    _zMin[side] = z;
                    _zMax[side] = z;
                    return;
                }

                if (Math.Abs(x) > Math.Abs(_x[side])) _x[side] = x;
                if (z < _zMin[side]) _zMin[side] = z;
                if (z > _zMax[side]) _zMax[side] = z;
            }

            /// <summary>
            /// The station's point on one side, in world metres. Raw ShipPlan units
            /// times the client's fixed hull scale - the hull's local frame reaches
            /// the world unrotated and uniformly scaled by 2
            /// (acs/CustomShipFrameVisualizer.ShipScale).
            /// </summary>
            public ShipMapPoint Point(int side, bool forward) => new ShipMapPoint(
                _x[side] * ShipHullMetrics.ShipScale,
                (_planeZ + (forward ? _zMax[side] : _zMin[side])) * ShipHullMetrics.ShipScale);
        }
    }
}
