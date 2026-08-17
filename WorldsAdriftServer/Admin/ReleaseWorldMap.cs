using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// Read-only, allowlisted projection of Bossa's preserved release MapFile.
    /// Geography is loaded once from the embedded research artifact; live ships
    /// and players remain a separate authoritative game-stats stream.
    /// </summary>
    internal static class ReleaseWorldMap
    {
        private static readonly Lazy<string> Projected = new(BuildProjectedJson);

        internal static string Json => Projected.Value;

        private static string BuildProjectedJson()
        {
            Assembly assembly = typeof(ReleaseWorldMap).Assembly;
            string? resourceName = assembly.GetManifestResourceNames()
                .SingleOrDefault(name => name.EndsWith("wamap-islands.json",
                    StringComparison.Ordinal));
            if (resourceName == null)
                throw new InvalidOperationException("Embedded release world map is missing.");

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Embedded release world map could not be opened.");
            using StreamReader reader = new(stream);
            JObject source = JObject.Parse(reader.ReadToEnd());

            double edge = (double?)source["WorldInfo"]?["WorldEdgeLength"] ?? 0;
            double havenSeparatorX = (double?)source["Haven"]?["xOfVerticalSeparator"] ?? 0;
            if (edge <= 0 || havenSeparatorX <= 0
                || source["Islands"] is not JArray sourceIslands
                || source["Walls"] is not JArray sourceWalls
                || source["Biomes"] is not JArray sourceBiomes)
                throw new InvalidOperationException("Embedded release world map has an invalid shape.");

            JArray islands = new();
            foreach (JObject island in sourceIslands.OfType<JObject>())
            {
                islands.Add(new JObject
                {
                    ["x"] = (double?)island["x"] ?? 0,
                    ["y"] = (double?)island["y"] ?? 0,
                    ["z"] = (double?)island["z"] ?? 0,
                    ["asset"] = (string?)island["Island"] ?? string.Empty,
                    ["haven"] = string.Equals((string?)island["Island"], "1431299145.json",
                        StringComparison.Ordinal),
                });
            }

            JArray biomes = new();
            foreach (JObject biome in sourceBiomes.OfType<JObject>())
            {
                int type = (int?)biome["Type"] ?? 0;
                if (type is < 1 or > 4) continue;
                biomes.Add(new JObject
                {
                    ["x"] = (double?)biome["x"] ?? 0,
                    ["z"] = (double?)biome["z"] ?? 0,
                    ["type"] = type,
                    ["civilization"] = (int?)biome["Civ"] ?? 0,
                    ["district"] = (string?)biome["District"] ?? string.Empty,
                });
            }

            JArray walls = new();
            foreach (JObject wall in sourceWalls.OfType<JObject>())
            {
                int type = (int?)wall["Type"] ?? -1;
                if (type is not (0 or 1 or 2 or 3 or 4 or 5)) continue;
                walls.Add(new JObject
                {
                    ["x1"] = (double?)wall["x1"] ?? 0,
                    ["z1"] = (double?)wall["z1"] ?? 0,
                    ["x2"] = (double?)wall["x2"] ?? 0,
                    ["z2"] = (double?)wall["z2"] ?? 0,
                    ["type"] = type,
                });
            }

            JObject projected = new()
            {
                ["source"] = "preserved-release-mapfile",
                ["worldEdgeLength"] = edge,
                ["havenSeparatorX"] = havenSeparatorX,
                ["islands"] = islands,
                ["biomes"] = biomes,
                ["walls"] = walls,
            };

            using StringWriter output = new();
            using (JsonTextWriter writer = new(output))
            {
                writer.Formatting = Formatting.None;
                writer.StringEscapeHandling = StringEscapeHandling.EscapeHtml;
                projected.WriteTo(writer);
            }
            return output.ToString();
        }
    }
}
