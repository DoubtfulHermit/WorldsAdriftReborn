using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The ad-hoc <c>coord X Y Z [entityId]</c> teleport - how a ship ferry's
    /// arrival on an island this menu does not name gets reached for testing.
    /// The grammar is the one instruction with more than two words, so it earns
    /// its own tests: that the numbers land as metres, that the optional entity
    /// id still works, and that a coordinate is never mistaken for solid ground.
    /// </summary>
    public class TeleportCoordCommandTests
    {
        private const double Tol = 0.001;

        [Fact]
        public void CoordParsesThreeMetresIntoADestination()
        {
            Assert.True(TeleportPolicy.TryParseCommand("coord 14321.44 -527.0 -4647.4",
                out TeleportCommand command, out string error));
            Assert.Equal(string.Empty, error);

            Assert.Equal(TeleportPolicy.CoordName, command.Destination.Name);
            Assert.InRange(command.Destination.Position.MetresX, 14321.44 - Tol, 14321.44 + Tol);
            Assert.InRange(command.Destination.Position.MetresY, -527.0 - Tol, -527.0 + Tol);
            Assert.InRange(command.Destination.Position.MetresZ, -4647.4 - Tol, -4647.4 + Tol);
            Assert.Null(command.EntityId);
        }

        [Fact]
        public void CoordAcceptsAnEntityId()
        {
            Assert.True(TeleportPolicy.TryParseCommand("coord 100 200 300 7",
                out TeleportCommand command, out string error));
            Assert.Equal(string.Empty, error);
            Assert.Equal(7L, command.EntityId);
        }

        [Fact]
        public void CoordKeywordIsCaseInsensitive()
        {
            Assert.True(TeleportPolicy.TryParseCommand("COORD 1 2 3", out _, out string error));
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void CoordIsNeverGuaranteedGround()
        {
            // A typo'd coordinate must never masquerade as a safe "home"; it is
            // always a fall the operator is warned about.
            Assert.True(TeleportPolicy.TryParseCommand("coord 0 0 0", out TeleportCommand command, out _));
            Assert.False(command.Destination.LandsOnLoadedGround);
        }

        [Fact]
        public void CoordWithTooFewNumbersIsRejectedWithAReason()
        {
            Assert.False(TeleportPolicy.TryParseCommand("coord 1 2", out _, out string error));
            Assert.Contains("coord X Y Z", error);
        }

        [Fact]
        public void CoordWithNonNumericComponentIsRejected()
        {
            Assert.False(TeleportPolicy.TryParseCommand("coord 1 up 3", out _, out string error));
            Assert.NotEqual(string.Empty, error);
        }

        [Fact]
        public void CoordWithBadEntityIdIsRejected()
        {
            Assert.False(TeleportPolicy.TryParseCommand("coord 1 2 3 notanid", out _, out string error));
            Assert.Contains("entity id", error);
        }

        [Fact]
        public void CoordRoundTripsThroughTheFactory()
        {
            TeleportDestination direct = TeleportPolicy.CoordDestination(12.5, -7.25, 3.0);
            Assert.True(TeleportPolicy.TryParseCommand("coord 12.5 -7.25 3.0",
                out TeleportCommand command, out _));
            Assert.Equal(direct.Position, command.Destination.Position);
        }

        [Fact]
        public void NamedDestinationsStillParseUnchanged()
        {
            // The coord branch must not have disturbed the ordinary grammar.
            Assert.True(TeleportPolicy.TryParseCommand("haven 3", out TeleportCommand command, out _));
            Assert.Equal(TeleportPolicy.HavenName, command.Destination.Name);
            Assert.Equal(3L, command.EntityId);
        }

        [Fact]
        public void CoordIsNotAResolvableNamedDestination()
        {
            // It is a keyword, not a menu entry - it must not leak into the named
            // lookup or the SafeDestination search.
            Assert.False(TeleportPolicy.TryResolve(TeleportPolicy.CoordName, out _));
        }
    }
}
