using System.Text;
using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// One layer: what it will accept, and the exact strings it writes.
    ///
    /// THE STRINGS ARE THE POINT. A layer is drawn three times - by the server's
    /// rasteriser, by the server's vector export and by the browser while you drag
    /// it - and the way that normally breaks is not a wrong formula but a number
    /// one of them wrote as <c>0.4823529411764706</c> and another as
    /// <c>0.482</c>. So every assertion below about a transform compares the WHOLE
    /// string, not a parsed value.
    /// </summary>
    public class EmblemLayerTests
    {
        private static EmblemLayer Layer(
            int obj = 0, int x = 0, int y = 0, int size = 500, int rotation = 0,
            int colour = 0, int opacity = EmblemLayer.OpacitySteps,
            bool flipX = false, bool flipY = false, bool locked = false)
        {
            Assert.True(EmblemLayer.TryCreate(
                obj, x, y, size, rotation, colour, opacity, flipX, flipY, locked,
                out EmblemLayer layer));
            return layer;
        }

        // ---------------------------------------------------------- what it takes

        [Fact]
        public void A_layer_at_every_extreme_the_vocabulary_allows_is_accepted()
        {
            Layer(x: -EmblemLayer.MaxOffset, y: EmblemLayer.MaxOffset,
                size: EmblemLayer.MinSize, rotation: 0, colour: 0, opacity: 0);

            Layer(obj: EmblemObjects.Count - 1,
                x: EmblemLayer.MaxOffset, y: -EmblemLayer.MaxOffset,
                size: EmblemLayer.MaxSize, rotation: EmblemLayer.RotationSteps - 1,
                colour: EmblemVocabulary.ColourCount - 1, opacity: EmblemLayer.OpacitySteps,
                flipX: true, flipY: true, locked: true);
        }

        [Theory]
        // Out of range is a REFUSAL rather than a clamp: these arrive from a form,
        // and quietly substituting a legal value draws a picture nobody composed.
        [InlineData(-1, 0, 0, 500, 0, 0, 40)]                      // no such object
        [InlineData(0, -2001, 0, 500, 0, 0, 40)]                   // too far left
        [InlineData(0, 0, 2001, 500, 0, 0, 40)]                    // too far down
        [InlineData(0, 0, 0, 0, 0, 0, 40)]                         // no size at all
        [InlineData(0, 0, 0, 9, 0, 0, 40)]                         // under the floor
        [InlineData(0, 0, 0, 2001, 0, 0, 40)]                      // over the ceiling
        [InlineData(0, 0, 0, 500, -1, 0, 40)]                      // negative turn
        [InlineData(0, 0, 0, 500, 360, 0, 40)]                     // a turn is 0..359
        [InlineData(0, 0, 0, 500, 0, -1, 40)]                      // no such colour
        [InlineData(0, 0, 0, 500, 0, 900, 40)]                     // far past the palette
        [InlineData(0, 0, 0, 500, 0, 0, 41)]                       // past full
        [InlineData(0, 0, 0, 500, 0, 0, -1)]                       // below empty
        public void Anything_outside_the_vocabulary_is_refused(
            int obj, int x, int y, int size, int rotation, int colour, int opacity)
        {
            Assert.False(EmblemLayer.TryCreate(
                obj, x, y, size, rotation, colour, opacity, false, false, false, out _));
        }

        [Fact]
        public void An_object_index_past_the_catalogue_is_refused()
        {
            Assert.False(EmblemLayer.TryCreate(
                EmblemObjects.Count, 0, 0, 500, 0, 0, 40, false, false, false, out _));
        }

        // --------------------------------------------------------- the numbers

        [Theory]
        [InlineData(0, "0.000")]
        [InlineData(1, "0.001")]
        [InlineData(9, "0.009")]
        [InlineData(10, "0.010")]
        [InlineData(99, "0.099")]
        [InlineData(100, "0.100")]
        [InlineData(482, "0.482")]
        [InlineData(1000, "1.000")]
        [InlineData(2000, "2.000")]
        [InlineData(-482, "-0.482")]
        [InlineData(-1, "-0.001")]
        [InlineData(-2000, "-2.000")]
        public void A_thousandth_is_written_the_same_way_every_time(int value, string expected)
        {
            Assert.Equal(expected, EmblemLayer.Thousandths(value));
        }

        /// <summary>
        /// The transform, whole. Written out here rather than assembled from the
        /// same parts the production code uses, because a test that builds the
        /// expected string the way the code does cannot notice the code changing.
        /// </summary>
        [Fact]
        public void The_transform_is_translate_then_rotate_then_scale()
        {
            Assert.Equal(
                "translate(0 0) rotate(0) scale(0.500 0.500)",
                Layer().Transform());

            Assert.Equal(
                "translate(-250 375) rotate(37) scale(0.820 0.820)",
                Layer(x: -250, y: 375, size: 820, rotation: 37).Transform());
        }

        /// <summary>
        /// A FLIP IS THE SIGN OF THE SCALE, not a separate transform. That is what
        /// lets the rasteriser undo it with one division, and what keeps the
        /// transform to one term the browser can build the same way.
        /// </summary>
        [Fact]
        public void A_flip_is_carried_as_a_negative_scale()
        {
            Assert.Equal("translate(0 0) rotate(0) scale(-0.500 0.500)",
                Layer(flipX: true).Transform());

            Assert.Equal("translate(0 0) rotate(0) scale(0.500 -0.500)",
                Layer(flipY: true).Transform());

            Assert.Equal("translate(0 0) rotate(0) scale(-0.500 -0.500)",
                Layer(flipX: true, flipY: true).Transform());
        }

        [Theory]
        [InlineData(0, "0.000")]
        [InlineData(1, "0.025")]
        [InlineData(20, "0.500")]
        [InlineData(39, "0.975")]
        [InlineData(40, "1.000")]
        public void Opacity_is_a_whole_number_of_thousandths_at_every_step(int steps, string expected)
        {
            Assert.Equal(expected, Layer(opacity: steps).FillOpacity());
        }

        /// <summary>
        /// Full opacity is EXACTLY one, not 0.999. A stack whose top layer is
        /// nearly opaque never lets the rasteriser's early-out fire, and a crest
        /// nobody asked to be translucent should composite as a flat fill.
        /// </summary>
        [Fact]
        public void Full_opacity_is_exactly_one()
        {
            Assert.Equal(1.0, Layer(opacity: EmblemLayer.OpacitySteps).Alpha);
            Assert.Equal("1.000", Layer(opacity: EmblemLayer.OpacitySteps).FillOpacity());
        }

        [Fact]
        public void The_three_booleans_are_the_three_bits_the_code_carries()
        {
            Assert.Equal(0, Layer().Flags);
            Assert.Equal(EmblemLayer.FlipXBit, Layer(flipX: true).Flags);
            Assert.Equal(EmblemLayer.FlipYBit, Layer(flipY: true).Flags);
            Assert.Equal(EmblemLayer.LockedBit, Layer(locked: true).Flags);
            Assert.Equal(7, Layer(flipX: true, flipY: true, locked: true).Flags);
        }

        [Fact]
        public void The_geometry_helpers_agree_with_the_integers_they_come_from()
        {
            EmblemLayer layer = Layer(x: -500, y: 250, size: 750, rotation: 90);

            Assert.Equal(-0.5, layer.CentreX);
            Assert.Equal(0.25, layer.CentreY);
            Assert.Equal(0.75, layer.Scale);
            Assert.Equal(Math.PI / 2, layer.Radians, 12);
        }

        [Fact]
        public void The_appended_transform_and_the_returned_one_are_the_same_string()
        {
            EmblemLayer layer = Layer(x: 11, y: -22, size: 1234, rotation: 359, flipY: true);

            StringBuilder text = new StringBuilder();
            layer.AppendTransform(text);

            Assert.Equal(layer.Transform(), text.ToString());
        }
    }
}
