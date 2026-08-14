using Wareborn.WorldImport;
using Xunit;

namespace Wareborn.WorldImport.Tests;

public sealed class WAMapImporterTests
{
    [Fact]
    public void Valid_reference_parses_quoted_metadata_and_preserves_xyz()
    {
        using Fixture fixture = Fixture.Valid();
        WAMapWorldReference world = WAMapImporter.Load(fixture.Root);

        Assert.Equal(4, world.Points.Count);
        Assert.Single(world.Sectors);
        Assert.Single(world.Walls);
        WAMapIsland island = Assert.Single(world.Islands.Values);
        Assert.Equal(125.5, island.X);
        Assert.Equal(-42.25, island.Y);
        Assert.Equal(750.75, island.Z);
        Assert.Equal("Birch, Elm", island.SourceFields["Trees"]);
        Assert.Empty(WAMapImporter.Summarize(world, "abc123")
            .IslandsOutsideDeclaredSector);
    }

    [Fact]
    public void Duplicate_point_id_fails_loudly()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Append("point_data.csv", "1,100,100,H1\n");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("duplicate point id '1'", error.Message);
    }

    [Fact]
    public void Missing_sector_corner_fails_loudly()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Replace("sector_data.csv", "1,2,3,4", "1,2,99,4");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("references missing id '99'", error.Message);
    }

    [Fact]
    public void Missing_wall_endpoint_fails_loudly()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Replace("wall_data.csv", "W1,2,1,2", "W1,2,1,99");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("wall 'W1' end references missing id '99'", error.Message);
    }

    [Fact]
    public void Repeated_wall_group_creates_distinct_ordered_segments()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Append("wall_data.csv", "W1,2,2,3\n");
        WAMapWorldReference world = WAMapImporter.Load(fixture.Root);

        Assert.Equal(new[] { "W1:001", "W1:002" }, world.Walls.Keys);
        Assert.All(world.Walls.Values, wall => Assert.Equal("W1", wall.GroupId));
    }

    [Fact]
    public void Missing_island_sector_fails_loudly()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Replace("island_data.csv", "Example,Author,H1", "Example,Author,H9");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("island 'H1_01' sector references missing id 'H9'", error.Message);
    }

    [Fact]
    public void Malformed_island_coordinate_fails_loudly()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Replace("island_data.csv", "125.5,-42.25,750.75", "wat,-42.25,750.75");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("'X' value 'wat' is not a finite number", error.Message);
    }

    [Fact]
    public void Zone_position_must_have_exactly_two_numbers()
    {
        using Fixture fixture = Fixture.Valid();
        fixture.Write("zone_data.json",
            "{\"ORION\":{\"pos\":[0],\"angle\":50,\"spacing\":1.6}}");
        WAMapValidationException error = Assert.Throws<WAMapValidationException>(
            () => WAMapImporter.Load(fixture.Root));
        Assert.Contains("pos must contain exactly two numbers", error.Message);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root)
        {
            Root = root;
            Directory.CreateDirectory(Path.Combine(root, "data"));
        }

        public string Root { get; }

        public static Fixture Valid()
        {
            Fixture fixture = new(Path.Combine(Path.GetTempPath(),
                "wareborn-wamap-tests-" + Guid.NewGuid().ToString("N")));
            fixture.Write("settings.json",
                "{\"minX\":-18000,\"maxX\":18000,\"minY\":-18000,"
                + "\"maxY\":18000,\"ZtoAltitude\":2000}");
            fixture.Write("point_data.csv",
                "ID,X,Z,Sectors\n1,0,0,H1\n2,1000,0,H1\n3,1000,1000,H1\n4,0,1000,H1\n");
            fixture.Write("sector_data.csv",
                "ID,Region,Tier,P1,P2,P3,P4,P5\nH1,X1,1,1,2,3,4,\n");
            fixture.Write("wall_data.csv", "ID,Tier,P1,P2\nW1,2,1,2\n");
            fixture.Write("island_data.csv",
                "Id,Name,Author,Sec,Tier,Type,X,Y,Z,Trees\n"
                + "H1_01,Example,Author,H1,1,Saborian,125.5,-42.25,750.75,"
                + "\"Birch, Elm\"\n");
            fixture.Write("zone_data.json",
                "{\"ORION\":{\"pos\":[0,-3000],\"angle\":50,\"spacing\":1.6}}");
            return fixture;
        }

        public void Write(string name, string value) =>
            File.WriteAllText(Path.Combine(Root, "data", name), value);

        public void Append(string name, string value) =>
            File.AppendAllText(Path.Combine(Root, "data", name), value);

        public void Replace(string name, string oldValue, string newValue)
        {
            string path = Path.Combine(Root, "data", name);
            File.WriteAllText(path, File.ReadAllText(path).Replace(
                oldValue, newValue, StringComparison.Ordinal));
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
