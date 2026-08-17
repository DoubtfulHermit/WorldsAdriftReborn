using Xunit;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    public sealed class IslandDistantShellProtocolTests
    {
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("TRUE", true)]
        [InlineData(" yes ", true)]
        public void FeatureIsExplicitlyOptIn(string? value, bool expected)
        {
            Assert.Equal(expected, IslandDistantShellProtocol.EnabledFrom(value!));
        }

        [Fact]
        public void RequestRoundTripsExactFixedPointOrigin()
        {
            var id = new IslandId("the-trades-challenge");
            var origin = new FixedPointPosition(54286560, -791844, -8077469);

            string marker = IslandDistantShellProtocol.Request(id.Value, 254,
                origin.X, origin.Y, origin.Z);

            Assert.True(IslandDistantShellProtocol.TryParseRequest(marker, out var parsed));
            Assert.Equal(id.Value, parsed.IslandId);
            Assert.Equal(254, parsed.EntityId);
            Assert.Equal(origin.X, parsed.X);
            Assert.Equal(origin.Y, parsed.Y);
            Assert.Equal(origin.Z, parsed.Z);
            Assert.False(IslandDistantShellProtocol.TryParseReady(marker, out _));
        }

        [Fact]
        public void ReadyUsesDistinctMarkerAndRoundTrips()
        {
            var id = new IslandId("mental-facility");
            var origin = new FixedPointPosition(34121298, 990124, 34175648);
            string marker = IslandDistantShellProtocol.Ready(id.Value, 255,
                origin.X, origin.Y, origin.Z);

            Assert.True(IslandDistantShellProtocol.TryParseReady(marker, out var parsed));
            Assert.Equal(id.Value, parsed.IslandId);
            Assert.Equal(255, parsed.EntityId);
            Assert.Equal(origin.X, parsed.X);
            Assert.Equal(origin.Y, parsed.Y);
            Assert.Equal(origin.Z, parsed.Z);
            Assert.False(IslandDistantShellProtocol.TryParseRequest(marker, out _));
        }

        [Fact]
        public void ProceduralRequestRoundTripsCompactOutlineWithoutBundleData()
        {
            IslandShellPoint[] outline =
            {
                new(-10.5, -4), new(12, -3.5), new(9.5, 8), new(-8, 7),
            };
            string marker = IslandDistantShellProtocol.ProceduralRequest(
                "release-123", 900, 1, 2, 3, -20, 40, outline);

            Assert.True(IslandDistantShellProtocol.TryParseProceduralRequest(marker, out var parsed));
            Assert.Equal("release-123", parsed.IslandId);
            Assert.Equal(-20, parsed.MinY);
            Assert.Equal(40, parsed.MaxY);
            Assert.Equal(4, parsed.Outline.Length);
            Assert.Equal(-10.5, parsed.Outline[0].X);
            Assert.False(IslandDistantShellProtocol.TryParseRequest(marker, out _));
        }

        [Theory]
        [InlineData("wareborn.island-shell.v1")]
        [InlineData("wareborn.island-shell.v1||254|1|2|3")]
        [InlineData("wareborn.island-shell.v1|island|0|1|2|3")]
        [InlineData("wareborn.island-shell.v1|island|254|x|2|3")]
        [InlineData("wareborn.island-shell.v1|island|254|1|2|3|extra")]
        [InlineData("wareborn.island-shell.v2|island|254|1|2|3")]
        public void MalformedOrUnknownRequestIsRejected(string marker)
        {
            Assert.False(IslandDistantShellProtocol.TryParseRequest(marker, out _));
        }
    }
}
