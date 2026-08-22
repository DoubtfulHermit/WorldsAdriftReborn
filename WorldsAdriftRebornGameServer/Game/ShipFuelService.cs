using System.Collections.Generic;
using Bossa.Travellers.Ship;
using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Crafting;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel;
using WorldsAdriftRebornGameServer.Game.Persistence;

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
    ///   * burning happens here on a 0.5 s cadence, proportional to the hull
    ///     session's physical throttle and mounted engine count. A cleanly
    ///     dismounted lever remains latched and therefore keeps burning.
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
    /// WITH <c>WAREBORN_FUEL_HULL_DEMAND=1</c>, the flight seam is one-way and
    /// hull-level. Fuel reads
    /// <see cref="ShipFlightService.PropulsionDemandFor"/>, whose throttle is the
    /// authoritative <c>FlightSession.Input</c> already responsible for 1111 delta
    /// suppression, clean-dismount latching and abandon neutralisation. Fuel never
    /// grows a second active input authority. Flight asks <see cref="EnginesPowered"/> while
    /// constructing <c>ShipPropulsion</c>; a dry tank makes engine force zero while
    /// sails, wind, lift and the physical lever remain untouched. The switch defaults
    /// OFF and preserves the pre-Track-7 pilot mirror and dry-throttle clamp.
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

        // The pre-Track-7 fuel path remains intact behind the new rollout gate. It
        // mirrors the current pilot's diff-suppressed 1111 input and burns one
        // ship-level rate, exactly as production did before hull-authored demand.
        private readonly Dictionary<long, FlightControlInput> _legacyInputs = new();
        private readonly Dictionary<long, long> _legacyPilotByHull = new();

        private TimeSpan _lastBurnAt;
        private TimeSpan _nextPersistenceAt;
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

        /// <summary>
        /// Track 7's authoritative hull-demand and durable per-generator lifecycle.
        /// Explicit opt-in only; unset/unknown values retain pre-Track-7 behavior.
        /// </summary>
        internal static bool HullDemandLifecycleEnabled =>
            ShipFuelPolicy.HullDemandLifecycleEnabledFrom(
                Environment.GetEnvironmentVariable(ShipFuelPolicy.HullDemandLifecycleEnvVar));

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
        internal void OnPartMounted(string? itemType, long partEntityId, long hullEntityId,
            GeneratorFuelSnapshot? restored = null)
        {
            if (!Enabled || !IsGenerator(itemType))
            {
                return;
            }

            bool registered;
            if (!HullDemandLifecycleEnabled || restored == null)
            {
                registered = _ledger.Register(partEntityId, hullEntityId, Capacity);
            }
            else
            {
                bool valid = restored.TryRestore(Capacity, out FuelReading reading);
                registered = _ledger.RegisterAt(
                    partEntityId, hullEntityId, reading.Capacity, reading.Level);
                if (!valid)
                {
                    Console.WriteLine("[warning] fuel: generator " + partEntityId
                        + " had invalid durable fuel data; restored safely at " + reading + ".");
                }
            }

            if (registered)
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " gained generator " + partEntityId
                    + " (" + _ledger.GeneratorsOn(hullEntityId) + " pooled); tank "
                    + _ledger.Read(hullEntityId) + ".");
            }
        }

        /// <summary>Restores a loose generator's tank without attaching it to a hull.</summary>
        internal void OnLoosePartRestored(string? itemType, long partEntityId,
            GeneratorFuelSnapshot? restored)
        {
            if (!Enabled || !HullDemandLifecycleEnabled || !IsGenerator(itemType) || restored == null) return;
            bool valid = restored.TryRestore(Capacity, out FuelReading reading);
            _ledger.RestoreDetached(partEntityId, restored, Capacity);
            if (!valid)
            {
                Console.WriteLine("[warning] fuel: loose generator " + partEntityId
                    + " had invalid durable fuel data; restored safely at " + reading + ".");
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

            if (HullDemandLifecycleEnabled) PersistGenerator(partEntityId);
            if (_ledger.Unregister(partEntityId))
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " lost generator " + partEntityId
                    + "; " + _ledger.GeneratorsOn(hullEntityId) + " left, tank now "
                    + _ledger.Read(hullEntityId)
                    + (_ledger.IsMetered(hullEntityId) ? "." : " (unmetered - it flies free)."));
            }
        }

        /// <summary>A part entity was permanently destroyed; discard any dormant tank too.</summary>
        internal void OnPartRemoved(string? itemType, long partEntityId, long hullEntityId)
        {
            if (itemType == GaugeItemType)
            {
                _gauges.Forget(partEntityId);
                return;
            }
            if (!IsGenerator(itemType)) return;
            if (!HullDemandLifecycleEnabled)
            {
                OnPartUnmounted(itemType, partEntityId, hullEntityId);
                return;
            }
            if (_ledger.Forget(partEntityId))
            {
                Console.WriteLine("[fuel] removed destroyed generator " + partEntityId
                    + " from hull " + hullEntityId + ".");
            }
        }

        /// <summary>The ship itself is gone. Drop every generator on it.</summary>
        internal void OnHullRemoved(long hullEntityId)
        {
            if (!HullDemandLifecycleEnabled && _legacyPilotByHull.Remove(hullEntityId, out long pilot))
            {
                _legacyInputs.Remove(pilot);
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
            if (HullDemandLifecycleEnabled) PersistHullFuel(hullEntityId);

            Console.WriteLine("[fuel] entity " + playerEntityId + " refuelled hull " + hullEntityId
                + " at generator " + generatorEntityId + ": +" + moved + " fuel, tank now "
                + _ledger.Read(hullEntityId) + ".");
            return moved;
        }

        // ------------------------------------------------------------------
        // Input compatibility and the engine-only thrust gate
        // ------------------------------------------------------------------

        /// <summary>
        /// Compatibility pass-through for one decoded 1111 delta. The force model
        /// does not alter this value: its dry gate removes engine force downstream.
        /// The rollback kinematic model cannot distinguish engine, sail and baseline
        /// propulsion, so fuel gating is deliberately unavailable there.
        /// </summary>
        internal float? OnControlInput(long playerEntityId, float? throttle, float? vertical,
            float? axisPitch, float? axisYaw, float? axisRoll)
        {
            if (!HullDemandLifecycleEnabled)
            {
                if (!Enabled) return throttle;

                _legacyInputs.TryGetValue(playerEntityId, out FlightControlInput held);
                _legacyInputs[playerEntityId] = held.Merge(
                    throttle, vertical, axisPitch, axisYaw, axisRoll);

                if (!GatesThrust || !throttle.HasValue || throttle.Value == 0f) return throttle;
                return _ledger.AnyDry && PilotsADryHull(playerEntityId) ? 0f : throttle;
            }

            // Never mutate the shared physical lever here. The force model gates
            // ENGINE force at PropulsionFor, leaving sails, wind and lift untouched.
            // Legacy kinematic flight has no separable combustion term, so fuel gating
            // is intentionally unavailable in that rollback model rather than lying by
            // suppressing canvas along with engines.
            return throttle;
        }

        /// <summary>
        /// Retained as a no-op compatibility seam for existing disconnect wiring.
        /// Fuel owns no per-player input; flight's hull session is the only command.
        /// </summary>
        internal void ForgetPlayer(long playerEntityId)
        {
            if (!HullDemandLifecycleEnabled) _legacyInputs.Remove(playerEntityId);
        }

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

        /// <summary>
        /// The force evaluator's engine gate. Empty fuel never changes throttle,
        /// sails, ambient wind or sky-core lift; it only removes combustion thrust.
        /// </summary>
        internal bool EnginesPowered(long hullEntityId) =>
            !HullDemandLifecycleEnabled || !Enabled || !GatesThrust
                || !_ledger.IsMetered(hullEntityId) || !_ledger.IsDry(hullEntityId);

        internal GeneratorFuelSnapshot? CaptureGenerator(long generatorEntityId) =>
            HullDemandLifecycleEnabled ? _ledger.CaptureGenerator(generatorEntityId) : null;

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

            IReadOnlyList<long> hulls = _ledger.HullEntityIds;
            if (HullDemandLifecycleEnabled)
            {
                // Track 7: flight's physical lever remains authoritative even with
                // an empty pilot seat, and mounted engines scale combustion demand.
                foreach (long hullEntityId in hulls)
                {
                    _ledger.SetDemand(hullEntityId,
                        WorldsAdriftRebornGameServer.Flight.PropulsionDemandFor(hullEntityId));
                }
            }
            else
            {
                // Exact pre-Track-7 behavior: only the currently seated pilot's
                // mirrored input burns, at one ship-level rate.
                foreach (long hullEntityId in hulls)
                {
                    long? pilot = WorldsAdriftRebornGameServer.Flight.PilotEntityOf(hullEntityId);
                    if (_legacyPilotByHull.TryGetValue(hullEntityId, out long previous)
                        && previous != (pilot ?? 0L))
                    {
                        _legacyInputs.Remove(previous);
                    }
                    if (pilot.HasValue) _legacyPilotByHull[hullEntityId] = pilot.Value;
                    else _legacyPilotByHull.Remove(hullEntityId);

                    double throttle = pilot.HasValue
                        && _legacyInputs.TryGetValue(pilot.Value, out FlightControlInput input)
                            ? input.Throttle
                            : 0.0;
                    _ledger.SetThrottle(hullEntityId, throttle);
                }
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

            if (HullDemandLifecycleEnabled && now >= _nextPersistenceAt)
            {
                _nextPersistenceAt = now + TimeSpan.FromSeconds(2);
                var poweredHulls = new List<long>();
                foreach (long hullEntityId in hulls)
                {
                    if (_ledger.DemandOf(hullEntityId).IsPowered)
                    {
                        poweredHulls.Add(hullEntityId);
                    }
                }
                PersistHullsFuel(poweredHulls);
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

            if (!HullDemandLifecycleEnabled)
            {
                long? pilot = WorldsAdriftRebornGameServer.Flight.PilotEntityOf(hullEntityId);
                if (!GatesThrust || !pilot.HasValue)
                {
                    Console.WriteLine("[fuel] hull " + hullEntityId + " is OUT OF FUEL"
                        + (GatesThrust ? "." : " (thrust gate disabled)."));
                    return;
                }
                if (_legacyInputs.TryGetValue(pilot.Value, out FlightControlInput held))
                {
                    _legacyInputs[pilot.Value] = held.Merge(0f, null, null, null, null);
                }
                WorldsAdriftRebornGameServer.Flight.OnControlInput(
                    pilot.Value, 0f, null, null, null, null);
                Console.WriteLine("[fuel] hull " + hullEntityId
                    + " is OUT OF FUEL; engines cut for pilot " + pilot.Value
                    + " (the ship keeps its altitude and coasts to a stop).");
                return;
            }

            PersistHullFuel(hullEntityId);
            if (!GatesThrust)
            {
                Console.WriteLine("[fuel] hull " + hullEntityId + " is OUT OF FUEL"
                    + " (thrust gate disabled).");
                return;
            }
            Console.WriteLine("[fuel] hull " + hullEntityId
                + " is OUT OF FUEL; combustion engine force is disabled. The physical throttle, sails,"
                + " ambient wind and sky-core lift are unchanged.");
        }

        private void PersistHullFuel(long hullEntityId)
        {
            PersistHullsFuel(new[] { hullEntityId });
        }

        private void PersistHullsFuel(IEnumerable<long> hullEntityIds)
        {
            var snapshots = new Dictionary<string, GeneratorFuelSnapshot>(StringComparer.Ordinal);
            foreach (long hullEntityId in hullEntityIds)
            {
                foreach (long generatorEntityId in _ledger.GeneratorEntityIdsOn(hullEntityId))
                {
                    string? partUid = Crafting.LooseParts.PartUidFor(generatorEntityId);
                    GeneratorFuelSnapshot? snapshot = _ledger.CaptureGenerator(generatorEntityId);
                    if (!string.IsNullOrEmpty(partUid) && snapshot != null)
                    {
                        snapshots[partUid] = snapshot;
                    }
                }
            }
            WorldStatePersistence.UpdateGeneratorFuel(snapshots);
        }

        private void PersistGenerator(long generatorEntityId)
        {
            string? partUid = Crafting.LooseParts.PartUidFor(generatorEntityId);
            GeneratorFuelSnapshot? snapshot = _ledger.CaptureGenerator(generatorEntityId);
            if (!string.IsNullOrEmpty(partUid) && snapshot != null)
            {
                WorldStatePersistence.UpdateGeneratorFuel(partUid, snapshot);
            }
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
