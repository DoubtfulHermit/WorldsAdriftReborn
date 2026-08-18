using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using Improbable;
using Bossa.Travellers.Interact;
using Bossa.Travellers.Motion.Prediction;
using Bossa.Travellers.Player;
using Bossa.Travellers.Ship;

namespace RelayBot
{
    /// <summary>
    /// One headless client. Performs the join handshake the server expects
    /// (docs/multiplayer.md): connect -> ack every AssetLoadRequestOp -> ack
    /// every AddEntityOp -> on its OWN AddEntity (prefab context "Player") send
    /// SEND_COMPONENT_INTEREST -> receive seeds and the authority grant -> then
    /// publish 190602 TransformState and 1073 ClientAuthoritativePlayerState at
    /// client rate, walking a slow circle around its spawn seed, and measure the
    /// staleness of every relayed update it receives from the other bot.
    /// </summary>
    public sealed class Bot
    {
        private const uint TransformStateId = 190602;
        private const uint ClientAuthoritativePlayerStateId = 1073;
        private const uint ShipControlInputId = 1111;
        private const uint InteractAgentStateId = 1211;
        private const uint PredictedMotionStateId = 1130;

        /// <summary>
        /// The interest set the bot requests for its own entity. Every id here
        /// must have a seed branch in the server's ComponentsSerializer: the
        /// first-time-setup path answers with failOnComponentInitError=true, so
        /// ONE unhandled id would cost the whole batch - including the authority
        /// grant this bot cannot publish without. This is the remote-seed set
        /// (docs/multiplayer.md rule 7), all long-proven to serialize.
        /// </summary>
        private static readonly uint[] OwnInterest = { 190602, 1086, 1081, 1088, 1073, 6910, 1098 };

        /// <summary>Movement: a slow circle, radius 5 m, one lap per minute (~0.52 m/s).</summary>
        private const double CircleRadiusMetres = 5.0;
        private const double CircleSecondsPerLap = 60.0;

        /// <summary>~18 Hz, the real client's transform publish rate.</summary>
        private const double PublishIntervalMs = 1000.0 / 18.0;

        /// <summary>
        /// Filler for 1073's boneData so the relayed packet has a realistic
        /// size; a real client ships its skeleton's bone bytes every tick.
        /// </summary>
        private static readonly byte[] BoneFiller = new byte[120];

        public readonly int Index;
        public readonly string Name;

        private readonly string _host;
        private readonly int _port;
        private readonly Metrics _metrics;

        /// <summary>timestamp-bits -> Stopwatch ns at ENet_Send, per component. Shared: the
        /// RECEIVING bot resolves the other bot's entries.</summary>
        private readonly ConcurrentDictionary<(int bot, uint componentId, int tsBits), long> _sendLog;

        /// <summary>entityId -> bot index, filled by each bot for its OWN entity.</summary>
        private readonly ConcurrentDictionary<long, int> _entityOwners;

        private readonly CancellationToken _cancel;
        private readonly Enet.PollCallback _onDisconnect; // kept referenced: GC'ing a marshaled delegate is a crash

        private IntPtr _clientHost = IntPtr.Zero;
        private IntPtr _peer = IntPtr.Zero;

        // Public commands are queued onto the bot's ENet thread. The native host
        // is deliberately never touched from Program's orchestration thread.
        private readonly ConcurrentQueue<Action> _commands = new();
        private readonly HashSet<long> _seenEntities = new();
        private readonly ConcurrentQueue<long> _removedEntities = new();
        private readonly ConcurrentQueue<long> _readdedEntities = new();
        private readonly ConcurrentDictionary<long, (double X, double Y, double Z)> _hullFrames = new();
        private readonly HashSet<long> _faunaEntities = new();

        public long IslandEntityId { get; private set; } = -1;
        public long ShipHullEntityId { get; private set; } = -1;
        public long HelmEntityId { get; private set; } = -1;
        public long DeckEntityId { get; private set; } = -1;
        public long HullMotionUpdates { get; private set; }

        /// <summary>Creature entities this bot was shown. Zero unless WAREBORN_ISLAND_FAUNA is on.</summary>
        public long FaunaEntitiesAdded { get; private set; }

        /// <summary>190602 updates received for those creatures - the fauna pose sender, observed.</summary>
        public long FaunaPoseUpdates { get; private set; }

        /// <summary>
        /// When each creature ARRIVED (AddEntity received), in this process's
        /// monotonic nanoseconds. The plan costed fauna arrival at 0.24 s per
        /// creature from the server's 120 ms SendInterval x two sends
        /// (AssetLoadRequest, then AddEntity one cadence later); this is the
        /// measurement that checks that claim through the real pipeline instead
        /// of trusting it. Consecutive deltas within one island's stream are the
        /// per-creature arrival cost.
        /// </summary>
        private readonly List<long> _faunaArrivalNs = new();

