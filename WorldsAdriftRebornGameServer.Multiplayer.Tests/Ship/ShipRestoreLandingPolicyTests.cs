using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    public sealed class ShipRestoreLandingPolicyTests
    {
        [Fact]
        public void Live_crowded_logout_point_moves_to_nearest_clear_panel()
        {
            FixedPointPosition requested = FixedPointPosition.FromMetres(-0.318, 3.565, -0.505);
            FixedPointPosition[] decks =
            {
                FixedPointPosition.FromMetres(0, 3.4, 0),
                FixedPointPosition.FromMetres(-2.638, 3.4, -4),
                FixedPointPosition.FromMetres(0, 3.4, -4),
                FixedPointPosition.FromMetres(2.638, 3.4, -4),
            };
            FixedPointPosition[] parts =
            {
                FixedPointPosition.FromMetres(0.079, 3.32, 0.782), // sky core by saved point
                FixedPointPosition.FromMetres(-2.761, 3.48, -0.362),
                FixedPointPosition.FromMetres(1.384, 3.48, -4.865),
            };

            Assert.True(ShipRestoreLandingPolicy.TryChooseLocal(
                requested, decks, parts, out FixedPointPosition landing));
            Assert.True(decks[0].X != landing.X || decks[0].Z != landing.Z);
            Assert.Equal(3.4 + ShipRestoreLandingPolicy.FootClearanceMetres,
                landing.MetresY, 3);
            Assert.True(parts.All(part =>
                part.MetresY < 2.4 || part.MetresY > 5.9
                || Math.Pow(part.MetresX - landing.MetresX, 2)
                    + Math.Pow(part.MetresZ - landing.MetresZ, 2)
                    >= Math.Pow(ShipRestoreLandingPolicy.MountedPartHorizontalClearanceMetres, 2)));
        }

        [Fact]
        public void Invalid_or_unbounded_inputs_fail_closed()
        {
            Assert.False(ShipRestoreLandingPolicy.TryChooseLocal(default,
                Array.Empty<FixedPointPosition>(), Array.Empty<FixedPointPosition>(), out _));
            Assert.False(ShipRestoreLandingPolicy.TryChooseLocal(default,
                Enumerable.Repeat(default(FixedPointPosition),
                    ShipRestoreLandingPolicy.MaxDecks + 1).ToArray(),
                Array.Empty<FixedPointPosition>(), out _));
        }

        [Fact]
        public void Every_crowded_panel_uses_the_least_obstructed_one()
        {
            FixedPointPosition[] decks =
            {
                FixedPointPosition.FromMetres(0, 3.4, 0),
                FixedPointPosition.FromMetres(4, 3.4, 0),
            };
            FixedPointPosition[] parts =
            {
                FixedPointPosition.FromMetres(0.1, 3.4, 0),
                FixedPointPosition.FromMetres(4.9, 3.4, 0),
            };

            Assert.True(ShipRestoreLandingPolicy.TryChooseLocal(default, decks, parts,
                out FixedPointPosition landing));
            Assert.Equal(4, landing.MetresX, 3);
        }
    }
}
