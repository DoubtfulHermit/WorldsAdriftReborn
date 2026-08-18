using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Fidelity tests for <see cref="DeckGenerator"/>, the pure server port of the
    /// client's <c>ShipPlan -&gt; ShipHullPartData.Decks</c> derivation. Every assertion
    /// here is what a running client would build from the same hull bytes: one deck panel
    /// per client-derived quad-strip, sized from the real section geometry, with the
    /// client's winding and its (faithfully reproduced) subdivision behaviour.
    ///
    /// These are the proof of correctness that runs on Linux with no game install, so a
    /// regression in the port is caught here rather than on a live ship whose floor is
    /// one slab wide.
    /// </summary>
    public class DeckGeneratorTests
    {
        // ---- plan builders -------------------------------------------------

        private static ShipSectionModel Section(float halfWidth)
        {
            var s = new ShipSectionModel();
            s.Top[0] = new ShipVertexModel(-halfWidth, 0f, 0f);
            s.Top[1] = new ShipVertexModel(halfWidth, 0f, 0f);
            s.Bottom[0] = new ShipVertexModel(-halfWidth, 0f, 0f);
            s.Bottom[1] = new ShipVertexModel(halfWidth, 0f, 0f);
            return s;
        }

        /// <summary>A shifted section: both sides moved by <paramref name="shift"/> so the cell is off-centre in x.</summary>
        private static ShipSectionModel ShiftedSection(float halfWidth, float shift)
        {
            var s = new ShipSectionModel();
            s.Top[0] = new ShipVertexModel(-halfWidth + shift, 0f, 0f);
            s.Top[1] = new ShipVertexModel(halfWidth + shift, 0f, 0f);
            s.Bottom[0] = new ShipVertexModel(-halfWidth + shift, 0f, 0f);
            s.Bottom[1] = new ShipVertexModel(halfWidth + shift, 0f, 0f);
            return s;
        }

        /// <summary>A section whose Bottom (floor) y is raised by <paramref name="y"/> on both sides.</summary>
        private static ShipSectionModel FloorYSection(float halfWidth, float y0, float y1)
        {
            var s = new ShipSectionModel();
            s.Top[0] = new ShipVertexModel(-halfWidth, 0f, 0f);
            s.Top[1] = new ShipVertexModel(halfWidth, 0f, 0f);
            s.Bottom[0] = new ShipVertexModel(-halfWidth, y0, 0f);
            s.Bottom[1] = new ShipVertexModel(halfWidth, y1, 0f);
            return s;
        }

        /// <summary>A contiguous single-deck row of <paramref name="n"/> cells, each <paramref name="halfWidth"/> wide.</summary>
        private static ShipPlanModel Row(int n, float halfWidth = 3f)
        {
            var plan = new ShipPlanModel();
            for (int c = 0; c < n; c++)
            {
                // Only the aft-most cell (no astern neighbour) carries a Back section, as
                // the client writes it (hasBack == no astern neighbour).
                ShipSectionModel? back = c == 0 ? Section(halfWidth) : null;
                plan.Cells.Add(new ShipCellModel(c, 0, Section(halfWidth), back));
            }
            return plan;
        }

        /// <summary>A single vertical stack of <paramref name="decks"/> cells at cell number 0.</summary>
        private static ShipPlanModel Stack(int decks, float halfWidth = 3f)
        {
            var plan = new ShipPlanModel();
            for (int d = 0; d < decks; d++)
            {
                // Each column cell is aft-most in its own column, so each carries a Back.
                plan.Cells.Add(new ShipCellModel(0, d, Section(halfWidth), Section(halfWidth)));
            }
            return plan;
        }

        // ---- geometry helpers ---------------------------------------------

        private const float Tol = 1e-3f;

        /// <summary>Signed-area magnitude of a flat panel in the x/z plane (shoelace).</summary>
        private static double PanelArea(IReadOnlyList<ShipVector3> vs)
        {
            double sum = 0;
            for (int i = 0; i < vs.Count; i++)
            {
                ShipVector3 a = vs[i];
                ShipVector3 b = vs[(i + 1) % vs.Count];
                sum += (double)a.X * b.Z - (double)b.X * a.Z;
            }
            return System.Math.Abs(sum) * 0.5;
        }

        private static double TotalArea(IReadOnlyList<DeckPanel> panels)
            => panels.Sum(p => PanelArea(p.LocalVertices));

        /// <summary>The client's MakeDeck winding test on a panel's local loop: Cross(v1-v0, v2-v0).y.</summary>
        private static float WindingY(IReadOnlyList<ShipVector3> vs)
            => ShipVector3.Cross(vs[1] - vs[0], vs[2] - vs[0]).Y;

        private static IEnumerable<DeckPanel> AtLevel(IReadOnlyList<DeckPanel> panels, float posY)
            => panels.Where(p => System.Math.Abs(p.HullLocalPositionMetres.Y - posY) < Tol);

        // ---- 1. default one-cell hull -------------------------------------

        [Fact]
        public void Default_hull_yields_six_panels_two_levels_three_strips_each()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

            // Two flat quads (lower floor at raw y=0, exposed top at raw y=1.7), each
            // subdivided into three lateral strips -> six deck entities.
            Assert.Equal(6, panels.Count);

            List<DeckPanel> floor = AtLevel(panels, 0f).ToList();       // 2*centroid.y = 0
            List<DeckPanel> top = AtLevel(panels, 3.4f).ToList();       // 2*1.7 = 3.4
            Assert.Equal(3, floor.Count);
            Assert.Equal(3, top.Count);
        }

        [Fact]
        public void Default_floor_strip_centroids_are_minus_four_zero_plus_four_metres()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

            float[] xs = AtLevel(panels, 0f)
                .Select(p => p.HullLocalPositionMetres.X)
                .OrderBy(x => x)
                .ToArray();

            // Strip centroids at raw x -2/0/+2, entity position = 2*centroid = -4/0/+4 m.
            Assert.Equal(new[] { -4f, 0f, 4f }, xs);
        }

        [Fact]
        public void Default_floor_panels_have_client_negative_winding()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

            foreach (DeckPanel p in AtLevel(panels, 0f))
            {
                // MeshGenerator.MakeDeck extrudes -0.04 when Cross(v1-v0,v2-v0).y < 0; the
                // ordinary lower floor is authored that way. Preserve it.
                Assert.True(WindingY(p.LocalVertices) < 0f,
                    "floor panel winding should be negative like the client");
            }
        }

        [Fact]
        public void Default_local_vertices_are_centroid_relative_and_flat()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

            foreach (DeckPanel p in panels)
            {
                // Local loop sums to ~zero (centroid-relative) and is flat (all y equal).
                float sx = p.LocalVertices.Sum(v => v.X);
                float sz = p.LocalVertices.Sum(v => v.Z);
                Assert.True(System.Math.Abs(sx) < Tol && System.Math.Abs(sz) < Tol);

                float y0 = p.LocalVertices[0].Y;
                Assert.All(p.LocalVertices, v => Assert.True(System.Math.Abs(v.Y - y0) < Tol));
            }
        }

        [Fact]
        public void Default_area_is_conserved_twelve_per_surface()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(ShipPlanModel.MakeDefaultStarterHull());

            // Each 6-wide x 2-deep surface is 12 raw square units; strips tile it exactly.
            Assert.Equal(12.0, AtLevel(panels, 0f).Sum(p => PanelArea(p.LocalVertices)), 3);
            Assert.Equal(24.0, TotalArea(panels), 3);
        }

        // ---- 2. contiguous cells ------------------------------------------

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void Contiguous_row_covers_every_cell_with_shared_edges_and_no_gaps(int n)
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(Row(n));

            // 2N flat quads (a floor and a top per cell), 3 strips each -> 6N panels.
            Assert.Equal(6 * n, panels.Count);

            // Total covered area equals N cells' floor + top, 12 each: no gaps, no overlap.
            Assert.Equal(24.0 * n, TotalArea(panels), 3);

            // Shared longitudinal edges are geometric: the floor strips tile a continuous
            // z span with no gap. Reconstruct each floor panel's world z-extent from its
            // centroid + local verts and assert the union is gapless [-1, 2N-1] raw.
            List<(float lo, float hi)> zSpans = AtLevel(panels, 0f)
                .Select(p =>
                {
                    float cz = p.HullLocalPositionMetres.Z / 2f; // centroid.z
                    float lo = cz + p.LocalVertices.Min(v => v.Z);
                    float hi = cz + p.LocalVertices.Max(v => v.Z);
                    return (lo, hi);
                })
                .OrderBy(s => s.lo)
                .ToList();

            float min = zSpans.Min(s => s.lo);
            float max = zSpans.Max(s => s.hi);
            Assert.Equal(-1f, min, 3);
            Assert.Equal(2f * n - 1f, max, 3);
        }

        // ---- 3. width edits -> trapezoids ---------------------------------

        [Fact]
        public void Widening_the_front_produces_a_wider_footprint_with_conserved_area()
        {
            // Aft half-width 3, forward half-width 5: a trapezoid floor. Union of the
            // clipped strips must equal the trapezoid, which is 0.5*(6+10)*2 = 16 per
            // surface, 32 for floor + top.
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(0, 0, Section(5f), Section(3f)));

            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(plan);

            Assert.Equal(32.0, TotalArea(panels), 2);

            // The wider footprint spans raw x [-5,5] -> 5 lateral strips per surface.
            Assert.Equal(5, AtLevel(panels, 0f).Count());
            Assert.Equal(10, panels.Count);

            // At least one panel is a non-rectangular trapezoid strip (a slanted edge).
            Assert.Contains(AtLevel(panels, 0f), p => IsTrapezoid(p.LocalVertices));
        }

        private static bool IsTrapezoid(IReadOnlyList<ShipVector3> vs)
        {
            // Two vertices at one z-extreme differ in |x| span from the two at the other
            // z-extreme: a trapezoid, not an axis-aligned rectangle.
            float zMin = vs.Min(v => v.Z);
            float zMax = vs.Max(v => v.Z);
            float wLo = XSpanAt(vs, zMin);
            float wHi = XSpanAt(vs, zMax);
            return System.Math.Abs(wLo - wHi) > Tol;
        }

        private static float XSpanAt(IReadOnlyList<ShipVector3> vs, float z)
        {
            var xs = vs.Where(v => System.Math.Abs(v.Z - z) < Tol).Select(v => v.X).ToList();
            return xs.Count < 2 ? 0f : xs.Max() - xs.Min();
        }

        // ---- 4. asymmetric x edit: lock down the client's mixed-coord clip -

        [Fact]
        public void Laterally_shifted_hull_reproduces_the_clients_mixed_coordinate_gap()
        {
            // A cell whose sides are BOTH shifted +2 in x is centroid-aligned back to a
            // symmetric quad, but ClipX compares reconstructed hull-local x against
            // centroid-relative strip bounds (acs/ShipHullPartData.cs:231-240). The client
            // therefore leaves a gap on the shifted side. We reproduce that faithfully
            // rather than "fixing" it; whether the editor can actually produce this shape
            // is a Section 7 live-check.
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(0, 0, ShiftedSection(3f, 2f), ShiftedSection(3f, 2f)));

            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(plan);

            // Only the two strips whose bounds overlap the shifted hull-local range survive;
            // the third clips to a degenerate sliver and is dropped. Area is short of the
            // full 12-per-surface footprint - the documented client gap.
            Assert.Equal(2, AtLevel(panels, 0f).Count());
            double floorArea = AtLevel(panels, 0f).Sum(p => PanelArea(p.LocalVertices));
            Assert.True(floorArea < 12.0 - Tol,
                "the client's mixed-coordinate clip leaves a gap on a laterally shifted hull");
            Assert.Equal(8.0, floorArea, 2);
        }

        // ---- 5. y/z edits: flat accepted within tolerance, slopes rejected -

        [Fact]
        public void A_flat_floor_raised_uniformly_is_still_a_deck()
        {
            // Both floor corners raised the same amount: the quad stays flat (equal y) and
            // is still derived as a deck.
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(0, 0, FloorYSection(3f, 0.5f, 0.5f), FloorYSection(3f, 0.5f, 0.5f)));

            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(plan);

            // Floor still produces its three strips (its entity y is lifted by the edit).
            Assert.Contains(panels, p => p.HullLocalPositionMetres.Y > 0f);
            Assert.True(panels.Count >= 3);
        }

        [Fact]
        public void IsDeck_accepts_within_tolerance_and_rejects_a_slope()
        {
            var a = new ShipVector3(-3, 0.0000f, -1);
            var b = new ShipVector3(3, 0.0005f, -1);   // 0.5 mm: within 0.001
            var c = new ShipVector3(3, 0.0000f, 1);
            var d = new ShipVector3(-3, 0.0005f, 1);
            Assert.True(DeckGenerator.IsDeck(a, b, c, d));

            var sloped = new ShipVector3(3, 0.5f, -1);  // 0.5 m: a slope
            Assert.False(DeckGenerator.IsDeck(a, sloped, c, d));
        }

        [Fact]
        public void A_sloped_floor_is_not_derived_as_a_deck_surface()
        {
            // One corner of the floor raised 1 m: the floor quad is no longer flat, so it
            // is filtered out as a hull side and produces no floor-level panels. The flat
            // top still yields its strips.
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(0, 0, FloorYSection(3f, 0f, 1f), FloorYSection(3f, 0f, 1f)));

            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(plan);

            // No panel sits at the lower floor plane (raw y ~ 0 -> entity y ~ 0); the sloped
            // floor was rejected. Panels that exist are the flat top.
            Assert.DoesNotContain(panels, p => System.Math.Abs(p.HullLocalPositionMetres.Y) < Tol);
            Assert.NotEmpty(panels);
        }

        // ---- 6. stacked cells ---------------------------------------------

        [Fact]
        public void Stacked_cells_emit_one_shared_interface_and_an_exposed_top()
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(Stack(2));

            // Three flat surfaces: bottom floor (y=0), shared interface (raw y=1.7 ->
            // entity y=3.4), exposed top (raw y=3.4 -> entity y=6.8). The lower cell's top
            // is suppressed by its above-connection; the upper cell's floor is the shared
            // interface. Three strips each -> nine panels.
            Assert.Equal(9, panels.Count);

            float[] levels = panels
                .Select(p => (float)System.Math.Round(p.HullLocalPositionMetres.Y, 2))
                .Distinct()
                .OrderBy(y => y)
                .ToArray();
            Assert.Equal(new[] { 0f, 3.4f, 6.8f }, levels);

            // Each surface is a full 12-unit footprint: no doubling at the shared interface.
            Assert.Equal(36.0, TotalArea(panels), 2);
        }

        // ---- 7. subdivision boundary behaviour ----------------------------

        [Fact]
        public void A_narrow_hull_is_a_single_undivided_strip()
        {
            // Half-width 0.5 (1 wide) fits in one band -> one panel per flat surface.
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(Row(1, 0.5f));

            Assert.Equal(2, panels.Count);                 // floor + top
            Assert.Single(AtLevel(panels, 0f));
        }

        [Fact]
        public void A_wide_hull_splits_into_two_metre_bands_with_narrow_edge_strips()
        {
            // Half-width 8 (16 wide): interior strips are 2 wide, the two edge strips 1.
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(Row(1, 8f));

            List<DeckPanel> floor = AtLevel(panels, 0f).ToList();
            Assert.Equal(9, floor.Count);
            Assert.Equal(18, panels.Count);

            float[] widths = floor
                .Select(p => p.LocalVertices.Max(v => v.X) - p.LocalVertices.Min(v => v.X))
                .OrderBy(w => w)
                .ToArray();

            // Two narrow edge strips of ~1, seven interior strips of ~2.
            Assert.Equal(2, widths.Count(w => System.Math.Abs(w - 1f) < Tol));
            Assert.Equal(7, widths.Count(w => System.Math.Abs(w - 2f) < Tol));

            // Area still conserved: 16 wide x 2 deep = 32 per surface.
            Assert.Equal(64.0, TotalArea(panels), 2);

            // Every clipped polygon is a valid loop of 3..5 vertices (ClipX's range).
            Assert.All(panels, p => Assert.InRange(p.LocalVertices.Count, 3, 5));
        }

        // ---- 8. determinism / restore -------------------------------------

        [Fact]
        public void The_same_hull_bytes_regenerate_identical_panels_and_order()
        {
            // Restore decodes the persisted bytes and regenerates: it must reproduce the
            // exact same panel list (count, order, positions, vertices) so the indexed
            // registration keys line up with the same geometry every boot.
            byte[] bytes = Row(3).Encode();

            IReadOnlyList<DeckPanel> a = DeckGenerator.Generate(ShipPlanModel.Decode(bytes));
            IReadOnlyList<DeckPanel> b = DeckGenerator.Generate(ShipPlanModel.Decode(bytes));

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].HullLocalPositionMetres, b[i].HullLocalPositionMetres);
                Assert.Equal(a[i].LocalVertices.Count, b[i].LocalVertices.Count);
                for (int j = 0; j < a[i].LocalVertices.Count; j++)
                {
                    Assert.Equal(a[i].LocalVertices[j], b[i].LocalVertices[j]);
                }
            }
        }

    }
}