        /// <summary>Snapshot of the fauna arrival instants, for the end-of-soak report.</summary>
        public long[] FaunaArrivalTimesNs { get { lock (_faunaArrivalNs) return _faunaArrivalNs.ToArray(); } }

        /// <summary>
        /// The IDENTITY seeds the server answered for creatures, decoded through
        /// the game's own generated codecs - the wire-level proof that the manta
        /// variant fix serializes and carries real values. Keys are human
        /// summaries ("1177 gender=Female", "4326 biome=Biome1 variant=unset",
        /// "4322 species=JellyFishFlower"), values are how often each was seen.
        /// </summary>
        private readonly Dictionary<string, int> _faunaIdentitySeeds = new();

        public KeyValuePair<string, int>[] FaunaIdentitySeeds
        {
            get { lock (_faunaIdentitySeeds) return _faunaIdentitySeeds.ToArray(); }
        }

        private void CountFaunaIdentity(string summary)
        {
            lock (_faunaIdentitySeeds)
            {
                _faunaIdentitySeeds.TryGetValue(summary, out int count);
                _faunaIdentitySeeds[summary] = count + 1;
            }
        }

        public long HelmWakeUpdates { get; private set; }
        public long RemoteAboardFrames { get; private set; }
        public long RemoteInvalidRelativeFrames { get; private set; }
        public double LastHullX { get; private set; }
        public double LastHullY { get; private set; }
        public double LastHullZ { get; private set; }
        public long LastHullTimestamp { get; private set; }
        public long TimelineViolationsObserved { get; private set; }

        private bool _shipAcceptanceMode;
        private double _acceptanceLocalX = 208.0;
        private double _acceptanceLocalY = 6.7;
        private double _acceptanceLocalZ = 4.0;
        private long _acceptanceRelativeTo = -1;
        private bool _acceptanceAboard;
        private int _invalidRelativeTicks;

        public long MyEntityId { get; private set; } = -1;
        public bool HasAuthority { get; private set; }
        public bool Disconnected { get; private set; }
        public string FailureReason { get; private set; }

        private double _centreX, _centreY, _centreZ;
        private bool _centreKnown;

        private long _publishStartNs = -1;
        private long _nextPublishNs = -1;
        private long _publishedCount;

        // Last time ANY relayed 190602/1073 arrived, for >1 s gap detection.
        // A gap is reported with its TRUE duration when it ENDS (or at shutdown):
        // the observed field failure was a silent 73 s hole, and "gap > 1 s"
        // logged at the crossing would have recorded it as one second.
        private long _lastRelayedReceiveNs = -1;
        private bool _gapOpen;
        private long _gapStartNs;

        /// <summary>
        /// Whether the server rewrites relayed 1073 timestamps onto its own
        /// synthetic per-recipient timeline (relay v2). Changes what a relayed
        /// 1073 stamp MEANS: not "the other bot's send clock" (matchable) but
        /// "the server's emission sequence" (verifiable only for monotonicity).
        /// </summary>
        private readonly bool _rewritten1073;

        /// <summary>Per sender bot: the last relayed 1073 stamp seen, for the monotonicity check.</summary>
        private readonly Dictionary<int, float> _lastRemote1073Stamp = new();

        public Bot(int index, string name, string host, int port, bool rewritten1073, Metrics metrics,
            ConcurrentDictionary<(int, uint, int), long> sendLog,
            ConcurrentDictionary<long, int> entityOwners,
            CancellationToken cancel,
            (double X, double Y, double Z)? centreOverride = null)
        {
            // WHY AN OVERRIDE EXISTS AT ALL. The bot normally latches its circle
            // centre from the 190602 spawn seed and walks a 5 m circle around it -
            // which is the Haven spawn, and the Haven spawn is 3.8 KM from the
            // nearest release-world island. Anything whose interest is island-scoped
            // is therefore unmeasurable by default: island fauna in particular is
            // never checked out to a bot no matter how the server is configured,
            // which was discovered the hard way after a soak reported a confident
            // FLAT while carrying exactly zero creatures. With this the bots can be
            // stood anywhere the world has islands, which is the only way the fauna
            // sender's rate can be soaked at all.
            if (centreOverride.HasValue)
            {
                _centreX = centreOverride.Value.X;
                _centreY = centreOverride.Value.Y;
                _centreZ = centreOverride.Value.Z;
                _centreKnown = true;
            }
            Index = index;
            Name = name;
            _host = host;
            _port = port;
            _rewritten1073 = rewritten1073;
            _metrics = metrics;
            _sendLog = sendLog;
            _entityOwners = entityOwners;
            _cancel = cancel;
            _onDisconnect = _ =>
            {
                Disconnected = true;
                _metrics.RecordDisconnect(Index, NowNs());
                Log("DISCONNECTED by server/transport.");
            };
        }

