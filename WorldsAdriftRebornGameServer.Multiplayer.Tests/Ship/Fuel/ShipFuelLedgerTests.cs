using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// PINS the ledger, and above all the rules that keep this feature from grounding
    /// ships nobody consented to ground:
    ///
    ///   * a hull with NO GENERATOR is UNMETERED, not empty - it never runs dry, never
    ///     gates, and reads a full tank;
    ///   * a newly registered generator starts FULL and re-registration never refills
    ///     it;
    ///   * the fuel lives in the GENERATOR, so lifting one off takes its contents with
    ///     it and bolting it to another ship brings them along.
    ///
    /// Each has a live-world consequence, and each is the kind of invisible per-life
    /// state that every reset path forgets unless a test holds it.
    ///
    /// Capacities here are written as literals rather than as
    /// <c>ShipFuelPolicy.GeneratorCapacity</c> wherever the arithmetic is the point,
    /// so that retuning the constant cannot silently retune the assertion.
    /// </summary>
    public class ShipFuelLedgerTests
    {
        private const long Hull = 5001L;
        private const long Gen = 7001L;
        private const long Gen2 = 7002L;

        private static ShipFuelLedger Registered(double capacity = 100.0)
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, capacity);
            return ledger;
        }

        [Fact]
        public void AHullWithNoGeneratorIsUnmeteredAndReadsFull()
        {
            var ledger = new ShipFuelLedger();

            Assert.False(ledger.IsMetered(Hull));
            Assert.False(ledger.IsDry(Hull));
            Assert.Equal(0, ledger.GeneratorsOn(Hull));
            Assert.Equal(FuelReading.Unmetered, ledger.Read(Hull));
            Assert.Equal(1.0, ledger.Read(Hull).Fraction);
        }

        [Fact]
        public void AHullWithNoGeneratorBurnsNothingAndAcceptsNoFuel()
        {
            var ledger = new ShipFuelLedger();
            ledger.SetThrottle(Hull, 1.0);

            Assert.Empty(ledger.Burn(1000.0, 1.0));
            Assert.Equal(0, ledger.Deposit(Hull, 25));
            Assert.False(ledger.IsMetered(Hull));
        }

        [Fact]
        public void ANewlyMountedGeneratorIsFull()
        {
            ShipFuelLedger ledger = Registered();

            FuelReading reading = ledger.Read(Hull);
            Assert.Equal(100.0, reading.Capacity);
            Assert.Equal(100.0, reading.Level);
            Assert.False(reading.IsDry);
            Assert.Equal(1, ledger.GeneratorsOn(Hull));
            Assert.Equal(Hull, ledger.HullOf(Gen));
        }

        [Fact]
        public void GeneratorsPoolTheirCapacity()
        {
            // THE WIKI CLAIM, and the whole point of moving the tank onto the part:
            // "multiple generators on one vessel pool their capacity automatically -
            // three generators = 300 units."
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            Assert.Equal(100.0, ledger.Read(Hull).Capacity);

            ledger.Register(Gen2, Hull, 100.0);
            Assert.Equal(200.0, ledger.Read(Hull).Capacity);
            Assert.Equal(200.0, ledger.Read(Hull).Level);

            ledger.Register(7003L, Hull, 100.0);
            Assert.Equal(300.0, ledger.Read(Hull).Capacity);
            Assert.Equal(3, ledger.GeneratorsOn(Hull));

            // ...and one hull, however many generators.
            Assert.Equal(new[] { Hull }, ledger.HullEntityIds);
            Assert.Equal(1, ledger.Count);
        }

        [Fact]
        public void ReRegistrationDoesNotRefillABurntGenerator()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(50.0, 1.0);
            double after = ledger.Read(Hull).Level;

            Assert.False(ledger.Register(Gen, Hull, 100.0));
            Assert.Equal(after, ledger.Read(Hull).Level);
        }

        [Fact]
        public void AGarbageCapacityFallsBackRatherThanBreaking()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, double.NaN);

            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, ledger.Read(Hull).Capacity);
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, ledger.Read(Hull).Level);
        }

        [Fact]
        public void RegisterAtRestoresASavedLevelClampedIntoTheGenerator()
        {
            var ledger = new ShipFuelLedger();
            ledger.RegisterAt(Gen, Hull, 100.0, 40.0);
            Assert.Equal(40.0, ledger.Read(Hull).Level);

            var over = new ShipFuelLedger();
            over.RegisterAt(Gen, Hull, 100.0, 9999.0);
            Assert.Equal(100.0, over.Read(Hull).Level);

            var under = new ShipFuelLedger();
            under.RegisterAt(Gen, Hull, 100.0, -5.0);
            Assert.Equal(0.0, under.Read(Hull).Level);
        }

        [Fact]
        public void BurningAtFullThrottleDrainsAtTheTunedRate()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);

            ledger.Burn(10.0, ShipFuelPolicy.DefaultBurnPerSecond);

            Assert.Equal(100.0 - 2.5, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void TheBurnIsPerSHIPNotPerGeneratorSoASecondOneIsRANGE()
        {
            // Two generators must double the ENDURANCE, not double the thirst. If the
            // burn were applied per generator this would drain twice as fast and
            // bolting one on would buy nothing at all.
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);
            ledger.SetThrottle(Hull, 1.0);

            ledger.Burn(10.0, 1.0);

            Assert.Equal(190.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void ThePoolDrainsInMountOrder()
        {
            // Nothing downstream can see which generator holds what, but a
            // deterministic order is what makes the pool assertable at all - and it is
            // what makes lifting a generator off a predictable amount of fuel.
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);
            ledger.SetThrottle(Hull, 1.0);

            ledger.Burn(120.0, 1.0);

            Assert.Equal(0.0, ledger.ReadGenerator(Gen).Level, 6);
            Assert.Equal(80.0, ledger.ReadGenerator(Gen2).Level, 6);
            Assert.Equal(80.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void AParkedShipBurnsNothingHoweverLongItSits()
        {
            ShipFuelLedger ledger = Registered();

            ledger.Burn(100000.0, ShipFuelPolicy.DefaultBurnPerSecond);

            Assert.Equal(100.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void RunningDryIsReportedExactlyOnce()
        {
            ShipFuelLedger ledger = Registered(capacity: 25.0);
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
        public void AHullIsOnlyDryWhenEVERYGeneratorIs()
        {
            // Emptying the first generator of two must NOT cut the engines. This is
            // the one place the pool could plausibly be got wrong in the punitive
            // direction.
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);
            ledger.SetThrottle(Hull, 1.0);

            Assert.Empty(ledger.Burn(150.0, 1.0));
            Assert.Equal(0.0, ledger.ReadGenerator(Gen).Level, 6);
            Assert.False(ledger.IsDry(Hull));
            Assert.False(ledger.AnyDry);

            Assert.Equal(new[] { Hull }, ledger.Burn(150.0, 1.0));
            Assert.True(ledger.IsDry(Hull));
        }

        [Fact]
        public void ADryPoolNeverGoesNegative()
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
            ledger.Register(Gen, 1L, 10.0);
            ledger.Register(Gen2, 2L, 10.0);
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
            Assert.Equal(100.0, ledger.Read(Hull).Level);
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
            ledger.Burn(40.0, 1.0);            // 100 -> 60

            Assert.Equal(25, ledger.Deposit(Hull, 25));
            Assert.Equal(85.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void DepositFillsTheFirstGeneratorBeforeTheSecond()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(150.0, 1.0);           // first empty, second at 50

            Assert.Equal(120, ledger.Deposit(Hull, 120));
            Assert.Equal(100.0, ledger.ReadGenerator(Gen).Level, 6);
            Assert.Equal(70.0, ledger.ReadGenerator(Gen2).Level, 6);
        }

        [Fact]
        public void DepositIntoAFullPoolTakesNothingSoTheCallerKeepsThePlayersFuel()
        {
            ShipFuelLedger ledger = Registered();

            Assert.Equal(0, ledger.Deposit(Hull, 100));
            Assert.Equal(100.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void DepositNeverOverfills()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(10.0, 1.0);            // 100 -> 90

            Assert.Equal(10, ledger.Deposit(Hull, 250));
            Assert.Equal(100.0, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void WithdrawUndoesADepositWithoutInventingADebt()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(50.0, 1.0);            // 100 -> 50
            Assert.Equal(25, ledger.Deposit(Hull, 25));

            Assert.Equal(25, ledger.Withdraw(Hull, 25));
            Assert.Equal(50.0, ledger.Read(Hull).Level, 6);

            // Never below empty, and never on a hull with no generator.
            Assert.Equal(50, ledger.Withdraw(Hull, 9999));
            Assert.Equal(0.0, ledger.Read(Hull).Level);
            Assert.Equal(0, ledger.Withdraw(Hull, 10));
            Assert.Equal(0, ledger.Withdraw(4242L, 10));
        }

        [Fact]
        public void DepositThenWithdrawConservesTheShipsFuelExactly()
        {
            // The rollback path: the pool accepted and the inventory then refused. The
            // property that matters is CONSERVATION - the same total comes back out,
            // so a failed refuel can neither dupe fuel nor eat it. Which generator the
            // units land in afterwards is deliberately not promised; see Withdraw.
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(150.0, 1.0);           // 0 / 50
            double before = ledger.Read(Hull).Level;
            Assert.Equal(50.0, before, 6);

            int moved = ledger.Deposit(Hull, 80);
            Assert.Equal(80, moved);
            Assert.Equal(before + 80, ledger.Read(Hull).Level, 6);

            Assert.Equal(80, ledger.Withdraw(Hull, moved));
            Assert.Equal(before, ledger.Read(Hull).Level, 6);

            // ...and no generator was pushed outside its own bounds on the way.
            Assert.InRange(ledger.ReadGenerator(Gen).Level, 0.0, 100.0);
            Assert.InRange(ledger.ReadGenerator(Gen2).Level, 0.0, 100.0);
        }

        [Fact]
        public void UnregisteringTheLastGeneratorMakesAHullUnmeteredAgain()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);
            Assert.True(ledger.IsDry(Hull));

            Assert.True(ledger.Unregister(Gen));
            Assert.False(ledger.IsMetered(Hull));
            Assert.False(ledger.IsDry(Hull));
            Assert.Equal(FuelReading.Unmetered, ledger.Read(Hull));
            Assert.False(ledger.Unregister(Gen));
        }

        [Fact]
        public void RemovingOneOfTwoGeneratorsHalvesTheCapacityAndKeepsTheShipMetered()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);

            Assert.True(ledger.Unregister(Gen2));
            Assert.True(ledger.IsMetered(Hull));
            Assert.Equal(1, ledger.GeneratorsOn(Hull));
            Assert.Equal(100.0, ledger.Read(Hull).Capacity);
            Assert.Equal(100.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void RefillIsTheAdminEscapeHatch()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);

            Assert.True(ledger.Refill(Hull));
            Assert.Equal(100.0, ledger.Read(Hull).Level);
            Assert.False(ledger.Refill(999L));

            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100000.0, 1.0);
            Assert.Equal(1, ledger.RefillAll());
            Assert.Equal(0, ledger.RefillAll());
        }

        [Fact]
        public void LiftingTheGeneratorOffIsNotAFreeRefuel()
        {
            // Unregister/Register is what a player does by lifting the generator and
            // bolting it back on. The fuel is INSIDE it, so it comes back exactly as
            // it left - otherwise the refuel errand is optional.
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(80.0, 1.0);            // 100 -> 20
            double before = ledger.Read(Hull).Level;
            Assert.Equal(20.0, before, 6);

            Assert.True(ledger.Unregister(Gen));
            Assert.False(ledger.IsMetered(Hull));

            Assert.True(ledger.Register(Gen, Hull, 100.0));
            Assert.Equal(before, ledger.Read(Hull).Level, 6);
        }

        [Fact]
        public void FuelTravelsWITHTheGeneratorToAnotherShip()
        {
            // The direct consequence of "the generator IS the tank": carry a
            // half-full generator to another hull and the fuel is still in it.
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(60.0, 1.0);            // 100 -> 40
            ledger.Unregister(Gen);

            const long OtherHull = 5002L;
            Assert.True(ledger.Register(Gen, OtherHull, 100.0));

            Assert.Equal(40.0, ledger.Read(OtherHull).Level, 6);
            Assert.Equal(OtherHull, ledger.HullOf(Gen));
            Assert.False(ledger.IsMetered(Hull));
        }

        [Fact]
        public void ADormantGeneratorBurnsNothingAndIsNeverReportedDry()
        {
            ShipFuelLedger ledger = Registered(capacity: 25.0);
            ledger.SetThrottle(Hull, 1.0);
            ledger.Unregister(Gen);

            Assert.Empty(ledger.Burn(10000.0, 1.0));
            Assert.False(ledger.IsDry(Hull));
            Assert.Equal(0, ledger.Count);
            Assert.Empty(ledger.HullEntityIds);
            Assert.Null(ledger.HullOf(Gen));
        }

        [Fact]
        public void AHullThatLosesEveryGeneratorForgetsItsThrottle()
        {
            // Otherwise a ship parked at full ahead, stripped of its generators and
            // later given a new one would inherit stale demand from a physically
            // different propulsion installation.
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Unregister(Gen);

            ledger.Register(Gen2, Hull, 100.0);
            Assert.Equal(0.0, ledger.ThrottleOf(Hull));
            Assert.Empty(ledger.Burn(1000.0, 1.0));
            Assert.Equal(100.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void AnyDryIsTheCheapGateInFrontOfTheThrottleClamp()
        {
            // The clamp runs on up to 20 packets a second per pilot, so it asks this
            // first. A wrong answer here either grounds a fuelled ship or lets a dry
            // one fly - and a dormant generator must never count.
            var ledger = new ShipFuelLedger();
            Assert.False(ledger.AnyDry);

            ledger.Register(Gen, Hull, 10.0);
            Assert.False(ledger.AnyDry);

            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(100.0, 1.0);
            Assert.True(ledger.AnyDry);

            ledger.Unregister(Gen);
            Assert.False(ledger.AnyDry);
        }

        [Fact]
        public void ForgetDropsTheGeneratorEntirely()
        {
            ShipFuelLedger ledger = Registered();
            ledger.SetThrottle(Hull, 1.0);
            ledger.Burn(80.0, 1.0);

            Assert.True(ledger.Forget(Gen));
            Assert.False(ledger.IsMetered(Hull));
            Assert.True(ledger.Register(Gen, Hull, 100.0));
            Assert.Equal(100.0, ledger.Read(Hull).Level);
        }

        [Fact]
        public void ForgetHullDropsEveryGeneratorOnASalvagedShip()
        {
            var ledger = new ShipFuelLedger();
            ledger.Register(Gen, Hull, 100.0);
            ledger.Register(Gen2, Hull, 100.0);

            Assert.Equal(2, ledger.ForgetHull(Hull));
            Assert.False(ledger.IsMetered(Hull));
            Assert.Null(ledger.HullOf(Gen));
            Assert.Equal(0, ledger.ForgetHull(Hull));

            // ...and the fuel really is gone with the ship, not merely dormant.
            Assert.Equal(0, ledger.RefillAll());
        }

        [Fact]
        public void AZeroCapacityReadingReadsFullRatherThanDividingByZero()
        {
            var reading = new FuelReading(0.0, 0.0);
            Assert.Equal(1.0, reading.Fraction);
        }

        [Fact]
        public void UnmeteredReadsOneGeneratorsWorth()
        {
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, FuelReading.Unmetered.Capacity);
            Assert.Equal(ShipFuelPolicy.GeneratorCapacity, FuelReading.Unmetered.Level);
            Assert.False(FuelReading.Unmetered.IsDry);
        }
    }
}
