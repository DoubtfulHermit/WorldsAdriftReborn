using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE per-hull lift-capacity answer for component serving - thin glue only.
    /// It gathers the ONE mass snapshot and the hull's gate state and hands both
    /// to <see cref="LiftGravityRuntime.PlanFor"/>, where every capacity decision
    /// lives (and is unit-tested). With the lift runtime OFF for a hull the served
    /// value is EXACTLY the historical <c>ShipLiftPolicy.SeededTotalLiftKg</c>
    /// seed - the pure test
    /// <c>Component_1258_serving_does_not_change_while_the_flag_is_off</c> holds
    /// that equality, so flipping nothing changes nothing on the wire.
    /// </summary>
    internal static class ShipLiftPlans
    {
        /// <summary>The current capacity plan for one built hull.</summary>
        internal static LiftCapacityPlan For(long hullEntityId)
        {
            return LiftGravityRuntime.PlanFor(
                ShipMassSnapshots.For(hullEntityId),
                ShipFlightService.Gravity,
                LiftRuntimeApplies(hullEntityId),
                // GRANDFATHER-ALL SEAM: no durable build-epoch exists yet; every
                // hull is treated as pre-activation. Same seam as the runtime's
                // slice glue - one bit, one meaning, documented in the report.
                existedBeforeLiftActivation: true);
        }

        /// <summary>What 1258 ShipLiftState serves for this hull.</summary>
        internal static double Served1258LiftKg(long hullEntityId)
        {
            return LiftGravityRuntime.Served1258LiftKg(
                LiftRuntimeApplies(hullEntityId), For(hullEntityId));
        }

        private static bool LiftRuntimeApplies(long hullEntityId) =>
            ShipFlightService.RuntimeFlags.LiftRuntimeAppliesTo(
                Crafting.BuiltShips.PersistentIndexFor(hullEntityId));
    }
}
