using System;
using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// THE ORIENTATION TRUTH for a ShipPlan hull, measured rather than assumed:
    /// which local axis is the bow, how long the keel is, how wide the beam is, and
    /// where the walkable deck plane sits - all in world metres at the client's fixed
    /// <see cref="ShipScale"/>.
    ///
    /// WHY THIS MODULE EXISTS. A live player reported "the ship goes sideways, I
    /// expect it to go forward" and, before that, "the helm is 90 degrees off", and
    /// the response both times was to blind-tune a helm yaw offset. Neither report
    /// was a rotation bug: a stock hull cell is 12 m of BEAM by 4 m of KEEL, so a
    /// short ship is genuinely wider than it is long and its bow is its SHORT axis.
    /// This type puts that number in the server log at spawn so nobody has to
    /// re-derive it from a hex dump again.
    ///
    /// THE AXES, cited to the decompiled client (all four agree, none of them is a
    /// guess):
    /// <list type="bullet">
    ///   <item>+Z is FORWARD/BOW. <c>acs/ShipCell.GetMidPoint</c> maps
    ///     <c>ShipDir.Forward</c> to +z and <c>ShipDir.Astern</c> to -z; the editor's
    ///     <c>acs/ShipExtruderGizmo.MoveTo</c> drives Forward/Astern along component
    ///     index 2 (z) in steps of 2; <c>acs/ShipSection.GetVertexOffset</c> maps the
    ///     section index onto z as <c>(sectionN - 0.5f) * 2f</c>; a cell's Front
    ///     section is <c>cellNumber + 1</c> (higher z) and its Back is
    ///     <c>cellNumber</c>.</item>
    ///   <item>+Y is UP. <c>GetVertexOffset</c> maps the deck index onto y as
    ///     <c>deckN * 1.7f</c>, with the four levels lerping 0..1.7 within a deck.</item>
    ///   <item>X is the BEAM. A vertex's serialised x IS its absolute half-width
    ///     (<c>GetVertexOffset</c> contributes x = 0), default +-3 m
    ///     (<c>ShipSection</c> ctor <c>float num = 3f</c>), clamped to +-8
    ///     (<c>ShipEditorConstants.MaxSectionWidth = 16f</c>); curve handles add to x
    ///     only. There is no lateral cell index at all - cells are keyed
    ///     <c>Vector2i(cellNumber, deckNumber)</c>, i.e. (along-ship, vertical).</item>
    ///   <item>The hull's local frame reaches the world UNROTATED and uniformly
    ///     scaled by 2: <c>acs/CustomShipFrameVisualizer.ShipScale = 2</c> and every
    ///     placement in <c>acs/MeshGenerator</c> is <c>localPosition = pos * scale</c>
    ///     with <c>localRotation = Quaternion.identity</c>.</item>
    /// </list>
    ///
    /// Pure and engine-free, so the mapping is asserted natively (ShipHullMetricsTests,
    /// including against the real 60-byte hull off the live save) instead of by
    /// staring at a running client.
    /// </summary>
    public readonly struct ShipHullMetrics
    {
        /// <summary>
        /// The client's fixed in-world hull scale: <c>CustomShipFrameVisualizer.ShipScale = 2</c>.
        /// Every metre in this type is a RAW ShipPlan unit multiplied by this.
        /// </summary>
        public const double ShipScale = 2.0;

        /// <summary>Raw distance between adjacent section planes: the <c>* 2f</c> in <c>(sectionN - 0.5f) * 2f</c>.</summary>
        public const double SectionPitchRaw = 2.0;

        /// <summary>Raw height of one deck: the <c>1.7f</c> in <c>deckN * 1.7f</c>.</summary>
        public const double DeckHeightRaw = 1.7;

        /// <summary>
        /// The hull-local axis the BOW points along, as a unit vector: +Z. Not a
        /// tunable - it is what the client's editor, mesh, thrust and roll axis all
        /// mean by "forward" (see the type remarks).
        /// </summary>
        public static readonly ShipVector3 BowDirection = new ShipVector3(0f, 0f, 1f);

        public ShipHullMetrics(
            int cellCount, int deckCount,
            int aftmostCellNumber, int foremostCellNumber,
            double beamMetres, double keelMetres,
            double bowLocalZMetres, double sternLocalZMetres,
            double deckPlaneMetres)
        {
            CellCount = cellCount;
            DeckCount = deckCount;
            AftmostCellNumber = aftmostCellNumber;
            ForemostCellNumber = foremostCellNumber;
            BeamMetres = beamMetres;
            KeelMetres = keelMetres;
            BowLocalZMetres = bowLocalZMetres;
            SternLocalZMetres = sternLocalZMetres;
            DeckPlaneMetres = deckPlaneMetres;
        }

        /// <summary>How many cells the plan carries.</summary>
        public int CellCount { get; }

        /// <summary>How many distinct deck numbers the plan carries.</summary>
        public int DeckCount { get; }

        /// <summary>The lowest cell number - the STERNMOST cell (cell numbers grow toward the bow).</summary>
        public int AftmostCellNumber { get; }

        /// <summary>The highest cell number - the BOWMOST cell.</summary>
        public int ForemostCellNumber { get; }

        /// <summary>Port-to-starboard width in world metres: the X extent times <see cref="ShipScale"/>.</summary>
        public double BeamMetres { get; }

        /// <summary>Stern-to-bow length in world metres: the Z extent times <see cref="ShipScale"/>.</summary>
        public double KeelMetres { get; }

        /// <summary>The hull-local Z of the foremost point of the hull, in world metres.</summary>
        public double BowLocalZMetres { get; }

        /// <summary>The hull-local Z of the aftmost point of the hull, in world metres.</summary>
        public double SternLocalZMetres { get; }

        /// <summary>
        /// The hull-local Y of the TOPMOST deck plane in world metres - the surface a
        /// player stands on and mounts parts to. A single-deck hull's is 3.4
        /// (<c>1.7 * 2</c>).
        /// </summary>
        public double DeckPlaneMetres { get; }

        /// <summary>
        /// Whether the hull is at least as long as it is wide. FALSE means the ship's
        /// longest run is its BEAM, so a pilot looking down the bow (+Z) sees the deck
        /// stretch left-right across the view - which reads as "the ship goes
        /// sideways" even though every axis is correct. Short hulls are false: a stock
        /// cell is 12 m of beam to 4 m of keel, so a hull needs 4 cells to be longer
        /// than it is wide at the default width.
        /// </summary>
        public bool KeelIsLongestAxis => KeelMetres >= BeamMetres;

        /// <summary>
        /// The number of stock-width cells a hull needs before its keel matches its
        /// beam: <see cref="BeamMetres"/> divided by the 4 m a cell contributes.
        /// Diagnostic for telling a player how much longer to build.
        /// </summary>
        public int CellsForKeelToMatchBeam
        {
            get
            {
                double perCell = SectionPitchRaw * ShipScale;
                return (int)Math.Ceiling((BeamMetres / perCell) - 1e-9);
            }
        }

        /// <summary>
        /// Measure a decoded plan. Never throws for a well-formed plan; an EMPTY plan
        /// measures as all-zero rather than blowing up a spawn path.
        /// </summary>
        public static ShipHullMetrics Measure(ShipPlanModel plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            if (plan.Cells.Count == 0)
            {
                return new ShipHullMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0);
            }

            int aftmost = int.MaxValue, foremost = int.MinValue;
            int minDeck = int.MaxValue, maxDeck = int.MinValue;
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            double maxTopY = double.MinValue;

            foreach (ShipCellModel cell in plan.Cells)
            {
                if (cell.CellNumber < aftmost) aftmost = cell.CellNumber;
                if (cell.CellNumber > foremost) foremost = cell.CellNumber;
                if (cell.DeckNumber < minDeck) minDeck = cell.DeckNumber;
                if (cell.DeckNumber > maxDeck) maxDeck = cell.DeckNumber;

                // A cell's Front section is at cellNumber + 1, its Back at cellNumber
                // (acs/ShipCell ctor). Back is absent on the wire whenever an astern
                // neighbour exists, because that neighbour's Front IS this section -
                // so measuring Front always and Back when present covers every
                // distinct section exactly once.
                Accumulate(cell.Front, cell.CellNumber + 1, cell.DeckNumber,
                    ref minX, ref maxX, ref minZ, ref maxZ, ref maxTopY);
                if (cell.Back != null)
                {
                    Accumulate(cell.Back, cell.CellNumber, cell.DeckNumber,
                        ref minX, ref maxX, ref minZ, ref maxZ, ref maxTopY);
                }
            }

            var decks = new System.Collections.Generic.HashSet<int>();
            foreach (ShipCellModel cell in plan.Cells)
            {
                decks.Add(cell.DeckNumber);
            }

            return new ShipHullMetrics(
                cellCount: plan.Cells.Count,
                deckCount: decks.Count,
                aftmostCellNumber: aftmost,
                foremostCellNumber: foremost,
                beamMetres: (maxX - minX) * ShipScale,
                keelMetres: (maxZ - minZ) * ShipScale,
                bowLocalZMetres: maxZ * ShipScale,
                sternLocalZMetres: minZ * ShipScale,
                deckPlaneMetres: maxTopY * ShipScale);
        }

        /// <summary>
        /// Fold one section's four vertices - AND the two per-side curve handles,
        /// which bulge the beam outward in x only - into the running extents. The
        /// vertex formula is the client's: a serialised x is the absolute half-width,
        /// while y and z are offsets ON the section/deck plane
        /// (<c>ShipSection.GetVertexOffset</c> + <c>GetCurvePosition</c>).
        /// </summary>
        private static void Accumulate(
            ShipSectionModel section, int sectionNumber, int deckNumber,
            ref double minX, ref double maxX,
            ref double minZ, ref double maxZ, ref double maxTopY)
        {
            double planeZ = (sectionNumber - 0.5) * SectionPitchRaw;
            double bottomY = deckNumber * DeckHeightRaw;
            double topY = bottomY + DeckHeightRaw;

            for (int side = 0; side < 2; side++)
            {
                ShipVertexModel bottom = section.Bottom[side];
                ShipVertexModel top = section.Top[side];

                foreach (double x in new[] { bottom.X, top.X })
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }

                // Curve handles sit at levels 1 and 2, offsetting x off the
                // bottom->top lerp; they can bulge the beam past either vertex.
                for (int handle = 0; handle < 2; handle++)
                {
                    double t = (handle + 1) / 3.0;
                    double baseX = bottom.X + (top.X - bottom.X) * t;
                    double x = baseX + section.Curve[handle, side];
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                }

                foreach (double z in new[] { planeZ + bottom.Z, planeZ + top.Z })
                {
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }

                double deckY = topY + top.Y;
                if (deckY > maxTopY) maxTopY = deckY;
            }
        }

        /// <summary>
        /// A one-line log summary. Deliberately spells out which axis is which and
        /// flags a beam-dominant hull, so a "my ship flies sideways" report can be
        /// answered from the server log instead of another round of yaw guessing.
        /// </summary>
        public string Describe()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            string shape = KeelIsLongestAxis
                ? "keel is the long axis"
                : "BEAM EXCEEDS KEEL - this hull is wider than it is long, so its bow is its SHORT axis"
                  + " (needs " + CellsForKeelToMatchBeam.ToString(c) + " cells at this width to match)";

            return CellCount.ToString(c) + " cell(s) on " + DeckCount.ToString(c) + " deck(s), cells "
                + AftmostCellNumber.ToString(c) + ".." + ForemostCellNumber.ToString(c)
                + " (aft..fore); beam " + BeamMetres.ToString("0.##", c) + " m (X) x keel "
                + KeelMetres.ToString("0.##", c) + " m (Z); bow at local +Z z="
                + BowLocalZMetres.ToString("0.##", c) + " m, stern z="
                + SternLocalZMetres.ToString("0.##", c) + " m; deck plane y="
                + DeckPlaneMetres.ToString("0.##", c) + " m; " + shape + ".";
        }
    }
}