        private static long NowNs() => (long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));

        private void Log(string message)
        {
            Console.WriteLine($"[{Name}] {message}");
        }

        /// <summary>Blocking; run on a dedicated thread.</summary>
        public unsafe void Run()
        {
            try
            {
                _clientHost = Enet.CreateHost(0, 1, Enet.ChannelCount, 0, 0);
                if (_clientHost == IntPtr.Zero)
                {
                    FailureReason = "failed to create ENet client host";
                    return;
                }

                Log($"connecting to {_host}:{_port} ...");
                _peer = Enet.Connect(_host, _port, _clientHost, Enet.ChannelCount);
                if (_peer == IntPtr.Zero)
                {
                    FailureReason = $"could not connect to {_host}:{_port}";
                    return;
                }
                Log("connected.");

                IntPtr disconnectPtr = Marshal.GetFunctionPointerForDelegate(_onDisconnect);

                while (!_cancel.IsCancellationRequested && !Disconnected)
                {
                    // 5 ms poll: bounds receive latency jitter this harness itself
                    // adds. (The server's own loop polls at 50 ms; that half of
                    // the pipeline is part of what is being measured.)
                    Enet.PacketWrapper* packet = Enet.Poll(_clientHost, 5, IntPtr.Zero, disconnectPtr);
                    if (packet != null)
                    {
                        try
                        {
                            HandlePacket(packet);
                        }
                        finally
                        {
                            Enet.DestroyPacket((IntPtr)packet);
                        }
                    }

                    MaybePublish();
                    DrainCommands();
                    MaybeReportGap();
                }
            }
            catch (Exception ex)
            {
                FailureReason = ex.ToString();
            }
            finally
            {
                if (_peer != IntPtr.Zero && !Disconnected)
                {
                    Enet.Disconnect(_peer, _clientHost);
                }
                // Self-explaining death: "stopped. published 2 updates." with
                // the reason held silently in FailureReason is how the v2 gate
                // failure cost a diagnosis round trip.
                if (FailureReason != null)
                {
                    Log("FATAL: " + FailureReason);
                }
                Log($"stopped. published {_publishedCount} updates.");
            }
        }

        private unsafe void HandlePacket(Enet.PacketWrapper* packet)
        {
            int length = (int)packet->DataLength;
            byte[] payload = new byte[length];
            Marshal.Copy(packet->Data, payload, 0, length);

            try
            {
                switch (packet->Channel)
                {
                    case Enet.ChAssetLoadRequestOp:
                        OnAssetLoadRequest(payload, length);
                        break;
                    case Enet.ChAddEntityOp:
                        OnAddEntity(payload, length);
                        break;
                    case Enet.ChSendComponentInterest: // server->client this channel carries AddComponentOp batches
                        OnAddComponents(payload, length);
                        break;
                    case Enet.ChAuthorityChangeOp:
                        OnAuthorityChange(payload, length);
                        break;
                    case Enet.ChComponentUpdateOp:
                        OnComponentUpdate(payload, length);
                        break;
                    case Enet.ChRemoveEntityOp:
                        OnRemoveEntity(payload, length);
                        break;
                }
            }
            catch (Exception ex)
            {
                // One bad packet must never silently end the soak: before this
                // guard existed, a payload the handler code choked on unwound
                // Run()'s loop, the bot printed nothing but "stopped.", and a
                // 10-minute soak aborted at t=0 with no reason on screen
                // (2026-08-09, relay v2 gate). The packet is dumped in full so
                // the failure identifies ITSELF - the bytes are the evidence.
                _metrics.RecordDecodeError();
                Log($"PACKET HANDLING FAILED on channel {packet->Channel} ({length} bytes): {ex.Message}"
                    + $"\n        payload: {Convert.ToHexString(payload)}"
                    + $"\n        {ex}");
            }
        }

        private void OnAssetLoadRequest(byte[] payload, int length)
        {
            PbAssetLoadRequestOp op = Wire.Decode<PbAssetLoadRequestOp>(payload, length);
            Log($"asset load request: {op.Name}@{op.Context} - acking immediately (headless, nothing to load).");

            // The server never parses the ack payload (the real client's shim
            // sends sizeof(pointer) bytes of a struct pointer); one byte is enough.
            Enet.Send(_peer, Enet.ChAssetLoadRequestOp, new byte[] { (byte)'k' }, Enet.FlagReliable);
            Enet.Flush(_clientHost);
        }

