using System.Text;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// One weather wall: the MapFile's own <c>Type</c>, the stroke it is drawn
    /// with, and the words the legend puts next to that stroke.
    /// </summary>
    internal readonly record struct MapWallColours(
        int Type, string Name, string Colour, string LegendLabel, double StrokeWidth);

    /// <summary>
    /// The six weather walls' colours, emitted once for both the stroke on the map
    /// and the swatch in the legend.
    ///
    /// This exists for the same reason <see cref="MapTierPalette"/> does. A wall is
    /// stroked straight across the tier cells, so it has two ways to disappear: it
    /// can match the fill under it, or the legend key can quietly stop matching the
    /// line. Both were live risks - the keys used to be hand-written hexes in the
    /// stylesheet, and only four of the six walls had a key at all.
    ///
    /// One colour moved. Storm Rift was <c>#9b86d8</c>, a pale violet chosen when
    /// Remnants was a deep violet. Remnants is now the lilac <c>#bc9be2</c>, and the
    /// two sat dE00 8.2 apart at full strength - a wall drawn invisibly across the
    /// tier it crosses most. It is now <c>#c04ae8</c>: the same violet family, so an
    /// operator's memory of "Storm Rift is the purple one" still holds, but pushed
    /// far enough in chroma and lightness to read as a discharge rather than as more
    /// Remnants. Against the composited Remnants fill that pair now measures dE00
    /// 16.2, and 15.1 is its closest approach to anything else on the map.
    ///
    /// This is the second wall moved for this reason; Sand Storm went from
    /// <c>#d9b36b</c> to <c>#e8963c</c> when Badlands became gold. The tier palette
    /// is the fixed point and the walls are fitted around it, because the tiers
    /// cover area and the walls are thin lines: the lines are the cheaper thing to
    /// move.
    /// </summary>
    internal static class MapWallPalette
    {
        internal static IReadOnlyList<MapWallColours> All { get; } = new[]
        {
            new MapWallColours(0, "Wind Rift", "#74c9cf", "Wind Rift", 2.5),
            new MapWallColours(1, "Storm Rift", "#c04ae8", "Storm Rift", 2.5),
            new MapWallColours(2, "Typhon", "#d48388", "Typhon", 2.5),
            new MapWallColours(3, "Sand Storm", "#e8963c", "Sand Storm", 2.5),
            new MapWallColours(4, "Ice Storm", "#a9d6ed", "Ice Storm", 2.5),
            new MapWallColours(5, "World End", "#ec8f88", "Haven separator / World End", 3.0),
        };

        /// <summary>The colour a wall <c>Type</c> is drawn with.</summary>
        internal static MapWallColours For(int type)
        {
            foreach (MapWallColours wall in All)
                if (wall.Type == type) return wall;
            throw new ArgumentOutOfRangeException(nameof(type), type,
                "The release MapFile only carries wall types 0-5.");
        }

        /// <summary>Stroke rules and legend swatches, from the one list.</summary>
        internal static string Css()
        {
            StringBuilder css = new();
            foreach (MapWallColours wall in All)
            {
                css.Append(".map-wall.type-").Append(wall.Type)
                   .Append("{stroke:").Append(wall.Colour)
                   .Append(";stroke-width:").Append(
                       wall.StrokeWidth.ToString("0.##",
                           System.Globalization.CultureInfo.InvariantCulture))
                   .Append('}');
                css.Append(".map-swatch.wall-").Append(wall.Type)
                   .Append("{background:").Append(wall.Colour).Append('}');
            }
            return css.ToString();
        }

        /// <summary>
        /// The legend keys for all six walls. Written here rather than in the page
        /// so a wall can never be drawn without a key, which is how Typhon and Ice
        /// Storm came to be on the map but not in the legend.
        /// </summary>
        internal static string LegendHtml()
        {
            StringBuilder html = new();
            foreach (MapWallColours wall in All)
                html.Append("<span><i class=\"map-swatch wall-").Append(wall.Type)
                    .Append("\"></i>").Append(AdminPageEncoding.Escape(wall.LegendLabel))
                    .Append("</span>");
            return html.ToString();
        }
    }

    /// <summary>Escaping for the small fragments the admin palettes emit.</summary>
    internal static class AdminPageEncoding
    {
        internal static string Escape(string value)
        {
            StringBuilder b = new(value.Length + 8);
            foreach (char c in value)
                b.Append(c switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => c.ToString(),
                });
            return b.ToString();
        }
    }
}
