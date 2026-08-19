using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>The level of one ship's tank, as a gauge would read it.</summary>
    public readonly struct FuelReading : System.IEquatable<FuelReading>
    {
        public FuelReading(double capacity, double level)
        {
            Capacity = capacity;
            Level = level;
        }

        /// <summary>1105 FuelGaugeState.capacity / 1106 FuelTankState.capacity.</summary>
        public double Capacity { get; }

        /// <summary>1105 FuelGaugeState.fuel / 1106 FuelTankState.fuel.</summary>
        public double Level { get; }

        /// <summary>0..1. A zero/garbage capacity reads FULL, never a division by zero.</summary>
        public double Fraction =>
            Capacity > 0.0 && !double.IsNaN(Capacity) ? System.Math.Clamp(Level / Capacity, 0.0, 1.0) : 1.0;

        /// <summary>Nothing left to burn.</summary>
        public bool IsDry => Level <= 0.0;

        /// <summary>
        /// The reading a hull with NO fuel system shows: a full static tank at the
        /// default capacity. See <see cref="ShipFuelLedger"/> for why that is the
        /// honest value and not zero.
        /// </summary>
        public static FuelReading Unmetered =>
            new FuelReading(ShipFuelPolicy.DefaultCapacity, ShipFuelPolicy.DefaultCapacity);

        public bool Equals(FuelReading other) => Capacity.Equals(other.Capacity) && Level.Equals(other.Level);
        public override bool Equals(object? obj) => obj is FuelReading other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Capacity, Level);
        public override string ToString() =>
            Level.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "/"
            + Capacity.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every ship whose tank this server tracks, and how much is in it.
    ///
    /// PER HULL, NOT PER TANK, and that is a forced deviation from retail worth
    /// naming. Retail put <c>1106 FuelTankState</c> on real tank ENTITIES and bound
    /// each engine to one of them by id through <c>1104 FuelConsumerState</c>. We
    /// cannot: the 349-name client entity-prefab census contains no ship fuel tank,
    /// so there is no tank entity to spawn, and naming a prefab the client cannot
    /// resolve eats the materials and renders nothing. Retail's own ship root
    /// aggregated its tanks anyway (<c>AccumulatedData.field5_fuel_tanks</c>, a
    /// Map&lt;EntityId, FuelData&gt;), so this ledger holds the aggregate one level
    /// up, which is the number the gauge was always shown.
    ///
    /// A HULL WITH NO ENTRY IS UNMETERED, NOT EMPTY. This is the rule that keeps
    /// the feature from grounding ships nobody consented to ground. Only a hull
    /// that has been <see cref="Register"/>ed - which the caller does only for a
    /// ship carrying a mounted atlas sky core, the one part with a baked Activate
    /// verb and therefore the only refuel door the shipped client leaves open - has
    /// a fuel system at all. Everything else burns nothing, is gated by nothing,
    /// and reads <see cref="FuelReading.Unmetered"/>: a full tank that never moves.
    /// A ship that cannot be refuelled can never be stranded.
    ///
    /// A REGISTERED TANK STARTS FULL, deliberately. Ships already fly on this
    /// server; introducing fuel must not empty a tank a player never knew they had.
    ///
    /// Pure: no ENet, no Improbable types, no clock - the caller injects elapsed
    /// seconds, exactly like <see cref="Horns"/>. NOT THREAD-SAFE, like the rest of
    /// this assembly: the server is a single poll loop.
    /// </summary>
    public sealed class ShipFuelLedger
    {
        private sealed class Tank
        {
            public double Capacity;
            public double Level;

            /// <summary>Last commanded throttle for this hull, -1..1. Absent input = unchanged.</summary>
            public double Throttle;

            /// <summary>
            /// Whether the hull currently has a refuel door. An INACTIVE tank keeps its
            /// level but behaves in every other way like an unknown hull - see
            /// <see cref="ShipFuelLedger.Unregister"/> for why it is dormant rather than
            /// deleted.
            /// </summary>
            public bool Active;
        }

        private readonly Dictionary<long, Tank> _byHull = new Dictionary<long, Tank>();

        /// <summary>
        /// Declares that a hull has a fuel system, with a full tank. Idempotent:
        /// registration runs on every mount/restore/late-join walk that notices a
        /// sky core, and a second pass must not refill a tank someone has burnt
        /// down - the same trap <c>FuelCanisterRegistry.Register</c> documents.
        ///
        /// A hull whose tank went DORMANT (see <see cref="Unregister"/>) comes back
        /// at the level it left, not full: bolting the core back on must not be a
        /// free refuel.
        /// </summary>
        /// <returns>True when the hull's fuel system became active; false if it already was.</returns>
        public bool Register(long hullEntityId, double capacity)
        {
            if (_byHull.TryGetValue(hullEntityId, out Tank? existing))
            {
                if (existing.Active)
                {
                    return false;
                }
                existing.Active = true;
                existing.Throttle = 0.0;
                return true;
            }

            double sane = capacity > 0.0 && !double.IsNaN(capacity) && !double.IsInfinity(capacity)
                ? capacity
                : ShipFuelPolicy.DefaultCapacity;
            _byHull[hullEntityId] = new Tank { Capacity = sane, Level = sane, Throttle = 0.0, Active = true };
            return true;
        }

        /// <summary>
        /// Declares a hull's fuel system with an EXPLICIT level - the restore path,
        /// for when a saved level exists. Idempotent like <see cref="Register"/>.
        /// The level is clamped into the tank.
        /// </summary>
        public bool RegisterAt(long hullEntityId, double capacity, double level)
        {
            if (!Register(hullEntityId, capacity))
            {
                return false;
            }
            Tank tank = _byHull[hullEntityId];
            tank.Level = Clamp(level, 0.0, tank.Capacity);
            return true;
        }

        /// <summary>
        /// The hull lost its refuel door - the sky core was lifted off or salvaged.
        /// The tank goes DORMANT rather than being deleted: the hull immediately
        /// behaves like an unmetered one (no burn, no gate, reads full), so it can
        /// never be stranded without a core, but its level is remembered so that
        /// bolting the core back on is not a free refuel.
        ///
        /// Use <see cref="Forget"/> when the ship itself is gone.
        /// </summary>
        /// <returns>True when an active fuel system went dormant.</returns>
        public bool Unregister(long hullEntityId)
        {
            if (!_byHull.TryGetValue(hullEntityId, out Tank? tank) || !tank.Active)
            {
                return false;
            }
            tank.Active = false;
            tank.Throttle = 0.0;
            return true;
        }

        /// <summary>Drops a hull entirely - the ship was salvaged or deleted.</summary>
        public bool Forget(long hullEntityId) => _byHull.Remove(hullEntityId);

        /// <summary>Whether this hull has a fuel system at all.</summary>
        public bool IsMetered(long hullEntityId) => Active(hullEntityId) != null;

        /// <summary>
        /// What a gauge on this hull should read. An unregistered hull reads
        /// <see cref="FuelReading.Unmetered"/> - a full static tank - because it
        /// genuinely has unlimited range, and a needle pinned at empty on a ship
        /// that flies forever would be a lie in the other direction.
        /// </summary>
        public FuelReading Read(long hullEntityId)
        {
            Tank? tank = Active(hullEntityId);
            return tank != null ? new FuelReading(tank.Capacity, tank.Level) : FuelReading.Unmetered;
        }

        /// <summary>
        /// Nothing left to burn. FALSE for an unregistered hull: no fuel system
        /// means never dry, which is what keeps the thrust gate off ships that
        /// cannot be refuelled.
        /// </summary>
        public bool IsDry(long hullEntityId) => Active(hullEntityId) is Tank tank && tank.Level <= 0.0;

        /// <summary>
        /// Records the pilot's commanded throttle for a hull. Unregistered hulls are
        /// ignored - there is nothing to burn.
        /// </summary>
        public void SetThrottle(long hullEntityId, double throttle)
        {
            Tank? tank = Active(hullEntityId);
            if (tank == null)
            {
                return;
            }
            tank.Throttle = double.IsNaN(throttle) || double.IsInfinity(throttle)
                ? 0.0
                : Clamp(throttle, -1.0, 1.0);
        }

        /// <summary>The throttle this ledger last saw for a hull. 0 for an unknown hull.</summary>
        public double ThrottleOf(long hullEntityId) => Active(hullEntityId)?.Throttle ?? 0.0;

        /// <summary>
        /// Moves up to <paramref name="offered"/> whole units of fuel into a hull's
        /// tank and reports how many actually went in. 0 for an unregistered hull -
        /// there is nowhere to put it, and the caller must NOT then take the fuel
        /// out of the player's inventory.
        /// </summary>
        public int Deposit(long hullEntityId, int offered)
        {
            Tank? tank = Active(hullEntityId);
            if (tank == null)
            {
                return 0;
            }

            int taken = ShipFuelPolicy.DepositRoom(tank.Level, tank.Capacity, offered);
            if (taken <= 0)
            {
                return 0;
            }

            tank.Level = Clamp(tank.Level + taken, 0.0, tank.Capacity);
            return taken;
        }

        /// <summary>
        /// Takes fuel back OUT of a tank - the inverse of <see cref="Deposit"/>, for
        /// undoing a deposit whose payment then failed. Clamped at empty, and 0 for
        /// an unmetered hull, so it can never invent a debt.
        /// </summary>
        public int Withdraw(long hullEntityId, int units)
        {
            Tank? tank = Active(hullEntityId);
            if (tank == null || units <= 0)
            {
                return 0;
            }

            int taken = (int)System.Math.Min(units, System.Math.Floor(tank.Level));
            if (taken <= 0)
            {
                return 0;
            }
            tank.Level = Clamp(tank.Level - taken, 0.0, tank.Capacity);
            return taken;
        }

        /// <summary>
        /// Burns <paramref name="seconds"/> of flight on every hull under power and
        /// returns the hulls that ran DRY on this tick - the transition, exactly
        /// once, so the caller cuts the throttle there and nowhere else. A hull
        /// already at zero is not reported again.
        ///
        /// Cheap when nothing is flying: a hull at zero throttle costs one compare.
        /// </summary>
        public IReadOnlyList<long> Burn(double seconds, double burnPerSecond)
        {
            List<long>? wentDry = null;

            foreach (KeyValuePair<long, Tank> entry in _byHull)
            {
                Tank tank = entry.Value;
                if (!tank.Active || tank.Level <= 0.0)
                {
                    continue;
                }

                double burnt = ShipFuelPolicy.BurnFor(tank.Throttle, seconds, burnPerSecond);
                if (burnt <= 0.0)
                {
                    continue;
                }

                tank.Level -= burnt;
                if (tank.Level <= 0.0)
                {
                    tank.Level = 0.0;
                    (wentDry ??= new List<long>()).Add(entry.Key);
                }
            }

            return (IReadOnlyList<long>?)wentDry ?? System.Array.Empty<long>();
        }

        /// <summary>Every ACTIVE metered hull. For fan-out, persistence and logs.</summary>
        public IReadOnlyList<long> HullEntityIds
        {
            get
            {
                var ids = new List<long>(_byHull.Count);
                foreach (KeyValuePair<long, Tank> entry in _byHull)
                {
                    if (entry.Value.Active)
                    {
                        ids.Add(entry.Key);
                    }
                }
                return ids;
            }
        }

        /// <summary>
        /// Whether ANY active hull is empty. The cheap gate in front of the 1111 hot
        /// path: the throttle clamp runs on up to 20 packets a second per pilot, and
        /// the overwhelming majority of the time no ship in the world is dry.
        /// </summary>
        public bool AnyDry
        {
            get
            {
                foreach (KeyValuePair<long, Tank> entry in _byHull)
                {
                    if (entry.Value.Active && entry.Value.Level <= 0.0)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>How many hulls have an ACTIVE fuel system. For logs and tests.</summary>
        public int Count
        {
            get
            {
                int active = 0;
                foreach (KeyValuePair<long, Tank> entry in _byHull)
                {
                    if (entry.Value.Active) { active++; }
                }
                return active;
            }
        }

        /// <summary>Fills one hull's tank. The admin escape hatch; returns false for an unknown hull.</summary>
        public bool Refill(long hullEntityId)
        {
            Tank? tank = Active(hullEntityId);
            if (tank == null)
            {
                return false;
            }
            tank.Level = tank.Capacity;
            return true;
        }

        /// <summary>Fills every tank. Returns how many were not already full.</summary>
        public int RefillAll()
        {
            int changed = 0;
            foreach (Tank tank in _byHull.Values)
            {
                if (!tank.Active) { continue; }
                if (tank.Level < tank.Capacity)
                {
                    changed++;
                }
                tank.Level = tank.Capacity;
            }
            return changed;
        }

        /// <summary>The hull's tank, but only while its fuel system is active.</summary>
        private Tank? Active(long hullEntityId) =>
            _byHull.TryGetValue(hullEntityId, out Tank? tank) && tank.Active ? tank : null;

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
