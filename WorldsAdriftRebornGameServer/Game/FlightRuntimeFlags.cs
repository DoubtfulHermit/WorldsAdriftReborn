using System;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The Steps 4-5 flight-runtime feature gates, read ONCE at type
    /// initialization per the repo's flag convention (opt-in via literal "1").
    /// Dependency rule: a dependent flag whose prerequisite is OFF logs one
    /// startup warning and stays OFF - a gate never half-enables.
    ///
    /// <list type="bullet">
    /// <item>WAREBORN_FLIGHT_COLLISION_OBSERVE - in-tick collision shadow
    ///   (requires WAREBORN_FLIGHT_FIXED_STEP=1: there is no honest stamp
    ///   without the fixed clock).</item>
    /// <item>WAREBORN_FLIGHT_COLLISION_RESPONSE - velocity-only response
    ///   (requires observe).</item>
    /// <item>WAREBORN_FLIGHT_DOCKING_TXN - transactional authentic docking;
    ///   suppresses the legacy capture writers for runtime-managed hulls
    ///   (requires observe for clearance evidence).</item>
    /// </list>
    /// All live values are visible in the admin snapshot via
    /// FlightCollisionDockingStat.
    /// </summary>
    internal static class FlightRuntimeFlags
    {
        internal static readonly bool CollisionObserveEnabled;
        internal static readonly bool CollisionResponseEnabled;
        internal static readonly bool DockingTxnEnabled;

        static FlightRuntimeFlags()
        {
            bool observeRequested = Environment.GetEnvironmentVariable(
                "WAREBORN_FLIGHT_COLLISION_OBSERVE") == "1";
            bool responseRequested = Environment.GetEnvironmentVariable(
                "WAREBORN_FLIGHT_COLLISION_RESPONSE") == "1";
            bool dockingRequested = Environment.GetEnvironmentVariable(
                "WAREBORN_FLIGHT_DOCKING_TXN") == "1";

            CollisionObserveEnabled = observeRequested && ShipFlightService.FixedStepEnabled;
            if (observeRequested && !CollisionObserveEnabled)
            {
                Console.WriteLine("[warning] flight: WAREBORN_FLIGHT_COLLISION_OBSERVE=1 requires"
                    + " WAREBORN_FLIGHT_FIXED_STEP=1; collision observation stays OFF.");
            }

            CollisionResponseEnabled = responseRequested && CollisionObserveEnabled;
            if (responseRequested && !CollisionResponseEnabled)
            {
                Console.WriteLine("[warning] flight: WAREBORN_FLIGHT_COLLISION_RESPONSE=1 requires"
                    + " WAREBORN_FLIGHT_COLLISION_OBSERVE=1 (with the fixed step);"
                    + " collision response stays OFF.");
            }

            DockingTxnEnabled = dockingRequested && CollisionObserveEnabled;
            if (dockingRequested && !DockingTxnEnabled)
            {
                Console.WriteLine("[warning] flight: WAREBORN_FLIGHT_DOCKING_TXN=1 requires"
                    + " WAREBORN_FLIGHT_COLLISION_OBSERVE=1 (with the fixed step);"
                    + " transactional docking stays OFF.");
            }
        }
    }
}