        private void OnAddEntity(byte[] payload, int length)
        {
            PbAddEntityOp op = Wire.Decode<PbAddEntityOp>(payload, length);
            Log($"add entity {op.EntityId}: {op.PrefabName}@{op.PrefabContext}");

            bool readd = !_seenEntities.Add(op.EntityId);
            if (readd) _readdedEntities.Enqueue(op.EntityId);
            // The spawn plan's first world entity is the island. Its prefab is a
            // census asset name rather than the literal "Island", so record the
            // first non-player entity instead of guessing that name.
            if (IslandEntityId < 0 && op.PrefabContext != "Player" && op.PrefabContext != "Default")
                IslandEntityId = op.EntityId;
            // ISLAND FAUNA. A creature is the only world entity whose transform the
            // server drives without any client ever asking about it, so a bot that
            // never declares interest cannot tell "the sender works" from "the
            // sender is silent". Declare 190602 plus the one identity component
            // that species uses (retail split them: SpeciesType for the rays,
            // BasicSpeciesType for the jellies) and count what comes back.
            if (IslandFaunaPrefabs.IsCreature(op.PrefabName))
            {
                FaunaEntitiesAdded++;
                lock (_faunaArrivalNs) _faunaArrivalNs.Add(NowNs());
                _faunaEntities.Add(op.EntityId);
                var faunaInterest = new PbSendComponentInterest { EntityId = op.EntityId };
                foreach (uint id in IslandFaunaPrefabs.InterestSetFor(op.PrefabName))
                {
                    faunaInterest.Components.Add(new PbInterestOverride { ComponentId = id, IsInterested = true });
                }
                Enet.Send(_peer, Enet.ChSendComponentInterest, Wire.Encode(faunaInterest), Enet.FlagReliable);
                Enet.Flush(_clientHost);
                Log($"fauna {op.PrefabName} {op.EntityId}: requested 190602 + identity components.");
            }

            if (op.PrefabName == "ShipFrame") ShipHullEntityId = op.EntityId;
            if (op.PrefabName == "Helm01") HelmEntityId = op.EntityId;
            if (op.PrefabName == "Deck01" && DeckEntityId < 0) DeckEntityId = op.EntityId;

            // Ack first (the real client's shim acks from inside GetOpList).
            Enet.Send(_peer, Enet.ChAddEntityOp, new byte[] { (byte)'a' }, Enet.FlagReliable);
            Enet.Flush(_clientHost);

            // Context "Player" marks OUR avatar; "Default" is another player's
            // mirror; anything else is a world entity (island, tree).
            if (op.PrefabContext == "Player" && MyEntityId < 0)
            {
                MyEntityId = op.EntityId;
                _entityOwners[op.EntityId] = Index;
                Log($"this is my entity ({MyEntityId}); requesting components [{string.Join(", ", OwnInterest)}].");

                var interest = new PbSendComponentInterest { EntityId = MyEntityId };
                foreach (uint id in OwnInterest)
                {
                    interest.Components.Add(new PbInterestOverride { ComponentId = id, IsInterested = true });
                }
                Enet.Send(_peer, Enet.ChSendComponentInterest, Wire.Encode(interest), Enet.FlagReliable);
                Enet.Flush(_clientHost);
            }
        }

        private void OnRemoveEntity(byte[] payload, int length)
        {
            PbRemoveEntityOp op = Wire.Decode<PbRemoveEntityOp>(payload, length);
            _removedEntities.Enqueue(op.EntityId);
            Log($"remove entity {op.EntityId}");
        }

