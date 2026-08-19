using System.Collections.Generic;
using Bossa.Travellers.Ship;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The engine that BURNS fuel, the door that lets it in, and the gauge that
    /// shows it. The thin glue over <see cref="ShipFuelLedger"/> - every decision
    /// worth asserting on lives in that pure class and in
    /// <see cref="ShipFuelPolicy"/>; this file owns only the wiring.
    ///
    /// HOW IT WORKS, in one pass:
    ///
    ///   * THE POWER GENERATOR IS THE TANK. A hull gets a fuel system when a
    ///     <c>powerGenerator</c>/<c>powerGenerator01</c> is mounted on it, and its
    ///     capacity is the sum over however many are bolted on.
    ///     <see cref="ShipFuelLedger"/> carries the evidence and the pooling rule.
    ///     No generator, no fuel system: no burn, no gate, and the gauge reads a
    ///     full static tank.
    ///   * refuelling is <b>hold E on the generator</b>: <see cref="TryRefuel"/>
    ///     moves every unit of <c>"fuel"</c> the player is carrying that fits, using
    ///     the same <c>CraftingPolicy</c> drawdown the crafting path uses, then
    ///     pushes 1081 once. The prompt is not ours and does not need to be - the
    ///     <c>PowerGenerator01</c> prefab bakes <c>InteractiveObjectVisualizer</c>
    ///     with <c>Verb = Activate</c> and a <c>TutorialHelper</c> whose
    ///     <c>_interactionStep</c> is <c>MOUSE_OVER_GENERATOR</c>, and that overlay
    ///     asset (<c>STANDARD_MOUSE_OVER_GENERATOR</c>) reads the single word
    ///     <b>"Refuel"</b> with <c>Hold: true</c>. The control says exactly what it
    ///     does, which is what the sky-core door could never do and what the bunker
    ///     drain worked around.
    ///     STANDING CONSEQUENCE, and it is a better one than before: metering and
    ///     the refuel door are now the SAME PART, so a metered hull is always
    ///     refuellable by hand. The old "a hull with a core and no container cannot
    ///     be refuelled" hazard is gone with the bunker.
    ///   * burning happens here on a 0.5 s cadence, proportional to the pilot's
    ///     commanded throttle.
    ///   * the gauge is 1105 <c>FuelGaugeState</c> broadcast to every mounted
    ///     <c>fuelGauge</c> on that hull, gated by
    ///     <see cref="FuelGaugePushTracker"/>.
    ///
    /// NOTHING NEW IS SERVED ON THE GENERATOR ITSELF, and that is deliberate.
    /// <c>1106 FuelTankState</c> is the obvious candidate and buys nothing: the only
    /// class in the whole decompile that <c>[Require]</c>s it is
    /// <c>FuelVisualizer</c>, <c>ShipPreprocessor.cs:77</c> attaches that to ship
    /// ROOTS only (confirmed by a UnityPy census - <c>FuelVisualizer</c> appears on
    /// ShipFrame/01/02 and on no part prefab), and its one method
    /// <c>GetFuelPercent()</c> has zero callers. So on a generator 1106 would satisfy
    /// no reader at all, and on the hull it would wake a visualiser that has been
    /// inert since this server started. 1105 on the gauge remains the only fuel
    /// component this server serves.
    ///
    /// THE FLIGHT SEAM, and why no flight file is touched. Fuel and flight meet at
    /// the engine, and flight physics belongs to another branch. So this service
    /// reads and writes flight only through its EXISTING public/internal surface:
    /// it mirrors the 1111 throttle deltas that <c>ShipControlInput_Handler</c>
    /// already decodes (using flight's own <see cref="FlightControlInput.Merge"/>,
    /// so there is no second copy of the merge semantics), resolves the pilot of a
    /// hull with <c>ShipFlightService.PilotEntityOf</c>, and cuts a dry ship's engines
    /// with one ordinary <c>OnControlInput(throttle: 0)</c>. Nothing in
    /// <c>ShipFlightService</c>, <c>FlightIntegrator</c>, <c>FlightSession</c>,
    /// <c>FlightTuning</c> or <c>HullMassCalculator</c> is modified.
    ///
    /// THE MIRROR IS NECESSARY, not lazy: the client DIFF-SUPPRESSES 1111, so a
    /// held throttle sends nothing at all. "No packet" means unchanged, never
    /// released. Burning on packet arrival would let a pilot fly for free by not
    /// touching the stick.
    ///
    /// KNOWN INACCURACY, stated rather than hidden: cutting the throttle removes
    /// SAIL propulsion too, because the flight integrator derives everything from
    /// one throttle. Retail separated them - sails are wind, engines are fuel.
    /// docs/plans/feature-roadmap.md 13.7 records exactly what this branch wants
    /// from feat/ship-flight to fix it.
    ///
    /// MULTIPLAYER SAFETY: the only new wire traffic is the 1105 broadcast, and it
    /// is quantised (>=1 fuel unit) and rate-floored (>=1 s) against a needle the
    /// client deliberately delays by 2 s. Burning itself is silent. Cheap when
    /// nothing is flying: one Stopwatch compare, then a walk of the metered hulls
    /// that allocates nothing while every throttle is zero.
    /// </summary>
    internal sealed class ShipFuelService
    {
        /// <summary>The catalogue itemType of the instrument that shows the level.</summary>
        internal const string GaugeItemType = "fuelGauge";

        /// <summary>
        /// Whether a catalogue itemType is a POWER GENERATOR - the tank, and the
        /// refuel door. Two schematic keys share one prefab
        /// (<c>LoosePartCatalogue</c> rows 335-336), so this is a predicate rather
        /// than a constant; a third generator recipe would need adding here, and
        /// <c>ShipFuelWiringTests</c> pins that both known keys are covered.
        /// </summary>
        internal static bool IsGenerator(string? itemType) =>
            itemType == "powerGenerator" || itemType == "powerGenerator01";

        private readonly IClock _clock;
        private readonly ShipFuelLedger _ledger = new ShipFuelLedger();
        private readonly FuelGaugePushTracker _gauges = new FuelGaugePushTracker();
        private readonly CadenceTimer _cadence;

        /// <summary>The mirrored 1111 stream, per PLAYER. Merged, never replaced.</summary>
        private readonly Dictionary<long, FlightControlInput> _inputs =
            new Dictionary<long, FlightControlInput>();

        /// <summary>
        /// Who was last at each hull's helm. Exists to close a staleness hole this
        /// service cannot fix at the source: <c>ShipFlightService</c> clears its own
        /// held input when a pilot DISMOUNTS, inside a file this branch does not
        /// modify. Without this, a pilot who parks with the throttle forward, gets
        /// off, and later re-mans would find flight starting from neutral while
        /// fuel's mirror still read "full ahead" - and fuel would burn for thrust
        /// nobody is getting.
        /// </summary>
        private readonly Dictionary<long, long> _lastPilotByHull = new Dictionary<long, long>();

        private TimeSpan _lastBurnAt;
        private bool _started;

        internal ShipFuelService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cadence = new CadenceTimer(TimeSpan.FromSeconds(ShipFuelPolicy.BurnIntervalSeconds));
        }

        /// <summary>Whether the fuel subsystem runs at all. <c>WAREBORN_FUEL=0</c> turns it off.</summary>
        internal static bool Enabled =>
            ShipFuelPolicy.EnabledFrom(Environment.GetEnvironmentVariable("WAREBORN_FUEL"));

        /// <summary>Whether an empty tank stops the engines. <c>WAREBORN_FUEL_GATES_THRUST=0</c> turns it off.</summary>
        internal static bool GatesThrust =>
            ShipFuelPolicy.GatesThrustFrom(Environment.GetEnvironmentVariable("WAREBORN_FUEL_GATES_THRUST"));

        internal static double Capacity =>
            ShipFuelPolicy.CapacityFrom(Environment.GetEnvironmentVariable("WAREBORN_FUEL_CAPACITY"));

        internal static double BurnRate =>
            ShipFuelPolicy.BurnRateFrom(Environment.GetEnvironmentVariable("WAREBORN_FUEL_BURN_RATE"));

        /// <summary>The ledger, for the 1105 serve branch and the admin commands.</summary>
        internal ShipFuelLedger Ledger => _ledger;

        // ------------------------------------------------------------------
        // Registration: a power generator is a hull's fuel system
        // ------------------------------------------------------------------

        /// <summary>
        /// A part was mounted on a hull. Only a power generator matters; everything
        /// else is two string compares. Idempotent, so every mount/restore/late-join
        /// walk can call it, and a re-mounted generator does NOT refill.
        /// </summary>
        internal void OnPartMounted(string? itemType, long partEntityId, long hullEntityId)
        {
            if (!Enabled || !IsGenerator(itemType))
            {
                return;
            }

            if (_ledger.Register(partEntityId, hullEntityId, Capacity))
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " gained generator " + partEntityId
                    + " (" + _ledger.GeneratorsOn(hullEntityId) + " pooled); tank "
                    + _ledger.Read(hullEntityId) + ".");
            }
        }

        /// <summary>
        /// A part left a hull (lifted, salvaged). A hull that loses its LAST generator
        /// is UNMETERED again - it burns nothing and is gated by nothing, because a
        /// ship with no fuel system must not be strandable. The generator keeps its
        /// own fuel, so remounting is not a free refuel and carrying it to another
        /// ship brings the fuel along.
        /// </summary>
        internal void OnPartUnmounted(string? itemType, long partEntityId, long hullEntityId)
        {
            if (itemType == GaugeItemType)
            {
                _gauges.Forget(partEntityId);
                return;
            }
            if (!IsGenerator(itemType))
            {
                return;
            }

            if (_ledger.Unregister(partEntityId))
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " lost generator " + partEntityId
                    + "; " + _ledger.GeneratorsOn(hullEntityId) + " left, tank now "
                    + _ledger.Read(hullEntityId)
                    + (_ledger.IsMetered(hullEntityId) ? "." : " (unmetered - it flies free)."));
            }
        }

        /// <summary>The ship itself is gone. Drop every generator on it.</summary>
        internal void OnHullRemoved(long hullEntityId)
        {
            if (_lastPilotByHull.Remove(hullEntityId, out long pilot))
            {
                _inputs.Remove(pilot);
            }
            _ledger.ForgetHull(hullEntityId);
        }

        // ------------------------------------------------------------------
        // Refuelling: hold E on the generator, and the client says "Refuel"
        // ------------------------------------------------------------------

        /// <summary>
        /// A completed Activate on a mounted POWER GENERATOR: move every unit of
        /// <c>"fuel"</c> the player carries that fits into the hull's pool. Returns
        /// how many units moved, or null when the target is not a mounted generator
        /// (so the interaction dispatcher can keep looking).
        ///
        /// THIS IS THE RETAIL DOOR, and unlike the two doors before it, it is not a
        /// reconstruction. <c>PowerGenerator01</c> bakes
        /// <c>InteractiveObjectVisualizer(Verb = Activate)</c> and a
        /// <c>TutorialHelper</c> pointing at <c>MOUSE_OVER_GENERATOR</c>, whose
        /// overlay asset spells the prompt <b>"Refuel"</b>. We do not choose those
        /// words and cannot change them; we only have to make them true.
        ///
        /// ORDER MATTERS AND IS DELIBERATE: ask the POOL first and take from the
        /// player only what it accepted, so a nearly-full ship can never eat a whole
        /// stack. The one rollback that remains - the pool accepted and the grid then
        /// refused - is an exact <see cref="ShipFuelLedger.Withdraw"/> of a double,
        /// never an attempt to re-create an item, because putting an item back into a
        /// grid is the operation that can fail a second time.
        /// </summary>
        internal int? TryRefuel(long playerEntityId, long generatorEntityId)
        {
            if (!Enabled)
            {
                return null;
            }

            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(generatorEntityId);
            if (mount == null || !IsGenerator(mount.Value.ItemType))
            {
                return null;
            }

            long hullEntityId = mount.Value.HullEntityId;
            if (!_ledger.IsMetered(hullEntityId))
            {
                // Mounted but never registered - a restore path that predates this
                // feature, or WAREBORN_FUEL flipped on mid-session. Register it now
                // rather than refusing an interaction the client told the player to
                // make.
                _ledger.Register(generatorEntityId, hullEntityId, Capacity);
            }

            InventoryModel model = InventoryService.ForEntity(playerEntityId);
            // The count and the drawdown MUST agree on what counts as fuel, or a
            // refuel can fill the tank and then fail to pay for it. Both go through
            // CraftingPolicy's own matching rule for exactly that reason.
            int carried = CraftingPolicy.AvailableFor(model, InventoryWire.CategoryLookup, FuelPods.ItemTypeId);
            FuelReading before = _ledger.Read(hullEntityId);

            if (carried <= 0)
            {
                Console.WriteLine("[fuel] refuel refused: entity " + playerEntityId
                    + " carries no fuel (hull " + hullEntityId + ", tank " + before + ").");
                return 0;
            }

            int moved = _ledger.Deposit(hullEntityId, carried);
            if (moved <= 0)
            {
                Console.WriteLine("[fuel] refuel refused: hull " + hullEntityId
                    + " is full (" + before + ").");
                return 0;
            }

            if (!TryTakeFuel(model, moved))
            {
                _ledger.Withdraw(hullEntityId, moved);
                Console.WriteLine("[warning] fuel: inventory drawdown of " + moved
                    + " failed for entity " + playerEntityId + "; tank rolled back.");
                return 0;
            }

            InventoryPush.Push(playerEntityId, "refuelled ship " + hullEntityId);
            PushGauges(hullEntityId, force: true);

            Console.WriteLine("[fuel] entity " + playerEntityId + " refuelled hull " + hullEntityId
                + " at generator " + generatorEntityId + ": +" + moved + " fuel, tank now "
                + _ledger.Read(hullEntityId) + ".");
            return moved;
        }

        // ------------------------------------------------------------------
        // The 1111 mirror and the thrust gate
        // ------------------------------------------------------------------

        /// <summary>
        /// One decoded 1111 delta, mirrored with flight's OWN merge semantics
        /// (absent = unchanged). Returns the throttle the flight service should
        /// actually be given: unchanged normally, forced to zero while the hull this
        /// player pilots is dry.
        /// </summary>
        internal float? OnControlInput(long playerEntityId, float? throttle, float? vertical,
            float? axisPitch, float? axisYaw, float? axisRoll)
        {
            if (!Enabled)
            {
                return throttle;
            }

            _inputs.TryGetValue(playerEntityId, out FlightControlInput held);
            _inputs[playerEntityId] = held.Merge(throttle, vertical, axisPitch, axisYaw, axisRoll);

            if (!GatesThrust || !throttle.HasValue || throttle.Value == 0f)
            {
                return throttle;
            }

            // AnyDry first: this runs on up to 20 packets a second per pilot, and
            // almost always no ship in the world is empty.
            return _ledger.AnyDry && PilotsADryHull(playerEntityId) ? 0f : throttle;
        }

        /// <summary>
        /// A pilot is gone - disconnected, released the helm, or their ship retired.
        /// Drops the mirrored input.
        ///
        /// MUST be called wherever <c>ShipFlightService</c> clears its own
        /// <c>_inputs</c>, or the two hold different opinions of a stick nobody is
        /// touching and fuel's opinion is the one that burns. That is the same
        /// invisible-per-life-state leak the flight service's own OnPlayerGone
        /// comment warns about.
        /// </summary>
        internal void ForgetPlayer(long playerEntityId) => _inputs.Remove(playerEntityId);

        private bool PilotsADryHull(long playerEntityId)
        {
            foreach (long hullEntityId in _ledger.HullEntityIds)
            {
                if (_ledger.IsDry(hullEntityId)
                    && WorldsAdriftRebornGameServer.Flight.IsPilotOf(playerEntityId, hullEntityId))
                {
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------
        // The heartbeat
        // ------------------------------------------------------------------

        /// <summary>
        /// One call per main-loop turn. Cheap when off (an env check) or when no ship
        /// has a fuel system (an empty walk).
        /// </summary>
        internal void Tick()
        {
            if (!Enabled || _ledger.Count == 0)
            {
                return;
            }
            if (!_cadence.Due(_clock.Elapsed))
            {
                return;
            }

            TimeSpan now = _clock.Elapsed;
            double seconds = _started ? (now - _lastBurnAt).TotalSeconds : 0.0;
            _lastBurnAt = now;
            _started = true;
            if (seconds <= 0.0)
            {
                return;
            }

            // Pull each metered hull's commanded throttle off the mirror. A hull with
            // nobody at the helm coasts on zero and costs nothing.
            IReadOnlyList<long> hulls = _ledger.HullEntityIds;
            foreach (long hullEntityId in hulls)
            {
                long? pilot = WorldsAdriftRebornGameServer.Flight.PilotEntityOf(hullEntityId);

                // Whoever was here before and is not here now has left the helm, and
                // flight has already dropped their held input. Drop ours in the same
                // breath, so a re-man starts both sides from neutral.
                if (_lastPilotByHull.TryGetValue(hullEntityId, out long previous)
                    && previous != (pilot ?? 0L))
                {
                    _inputs.Remove(previous);
                }
                if (pilot.HasValue)
                {
                    _lastPilotByHull[hullEntityId] = pilot.Value;
                }
                else
                {
                    _lastPilotByHull.Remove(hullEntityId);
                }

                double throttle = pilot.HasValue
                    && _inputs.TryGetValue(pilot.Value, out FlightControlInput input)
                        ? input.Throttle
                        : 0.0;
                _ledger.SetThrottle(hullEntityId, throttle);
            }

            IReadOnlyList<long> wentDry = _ledger.Burn(seconds, BurnRate);

            // Only a hull that actually burned can have moved its needle. A parked
            // ship therefore costs nothing at all - no mounted-part walk, no push.
            foreach (long hullEntityId in hulls)
            {
                if (_ledger.ThrottleOf(hullEntityId) != 0.0)
                {
                    PushGauges(hullEntityId, force: false);
                }
            }

            // REFUELLING IS NOT ON THIS TICK. It is a player holding E on a generator
            // (TryRefuel), which is the door the shipped client already labels
            // "Refuel". The burn tick used to also drain the ship's containers; that
            // was a workaround for having no honest prompt, and it is gone.
            foreach (long hullEntityId in wentDry)
            {
                CutEngines(hullEntityId);
            }
        }

        /// <summary>
        /// A hull just ran dry. Cut the engines ONCE, on the transition - the ledger
        /// reports it exactly once for precisely this reason. The ship decelerates on
        /// its normal curve and coasts to a halt; it does NOT lose altitude, because
        /// lift is the sky core's (1258) and fuel never touched it in retail either.
        /// </summary>
        private void CutEngines(long hullEntityId)
        {
            PushGauges(hullEntityId, force: true);

            long? pilot = WorldsAdriftRebornGameServer.Flight.PilotEntityOf(hullEntityId);
            if (!GatesThrust || !pilot.HasValue)
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " is OUT OF FUEL"
                    + (GatesThrust ? "." : " (thrust gate disabled)."));
                return;
            }

            // Zero the mirror too, or the next tick would think the stick is still
            // forward and the handler's clamp would be arguing with a stale value.
            if (_inputs.TryGetValue(pilot.Value, out FlightControlInput held))
            {
                _inputs[pilot.Value] = held.Merge(0f, null, null, null, null);
            }

            WorldsAdriftRebornGameServer.Flight.OnControlInput(pilot.Value, 0f, null, null, null, null);
            Console.WriteLine("[fuel] hull " + hullEntityId + " is OUT OF FUEL; engines cut for pilot "
                + pilot.Value + " (the ship keeps its altitude and coasts to a stop).");
        }

        // ------------------------------------------------------------------
        // The gauge
        // ------------------------------------------------------------------

        /// <summary>
        /// What a gauge mounted on <paramref name="gaugeEntityId"/>'s hull should
        /// read. A LOOSE gauge, or one on a hull with no fuel system, reads a full
        /// static tank - see <see cref="ShipFuelLedger"/> for why that is honest.
        /// This is what the 1105 serve branch calls.
        /// </summary>
        internal FuelReading ReadingForGauge(long gaugeEntityId)
        {
            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(gaugeEntityId);
            return mount == null ? FuelReading.Unmetered : _ledger.Read(mount.Value.HullEntityId);
        }

        /// <summary>
        /// Push 1105 to every mounted fuel gauge on a hull whose needle would move.
        /// <paramref name="force"/> bypasses only the quantum's caller-side skip; the
        /// tracker's own quantum and rate floor still apply, so a refuel spam cannot
        /// become a packet storm.
        /// </summary>
        private void PushGauges(long hullEntityId, bool force)
        {
            FuelReading reading = _ledger.Read(hullEntityId);
            double now = _clock.Elapsed.TotalSeconds;

            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry
                in Crafting.MountedParts.OnHull(hullEntityId))
            {
                if (entry.Value.ItemType != GaugeItemType)
                {
                    continue;
                }
                if (force)
                {
                    // Record it, so the rate floor still applies to what follows.
                    _gauges.Record(entry.Key, reading.Level, now);
                }
                else if (!_gauges.ShouldPush(entry.Key, reading.Level, reading.Capacity, now))
                {
                    continue;
                }

                FuelGaugeState.Update update = new FuelGaugeState.Update()
                    .SetCapacity((float)reading.Capacity)
                    .SetFuel((float)reading.Level);
                ShipPublisher.Broadcast(entry.Key, 1105u, update);
            }
        }

        // ------------------------------------------------------------------
        // Inventory drawdown
        // ------------------------------------------------------------------

        /// <summary>
        /// Takes exactly <paramref name="units"/> of <c>"fuel"</c> out of a model,
        /// using the same Remove/Replace idiom <c>CraftingPolicy</c> uses. Works on a
        /// copy and commits only on success, so a partial take can never happen.
        /// </summary>
        private static bool TryTakeFuel(InventoryModel model, int units)
        {
            InventoryModel work = model.Copy();
            int remaining = units;

            foreach (InventoryItem item in new List<InventoryItem>(work.Items))
            {
                if (remaining <= 0)
                {
                    break;
                }
                if (item.IsWorn || item.IsStashed)
                {
                    continue;
                }
                InventoryWire.CategoryLookup(item.ItemTypeId, out string category);
                if (!CraftingPolicy.Matches(FuelPods.ItemTypeId, item.ItemTypeId, category))
                {
                    continue;
                }

                int take = Math.Min(remaining, item.Amount);
                if (take >= item.Amount)
                {
                    work.Remove(item.ItemId);
                }
                else
                {
                    work.Replace(item with { Amount = item.Amount - take });
                }
                remaining -= take;
            }

            if (remaining > 0)
            {
                return false;
            }

            model.Reset(work.Items);
            return true;
        }


    }
}
