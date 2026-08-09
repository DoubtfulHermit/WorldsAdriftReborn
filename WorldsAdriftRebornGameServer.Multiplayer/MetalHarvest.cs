namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What one salvage shot did to a node: whether it was the shot that emptied
    /// the node, and - only then - how many units of metal that is worth.
    ///
    /// A hit that merely wears a node down carries <see cref="Units"/> 0 and
    /// <see cref="Depleted"/> false; there is nothing to grant until the node is
    /// gone. Exactly ONE hit per node ever returns <see cref="Depleted"/> true -
    /// the transition - so a caller can grant on it without a "have I already paid
    /// this out" flag of its own.
    /// </summary>
    public readonly struct MetalHitOutcome : IEquatable<MetalHitOutcome>
    {
        public MetalHitOutcome(bool depleted, int units)
        {
            Depleted = depleted;
            Units = units;
        }

        /// <summary>True on the single shot that emptied the node, false otherwise.</summary>
        public bool Depleted { get; }

        /// <summary>Units of metal freed by this shot. Non-zero only when <see cref="Depleted"/>.</summary>
        public int Units { get; }

        /// <summary>The shot changed nothing worth granting (worn a little, or a no-op).</summary>
        public static MetalHitOutcome Nothing => new MetalHitOutcome(false, 0);

        public bool Equals(MetalHitOutcome other) => Depleted == other.Depleted && Units == other.Units;

        public override bool Equals(object? obj) => obj is MetalHitOutcome other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Depleted, Units);

        public override string ToString() => Depleted ? "depleted, " + Units + " units" : "not depleted";
    }

    /// <summary>
    /// The state of every metal node this server can be shot at, and WHEN a run of
    /// salvage shots empties one. The metal analogue of <see cref="TreeHarvest"/>,
    /// and deliberately the simpler of the two.
    ///
    /// WHY THERE IS NO CLOCK HERE, unlike the tree. A tree's 1037 TreeCutterState
    /// is a LATCH - one packet when the beam arrives on a section, one when it
    /// leaves - so "hold the beam and the tree comes apart" is a server timer or it
    /// does not exist. A salvage shot is the opposite: the client's
    /// <c>MultitoolSalvageController.TryDeploy</c> already rate-limits itself to one
    /// deploy per <c>MinDeployInterval</c> (≈0.75 s) and publishes ONE 2106
    /// <c>ShotEvent</c> per deploy (verified: <c>PlayerMultitoolVisualizer.
    /// OnPlayerShotSalvage</c> → <c>MultitoolSalvagerState.TriggerShotEvent</c>).
    /// So each inbound event is already one discrete hit; the cadence lives in the
    /// client and this module only has to COUNT.
    ///
    /// This is the depletion POLICY - how many shots empty a node and what that is
    /// worth. It is NOT the node ledger: <see cref="NodeRegistry"/> stays the
    /// authority on the destroyed flag and the crust points a late joiner is
    /// replayed. The two agree because <see cref="Hit"/> reports the deplete
    /// transition exactly once, and the glue marks the registry destroyed at that
    /// same moment. Keeping them apart is deliberate - a node's persistent,
    /// replayed state and the "how do I mine it" rules have different lifetimes.
    ///
    /// Pure: no ENet, no Improbable types, no game install, so the counting and the
    /// once-only transition are pinned by xUnit rather than by a running client -
    /// the standing caveat (a third of this project's confident static conclusions
    /// have been wrong when run) applies hardest to exactly this kind of state
    /// machine.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly: the server is
    /// a single poll loop.
    /// </summary>
    public sealed class MetalHarvest
    {
        /// <summary>
        /// How many salvage shots empty a node when a placement does not say
        /// otherwise. Invented, like the tree's cut interval: the shipped nugget has
        /// no health or crust of its own (<c>IsSalvageable() =&gt; true</c>,
        /// <c>IsDamaged() =&gt; false</c> unconditionally - findings-metal-deposits),
        /// so there is no authored number to be faithful to. A few shots so holding
        /// the beam reads as work, not so many the tester gives up before it pops.
        /// </summary>
        public const int DefaultShotsToDeplete = 3;

        private sealed class Deposit
        {
            public Deposit(int shotsToDeplete, int unitsYield)
            {
                ShotsToDeplete = shotsToDeplete;
                UnitsYield = unitsYield;
            }

            public int ShotsToDeplete { get; }
            public int UnitsYield { get; }
            public int Hits { get; set; }
            public bool Depleted { get; set; }
        }

        private readonly Dictionary<long, Deposit> _nodes = new Dictionary<long, Deposit>();
        private readonly int _defaultShots;

        public MetalHarvest(int defaultShotsToDeplete = DefaultShotsToDeplete)
        {
            if (defaultShotsToDeplete < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultShotsToDeplete),
                    "a node that empties in zero shots would deplete before anyone shot it");
            }
            _defaultShots = defaultShotsToDeplete;
        }

        /// <summary>
        /// Declares a spawned node as shootable, with what it yields when emptied.
        /// Called when the node's AddEntityOp goes out and its entity id is known -
        /// the same seam as <see cref="TreeHarvest.Plant"/> and
        /// <see cref="NodeRegistry.Register"/>.
        ///
        /// Idempotent by design: every joining client walks the same spawn plan and
        /// reaches this node's step, but there is one node, and the second player
        /// arriving must not refill a node someone has already emptied. The first
        /// call wins; later ones are no-ops.
        /// </summary>
        /// <param name="unitsYield">Units of metal the node frees when emptied. At least one.</param>
        /// <param name="shotsToDeplete">Shots to empty it; defaults to the constructor's default.</param>
        /// <returns>True on the first placement of this id; false thereafter.</returns>
        public bool Place(long nodeEntityId, int unitsYield, int? shotsToDeplete = null)
        {
            if (unitsYield < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(unitsYield), unitsYield,
                    "a node that yields nothing is not a harvest source");
            }
            int shots = shotsToDeplete ?? _defaultShots;
            if (shots < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(shotsToDeplete), shots,
                    "a node that empties in zero shots would deplete before anyone shot it");
            }
            if (_nodes.ContainsKey(nodeEntityId))
            {
                return false;
            }
            _nodes[nodeEntityId] = new Deposit(shots, unitsYield);
            return true;
        }

        /// <summary>Whether an entity id is a node this module is tracking.</summary>
        public bool IsNode(long nodeEntityId) => _nodes.ContainsKey(nodeEntityId);

        /// <summary>Whether a node has been emptied. False for an intact node and a non-node id.</summary>
        public bool IsDepleted(long nodeEntityId) =>
            _nodes.TryGetValue(nodeEntityId, out Deposit? d) && d.Depleted;

        /// <summary>How many shots have landed on a node. 0 for an untouched or non-node id.</summary>
        public int HitsOn(long nodeEntityId) =>
            _nodes.TryGetValue(nodeEntityId, out Deposit? d) ? d.Hits : 0;

        /// <summary>
        /// Shots still needed to empty a node, or null for a non-node or an
        /// already-empty one. For logs and tests.
        /// </summary>
        public int? ShotsRemaining(long nodeEntityId) =>
            _nodes.TryGetValue(nodeEntityId, out Deposit? d) && !d.Depleted
                ? d.ShotsToDeplete - d.Hits
                : (int?)null;

        /// <summary>
        /// Records one salvage shot on a node and reports what it did.
        ///
        /// A shot on a non-node id or an already-empty node is
        /// <see cref="MetalHitOutcome.Nothing"/>, not a throw: the beam legitimately
        /// rests on trees, hulls, players and depleted nodes, and this is driven by
        /// client input, which is never trusted and never fatal. The deplete
        /// transition is returned on exactly ONE shot - the one that reaches the
        /// threshold - so the caller grants and marks the registry destroyed there
        /// and nowhere else.
        /// </summary>
        public MetalHitOutcome Hit(long nodeEntityId)
        {
            if (!_nodes.TryGetValue(nodeEntityId, out Deposit? d) || d.Depleted)
            {
                return MetalHitOutcome.Nothing;
            }

            d.Hits++;
            if (d.Hits >= d.ShotsToDeplete)
            {
                d.Depleted = true;
                return new MetalHitOutcome(true, d.UnitsYield);
            }

            return MetalHitOutcome.Nothing;
        }

        /// <summary>How many nodes are placed. For logs and tests.</summary>
        public int Count => _nodes.Count;
    }
}