        private void OnAddComponents(byte[] payload, int length)
        {
            PbComponentBatchOp batch = Wire.Decode<PbComponentBatchOp>(payload, length);
            Log($"seeded {batch.Components.Count} component(s) on entity {batch.EntityId}"
                + $" [{string.Join(", ", batch.Components.Select(c => c.ComponentId))}]");

            // FAUNA IDENTITY SEEDS, decoded through the game's own generated
            // codecs so "the server serves 1177/4326" is proven at the byte
            // level, not at the op level. A summary counter per distinct value,
            // reported at the end of the soak.
            if (_faunaEntities.Contains(batch.EntityId))
            {
                foreach (PbComponentData component in batch.Components)
                {
                    switch (component.ComponentId)
                    {
                        case 1177:
                            if (GameComponents.Deserialize(1177, GameComponents.TypeSnapshot,
                                    component.Data, component.Data.Length)
                                is Bossa.Travellers.Creatures.GenderState.Data gender)
                            {
                                CountFaunaIdentity($"1177 gender={gender.Value.gender}");
                            }
                            break;
                        case 4326:
                            if (GameComponents.Deserialize(4326, GameComponents.TypeSnapshot,
                                    component.Data, component.Data.Length)
                                is Bossa.Travellers.Creatures.Variants.MantaRayVariantState.Data variant)
                            {
                                CountFaunaIdentity("4326 biome=" + variant.Value.biomeType
                                    + " variant=" + (variant.Value.mantaRayVariantType.HasValue
                                        ? variant.Value.mantaRayVariantType.Value.ToString() : "unset"));
                            }
                            break;
                        case 4322:
                            if (GameComponents.Deserialize(4322, GameComponents.TypeSnapshot,
                                    component.Data, component.Data.Length)
                                is Bossa.Travellers.Creatures.Basic.BasicCreatureState.Data basic)
                            {
                                CountFaunaIdentity($"4322 species={basic.Value.speciesType}");
                            }
                            break;
                        case 1166:
                            // THE SCALE THE REAL CLIENT WOULD DRAW, computed here
                            // with the client's own arithmetic and the RECOVERED
                            // prefab endpoints (birthScale 0.25, fullyGrownScale
                            // 1.0) rather than reported as raw seconds. That makes
                            // the soak line answer the question that actually
                            // matters - "did any manta shrink that should not
                            // have" - at a glance.
                            if (GameComponents.Deserialize(1166, GameComponents.TypeSnapshot,
                                    component.Data, component.Data.Length)
                                is Bossa.Travellers.Creatures.AgeState.Data age)
                            {
                                int grown = age.Value.secondsTillFullyGrown;
                                double ratio = grown <= 0 ? 1.0
                                    : Math.Clamp((double)age.Value.secondsOld / grown, 0.0, 1.0);
                                double scale = 0.25 + (0.75 * ratio);
                                CountFaunaIdentity(scale >= 1.0
                                    ? "1166 adult scale=1.00"
                                    : "1166 CALF scale=" + scale.ToString("0.00",
                                        System.Globalization.CultureInfo.InvariantCulture));
                            }
                            break;
                    }
                }
            }

            if (batch.EntityId != MyEntityId)
            {
                return;
            }

            foreach (PbComponentData component in batch.Components)
            {
                if (component.ComponentId != TransformStateId || _centreKnown)
                {
                    continue;
                }

                // The 190602 seed carries the spawn point (SpawnPolicy); the
                // circle is walked around it so the bot moves where the world
                // has ground. Snapshot-typed, Q52.12 fixed point.
                if (GameComponents.Deserialize(TransformStateId, GameComponents.TypeSnapshot,
                        component.Data, component.Data.Length) is TransformState.Data data)
                {
                    var fixedPoint = data.Value.localPosition.fixedPointValues;
                    _centreX = fixedPoint[0] / 4096.0;
                    _centreY = fixedPoint[1] / 4096.0;
                    _centreZ = fixedPoint[2] / 4096.0;
                    _centreKnown = true;
                    Log($"spawn seed: ({_centreX:0.##}, {_centreY:0.##}, {_centreZ:0.##}) m - circling it.");
                }
            }
        }

        private void OnAuthorityChange(byte[] payload, int length)
        {
            PbAuthorityChangeOpWrapper op = Wire.Decode<PbAuthorityChangeOpWrapper>(payload, length);
            if (op.EntityId != MyEntityId)
            {
                return;
            }

            foreach (PbAuthorityChange change in op.OpList)
            {
                if (change.ComponentId == TransformStateId && change.HasAuthority && !HasAuthority)
                {
                    HasAuthority = true;
                    Log("authority over 190602 granted - ready to publish.");
                }
            }
        }

