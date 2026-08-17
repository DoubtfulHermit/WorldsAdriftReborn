using System.Globalization;
using System.Text.Json;

namespace Wareborn.WorldImport;

public static class WAMapImporter
{
    public static WAMapWorldReference Load(string wamapRoot)
    {
        if (string.IsNullOrWhiteSpace(wamapRoot))
        {
            throw new WAMapValidationException("A WAMap checkout path is required.");
        }

        string data = Path.Combine(Path.GetFullPath(wamapRoot), "data");
        WAMapSettings settings = LoadSettings(Path.Combine(data, "settings.json"));
        Dictionary<string, WAMapPoint> points = LoadPoints(
            CsvTable.Load(Path.Combine(data, "point_data.csv")));
        Dictionary<string, WAMapSector> sectors = LoadSectors(
            CsvTable.Load(Path.Combine(data, "sector_data.csv")), points);
        Dictionary<string, WAMapWallSegment> walls = LoadWalls(
            CsvTable.Load(Path.Combine(data, "wall_data.csv")), points);
        Dictionary<string, WAMapIsland> islands = LoadIslands(
            CsvTable.Load(Path.Combine(data, "island_data.csv")), sectors);
        Dictionary<string, WAMapZone> zones = LoadZones(
            Path.Combine(data, "zone_data.json"));

        return new WAMapWorldReference(
            settings, points, sectors, walls, islands, zones);
    }

