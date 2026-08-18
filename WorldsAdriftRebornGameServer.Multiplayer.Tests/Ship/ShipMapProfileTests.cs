using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// THE ELEVATION ON THE CARD IS THE ELEVATION OF THE SHIP.
    ///
    /// <see cref="ShipMapProfile"/> is the side view of the hull the player built,
    /// and the whole claim of the ship card is that both of its views are that hull
    /// rather than a boat drawing. These assert it the same way
    /// <see cref="ShipMapSilhouetteTests"/> asserts the plan view: against the
    /// client's own section formulas, and against the REAL 60-byte hull pulled byte
    /// for byte off the live save.
    ///
    /// The load-bearing assertion is again the one tying the drawing to
    /// <see cref="ShipHullMetrics"/>. The ring's own bounding box must BE the
    /// measured keel, bow and stern along Z, and its top must BE the measured deck
    /// plane - to the millimetre. Those derivations share their constants but not
    /// their arithmetic, so an elevation that had quietly stopped following the hull
    /// would move away from a measurement that had not.
    /// </summary>
    public class ShipMapProfileTests
    {
        /// <summary>
        /// The live player's saved hull, byte for byte off the server: two cells at
        /// cellNumber 0 and -1 on deck 0, stock half-width, raked bow and stern. The
        /// same fixture ShipHullMetricsTests and ShipMapSilhouetteTests use.
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

        // ---- the ring is the hull ----------------------------------------------

        /// <summary>
        /// THE ANCHOR TEST. The elevation's own extents must equal the hull's
        /// measured keel, bow and stern along the keel axis, and its topmost point
        /// must equal the measured deck plane - which for the live hull means 8 m
        /// long, bow at +2, stern at -6, deck at 3.4 - or the card is drawing
        /// something that is not this ship.
        /// </summary>
        [Fact]
        public void The_live_hulls_elevation_spans_exactly_its_measured_keel_and_deck_plane()
        {
            ShipPlanModel plan = LiveSavedHull();
            ShipMapProfile profile = ShipMapProfile.Of(plan);
            ShipHullMetrics metrics = ShipHullMetrics.Measure(plan);

            Assert.False(profile.IsEmpty);
            (double minZ, double maxZ, double minY, double maxY) = Bounds(profile.Outline);

            Assert.Equal(metrics.KeelMetres, maxZ - minZ, 6);
            Assert.Equal(metrics.BowLocalZMetres, maxZ, 6);
            Assert.Equal(metrics.SternLocalZMetres, minZ, 6);
            Assert.Equal(metrics.DeckPlaneMetres, maxY, 6);

            // And the profile's own published extremes agree with its own ring.
            Assert.Equal(maxY, profile.HeadMetres, 6);
            Assert.Equal(minY, profile.FloorMetres, 6);
            Assert.Equal(maxY - minY, profile.HeightMetres, 6);
        }

        /// <summary>
        /// The live hull's exact elevation, to the millimetre: a 3.4 m single deck
        /// standing on a keel 0.82 m long, with the deck overhanging it. Spelled out
        /// as numbers rather than as relations, because "the drawing is the ship" is
        /// a claim about specific metres.
        /// </summary>
        [Fact]
        public void The_live_hull_is_a_single_deck_three_point_four_metres_over_a_short_keel()
        {
            ShipMapProfile profile = ShipMapProfile.Of(LiveSavedHull());

            Assert.Equal(3, profile.SectionCount);
            Assert.Equal(6, profile.Outline.Count);
            Assert.Equal(0.0, profile.FloorMetres, 6);
            Assert.Equal(3.4, profile.HeadMetres, 6);
            Assert.Equal(3.4, profile.HeightMetres, 6);

            ShipDeckLevel deck = Assert.Single(profile.Decks);
            Assert.Equal(0, deck.DeckNumber);
            Assert.Equal(0.0, deck.FloorMetres, 6);
            Assert.Equal(3.4, deck.PlaneMetres, 6);
            Assert.Equal(-6.0, deck.SternZMetres, 6);
            Assert.Equal(2.0, deck.BowZMetres, 6);
        }

        /// <summary>
        /// THE RAKE SURVIVES IN ELEVATION, which is the reason the two edges of the
        /// ring carry their own z instead of sharing one per station. The live hull's
        /// deck overhangs its keel by 3.59 m at each end - the same overhang
        /// ShipMapSilhouetteTests measures in plan - and a ring that had put the top
        /// and bottom of a station at one z would draw this hull with a vertical stem
        /// and a keel as long as its deck.
        /// </summary>
        [Fact]
        public void The_deck_overhangs_the_keel_fore_and_aft_by_the_hulls_own_rake()
        {
            ShipMapProfile profile = ShipMapProfile.Of(LiveSavedHull());
            IReadOnlyList<ShipProfilePoint> ring = profile.Outline;
            int half = ring.Count / 2;

            // Upper edge runs stern to bow, lower edge bow to stern.
            double deckBow = ring[half - 1].Z, deckStern = ring[0].Z;
            double keelBow = ring[half].Z, keelStern = ring[ring.Count - 1].Z;

            Assert.Equal(3.59, deckBow - keelBow, 2);
            Assert.Equal(3.59, keelStern - deckStern, 2);
        }

        /// <summary>
        /// The ring runs along the top from the stern to the bow and back along the
        /// bottom, which is what lets the card emit it as a single SVG subpath
        /// without sorting anything itself.
        /// </summary>
        [Fact]
        public void The_ring_runs_along_the_deck_forward_and_back_along_the_keel()
        {
            ShipMapProfile profile = ShipMapProfile.Of(LiveSavedHull());
            IReadOnlyList<ShipProfilePoint> ring = profile.Outline;
            int half = ring.Count / 2;

            for (int i = 1; i < half; i++)
            {
                Assert.True(ring[i].Z > ring[i - 1].Z, "the upper edge should run toward the bow");
            }
            for (int i = half + 1; i < ring.Count; i++)
            {
                Assert.True(ring[i].Z < ring[i - 1].Z, "the lower edge should run back toward the stern");
            }
            for (int i = 0; i < half; i++)
            {
                Assert.True(ring[i].Y >= ring[ring.Count - 1 - i].Y,
                    "the upper edge should never dip below the lower one at the same station");
            }
        }

        // ---- decks are the levels they are --------------------------------------

        /// <summary>
        /// A second deck raises the elevation by exactly one deck height and appears
        /// as its own level - the plan view cannot show either, which is the whole
        /// reason this view exists.
        /// </summary>
        [Fact]
        public void A_second_deck_is_its_own_level_one_deck_height_higher()
        {
            ShipPlanModel plan = ShipPlanModel.MakeDefaultStarterHull();
            ShipMapProfile single = ShipMapProfile.Of(plan);

            plan.Cells.Add(new ShipCellModel(
                cellNumber: 0, deckNumber: 1,
                front: ShipSectionModel.MakeDefault(), back: ShipSectionModel.MakeDefault()));
            ShipMapProfile stacked = ShipMapProfile.Of(plan);

            double deckHeight = ShipHullMetrics.DeckHeightRaw * ShipHullMetrics.ShipScale;
            Assert.Equal(single.HeightMetres + deckHeight, stacked.HeightMetres, 6);
            Assert.Equal(ShipHullMetrics.Measure(plan).DeckPlaneMetres, stacked.HeadMetres, 6);

            Assert.Equal(2, stacked.Decks.Count);
            Assert.Equal(0, stacked.Decks[0].DeckNumber);
            Assert.Equal(0.0, stacked.Decks[0].FloorMetres, 6);
            Assert.Equal(3.4, stacked.Decks[0].PlaneMetres, 6);
            Assert.Equal(1, stacked.Decks[1].DeckNumber);
            Assert.Equal(3.4, stacked.Decks[1].FloorMetres, 6);
            Assert.Equal(6.8, stacked.Decks[1].PlaneMetres, 6);
        }

        /// <summary>
        /// A deck that covers only part of the ship is drawn only where it is. This
        /// is the case a "deckCount x deck height" drawing gets silently wrong: an
        /// upper deck over the after two cells of a four-cell hull is a poop deck,
        /// and a full-length line would be a claim the hull bytes do not make.
        /// </summary>
        [Fact]
        public void A_partial_upper_deck_spans_only_the_cells_it_covers()
        {
            ShipPlanModel plan = new ShipPlanModel();
            for (int cell = 0; cell < 4; cell++)
            {
                plan.Cells.Add(new ShipCellModel(cell, 0,
                    ShipSectionModel.MakeDefault(), cell == 0 ? ShipSectionModel.MakeDefault() : null));
            }
            // An upper deck over cells 0 and 1 only.
            plan.Cells.Add(new ShipCellModel(0, 1,
                ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()));
            plan.Cells.Add(new ShipCellModel(1, 1, ShipSectionModel.MakeDefault(), null));

            ShipMapProfile profile = ShipMapProfile.Of(plan);
            Assert.Equal(2, profile.Decks.Count);

            // The main deck runs the whole hull: stations 0..4, i.e. -2 m to 14 m.
            Assert.Equal(-2.0, profile.Decks[0].SternZMetres, 6);
            Assert.Equal(14.0, profile.Decks[0].BowZMetres, 6);
            // The upper deck stops where its cells do: stations 0..2, -2 m to 6 m.
            Assert.Equal(-2.0, profile.Decks[1].SternZMetres, 6);
            Assert.Equal(6.0, profile.Decks[1].BowZMetres, 6);
        }

        /// <summary>
        /// The elevation follows the TALLEST thing at each station, so a hull with a
        /// partial upper deck steps up where that deck begins rather than being drawn
        /// at one height throughout.
        /// </summary>
        [Fact]
        public void The_elevation_steps_up_where_an_upper_deck_begins()
        {
            ShipPlanModel plan = new ShipPlanModel();
            plan.Cells.Add(new ShipCellModel(0, 0,
                ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()));
            plan.Cells.Add(new ShipCellModel(1, 0, ShipSectionModel.MakeDefault(), null));
            plan.Cells.Add(new ShipCellModel(1, 1,
                ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()));

            ShipMapProfile profile = ShipMapProfile.Of(plan);
            IReadOnlyList<ShipProfilePoint> ring = profile.Outline;
            int half = ring.Count / 2;

            // Three stations: -1, 1, 3 metres. The upper deck stands on the forward
            // two, so the after station is one deck lower than the other two.
            Assert.Equal(3, half);
            Assert.Equal(3.4, ring[0].Y, 6);
            Assert.Equal(6.8, ring[1].Y, 6);
            Assert.Equal(6.8, ring[2].Y, 6);
        }

        // ---- what the elevation must NOT react to -------------------------------

        /// <summary>
        /// A curve handle bulges the BEAM and nothing else. It offsets x only
        /// (acs/ShipSection.GetCurvePosition), so in the (z, y) plane it lies on the
        /// segment between the vertices it interpolates and can never move the
        /// elevation. Asserted rather than assumed, because "we left the handles out"
        /// is otherwise indistinguishable from "we forgot the handles".
        /// </summary>
        [Fact]
        public void A_curve_handle_widens_the_plan_view_and_leaves_the_elevation_alone()
        {
            ShipPlanModel plain = ShipPlanModel.MakeDefaultStarterHull();
            ShipPlanModel bulged = ShipPlanModel.MakeDefaultStarterHull();
            bulged.Cells[0].Front.Curve[0, 1] = 1.5f;

            Assert.NotEqual(
                ShipHullMetrics.Measure(plain).BeamMetres,
                ShipHullMetrics.Measure(bulged).BeamMetres);

            ShipMapProfile before = ShipMapProfile.Of(plain);
            ShipMapProfile after = ShipMapProfile.Of(bulged);
            Assert.Equal(before.Outline.Count, after.Outline.Count);
            for (int i = 0; i < before.Outline.Count; i++)
            {
                Assert.Equal(before.Outline[i].Z, after.Outline[i].Z, 6);
                Assert.Equal(before.Outline[i].Y, after.Outline[i].Y, 6);
            }
        }

        // ---- totality ------------------------------------------------------------

        /// <summary>
        /// A hull the card cannot draw must still be REPORTABLE. Nothing here may
        /// throw into a stats snapshot: a snapshot that fails to be written takes the
        /// whole operator surface offline, and the cost of an undrawable hull is a
        /// card that says the shape is unavailable.
        /// </summary>
        [Fact]
        public void An_absent_or_empty_plan_is_an_empty_profile_and_never_a_throw()
        {
            Assert.True(ShipMapProfile.Of(null).IsEmpty);
            Assert.Empty(ShipMapProfile.Of(null).Outline);
            Assert.Empty(ShipMapProfile.Of(null).Decks);
            Assert.True(ShipMapProfile.Of(new ShipPlanModel()).IsEmpty);
            Assert.Equal(0, ShipMapProfile.Empty.SectionCount);
            Assert.Equal(0.0, ShipMapProfile.Empty.HeightMetres);
        }

        /// <summary>
        /// The minimum hull the server falls back to is drawable in elevation too,
        /// because it is what a ship whose bytes failed to decode ends up being.
        /// </summary>
        [Fact]
        public void The_minimum_fallback_hull_has_an_elevation()
        {
            Assert.True(ShipPlanModel.TryDecode(
                Convert.FromBase64String(ShipHull.MinimumHullDataBase64),
                out ShipPlanModel? plan, out string? error), error);

            ShipMapProfile profile = ShipMapProfile.Of(plan);
            Assert.False(profile.IsEmpty);
            Assert.Equal(2, profile.SectionCount);
            Assert.Equal(4, profile.Outline.Count);
            Assert.Equal(3.4, profile.HeightMetres, 6);
        }

        private static (double MinZ, double MaxZ, double MinY, double MaxY) Bounds(
            IReadOnlyList<ShipProfilePoint> ring)
        {
            double minZ = double.MaxValue, maxZ = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (ShipProfilePoint p in ring)
            {
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }
            return (minZ, maxZ, minY, maxY);
        }
    }
}