        private void OnComponentUpdate(byte[] payload, int length)
        {
            PbComponentBatchOp batch = Wire.Decode<PbComponentBatchOp>(payload, length);
            long nowNs = NowNs();

            foreach (PbComponentData component in batch.Components)
            {
                if (component.ComponentId == 190602 && _faunaEntities.Contains(batch.EntityId))
                {
                    FaunaPoseUpdates++;
                    continue;
                }

                if (batch.EntityId == ShipHullEntityId && component.ComponentId == PredictedMotionStateId)
                {
                    if (GameComponents.Deserialize(PredictedMotionStateId, GameComponents.TypeUpdate,
                            component.Data, component.Data.Length) is SSPPredictedMotionState.Update motion
                        && motion.latestControlPoint.HasValue
                        && motion.latestControlPoint.Value.HasValue)
                    {
                        ShipControlPoint point = motion.latestControlPoint.Value.Value;
                        LastHullX = point.position.X;
                        LastHullY = point.position.Y;
                        LastHullZ = point.position.Z;
                        LastHullTimestamp = point.timestamp;
                        _hullFrames[point.timestamp] = (point.position.X,
                            point.position.Y, point.position.Z);
                        HullMotionUpdates++;
                    }
                    continue;
                }

                if (batch.EntityId == HelmEntityId && component.ComponentId == TransformStateId)
                {
                    HelmWakeUpdates++;
                    continue;
                }

                if (component.ComponentId != TransformStateId && component.ComponentId != ClientAuthoritativePlayerStateId)
                {
                    continue;
                }

                // Relayed updates are re-addressed to the SENDER's own entity id;
                // resolve which bot that is. Updates about our own entity (e.g.
                // a server-sent 1036) are not relays and carry no timestamps of ours.
                if (!_entityOwners.TryGetValue(batch.EntityId, out int senderBot) || senderBot == Index)
                {
                    continue;
                }

                if (_shipAcceptanceMode && component.ComponentId == ClientAuthoritativePlayerStateId
                    && GameComponents.Deserialize(ClientAuthoritativePlayerStateId, GameComponents.TypeUpdate,
                        component.Data, component.Data.Length) is ClientAuthoritativePlayerState.Update remoteState)
                {
                    if (remoteState.relativeTo.HasValue && remoteState.relativeTo.Value.Id == ShipHullEntityId)
                        RemoteAboardFrames++;
                    if (remoteState.relativeTo.HasValue && remoteState.relativeTo.Value.Id <= 0)
                        RemoteInvalidRelativeFrames++;
                }

                _metrics.RecordReceive(Index, nowNs);
                if (_gapOpen)
                {
                    double gapSeconds = (nowNs - _gapStartNs) / 1e9;
                    _metrics.RecordGap(Index, nowNs, gapSeconds, "relayed 190602/1073");
                    Log($"receive gap ENDED after {gapSeconds:0.##} s.");
                    _gapOpen = false;
                }
                _lastRelayedReceiveNs = nowNs;

                // HasValue-gated like the real client's own apply path - never
                // Option.Value blind. The 2026-08-09 v2 gate died exactly here:
                // relay v2's heartbeat re-sends the last position WITHOUT a
                // timestamp field (a legal update the presence-checked game
                // code shrugs at), and .timestamp.Value on it killed the bot
                // silently at t=0.
                float? timestamp = null;
                if (component.ComponentId == TransformStateId)
                {
                    if (GameComponents.Deserialize(TransformStateId, GameComponents.TypeUpdate,
                            component.Data, component.Data.Length) is TransformState.Update u && u.timestamp.HasValue)
                    {
                        timestamp = u.timestamp.Value;
                    }
                }
                else if (GameComponents.Deserialize(ClientAuthoritativePlayerStateId, GameComponents.TypeUpdate,
                        component.Data, component.Data.Length) is ClientAuthoritativePlayerState.Update s && s.timestamp.HasValue)
                {
                    timestamp = s.timestamp.Value;
                }

                if (!timestamp.HasValue)
                {
                    // v2 heartbeat: nothing was sent, so nothing can match.
                    _metrics.RecordHeartbeat();
                    continue;
                }

                if (_rewritten1073 && component.ComponentId == ClientAuthoritativePlayerStateId)
                {
                    // Server-issued synthetic stamp: unmatchable by design, but
                    // it must be strictly increasing AS DELIVERED - the client
                    // pairs every position with the latest of these and
                    // collapses equals. This is the receiver-side twin of the
                    // server's badTsPairs counter.
                    if (_lastRemote1073Stamp.TryGetValue(senderBot, out float previous) && timestamp.Value <= previous)
                    {
                        _metrics.RecordTimelineViolation();
                        TimelineViolationsObserved++;
                        Log($"1073 TIMELINE VIOLATION from bot {senderBot}: stamp {timestamp.Value} after {previous}.");
                    }
                    _lastRemote1073Stamp[senderBot] = timestamp.Value;
                    continue;
                }

                if (_sendLog.TryRemove((senderBot, component.ComponentId, BitConverter.SingleToInt32Bits(timestamp.Value)), out long sentNs))
                {
                    _metrics.RecordStaleness(Index, nowNs, (nowNs - sentNs) / 1e6);
                }
                else
                {
                    _metrics.RecordUnmatched();
                }
            }
        }

        public void EnableShipAcceptance()
        {
            _commands.Enqueue(() => _shipAcceptanceMode = true);
        }

