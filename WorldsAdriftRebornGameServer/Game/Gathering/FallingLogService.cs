using Improbable.Corelibrary.Transforms;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// A FELLED TREE FALLS OVER. The impure half of <see cref="TreeFall"/>: it turns
    /// each cut into a live entity, drives it down the authored arc, and retires it.
    ///
    /// WIRE SHAPE, per felled section (the multiplayer-safety contract):
    /// <list type="bullet">
    /// <item>OUT, once: an AssetLoadRequest and an AddEntity, to the peers that
    ///   already hold the parent tree. Nothing else; the log seeds no components,
    ///   exactly like the tree it came off, and the client asks for what it wants
    ///   over SEND_COMPONENT_INTEREST.</item>
    /// <item>OUT, ~32 times over 1.6 s: one 190602 TransformState carrying
    ///   localPosition and localRotation - the SAME two fields whether or not the
    ///   log was grounded, because grounding changes what those fields say and never
    ///   how many of them there are. It adds no component, no stream and no rate;
    ///   see <see cref="LogGrounding"/>. 190602 is UNRELIABLE by
    ///   <c>MirrorSendPolicy.RelayReliabilityFor</c> and this stream is superseding -
    ///   every update is the complete absolute pose, never a delta - so a loss costs
    ///   one frame of smoothness and nothing else.</item>
    /// <item>OUT, 4 more times: the same flat pose, because the ONE update that says
    ///   "it is down" is exactly the one whose loss would leave a log hanging in the
    ///   air.</item>
    /// <item>Then SILENCE until the log is removed. A landed log costs nothing.</item>
    /// <item>OUT, once: RemoveEntity on channel 5.</item>
    /// <item>IN: nothing. No client ever sends anything about a log; it is not
    ///   choppable, not interactive, and has no update handler.</item>
    /// </list>
    /// The world-wide ceiling is <see cref="TreeFall.DefaultMaxConcurrent"/> logs at
    /// <see cref="TreeFall.PoseInterval"/> - 160 updates a second, a fifth of one
    /// 20 Hz avatar - and it is a hard cap, not an expectation: over budget, the
    /// section vanishes exactly as it did before this existed.
    ///
    /// A LOG IS NOT A WORLD REGISTRATION, and that is the design decision everything
    /// else follows from. See <see cref="TreeFall.FirstLogEntityId"/>: registering a
    /// short-lived entity would put it in the connect-time spawn plan, the loading
    /// barrier and the domain host's expected-owned list, all three of which assume a
    /// registration outlives the session. Instead the log lives only in
    /// <see cref="FallingLogs"/>, and the three component branches that need to know
    /// where it is ask that (see <c>ComponentsSerializer</c>, 1035/1036/190602).
    ///
    /// ONLY PEERS THAT CAN RECEIVE RemoveEntity ARE EVER SENT A LOG. Channel 5 is a
    /// negotiated capability; a peer that lacks it would be left with permanent
    /// litter, so it is simply never shown the log. That is the whole reason this
    /// feature can exist without a litter-collection story.
    /// </summary>
    internal sealed class FallingLogService
    {
        /// <summary>Set to "0" to switch felled logs off entirely.</summary>
        internal const string EnableEnv = "WAREBORN_TREE_FALL";

        /// <summary>How many logs may be live at once. 0 is another way to switch it off.</summary>
        internal const string BudgetEnv = "WAREBORN_TREE_FALL_MAX";

        /// <summary>
        /// How far a resting log's origin sits above the ground, in metres. See
        /// <see cref="LogGrounding.ParseLift"/>: it is a trunk radius, it is
        /// reconstructed rather than measured, and the only instrument that can read
        /// it is somebody standing next to a felled log.
        /// </summary>
        internal const string LiftEnv = "WAREBORN_TREE_FALL_LIFT";

        private const uint TransformStateComponentId = 190602;

        private readonly FallingLogs _logs;
        private readonly bool _enabled;
        private long _sample;

        internal FallingLogService(IClock clock)
            : this(clock,
                TreeFall.FallEnabled(Environment.GetEnvironmentVariable(EnableEnv)),
                TreeFall.ParseBudget(Environment.GetEnvironmentVariable(BudgetEnv)),
                LogGrounding.ParseLift(Environment.GetEnvironmentVariable(LiftEnv)))
        {
        }

        internal FallingLogService(IClock clock, bool enabled, int? maxConcurrent, double? liftMetres = null)
        {
            _enabled = enabled;
            _logs = new FallingLogs(clock, maxConcurrent: maxConcurrent, liftMetres: liftMetres);
        }

        /// <summary>Whether felled logs are switched on.</summary>
        internal bool Enabled => _enabled;

        /// <summary>
        /// The live logs. Internal because <c>ComponentsSerializer</c>'s 1035, 1036
        /// and 190602 branches resolve a log's prefab, mask and pose off it - a log
        /// is not in the world registry, so this is the only place that knows.
        /// </summary>
        internal FallingLogs Logs => _logs;

        /// <summary>
        /// Turns one applied cut into a falling log.
        ///
        /// CALL THIS BEFORE THE MASK PUSH. Retail's order is
        /// <c>SpawnNewTree(salvagerId, fallingMask)</c> and only then
        /// <c>ChangeMask(remaining)</c> (acs/TreeSection.cs:78-79), so the log is on
        /// the wire while the crown is still standing and the crown then disappears
        /// underneath it. Reversed, there is a window in which the tree is visibly
        /// bald and nothing is falling.
        ///
        /// Every refusal is silent and harmless: over budget, an unknown tree, a cut
        /// that severed nothing, or the feature switched off all leave the cut
        /// behaving exactly as it did before logs existed.
        /// </summary>
        internal int Drop(TreeSectionMaskChange change)
        {
            if (!_enabled || !_logs.HasCapacity)
            {
                return 0;
            }

            // THE PARENT MAY ITSELF BE A LOG. Chopping a trunk on the ground splits a
            // piece off it, and that piece has to be seeded from what the trunk looks
            // like RIGHT NOW - its current toppled rotation, not the upright rotation
            // of the tree three cuts ago - or the piece would stand up out of the log
            // it came from. A log is not a world registration (see
            // TreeFall.FirstLogEntityId), so its pose comes off the log ledger.
            bool parentIsLog = _logs.IsLog(change.TreeEntityId);

            string assetName;
            string assetContext;
            FixedPointPosition position;
            uint parentRotation;

            if (parentIsLog)
            {
                assetName = _logs.AssetNameOf(change.TreeEntityId) ?? string.Empty;
                assetContext = _logs.AssetContextOf(change.TreeEntityId) ?? string.Empty;
                position = _logs.PositionOf(change.TreeEntityId) ?? default;
                parentRotation = _logs.RotationOf(change.TreeEntityId) ?? 0u;
            }
            else
            {
                // The parent's OWN prefab: a palm must shed a palm, and 1035's
                // prefabName is read off the registration for exactly this reason.
                WorldEntity? parent = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(change.TreeEntityId);
                if (parent == null)
                {
                    return 0;
                }
                assetName = parent.AssetName;
                assetContext = parent.AssetContext;
                position = parent.Position;
                parentRotation = parent.PackedRotation;
            }

            Multiplayer.TreeTopology? topology =
                WorldsAdriftRebornGameServer.Harvest.TopologyOf(change.TreeEntityId);
            int sectionCount = topology?.SectionCount ?? Trees.SectionCount;

            long logEntityId = _logs.NextEntityId();
            FelledLog? dropped = _logs.Drop(
                logEntityId, change, assetName, assetContext,
                position, parentRotation, sectionCount, alreadyDown: parentIsLog);

            if (dropped == null)
            {
                return 0;
            }

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                // WHO HOLDS THE PARENT, asked of both ledgers rather than of one.
                // See TreeFall.MayShowLog: asking only the send ledger is what made
                // every release-world log invisible, because a streamed tree is
                // recorded by ResourceInterestService and nowhere else. The channel
                // count stays a veto - a peer that cannot receive RemoveEntity would
                // keep this log for the rest of its session.
                bool addEntitySent =
                    WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, change.TreeEntityId);
                bool holdsParent = HoldsAnyComponentOf(peer, change.TreeEntityId);

                if (!TreeFall.MayShowLog(addEntitySent, holdsParent, CanReceiveRemove(peer)))
                {
                    continue;
                }

                SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?",
                    dropped.Value.AssetName, dropped.Value.AssetContext);
                if (!SendOPHelper.SendAddEntityOP(peer, logEntityId,
                        dropped.Value.AssetName, dropped.Value.AssetContext))
                {
                    Console.WriteLine("[tree-fall] failed to send AddEntityOp for log "
                        + logEntityId + " to a peer.");
                    continue;
                }

                WorldsAdriftRebornGameServer.SentEntities.MarkSent(peer, logEntityId);
                reached++;
            }

            if (reached == 0)
            {
                // Nobody can see it, so it is not a log - it is bookkeeping that would
                // hold a budget slot and then be paid for in wood the chopper never
                // sees fall. Drop it and let the caller award the whole cut instead:
                // the economy degrades back to exactly what it was before falling logs
                // existed rather than quietly charging the player for the visual.
                _logs.Forget(logEntityId);
                Console.WriteLine("[tree-fall] " + dropped.Value
                    + ", but no peer holds tree " + change.TreeEntityId + "; log abandoned.");
                return 0;
            }

            // The log is a harvestable stand from this moment: same topology, same
            // cadence, only the sections it carried away. This is what makes a tree
            // come apart piece by piece instead of in one go - see TreeHarvest.PlantFelled.
            if (topology != null)
            {
                WorldsAdriftRebornGameServer.Harvest.PlantFelled(
                    logEntityId, topology, dropped.Value.WoodType, dropped.Value.SectionMask);
            }

            Console.WriteLine("[tree-fall] " + dropped.Value + ", shown to " + reached + " peer(s).");
            return reached;
        }

        /// <summary>
        /// One call per main-loop turn: advances every falling log and retires the
        /// ones whose time is up. Cheap when nothing is falling - the common case is
        /// two empty-dictionary walks that allocate nothing.
        /// </summary>
        internal void Tick()
        {
            if (!_enabled)
            {
                return;
            }

            IReadOnlyList<FallingLogPose> poses = _logs.DuePoses();
            if (poses.Count > 0)
            {
                // ONE sample index per turn, shared by every log moving in it -
                // exactly what ShipPartMotionService does. Per-log increments would
                // make each log's stamps climb at a rate that has nothing to do with
                // the interval they are actually sent at, and the client's
                // interpolator plays back on the stamps.
                long sample = ++_sample;
                float stamp = ShipPartMotionPolicy.StampFor(sample, TreeFall.PoseInterval.TotalSeconds);

                foreach (FallingLogPose pose in poses)
                {
                    PushPose(pose, stamp);
                }
            }

            foreach (long logEntityId in _logs.DueRemovals())
            {
                Retire(logEntityId);
            }
        }

        /// <summary>
        /// A cut landed on a LOG rather than on a rooted tree: restart its linger so
        /// it is not deleted mid-chop, and retire it outright once its last section
        /// has gone.
        ///
        /// Returns whether the entity was a log at all, so the caller can tell a cut
        /// on a trunk from a cut on a tree without asking twice.
        /// </summary>
        internal bool NoteCut(long entityId, int remainingMask)
        {
            if (!_logs.IsLog(entityId))
            {
                return false;
            }

            if (remainingMask == 0)
            {
                // Nothing left to draw. Retire it NOW rather than leaving an empty
                // trunk on every client for the rest of its linger: an entity with an
                // empty section mask renders nothing but still costs a slot, and
                // Retire is the only path that also clears the harvest stand.
                Retire(entityId);
                _logs.Forget(entityId);
                return true;
            }

            _logs.Touch(entityId, remainingMask);
            return true;
        }

        /// <summary>
        /// Sends one log's pose to every peer holding its 190602.
        ///
        /// Pushed to each peer DIRECTLY, never through <c>RelayToOtherPlayers</c> -
        /// that method re-addresses an update to the SENDER's own avatar, so a log's
        /// pose routed through it would teleport whoever happened to be chopping.
        /// The same trap is documented on <c>PushTreeSectionMask</c> and on the
        /// nugget depletion sink; it has cost this project a debugging round.
        /// </summary>
        private void PushPose(FallingLogPose pose, float stamp)
        {
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (!TryGetStoredRef(peer, pose.LogEntityId, TransformStateComponentId, out ulong refId))
                {
                    continue;
                }

                TransformState.Update update = ShipPartTransform.BuildParentlessWakeUpdate(
                    pose.Position,
                    new Improbable.Corelibrary.Math.Quaternion32(pose.PackedRotation),
                    stamp);

                // Keep this peer's stored 190602 in step with what it has just been
                // told, so a re-serve cannot resurrect the upright pose.
                if (Improbable.Worker.Internal.ClientObjects.Instance.Dereference(refId)
                    is TransformState.Data stored)
                {
                    update.ApplyTo(stored);
                }

                SendOPHelper.SendComponentUpdateOp(peer, pose.LogEntityId,
                    new List<uint> { TransformStateComponentId },
                    new List<object> { update });
            }
        }

        /// <summary>
        /// Removes one log from every peer that holds it.
        ///
        /// ORDER MATTERS: the RemoveEntity op builds its component list by READING
        /// the served-component ledger, so <c>PeerCheckoutCleanup.RemoveEntity</c> -
        /// which clears that ledger - has to run after the send, never before.
        /// </summary>
        private static void Retire(long logEntityId)
        {
            // The stand goes first, and unconditionally: a log that stopped existing
            // must stop being choppable even for a peer whose RemoveEntity failed,
            // or a held beam would keep cutting an id nobody can see.
            WorldsAdriftRebornGameServer.Harvest.Uproot(logEntityId);

            int removed = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (!WorldsAdriftRebornGameServer.SentEntities.WasSent(peer, logEntityId))
                {
                    continue;
                }

                if (SendOPHelper.SendRemoveEntityOP(peer, logEntityId))
                {
                    PeerCheckoutCleanup.RemoveEntity(peer, logEntityId);
                    removed++;
                }
                else
                {
                    Console.WriteLine("[tree-fall] peer could not receive RemoveEntity for log "
                        + logEntityId + "; it will remain on that client until it reconnects.");
                }
            }

            Console.WriteLine("[tree-fall] retired log " + logEntityId
                + " from " + removed + " peer(s).");
        }

        private static bool CanReceiveRemove(ENetPeerHandle peer)
        {
            return EnetLayer.ENet_PeerChannelCount(peer) > (int)EnetLayer.ENetChannel.REMOVE_ENTITY_OP;
        }

        /// <summary>
        /// Whether this peer has ANY component of an entity checked out - the
        /// evidence that it actually holds the thing, as opposed to the send ledger's
        /// record that we once announced it.
        ///
        /// This is the same map <c>PushTreeSectionMask</c> reads to decide who is told
        /// a tree shrank, which is the whole point: the log and the mask must agree
        /// about who is watching, or a peer sees the crown vanish with nothing falling
        /// (which is exactly what streamed trees did) or a log fall off a tree it
        /// cannot see.
        /// </summary>
        private static bool HoldsAnyComponentOf(ENetPeerHandle peer, long entityId)
        {
            return GameState.Instance.ComponentMap.TryGetValue(peer,
                       out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                && byEntity.TryGetValue(entityId, out Dictionary<uint, ulong>? byComponent)
                && byComponent.Count > 0;
        }

        private static bool TryGetStoredRef(ENetPeerHandle peer, long entityId, uint componentId, out ulong refId)
        {
            refId = 0;
            return GameState.Instance.ComponentMap.TryGetValue(peer,
                       out Dictionary<long, Dictionary<uint, ulong>>? byEntity)
                && byEntity.TryGetValue(entityId, out Dictionary<uint, ulong>? byComponent)
                && byComponent.TryGetValue(componentId, out refId);
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers()
        {
            return PeerManager.Instance.playerState.Keys.ToList();
        }
    }
}
