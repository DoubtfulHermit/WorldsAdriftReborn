using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation.Wareborn
{
    /// <summary>
    /// When the shadow observer runs, and how often. The rule lives here rather than
    /// inline at the call site because the game-server assembly has no test project:
    /// a flag check written there is guarded by string matching, and a flag check
    /// written here is guarded by an assertion.
    /// </summary>
    public static class SimulationObserverPolicy
    {
        public const string EnabledEnvVar = "WAREBORN_SIMULATION_MODEL";

        /// <summary>
        /// Only the exact string <c>1</c> enables it. Unset, empty, <c>0</c>,
        /// <c>true</c>, <c>yes</c> and nonsense all leave it off - the same strictness
        /// every other flag in this server that touches the running world uses, so an
        /// operator cannot half-enable it with a plausible-looking value.
        /// </summary>
        public static bool IsEnabled(string? env) => env == "1";

        /// <summary>
        /// Five seconds. Section 11 asks for 5-10 s and explicitly forbids per-tick
        /// work; five is the fast end of that window because the admin panel polls
        /// every 1.5 s off a 3 s stats file, and a 10 s shadow refresh would make the
        /// inspector show a world up to ten seconds behind the ownership topology
        /// sitting next to it in the same card.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Whether enough time has passed to rebuild. Separate from the runtime so the
        /// cadence itself is testable without a clock.
        /// </summary>
        public static bool ShouldRefresh(TimeSpan sinceLastRefresh, TimeSpan interval) =>
            sinceLastRefresh >= interval;
    }
}
