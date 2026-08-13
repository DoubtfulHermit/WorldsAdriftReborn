using System;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// THE ORIENTATION REGRESSION SUITE. These assert the plan-axis mapping against
    /// the client's own formulas - <c>acs/ShipSection.GetVertexOffset</c>
    /// (<c>(sectionN - 0.5f) * 2f</c> on Z, <c>deckN * 1.7f</c> on Y, x as the
    /// absolute half-width) and <c>acs/ShipCell.GetMidPoint</c>
    /// (<c>ShipDir.Forward -&gt; +z</c>) - and then against the REAL 60-byte hull
    /// pulled off the live save, so "which axis is the bow" is a failing test rather
    /// than a hex dump nobody can find again.
    /// </summary>
    public class ShipHullMetricsTests
    {
        /// <summary>
        /// The live player's saved hull, byte for byte off the server: two cells at
        /// cellNumber 0 and -1 on deck 0, stock half-width, raked bow and stern.
        /// </summary>
        private const string LiveSavedHullHex =
            "020000000000e80000180000e8008e18008e0000000000ffff0000e80000180000e8"
            + "00001800000000000001e80000180000e8007218007200000000";

        private static byte[] LiveSavedHull()
        {
            string hex = LiveSavedHullHex;
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // ---- the axis mapping itself -------------------------------------------

        /// <summary>
        /// Cell numbers grow toward the BOW along +Z. Adding a cell "forward" in the
        /// editor increments cellNumber (ShipExtruderGizmo.GetDirectionVector maps
        /// ShipDir.Forward to x=+1 of the (cellNumber, deckNumber) key), and that
        /// must show up as a longer keel with the bow further along +Z - never as a
        /// wider beam.
        /// </summary>
        [Fact]
        public void Adding_a_cell_forward_lengthens_the_keel_along_positive_Z()
        {
            var one = ShipPlanModel.MakeDefaultStarterHull();
            ShipHullMetrics before = ShipHullMetrics.Measure(one);

            var two = ShipPlanModel.MakeDefaultStarterHull();
            two.Cells.Add(new ShipCellModel(
                cellNumber: 1, deckNumber: 0,
                front: ShipSectionModel.MakeDefault(), back: null));
            ShipHullMetrics after = ShipHullMetrics.Measure(two);

            Assert.Equal(before.BeamMetres, after.BeamMetres, 4);
            Assert.Equal(before.KeelMetres + 4.0, after.KeelMetres, 4);
            Assert.Equal(before.BowLocalZMetres + 4.0, after.BowLocalZMetres, 4);
            Assert.Equal(before.SternLocalZMetres, after.SternLocalZMetres, 4);
            Assert.Equal(1, after.ForemostCellNumber);
        }

        /// <summary>Astern is the mirror: it extends -Z and leaves the bow where it was.</summary>
        [Fact]
        public void Adding_a_cell_astern_extends_negative_Z_and_leaves_the_bow_alone()
        {
            var one = ShipPlanModel.MakeDefaultStarterHull();
            ShipHullMetrics before = ShipHullMetrics.Measure(one);

            var two = ShipPlanModel.MakeDefaultStarterHull();
            two.Cells.Add(new ShipCellModel(
                cellNumber: -1, deckNumber: 0,
                front: ShipSectionModel.MakeDefault(), back: ShipSectionModel.MakeDefault()));
            ShipHullMetrics after = ShipHullMetrics.Measure(two);

            Assert.Equal(before.BowLocalZMetres, after.BowLocalZMetres, 4);
            Assert.Equal(before.SternLocalZMetres - 4.0, after.SternLocalZMetres, 4);
            Assert.Equal(-1, after.AftmostCellNumber);
        }

        /// <summary>
        /// A vertex's serialised x IS the absolute half-width, so widening a section
        /// grows the BEAM (X) and cannot touch the keel. This is the axis-swap
        /// canary: any transposition of X and Z fails here.
        /// </summary>
        [Fact]
        public void Vertex_x_is_the_beam_and_never_the_keel()
        {
            var plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(
                cellNumber: 0, deckNumber: 0,
                front: ShipSectionModel.MakeDefault(halfWidth: 8f),
                back: ShipSectionModel.MakeDefault(halfWidth: 8f)));

            ShipHullMetrics m = ShipHullMetrics.Measure(plan);
            Assert.Equal(32.0, m.BeamMetres, 4);   // +-8 raw, scale 2
            Assert.Equal(4.0, m.KeelMetres, 4);    // one cell is always 4 m fore-aft
        }

        /// <summary>
        /// Deck numbers land on Y at 1.7 raw each, so a single-deck hull's walkable
        /// plane is 3.4 m up. This is the number the live save's mounted parts sit
        /// on (y = 3.32..3.50), which is the independent proof our hull-local frame
        /// is the same frame the client placed those parts in.
        /// </summary>
        [Fact]
        public void The_deck_plane_of_a_single_deck_hull_is_3_point_4_metres()
        {
            ShipHullMetrics m = ShipHullMetrics.Measure(ShipPlanModel.MakeDefaultStarterHull());
            Assert.Equal(3.4, m.DeckPlaneMetres, 4);
            Assert.Equal(1, m.DeckCount);
        }

        /// <summary>The bow direction is a fact about the format, not a tunable.</summary>
        [Fact]
        public void The_bow_direction_is_hull_local_plus_Z()
        {
            Assert.Equal(0f, ShipHullMetrics.BowDirection.X);
            Assert.Equal(0f, ShipHullMetrics.BowDirection.Y);
            Assert.Equal(1f, ShipHullMetrics.BowDirection.Z);
        }

        // ---- the stock hull is beam-dominant, and that is not a bug -------------

        /// <summary>
        /// A stock cell is 12 m of beam by 4 m of keel, so the starter hull is three
        /// times wider than it is long and its bow is its SHORT axis. This is the
        /// whole explanation for "the ship flies sideways" - it is correct geometry,
        /// not a rotation bug, and the fix is more cells.
        /// </summary>
        [Fact]
        public void The_stock_starter_hull_is_wider_than_it_is_long()
        {
            ShipHullMetrics m = ShipHullMetrics.Measure(ShipPlanModel.MakeDefaultStarterHull());
            Assert.Equal(12.0, m.BeamMetres, 1);
            Assert.Equal(4.0, m.KeelMetres, 4);
            Assert.False(m.KeelIsLongestAxis);
            Assert.Equal(3, m.CellsForKeelToMatchBeam);
        }

        /// <summary>
        /// Three stock cells make the keel match the beam; four make it dominant -
        /// the concrete build advice a player gets for "I want it to fly long-ways".
        /// </summary>
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, true)]
        [InlineData(4, true)]
        public void The_keel_overtakes_the_beam_at_three_stock_cells(int cells, bool keelWins)
        {
            var plan = new ShipPlanModel();
            for (int i = 0; i < cells; i++)
            {
                plan.Cells.Add(new ShipCellModel(
                    cellNumber: i, deckNumber: 0,
                    front: ShipSectionModel.MakeDefault(),
                    back: i == 0 ? ShipSectionModel.MakeDefault() : null));
            }

            ShipHullMetrics m = ShipHullMetrics.Measure(plan);
            Assert.Equal(cells * 4.0, m.KeelMetres, 4);
            Assert.Equal(keelWins, m.KeelIsLongestAxis);
        }

        // ---- the real live hull -------------------------------------------------

        /// <summary>
        /// THE LIVE SHIP, decoded. Two cells, stock width: 12.1 m of beam against
        /// 8 m of keel, deck plane at 3.4 m, bow at local z = +2. Every number here
        /// was reproduced independently by running DeckGenerator over the same bytes
        /// (deck extent X 12.09, Y 0..3.4, Z -6..+2), and all four of the player's
        /// mounted parts land inside that footprint at y = 3.32..3.50.
        /// </summary>
        [Fact]
        public void The_live_saved_hull_measures_12_metres_of_beam_against_8_of_keel()
        {
            Assert.True(ShipPlanModel.TryDecode(LiveSavedHull(), out ShipPlanModel? plan, out string? error), error);
            ShipHullMetrics m = ShipHullMetrics.Measure(plan!);

            Assert.Equal(2, m.CellCount);
            Assert.Equal(1, m.DeckCount);
            Assert.Equal(-1, m.AftmostCellNumber);
            Assert.Equal(0, m.ForemostCellNumber);
            Assert.Equal(12.1, m.BeamMetres, 1);
            Assert.Equal(8.0, m.KeelMetres, 4);
            Assert.Equal(2.0, m.BowLocalZMetres, 4);
            Assert.Equal(-6.0, m.SternLocalZMetres, 4);
            Assert.Equal(3.4, m.DeckPlaneMetres, 4);
            Assert.False(m.KeelIsLongestAxis);
        }

        /// <summary>The live hull still round-trips byte-for-byte; the fixture is not drifting.</summary>
        [Fact]
        public void The_live_saved_hull_round_trips_byte_identically()
        {
            byte[] bytes = LiveSavedHull();
            Assert.Equal(60, bytes.Length);
            Assert.True(ShipPlanModel.TryDecode(bytes, out ShipPlanModel? plan, out string? error), error);
            Assert.Equal(bytes, plan!.Encode());
        }

        /// <summary>
        /// The player's own design put the raked ends on the Z faces: the bowmost
        /// section's BOTTOM vertices sit 1.795 raw astern of its top (an overhanging
        /// prow) and the sternmost section's bottom sits 1.795 raw forward of its
        /// top. Rake on Z is the design's own statement that Z is its fore-aft axis.
        /// </summary>
        [Fact]
        public void The_live_hull_rakes_its_bow_and_stern_on_the_Z_faces()
        {
            Assert.True(ShipPlanModel.TryDecode(LiveSavedHull(), out ShipPlanModel? plan, out _));
            ShipCellModel bowCell = plan!.Cells.Find(c => c.CellNumber == 0)!;
            ShipCellModel sternCell = plan.Cells.Find(c => c.CellNumber == -1)!;

            Assert.Equal(0f, bowCell.Front.Top[0].Z, 3);
            Assert.Equal(-1.795f, bowCell.Front.Bottom[0].Z, 3);
            Assert.NotNull(sternCell.Back);
            Assert.Equal(0f, sternCell.Back!.Top[0].Z, 3);
            Assert.Equal(1.795f, sternCell.Back.Bottom[0].Z, 3);

            // ...and nothing is raked on X: every half-width is the stock value.
            foreach (ShipCellModel cell in plan.Cells)
            {
                Assert.Equal(-3.024f, cell.Front.Top[0].X, 3);
                Assert.Equal(3.024f, cell.Front.Top[1].X, 3);
            }
        }

        /// <summary>
        /// The log line a spawn emits names the axes and flags a beam-dominant hull,
        /// so the next "it flies sideways" report is answerable from the server log.
        /// </summary>
        [Fact]
        public void The_description_names_the_axes_and_flags_a_beam_dominant_hull()
        {
            Assert.True(ShipPlanModel.TryDecode(LiveSavedHull(), out ShipPlanModel? plan, out _));
            string text = ShipHullMetrics.Measure(plan!).Describe();

            Assert.Contains("beam 12.09 m (X)", text);
            Assert.Contains("keel 8 m (Z)", text);
            Assert.Contains("bow at local +Z", text);
            Assert.Contains("BEAM EXCEEDS KEEL", text);
        }

        /// <summary>An empty plan measures as zeroes rather than throwing on a spawn path.</summary>
        [Fact]
        public void An_empty_plan_measures_as_zero()
        {
            ShipHullMetrics m = ShipHullMetrics.Measure(new ShipPlanModel());
            Assert.Equal(0, m.CellCount);
            Assert.Equal(0.0, m.BeamMetres);
            Assert.Equal(0.0, m.KeelMetres);
        }
    }
}