        public void ManHelm()
        {
            _commands.Enqueue(() =>
            {
                if (HelmEntityId <= 0) throw new InvalidOperationException("helm entity is not known");
                var update = new InteractAgentState.Update()
                    .AddInteractWithObject(new InteractWithObject(new EntityId(HelmEntityId), InteractVerb.Man));
                SendAcceptanceUpdate(InteractAgentStateId, update);
                Log($"acceptance: Man helm {HelmEntityId}");
            });
        }

        public void ReleaseHelm()
        {
            _commands.Enqueue(() =>
            {
                var update = new InteractAgentState.Update()
                    .AddReleaseInteraction(new ReleaseInteraction(new EntityId(HelmEntityId)));
                SendAcceptanceUpdate(InteractAgentStateId, update);
                Log($"acceptance: release helm {HelmEntityId}");
            });
        }

        public void SetShipInput(float throttle, float yaw)
        {
            _commands.Enqueue(() =>
            {
                var update = new ShipControlInput.Update()
                    .SetThrottle(throttle)
                    .SetVertical(0f)
                    .SetShipAxes(new Improbable.Math.Vector3f(0f, yaw, 0f));
                SendAcceptanceUpdate(ShipControlInputId, update);
                Log($"acceptance: input throttle={throttle:0.##}, yaw={yaw:0.##}");
            });
        }

        public void SetAboard(bool aboard)
        {
            _commands.Enqueue(() =>
            {
                _acceptanceAboard = aboard;
                _acceptanceRelativeTo = aboard ? ShipHullEntityId : IslandEntityId;
            });
        }

        public void InjectBriefContactSeam(int ticks = 3)
        {
            _commands.Enqueue(() => _invalidRelativeTicks = Math.Max(1, ticks));
        }

        public void MoveIslandLocal(double x, double y, double z)
        {
            _commands.Enqueue(() =>
            {
                _acceptanceAboard = false;
                _acceptanceRelativeTo = IslandEntityId;
                _acceptanceLocalX = x;
                _acceptanceLocalY = y;
                _acceptanceLocalZ = z;
            });
        }

        public long[] DrainRemovedEntities()
        {
            var result = new System.Collections.Generic.List<long>();
            while (_removedEntities.TryDequeue(out long id)) result.Add(id);
            return result.ToArray();
        }

        public long[] DrainReaddedEntities()
        {
            var result = new System.Collections.Generic.List<long>();
            while (_readdedEntities.TryDequeue(out long id)) result.Add(id);
            return result.ToArray();
        }

        public bool TryGetHullFrame(long timestamp,
            out (double X, double Y, double Z) position) =>
            _hullFrames.TryGetValue(timestamp, out position);

        private void DrainCommands()
        {
            while (_commands.TryDequeue(out Action command)) command();
        }

        private void SendAcceptanceUpdate(uint componentId, object update)
        {
            byte[] inner = GameComponents.Serialize(componentId, GameComponents.TypeUpdate, update);
            byte[] outer = Wire.Encode(new PbComponentBatchOp
            {
                EntityId = MyEntityId,
                Components =
                {
                    new PbComponentData { ComponentId = componentId, Data = inner, DataLength = inner.Length }
                }
            });
            Enet.Send(_peer, Enet.ChComponentUpdateOp, outer, PacketFlagFor(componentId));
            Enet.Flush(_clientHost);
        }

