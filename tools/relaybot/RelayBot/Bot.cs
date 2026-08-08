using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Improbable.Corelibrary.Math;
using Improbable.Corelibrary.Transforms;
using Bossa.Travellers.Player;

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

        public Bot(int index, string name, string host, int port, Metrics metrics,
            ConcurrentDictionary<(int, uint, int), long> sendLog,
            ConcurrentDictionary<long, int> entityOwners,
            CancellationToken cancel)
        {
            Index = index;
            Name = name;
            _host = host;
            _port = port;
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
                Log($"stopped. published {_publishedCount} updates.");
            }
        }

        private unsafe void HandlePacket(Enet.PacketWrapper* packet)
        {
            int length = (int)packet->DataLength;
            byte[] payload = new byte[length];
            Marshal.Copy(packet->Data, payload, 0, length);

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

        private void OnAddComponents(byte[] payload, int length)
        {
            PbComponentBatchOp batch = Wire.Decode<PbComponentBatchOp>(payload, length);
            Log($"seeded {batch.Components.Count} component(s) on entity {batch.EntityId}"
                + $" [{string.Join(", ", batch.Components.Select(c => c.ComponentId))}]");

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

                _metrics.RecordReceive(Index, nowNs);
                if (_gapOpen)
                {
                    double gapSeconds = (nowNs - _gapStartNs) / 1e9;
                    _metrics.RecordGap(Index, nowNs, gapSeconds, "relayed 190602/1073");
                    Log($"receive gap ENDED after {gapSeconds:0.##} s.");
                    _gapOpen = false;
                }
                _lastRelayedReceiveNs = nowNs;

                float? timestamp = component.ComponentId == TransformStateId
                    ? (GameComponents.Deserialize(TransformStateId, GameComponents.TypeUpdate,
                        component.Data, component.Data.Length) as TransformState.Update)?.timestamp.Value
                    : (GameComponents.Deserialize(ClientAuthoritativePlayerStateId, GameComponents.TypeUpdate,
                        component.Data, component.Data.Length) as ClientAuthoritativePlayerState.Update)?.timestamp.Value;

                if (timestamp.HasValue
                    && _sendLog.TryRemove((senderBot, component.ComponentId, BitConverter.SingleToInt32Bits(timestamp.Value)), out long sentNs))
                {
                    _metrics.RecordStaleness(Index, nowNs, (nowNs - sentNs) / 1e6);
                }
                else
                {
                    _metrics.RecordUnmatched();
                }
            }
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
            double x = _centreX + CircleRadiusMetres * Math.Cos(angle);
            double z = _centreZ + CircleRadiusMetres * Math.Sin(angle);

            // Q52.12, truncation toward zero - FixedPointPosition semantics.
            var position = new FixedPointVector3(new Improbable.Collections.List<long>
            {
                (long)(x * 4096), (long)(_centreY * 4096), (long)(z * 4096)
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

            // One update per packet, RELIABLE - exactly what the real client's
            // Connection::SendComponentUpdate does. The send instant is recorded
            // as late as possible, immediately before handing to the transport.
            long nowNs = NowNs();
            _sendLog[(Index, componentId, BitConverter.SingleToInt32Bits(timestamp))] = nowNs;
            Enet.Send(_peer, Enet.ChComponentUpdateOp, outer, Enet.FlagReliable);
            _metrics.RecordSend(Index, nowNs);
        }

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
