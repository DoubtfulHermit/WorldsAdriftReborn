using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// PINS the fuel numbers and the burn/deposit arithmetic.
    ///
    /// The numbers themselves are WAREBORN TUNING (the burn loop lived on the GSim
    /// and is gone), so what these tests defend is not their rightness but their
    /// SHAPE: burn proportional to absolute throttle, whole-unit deposits, and
    /// hostile input costing nothing rather than refunding fuel or throwing. Every
    /// one of those inputs arrives from a client.
    /// </summary>
    public class ShipFuelPolicyTests
    {
        [Fact]
        public void AGeneratorHoldsTheRECOVERED100AndFourCanistersFillIt()
        {
            // 100 is not tuning. The community record says a standard generator holds
            // 100 units, and the shipped client agrees: FuelGaugeVisualizer starts its
            // needle at SetFuelAmount(0f, 100f) before any server speaks to it. If
            // someone "rounds this up a bit", this is the test that should stop them.
            Assert.Equal(100.0, ShipFuelPolicy.GeneratorCapacity);
            Assert.Equal(25, FuelCanisterYield.TotalFuel);
            Assert.Equal(4.0, ShipFuelPolicy.GeneratorCapacity / FuelCanisterYield.TotalFuel, 6);
        }

        [Fact]
        public void OneGeneratorIsAboutSixAndAHalfMinutesOfFullThrottle()
        {
            double seconds = ShipFuelPolicy.GeneratorCapacity / ShipFuelPolicy.DefaultBurnPerSecond;
            Assert.Equal(400.0, seconds, 6);
        }

        [Fact]
        public void BurnIsProportionalToThrottle()
        {
            double full = ShipFuelPolicy.BurnFor(1.0, 10.0, 0.25);
            double half = ShipFuelPolicy.BurnFor(0.5, 10.0, 0.25);
            Assert.Equal(2.5, full, 6);
            Assert.Equal(full / 2.0, half, 6);
        }

        [Fact]
        public void ReverseCostsTheSameAsForward()
        {
            Assert.Equal(
                ShipFuelPolicy.BurnFor(0.7, 3.0, 0.25),
                ShipFuelPolicy.BurnFor(-0.7, 3.0, 0.25), 6);
        }

        [Fact]
        public void IdlingCostsNothing()
        {
            Assert.Equal(0.0, ShipFuelPolicy.BurnFor(0.0, 60.0, 0.25));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void AGarbageThrottleBurnsNothingRatherThanThrowing(double throttle)
        {
            Assert.Equal(0.0, ShipFuelPolicy.BurnFor(throttle, 1.0, 0.25));
        }

        [Fact]
        public void AThrottleBeyondTheStickIsClampedNotTrusted()
        {
            // A modified client sending throttle 1000 must not empty a tank in a tick.
            Assert.Equal(
                ShipFuelPolicy.BurnFor(1.0, 1.0, 0.25),
                ShipFuelPolicy.BurnFor(1000.0, 1.0, 0.25), 6);
        }

        [Fact]
        public void NegativeOrZeroDurationBurnsNothing()
        {
            Assert.Equal(0.0, ShipFuelPolicy.BurnFor(1.0, 0.0, 0.25));
            Assert.Equal(0.0, ShipFuelPolicy.BurnFor(1.0, -5.0, 0.25));
        }

        [Fact]
        public void DepositTakesOnlyWholeUnitsBecauseFuelIsAnInventoryItem()
        {
            // 3.6 units of room accepts 3: a partial unit could not be taken out of
            // the player's integer stack and would silently vanish.
            Assert.Equal(3, ShipFuelPolicy.DepositRoom(96.4, 100.0, 25));
        }

        [Fact]
        public void DepositIntoAFullTankTakesNothing()
        {
            Assert.Equal(0, ShipFuelPolicy.DepositRoom(100.0, 100.0, 25));
            Assert.Equal(0, ShipFuelPolicy.DepositRoom(200.0, 100.0, 25));
        }

        [Fact]
        public void DepositNeverExceedsWhatIsOffered()
        {
            Assert.Equal(25, ShipFuelPolicy.DepositRoom(0.0, 100.0, 25));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-4)]
        public void DepositRefusesANonPositiveOffer(int offered)
        {
            Assert.Equal(0, ShipFuelPolicy.DepositRoom(0.0, 100.0, offered));
        }

        [Fact]
        public void CapacityAndBurnFallBackAndClamp()
        {
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, ShipFuelPolicy.CapacityFrom(null));
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, ShipFuelPolicy.CapacityFrom("nonsense"));
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, ShipFuelPolicy.CapacityFrom("-3"));
            Assert.Equal(ShipFuelPolicy.MinCapacity, ShipFuelPolicy.CapacityFrom("1"));
            Assert.Equal(ShipFuelPolicy.MaxCapacity, ShipFuelPolicy.CapacityFrom("99999999"));
            Assert.Equal(500.0, ShipFuelPolicy.CapacityFrom("500"));

            Assert.Equal(ShipFuelPolicy.DefaultBurnPerSecond, ShipFuelPolicy.BurnRateFrom(null));
            Assert.Equal(ShipFuelPolicy.MaxBurnPerSecond, ShipFuelPolicy.BurnRateFrom("9999"));
            Assert.Equal(1.5, ShipFuelPolicy.BurnRateFrom("1.5"));
        }

        [Fact]
        public void TheSubsystemAndTheThrustGateAreBothOnByDefault()
        {
            // A fuel level nothing acts on is the defect this subsystem exists to
            // fix, so neither switch may default off.
            Assert.True(ShipFuelPolicy.EnabledFrom(null));
            Assert.True(ShipFuelPolicy.EnabledFrom(""));
            Assert.True(ShipFuelPolicy.EnabledFrom("1"));
            Assert.True(ShipFuelPolicy.GatesThrustFrom(null));
        }

        [Fact]
        public void Track7HullDemandLifecycleIsExplicitOptInWithoutChangingExistingDefaults()
        {
            Assert.True(ShipFuelPolicy.EnabledFrom(null));
            Assert.True(ShipFuelPolicy.GatesThrustFrom(null));

            Assert.False(ShipFuelPolicy.HullDemandLifecycleEnabledFrom(null));
            Assert.False(ShipFuelPolicy.HullDemandLifecycleEnabledFrom(""));
            Assert.False(ShipFuelPolicy.HullDemandLifecycleEnabledFrom("garbage"));
            Assert.False(ShipFuelPolicy.HullDemandLifecycleEnabledFrom("0"));
            Assert.True(ShipFuelPolicy.HullDemandLifecycleEnabledFrom("1"));
            Assert.True(ShipFuelPolicy.HullDemandLifecycleEnabledFrom(" true "));
            Assert.True(ShipFuelPolicy.HullDemandLifecycleEnabledFrom("ON"));
            Assert.True(ShipFuelPolicy.HullDemandLifecycleEnabledFrom("yes"));
        }

        [Fact]
        public void CurrentProductionFuelConfigKeepsTrack7Off()
        {
            // Production deliberately disables the pre-existing thrust gate and does
            // not yet set the new rollout variable. Merging Track 7 must therefore
            // retain legacy burn/input/persistence behavior.
            Assert.True(ShipFuelPolicy.EnabledFrom(null));
            Assert.False(ShipFuelPolicy.GatesThrustFrom("0"));
            Assert.False(ShipFuelPolicy.HullDemandLifecycleEnabledFrom(null));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("false")]
        [InlineData("OFF")]
        [InlineData(" no ")]
        public void TheKillSwitchesAcceptTheUsualSpellings(string env)
        {
            Assert.False(ShipFuelPolicy.EnabledFrom(env));
            Assert.False(ShipFuelPolicy.GatesThrustFrom(env));
        }
    }
}
