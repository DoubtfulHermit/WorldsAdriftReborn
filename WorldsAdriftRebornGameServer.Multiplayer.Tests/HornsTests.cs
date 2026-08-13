using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Pins the horn honk ledger: the 30 s recharge gate (caller-supplied time, no
    /// sleeping), the served charge ramp, and the registration lifecycle.
    /// </summary>
    public class HornsTests
    {
        [Fact]
        public void FreshHornHonksImmediately()
        {
            var horns = new Horns();
            horns.Register(10, 1);
            Assert.True(horns.TryHonk(10, nowSeconds: 100.0));
        }

        [Fact]
        public void HonkWhileRechargingIsRefusedThenAllowedAfterWindow()
        {
            var horns = new Horns();
            horns.Register(10, 1);

            Assert.True(horns.TryHonk(10, 100.0));
            Assert.False(horns.TryHonk(10, 100.0 + Horns.RechargeSeconds - 0.001));
            Assert.True(horns.TryHonk(10, 100.0 + Horns.RechargeSeconds));
        }

        [Fact]
        public void RefusedHonkDoesNotRestartTheWindow()
        {
            var horns = new Horns();
            horns.Register(10, 1);
            horns.TryHonk(10, 100.0);

            // Spamming E mid-recharge must not push the ready time out.
            Assert.False(horns.TryHonk(10, 115.0));
            Assert.True(horns.TryHonk(10, 100.0 + Horns.RechargeSeconds));
        }

        [Fact]
        public void HonkOnUnknownIdReturnsNull()
        {
            var horns = new Horns();
            Assert.Null(horns.TryHonk(99, 100.0));
            Assert.Null(horns.ChargeFor(99, 100.0));
        }

        [Fact]
        public void ChargeIsFullBeforeAnyHonkAndRampsAfter()
        {
            var horns = new Horns();
            horns.Register(10, 1);
            Assert.Equal(1f, horns.ChargeFor(10, 100.0));

            horns.TryHonk(10, 100.0);
            Assert.Equal(0f, horns.ChargeFor(10, 100.0));
            Assert.Equal(0.5f, horns.ChargeFor(10, 100.0 + Horns.RechargeSeconds / 2)!.Value, 3);
            Assert.Equal(1f, horns.ChargeFor(10, 100.0 + Horns.RechargeSeconds));
            Assert.Equal(1f, horns.ChargeFor(10, 100.0 + Horns.RechargeSeconds * 10));
        }

        [Fact]
        public void UnregisterForgetsCooldown()
        {
            var horns = new Horns();
            horns.Register(10, 1);
            horns.TryHonk(10, 100.0);

            Assert.True(horns.Unregister(10));
            Assert.False(horns.IsHorn(10));

            // Re-mounted: fresh horn, fully charged.
            horns.Register(10, 2);
            Assert.True(horns.TryHonk(10, 101.0));
        }
    }
}
