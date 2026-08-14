using System.Text.Json;
using Wareborn.WorldImport;

return Run(args);

static int Run(string[] args)
{
    string? wamap = Value(args, "--wamap");
    string? output = Value(args, "--out");
    string revision = Value(args, "--source-revision") ?? "unknown";
    if (wamap == null || output == null || args.Contains("--help"))
    {
        Console.Error.WriteLine(
            "Usage: Wareborn.WorldImport --wamap /path/to/WAMap --out report.json "
            + "[--source-revision GIT_SHA]");
        return 2;
    }

    try
    {
        WAMapWorldReference world = WAMapImporter.Load(wamap);
        WAMapImportSummary summary = WAMapImporter.Summarize(world, revision);
        string destination = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(summary,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"Validated {summary.PointCount} points, {summary.SectorCount} "
            + $"sectors, {summary.WallCount} walls, {summary.IslandCount} islands and "
            + $"{summary.ZoneLabelCount} zone labels.");
        Console.WriteLine($"Report: {destination}");
        if (summary.IslandsOutsideDeclaredSector.Count != 0)
        {
            Console.WriteLine("Non-fatal source anomaly: "
                + summary.IslandsOutsideDeclaredSector.Count
                + " island(s) lie outside their declared sector polygon.");
        }
        return 0;
    }
    catch (WAMapValidationException exception)
    {
        Console.Error.WriteLine("WAMap validation failed: " + exception.Message);
        return 1;
    }
}

static string? Value(string[] args, string option)
{
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] != option)
        {
            continue;
        }
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }
        return args[i + 1];
    }
    return null;
}
