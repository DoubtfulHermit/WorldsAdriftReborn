using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The bounded inner drain: how many ENet events one main-loop iteration may
    /// consume, and which of those polls may block.
    ///
    /// The numbers here are load-bearing in both directions. Too small (the old
    /// value was effectively 1) and a backlog can NEVER clear: the moment
    /// per-packet cost exceeds inter-arrival time the queue grows without bound
    /// - the observed live failure that ended in a 73 s peer timeout. Too large
    /// and a flooding client starves the loop's timers (mirror flushes, tree
    /// harvest, teleports) for the whole drain.
    /// </summary>
    public class PollDrainPolicyTests
    {
        // ------------------------------------------------------------------
        // THE BUDGET
        // ------------------------------------------------------------------

        [Fact]
        public void The_default_budget_is_thirty_two()
        {
            // > 1, or a backlog can never clear; small enough that the timers
            // run at least every 32 packets under sustained flood.
            Assert.Equal(32, PollDrainPolicy.DefaultBudget);
        }

        [Fact]
        public void An_unset_environment_value_means_the_default()
        {
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom(null));
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom(""));
        }

        [Fact]
        public void A_garbage_environment_value_means_the_default_not_a_crash()
        {
            // A perf knob must never stop the server booting.
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom("lots"));
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom("3.5"));
        }

        [Fact]
        public void A_valid_environment_value_is_used()
        {
            Assert.Equal(1, PollDrainPolicy.BudgetFrom("1"));
            Assert.Equal(64, PollDrainPolicy.BudgetFrom("64"));
        }

        [Fact]
        public void Zero_and_negative_budgets_fall_back_to_the_default()
        {
            // A budget of zero would drain NOTHING - the server would never read
            // a packet again. Falling back beats honouring a foot-gun.
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom("0"));
            Assert.Equal(PollDrainPolicy.DefaultBudget, PollDrainPolicy.BudgetFrom("-5"));
        }

        [Fact]
        public void Absurd_budgets_are_clamped_not_honoured()
        {
            // 32000 events x anything is a loop that holds the timers off for
            // seconds. The clamp turns a typo into a big-but-sane number.
            Assert.Equal(PollDrainPolicy.MaxBudget, PollDrainPolicy.BudgetFrom("32000"));
            Assert.Equal(PollDrainPolicy.MaxBudget, PollDrainPolicy.BudgetFrom(int.MaxValue.ToString()));
        }

        // ------------------------------------------------------------------
        // THE WAITS
        // ------------------------------------------------------------------

        [Fact]
        public void Only_the_first_poll_of_an_iteration_may_block()
        {
            // First poll keeps the historical 50 ms so an idle server sleeps
            // instead of spinning.
            Assert.Equal(PollDrainPolicy.FirstWaitMs, PollDrainPolicy.WaitMsFor(0));
            Assert.Equal(50, PollDrainPolicy.WaitMsFor(0));
        }

        [Fact]
        public void Every_catch_up_poll_is_zero_wait()
        {
            // Otherwise a budget of 32 could stall one iteration 32 x 50 ms.
            for (int drained = 1; drained < PollDrainPolicy.DefaultBudget; drained++)
            {
                Assert.Equal(0, PollDrainPolicy.WaitMsFor(drained));
            }
        }
    }
}
