using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// One hull-local point on the SIDE elevation, in world metres. Z is along the
    /// keel (positive toward the bow), Y is up from the hull origin.
    /// </summary>
    public readonly record struct ShipProfilePoint(double Z, double Y);

    /// <summary>
    /// One deck of the hull as a LEVEL: the plane a player stands on, the floor the
    /// cell rises from, and how far fore and aft that deck actually runs. All in
    /// hull-local world metres.
    ///
    /// A deck is not necessarily the whole ship: a plan can carry an upper deck over
    /// only two of its six cells, and drawing that deck as a full-length line would
    /// be a claim about the ship that the hull bytes do not make.
    /// </summary>
    public readonly record struct ShipDeckLevel(
        int DeckNumber,
        double FloorMetres,
        double PlaneMetres,
        double SternZMetres,
        double BowZMetres);

    /// <summary>
    /// A ship's SIDE ELEVATION, derived from the hull the player actually built -
    /// the profile counterpart of <see cref="ShipMapSilhouette"/>, and built by the
    /// same station walk.
    ///
    /// WHY A SECOND VIEW AT ALL. The plan view answers "how wide, how long, what
    /// taper"; it cannot answer "how tall, how many decks, does the prow overhang".
    /// A ShipPlan is keyed <c>(cellNumber, deckNumber)</c> - along-ship and VERTICAL
    /// - so the deck axis is half of what the player built and the plan view throws
    /// all of it away. A two-deck ship and a one-deck ship of the same footprint are
    /// the same drawing from above and obviously different from the side.
    ///
    /// WHAT THE RING IS. Per section plane, the vertical envelope of the hull: the
    /// topmost point of the station and the bottom-most, taken over every cell
    /// standing at that station whichever deck it belongs to. Walked stern-to-bow
    /// along the upper edge and bow-to-stern back along the lower edge, so the ring
    /// closes on the elevation. The two edges carry their OWN z, which is what makes
    /// an overhanging prow visible: on the live 60-byte hull the top vertices rake
    /// 3.59 m forward of the bottom ones, and a ring that shared one z per station
    /// would draw that hull with a vertical stem.
    ///
    /// The curve handles are deliberately NOT folded in here, and that is not an
    /// omission: a handle offsets X ONLY (acs/ShipSection.GetCurvePosition), so in
    /// the (z, y) plane it sits exactly on the segment between the two vertices it
    /// interpolates and can never push the elevation's envelope outward. It bulges
    /// the BEAM, which is <see cref="ShipMapSilhouette"/>'s business.
    ///
    /// EVERY CONSTANT IS <see cref="ShipHullMetrics"/>'s, for the reason the
    /// silhouette gives: a second copy of a number is a number that can drift.
    ///
    /// PROVENANCE: RECOVERED. The elevation is the player's own hull bytes decoded,
    /// at the client's own scale. Pure, engine-free and total.
    /// </summary>
    public sealed class ShipMapProfile
    {
        private ShipMapProfile(
            IReadOnlyList<ShipProfilePoint> outline,
            IReadOnlyList<ShipDeckLevel> decks,
            int sectionCount,
            double floorMetres,
            double headMetres,
            ShipHullMetrics metrics)
        {
            Outline = outline;
            Decks = decks;
            SectionCount = sectionCount;
            FloorMetres = floorMetres;
            HeadMetres = headMetres;
            Metrics = metrics;
        }

        /// <summary>
        /// The closed ring, hull-local metres, in draw order: the upper edge from the
        /// stern to the bow, then the lower edge from the bow back to the stern. The
        /// last point is NOT a repeat of the first - closing the ring is the drawer's
        /// job, exactly as for the plan view.
        ///
        /// Empty when the hull carries fewer than two section planes.
        /// </summary>
        public IReadOnlyList<ShipProfilePoint> Outline { get; }

        /// <summary>
        /// The decks, lowest first. One entry per distinct <c>deckNumber</c> the plan
        /// carries, each with the run it actually spans.
        /// </summary>
        public IReadOnlyList<ShipDeckLevel> Decks { get; }

        /// <summary>How many distinct section planes the elevation is built from.</summary>
        public int SectionCount { get; }

        /// <summary>The lowest point of the hull, hull-local metres. The keel line.</summary>
        public double FloorMetres { get; }

        /// <summary>
        /// The highest point of the hull, hull-local metres. For a plan whose decks
        /// stack without gaps this is the topmost walkable plane, and it is the same
        /// number <see cref="ShipHullMetrics.DeckPlaneMetres"/> measures - asserted,
        /// not assumed.
        /// </summary>
        public double HeadMetres { get; }

        /// <summary>Overall height, stem to keel, in metres.</summary>
        public double HeightMetres => HeadMetres - FloorMetres;

        /// <summary>The measured hull, from <see cref="ShipHullMetrics.Measure"/>.</summary>
        public ShipHullMetrics Metrics { get; }

        /// <summary>Whether anything can be drawn.</summary>
        public bool IsEmpty => Outline.Count < 3;

        /// <summary>A profile for a hull with no sections.</summary>
        public static ShipMapProfile Empty { get; } = new ShipMapProfile(
            Array.Empty<ShipProfilePoint>(), Array.Empty<ShipDeckLevel>(), 0, 0, 0,
            new ShipHullMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0));

        /// <summary>
        /// Derive the side elevation of a decoded hull plan. Total, for the same
        /// reason <see cref="ShipMapSilhouette.Of"/> is: a hull whose shape cannot be
        /// drawn must still be REPORTED, and a snapshot that fails to be written
        /// takes the whole operator surface offline.
        /// </summary>
        public static ShipMapProfile Of(ShipPlanModel? plan)
        {
            if (plan == null || plan.Cells.Count == 0)
            {
                return Empty;
            }

            // The same traversal ShipHullMetrics.Measure and ShipMapSilhouette.Of
            // make: a cell's Front section stands at cellNumber + 1 and its Back at
            // cellNumber, and Back is omitted on the wire whenever an astern
            // neighbour exists - so visiting Front always and Back when present
            // reaches every distinct station exactly once.
            SortedDictionary<int, Station> stations = new SortedDictionary<int, Station>();
            SortedDictionary<int, DeckRun> decks = new SortedDictionary<int, DeckRun>();

            foreach (ShipCellModel cell in plan.Cells)
            {
                Fold(stations, decks, cell.Front, cell.CellNumber + 1, cell.DeckNumber);
                if (cell.Back != null)
                {
                    Fold(stations, decks, cell.Back, cell.CellNumber, cell.DeckNumber);
                }
            }

            List<ShipDeckLevel> levels = new List<ShipDeckLevel>(decks.Count);
            foreach (KeyValuePair<int, DeckRun> entry in decks)
            {
                levels.Add(entry.Value.Level());
            }

            ShipHullMetrics metrics = ShipHullMetrics.Measure(plan);
            if (stations.Count < 2)
            {
                return new ShipMapProfile(
                    Array.Empty<ShipProfilePoint>(), levels, stations.Count, 0, 0, metrics);
            }

            // Which end of a raked section each edge follows, by the same rule the
            // plan view uses: seen from the side the elevation is the OUTER envelope
            // of the rake, so a station in the forward half contributes its foremost
            // z and one in the after half its aftmost. Exact at the two ends, where
            // the ring turns and nothing else covers the overhang.
            int first = int.MaxValue, last = int.MinValue;
            foreach (int sectionNumber in stations.Keys)
            {
                if (sectionNumber < first) first = sectionNumber;
                if (sectionNumber > last) last = sectionNumber;
            }
            double midSection = (first + last) / 2.0;

            List<ShipProfilePoint> upper = new List<ShipProfilePoint>(stations.Count);
            List<ShipProfilePoint> lower = new List<ShipProfilePoint>(stations.Count);
            double floor = double.MaxValue, head = double.MinValue;
            foreach (KeyValuePair<int, Station> entry in stations)
            {
                bool forward = entry.Key >= midSection;
                ShipProfilePoint top = entry.Value.Point(Station.Upper, forward);
                ShipProfilePoint bottom = entry.Value.Point(Station.Lower, forward);
                upper.Add(top);
                lower.Add(bottom);
                if (top.Y > head) head = top.Y;
                if (bottom.Y < floor) floor = bottom.Y;
            }

            // Stern to bow along the top, bow to stern back along the bottom.
            List<ShipProfilePoint> ring = new List<ShipProfilePoint>(upper.Count + lower.Count);
            ring.AddRange(upper);
            for (int i = lower.Count - 1; i >= 0; i--)
            {
                ring.Add(lower[i]);
            }

            return new ShipMapProfile(ring, levels, stations.Count, floor, head, metrics);
        }

        /// <summary>
        /// Fold one section into its station AND into its deck's run. Several cells
        /// can stand at one station - a two-deck ship has one per deck - and the
        /// elevation is the tallest and the lowest of them, whichever deck each
        /// belongs to.
        /// </summary>
        private static void Fold(
            SortedDictionary<int, Station> stations,
            SortedDictionary<int, DeckRun> decks,
            ShipSectionModel section, int sectionNumber, int deckNumber)
        {
            if (!stations.TryGetValue(sectionNumber, out Station? station))
            {
                station = new Station();
                stations[sectionNumber] = station;
            }
            if (!decks.TryGetValue(deckNumber, out DeckRun? run))
            {
                run = new DeckRun(deckNumber);
                decks[deckNumber] = run;
            }

            // The client's own vertex formula: a section's y and z are OFFSETS on the
            // station/deck plane, the deck plane is deckN * 1.7 and the four levels
            // lerp 0..1.7 within a deck, so a bottom vertex sits on this deck's floor
            // and a top vertex on the next one up (ShipSection.GetVertexOffset).
            double planeZ = (sectionNumber - 0.5) * ShipHullMetrics.SectionPitchRaw;
            double floorY = deckNumber * ShipHullMetrics.DeckHeightRaw;
            double planeY = floorY + ShipHullMetrics.DeckHeightRaw;

            for (int side = 0; side < 2; side++)
            {
                ShipVertexModel bottom = section.Bottom[side];
                ShipVertexModel top = section.Top[side];

                station.Offer(Station.Lower, planeZ + bottom.Z, floorY + bottom.Y);
                station.Offer(Station.Upper, planeZ + top.Z, planeY + top.Y);
                run.Offer(planeZ + bottom.Z, planeZ + top.Z, floorY + bottom.Y, planeY + top.Y);
            }
        }

        /// <summary>
        /// One transverse station's vertical envelope: the topmost and bottom-most
        /// point found, each with the fore-aft range of the vertices that carry it.
        /// </summary>
        private sealed class Station
        {
            internal const int Lower = 0;
            internal const int Upper = 1;

            private readonly double[] _y = new double[2];
            private readonly double[] _zMin = new double[2];
            private readonly double[] _zMax = new double[2];
            private readonly bool[] _has = new bool[2];

            /// <summary>
            /// Fold one candidate in. The y kept is the OUTERMOST for that edge -
            /// highest for the upper edge, lowest for the lower - and the z is kept
            /// as a RANGE rather than paired with the winning y, because the fore-aft
            /// extent of a raked section is a property of the whole section and not
            /// of whichever vertex happened to be tallest.
            /// </summary>
            public void Offer(int edge, double z, double y)
            {
                if (double.IsNaN(z) || double.IsNaN(y))
                {
                    return;
                }

                if (!_has[edge])
                {
                    _has[edge] = true;
                    _y[edge] = y;
                    _zMin[edge] = z;
                    _zMax[edge] = z;
                    return;
                }

                if (edge == Upper ? y > _y[edge] : y < _y[edge]) _y[edge] = y;
                if (z < _zMin[edge]) _zMin[edge] = z;
                if (z > _zMax[edge]) _zMax[edge] = z;
            }

            /// <summary>
            /// The station's point on one edge, in world metres: raw ShipPlan units
            /// times the client's fixed hull scale of 2
            /// (acs/CustomShipFrameVisualizer.ShipScale).
            /// </summary>
            public ShipProfilePoint Point(int edge, bool forward) => new ShipProfilePoint(
                (forward ? _zMax[edge] : _zMin[edge]) * ShipHullMetrics.ShipScale,
                _y[edge] * ShipHullMetrics.ShipScale);
        }

        /// <summary>One deck's accumulated run: how high it sits and how far it reaches.</summary>
        private sealed class DeckRun
        {
            private readonly int _deckNumber;
            private double _minZ = double.MaxValue, _maxZ = double.MinValue;
            private double _floor = double.MaxValue, _plane = double.MinValue;

            public DeckRun(int deckNumber)
            {
                _deckNumber = deckNumber;
            }

            public void Offer(double bottomZ, double topZ, double floorY, double planeY)
            {
                foreach (double z in new[] { bottomZ, topZ })
                {
                    if (double.IsNaN(z)) continue;
                    if (z < _minZ) _minZ = z;
                    if (z > _maxZ) _maxZ = z;
                }
                if (!double.IsNaN(floorY) && floorY < _floor) _floor = floorY;
                if (!double.IsNaN(planeY) && planeY > _plane) _plane = planeY;
            }

            public ShipDeckLevel Level()
            {
                bool any = _minZ <= _maxZ;
                return new ShipDeckLevel(
                    _deckNumber,
                    any ? _floor * ShipHullMetrics.ShipScale : 0,
                    any ? _plane * ShipHullMetrics.ShipScale : 0,
                    any ? _minZ * ShipHullMetrics.ShipScale : 0,
                    any ? _maxZ * ShipHullMetrics.ShipScale : 0);
            }
        }
    }
}