    public static WAMapImportSummary Summarize(
        WAMapWorldReference world,
        string sourceRevision)
    {
        List<string> outside = new();
        foreach (WAMapIsland island in world.Islands.Values.OrderBy(x => x.Id,
                     StringComparer.Ordinal))
        {
            WAMapSector sector = world.Sectors[island.SectorId];
            if (!Contains(sector.PointIds.Select(id => world.Points[id]).ToArray(),
                    island.X, island.Z))
            {
                outside.Add(island.Id);
            }
        }

        return new WAMapImportSummary(
            SourceKind: "Jerodar/WAMap historical closed-beta reference",
            SourceRevision: sourceRevision,
            PointCount: world.Points.Count,
            SectorCount: world.Sectors.Count,
            WallCount: world.Walls.Count,
            IslandCount: world.Islands.Count,
            ZoneLabelCount: world.Zones.Count,
            WallTypes: world.Walls.Values.GroupBy(x => x.Type).OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.Count()),
            IslandTiers: world.Islands.Values.GroupBy(x => x.Tier).OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.Count()),
            IslandsOutsideDeclaredSector: outside,
            CoordinateEvidence: "Jerodar main.js maps CSV X directly to map X, CSV Z "
                + "to map latitude/vertical, and CSV Y to altitude (+display offset only). "
                + "Values are preserved; no Wareborn runtime transform is inferred.",
            RuntimeUseWarning: "Research input only. Do not use these historical "
                + "island rows as production placement. Wareborn's release-era Bossa "
                + "MapFile remains the island-placement source of record.");
    }

    private static WAMapSettings LoadSettings(string path)
    {
        using JsonDocument json = LoadJson(path);
        JsonElement root = json.RootElement;
        return new WAMapSettings(
            RequiredDouble(root, "minX", path),
            RequiredDouble(root, "maxX", path),
            RequiredDouble(root, "minY", path),
            RequiredDouble(root, "maxY", path),
            RequiredDouble(root, "ZtoAltitude", path));
    }

    private static Dictionary<string, WAMapPoint> LoadPoints(CsvTable table)
    {
        int id = table.RequireColumn("ID");
        int x = table.RequireColumn("X");
        int z = table.RequireColumn("Z");
        int? sectors = table.OptionalColumn("Sectors");
        Dictionary<string, WAMapPoint> result = new(StringComparer.Ordinal);
        foreach (CsvTable.Row row in table.Rows)
        {
            string key = Required(row, id, table.Path, "ID");
            WAMapPoint point = new(
                key,
                Number(row, x, table.Path, "X"),
                Number(row, z, table.Path, "Z"),
                sectors == null
                    ? Array.Empty<string>()
                    : row.At(sectors.Value).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            AddUnique(result, key, point, table.Path, row.LineNumber, "point");
        }
        return result;
    }

    private static Dictionary<string, WAMapSector> LoadSectors(
        CsvTable table,
        IReadOnlyDictionary<string, WAMapPoint> points)
    {
        int id = table.RequireColumn("ID");
        int region = table.RequireColumn("Region");
        int tier = table.RequireColumn("Tier");
        int[] corners = table.Headers
            .Select((name, index) => (name: name.Trim(), index))
            .Where(x => x.name.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.index)
            .ToArray();
        if (corners.Length < 3)
        {
            throw new WAMapValidationException(
                $"{table.Path}: sector schema needs at least three P* columns.");
        }

        Dictionary<string, WAMapSector> result = new(StringComparer.Ordinal);
        foreach (CsvTable.Row row in table.Rows)
        {
            string key = Required(row, id, table.Path, "ID");
            string[] pointIds = corners.Select(row.At)
                .Where(value => value.Length != 0).ToArray();
            if (pointIds.Length < 3)
            {
                throw new WAMapValidationException(
                    $"{table.Path}:{row.LineNumber}: sector '{key}' has fewer than three points.");
            }
            foreach (string pointId in pointIds)
            {
                RequireReference(points, pointId, table.Path, row.LineNumber,
                    $"sector '{key}' point");
            }
            WAMapSector sector = new(key, Required(row, region, table.Path, "Region"),
                Integer(row, tier, table.Path, "Tier"), pointIds);
            AddUnique(result, key, sector, table.Path, row.LineNumber, "sector");
        }
        return result;
    }

    private static Dictionary<string, WAMapWallSegment> LoadWalls(
        CsvTable table,
        IReadOnlyDictionary<string, WAMapPoint> points)
    {
        int id = table.RequireColumn("ID");
        int type = table.RequireColumn("Tier");
        int p1 = table.RequireColumn("P1");
        int p2 = table.RequireColumn("P2");
        Dictionary<string, WAMapWallSegment> result = new(StringComparer.Ordinal);
        Dictionary<string, int> segmentCounts = new(StringComparer.Ordinal);
        foreach (CsvTable.Row row in table.Rows)
        {
            string group = Required(row, id, table.Path, "ID");
            int ordinal = segmentCounts.TryGetValue(group, out int previous)
                ? previous + 1
                : 1;
            segmentCounts[group] = ordinal;
            string key = group + ":" + ordinal.ToString("D3", CultureInfo.InvariantCulture);
            string start = Required(row, p1, table.Path, "P1");
            string end = Required(row, p2, table.Path, "P2");
            RequireReference(points, start, table.Path, row.LineNumber,
                $"wall '{group}' start");
            RequireReference(points, end, table.Path, row.LineNumber,
                $"wall '{group}' end");
            WAMapWallSegment wall = new(key, group,
                Integer(row, type, table.Path, "Tier"), start, end);
            result.Add(key, wall);
        }
        return result;
    }

    private static Dictionary<string, WAMapIsland> LoadIslands(
        CsvTable table,
        IReadOnlyDictionary<string, WAMapSector> sectors)
    {
        int id = table.RequireColumn("Id");
        int name = table.RequireColumn("Name");
        int author = table.RequireColumn("Author");
        int sector = table.RequireColumn("Sec");
        int tier = table.RequireColumn("Tier");
        int type = table.RequireColumn("Type");
        int x = table.RequireColumn("X");
        int y = table.RequireColumn("Y");
        int z = table.RequireColumn("Z");
        Dictionary<string, WAMapIsland> result = new(StringComparer.Ordinal);
        foreach (CsvTable.Row row in table.Rows)
        {
            string key = Required(row, id, table.Path, "Id");
            string sectorId = Required(row, sector, table.Path, "Sec");
            RequireReference(sectors, sectorId, table.Path, row.LineNumber,
                $"island '{key}' sector");
            Dictionary<string, string> fields = new(StringComparer.Ordinal);
            for (int i = 0; i < table.Headers.Count; i++)
            {
                fields[table.Headers[i].Trim().TrimStart('\uFEFF')] = row.At(i);
            }
            WAMapIsland island = new(
                key,
                Required(row, name, table.Path, "Name"),
                row.At(author),
                sectorId,
                Integer(row, tier, table.Path, "Tier"),
                Required(row, type, table.Path, "Type"),
                Number(row, x, table.Path, "X"),
                Number(row, y, table.Path, "Y"),
                Number(row, z, table.Path, "Z"),
                fields);
            AddUnique(result, key, island, table.Path, row.LineNumber, "island");
        }
        return result;
    }

    private static Dictionary<string, WAMapZone> LoadZones(string path)
    {
        using JsonDocument json = LoadJson(path);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new WAMapValidationException($"{path}: zone root must be an object.");
        }
        Dictionary<string, WAMapZone> result = new(StringComparer.Ordinal);
        foreach (JsonProperty property in json.RootElement.EnumerateObject())
        {
            JsonElement zone = property.Value;
            if (!zone.TryGetProperty("pos", out JsonElement pos)
                || pos.ValueKind != JsonValueKind.Array
                || pos.GetArrayLength() != 2
                || !pos[0].TryGetDouble(out double mapZ)
                || !pos[1].TryGetDouble(out double mapX))
            {
                throw new WAMapValidationException(
                    $"{path}: zone '{property.Name}' pos must contain exactly two numbers.");
            }
            double angle = RequiredDouble(zone, "angle", path + ":" + property.Name);
            double spacing = RequiredDouble(zone, "spacing", path + ":" + property.Name);
            result.Add(property.Name,
                new WAMapZone(property.Name, mapZ, mapX, angle, spacing));
        }
        return result;
    }

    private static JsonDocument LoadJson(string path)
    {
        if (!File.Exists(path))
        {
            throw new WAMapValidationException($"Required WAMap file is missing: {path}");
        }
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path).TrimStart('\uFEFF'));
        }
        catch (JsonException exception)
        {
            throw new WAMapValidationException($"{path}: invalid JSON: {exception.Message}");
        }
    }

    private static double RequiredDouble(JsonElement parent, string name, string source)
    {
        if (!parent.TryGetProperty(name, out JsonElement value)
            || !value.TryGetDouble(out double number)
            || !double.IsFinite(number))
        {
            throw new WAMapValidationException(
                $"{source}: property '{name}' must be a finite number.");
        }
        return number;
    }

    private static string Required(
        CsvTable.Row row, int column, string path, string name)
    {
        string value = row.At(column);
        if (value.Length == 0)
        {
            throw new WAMapValidationException(
                $"{path}:{row.LineNumber}: '{name}' is required.");
        }
        return value;
    }

    private static double Number(
        CsvTable.Row row, int column, string path, string name)
    {
        string raw = Required(row, column, path, name);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double result) || !double.IsFinite(result))
        {
            throw new WAMapValidationException(
                $"{path}:{row.LineNumber}: '{name}' value '{raw}' is not a finite number.");
        }
        return result;
    }

    private static int Integer(
        CsvTable.Row row, int column, string path, string name)
    {
        string raw = Required(row, column, path, name);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int result))
        {
            throw new WAMapValidationException(
                $"{path}:{row.LineNumber}: '{name}' value '{raw}' is not an integer.");
        }
        return result;
    }

    private static void AddUnique<T>(
        IDictionary<string, T> target,
        string key,
        T value,
        string path,
        int line,
        string kind)
    {
        if (!target.TryAdd(key, value))
        {
            throw new WAMapValidationException(
                $"{path}:{line}: duplicate {kind} id '{key}'.");
        }
    }

    private static void RequireReference<T>(
        IReadOnlyDictionary<string, T> target,
        string key,
        string path,
        int line,
        string description)
    {
        if (!target.ContainsKey(key))
        {
            throw new WAMapValidationException(
                $"{path}:{line}: {description} references missing id '{key}'.");
        }
    }

    private static bool Contains(IReadOnlyList<WAMapPoint> polygon, double x, double z)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            WAMapPoint a = polygon[i];
            WAMapPoint b = polygon[j];
            bool crosses = (a.Z > z) != (b.Z > z)
                && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X;
            if (crosses)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
