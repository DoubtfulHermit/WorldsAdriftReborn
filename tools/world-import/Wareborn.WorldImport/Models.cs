using System.Collections.ObjectModel;

namespace Wareborn.WorldImport;

public sealed record WAMapPoint(
    string Id,
    double X,
    double Z,
    IReadOnlyList<string> DeclaredSectors);

public sealed record WAMapSector(
    string Id,
    string Region,
    int Tier,
    IReadOnlyList<string> PointIds);

public sealed record WAMapWallSegment(
    string Id,
    string GroupId,
    int Type,
    string StartPointId,
    string EndPointId);

public sealed record WAMapIsland(
    string Id,
    string Name,
    string Author,
    string SectorId,
    int Tier,
    string Type,
    double X,
    double Y,
    double Z,
    IReadOnlyDictionary<string, string> SourceFields);

public sealed record WAMapZone(
    string Id,
    double MapZ,
    double MapX,
    double AngleDegrees,
    double LetterSpacing);

public sealed record WAMapSettings(
    double MinX,
    double MaxX,
    double MinZ,
    double MaxZ,
    double AltitudeDisplayOffset);

public sealed class WAMapWorldReference
{
    public WAMapWorldReference(
        WAMapSettings settings,
        IReadOnlyDictionary<string, WAMapPoint> points,
        IReadOnlyDictionary<string, WAMapSector> sectors,
        IReadOnlyDictionary<string, WAMapWallSegment> walls,
        IReadOnlyDictionary<string, WAMapIsland> islands,
        IReadOnlyDictionary<string, WAMapZone> zones)
    {
        Settings = settings;
        Points = new ReadOnlyDictionary<string, WAMapPoint>(
            new Dictionary<string, WAMapPoint>(points, StringComparer.Ordinal));
        Sectors = new ReadOnlyDictionary<string, WAMapSector>(
            new Dictionary<string, WAMapSector>(sectors, StringComparer.Ordinal));
        Walls = new ReadOnlyDictionary<string, WAMapWallSegment>(
            new Dictionary<string, WAMapWallSegment>(walls, StringComparer.Ordinal));
        Islands = new ReadOnlyDictionary<string, WAMapIsland>(
            new Dictionary<string, WAMapIsland>(islands, StringComparer.Ordinal));
        Zones = new ReadOnlyDictionary<string, WAMapZone>(
            new Dictionary<string, WAMapZone>(zones, StringComparer.Ordinal));
    }

    public WAMapSettings Settings { get; }
    public IReadOnlyDictionary<string, WAMapPoint> Points { get; }
    public IReadOnlyDictionary<string, WAMapSector> Sectors { get; }
    public IReadOnlyDictionary<string, WAMapWallSegment> Walls { get; }
    public IReadOnlyDictionary<string, WAMapIsland> Islands { get; }
    public IReadOnlyDictionary<string, WAMapZone> Zones { get; }
}

public sealed record WAMapImportSummary(
    string SourceKind,
    string SourceRevision,
    int PointCount,
    int SectorCount,
    int WallCount,
    int IslandCount,
    int ZoneLabelCount,
    IReadOnlyDictionary<int, int> WallTypes,
    IReadOnlyDictionary<int, int> IslandTiers,
    IReadOnlyList<string> IslandsOutsideDeclaredSector,
    string CoordinateEvidence,
    string RuntimeUseWarning);
