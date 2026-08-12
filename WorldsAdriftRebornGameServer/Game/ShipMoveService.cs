using System.Diagnostics;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// STEP 3, THE GATE. Fires ONE 1130 control point that translates the spawned
    /// hull a few metres, so a human can prove the single unverified assumption
    /// the whole ferry rests on: does a player STANDING ON the hull get carried
    /// with it?
    ///
    /// findings-first-ship.md, "NOT VERIFIED #1": there is no explicit carry code.
    /// A player on a deck is not parented to it; carrying relies entirely on PhysX
    /// friction between the player's dynamic rigidbody and the hull's kinematic
    /// mesh colliders. The doc's instruction is exact - "spawn a static ship,
    /// stand on it, send one 1130 update moving it 5 m, watch" - and this is that
    /// one update, on demand, so the answer is known before step 4 is trusted.
    ///
    /// HOW A HUMAN FIRES IT, mirroring the teleport trigger file
    /// (<see cref="TeleportService"/>) so the two read alike:
    /// <code>
    ///   echo          &gt; /tmp/wareborn-ship   # default: 5 m north (+Z)
    ///   echo 'nudge 8'&gt; /tmp/wareborn-ship   # 8 m north
    ///   echo '3 0 0'  &gt; /tmp/wareborn-ship   # 3 m along +X
    /// </code>
    /// The path is <c>WAREBORN_SHIP_FILE</c>, default <c>/tmp/wareborn-ship</c>.
    /// The file is consumed once read, so one write is one move. A file, not a
    /// keypress, for the same reason teleport uses one: the server runs detached
    /// or over ssh with no TTY.
    ///
    /// EACH NUDGE ACCUMULATES. The service tracks the hull's current commanded
    /// position (starting at its 190602/1130 seed), so two nudges in a row walk
    /// the ship two steps rather than both moving it from the origin. Each point
    /// carries ZERO velocity - the ship glides to the new spot and stops, which is
    /// the safe seed the client can hold indefinitely. Whether that glide reads as
    /// a smooth carry or a snap is precisely what the human is here to see; the
    /// ferry's 0.24 s stream is the continuous-motion version.
    /// </summary>
    internal sealed class ShipMoveService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private const string DefaultTriggerFile = "/tmp/wareborn-ship";

        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private readonly string _triggerFile;

        /// <summary>
        /// The hull's current commanded position. Null until the first nudge, when
        /// it is seeded from the registry; thereafter it accumulates so repeated
        /// nudges walk rather than reset.
        /// </summary>
        private FixedPointPosition? _current;

        public ShipMoveService()
        {
            string? configured = Environment.GetEnvironmentVariable("WAREBORN_SHIP_FILE");
            _triggerFile = string.IsNullOrWhiteSpace(configured) ? DefaultTriggerFile : configured.Trim();
        }

        /// <summary>The trigger file path, for the startup banner.</summary>
        public string TriggerFile => _triggerFile;

        /// <summary>
        /// Reads and consumes the trigger file if it is there and the poll
        /// interval has elapsed. Safe to call every main-loop turn; cheap when
        /// idle (one <c>File.Exists</c> twice a second).
        /// </summary>
        public void PollTrigger()
        {
            if (_sinceLastPoll.Elapsed < PollInterval)
            {
                return;
            }
            _sinceLastPoll.Restart();

            string text;
            try
            {
                if (!File.Exists(_triggerFile))
                {
                    return;
                }

                // Read then delete, so a move fires exactly once per write. Delete
                // before acting: if anything below throws, the file is already
                // gone and the server cannot get stuck moving on a loop.
                text = File.ReadAllText(_triggerFile);
                File.Delete(_triggerFile);
            }
            catch (Exception e)
            {
                Console.WriteLine("[warning] ship: could not read " + _triggerFile + ": " + e.Message);
                return;
            }

            foreach (string candidate in text.Split('\n'))
            {
                if (ShipNudgePolicy.TryParseCommand(candidate, out ShipNudge nudge, out string error))
                {
                    Execute(nudge);
                }
                else if (error.Length > 0)
                {
                    Console.WriteLine("[warning] ship: " + error + ".");
                }
            }
        }

        private void Execute(ShipNudge nudge)
        {
            if (!ShipPublisher.TryResolveShip(out long entityId, out FixedPointPosition seed))
            {
                Console.WriteLine("[warning] ship: no ship is in the world yet (nothing has walked the spawn plan);"
                    + " connect a client first, then nudge.");
                return;
            }

            FixedPointPosition from = _current ?? seed;
            FixedPointPosition to = new FixedPointPosition(
                from.X + (long)(nudge.Dx * FixedPointPosition.UnitsPerMetre),
                from.Y + (long)(nudge.Dy * FixedPointPosition.UnitsPerMetre),
                from.Z + (long)(nudge.Dz * FixedPointPosition.UnitsPerMetre));

            // ONE control point, zero velocity, timestamped now. The seed went out
            // at checkout seconds ago, so this point is far past the client's
            // PreviousControlPoint and clears the 0.228 s cadence floor with ease.
            ShipControlPointSpec spec = new ShipControlPointSpec(
                ShipHull.NowMillisecondsSinceEpoch(),
                to.MetresX, to.MetresY, to.MetresZ,
                0.0, 0.0, 0.0,
                arrived: true);

            int sent = ShipPublisher.Broadcast(entityId, ShipPublisher.BuildUpdate(spec));
            // Wake the bolted parts in the same breath as the hull move so the deck
            // and helm follow this nudge immediately rather than waiting up to half a
            // heartbeat; the standalone heartbeat then keeps them awake for the glide.
            ShipPartMotionService.PublishWake(entityId);
            _current = to;

            if (sent == 0)
            {
                Console.WriteLine("[warning] ship: nudge " + nudge + " built for entity " + entityId
                    + " but no fully-loaded client received it (nobody connected, or still loading).");
            }
            else
            {
                Console.WriteLine("[info] ship: CARRY TEST - moved entity " + entityId + " by " + nudge
                    + " to " + to + " (one 1130 control point, zero velocity), sent to " + sent
                    + " client(s). Stand on the beams and watch whether you travel with it.");
            }
        }
    }
}
