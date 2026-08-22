namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Fuel
{
    /// <summary>
    /// The NUMBERS of the fuel subsystem, and the arithmetic that turns a throttle
    /// and a duration into fuel burnt. Pure: no ENet, no Improbable types, no clock.
    ///
    /// PROVENANCE, because most of this is the part of fuel that did not survive.
    /// The transfer amount, the depletion loop and the per-engine burn rates lived on
    /// the GSim (Scala), which is gone. The shipped client carries no fuel tunable:
    /// <c>ShipConfiguration</c> has ~40 flight knobs and not one fuel entry,
    /// <c>ConfigKeys</c> has no fuel key, and every fuel schema field defaults to
    /// proto zero.
    ///
    /// TWO numbers here are nonetheless RECOVERED and are marked as such:
    /// <list type="bullet">
    /// <item>the 8/8/9 canister yield, in <see cref="FuelCanisterYield"/>, not
    /// touched by this file;</item>
    /// <item><see cref="GeneratorCapacity"/> = 100, which the community record and
    /// the client's own <c>FuelGaugeVisualizer</c> default agree on.</item>
    /// </list>
    /// Everything else below is <b>WAREBORN TUNING</b>, with its reasoning attached
    /// and an env override, because the first live flight is the only real test.
    /// See docs/plans/feature-roadmap.md 13.6 and 13.11.
    ///
    /// WHAT RETAIL DOES PIN, and what this module reproduces in shape if not in
    /// magnitude: consumption was CONTINUOUS and THROTTLE-DRIVEN, not per-action.
    /// <c>ShipEngineState</c> (1116) carries <c>throttle</c>, <c>power</c>,
    /// <c>spinup</c> and <c>consumption</c> as separate live floats, and the
    /// client's own engine audio scales its load parameter by their product
    /// (<c>EngineVisualizer.GetInefficiency</c> -> <c>UpdateAudio</c>). Half
    /// throttle costs half. An idling ship costs nothing.
    /// </summary>
    public static class ShipFuelPolicy
    {
        /// <summary>
        /// Explicit rollout gate for Track 7's hull-authored demand and durable
        /// per-generator tank lifecycle. The existing fuel and thrust-gate switches
        /// keep their historical defaults; this new behavior requires an affirmative
        /// value so merging the reconstruction cannot change production flight.
        /// </summary>
        public const string HullDemandLifecycleEnvVar = "WAREBORN_FUEL_HULL_DEMAND";

        // ------------------------------------------------------------------
        // Capacity
        // ------------------------------------------------------------------

        /// <summary>
        /// ONE POWER GENERATOR's capacity in fuel units. A hull's capacity is this
        /// times the number of generators bolted to it - see
        /// <see cref="ShipFuelLedger"/> for why the pool is a sum.
        ///
        /// <b>100 is RECOVERED, not invented</b>, and it is the one number in this
        /// file that is not Wareborn tuning. Two independent sources agree:
        /// <list type="bullet">
        /// <item>the community record ("a standard generator holds 100 units;
        /// multiple generators pool automatically"), and</item>
        /// <item>the shipped client's own default - <c>FuelGaugeVisualizer</c>
        /// initialises its needle with <c>SetFuelAmount(0f, 100f)</c>
        /// (acs/Assets.Scripts.Visualisers.Ship/FuelGaugeVisualizer.cs:56), the only
        /// <c>100f</c> anywhere near fuel in the decompile. That is the capacity the
        /// instrument assumes before a server ever speaks to it.</item>
        /// </list>
        /// It replaces a 250-per-hull figure that was explicitly WAREBORN TUNING
        /// ("ten canisters"), which a recovered number outranks.
        ///
        /// Four canisters (<see cref="FuelCanisterYield.TotalFuel"/> = 25 each) fill
        /// one generator exactly, which is a pleasant accident of two independently
        /// recovered numbers and is worth not rounding away.
        /// </summary>
        public const double GeneratorCapacity = 100.0;

        /// <summary>Floor for a configured per-generator capacity - one canister.</summary>
        public const double MinCapacity = 25.0;

        /// <summary>
        /// Ceiling for a configured capacity. The gauge's odometer is four digits
        /// plus a powers-of-1000 magnitude roller, so it renders far more than this;
        /// the cap exists to keep a typo from making fuel meaningless, not because
        /// the instrument cannot show it.
        /// </summary>
        public const double MaxCapacity = 100000.0;

        // ------------------------------------------------------------------
        // Burn
        // ------------------------------------------------------------------

        /// <summary>
        /// Fuel burnt per second at FULL throttle, PER MOUNTED ENGINE. A one-engine
        /// hull drains one generator in 400 s; two engines consume twice as quickly
        /// because retail carried consumption on each 1116 ShipEngineState. Bolt on
        /// a second generator and range doubles; bolt on a second engine and power
        /// and consumption both increase.
        /// WAREBORN TUNING: retail's burn rate lived on the GSim and is gone.
        /// </summary>
        public const double DefaultBurnPerSecond = 0.25;

        /// <summary>Floor for a configured burn rate. Zero would mean nothing burns fuel again.</summary>
        public const double MinBurnPerSecond = 0.001;

        /// <summary>Ceiling for a configured burn rate: one full generator in four seconds.</summary>
        public const double MaxBurnPerSecond = 25.0;

        // ------------------------------------------------------------------
        // Gauge push
        // ------------------------------------------------------------------

        /// <summary>
        /// Smallest change in the level that is worth a 1105 broadcast, in fuel
        /// units. Below this the needle would not visibly move: the client puts TWO
        /// smoothing stages in front of it - a <c>DelayedInterpolator</c> with
        /// <c>Delay = 2.0</c> seconds, then <c>Mathf.Lerp(current, target, 2f *
        /// Time.deltaTime)</c> - so sub-unit updates are pure wire cost.
        /// </summary>
        public const double GaugePushQuantum = 1.0;

        /// <summary>
        /// Minimum seconds between two 1105 pushes for the same gauge. One second
        /// against a needle that is deliberately two seconds behind the wire.
        /// This is the rate half of the standing multiplayer-safety rule.
        /// </summary>
        public const double GaugePushMinIntervalSeconds = 1.0;

        /// <summary>
        /// How often the fuel service integrates. Deliberately SLOWER than the
        /// flight cadence (0.24 s): fuel is an accumulator, and integrating it at
        /// 0.5 s costs a quarter of the work with no observable difference, because
        /// nothing downstream can see the level change faster than the gauge quantum
        /// above allows.
        /// </summary>
        public const double BurnIntervalSeconds = 0.5;

        // ------------------------------------------------------------------
        // The arithmetic
        // ------------------------------------------------------------------

        /// <summary>
        /// Fuel burnt by <paramref name="engineCount"/> engines holding
        /// <paramref name="throttle"/> for
        /// <paramref name="seconds"/>, at <paramref name="burnPerSecond"/> for full
        /// throttle.
        ///
        /// Proportional to the ABSOLUTE throttle: reverse costs the same as forward,
        /// because the engines are doing the same work. Never negative, never NaN -
        /// throttle arrives from client input, which is never trusted, and a bad
        /// value must cost nothing rather than refund fuel or take the server down.
        /// </summary>
        public static double BurnFor(double throttle, double seconds, double burnPerSecond,
            int engineCount = 1)
        {
            if (!IsFinite(throttle) || !IsFinite(seconds) || !IsFinite(burnPerSecond))
            {
                return 0.0;
            }
            if (seconds <= 0.0 || burnPerSecond <= 0.0 || engineCount <= 0)
            {
                return 0.0;
            }

            double magnitude = System.Math.Abs(throttle);
            if (magnitude <= 0.0)
            {
                return 0.0;
            }
            if (magnitude > 1.0)
            {
                magnitude = 1.0;
            }

            return magnitude * seconds * burnPerSecond * engineCount;
        }

        /// <summary>
        /// How much of <paramref name="offered"/> fuel actually fits in one generator
        /// holding <paramref name="level"/> of <paramref name="capacity"/>.
        ///
        /// Whole units only: fuel is an inventory item with an integer amount, so a
        /// partial unit could not be taken out of the player's stack and would
        /// silently vanish. Clamped at zero for a full tank, a garbage capacity or a
        /// negative offer.
        /// </summary>
        public static int DepositRoom(double level, double capacity, int offered)
        {
            if (offered <= 0 || !IsFinite(level) || !IsFinite(capacity) || capacity <= 0.0)
            {
                return 0;
            }

            double room = capacity - level;
            if (room <= 0.0)
            {
                return 0;
            }

            int whole = (int)System.Math.Floor(room);
            return whole < offered ? whole : offered;
        }

        // ------------------------------------------------------------------
        // Configuration
        // ------------------------------------------------------------------

        /// <summary>
        /// PER-GENERATOR capacity from <c>WAREBORN_FUEL_CAPACITY</c>. Unset or garbage
        /// falls back to the recovered 100; out of range clamps. Never throws - the
        /// same contract as <c>ShipMotionPolicy.SpeedFrom</c>, because a bad env var
        /// must not take the server down.
        ///
        /// NOTE FOR ANYONE WITH THIS SET IN PRODUCTION: its meaning changed with the
        /// move to per-generator tanks. It used to be a whole ship's capacity; it is
        /// now one generator's, and a two-generator ship gets twice it.
        /// </summary>
        public static double CapacityFrom(string? env) =>
            ParseClamped(env, GeneratorCapacity, MinCapacity, MaxCapacity);

        /// <summary>Burn rate from <c>WAREBORN_FUEL_BURN_RATE</c>. Same contract.</summary>
        public static double BurnRateFrom(string? env) =>
            ParseClamped(env, DefaultBurnPerSecond, MinBurnPerSecond, MaxBurnPerSecond);

        /// <summary>
        /// Whether the fuel subsystem runs at all, from <c>WAREBORN_FUEL</c>.
        /// DEFAULT ON. "0"/"false"/"off"/"no" turns the whole thing off: no burn, no
        /// gate, and the gauge reads a full static tank - which is exactly the
        /// pre-fuel behaviour of every ship on this server.
        /// </summary>
        public static bool EnabledFrom(string? env) => !IsOff(env);

        /// <summary>
        /// Whether an empty tank stops the engines, from
        /// <c>WAREBORN_FUEL_GATES_THRUST</c>. DEFAULT ON - a fuel level nothing acts
        /// on is the defect this subsystem exists to fix. The kill switch is here
        /// because it is the one part of fuel that can strand a player mid-flight,
        /// and that must be revertible without a rebuild.
        /// </summary>
        public static bool GatesThrustFrom(string? env) => !IsOff(env);

        /// <summary>
        /// Track 7 is DEFAULT OFF. Only an explicit 1/true/on/yes enables the new
        /// authoritative hull-demand source, per-engine burn, engine-only dry gate,
        /// and per-generator persistence. Unknown values fail closed to legacy fuel.
        /// </summary>
        public static bool HullDemandLifecycleEnabledFrom(string? env)
        {
            if (string.IsNullOrWhiteSpace(env)) return false;
            string value = env.Trim().ToLowerInvariant();
            return value == "1" || value == "true" || value == "on" || value == "yes";
        }

        private static bool IsOff(string? env)
        {
            if (string.IsNullOrWhiteSpace(env))
            {
                return false;
            }
            string value = env.Trim().ToLowerInvariant();
            return value == "0" || value == "false" || value == "off" || value == "no";
        }

        private static double ParseClamped(string? env, double fallback, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(env)
                || !double.TryParse(env, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double value)
                || !IsFinite(value)
                || value <= 0.0)
            {
                return fallback;
            }
            return System.Math.Clamp(value, min, max);
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
