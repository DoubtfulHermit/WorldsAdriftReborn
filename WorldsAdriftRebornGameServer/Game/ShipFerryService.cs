using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// STEP 4, THE MILESTONE. A path publisher: once per
    /// <see cref="ShipMotionPolicy.SendIntervalSeconds"/> it emits one 1130
    /// control point that flies the spawned hull along a straight line, and every
    /// client's <c>SSPDeadReckoningVisualizer</c> -&gt; <c>PathFollower</c> does
    /// the motion. No client patch, no physics engine - a control point is just
    /// {timestamp, position, rotation, velocity, fsimIdHash}.
    ///
    /// OFF BY DEFAULT. It flies only under <c>WAREBORN_SHIP_FERRY=1</c>, because it
    /// has never been in front of a running client and a moving ship carrying a
    /// player is a large visible change. The step-3 carry probe
    /// (<see cref="ShipMoveService"/>) is meant to be believed FIRST.
    ///
    /// THE CONSTRAINTS IT HONOURS, all VERIFIED in ~/Games/WAReborn-decompiled and
    /// pinned by <see cref="ShipMotionPolicy"/> / <see cref="ShipFerryPlan"/>:
    /// <list type="bullet">
    /// <item>Cadence: emitted at exactly one <see cref="ShipMotionPolicy.SendIntervalSeconds"/>
    ///   per point, timestamps on an ideal grid, so no pair ever crowds the
    ///   client's 0.228 s reject floor. Sent RELIABLY (<see cref="ShipPublisher"/>).</item>
    /// <item>fsimIdHash: one constant marker for the whole flight
    ///   (<see cref="ShipHull.FsimIdHash"/>) - a change costs half a second of
    ///   ignored motion, a WorkerId collision a silent drop.</item>
    /// <item>Timestamps: NTP wall-clock ms since the 2018 epoch
    ///   (<see cref="ShipHull.NowMillisecondsSinceEpoch"/>), so the client's own
    ///   server-latency estimate stays sane and its playback buffer populated.</item>
    /// </list>
    ///
    /// THE KNOWN RISK IT CANNOT SEE (findings-first-ship.md "NOT VERIFIED #3"):
    /// <c>PathFollower</c> samples on <c>SynchronisedTime.SmoothFixedNow</c>, which
    /// ONLY advances once NTP has synced (<c>_synced</c> true). If the ship never
    /// moves on a live client, the FIRST suspect is NTP, not these control points -
    /// check the client log for the <c>"NtpTimeKeeper failed to sync"</c>
    /// ErrorOnce. (A prior session log showed zero occurrences and the machine
    /// reached pool.ntp.org, so the catastrophic mode has no evidence behind it -
    /// but it is the thing to rule out first.)
    /// </summary>
    internal sealed class ShipFerryService
    {
        /// <summary>Flies only when explicitly switched on. Not "anything but 0" - a bare OFF default.</summary>
        internal static readonly bool Enabled =
            Environment.GetEnvironmentVariable("WAREBORN_SHIP_FERRY") == "1";

        /// <summary>
        /// How many extra zero-velocity resting points to emit AFTER arrival
        /// before falling silent. Belt and braces against a dropped final packet:
        /// the ship must not be left mid-air because the one point that stopped it
        /// was the one that got lost. Cheap - a handful of reliable sends.
        /// </summary>
        private const int RestRepeats = 4;

        private const double DefaultDistanceNorthMetres = 300.0;

        private readonly IClock _clock;
        private readonly CadenceTimer _cadence;
        private readonly double _speed;

        private ShipFerryPlan? _plan;
        private long _index;
        private int _restEmitted;
        private bool _done;
        private bool _announced;

        public ShipFerryService(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _cadence = new CadenceTimer(TimeSpan.FromSeconds(ShipMotionPolicy.SendIntervalSeconds));
            _speed = ShipMotionPolicy.SpeedFrom(Environment.GetEnvironmentVariable("WAREBORN_SHIP_FERRY_SPEED"));

            if (Enabled)
            {
                Console.WriteLine("[info] ship ferry is ARMED (WAREBORN_SHIP_FERRY=1): the hull will fly a "
                    + "straight path at " + _speed.ToString("0.#") + " m/s once a client is in the world, one "
                    + "1130 control point every " + (ShipMotionPolicy.SendIntervalSeconds * 1000).ToString("0")
                    + " ms. WAREBORN_SHIP_FERRY_DISTANCE / WAREBORN_SHIP_FERRY_TO set the destination.");
            }
        }

        /// <summary>
        /// The emitter's heartbeat, one call per main-loop turn. Cheap when off or
        /// idle (an env check and one Stopwatch compare). The flight starts on the
        /// first due tick at which a ship and at least one loaded client both
        /// exist, and stops a few points after arrival.
        /// </summary>
        public void Tick()
        {
            if (!Enabled || _done)
            {
                return;
            }

            if (!_cadence.Due(_clock.Elapsed))
            {
                return;
            }

            if (!ShipPublisher.TryResolveShip(out long entityId, out FixedPointPosition seed))
            {
                // No hull in the world yet. Burn the tick; try again in a step.
                return;
            }

            if (_plan == null)
            {
                _plan = BuildPlan(seed);
                _index = 0;
                Console.WriteLine("[info] ship ferry: STARTING flight of entity " + entityId + " from " + seed
                    + " over " + _plan.LengthMetres.ToString("0.#") + " m at " + _speed.ToString("0.#")
                    + " m/s (~" + (_plan.ArrivalIndex * ShipMotionPolicy.SendIntervalSeconds).ToString("0.#")
                    + " s, " + _plan.ArrivalIndex + " points).");
            }

            ShipControlPointSpec spec = _plan.Spec(_index);
            int sent = ShipPublisher.Broadcast(entityId, ShipPublisher.BuildUpdate(spec));

            if (sent > 0 && !_announced && _index == 0)
            {
                _announced = true;
                Console.WriteLine("[info] ship ferry: first control point away to " + sent + " client(s).");
            }

            _index++;

            if (spec.Arrived)
            {
                _restEmitted++;
                if (_restEmitted >= RestRepeats)
                {
                    _done = true;
                    Console.WriteLine("[info] ship ferry: entity " + entityId
                        + " has arrived and is at rest; publisher done. The hull holds its last"
                        + " zero-velocity control point.");
                }
            }
        }

        /// <summary>
        /// The flight the operator asked for. An absolute destination
        /// (<c>WAREBORN_SHIP_FERRY_TO=x,y,z</c>, global metres) wins; otherwise a
        /// hop <c>WAREBORN_SHIP_FERRY_DISTANCE</c> metres due north (+Z), the
        /// direction the hull already sits from spawn.
        /// </summary>
        private ShipFerryPlan BuildPlan(FixedPointPosition seed)
        {
            FixedPointPosition destination = ResolveDestination(seed);
            long anchorMs = ShipHull.NowMillisecondsSinceEpoch();
            return new ShipFerryPlan(seed, destination, _speed, ShipMotionPolicy.SendIntervalSeconds, anchorMs);
        }

        private static FixedPointPosition ResolveDestination(FixedPointPosition seed)
        {
            string? to = Environment.GetEnvironmentVariable("WAREBORN_SHIP_FERRY_TO");
            if (!string.IsNullOrWhiteSpace(to))
            {
                string[] parts = to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 3
                    && TryMetres(parts[0], out double x)
                    && TryMetres(parts[1], out double y)
                    && TryMetres(parts[2], out double z))
                {
                    return FixedPointPosition.FromMetres(x, y, z);
                }
                Console.WriteLine("[warning] ship ferry: WAREBORN_SHIP_FERRY_TO='" + to
                    + "' is not 'x,y,z' in metres; using the default north hop instead.");
            }

            double distance = DefaultDistanceNorthMetres;
            string? distanceEnv = Environment.GetEnvironmentVariable("WAREBORN_SHIP_FERRY_DISTANCE");
            if (!string.IsNullOrWhiteSpace(distanceEnv) && TryMetres(distanceEnv, out double parsed) && parsed != 0.0)
            {
                distance = parsed;
            }

            return new FixedPointPosition(seed.X, seed.Y, seed.Z + (long)(distance * FixedPointPosition.UnitsPerMetre));
        }

        private static bool TryMetres(string token, out double metres)
        {
            return double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out metres)
                && !double.IsNaN(metres) && !double.IsInfinity(metres);
        }
    }
}
