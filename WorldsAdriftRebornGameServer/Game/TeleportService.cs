using System.Diagnostics;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Fires teleports and watches for them landing. All the ENet-shaped work;
    /// every decision it makes lives in <see cref="TeleportPolicy"/> and
    /// <see cref="TeleportRequestCounter"/>, which are tested natively.
    ///
    /// HOW A HUMAN FIRES ONE. There is no command channel - the SDK's
    /// SendCommandRequest is a `// TODO` stub - so a client cannot ask to be
    /// teleported and this is deliberately not trying to invent one. Instead the
    /// server watches a file:
    ///
    /// <code>
    ///   echo haven         &gt; /tmp/wareborn-teleport   # everyone, to spawn
    ///   echo 'mausoleum'   &gt; /tmp/wareborn-teleport   # everyone, 4.4 km away
    ///   echo 'haven 3'     &gt; /tmp/wareborn-teleport   # just player entity 3
    /// </code>
    ///
    /// The path is <c>WAREBORN_TELEPORT_FILE</c>, defaulting to
    /// <c>/tmp/wareborn-teleport</c>. The file is consumed (deleted) once read,
    /// so one write is one teleport.
    ///
    /// A FILE rather than a keypress on purpose: this server is normally run
    /// detached or over ssh, where <c>Console.ReadKey</c> either throws on
    /// redirected input or silently never fires. A file works identically in a
    /// terminal, under systemd, and from a second ssh session, needs no TTY, and
    /// costs one <c>File.Exists</c> per poll.
    /// </summary>
    internal sealed class TeleportService
    {
        /// <summary>
        /// How often the trigger file is looked at. The main loop turns once per
        /// ENet EVENT, not once per 50 ms - a busy server spins it far faster
        /// than the poll timeout suggests - so the check is clock-gated rather
        /// than run every iteration. Half a second is imperceptible to whoever
        /// just typed `echo`, and it is why this costs nothing when idle.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

        private const string DefaultTriggerFile = "/tmp/wareborn-teleport";

        /// <summary>
        /// The label the operator trigger file's teleports carry in the log.
        /// </summary>
        private const string OperatorReason = "teleport";

        /// <summary>
        /// The label an automatic fall-floor rescue carries in the log, at BOTH
        /// ends: on the send, and again on the 1073 ack that proves it landed.
        /// Grep-able on purpose - "why did I end up back at spawn" and "did the
        /// rescue work" are the two questions this feature will ever be asked.
        /// </summary>
        internal const string FallRescueReason = "fall-rescue";

        private readonly TeleportRequestCounter _requests = new TeleportRequestCounter();

        /// <summary>
        /// Why the outstanding teleport for an entity was sent, so the ack can be
        /// logged in the same words as the send. Set on every send, read once on
        /// the ack; entries are dropped with the entity in <see cref="Forget"/>.
        /// </summary>
        private readonly Dictionary<long, string> _reasonByEntity = new Dictionary<long, string>();

        private readonly Stopwatch _sinceLastPoll = Stopwatch.StartNew();
        private readonly string _triggerFile;

        public TeleportService()
        {
            string? configured = Environment.GetEnvironmentVariable("WAREBORN_TELEPORT_FILE");
            _triggerFile = string.IsNullOrWhiteSpace(configured) ? DefaultTriggerFile : configured.Trim();
        }

        /// <summary>The trigger file path, for the startup banner.</summary>
        public string TriggerFile => _triggerFile;

        /// <summary>
        /// Puts 190607 TeleportRequestState on a player entity that has just
        /// finished first-time setup. Without it nothing here can move anybody:
        /// it is the third [Require] of TeleportTransformVisualizer, and the
        /// client does not reliably ask for it.
        ///
        /// It is NOT added to the authority grant. The server is the only writer;
        /// granting it would let any client teleport itself anywhere in the
        /// world. And the seed it carries has request 0, which by construction
        /// cannot fire a teleport - see TeleportComponent.Seed for why that is
        /// the difference between "teleport is available" and "everybody
        /// teleports the moment they load in".
        ///
        /// Deliberately non-fatal: a failure here costs teleport and nothing
        /// else.
        /// </summary>
        public void SeedOn(ENetPeerHandle peer, long entityId)
        {
            List<Structs.Structs.InterestOverride> teleport = new List<Structs.Structs.InterestOverride>
            {
                new Structs.Structs.InterestOverride(TeleportPolicy.TeleportRequestStateComponentId, 1),
            };

            if (SendOPHelper.SendAddComponentOp(peer, entityId, teleport))
            {
                Console.WriteLine("[info] teleport: seeded 190607 on entity " + entityId
                    + " (request " + TeleportPolicy.SeedRequest + ", parent absent). It can now be moved.");
            }
            else
            {
                Console.WriteLine("[warning] teleport: could not seed 190607 on entity " + entityId
                    + "; that player cannot be teleported this session.");
            }
        }

        /// <summary>
        /// Reads and consumes the trigger file if it is there and the poll
        /// interval has elapsed. Safe to call as often as you like.
        /// </summary>
        public void PollTrigger()
        {
            if (_sinceLastPoll.Elapsed < PollInterval)
            {
                return;
            }
            _sinceLastPoll.Restart();

            string line;
            try
            {
                if (!File.Exists(_triggerFile))
                {
                    return;
                }

                // Read then delete, so a teleport fires exactly once per write
                // even if it takes several polls to reach every peer. Delete
                // before acting: if anything below throws, the file is already
                // gone and the server cannot get stuck teleporting on a loop.
                line = File.ReadAllText(_triggerFile);
                File.Delete(_triggerFile);
            }
            catch (Exception e)
            {
                // A half-written file, a permissions change, a race with the
                // shell redirect: none of that is worth taking the server down.
                Console.WriteLine("[warning] teleport: could not read " + _triggerFile + ": " + e.Message);
                return;
            }

            foreach (string candidate in line.Split('\n'))
            {
                if (TeleportPolicy.TryParseCommand(candidate, out TeleportCommand command, out string error))
                {
                    Execute(command);
                }
                else if (error.Length > 0)
                {
                    Console.WriteLine("[warning] teleport: " + error + ".");
                }
            }
        }

        /// <summary>Carries out one parsed command.</summary>
        private void Execute(TeleportCommand command)
        {
            if (!command.Destination.LandsOnLoadedGround)
            {
                // Not a refusal - going somewhere with no ground is the whole
                // point of testing this - but it must be said out loud, because
                // there is still no fall damage and no world-edge pushback here,
                // so the arrival is a fall rather than a landing.
                //
                // The fall no longer lasts forever: FallPolicy catches it. Which
                // way that goes depends entirely on the destination's altitude,
                // and both cases are surprising if unannounced, so say which one
                // this is rather than making the operator work it out.
                Console.WriteLine("[warning] teleport: '" + command.Destination.Name
                    + "' has no entity spawned at it, so expect a fall. "
                    + (FallPolicy.IsBelowFloor(command.Destination.Position)
                        ? "It is BELOW the fall floor (" + FallPolicy.FloorMetres.ToString("0.#")
                          + " m), so the rescue fires on arrival and sends the player straight back to "
                          + TeleportPolicy.SafeDestination.Name + "."
                        : "The fall floor at " + FallPolicy.FloorMetres.ToString("0.#")
                          + " m will return the player to " + TeleportPolicy.SafeDestination.Name
                          + " a few seconds after arrival."));
            }

            int sent = 0;
            foreach ((ulong peerId, long entityId) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (command.EntityId.HasValue && command.EntityId.Value != entityId)
                {
                    continue;
                }

                if (Send(peerId, entityId, command.Destination))
                {
                    sent++;
                }
            }

            if (sent == 0)
            {
                Console.WriteLine("[warning] teleport: nobody to move ("
                    + (command.EntityId.HasValue
                        ? "entity " + command.EntityId.Value + " is not a connected player"
                        : "no players connected")
                    + ").");
            }
        }

        /// <summary>
        /// Puts a player who has fallen through the bottom of the world back on
        /// solid ground, by the same 190607 path the operator trigger file uses.
        ///
        /// The DECISION to do this is not taken here and is not taken in the
        /// packet loop either - it is <see cref="FallWatch.Observe"/>, which is
        /// pure and tested. This method is only the wire.
        ///
        /// Nothing else about the rescue is special: the same request counter,
        /// the same parentless 190607 update, the same 1073 ack. That is the
        /// whole reason a fall floor was a small change - the machinery to put a
        /// player somewhere already existed and had already been proven against
        /// a real client.
        /// </summary>
        public bool RescueFromFall(long entityId, FixedPointPosition where, int attempt)
        {
            TeleportDestination home = TeleportPolicy.SafeDestination;

            Console.WriteLine("[warning] " + FallRescueReason + ": entity " + entityId + " is at y "
                + where.MetresY.ToString("0.#") + " m, below the fall floor at "
                + FallPolicy.FloorMetres.ToString("0.#") + " m. It fell off the world; "
                + "sending it to " + home.Name + " (attempt " + attempt + " of "
                + FallWatch.MaxAttemptsPerFall + ").");

            foreach ((ulong peerId, long candidate) in WorldsAdriftRebornGameServer.Players.All())
            {
                if (candidate == entityId)
                {
                    return Send(peerId, entityId, home, FallRescueReason);
                }
            }

            // The peer went away between publishing that transform and this call.
            // Nothing to do, and nothing wrong: the watch record is dropped by
            // ForgetPeer along with everything else the peer owned.
            Console.WriteLine("[info] " + FallRescueReason + ": entity " + entityId
                + " is no longer a connected player, nothing to rescue.");
            return false;
        }

        /// <summary>
        /// Sends one player one teleport: a 190607 update carrying the
        /// destination and a fresh request number.
        /// </summary>
        private bool Send(ulong peerId, long entityId, TeleportDestination destination, string reason = OperatorReason)
        {
            ENetPeerHandle? peer = PeerIdentity.Instance.Resolve(new IntPtr((long)peerId));
            if (peer == null)
            {
                Console.WriteLine("[warning] teleport: peer 0x" + peerId.ToString("x") + " vanished.");
                return false;
            }

            // A client that has not finished first-time setup has not been given
            // 190607 yet, so an update would land on a component it does not
            // have. It is also still inside its loading screen, where a teleport
            // would be invisible and then overwritten by its own spawn.
            if (!PeerManager.Instance.clientSetupState.Contains(peer))
            {
                Console.WriteLine("[info] teleport: entity " + entityId + " is still loading, skipping.");
                return false;
            }

            int request = _requests.Next(entityId);
            _reasonByEntity[entityId] = reason;

            bool ok = SendOPHelper.SendComponentUpdateOp(
                peer,
                entityId,
                new List<uint> { TeleportPolicy.TeleportRequestStateComponentId },
                new List<object> { TeleportComponent.Request(destination.Position, request) });

            if (!ok)
            {
                Console.WriteLine("[error] " + reason + ": failed to send request " + request
                    + " for entity " + entityId + ".");
                return false;
            }

            Console.WriteLine("[info] " + reason + ": entity " + entityId + " -> " + destination.Name
                + " " + destination.Position + ", request " + request + ", awaiting 1073 ack.");
            return true;
        }

        /// <summary>
        /// Called with every 1073 <c>lastExecutedRequest</c> the client
        /// publishes. This is the ONLY evidence the server has that a teleport
        /// happened: <c>TeleportTransformVisualizer</c> writes the executed
        /// request number back into ClientAuthoritativePlayerState, a component
        /// we already grant the client authority over, which is precisely why
        /// this path needs no new grant.
        ///
        /// The client re-publishes 1073 every tick, so the field arrives
        /// constantly; the counter decides what is news so the log carries one
        /// line per landing rather than one per frame.
        /// </summary>
        public void OnAck(long entityId, int lastExecutedRequest)
        {
            int? outstandingBefore = _requests.Outstanding(entityId);

            if (!_requests.RecordAck(entityId, lastExecutedRequest))
            {
                return;
            }

            // Report the landing in the same words as the send, so a fall rescue
            // reads as one event with two halves rather than as an unexplained
            // teleport somebody has to correlate by request number.
            if (!_reasonByEntity.TryGetValue(entityId, out string? reason))
            {
                reason = OperatorReason;
            }

            if (outstandingBefore.HasValue && lastExecutedRequest >= outstandingBefore.Value)
            {
                Console.WriteLine("[success] " + reason + ": entity " + entityId
                    + " executed request " + lastExecutedRequest + ". It landed"
                    + (reason == FallRescueReason ? " - it is back on solid ground." : "."));
            }
            else
            {
                // Either an ack for something we never sent (a re-seed, another
                // writer) or one that does not yet cover the outstanding
                // request. Worth a line: this is what a half-applied teleport
                // looks like from here.
                Console.WriteLine("[info] " + reason + ": entity " + entityId + " reports lastExecutedRequest "
                    + lastExecutedRequest + ", outstanding "
                    + (outstandingBefore.HasValue ? outstandingBefore.Value.ToString() : "none") + ".");
            }
        }

        /// <summary>Drops an entity's counters when its peer disconnects.</summary>
        public void Forget(long entityId)
        {
            _requests.Forget(entityId);
            _reasonByEntity.Remove(entityId);
        }
    }
}
