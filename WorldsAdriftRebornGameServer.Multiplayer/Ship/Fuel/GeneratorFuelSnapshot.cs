namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>
    /// Additive, versioned durable state for ONE power generator. It is stored on
    /// the part record, not the hull, because the generator is the tank and its fuel
    /// must follow its stable PartUid through mount, lift, transfer and restart.
    ///
    /// Capacity 100 is RECOVERED. A saved capacity is retained for audit and rollback,
    /// but the current configured per-generator capacity is authoritative on restore;
    /// only the saved level crosses the process boundary. Consumption magnitude remains
    /// explicitly WAREBORN TUNING.
    /// </summary>
    public sealed class GeneratorFuelSnapshot
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public double Capacity { get; set; }
        public double Level { get; set; }

        public static GeneratorFuelSnapshot Capture(FuelReading reading) => new GeneratorFuelSnapshot
        {
            Version = CurrentVersion,
            Capacity = FinitePositive(reading.Capacity)
                ? reading.Capacity
                : ShipFuelPolicy.GeneratorCapacity,
            Level = double.IsFinite(reading.Level)
                ? System.Math.Clamp(reading.Level, 0.0,
                    FinitePositive(reading.Capacity) ? reading.Capacity : ShipFuelPolicy.GeneratorCapacity)
                : 0.0,
        };

        /// <summary>
        /// Reads untrusted JSON into the CURRENT configured tank. Invalid, negative,
        /// non-finite and future-version data fails closed at empty rather than granting
        /// free fuel. A null snapshot is handled by the caller as a legacy full tank.
        /// </summary>
        public bool TryRestore(double configuredCapacity, out FuelReading reading)
        {
            double capacity = FinitePositive(configuredCapacity)
                ? configuredCapacity
                : ShipFuelPolicy.GeneratorCapacity;
            bool valid = Version == CurrentVersion
                && FinitePositive(Capacity)
                && double.IsFinite(Level)
                && Level >= 0.0
                && Level <= Capacity;
            reading = new FuelReading(capacity,
                valid ? System.Math.Clamp(Level, 0.0, capacity) : 0.0);
            return valid;
        }

        private static bool FinitePositive(double value) => double.IsFinite(value) && value > 0.0;
    }
}
