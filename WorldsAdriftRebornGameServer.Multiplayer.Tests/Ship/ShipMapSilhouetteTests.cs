using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// THE SHAPE ON THE MAP IS THE SHAPE IN THE GAME.
    ///
    /// The operator console draws a ship as an outline, and the whole claim of that
    /// feature is that the outline is the player's own hull rather than a boat
    /// icon. These assert it against the same evidence the orientation suite uses:
    /// the client's own section formulas, and the REAL 60-byte hull pulled byte for
    /// byte off the live save.
    ///
    /// The load-bearing assertion is the one tying the outline to
    /// <see cref="ShipHullMetrics"/>: the ring's own bounding box must BE the
    /// measured beam and keel, to the millimetre. Those two derivations share their
    /// constants but not their arithmetic, so a ring that had quietly stopped
    /// following the hull would move away from a measurement that had not.
    /// </summary>
    public class ShipMapSilhouetteTests
    {
        /// <summary>
        /// The live player's saved hull, byte for byte off the server: two cells at
        /// cellNumber 0 and -1 on deck 0, stock half-width, raked bow and stern.
        /// The same fixture ShipHullMetricsTests uses.
        /// </summary>
        private const string LiveSavedHullHex =
            "020000000000e80000180000e8008e18008e0000000000ffff0000e80000180000e8"
            + "00001800000000000001e80000180000e8007218007200000000";

        private static ShipPlanModel LiveSavedHull()
        {
            string hex = LiveSavedHullHex;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            Assert.True(ShipPlanModel.TryDecode(bytes, out ShipPlanModel? plan, out string? error), error);
            return plan!;
        }

        // ---- the ring is the hull ---------------------------------------------

        /// <summary>
        /// THE ANCHOR TEST. The outline's own extents must equal the hull's
        /// measured beam, keel, bow and stern - which for the live hull means
        /// 12.1 m across, 8 m long, bow at +2 and stern at -6 - or the console is
        /// drawing something that is not this ship.
        /// </summary>
        [Fact]
        public void The_live_hulls_outline_spans_exactly_its_measured_beam_and_keel()
        {
            ShipPlanModel plan = LiveSavedHull();
            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(plan);
            ShipHullMetrics metrics = ShipHullMetrics.Measure(plan);

            Assert.False(silhouette.IsEmpty);
            (double minX, double maxX, double minZ, double maxZ) = Bounds(silhouette.Outline);

            Assert.Equal(metrics.BeamMetres, maxX - minX, 6);
            Assert.Equal(metrics.KeelMetres, maxZ - minZ, 6);
            Assert.Equal(metrics.BowLocalZMetres, maxZ, 6);
            Assert.Equal(metrics.SternLocalZMetres, minZ, 6);
        }

        /// <summary>
        /// Two cells means three section planes - the shared one in the middle is
        /// visited once, not twice - so the ring is six points, not eight. A
        /// duplicated station would put two coincident points in the ring and,
        /// worse, would mean the traversal had double-counted a section.
        /// </summary>
        [Fact]
        public void The_live_hull_yields_one_station_per_section_plane()
        {
            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(LiveSavedHull());

            Assert.Equal(3, silhouette.SectionCount);
            Assert.Equal(6, silhouette.Outline.Count);
        }

        /// <summary>
        /// THE RAKE SURVIVES, which is the entire reason this is not a bounding
        /// box. The live hull's prow overhangs its keel by 1.795 raw units - 3.59 m
        /// at hull scale - and the ring must follow the overhang, i.e. its bowmost
        /// point must be 3.59 m forward of where the bow section's LOWER vertices
        /// sit. A box, or a ring that had picked the wrong end of the rake, would
        /// put the two in the same place.
        /// </summary>
        [Fact]
        public void The_outline_follows_the_overhanging_prow_rather_than_the_keel_below_it()
        {
            ShipPlanModel plan = LiveSavedHull();
            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(plan);
            (_, _, double minZ, double maxZ) = Bounds(silhouette.Outline);

            ShipCellModel bow = plan.Cells.Find(c => c.CellNumber == 0)!;
            ShipCellModel stern = plan.Cells.Find(c => c.CellNumber == -1)!;

            double bowPlane = (bow.CellNumber + 1 - 0.5) * ShipHullMetrics.SectionPitchRaw;
            double sternPlane = (stern.CellNumber - 0.5) * ShipHullMetrics.SectionPitchRaw;
            double keelAtBow = (bowPlane + bow.Front.Bottom[0].Z) * ShipHullMetrics.ShipScale;
            double keelAtStern = (sternPlane + stern.Back!.Bottom[0].Z) * ShipHullMetrics.ShipScale;

            Assert.Equal(3.59, maxZ - keelAtBow, 2);
            Assert.Equal(3.59, keelAtStern - minZ, 2);
        }

        /// <summary>
        /// The ring is drawn starboard-then-port, so the first half is all at
        /// positive x and the second half all at negative x, and each half runs
        /// monotonically along the keel. This is what lets the console emit it as a
        /// single SVG subpath without sorting anything itself.
        /// </summary>
        [Fact]
        public void The_ring_runs_up_the_starboard_side_and_back_down_the_port_side()
        {
            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(LiveSavedHull());
            IReadOnlyList<ShipMapPoint> ring = silhouette.Outline;
            int half = ring.Count / 2;

            for (int i = 0; i < half; i++)
            {
                Assert.True(ring[i].X > 0, "point " + i + " should be to starboard");
                if (i > 0) Assert.True(ring[i].Z > ring[i - 1].Z, "starboard should run toward the bow");
            }
            for (int i = half; i < ring.Count; i++)
            {
                Assert.True(ring[i].X < 0, "point " + i + " should be to port");
                if (i > half) Assert.True(ring[i].Z < ring[i - 1].Z, "port should run toward the stern");
            }
        }

        // ---- what makes a hull a different shape -------------------------------

        /// <summary>
        /// A curve handle pulled outboard bulges the beam, and the silhouette must
        /// show the bulge. This is the case a top-and-bottom-vertices-only outline
        /// silently loses: both vertices are untouched and only the handle moved.
        /// </summary>
        [Fact]
        public void A_curve_handle_pulled_outboard_widens_the_outline()
        {
            ShipPlanModel plain = ShipPlanModel.MakeDefaultStarterHull();
            double before = Width(ShipMapSilhouette.Of(plain));

            ShipPlanModel bulged = ShipPlanModel.MakeDefaultStarterHull();
            bulged.Cells[0].Front.Curve[0, 1] = 1.5f;
            double after = Width(ShipMapSilhouette.Of(bulged));

            Assert.Equal(before + 1.5 * ShipHullMetrics.ShipScale, after, 6);
        }

        /// <summary>
        /// A narrowed bow section narrows the outline THERE and nowhere else - the
        /// ring is per-station, so a taper is a taper and not a uniform rescale.
        /// </summary>
        [Fact]
        public void Narrowing_one_section_tapers_the_outline_at_that_station_only()
        {
            ShipPlanModel plan = ShipPlanModel.MakeDefaultStarterHull();
            plan.Cells.Add(new ShipCellModel(
                cellNumber: 1, deckNumber: 0,
                front: ShipSectionModel.MakeDefault(halfWidth: 1f), back: null));

            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(plan);
            Assert.Equal(3, silhouette.SectionCount);

            // Starboard runs stern to bow, so the last starboard point is the bow.
            IReadOnlyList<ShipMapPoint> ring = silhouette.Outline;
            Assert.Equal(2.0, ring[2].X, 6);
            Assert.Equal(6.0, ring[1].X, 6);
            Assert.Equal(6.0, ring[0].X, 6);
        }

        /// <summary>
        /// A second deck stacked on the same cells does NOT widen the plan view -
        /// there is no lateral cell index, decks stack in y - but a WIDER second
        /// deck does, because the silhouette from above is the widest thing at each
        /// station whichever deck it belongs to.
        /// </summary>
        [Fact]
        public void Stacked_decks_contribute_their_widest_section_and_only_that()
        {
            ShipPlanModel same = ShipPlanModel.MakeDefaultStarterHull();
            double single = Width(ShipMapSilhouette.Of(same));
            same.Cells.Add(new ShipCellModel(
                cellNumber: same.Cells[0].CellNumber, deckNumber: 1,
                front: ShipSectionModel.MakeDefault(), back: ShipSectionModel.MakeDefault()));
            Assert.Equal(single, Width(ShipMapSilhouette.Of(same)), 6);

            ShipPlanModel wider = ShipPlanModel.MakeDefaultStarterHull();
            wider.Cells.Add(new ShipCellModel(
                cellNumber: wider.Cells[0].CellNumber, deckNumber: 1,
                front: ShipSectionModel.MakeDefault(halfWidth: 5f),
                back: ShipSectionModel.MakeDefault(halfWidth: 5f)));
            Assert.Equal(20.0, Width(ShipMapSilhouette.Of(wider)), 6);
        }

        // ---- totality ----------------------------------------------------------

        /// <summary>
        /// A hull the console cannot draw must still be REPORTABLE. Nothing here
        /// may throw into a stats snapshot: a snapshot that fails to be written
        /// takes the whole operator panel offline, and the cost of an undrawable
        /// hull is one ship shown as a plain mark.
        /// </summary>
        [Fact]
        public void An_absent_or_empty_plan_is_an_empty_silhouette_and_never_a_throw()
        {
            Assert.True(ShipMapSilhouette.Of(null).IsEmpty);
            Assert.Empty(ShipMapSilhouette.Of(null).Outline);
            Assert.True(ShipMapSilhouette.Of(new ShipPlanModel()).IsEmpty);
            Assert.Equal(0, ShipMapSilhouette.Empty.SectionCount);
        }

        /// <summary>
        /// The minimum hull the server falls back to is drawable, because it is
        /// what a ship whose bytes failed to decode ends up being.
        /// </summary>
        [Fact]
        public void The_minimum_fallback_hull_is_drawable()
        {
            Assert.True(ShipPlanModel.TryDecode(
                Convert.FromBase64String(ShipHull.MinimumHullDataBase64),
                out ShipPlanModel? plan, out string? error), error);

            ShipMapSilhouette silhouette = ShipMapSilhouette.Of(plan);
            Assert.False(silhouette.IsEmpty);
            Assert.Equal(2, silhouette.SectionCount);
            Assert.Equal(4, silhouette.Outline.Count);
        }

        private static double Width(ShipMapSilhouette silhouette)
        {
            (double minX, double maxX, _, _) = Bounds(silhouette.Outline);
            return maxX - minX;
        }

        private static (double MinX, double MaxX, double MinZ, double MaxZ) Bounds(
            IReadOnlyList<ShipMapPoint> ring)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (ShipMapPoint p in ring)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            return (minX, maxX, minZ, maxZ);
        }
    }
}
