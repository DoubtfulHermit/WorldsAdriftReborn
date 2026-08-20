using System;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation;
using WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Simulation
{
    /// <summary>
    /// ACCEPTANCE CRITERION 6: "disabling the observer produces identical gameplay
    /// and network behaviour."
    ///
    /// <para>
    /// That is the criterion most likely to rot silently, so it is guarded
    /// STRUCTURALLY rather than by inspection. The observation supplier is the only
    /// channel from this subsystem to live server state - it is what reads the ship
    /// registry, the aboard tracker, the interest services and the player registry.
    /// If a disabled runtime never invokes it, a disabled runtime cannot have read,
    /// and therefore cannot have perturbed, anything. The counter below is that
    /// proof: delete the flag check in Poll or Refresh and these go red.
    /// </para>
    /// </summary>
    public class SimulationShadowRuntimeTests
    {
        private sealed class CountingObserver
        {
            public int Calls { get; private set; }
            public WarebornWorldObservation Observe()
            {
                Calls++;
                return new WarebornWorldObservation(
                    new[] { new ObservedIsland("haven", new long[] { 1, 2 }) }, null, null);
            }
        }

        [Fact]
        public void A_disabled_runtime_never_reads_the_world()
        {
            CountingObserver observer = new CountingObserver();
            var runtime = new SimulationShadowRuntime(false, observer.Observe);

            for (int second = 0; second < 120; second++)
            {
                Assert.False(runtime.Poll(TimeSpan.FromSeconds(second)));
            }

            Assert.Equal(0, observer.Calls);
            Assert.Equal(0, runtime.RefreshCount);
            Assert.Null(runtime.LatestSnapshot);
            Assert.Empty(runtime.LatestDiagnostics);
            Assert.False(runtime.Enabled);
        }

        [Fact]
        public void A_disabled_runtime_ignores_even_an_explicit_refresh_command()
        {
            CountingObserver observer = new CountingObserver();
            var runtime = new SimulationShadowRuntime(false, observer.Observe);

            Assert.False(runtime.Refresh(TimeSpan.Zero));

            Assert.Equal(0, observer.Calls);
            Assert.Null(runtime.LatestSnapshot);
        }

        [Fact]
        public void A_runtime_with_no_supplier_is_disabled_however_it_was_flagged()
        {
            var runtime = new SimulationShadowRuntime(true, null);
            Assert.False(runtime.Enabled);
            Assert.False(runtime.Poll(TimeSpan.FromHours(1)));
            Assert.Null(runtime.LatestSnapshot);
            Assert.False(SimulationShadowRuntime.Disabled.Enabled);
        }

        [Fact]
        public void An_enabled_runtime_reads_the_world_once_per_interval_and_no_more()
        {
            CountingObserver observer = new CountingObserver();
            var runtime = new SimulationShadowRuntime(
                true, observer.Observe, TimeSpan.FromSeconds(5));

            // First call warms up immediately; the next four seconds are silent.
            Assert.True(runtime.Poll(TimeSpan.Zero));
            for (int ms = 100; ms < 5000; ms += 100)
                Assert.False(runtime.Poll(TimeSpan.FromMilliseconds(ms)));
            Assert.True(runtime.Poll(TimeSpan.FromSeconds(5)));

            Assert.Equal(2, observer.Calls);
            Assert.Equal(2, runtime.RefreshCount);
        }

        [Fact]
        public void A_poll_loop_running_at_full_rate_still_only_refreshes_on_cadence()
        {
            CountingObserver observer = new CountingObserver();
            var runtime = new SimulationShadowRuntime(
                true, observer.Observe, TimeSpan.FromSeconds(5));

            // 60 s of a 100 Hz loop.
            for (int tick = 0; tick <= 6000; tick++)
                runtime.Poll(TimeSpan.FromMilliseconds(tick * 10));

            Assert.Equal(13, observer.Calls);
        }

        [Fact]
        public void A_refreshed_runtime_publishes_a_snapshot_and_its_diagnostics()
        {
            CountingObserver observer = new CountingObserver();
            var runtime = new SimulationShadowRuntime(true, observer.Observe);

            Assert.True(runtime.Poll(TimeSpan.Zero));

            Assert.True(runtime.LatestSnapshot.HasValue);
            WorldSnapshot snapshot = runtime.LatestSnapshot!.Value;
            Assert.Equal(1, snapshot.DomainCount);
            Assert.Equal("[sim] domains=1 entities=3 interactions=0", runtime.LatestDiagnostics[0]);
            Assert.Null(runtime.LastError);
        }

        [Fact]
        public void A_faulting_observer_parks_itself_instead_of_throwing_into_the_poll_loop()
        {
            var runtime = new SimulationShadowRuntime(
                true,
                () => throw new InvalidOperationException("the registry moved under us"),
                TimeSpan.FromSeconds(5));

            Assert.False(runtime.Poll(TimeSpan.Zero));

            Assert.Null(runtime.LatestSnapshot);
            Assert.Equal(0, runtime.RefreshCount);
            Assert.Contains("the registry moved under us", runtime.LastError);
        }

        [Fact]
        public void A_null_observation_is_treated_as_an_empty_world_not_a_crash()
        {
            var runtime = new SimulationShadowRuntime(true, () => null!);

            Assert.True(runtime.Poll(TimeSpan.Zero));

            Assert.True(runtime.LatestSnapshot.HasValue);
            Assert.Equal(0, runtime.LatestSnapshot!.Value.DomainCount);
            Assert.Null(runtime.LastError);
        }
    }
}
