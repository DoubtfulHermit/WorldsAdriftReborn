using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn
{
    /// <summary>
    /// The throttled holder that stands between the server's poll loop and the shadow
    /// model. It owns three things and nothing else: the feature flag, the cadence,
    /// and the latest snapshot.
    ///
    /// <para>
    /// The inertness contract, which is acceptance criterion 6: when
    /// <see cref="Enabled"/> is false this class NEVER invokes the observation
    /// supplier. That is the structural guarantee, not a comment - the supplier is the
    /// only door to live server state, so an unenabled runtime cannot read a ship, a
    /// player, an interest set or anything else, and therefore cannot perturb one. A
    /// test asserts the supplier is never called; break the flag check and it goes red.
    /// </para>
    ///
    /// <para>
    /// It also never throws into the poll loop. A diagnostic that can take the server
    /// down is worse than no diagnostic; a fault parks the observer and is reported
    /// through <see cref="LastError"/> instead.
    /// </para>
    /// </summary>
    public sealed class SimulationShadowRuntime
    {
        private readonly Func<WarebornWorldObservation>? _observe;
        private readonly TimeSpan _refreshInterval;
        private TimeSpan? _lastRefreshAt;
        private WorldSnapshot _latest;
        private bool _hasSnapshot;
        private IReadOnlyList<string> _diagnostics = Array.Empty<string>();

        /// <param name="enabled">The evaluated WAREBORN_SIMULATION_MODEL flag.</param>
        /// <param name="observe">
        /// The only channel to live state. Null is allowed and means the same as
        /// disabled, so a caller that could not build a reader does not have to
        /// invent one.
        /// </param>
        public SimulationShadowRuntime(
            bool enabled,
            Func<WarebornWorldObservation>? observe,
            TimeSpan? refreshInterval = null)
        {
            _observe = observe;
            Enabled = enabled && observe != null;
            TimeSpan interval = refreshInterval ?? SimulationObserverPolicy.RefreshInterval;
            _refreshInterval = interval > TimeSpan.Zero ? interval : SimulationObserverPolicy.RefreshInterval;
        }

        /// <summary>A runtime that is off and holds no supplier at all.</summary>
        public static SimulationShadowRuntime Disabled => new SimulationShadowRuntime(false, null);

        public bool Enabled { get; }

        /// <summary>How many times the world was actually rebuilt. Off means zero, forever.</summary>
        public int RefreshCount { get; private set; }

        /// <summary>The first fault, if the observer ever faulted. Null when healthy.</summary>
        public string? LastError { get; private set; }

        /// <summary>Null until the first successful refresh.</summary>
        public WorldSnapshot? LatestSnapshot => _hasSnapshot ? _latest : null;

        /// <summary>The section-11 text for the latest snapshot. Empty until then.</summary>
        public IReadOnlyList<string> LatestDiagnostics => _diagnostics;

        /// <summary>
        /// Called from the existing poll loop. Deliberately NOT named Tick: nothing in
        /// this subsystem simulates, and the domain layer's no-Tick decision should not
        /// be blurred by a neighbour that borrows the word.
        /// </summary>
        /// <param name="monotonicNow">
        /// Elapsed time from any fixed origin - a Stopwatch, not a wall clock. Passing
        /// it in rather than reading a clock is what makes the cadence testable.
        /// </param>
        /// <returns>True when this call rebuilt the world.</returns>
        public bool Poll(TimeSpan monotonicNow)
        {
            if (!Enabled) return false;
            if (_lastRefreshAt.HasValue
                && !SimulationObserverPolicy.ShouldRefresh(monotonicNow - _lastRefreshAt.Value, _refreshInterval))
                return false;
            return Refresh(monotonicNow);
        }

        /// <summary>
        /// The explicit debug-command path: rebuild now, ignoring the cadence. Still
        /// respects the flag - "even enabled it only observes" cuts both ways, and a
        /// command must not be a way to switch the observer on.
        /// </summary>
        public bool Refresh(TimeSpan monotonicNow)
        {
            if (!Enabled || _observe == null) return false;
            _lastRefreshAt = monotonicNow;
            try
            {
                WarebornWorldObservation observation = _observe() ?? WarebornWorldObservation.Empty;
                _latest = WarebornSimulationProjection.Project(observation).Snapshot();
                _hasSnapshot = true;
                _diagnostics = SimulationDiagnostics.Format(_latest);
                RefreshCount++;
                return true;
            }
            catch (Exception ex)
            {
                LastError ??= ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
