using System;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// The pressure score. These assertions pin the SHAPE of the formula - product of
    /// three factors, idle is exactly zero, bounded by one, repeatable - not the
    /// rightness of the weights, which nobody has measured.
    /// </summary>
    public class InteractionPressureTests
    {
        private static InteractionEdge Edge(
            InteractionStrength strength,
            InteractionLatencySensitivity latency,
            InteractionActivity activity) =>
            new InteractionEdge(
                new SimulationEntityId("a"), new SimulationEntityId("b"),
                InteractionKind.Containment, strength, latency, activity);

        [Fact]
        public void Pressure_is_the_product_of_its_three_factors() =>
            Assert.Equal(
                0.75 * 0.5 * 0.5,
                InteractionPressure.For(Edge(
                    InteractionStrength.Strong,
                    InteractionLatencySensitivity.Moderate,
                    InteractionActivity.Intermittent)));

        [Fact]
        public void The_strongest_possible_edge_scores_exactly_one() =>
            Assert.Equal(1.0, InteractionPressure.For(Edge(
                InteractionStrength.VeryStrong,
                InteractionLatencySensitivity.VeryHigh,
                InteractionActivity.Active)));

        [Theory]
        [InlineData(InteractionStrength.Weak, InteractionLatencySensitivity.Low)]
        [InlineData(InteractionStrength.VeryStrong, InteractionLatencySensitivity.VeryHigh)]
        public void An_idle_edge_scores_exactly_zero_whatever_else_it_is(
            InteractionStrength strength, InteractionLatencySensitivity latency) =>
            Assert.Equal(0.0, InteractionPressure.For(Edge(strength, latency, InteractionActivity.Idle)));

        [Fact]
        public void Every_possible_edge_lands_inside_zero_to_one()
        {
            foreach (InteractionStrength s in Enum.GetValues(typeof(InteractionStrength)).Cast<InteractionStrength>())
            foreach (InteractionLatencySensitivity l in Enum.GetValues(typeof(InteractionLatencySensitivity)).Cast<InteractionLatencySensitivity>())
            foreach (InteractionActivity a in Enum.GetValues(typeof(InteractionActivity)).Cast<InteractionActivity>())
            {
                double pressure = InteractionPressure.For(Edge(s, l, a));
                Assert.InRange(pressure, 0.0, 1.0);
            }
        }

        [Fact]
        public void Weights_rise_monotonically_with_each_ordinal_step()
        {
            Assert.True(InteractionPressure.WeightOf(InteractionStrength.Weak)
                < InteractionPressure.WeightOf(InteractionStrength.Moderate));
            Assert.True(InteractionPressure.WeightOf(InteractionStrength.Moderate)
                < InteractionPressure.WeightOf(InteractionStrength.Strong));
            Assert.True(InteractionPressure.WeightOf(InteractionStrength.Strong)
                < InteractionPressure.WeightOf(InteractionStrength.VeryStrong));
            Assert.True(InteractionPressure.WeightOf(InteractionLatencySensitivity.Low)
                < InteractionPressure.WeightOf(InteractionLatencySensitivity.VeryHigh));
            Assert.True(InteractionPressure.WeightOf(InteractionActivity.Idle)
                < InteractionPressure.WeightOf(InteractionActivity.Intermittent));
            Assert.True(InteractionPressure.WeightOf(InteractionActivity.Intermittent)
                < InteractionPressure.WeightOf(InteractionActivity.Active));
        }

        [Fact]
        public void A_control_edge_outscores_a_containment_edge_which_outscores_interest()
        {
            double control = InteractionPressure.For(Edge(
                InteractionStrength.VeryStrong, InteractionLatencySensitivity.VeryHigh,
                InteractionActivity.Active));
            double containment = InteractionPressure.For(Edge(
                InteractionStrength.VeryStrong, InteractionLatencySensitivity.High,
                InteractionActivity.Active));
            double interest = InteractionPressure.For(Edge(
                InteractionStrength.Weak, InteractionLatencySensitivity.Low,
                InteractionActivity.Intermittent));

            Assert.True(control > containment);
            Assert.True(containment > interest);
        }

        [Fact]
        public void The_same_edge_scores_the_same_number_every_time()
        {
            InteractionEdge edge = Edge(
                InteractionStrength.Strong, InteractionLatencySensitivity.High,
                InteractionActivity.Intermittent);
            Assert.Equal(InteractionPressure.For(edge), InteractionPressure.For(edge));
            Assert.Equal(edge.Pressure, InteractionPressure.For(edge));
        }

        [Fact]
        public void An_unknown_enum_value_throws_rather_than_scoring_zero() =>
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InteractionPressure.WeightOf((InteractionStrength)99));
    }
}