        private void MaybePublish()
        {
            if (!HasAuthority || Disconnected)
            {
                return;
            }

            long nowNs = NowNs();
            if (_publishStartNs < 0)
            {
                _publishStartNs = nowNs;
                _nextPublishNs = nowNs;
                if (!_centreKnown)
                {
                    // SpawnPolicy.PlayerSpawnPosition, in case the seed decode ever
                    // fails: Haven island-local (208, 6.7, 4).
                    _centreX = 70502113 / 4096.0;
                    _centreY = -1277826 / 4096.0;
                    _centreZ = -4629165 / 4096.0;
                    Log("no spawn seed decoded; falling back to the documented spawn point.");
                }
            }

            if (nowNs < _nextPublishNs)
            {
                return;
            }
            _nextPublishNs += (long)(PublishIntervalMs * 1e6);

            // The client-semantics timestamp: a float seconds accumulator that
            // starts at 0 and advances with time. Strictly monotonic per bot, so
            // (bot, component, bits-of-timestamp) is a unique send key.
            float t = (float)((nowNs - _publishStartNs) / 1e9);

            double angle = 2 * Math.PI * (t / CircleSecondsPerLap) + Index * Math.PI; // bots start opposite
            double x = _shipAcceptanceMode
                ? _centreX + (_acceptanceLocalX - 208.0)
                : _centreX + CircleRadiusMetres * Math.Cos(angle);
            double y = _shipAcceptanceMode
                ? _centreY + (_acceptanceLocalY - 6.7)
                : _centreY;
            double z = _shipAcceptanceMode
                ? _centreZ + (_acceptanceLocalZ - 4.0)
                : _centreZ + CircleRadiusMetres * Math.Sin(angle);

            // Q52.12, truncation toward zero - FixedPointPosition semantics.
            var position = new FixedPointVector3(new Improbable.Collections.List<long>
            {
                (long)(x * 4096), (long)(y * 4096), (long)(z * 4096)
            });

            TransformState.Update transform = new TransformState.Update()
                .SetLocalPosition(position)
                .SetLocalRotation(new Quaternion32(1023)) // identity SENTINEL: low 10 bits set; 1 decodes to NaN
                .SetTimestamp(t);
            // parent deliberately ABSENT: present would mean parent-local coordinates.

            ClientAuthoritativePlayerState.Update state = new ClientAuthoritativePlayerState.Update()
                .SetTimestamp(t)
                .SetGrounded(true)
                .SetBoneData(BoneFiller);

            if (_shipAcceptanceMode)
            {
                bool seam = _invalidRelativeTicks > 0;
                if (seam) _invalidRelativeTicks--;
                long relativeTo = seam ? -1 : _acceptanceRelativeTo;
                state.SetPositionRelative(_acceptanceAboard
                    ? new Improbable.Math.Vector3f(0f, 1f, 0f)
                    : new Improbable.Math.Vector3f(
                        (float)_acceptanceLocalX, (float)_acceptanceLocalY, (float)_acceptanceLocalZ));
                state.SetRelativeTo(new EntityId(relativeTo));
                state.SetRelativeBias(seam ? 0f : 1f);
                state.SetIsRelativeToShip(new Improbable.Collections.Option<bool>(
                    !seam && _acceptanceAboard));
            }

            PublishUpdate(TransformStateId, transform, t);
            PublishUpdate(ClientAuthoritativePlayerStateId, state, t);
            Enet.Flush(_clientHost);
            _publishedCount++;
        }

        private void PublishUpdate(uint componentId, object update, float timestamp)
        {
            byte[] inner = GameComponents.Serialize(componentId, GameComponents.TypeUpdate, update);
            byte[] outer = Wire.Encode(new PbComponentBatchOp
            {
                EntityId = MyEntityId,
                Components =
                {
                    new PbComponentData { ComponentId = componentId, Data = inner, DataLength = inner.Length }
                }
            });

            // One update per packet, using the same superseding-stream policy as
            // the native client's Connection::SendComponentUpdate. The send
            // instant is recorded immediately before handing to the transport.
            //
            // Under relay v2 a published 1073's timestamp never comes back (the
            // server rewrites it), so it enters neither the send log nor the
            // delivery denominator: sends/matched/delivered% then all speak
            // about the one stream that IS matchable, 190602.
            long nowNs = NowNs();
            if (!(_rewritten1073 && componentId == ClientAuthoritativePlayerStateId))
            {
                _sendLog[(Index, componentId, BitConverter.SingleToInt32Bits(timestamp))] = nowNs;
                _metrics.RecordSend(Index, nowNs);
            }
            Enet.Send(_peer, Enet.ChComponentUpdateOp, outer, PacketFlagFor(componentId));
        }

        private static int PacketFlagFor(uint componentId) =>
            componentId == TransformStateId || componentId == ClientAuthoritativePlayerStateId
                ? Enet.FlagUnreliable
                : Enet.FlagReliable;

        private void MaybeReportGap()
        {
            if (_lastRelayedReceiveNs < 0 || _gapOpen)
            {
                return;
            }

            long nowNs = NowNs();
            double gapSeconds = (nowNs - _lastRelayedReceiveNs) / 1e9;
            if (gapSeconds > 1.0)
            {
                _gapOpen = true;
                _gapStartNs = _lastRelayedReceiveNs;
                Log($"receive gap OPEN: no relayed update for {gapSeconds:0.##} s and counting...");
            }
        }

        /// <summary>Close out a gap still open when the soak ends, with its true length.</summary>
        public void FlushOpenGap()
        {
            if (_gapOpen)
            {
                long nowNs = NowNs();
                _metrics.RecordGap(Index, nowNs, (nowNs - _gapStartNs) / 1e9, "relayed 190602/1073 (unresolved at shutdown)");
                _gapOpen = false;
            }
        }
    }
}
