using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>
    /// THE REFUEL DOOR, and the reason it is not the sky core any more.
    ///
    /// WHAT WENT WRONG WITH THE OLD DOOR. Fuel shipped with "hold E on the atlas sky
    /// core" because 13.4 established that a verb cannot be invented -
    /// <c>InteractiveObjectVisualizer</c> caches the entry matching its PREFAB-BAKED
    /// verb once in <c>OnEnable</c> - and the sky core looked like the only ship part
    /// whose <c>Activate</c> was baked and unclaimed. That premise was half right and
    /// half wrong:
    ///
    ///   * RIGHT: no shipped client script consumes the core's interact. The only
    ///     client-side reaction to a returning <c>Interact</c> is
    ///     <c>InteractiveObjectVisualizer.OnInteractSent</c> ->
    ///     <c>SendMessage("OnInteract")</c>, and the only three <c>OnInteract</c>
    ///     receivers in the whole decompile are <c>ToggleRoller</c>,
    ///     <c>ResetSwitch</c> and <c>InteractiveDemo</c>. So our 1211 really does
    ///     arrive and really is handled; nothing intercepts it.
    ///   * WRONG: the PROMPT is claimed, and unfixably so. The words the player reads
    ///     are not ours - <c>InteractiveObjectVisualizer.GetTutorialStep</c> maps
    ///     (verb == Activate) + (has a <c>ShipCoreVisualizer</c>) to
    ///     <c>TutorialStep.MOUSE_OVER_CORE</c>, whose baked overlay asset spells
    ///     "Activate Atlas Pulse" with <c>Hold: true</c>. Nothing a server can send
    ///     changes that string. And it names a REAL retail action: 1306
    ///     <c>ShipAtlasPulseState</c> drives <c>ShipAtlasPulseVisualizer</c>, which
    ///     <c>ShipPreprocessor</c> attaches to every hull and which implements
    ///     <c>IClimbGrapplePreventer</c> - the pulse was the anti-boarding defence.
    ///
    /// So the old door was a control that lied about what it did, and squatting on it
    /// also forecloses ever implementing the pulse. <c>PartInteractionPolicy</c>'s
    /// standing rule - never advertise a verb we cannot honour - applies to a verb
    /// whose LABEL we cannot honour just as much.
    ///
    /// THE NEW DOOR NEEDS NO PROMPT AT ALL. A ship burns what is aboard it: fuel put
    /// into any container BOLTED TO THE HULL is drawn into the tank as the tank makes
    /// room. Every gesture in that sentence already works - the four containers open
    /// (<c>ShipContainerPreprocessor</c> bakes <c>Inventory</c>, and we seed 1081 +
    /// 1236), and moving an item into one is an ordinary cross-inventory move. There
    /// is no new verb, no new prompt, no new prefab and nothing that can misdescribe
    /// itself, because nothing new is described.
    ///
    /// This class is the arithmetic half, kept pure so the split can be asserted on
    /// without an inventory, a ship or a clock.
    /// </summary>
    public static class ShipFuelBunkerPolicy
    {
        /// <summary>
        /// One container's contribution to a drain: which container, and how many
        /// units of <c>"fuel"</c> to take out of it.
        /// </summary>
        public readonly struct Draw
        {
            public Draw(long containerEntityId, int units)
            {
                ContainerEntityId = containerEntityId;
                Units = units;
            }

            public long ContainerEntityId { get; }

            /// <summary>Always >= 1: a zero draw is never emitted.</summary>
            public int Units { get; }

            public override string ToString() => ContainerEntityId + " x" + Units;
        }

        /// <summary>
        /// The tank must have at least a CANISTER's worth of room before the bunker
        /// feeds it - one full 8+8+9 pod, the one recovered number in this subsystem.
        ///
        /// THIS IS A WIRE RULE, not a fuel rule, and it is the standing
        /// multiplayer-safety audit applied to this feature. Every draw pushes that
        /// container's 1081, on an entity that RIDES A MOVING SHIP - the exact class
        /// of traffic that caused this project's desync spiral. Without a threshold a
        /// hull at full throttle (0.25 fuel/s) opens a unit of room every four
        /// seconds and would push 1081 at ~0.25 Hz per container for the whole
        /// flight. With it, a bunker feeds the tank once per canister burned - about
        /// one push per 100 s of continuous full throttle - and the player sees the
        /// same needle either way, because the client delays it 2 s regardless.
        ///
        /// It cannot strand anybody: an empty tank has the whole capacity free, which
        /// is ten canisters, so the threshold is only ever reached by a tank that is
        /// already nearly full.
        /// </summary>
        public static int MinimumDrawUnits => WorldsAdriftRebornGameServer.Multiplayer.FuelCanisterYield.TotalFuel;

        /// <summary>
        /// Whether a tank at this level should ask its bunker for anything at all.
        /// The cheap first line of the drain, so a parked or nearly-full ship costs
        /// one subtraction and no walk of its mounted parts.
        /// </summary>
        public static bool ShouldDraw(double level, double capacity) =>
            FreeUnits(level, capacity) >= MinimumDrawUnits;

        /// <summary>
        /// How much room the tank has, in whole units, for a level/capacity pair.
        /// Floored, never negative - a tank 0.4 units short cannot accept a whole
        /// canister unit, and offering to take one would round fuel away.
        /// </summary>
        public static int FreeUnits(double level, double capacity)
        {
            double free = capacity - level;
            if (free <= 0.0)
            {
                return 0;
            }
            int whole = (int)free;
            return whole < 0 ? 0 : whole;
        }

        /// <summary>
        /// Split <paramref name="freeUnits"/> of tank room across the containers on a
        /// hull, in the order given, taking from each only what it actually holds.
        ///
        /// THE INVARIANT THE CALLER DEPENDS ON: the returned units sum to exactly
        /// <c>min(freeUnits, total available)</c> and no entry exceeds that
        /// container's own stock. The drain must be able to run as
        /// take-then-deposit with no rounding slack, or a partial failure leaves
        /// fuel either duplicated or destroyed.
        ///
        /// Containers holding nothing are skipped rather than returned with zero, so
        /// a hull with ten empty crates costs one walk and no work.
        /// </summary>
        public static IReadOnlyList<Draw> Plan(int freeUnits, IReadOnlyList<Draw> available)
        {
            var draws = new List<Draw>();
            if (freeUnits <= 0 || available == null || available.Count == 0)
            {
                return draws;
            }

            int remaining = freeUnits;
            foreach (Draw stock in available)
            {
                if (remaining <= 0)
                {
                    break;
                }
                if (stock.Units <= 0)
                {
                    continue;
                }

                int take = stock.Units < remaining ? stock.Units : remaining;
                draws.Add(new Draw(stock.ContainerEntityId, take));
                remaining -= take;
            }

            return draws;
        }

        /// <summary>The total a plan moves. Exists so the caller never re-sums it by hand.</summary>
        public static int TotalOf(IReadOnlyList<Draw> draws)
        {
            int total = 0;
            if (draws == null)
            {
                return 0;
            }
            foreach (Draw draw in draws)
            {
                total += draw.Units;
            }
            return total;
        }
    }
}
