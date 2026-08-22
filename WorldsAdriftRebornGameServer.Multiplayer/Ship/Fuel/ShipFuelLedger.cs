using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>The level of one ship's POOLED tank, as a gauge would read it.</summary>
    public readonly struct FuelReading : System.IEquatable<FuelReading>
    {
        public FuelReading(double capacity, double level)
        {
            Capacity = capacity;
            Level = level;
        }

        /// <summary>1105 FuelGaugeState.capacity - the SUM over the hull's generators.</summary>
        public double Capacity { get; }

        /// <summary>1105 FuelGaugeState.fuel - the SUM over the hull's generators.</summary>
        public double Level { get; }

        /// <summary>0..1. A zero/garbage capacity reads FULL, never a division by zero.</summary>
        public double Fraction =>
            Capacity > 0.0 && !double.IsNaN(Capacity) ? System.Math.Clamp(Level / Capacity, 0.0, 1.0) : 1.0;

        /// <summary>Nothing left to burn.</summary>
        public bool IsDry => Level <= 0.0;

        /// <summary>
        /// The reading a hull with NO generator shows: a full static tank at one
        /// generator's capacity. See <see cref="ShipFuelLedger"/> for why that is the
        /// honest value and not zero.
        /// </summary>
        public static FuelReading Unmetered =>
            new FuelReading(ShipFuelPolicy.GeneratorCapacity, ShipFuelPolicy.GeneratorCapacity);

        public bool Equals(FuelReading other) => Capacity.Equals(other.Capacity) && Level.Equals(other.Level);
        public override bool Equals(object? obj) => obj is FuelReading other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Capacity, Level);
        public override string ToString() =>
            Level.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "/"
            + Capacity.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every POWER GENERATOR this server tracks, which hull it is bolted to, and how
    /// much fuel is inside it.
    ///
    /// THE GENERATOR IS THE TANK. That is the correction this class exists to carry.
    /// An earlier pass put the tank on the HULL, on the reasoning that the 349-name
    /// client entity-prefab census has no fuel-tank prefab so there was nothing to
    /// hang <c>1106 FuelTankState</c> on. The search was for the words "fuel tank".
    /// The prefab is called <c>PowerGenerator01</c> (census line 219), it is
    /// craftable and mountable on this server today, and the shipped client's own
    /// prompt for it - <c>TutorialHelper._interactionStep = 17</c> ->
    /// <c>MOUSE_OVER_GENERATOR</c> -> the baked overlay asset
    /// <c>STANDARD_MOUSE_OVER_GENERATOR</c> - reads the single word
    /// <b>"Refuel"</b>. Retail stored fuel in the generator, and the client still
    /// says so out loud.
    ///
    /// POOLING IS BY SUMMATION over a hull's mounted generators: two generators are
    /// two tanks and one gauge reading, three are three. That is the behaviour the
    /// community record describes ("multiple generators pool their capacity
    /// automatically"), and it is also what retail's own ship root did one level up -
    /// <c>AccumulatedData.field5_fuel_tanks</c> is a
    /// Map&lt;EntityId, FuelData{capacity, fuel}&gt; and the gauge was always shown
    /// the sum. It is NOT <c>1106.subtanks</c>: that field is a per-tank int nothing
    /// in the shipped client reads (exhaustive grep over <c>acs/</c> and <c>ecs/</c>
    /// returns zero non-gencode hits), so reproducing it would be inventing a number
    /// with no observable consequence. Summation is the honest equivalent.
    ///
    /// FUEL TRAVELS WITH THE GENERATOR. Unbolting one takes its contents off the
    /// hull; bolting it to another ship brings them along. That falls out of storing
    /// the level against the generator's entity id rather than the hull's, and it is
    /// the behaviour "the generator is the tank" implies, so it is asserted rather
    /// than left to chance.
    ///
    /// A HULL WITH NO GENERATOR IS UNMETERED, NOT EMPTY. This is the rule that keeps
    /// the feature from grounding ships nobody consented to ground, and under this
    /// design it is stronger than it was: metering and the refuel door are now THE
    /// SAME PART. A hull is metered only because a generator is bolted to it, and
    /// that generator is exactly the thing a player holds E on to refuel. There is no
    /// longer any way to be metered and unrefuellable. Every other hull burns
    /// nothing, is gated by nothing, and reads <see cref="FuelReading.Unmetered"/>: a
    /// full tank that never moves.
    ///
    /// A NEWLY REGISTERED GENERATOR STARTS FULL, deliberately. Ships already fly on
    /// this server; introducing fuel must not empty a tank a player never knew they
    /// had. A generator coming back from dormancy keeps the level it left with.
    ///
    /// Pure: no ENet, no Improbable types, no clock - the caller injects elapsed
    /// seconds, exactly like <see cref="Horns"/>. NOT THREAD-SAFE, like the rest of
    /// this assembly: the server is a single poll loop.
    /// </summary>
    public sealed class ShipFuelLedger
    {
        private sealed class Generator
        {
            /// <summary>The hull it is bolted to while <see cref="Active"/>.</summary>
            public long Hull;

            public double Capacity;
            public double Level;

            /// <summary>
            /// Whether it is currently bolted to a hull. An INACTIVE generator keeps
            /// its fuel but contributes nothing to any pool - see
            /// <see cref="ShipFuelLedger.Unregister"/> for why it is dormant rather
            /// than deleted.
            /// </summary>
            public bool Active;
        }

        private readonly Dictionary<long, Generator> _byGenerator = new Dictionary<long, Generator>();

        /// <summary>
        /// The generators bolted to each hull, in mount order. Drain and fill walk
        /// this, so the order a pool is consumed in is deterministic and testable
        /// rather than dictionary-iteration luck.
        /// </summary>
        private readonly Dictionary<long, List<long>> _byHull = new Dictionary<long, List<long>>();

        /// <summary>
        /// Current combustion demand per hull, copied from flight's authoritative
        /// hull session. It survives an empty pilot seat when the physical lever is
        /// latched; it becomes neutral when flight abandons the helm.
        /// </summary>
        private readonly Dictionary<long, HullPropulsionDemand> _demandByHull = new();

        /// <summary>
        /// Declares that a generator is bolted to a hull, FULL if this server has
        /// never seen it. Idempotent: registration runs on every mount/restore/
        /// late-join walk that notices a generator, and a second pass must not refill
        /// a tank someone has burnt down - the same trap
        /// <c>FuelCanisterRegistry.Register</c> documents.
        ///
        /// A generator that went DORMANT (see <see cref="Unregister"/>) comes back at
        /// the level it left, on whatever hull it is now bolted to: bolting it back on
        /// must not be a free refuel, and carrying it to another ship must not spill.
        /// </summary>
        /// <returns>True when this generator newly joined a hull's pool; false if it was already in one.</returns>
        public bool Register(long generatorEntityId, long hullEntityId, double capacity)
        {
            if (_byGenerator.TryGetValue(generatorEntityId, out Generator? existing))
            {
                if (existing.Active && existing.Hull == hullEntityId)
                {
                    return false;
                }

                // Either it was dormant, or it moved to another hull without an
                // unmount reaching us. Detach from wherever it was, keep the fuel.
                Detach(generatorEntityId, existing);
                existing.Hull = hullEntityId;
                existing.Active = true;
                Attach(hullEntityId, generatorEntityId);
                return true;
            }

            double sane = capacity > 0.0 && !double.IsNaN(capacity) && !double.IsInfinity(capacity)
                ? capacity
                : ShipFuelPolicy.GeneratorCapacity;
            _byGenerator[generatorEntityId] = new Generator
            {
                Hull = hullEntityId,
                Capacity = sane,
                Level = sane,
                Active = true,
            };
            Attach(hullEntityId, generatorEntityId);
            return true;
        }

        /// <summary>
        /// Declares a generator with an EXPLICIT level - the restore path, for when a
        /// saved level exists. Idempotent like <see cref="Register"/>. The level is
        /// clamped into the generator.
        /// </summary>
        public bool RegisterAt(long generatorEntityId, long hullEntityId, double capacity, double level)
        {
            if (!Register(generatorEntityId, hullEntityId, capacity))
            {
                return false;
            }
            Generator generator = _byGenerator[generatorEntityId];
            generator.Level = Clamp(level, 0.0, generator.Capacity);
            return true;
        }

        /// <summary>
        /// Restores a loose generator before it is mounted. It contributes to no hull
        /// and burns nothing, but retains its fuel so a later mount cannot refill it.
        /// Invalid durable data fails closed at empty.
        /// </summary>
        public bool RestoreDetached(long generatorEntityId, GeneratorFuelSnapshot snapshot,
            double configuredCapacity)
        {
            if (_byGenerator.ContainsKey(generatorEntityId))
            {
                return false;
            }
            snapshot ??= new GeneratorFuelSnapshot { Version = -1 };
            snapshot.TryRestore(configuredCapacity, out FuelReading restored);
            _byGenerator[generatorEntityId] = new Generator
            {
                Hull = 0,
                Capacity = restored.Capacity,
                Level = restored.Level,
                Active = false,
            };
            return true;
        }

        /// <summary>
        /// The generator was lifted off, salvaged or destroyed. It leaves the hull's
        /// pool immediately - a ship that just lost its last generator is unmetered
        /// from this instant, so it can never be stranded - but its fuel is REMEMBERED
        /// against its own entity id, because the fuel is inside it.
        ///
        /// Use <see cref="Forget"/> when the generator entity itself is gone for good.
        /// </summary>
        /// <returns>True when an active generator went dormant.</returns>
        public bool Unregister(long generatorEntityId)
        {
            if (!_byGenerator.TryGetValue(generatorEntityId, out Generator? generator) || !generator.Active)
            {
                return false;
            }
            Detach(generatorEntityId, generator);
            generator.Active = false;
            return true;
        }

        /// <summary>Drops a generator entirely - the entity was deleted.</summary>
        public bool Forget(long generatorEntityId)
        {
            if (!_byGenerator.TryGetValue(generatorEntityId, out Generator? generator))
            {
                return false;
            }
            Detach(generatorEntityId, generator);
            return _byGenerator.Remove(generatorEntityId);
        }

        /// <summary>
        /// The ship itself is gone. Drops every generator on it and returns how many.
        /// </summary>
        public int ForgetHull(long hullEntityId)
        {
            if (!_byHull.TryGetValue(hullEntityId, out List<long>? generators))
            {
                _demandByHull.Remove(hullEntityId);
                return 0;
            }

            long[] doomed = generators.ToArray();
            foreach (long generatorEntityId in doomed)
            {
                _byGenerator.Remove(generatorEntityId);
            }
            _byHull.Remove(hullEntityId);
            _demandByHull.Remove(hullEntityId);
            return doomed.Length;
        }

        /// <summary>Whether this hull has a fuel system at all - that is, any generator.</summary>
        public bool IsMetered(long hullEntityId) => Pool(hullEntityId) != null;

        /// <summary>How many generators are pooled on this hull. 0 for an unmetered hull.</summary>
        public int GeneratorsOn(long hullEntityId) => Pool(hullEntityId)?.Count ?? 0;

        /// <summary>Which hull a generator is bolted to, or null when it is loose or unknown.</summary>
        public long? HullOf(long generatorEntityId) =>
            _byGenerator.TryGetValue(generatorEntityId, out Generator? generator) && generator.Active
                ? generator.Hull
                : (long?)null;

        /// <summary>
        /// What a gauge on this hull should read: the SUM over its generators. A hull
        /// with none reads <see cref="FuelReading.Unmetered"/> - a full static tank -
        /// because it genuinely has unlimited range, and a needle pinned at empty on a
        /// ship that flies forever would be a lie in the other direction.
        /// </summary>
        public FuelReading Read(long hullEntityId)
        {
            List<long>? pool = Pool(hullEntityId);
            if (pool == null)
            {
                return FuelReading.Unmetered;
            }

            double capacity = 0.0;
            double level = 0.0;
            foreach (long generatorEntityId in pool)
            {
                Generator generator = _byGenerator[generatorEntityId];
                capacity += generator.Capacity;
                level += generator.Level;
            }
            return new FuelReading(capacity, level);
        }

        /// <summary>
        /// What ONE generator holds, for logs and for a future per-generator serve.
        /// A loose or unknown generator reads <see cref="FuelReading.Unmetered"/>.
        /// </summary>
        public FuelReading ReadGenerator(long generatorEntityId) =>
            _byGenerator.TryGetValue(generatorEntityId, out Generator? generator) && generator.Active
                ? new FuelReading(generator.Capacity, generator.Level)
                : FuelReading.Unmetered;

        /// <summary>
        /// Nothing left to burn anywhere on this hull. FALSE for an unmetered hull: no
        /// generator means never dry, which is what keeps the thrust gate off ships
        /// that have no fuel system.
        /// </summary>
        public bool IsDry(long hullEntityId)
        {
            List<long>? pool = Pool(hullEntityId);
            if (pool == null)
            {
                return false;
            }
            foreach (long generatorEntityId in pool)
            {
                if (_byGenerator[generatorEntityId].Level > 0.0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Records flight's authoritative hull propulsion demand. Unmetered hulls
        /// are ignored - there is nothing to burn.
        /// </summary>
        public void SetDemand(long hullEntityId, HullPropulsionDemand demand)
        {
            if (Pool(hullEntityId) == null)
            {
                return;
            }
            _demandByHull[hullEntityId] = demand;
        }

        /// <summary>Compatibility helper for callers that model one engine.</summary>
        public void SetThrottle(long hullEntityId, double throttle) =>
            SetDemand(hullEntityId, new HullPropulsionDemand(throttle, 1));

        /// <summary>The throttle this ledger last saw for a hull. 0 for an unmetered hull.</summary>
        public double ThrottleOf(long hullEntityId) =>
            Pool(hullEntityId) != null && _demandByHull.TryGetValue(hullEntityId, out HullPropulsionDemand demand)
                ? demand.Throttle
                : 0.0;

        public HullPropulsionDemand DemandOf(long hullEntityId) =>
            Pool(hullEntityId) != null && _demandByHull.TryGetValue(hullEntityId, out HullPropulsionDemand demand)
                ? demand
                : HullPropulsionDemand.None;

        /// <summary>
        /// Moves up to <paramref name="offered"/> whole units of fuel into a hull's
        /// generators, filling them in mount order, and reports how many actually went
        /// in. 0 for an unmetered hull - there is nowhere to put it, and the caller
        /// must NOT then take the fuel out of the player's inventory.
        /// </summary>
        public int Deposit(long hullEntityId, int offered)
        {
            List<long>? pool = Pool(hullEntityId);
            if (pool == null || offered <= 0)
            {
                return 0;
            }

            int moved = 0;
            foreach (long generatorEntityId in pool)
            {
                if (moved >= offered)
                {
                    break;
                }
                Generator generator = _byGenerator[generatorEntityId];
                int taken = ShipFuelPolicy.DepositRoom(generator.Level, generator.Capacity, offered - moved);
                if (taken <= 0)
                {
                    continue;
                }
                generator.Level = Clamp(generator.Level + taken, 0.0, generator.Capacity);
                moved += taken;
            }
            return moved;
        }

        /// <summary>
        /// Takes fuel back OUT of a hull's generators, for undoing a deposit whose
        /// payment then failed. Clamped at empty, and 0 for an unmetered hull, so it
        /// can never invent a debt.
        ///
        /// THE GUARANTEE IS CONSERVATION, NOT SYMMETRY, and the difference is worth
        /// stating because it looks like a bug otherwise. Deposit fills the pool front
        /// to back and this drains it front to back, so a Deposit/Withdraw pair always
        /// moves the same TOTAL back out - no fuel is created or destroyed, which is
        /// the only property the rollback needs. It does not always put each unit back
        /// in the generator it came from: if a deposit spilled into a later generator,
        /// the withdrawal may take those units from an earlier one instead. No
        /// observer can tell (the gauge reads the sum) and no fuel moves, so buying
        /// exact per-generator symmetry would mean remembering every deposit's
        /// distribution for a case nobody can see.
        /// </summary>
        public int Withdraw(long hullEntityId, int units)
        {
            List<long>? pool = Pool(hullEntityId);
            if (pool == null || units <= 0)
            {
                return 0;
            }

            int taken = 0;
            foreach (long generatorEntityId in pool)
            {
                if (taken >= units)
                {
                    break;
                }
                Generator generator = _byGenerator[generatorEntityId];
                int here = (int)System.Math.Min(units - taken, System.Math.Floor(generator.Level));
                if (here <= 0)
                {
                    continue;
                }
                generator.Level = Clamp(generator.Level - here, 0.0, generator.Capacity);
                taken += here;
            }
            return taken;
        }

        /// <summary>
        /// Burns <paramref name="seconds"/> of flight on every hull under power and
        /// returns the hulls that ran DRY on this tick - the transition, exactly once,
        /// so the caller gates engine force there and nowhere else. A hull already at
        /// zero is not reported again.
        ///
        /// The draw is spread across the hull's generators in mount order, so the
        /// first one empties first. Nothing downstream can see which generator holds
        /// what, but a deterministic order is what makes the pool assertable.
        ///
        /// Cheap when nothing is flying: a hull at zero throttle costs one lookup.
        /// </summary>
        public IReadOnlyList<long> Burn(double seconds, double burnPerSecond)
        {
            List<long>? wentDry = null;

            foreach (KeyValuePair<long, List<long>> entry in _byHull)
            {
                List<long> pool = entry.Value;
                if (pool.Count == 0)
                {
                    continue;
                }
                if (!_demandByHull.TryGetValue(entry.Key, out HullPropulsionDemand demand))
                {
                    continue;
                }

                double burnt = ShipFuelPolicy.BurnFor(
                    demand.Throttle, seconds, burnPerSecond, demand.EngineCount);
                if (burnt <= 0.0)
                {
                    continue;
                }

                double remaining = burnt;
                bool hadFuel = false;
                bool hasFuel = false;
                foreach (long generatorEntityId in pool)
                {
                    Generator generator = _byGenerator[generatorEntityId];
                    if (generator.Level > 0.0)
                    {
                        hadFuel = true;
                        double here = generator.Level < remaining ? generator.Level : remaining;
                        generator.Level -= here;
                        remaining -= here;
                        if (generator.Level <= 0.0)
                        {
                            generator.Level = 0.0;
                        }
                    }
                    if (generator.Level > 0.0)
                    {
                        hasFuel = true;
                    }
                }

                if (hadFuel && !hasFuel)
                {
                    (wentDry ??= new List<long>()).Add(entry.Key);
                }
            }

            return (IReadOnlyList<long>?)wentDry ?? System.Array.Empty<long>();
        }

        /// <summary>Every hull with a fuel system. For fan-out, persistence and logs.</summary>
        public IReadOnlyList<long> HullEntityIds
        {
            get
            {
                var ids = new List<long>(_byHull.Count);
                foreach (KeyValuePair<long, List<long>> entry in _byHull)
                {
                    if (entry.Value.Count > 0)
                    {
                        ids.Add(entry.Key);
                    }
                }
                return ids;
            }
        }

        /// <summary>Stable mount-order generator ids on a hull; empty when unmetered.</summary>
        public IReadOnlyList<long> GeneratorEntityIdsOn(long hullEntityId) =>
            Pool(hullEntityId)?.ToArray() ?? System.Array.Empty<long>();

        /// <summary>Durable state for one known generator, mounted or loose.</summary>
        public GeneratorFuelSnapshot? CaptureGenerator(long generatorEntityId) =>
            _byGenerator.TryGetValue(generatorEntityId, out Generator? generator)
                ? GeneratorFuelSnapshot.Capture(new FuelReading(generator.Capacity, generator.Level))
                : null;

        /// <summary>
        /// Whether ANY metered hull is empty. The cheap gate in front of the 1111 hot
        /// path: the throttle clamp runs on up to 20 packets a second per pilot, and
        /// the overwhelming majority of the time no ship in the world is dry.
        /// </summary>
        public bool AnyDry
        {
            get
            {
                foreach (KeyValuePair<long, List<long>> entry in _byHull)
                {
                    if (entry.Value.Count > 0 && IsDry(entry.Key))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>How many hulls have a fuel system. For logs and tests.</summary>
        public int Count
        {
            get
            {
                int metered = 0;
                foreach (KeyValuePair<long, List<long>> entry in _byHull)
                {
                    if (entry.Value.Count > 0) { metered++; }
                }
                return metered;
            }
        }

        /// <summary>Fills one hull's generators. The admin escape hatch; false for an unmetered hull.</summary>
        public bool Refill(long hullEntityId)
        {
            List<long>? pool = Pool(hullEntityId);
            if (pool == null)
            {
                return false;
            }
            foreach (long generatorEntityId in pool)
            {
                Generator generator = _byGenerator[generatorEntityId];
                generator.Level = generator.Capacity;
            }
            return true;
        }

        /// <summary>Fills every generator, mounted or dormant. Returns how many were not already full.</summary>
        public int RefillAll()
        {
            int changed = 0;
            foreach (Generator generator in _byGenerator.Values)
            {
                if (generator.Level < generator.Capacity)
                {
                    changed++;
                }
                generator.Level = generator.Capacity;
            }
            return changed;
        }

        /// <summary>The hull's generator list, but only while it actually has one.</summary>
        private List<long>? Pool(long hullEntityId) =>
            _byHull.TryGetValue(hullEntityId, out List<long>? pool) && pool.Count > 0 ? pool : null;

        private void Attach(long hullEntityId, long generatorEntityId)
        {
            if (!_byHull.TryGetValue(hullEntityId, out List<long>? pool))
            {
                pool = new List<long>(1);
                _byHull[hullEntityId] = pool;
            }
            if (!pool.Contains(generatorEntityId))
            {
                pool.Add(generatorEntityId);
            }
        }

        private void Detach(long generatorEntityId, Generator generator)
        {
            if (!_byHull.TryGetValue(generator.Hull, out List<long>? pool))
            {
                return;
            }
            pool.Remove(generatorEntityId);
            if (pool.Count == 0)
            {
                // The hull has no fuel system any more, so it must not keep a stale
                // throttle that a later generator would inherit as "full ahead".
                _byHull.Remove(generator.Hull);
                _demandByHull.Remove(generator.Hull);
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value))
            {
                return min;
            }
            return value < min ? min : (value > max ? max : value);
        }
    }
}
