using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The crust -> core -> yield staging. Retail paid nothing for breaking the shell
    /// and paid per scrap PIECE freed from the exposed centre; the invariant that
    /// matters most is that a full mine-out still totals exactly the deposit's yield,
    /// and that all of it is obtainable BEFORE the core is destroyed.
    /// </summary>
    public class MetalDepositYieldTests
    {
        private const int Expose = 5;
        private const int Deplete = 10;
        private const int Total = 12;
        private const int Chunks = MetalDepositYield.DefaultChunks;

        private static int[] Schedule(int expose = Expose, int deplete = Deplete,
            int total = Total, int chunks = Chunks)
        {
            return Enumerable.Range(0, deplete + 1)
                .Select(h => MetalDepositYield.UnitsFor(h, expose, deplete, total, chunks))
                .ToArray();
        }

        [Fact]
        public void Retail_says_three_chunks_per_node()
        {
            // Update 31: "The amount of chunks per metal node is now always 3."
            Assert.Equal(3, MetalDepositYield.DefaultChunks);
        }

        [Fact]
        public void Breaking_the_shell_pays_nothing()
        {
            int[] schedule = Schedule();
            for (int hits = 0; hits <= Expose; hits++)
            {
                Assert.Equal(0, schedule[hits]);
            }
        }

        [Fact]
        public void The_whole_yield_is_paid_over_a_full_mine_out()
        {
            Assert.Equal(Total, Schedule().Sum());
        }

        [Fact]
        public void All_of_it_is_obtainable_without_destroying_the_core()
        {
            // Retail's warning is that finishing the node loses what you had not taken.
            // The mirror of that here: a player who stops one shot short has everything,
            // so the depletion shot owes nothing.
            int[] schedule = Schedule();
            Assert.Equal(Total, schedule.Take(Deplete).Sum());
            Assert.Equal(0, schedule[Deplete]);
        }

        [Fact]
        public void The_pieces_come_free_on_distinct_shots_inside_the_exposed_stage()
        {
            var shots = MetalDepositYield.ChunkShots(Expose, Deplete, Chunks);
            Assert.Equal(Chunks, shots.Count);
            Assert.All(shots, s => Assert.InRange(s, Expose + 1, Deplete - 1));
            Assert.Equal(shots.OrderBy(s => s).ToArray(), shots.ToArray());
            Assert.Equal(shots.Distinct().Count(), shots.Count);
            // The last piece lands on the last pre-depletion shot.
            Assert.Equal(Deplete - 1, shots[shots.Count - 1]);
        }

        [Fact]
        public void The_pieces_split_the_yield_exactly()
        {
            var units = MetalDepositYield.ChunkUnits(Total, Chunks);
            Assert.Equal(Chunks, units.Count);
            Assert.Equal(Total, units.Sum());
            Assert.All(units, u => Assert.True(u > 0));
        }

        [Fact]
        public void An_indivisible_yield_still_totals_exactly()
        {
            var units = MetalDepositYield.ChunkUnits(10, 3);
            Assert.Equal(10, units.Sum());
            Assert.Equal(3, units.Count);
            Assert.Equal(10, Schedule(total: 10).Sum());
        }

        [Fact]
        public void A_deposit_with_no_room_for_pieces_pays_on_depletion()
        {
            // Three shots, exposed after two: there is no shot strictly between exposure
            // and depletion, so there is nowhere to put a piece. Nothing may be lost.
            var shots = MetalDepositYield.ChunkShots(2, 3, 3);
            Assert.Empty(shots);

            int[] schedule = Schedule(expose: 2, deplete: 3, total: Total);
            Assert.Equal(Total, schedule.Sum());
            Assert.Equal(Total, schedule[3]);
        }

        [Fact]
        public void A_narrow_exposed_stage_stacks_pieces_without_losing_any()
        {
            // Exposed at 4 of 6: only shot 5 is available, so all three pieces land there.
            int[] schedule = Schedule(expose: 4, deplete: 6, total: 9);
            Assert.Equal(9, schedule.Sum());
            Assert.Equal(9, schedule[5]);
            Assert.Equal(0, schedule[6]);
        }

        [Fact]
        public void Shots_outside_the_deposits_life_pay_nothing()
        {
            Assert.Equal(0, MetalDepositYield.UnitsFor(0, Expose, Deplete, Total, Chunks));
            Assert.Equal(0, MetalDepositYield.UnitsFor(-1, Expose, Deplete, Total, Chunks));
            Assert.Equal(0, MetalDepositYield.UnitsFor(Deplete + 1, Expose, Deplete, Total, Chunks));
        }

        [Fact]
        public void A_zero_yield_deposit_pays_nothing_anywhere()
        {
            Assert.Equal(0, Schedule(total: 0).Sum());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("nonsense")]
        [InlineData("0")]
        [InlineData("-2")]
        public void A_garbled_chunk_count_falls_back_to_three(string? env)
        {
            Assert.Equal(MetalDepositYield.DefaultChunks, MetalDepositYield.Chunks(env));
        }

        [Fact]
        public void A_valid_chunk_count_is_honoured()
        {
            Assert.Equal(1, MetalDepositYield.Chunks("1"));
            Assert.Equal(5, MetalDepositYield.Chunks(" 5 "));
        }

        [Fact]
        public void The_live_deposit_sizing_pays_out_in_full()
        {
            // The numbers the server actually runs with, end to end.
            int expose = MetalDepositExposure.ShotsToExpose(
                MetalDeposits.ShotsToDeplete, MetalDepositExposure.DefaultExposureHealthFraction);

            int[] schedule = Enumerable.Range(0, MetalDeposits.ShotsToDeplete + 1)
                .Select(h => MetalDepositYield.UnitsFor(
                    h, expose, MetalDeposits.ShotsToDeplete, MetalDeposits.YieldUnits, Chunks))
                .ToArray();

            Assert.Equal(MetalDeposits.YieldUnits, schedule.Sum());
            // Nothing before the core is open.
            Assert.Equal(0, schedule.Take(expose + 1).Sum());
        }
    }
}
