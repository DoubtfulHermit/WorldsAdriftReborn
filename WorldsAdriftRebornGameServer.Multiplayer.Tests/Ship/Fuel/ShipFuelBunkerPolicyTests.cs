using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Fuel
{
    /// <summary>
    /// The arithmetic of the refuel door that replaced the sky core: how much room a
    /// tank has, and how that room is split across the containers bolted to the hull.
    ///
    /// The invariant worth this file is the one the service leans on: a plan's units
    /// sum to exactly <c>min(free, available)</c> and no entry exceeds its own
    /// container's stock. The drain runs as deposit-then-take, so an over-plan would
    /// take fuel a tank never accepted and an under-plan would leave a ship sitting
    /// next to fuel it will not use.
    /// </summary>
    public class ShipFuelBunkerPolicyTests
    {
        private static ShipFuelBunkerPolicy.Draw D(long id, int units) =>
            new ShipFuelBunkerPolicy.Draw(id, units);

        // ---- FreeUnits ----

        [Theory]
        [InlineData(0.0, 250.0, 250)]
        [InlineData(250.0, 250.0, 0)]
        [InlineData(249.5, 250.0, 0)]   // half a unit short accepts nothing
        [InlineData(248.5, 250.0, 1)]
        [InlineData(100.0, 250.0, 150)]
        public void FreeUnitsFloorsAndNeverGoesNegative(double level, double capacity, int expected)
        {
            Assert.Equal(expected, ShipFuelBunkerPolicy.FreeUnits(level, capacity));
        }

        [Fact]
        public void AnOverfullTankOffersNoRoomRatherThanNegativeRoom()
        {
            // Can only arise from a capacity change under a live tank, but a negative
            // "free" would make Plan hand out a negative take.
            Assert.Equal(0, ShipFuelBunkerPolicy.FreeUnits(300.0, 250.0));
        }

        [Fact]
        public void AnUnmeteredReadingOffersNoRoom()
        {
            Assert.Equal(0, ShipFuelBunkerPolicy.FreeUnits(0.0, 0.0));
        }

        // ---- Plan ----

        [Fact]
        public void NoRoomTakesNothingEvenFromAFullBunker()
        {
            Assert.Empty(ShipFuelBunkerPolicy.Plan(0, new[] { D(1, 500) }));
        }

        [Fact]
        public void NoBunkerTakesNothing()
        {
            Assert.Empty(ShipFuelBunkerPolicy.Plan(100, System.Array.Empty<ShipFuelBunkerPolicy.Draw>()));
            Assert.Empty(ShipFuelBunkerPolicy.Plan(100, null!));
        }

        [Fact]
        public void ASingleContainerGivesOnlyWhatItHolds()
        {
            var plan = ShipFuelBunkerPolicy.Plan(100, new[] { D(7, 25) });

            Assert.Single(plan);
            Assert.Equal(7, plan[0].ContainerEntityId);
            Assert.Equal(25, plan[0].Units);
            Assert.Equal(25, ShipFuelBunkerPolicy.TotalOf(plan));
        }

        [Fact]
        public void ASingleContainerIsCappedByTheTanksRoom()
        {
            var plan = ShipFuelBunkerPolicy.Plan(10, new[] { D(7, 25) });

            Assert.Single(plan);
            Assert.Equal(10, plan[0].Units);
        }

        [Fact]
        public void RoomIsSpentInOrderAcrossContainersAndStopsWhenFull()
        {
            var plan = ShipFuelBunkerPolicy.Plan(60, new[] { D(1, 25), D(2, 25), D(3, 25), D(4, 25) });

            Assert.Equal(3, plan.Count);
            Assert.Equal(new[] { 25, 25, 10 }, plan.Select(p => p.Units).ToArray());
            Assert.Equal(new long[] { 1, 2, 3 }, plan.Select(p => p.ContainerEntityId).ToArray());
            // Container 4 is never touched, so its 1081 is never pushed.
            Assert.Equal(60, ShipFuelBunkerPolicy.TotalOf(plan));
        }

        [Fact]
        public void EmptyContainersAreSkippedRatherThanPlannedAtZero()
        {
            // A zero entry would cost a pointless 1081 push per container per tick.
            var plan = ShipFuelBunkerPolicy.Plan(100, new[] { D(1, 0), D(2, 30), D(3, 0) });

            Assert.Single(plan);
            Assert.Equal(2, plan[0].ContainerEntityId);
            Assert.All(plan, p => Assert.True(p.Units >= 1));
        }

        [Fact]
        public void TheTotalIsExactlyTheMinimumOfRoomAndStock()
        {
            IReadOnlyList<ShipFuelBunkerPolicy.Draw> stock = new[] { D(1, 8), D(2, 8), D(3, 9) };

            // Stock-limited.
            Assert.Equal(25, ShipFuelBunkerPolicy.TotalOf(ShipFuelBunkerPolicy.Plan(250, stock)));
            // Room-limited.
            Assert.Equal(12, ShipFuelBunkerPolicy.TotalOf(ShipFuelBunkerPolicy.Plan(12, stock)));
            // Exactly equal.
            Assert.Equal(25, ShipFuelBunkerPolicy.TotalOf(ShipFuelBunkerPolicy.Plan(25, stock)));
        }

        [Fact]
        public void NoEntryEverExceedsItsOwnContainersStock()
        {
            var stock = new[] { D(1, 3), D(2, 4), D(3, 5) };
            var plan = ShipFuelBunkerPolicy.Plan(100, stock);

            foreach (var draw in plan)
            {
                int held = stock.First(s => s.ContainerEntityId == draw.ContainerEntityId).Units;
                Assert.True(draw.Units <= held,
                    "container " + draw.ContainerEntityId + " was planned for " + draw.Units
                    + " but holds only " + held);
            }
        }

        [Fact]
        public void OneCanisterFillsTheDefaultTankFromEmptyInTenDraws()
        {
            // 25 fuel per canister is the one RECOVERED number in the subsystem, and
            // the 250 capacity is ten of them - so a full tank is exactly ten
            // canisters, and this pins that the two numbers still agree.
            int free = ShipFuelBunkerPolicy.FreeUnits(0.0, 250.0);
            var stock = Enumerable.Range(1, 10).Select(i => D(i, 25)).ToArray();

            var plan = ShipFuelBunkerPolicy.Plan(free, stock);

            Assert.Equal(10, plan.Count);
            Assert.Equal(250, ShipFuelBunkerPolicy.TotalOf(plan));
        }

        // ---- ShouldDraw: the WIRE rule ----

        [Fact]
        public void ABunkerFeedsTheTankOnceACanistersWorthOfRoomExists()
        {
            // 25 = the recovered 8+8+9 canister. Every draw pushes a container's 1081
            // on an entity riding a moving ship, so the threshold is what keeps this
            // feature out of the traffic class that caused the desync spiral.
            Assert.Equal(25, ShipFuelBunkerPolicy.MinimumDrawUnits);

            Assert.False(ShipFuelBunkerPolicy.ShouldDraw(250.0, 250.0));  // full
            Assert.False(ShipFuelBunkerPolicy.ShouldDraw(226.0, 250.0));  // 24 short
            Assert.True(ShipFuelBunkerPolicy.ShouldDraw(225.0, 250.0));   // 25 short
            Assert.True(ShipFuelBunkerPolicy.ShouldDraw(0.0, 250.0));     // empty
        }

        [Fact]
        public void ANearlyFullTankDoesNotPushAContainerEveryFewSeconds()
        {
            // At 0.25 fuel/s a hull opens one unit of room every four seconds. Without
            // the threshold that is a 1081 push per container at ~0.25 Hz for the
            // whole flight; with it, one per canister burned.
            for (int shortBy = 1; shortBy < 25; shortBy++)
            {
                Assert.False(ShipFuelBunkerPolicy.ShouldDraw(250.0 - shortBy, 250.0),
                    "a tank " + shortBy + " unit(s) short must wait rather than push");
            }
        }

        [Fact]
        public void AnEmptyTankCanNeverBeBlockedByTheThreshold()
        {
            // The threshold must not be able to strand a ship: an empty tank has the
            // whole capacity free, which is ten canisters.
            Assert.True(ShipFuelBunkerPolicy.ShouldDraw(0.0, 250.0));
            Assert.True(ShipFuelBunkerPolicy.ShouldDraw(0.0, ShipFuelBunkerPolicy.MinimumDrawUnits));
        }

        [Fact]
        public void AnUnmeteredHullNeverDraws()
        {
            Assert.False(ShipFuelBunkerPolicy.ShouldDraw(0.0, 0.0));
        }

        [Fact]
        public void TotalOfIsZeroForNothing()
        {
            Assert.Equal(0, ShipFuelBunkerPolicy.TotalOf(null!));
            Assert.Equal(0, ShipFuelBunkerPolicy.TotalOf(System.Array.Empty<ShipFuelBunkerPolicy.Draw>()));
        }
    }
}
