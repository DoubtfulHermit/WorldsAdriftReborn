using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// PINS the ledger, and above all the two rules that keep this feature from
    /// grounding ships nobody consented to ground:
    ///
    ///   * an UNREGISTERED hull is UNMETERED, not empty - it never runs dry, never
    ///     gates, and reads a full tank;
    ///   * a registered tank starts FULL and re-registration never refills it.
    ///
    /// Both have a live-world consequence, and both are the kind of invisible
    /// per-life state that every reset path forgets unless a test holds it.
    /// </summary>
    public class ShipFuelLedgerTests
    {
        private const long Hull = 5001L;

        private static ShipFuelLedger Registered(double capacity = 250.0)
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Hull, capacity);
            return ledger;
        }

        [Fact]
        public void AnUnknownHullIsUnmeteredAndReadsFull()
        {
            var ledger = new ShipFuelLedger();

            Assert.False(ledger.IsMetered(Hull));
            Assert.False(ledger.IsDry(Hull));
            Assert.Equal(FuelReading.Unmetered, ledger.Read(Hull));
            Assert.Equal(1.0, ledger.Read(Hull).Fraction);
        }

        [Fact]
        public void AnUnknownHullBurnsNothingAndAcceptsNoFuel()
        {
            var ledger = new ShipFuelLedger();
            ledger.SetThrottle(Hull, 1.0);

            Assert.Empty(ledger.Burn(1000.0, 1.0));
            Assert.Equal(0, ledger.Deposit(Hull, 25));
            Assert.False(ledger.IsMetered(Hull));
        }

        [Fact]
        public void ANewlyRegisteredTankIsFull()
        {
            ShipFuelLedger ledger = Registered();

            FuelReading reading = ledger.Read(Hull);
            Assert.Equal(250.0, reading.Capacity);
            Assert.Equal(250.0, reading.Level);
            Assert.False(reading.IsDry);
        }

        [Fact]
        public void ReRegistrationDoesNotRefillABurntTank()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100.0, 1.0);
            double after = ledger.Read(Hull).Level;

            Assert.False(ledger.Register(Hull, 250.0));
            Assert.Equal(after, ledger.Read(Hull).Level);
        }

        [Fact]
        public void ARegisteredHullWithAGarbageCapacityFallsBackRatherThanBreaking()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Hull, double.NaN);

            Assert.Equal(ShipFuelPolicy.DefaultCapacity, ledger.Read(Hull).Capacity);
            Assert.Equal(ShipFuelPolicy.DefaultCapacity, ledger.Read(Hull).Level);
        }

        [Fact]
        public void RegisterAtRestoresASavedLevelClampedIntoTheTank()
        {
            var ledger = new ShipFuelLedger();
            ledger.RegisterAt(Hull, 250.0, 40.0);
            Assert.Equal(40.0, ledger.Read(Hull).Level);

            var over = new ShipFuelLedger();
            over.RegisterAt(Hull, 250.0, 9999.0);
            Assert.Equal(250.0, over.Read(Hull).Level);

            var under = new ShipFuelLedger();
            under.RegisterAt(Hull, 250.0, -5.0);
            Assert.Equal(0.0, under.Read(Hull).Level);
        }

        [Fact]
        public void BurningAtFullThrottleDrainsAtTheTunedRate()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);

            ledger.Burn(10.0, ShipFuelPolicy.DefaultBurnPerSecond);

            Assert.Equal(250.0 - 2.5, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void AParkedShipBurnsNothingHoweverLongItSits()
        {
            ShipFuelLedger ledger = Registered();

            ledger.Burn(100000.0, ShipFuelPolicy.DefaultBurnPerSecond);

            Assert.Equal(250.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void RunningDryIsReportedExactlyOnce()
        {
            ShipFuelLedger ledger = Registered(capacity: 10.0);
            ledger.SetThrottle(Hull, 1.0);

            IReadOnlyList<long> first = ledger.Burn(100.0, 1.0);
            Assert.Equal(new[] { Hull }, first);
            Assert.True(ledger.IsDry(Hull));
            Assert.Equal(0.0, ledger.Read(Hull).Level);

            // Still commanding throttle, still empty - but the transition already
            // fired, and the caller must not cut the helm again every tick.
            Assert.Empty(ledger.Burn(100.0, 1.0));
        }

        [Fact]
        public void ADryTankNeverGoesNegative()
        {
            ShipFuelLedger ledger = Registered(capacity: 25.0);
            ledger.SetThrottle(Hull, 1.0);

            ledger.Burn(10000.0, 5.0);

            Assert.Equal(0.0, ledger.Read(Hull).Level);
            Assert.Equal(0.0, ledger.Read(Hull).Fraction);
        }

        [Fact]
        public void OnlyTheHullsUnderPowerAreReportedDry()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(1L, 10.0);
            ledger.Register(2L, 10.0);
            ledger.SetThrottle(1L, 1.0);

            Assert.Equal(new[] { 1L }, ledger.Burn(100.0, 1.0));
            Assert.False(ledger.IsDry(2L));
        }

        [Fact]
        public void AGarbageThrottleIsTreatedAsIdle()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, double.NaN);

            Assert.Equal(0.0, ledger.ThrottleOf(Hull));
            ledger.Burn(100.0, 1.0);
            Assert.Equal(250.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void ThrottleIsClampedToTheStick()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 50.0);
            Assert.Equal(1.0, ledger.ThrottleOf(Hull));

            ledger.SetThrottle(Hull, -50.0);
            Assert.Equal(-1.0, ledger.ThrottleOf(Hull));
        }

        [Fact]
        public void DepositMovesWhatFitsAndReportsIt()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(40.0, 1.0);            // 250 -> 210

            Assert.Equal(25, ledger.Deposit(Hull, 25));
            Assert.Equal(235.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void DepositIntoAFullTankTakesNothingSoTheCallerKeepsThePlayersFuel()
        {
            ShipFuelLedger ledger = Registered();

            Assert.Equal(0, ledger.Deposit(Hull, 100));
            Assert.Equal(250.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void DepositNeverOverfills()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(10.0, 1.0);            // 250 -> 240

            Assert.Equal(10, ledger.Deposit(Hull, 250));
            Assert.Equal(250.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void UnregisterMakesAHullUnmeteredAgain()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);
            Assert.True(ledger.IsDry(Hull));

            Assert.True(ledger.Unregister(Hull));
            Assert.False(ledger.IsDry(Hull));
            Assert.Equal(FuelReading.Unmetered, ledger.Read(Hull));
            Assert.False(ledger.Unregister(Hull));
        }

        [Fact]
        public void RefillIsTheAdminEscapeHatch()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);

            Assert.True(ledger.Refill(Hull));
            Assert.Equal(250.0, ledger.Read(Hull).Level);
            Assert.False(ledger.Refill(999L));

            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);
            Assert.Equal(1, ledger.RefillAll());
            Assert.Equal(0, ledger.RefillAll());
        }

        [Fact]
        public void AZeroCapacityReadingReadsFullRatherThanDividingByZero()
        {
            var reading = new FuelReading(0.0, 0.0);
            Assert.Equal(1.0, reading.Fraction);
        }
    }
}
