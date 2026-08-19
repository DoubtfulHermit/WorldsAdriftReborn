using Bossa.Travellers.Player;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Networking
{
    /// <summary>
    /// The movement relay, second generation: latest-state in, fixed cadence
    /// out, one synthetic timebase per recipient.
    ///
    /// WHY IT EXISTS. The old path forwarded 190602/1073 as raw bytes,
    /// one-for-one, in arrival order, never coalescing - so nothing bounded how
    /// STALE a relayed position was, only how fast positions flowed. A live
    /// session measured exactly that: rates flat everywhere while rendered lag
    /// grew, because a FIFO draining as fast as it fills keeps rate flat while
    /// its contents age. Two independent diagnoses (ours and an external
    /// architecture review) converged on the same shape: ingest to latest state
    /// with stale/absurd samples dropped, emit at a fixed cadence, rewrite the
    /// pairing timestamp at emit into a timebase the server owns.
    ///
    /// THE THREE PIECES, all pure and unit-tested in Multiplayer:
    ///   - <see cref="MovementIngest"/>: latest-state tracking; drops timestamp
    ///     regressions, exact duplicates and impossible jumps; accumulates the
    ///     staleness discriminator.
    ///   - <see cref="CadenceTimer"/> / <see cref="RelayCadencePolicy"/>: the
    ///     20 Hz metronome (WAREBORN_RELAY_HZ to tune, clamped 15-30).
    ///   - <see cref="SyntheticTimeline"/> / <see cref="RelayTimestampPolicy"/>:
    ///     the per-(recipient, sender) stamp sequence every emitted 1073
    ///     carries - seed 0.2, one emit interval per sample.
    /// This class is only the glue: merge typed updates, serialize, send.
    ///
    /// THE FLAG. WAREBORN_RELAY_V2=0 restores the legacy raw path byte-for-byte
    /// (a live regression is reverted by env var, not redeploy); anything else
    /// is v2. There is deliberately NO intermediate "cadence without rewrite"
    /// mode: re-emitting a source timestamp under multiple positions makes the
    /// receiver's DiscardOutdatedValues collapse them - strictly worse than the
    /// raw path. Coalescing, cadence and the synthetic timeline are one atomic
    /// behaviour or none of it.
    ///
    /// WHAT IT NEVER TOUCHES: every component id other than 190602/1073 keeps
    /// the existing raw relay path, including the 1231/1037
    /// IsRelayedToOtherPlayers filter. The fall floor, teleport ack and mirror
    /// seeding read their data BEFORE this class sees anything, in the same
    /// handlers that feed it.
    /// </summary>
    internal sealed class RelayEmitter
    {
        /// <summary>
        /// v2 unless WAREBORN_RELAY_V2=0. Default ON: the raw path is the
        /// measured pathology, not the safe choice.
        /// </summary>
        internal static readonly bool V2Enabled =
            Environment.GetEnvironmentVariable("WAREBORN_RELAY_V2") != "0";

        /// <summary>
        /// WAREBORN_RELAY_TRACE=1: log the first <see cref="TraceEmits"/> emits,
        /// each with the gap since the previous emit and the age of the position
        /// it carried.
        ///
        /// WHY IT EXISTS. The relay's own 5 s stats say the cadence held - 20
        /// emits a second, no drops, no skips - while a two-bot harness measured
        /// the same server delivering every position either instantly or a whole
        /// interval late, decided at join and then fixed for the session. Those
        /// two accounts cannot both be complete, and the missing number is the
        /// only one neither reports: how long a position actually sat in the
        /// pending slot. Off by default; one Stopwatch read per accepted sample
        /// when on, and it stops itself after the sample is taken.
        /// </summary>
        internal static readonly bool Trace =
            Environment.GetEnvironmentVariable("WAREBORN_RELAY_TRACE") == "1";

        private const int TraceEmits = 400;
        private int _traced;
        private TimeSpan _lastTraceEmit = TimeSpan.MinValue;

        /// <summary>
        /// Whether this component id is coalesced by the emitter instead of
        /// being raw-relayed on arrival. The RelayToOtherPlayers gate.
        /// </summary>
        internal static bool CoalescesComponent(uint componentId)
        {
            return V2Enabled
                && (componentId == MirrorSendPolicy.TransformStateComponentId
                    || componentId == MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId);
        }

        /// <summary>
        /// Everything the emitter knows about one SENDER: the accumulated
        /// not-yet-emitted fields of each stream, and the last known
        /// position/rotation for the heartbeat re-send.
        /// </summary>
        private sealed class SenderState
        {
            /// <summary>Fields received (and accepted) since the last emit, or null.</summary>
            public TransformState.Update? PendingTransform;

            /// <summary>
            /// When the oldest not-yet-emitted 190602 field was accepted. Only
            /// maintained under WAREBORN_RELAY_TRACE: it is the age this emitter
            /// is ADDING to a position, which is the one number nothing else
            /// reports and which two whole days were spent inferring from a bot.
            /// </summary>
            public TimeSpan PendingTransformSince;
            public ClientAuthoritativePlayerState.Update? PendingPlayerState;

            /// <summary>
            /// The last accepted 190602 position/rotation, re-sent every tick
            /// the source is silent. A constant-position, advancing-stamp
            /// stream freezes the avatar CLEANLY during a source hitch; going
            /// silent drains the receiver's 5-slot queue and invites the hard
            /// snap on resume.
            /// </summary>
            public bool HasPosition;
            public FixedPointVector3 LastPosition;
            public bool HasRotation;
            public Quaternion32 LastRotation;

            // Stats-window counters.
            public long EmittedTransform;
            public long EmittedPlayerState;
            public long HeldShipDetachEdges;
            public long DomainAlignedEmits;
            public long BackpressureSkips;
        }

        private readonly Dictionary<ulong, SenderState> _senders = new();

        /// <summary>One synthetic 1073 timeline per (recipient, sender) pair.</summary>
        private readonly Dictionary<(ulong Recipient, ulong Sender), SyntheticTimeline> _timelines = new();
        private readonly Dictionary<ulong, RecipientRelayPressure> _pressureByRecipient = new();
        private readonly Dictionary<(ulong Recipient, ulong Sender), TimeSpan> _lastEmitByPair = new();

        private readonly MovementIngest _ingest;
        private readonly CadenceTimer _cadence;
        private readonly CadenceTimer _stats;
        private readonly IClock _clock;
        private readonly PlayerRegistry _players;
        private readonly double _stepSeconds;
        private readonly double _hz;

        public RelayEmitter(IClock clock, PlayerRegistry players)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _ingest = new MovementIngest(clock);

            _hz = RelayCadencePolicy.HzFrom(Environment.GetEnvironmentVariable("WAREBORN_RELAY_HZ"));
            _stepSeconds = RelayCadencePolicy.StepSecondsFor(_hz);
            _cadence = new CadenceTimer(RelayCadencePolicy.IntervalFor(_hz));
            _stats = new CadenceTimer(RelayCadencePolicy.StatsInterval);

            // Said once so a live A/B session's log states which arm it is.
            Console.WriteLine(V2Enabled
                ? "[info] movement relay v2 is ON: 190602/1073 coalesced and emitted at " + _hz
                    + " Hz on a synthetic timebase (WAREBORN_RELAY_V2=0 restores the raw path, WAREBORN_RELAY_HZ tunes the rate)."
                : "[info] movement relay v2 is OFF (WAREBORN_RELAY_V2=0): raw arrival-order relay, as before. Ingest statistics are still collected.");
        }

        /// <summary>
        /// The relay's effective emit rate in whole Hz, or 0 under the raw path
        /// (there is no fixed cadence then). For the operator dashboard.
        /// </summary>
        public int Hz => V2Enabled ? (int)Math.Round(_hz) : 0;

        /// <summary>
        /// A one-word-ish description of the current relay arm, matching the 5 s
        /// stats line: "v2@20Hz" or "raw". For the operator dashboard.
        /// </summary>
        public string ModeDescription => V2Enabled ? "v2@" + Hz + "Hz" : "raw";

        // ------------------------------------------------------------------
        // INGEST - called from the two typed handlers, AFTER their ownership
        // gates, with the update the game's own code already deserialized.
        // Never a second deserialization pass.
        // ------------------------------------------------------------------

        /// <summary>
        /// One 190602 the sender published about its own entity. Judged always
        /// (the staleness/drop counters are the A/B discriminator and must
        /// exist in BOTH arms); merged for emission only under v2.
        /// </summary>
        public void ObserveTransform(ulong senderPeerId, TransformState.Update update)
        {
            bool hasPosition = false;
            double x = 0, y = 0, z = 0;
            if (update.localPosition.HasValue)
            {
                Improbable.Collections.List<long> fp = update.localPosition.Value.fixedPointValues;
                if (fp != null && fp.Count >= 3)
                {
                    hasPosition = true;
                    x = (double)fp[0] / FixedPointPosition.UnitsPerMetre;
                    y = (double)fp[1] / FixedPointPosition.UnitsPerMetre;
                    z = (double)fp[2] / FixedPointPosition.UnitsPerMetre;
                }
            }

            // A parent change moves the position into another coordinate space;
            // distances across it are meaningless to the jump rule.
            MovementSample sample = new(
                hasTimestamp: false, timestamp: 0f,
                hasPosition: hasPosition, x, y, z,
                carriesSpaceChange: update.parent.HasValue);

            IngestVerdict verdict = _ingest.Observe(senderPeerId, MovementStream.Transform, sample);

            if (!V2Enabled)
            {
                return;
            }

            SenderState state = SenderFor(senderPeerId);
            bool keepMovement = verdict is IngestVerdict.Accept or IngestVerdict.AcceptReanchor;

            if (Trace && state.PendingTransform == null)
            {
                state.PendingTransformSince = _clock.Elapsed;
            }

            TransformState.Update pending = state.PendingTransform ??= new TransformState.Update();

            // EDGE FIELDS ARE NEVER DROPPED, whatever the verdict. The client
            // writer only puts them on the wire when they CHANGE, so a lost one
            // is a lost state transition forever - parent decides which SPACE
            // the receiver interprets positions in, and onReset is an event.
            if (update.parent.HasValue)
            {
                pending.SetParent(update.parent.Value);
            }
            if (update.pivot.HasValue)
            {
                pending.SetPivot(update.pivot.Value);
            }
            if (update.isSleeping.HasValue)
            {
                pending.SetIsSleeping(update.isSleeping.Value);
            }
            for (int i = 0; i < update.onReset.Count; i++)
            {
                pending.AddOnReset(update.onReset[i]);
            }

            if (keepMovement)
            {
                if (update.localPosition.HasValue)
                {
                    pending.SetLocalPosition(update.localPosition.Value);
                    state.HasPosition = true;
                    state.LastPosition = update.localPosition.Value;
                }
                if (update.localRotation.HasValue)
                {
                    pending.SetLocalRotation(update.localRotation.Value);
                    state.HasRotation = true;
                    state.LastRotation = update.localRotation.Value;
                }
                if (update.velocity.HasValue)
                {
                    pending.SetVelocity(update.velocity.Value);
                }
                if (update.angularVelocity.HasValue)
                {
                    pending.SetAngularVelocity(update.angularVelocity.Value);
                }
                // The 190602 timestamp passes through untouched: PlayerVisualizer
                // pairs positions with the 1073 stamp, not this one, and other
                // consumers saw the sender's value under the raw path too.
                if (update.timestamp.HasValue)
                {
                    pending.SetTimestamp(update.timestamp.Value);
                }
            }
        }

        /// <summary>
        /// One 1073 the sender published about its own entity. The SOURCE
        /// timestamp is read for ingest judgement and the staleness metric and
        /// then DISCARDED - it is a private accumulator on the sender's frame
        /// clock and describes nothing the receiver experiences once the server
        /// re-times the stream. Every emitted 1073 gets a synthetic stamp at
        /// emit instead.
        /// </summary>
        public void ObservePlayerState(ulong senderPeerId, ClientAuthoritativePlayerState.Update update,
            bool holdRelativeFrame = false, bool synthesizeRelativeDetach = false)
        {
            bool hasPosition = update.positionRelative.HasValue;
            Improbable.Math.Vector3f pos = hasPosition ? update.positionRelative.Value : default;

            // relativeTo / isRelativeToShip flips move positionRelative into
            // another object's space (island vs ship deck).
            bool spaceChange = synthesizeRelativeDetach
                || (!holdRelativeFrame && (update.relativeTo.HasValue
                    || update.isRelativeToShip.HasValue
                    || update.relativeToShipUid.HasValue));

            MovementSample sample = new(
                hasTimestamp: update.timestamp.HasValue,
                timestamp: update.timestamp.HasValue ? update.timestamp.Value : 0f,
                hasPosition, pos.X, pos.Y, pos.Z,
                carriesSpaceChange: spaceChange);

            IngestVerdict verdict = _ingest.Observe(senderPeerId, MovementStream.PlayerState, sample);

            if (!V2Enabled)
            {
                return;
            }

            SenderState state = SenderFor(senderPeerId);
            bool keepMovement = verdict is IngestVerdict.Accept or IngestVerdict.AcceptReanchor;

            if (holdRelativeFrame)
            {
                state.HeldShipDetachEdges++;
            }

            ClientAuthoritativePlayerState.Update pending = state.PendingPlayerState ??= new ClientAuthoritativePlayerState.Update();

            // Edge fields: written on CHANGE only, so never dropped. This is
            // also what carries the teleport ack (lastExecutedRequest) - the
            // server's own ack path already ran in the handler; this keeps the
            // relayed copy other clients see from silently vanishing.
            if (update.lastExecutedRequest.HasValue)
            {
                pending.SetLastExecutedRequest(update.lastExecutedRequest.Value);
            }
            if (update.knockedOut.HasValue)
            {
                pending.SetKnockedOut(update.knockedOut.Value);
            }
            if (update.grounded.HasValue)
            {
                pending.SetGrounded(update.grounded.Value);
            }
            // A moving ship's adjacent colliders produce brief Invalid/bias=0
            // samples. AboardTracker intentionally bridges those gaps. Do not let
            // the relay contradict that canonical state: PlayerVisualizer would
            // lower its relative bias and blend toward the avatar's stale absolute
            // 190602 until the hull stops, which is the observed trail-then-snap.
            if (synthesizeRelativeDetach)
            {
                pending.SetRelativeTo(new Improbable.EntityId(-1));
                pending.SetRelativeBias(0f);
                pending.SetIsRelativeToShip(new Improbable.Collections.Option<bool>(false));
                pending.SetRelativeToShipUid(new Improbable.Collections.Option<long>(-1));
            }
            else if (!holdRelativeFrame)
            {
                if (update.relativeTo.HasValue)
                {
                    pending.SetRelativeTo(update.relativeTo.Value);
                }
                if (update.relativeBias.HasValue)
                {
                    pending.SetRelativeBias(update.relativeBias.Value);
                }
                if (update.isRelativeToShip.HasValue)
                {
                    pending.SetIsRelativeToShip(update.isRelativeToShip.Value);
                }
                if (update.relativeToShipUid.HasValue)
                {
                    pending.SetRelativeToShipUid(update.relativeToShipUid.Value);
                }
            }

            if (keepMovement)
            {
                if (update.positionRelative.HasValue)
                {
                    pending.SetPositionRelative(update.positionRelative.Value);
                }
                if (update.rotationRelative.HasValue)
                {
                    pending.SetRotationRelative(update.rotationRelative.Value);
                }
                if (update.boneData.HasValue)
                {
                    pending.SetBoneData(update.boneData.Value);
                }
                // update.timestamp: deliberately NOT merged - see the summary.
            }
        }

        private SenderState SenderFor(ulong peerId)
        {
            if (!_senders.TryGetValue(peerId, out SenderState? state))
            {
                state = new SenderState();
                _senders[peerId] = state;
            }
            return state;
        }

        /// <summary>
        /// The last accepted 190602 position this peer published about its own
        /// avatar, in the game's fixed-point wire units, or false when none is
        /// held (relay v2 off - the raw path never stores it - or the peer has
        /// not moved yet). CAVEAT for callers doing distance checks: this is the
        /// position in whatever SPACE the client publishes in - world space on
        /// foot, but SHIP-LOCAL while parented to a moving hull - so gate on the
        /// aboard tracker before treating it as a world coordinate. Used by the
        /// station-pickup transaction's authoritative range check.
        /// </summary>
        internal bool TryLastPosition(ulong senderPeerId, out FixedPointPosition position)
        {
            position = default;
            if (!_senders.TryGetValue(senderPeerId, out SenderState? state) || !state.HasPosition)
            {
                return false;
            }

            Improbable.Collections.List<long> fp = state.LastPosition.fixedPointValues;
            if (fp == null || fp.Count < 3)
            {
                return false;
            }

            position = new FixedPointPosition(fp[0], fp[1], fp[2]);
            return true;
        }

        // ------------------------------------------------------------------
        // EMIT - driven by ONE call per main-loop turn.
        // ------------------------------------------------------------------

        /// <summary>
        /// The emitter's heartbeat: emits every sender's latest state to every
        /// other player when the cadence says so, and the statistics every
        /// 5 s. Cheap when idle - two Stopwatch comparisons.
        /// </summary>
        public void Tick(IReadOnlySet<ulong>? domainFrameSenders = null)
        {
            TimeSpan now = _clock.Elapsed;

            bool regularCadenceDue = V2Enabled && _cadence.Due(now);
            if (regularCadenceDue
                || (V2Enabled && domainFrameSenders != null && domainFrameSenders.Count > 0))
            {
                EmitAll(regularCadenceDue, domainFrameSenders);
            }

            if (_stats.Due(now))
            {
                LogStats();
            }
        }

        private void EmitAll(bool regularCadenceDue, IReadOnlySet<ulong>? domainFrameSenders)
        {
            foreach (KeyValuePair<ulong, SenderState> entry in _senders)
            {
                ulong senderId = entry.Key;
                SenderState state = entry.Value;

                bool aboardDomainFrame = domainFrameSenders?.Contains(senderId) == true;
                if (!DomainAlignedRelayPolicy.ShouldEmitSender(
                        regularCadenceDue, aboardDomainFrame))
                {
                    continue;
                }
                if (!regularCadenceDue && aboardDomainFrame)
                {
                    state.DomainAlignedEmits++;
                }

                // Nothing worth animating until a first position exists.
                if (!state.HasPosition)
                {
                    continue;
                }

                long? entityId = _players.EntityOf(senderId);
                if (entityId == null)
                {
                    continue;
                }

                IReadOnlyList<(ulong PeerId, long EntityId)> others = _players.Others(senderId);
                if (others.Count == 0)
                {
                    // Nobody to speak to. The pending updates keep merging -
                    // latest-state, so this is bounded - and the first observer
                    // to join is seeded by the mirror anyway.
                    continue;
                }

                // Take the accumulated fields; from here the tick owns them.
                ClientAuthoritativePlayerState.Update playerState =
                    state.PendingPlayerState ?? new ClientAuthoritativePlayerState.Update();
                state.PendingPlayerState = null;

                TransformState.Update transform = state.PendingTransform ?? new TransformState.Update();
                bool carriedFreshPosition = state.PendingTransform != null;
                state.PendingTransform = null;

                if (Trace && _traced < TraceEmits)
                {
                    TimeSpan traceNow = _clock.Elapsed;
                    double sinceLastEmitMs = _lastTraceEmit == TimeSpan.MinValue
                        ? double.NaN
                        : (traceNow - _lastTraceEmit).TotalMilliseconds;
                    _lastTraceEmit = traceNow;
                    _traced++;
                    Console.WriteLine("[relay-trace] sender 0x" + senderId.ToString("x")
                        + " emitGap=" + sinceLastEmitMs.ToString("0.###")
                        + "ms pendingAge=" + (carriedFreshPosition
                            ? (traceNow - state.PendingTransformSince).TotalMilliseconds.ToString("0.###")
                            : "heartbeat")
                        + " cadenceDue=" + regularCadenceDue
                        + " skips=" + _cadence.SkippedIntervals);
                }

                // The heartbeat: a 190602 with no fresh position re-sends the
                // last accepted one, so every emitted 1073 stamp has a position
                // to pair with and the receiver's queue never runs dry.
                if (!transform.localPosition.HasValue)
                {
                    transform.SetLocalPosition(state.LastPosition);
                }
                if (!transform.localRotation.HasValue && state.HasRotation)
                {
                    transform.SetLocalRotation(state.LastRotation);
                }

                // The transform payload is identical for every recipient:
                // serialize once. The 1073 payload differs per recipient (its
                // stamp is the recipient's own timeline), so it cannot be.
                byte[]? transformPayload = SendOPHelper.SerializeComponentUpdatePayload(
                    MirrorSendPolicy.TransformStateComponentId, transform);

                foreach ((ulong targetId, long _) in others)
                {
                    ENetPeerHandle? target = PeerIdentity.Instance.Resolve(new IntPtr((long)targetId));
                    if (target == null)
                    {
                        continue;
                    }

                    // The mirror seed establishes this recipient's synthetic
                    // timestamp epoch. Do not race live movement ahead of the
                    // entity or either seed component: .25 -> seed .20 -> .25
                    // is a real delivered regression that splits the avatar.
                    if (!MirrorSendPolicy.MayRelayMovement(
                            WorldsAdriftRebornGameServer.SentEntities.WasSent(target, entityId.Value),
                            WorldsAdriftRebornGameServer.ServedComponents.HasServed(target, entityId.Value,
                                MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId),
                            WorldsAdriftRebornGameServer.ServedComponents.HasServed(target, entityId.Value,
                                MirrorSendPolicy.TransformStateComponentId)))
                    {
                        continue;
                    }

                    // Advance the synthetic clock even when this recipient is
                    // pressure-limited, so a later delivered sample describes
                    // elapsed emit time rather than playing the avatar in slow
                    // motion. Healthy recipients take this path every time.
                    playerState.SetTimestamp(TimelineFor(targetId, senderId).Next(_stepSeconds));
                    RecipientRelayPressure pressure = PressureFor(targetId, target);
                    (ulong Recipient, ulong Sender) pair = (targetId, senderId);
                    TimeSpan? lastSent = _lastEmitByPair.TryGetValue(pair, out TimeSpan last)
                        ? last
                        : null;
                    if (!RelayBackpressurePolicy.IsDue(_clock.Elapsed, lastSent, pressure))
                    {
                        state.BackpressureSkips++;
                        continue;
                    }
                    _lastEmitByPair[pair] = _clock.Elapsed;
                    byte[]? playerStatePayload = SendOPHelper.SerializeComponentUpdatePayload(
                        MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId, playerState);

                    // 1073 FIRST, 190602 SECOND, always: the receiver pairs each
                    // arriving 190602 position with the LATEST 1073 stamp it
                    // holds, so the stamp must land before its position.
                    if (playerStatePayload != null && SendOPHelper.SendRawComponentUpdateOp(
                        target, entityId.Value, MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId, playerStatePayload))
                    {
                        state.EmittedPlayerState++;
                    }

                    if (transformPayload != null && SendOPHelper.SendRawComponentUpdateOp(
                        target, entityId.Value, MirrorSendPolicy.TransformStateComponentId, transformPayload))
                    {
                        state.EmittedTransform++;
                    }
                }
            }
        }

        private SyntheticTimeline TimelineFor(ulong recipientId, ulong senderId)
        {
            (ulong, ulong) key = (recipientId, senderId);
            if (!_timelines.TryGetValue(key, out SyntheticTimeline? timeline))
            {
                timeline = new SyntheticTimeline();
                _timelines[key] = timeline;
            }
            return timeline;
        }

        private RecipientRelayPressure PressureFor(ulong recipientId, ENetPeerHandle peer)
        {
            RecipientRelayPressure current = _pressureByRecipient.TryGetValue(
                recipientId, out RecipientRelayPressure known)
                ? known
                : RecipientRelayPressure.Normal;
            if (EnetPeerProbe.TryRead(peer.DangerousGetHandle(), out EnetPeerHealth health))
            {
                current = RelayBackpressurePolicy.Next(current, health.RoundTripTimeMs);
                _pressureByRecipient[recipientId] = current;
            }
            return current;
        }

        // ------------------------------------------------------------------
        // SEED COORDINATION
        // ------------------------------------------------------------------

        /// <summary>
        /// The 1073 seed was just serialized for this recipient about this
        /// entity. The seed carries stamp <see cref="RelayTimestampPolicy.SeedTimestampSeconds"/>,
        /// so the recipient's timeline for that entity's owner restarts: the
        /// next live emit is one step past the seed, and stream and seed can
        /// never disagree about the epoch (a reconnect is a new incarnation).
        /// </summary>
        public void OnSeed1073Served(ulong recipientPeerId, long entityId)
        {
            foreach ((ulong ownerPeer, long ownedEntity) in _players.All())
            {
                if (ownedEntity != entityId)
                {
                    continue;
                }
                if (ownerPeer != recipientPeerId)
                {
                    // A recipient's seed of its OWN 1073 feeds no interpolator;
                    // only remote views carry a timeline.
                    TimelineFor(recipientPeerId, ownerPeer).ResetIncarnation();
                }
                return;
            }
        }

        // ------------------------------------------------------------------
        // LIFECYCLE
        // ------------------------------------------------------------------

        /// <summary>
        /// Drops EVERYTHING keyed by this peer: its sender state, its ingest
        /// baselines, and every timeline it appears in as sender OR recipient.
        /// Part of ForgetPeer's everything-contract.
        /// </summary>
        public void Forget(ulong peerId)
        {
            _senders.Remove(peerId);
            _ingest.Forget(peerId);

            List<(ulong, ulong)> dead = new();
            foreach ((ulong recipient, ulong sender) key in _timelines.Keys)
            {
                if (key.recipient == peerId || key.sender == peerId)
                {
                    dead.Add(key);
                }
            }
            foreach ((ulong, ulong) key in dead)
            {
                _timelines.Remove(key);
                _lastEmitByPair.Remove(key);
            }
            _pressureByRecipient.Remove(peerId);
        }

        // ------------------------------------------------------------------
        // STATISTICS - the 5 s line that makes a live A/B test readable.
        // ------------------------------------------------------------------

        private void LogStats()
        {
            foreach (ulong peerId in _ingest.KnownPeers())
            {
                IngestWindowStats window = _ingest.SnapshotAndReset(peerId);

                long emittedTransform = 0;
                long emittedPlayerState = 0;
                long heldShipDetachEdges = 0;
                long domainAlignedEmits = 0;
                long backpressureSkips = 0;
                if (_senders.TryGetValue(peerId, out SenderState? state))
                {
                    emittedTransform = state.EmittedTransform;
                    emittedPlayerState = state.EmittedPlayerState;
                    state.EmittedTransform = 0;
                    state.EmittedPlayerState = 0;
                    heldShipDetachEdges = state.HeldShipDetachEdges;
                    state.HeldShipDetachEdges = 0;
                    domainAlignedEmits = state.DomainAlignedEmits;
                    state.DomainAlignedEmits = 0;
                    backpressureSkips = state.BackpressureSkips;
                    state.BackpressureSkips = 0;
                }

                if (window.IsEmpty && emittedTransform == 0 && emittedPlayerState == 0)
                {
                    continue;
                }

                long badPairs = 0;
                foreach (KeyValuePair<(ulong Recipient, ulong Sender), SyntheticTimeline> t in _timelines)
                {
                    if (t.Key.Sender == peerId)
                    {
                        badPairs += t.Value.BadPairs;
                    }
                }

                // One line per peer per 5 s: rare enough to be free, and the
                // whole live diagnosis in one greppable place.
                //   kept/drops  - what the ingest thought of the inbound stream;
                //   staleness   - Σ(wallΔ − senderTsΔ), the sender-clock-vs-
                //                 backlog discriminator (see IngestWindowStats);
                //   emitted     - what the cadence put out (0 in raw mode);
                //   badTsPairs  - lifetime count of non-increasing synthetic
                //                 stamps; NONZERO MEANS THE REWRITE IS WRONG.
                Console.WriteLine("[relay-stats] peer 0x" + peerId.ToString("x")
                    + ": kept=" + window.Accepted
                    + " drops(reg=" + window.TimestampRegressions
                    + ",dup=" + window.Duplicates
                    + ",jump=" + window.AbsurdJumps + ")"
                    // Sign built by hand: a two-section format ("+0.000;-0.000")
                    // reformats a negative that ROUNDS to zero through the
                    // positive section while keeping the minus - "-+0.000".
                    + " staleness=" + (window.StalenessSeconds < 0 ? "-" : "+")
                    + Math.Abs(window.StalenessSeconds).ToString("0.000") + "s"
                    + " emitted(190602=" + emittedTransform + ",1073=" + emittedPlayerState + ")"
                    + " heldShipDetach=" + heldShipDetachEdges
                    + " domainAligned=" + domainAlignedEmits
                    + " pressureSkips=" + backpressureSkips
                    + " badTsPairs=" + badPairs
                    // NONZERO AND RISING MEANS THE CADENCE IS SLIPPING. A whole
                    // emit interval went by without this loop coming back, so
                    // that window was long enough to hold two of a sender's
                    // publishes - and the older one was coalesced away, i.e. a
                    // published position that no other client will ever see.
                    // This is the server-side twin of the soak's delivery
                    // shortfall; the soak needs two bots, this needs nothing.
                    + " cadenceSkips=" + _cadence.SkippedIntervals
                    + " mode=" + (V2Enabled ? "v2@" + _hz + "Hz" : "raw"));
            }
        }
    }
}
