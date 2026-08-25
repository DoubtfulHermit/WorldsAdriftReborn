using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    public sealed class FlightRuntimeFlagsTests
    {
        [Fact]
        public void Everything_defaults_off_with_no_warnings()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse(null, null, null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.False(flags.LiftRuntimeEnabled);
            Assert.Empty(flags.PromotedHullPersistentIndices);
            Assert.Empty(flags.StartupWarnings);
        }

        [Fact]
        public void Only_the_literal_one_opts_in()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("true", null, "yes",
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.False(flags.LiftRuntimeEnabled);
        }

        [Fact]
        public void Master_without_fixed_step_stays_off_with_one_warning()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", null, null,
                fixedStepEnabled: false, forceModelEnabled: true);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.Single(flags.StartupWarnings);
            Assert.Contains("WAREBORN_FLIGHT_FIXED_STEP", flags.StartupWarnings[0]);
        }

        [Fact]
        public void Master_without_force_model_stays_off_with_one_warning()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", null, null,
                fixedStepEnabled: true, forceModelEnabled: false);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.Single(flags.StartupWarnings);
            Assert.Contains("WAREBORN_FLIGHT_FORCES", flags.StartupWarnings[0]);
        }

        [Fact]
        public void Master_with_prerequisites_enables_observer_phase_with_no_promoted_hull()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", null, null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.True(flags.VectorAuthorityEnabled);
            Assert.Empty(flags.PromotedHullPersistentIndices);
            Assert.False(flags.IsPromoted(3));
            Assert.Empty(flags.StartupWarnings);
        }

        [Fact]
        public void Hull_list_parses_persistent_indices()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", " 3, 17,3 ", null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.True(flags.IsPromoted(3));
            Assert.True(flags.IsPromoted(17));
            Assert.False(flags.IsPromoted(4));
            Assert.False(flags.IsPromoted(null));
            Assert.Equal(2, flags.PromotedHullPersistentIndices.Count);
        }

        [Fact]
        public void Invalid_hull_tokens_are_ignored_with_a_warning_each()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", "3,ship-9,-2", null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.True(flags.IsPromoted(3));
            Assert.Single(flags.PromotedHullPersistentIndices);
            Assert.Equal(2, flags.StartupWarnings.Count);
            Assert.All(flags.StartupWarnings, w => Assert.Contains("ignored", w));
        }

        [Fact]
        public void Hull_list_without_master_promotes_nothing_and_warns()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse(null, "3", null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.Empty(flags.PromotedHullPersistentIndices);
            Assert.Single(flags.StartupWarnings);
        }

        [Fact]
        public void Lift_without_master_stays_off_and_warns()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse(null, null, "1",
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.False(flags.LiftRuntimeEnabled);
            Assert.Single(flags.StartupWarnings);
            Assert.Contains("WAREBORN_FLIGHT_VECTOR_AUTHORITY", flags.StartupWarnings[0]);
        }

        [Fact]
        public void Lift_with_master_missing_prerequisites_stays_off()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", "3", "1",
                fixedStepEnabled: false, forceModelEnabled: true);

            Assert.False(flags.VectorAuthorityEnabled);
            Assert.False(flags.LiftRuntimeEnabled);
            Assert.Empty(flags.PromotedHullPersistentIndices);
        }

        [Fact]
        public void Lift_applies_per_hull_and_only_where_vector_authority_does()
        {
            FlightRuntimeFlags flags = FlightRuntimeFlags.Parse("1", "3", "1",
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.True(flags.LiftRuntimeEnabled);
            Assert.True(flags.LiftRuntimeAppliesTo(3));
            Assert.False(flags.LiftRuntimeAppliesTo(4));
            Assert.False(flags.LiftRuntimeAppliesTo(null));
        }

        [Fact]
        public void Removing_a_hull_index_rolls_that_hull_back_to_scalar()
        {
            FlightRuntimeFlags before = FlightRuntimeFlags.Parse("1", "3,17", null,
                fixedStepEnabled: true, forceModelEnabled: true);
            FlightRuntimeFlags after = FlightRuntimeFlags.Parse("1", "17", null,
                fixedStepEnabled: true, forceModelEnabled: true);

            Assert.True(before.IsPromoted(3));
            Assert.False(after.IsPromoted(3));
            Assert.True(after.IsPromoted(17));
        }

        [Fact]
        public void Disabled_instance_promotes_nothing()
        {
            Assert.False(FlightRuntimeFlags.Disabled.VectorAuthorityEnabled);
            Assert.False(FlightRuntimeFlags.Disabled.LiftRuntimeEnabled);
            Assert.False(FlightRuntimeFlags.Disabled.IsPromoted(0));
            Assert.Empty(FlightRuntimeFlags.Disabled.StartupWarnings.ToList());
        }
    }
}
