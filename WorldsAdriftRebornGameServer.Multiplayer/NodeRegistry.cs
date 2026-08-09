namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// A single accumulated shot on a node's crust, in LOCAL metres relative to the
    /// entity root. Plain float, NO 4096 factor - 12283 shotPoints is Vector3f, not
    /// fixed point, and mixing that up is the easiest way to get metal wrong
    /// (docs/research/gathering/findings-metal-deposits.md).
    /// </summary>
    public readonly struct ShotPoint
    {
        public ShotPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public override string ToString() => "(" + X + ", " + Y + ", " + Z + ")";
    }

    /// <summary>
    /// The state of one node as a late joiner must be told it, primitives only.
    /// </summary>
    public sealed class NodeSnapshot
    {
        public NodeSnapshot(MetalNode node, bool isDestroyed, IReadOnlyList<ShotPoint> shotPoints)
        {
            Node = node;
            IsDestroyed = isDestroyed;
            ShotPoints = shotPoints;
        }

        /// <summary>The static facts - key, metal, quality, position.</summary>
        public MetalNode Node { get; }

        /// <summary>Whether the node has been depleted/collected. Replayed, never re-triggered.</summary>
        public bool IsDestroyed { get; }

        /// <summary>
        /// The accumulated crust damage, as STATE (not events). A late joiner is
        /// replayed these via the client's own SimulatePastShot (renderer off,
        /// instant, silent); the transient ShotCrustEvent VFX are NEVER replayed, or
        /// the joiner sees every impact flash at once.
        /// </summary>
        public IReadOnlyList<ShotPoint> ShotPoints { get; }
    }

    /// <summary>
    /// The server's ledger of every resource node it has put in the world, and the
    /// ONLY place a node's live harvest state lives. Pure: no ENet, no Improbable
    /// types, no game install, so the two rules that are counter-intuitive enough
    /// to have their own findings doc are pinned by xUnit rather than by a running
    /// game.
    ///
    /// THE TWO RULES (docs/research/gathering/findings-node-relay.md):
    ///
    /// 1. A DESTROYED NODE STAYS IN THE REGISTRY. There is no RemoveEntityOp and
    ///    depletion is state-based, so a node is never forgotten - it is marked
    ///    destroyed and kept. A late joiner is then spawned the node in its
    ///    destroyed state (<see cref="Snapshot"/>). Drop it instead and late
    ///    joiners see intact rocks where everyone else sees depleted ones - the
    ///    single most counter-intuitive rule in the whole subsystem.
    ///
    /// 2. A SHOT IS APPENDED, NEVER REPLACED. shotPoints is a list that GROWS; it
    ///    is replicated in full on every update and replayed linearly on every
    ///    join, so it is capped (<see cref="MaxShotPoints"/>) - a destroyed crust
    ///    is already a few dozen points.
    ///
    /// This registry is the node analogue of TreeHarvest, and it is intentionally
    /// generic over the node KIND: today only the nugget is placed (which has no
    /// crust of its own, so its depletion is a plain destroyed flag), but the same
    /// ledger carries the deposit's shotPoints unchanged when that lands.
    ///
    /// NOT THREAD-SAFE, deliberately, for the same reason as
    /// <see cref="WorldEntityRegistry"/>: the server is a single poll loop.
    /// </summary>
    public sealed class NodeRegistry
    {
        /// <summary>
        /// The most crust-damage points ever kept for one node. Capped because the
        /// list is replicated in full every update and replayed linearly on every
        /// join; 40-60 points is already a destroyed crust. Older points past the
        /// cap are dropped (a full crust is statistically the same hole either way).
        /// </summary>
        public const int MaxShotPoints = 60;

        private sealed class NodeState
        {
            public NodeState(MetalNode node)
            {
                Node = node;
            }

            public MetalNode Node { get; }
            public bool IsDestroyed { get; set; }
            public List<ShotPoint> ShotPoints { get; } = new List<ShotPoint>();
        }

        private readonly Dictionary<long, NodeState> _byEntityId = new Dictionary<long, NodeState>();

        /// <summary>
        /// Records that an entity id is a placed node. Idempotent and keyed by
        /// entity id, exactly like TreeHarvest.Plant: every joining client walks the
        /// identical spawn plan and reaches this step for the same node, but there
        /// is one node, so the second and later calls are no-ops that must NOT reset
        /// its harvest state.
        /// </summary>
        /// <returns>True on the first registration of this id; false thereafter.</returns>
        public bool Register(long entityId, MetalNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            if (_byEntityId.ContainsKey(entityId))
            {
                return false;
            }
            _byEntityId[entityId] = new NodeState(node);
            return true;
        }

        /// <summary>Whether an entity id is a placed node.</summary>
        public bool IsNode(long entityId) => _byEntityId.ContainsKey(entityId);

        /// <summary>The node facts for an entity id, or null if it is not a node.</summary>
        public MetalNode? NodeOf(long entityId) =>
            _byEntityId.TryGetValue(entityId, out NodeState? s) ? s.Node : null;

        /// <summary>
        /// Marks a node depleted/collected. Idempotent. The node STAYS in the
        /// registry (rule 1) so a late joiner can be told the truth about it.
        /// </summary>
        /// <returns>True if this call changed the node from intact to destroyed.</returns>
        public bool MarkDestroyed(long entityId)
        {
            if (!_byEntityId.TryGetValue(entityId, out NodeState? s) || s.IsDestroyed)
            {
                return false;
            }
            s.IsDestroyed = true;
            return true;
        }

        /// <summary>Whether a node is depleted. False for an intact node and for a non-node id.</summary>
        public bool IsDestroyed(long entityId) =>
            _byEntityId.TryGetValue(entityId, out NodeState? s) && s.IsDestroyed;

        /// <summary>
        /// Whether a joiner should be spawned this node in its INTACT form. A
        /// destroyed node is still spawned - it stays in the registry - but in its
        /// destroyed state, which for a nugget (no depletion feedback of its own)
        /// means it is not placed as a pickable rock. The caller uses this to decide
        /// the node's 190602 seed / whether to AddEntity it at all for a late joiner.
        /// </summary>
        public bool ShouldSpawnIntact(long entityId) => IsNode(entityId) && !IsDestroyed(entityId);

        /// <summary>
        /// Appends one crust-damage point to a node (rule 2 - appended, never
        /// replaced), capping at <see cref="MaxShotPoints"/> by dropping the oldest.
        /// A no-op for a non-node id or a node already destroyed.
        /// </summary>
        /// <returns>True if the point was recorded.</returns>
        public bool AddShotPoint(long entityId, ShotPoint point)
        {
            if (!_byEntityId.TryGetValue(entityId, out NodeState? s) || s.IsDestroyed)
            {
                return false;
            }
            s.ShotPoints.Add(point);
            if (s.ShotPoints.Count > MaxShotPoints)
            {
                s.ShotPoints.RemoveAt(0);
            }
            return true;
        }

        /// <summary>The accumulated crust damage for a node, or an empty list.</summary>
        public IReadOnlyList<ShotPoint> ShotPointsOf(long entityId) =>
            _byEntityId.TryGetValue(entityId, out NodeState? s)
                ? s.ShotPoints.ToArray()
                : Array.Empty<ShotPoint>();

        /// <summary>
        /// The node's full state as a late joiner must be told it, or null if the id
        /// is not a node. THIS IS THE REPLAY POINT: there is no snapshot queue and no
        /// join-time state dump - the client asks for a node's components at its own
        /// checkout moment, and the seed is built from this snapshot, so a joiner
        /// arriving after a node was depleted is told it is depleted.
        /// </summary>
        public NodeSnapshot? Snapshot(long entityId) =>
            _byEntityId.TryGetValue(entityId, out NodeState? s)
                ? new NodeSnapshot(s.Node, s.IsDestroyed, s.ShotPoints.ToArray())
                : null;

        /// <summary>Every placed node, in registration order. For fan-out and logs.</summary>
        public IReadOnlyList<long> EntityIds => _byEntityId.Keys.ToArray();
    }
}
