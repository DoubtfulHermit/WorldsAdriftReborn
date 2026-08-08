namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What one client's <c>1037 TreeCutterState</c> currently says its salvage
    /// beam is resting on. The wire record, field for field
    /// (<c>TreeCutterStateData{ EntityId treeEntityId, int sectionId, bool aboveOrBelow }</c>).
    ///
    /// IT IS A LATCH, NOT A PULSE, and every design decision downstream follows
    /// from that. <c>TreeCuttingBehaviour.Update</c> writes this every frame, but
    /// the writer's <c>FinishAndSend</c> suppresses a send when nothing changed -
    /// so the server sees ONE packet when the beam arrives on a section and ONE
    /// when it leaves. There is no per-hit event to count. Anything that wants a
    /// repeating chop has to supply its own clock, which is what
    /// <see cref="TreeHarvest"/> is.
    /// </summary>
    public readonly struct TreeCutSignal : IEquatable<TreeCutSignal>
    {
        public TreeCutSignal(long treeEntityId, int sectionId, bool above)
        {
            TreeEntityId = treeEntityId;
            SectionId = sectionId;
            Above = above;
        }

        /// <summary>The tree being aimed at. Invalid (-1) or 0 when the beam is on nothing.</summary>
        public long TreeEntityId { get; }

        /// <summary>The section under the beam, or -1 for none.</summary>
        public int SectionId { get; }

        /// <summary>The wire's <c>aboveOrBelow</c>: the hit was above the section's own origin.</summary>
        public bool Above { get; }

        /// <summary>
        /// Whether this names a real target. The client publishes
        /// <c>{InvalidEntityId, -1, false}</c> to say "nothing", and
        /// <c>InvalidEntityId</c> is -1 while an unresolvable entity reads as 0 -
        /// so both are rejected here rather than at the four call sites that
        /// would otherwise each have to remember.
        /// </summary>
        public bool IsEngaged => TreeEntityId > 0 && SectionId >= 0;

        /// <summary>The beam is on nothing.</summary>
        public static TreeCutSignal Disengaged => new TreeCutSignal(0, -1, false);

        public bool Equals(TreeCutSignal other)
        {
            return TreeEntityId == other.TreeEntityId && SectionId == other.SectionId && Above == other.Above;
        }

        public override bool Equals(object? obj) => obj is TreeCutSignal other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(TreeEntityId, SectionId, Above);

        public static bool operator ==(TreeCutSignal a, TreeCutSignal b) => a.Equals(b);

        public static bool operator !=(TreeCutSignal a, TreeCutSignal b) => !a.Equals(b);

        public override string ToString()
        {
            return IsEngaged
                ? "cutting tree " + TreeEntityId + " section " + SectionId + (Above ? " (above)" : " (below)")
                : "not cutting";
        }
    }

    /// <summary>
    /// One tree's mask changed, and everything a caller needs to act on it -
    /// including the two fields nothing in this assembly consumes yet, which are
    /// the inventory-grant seam. See <see cref="WoodType"/>.
    /// </summary>
    public readonly struct TreeSectionMaskChange
    {
        public TreeSectionMaskChange(long treeEntityId, long cutterEntityId, int sectionId,
                                     int fallingMask, int sectionMask, int sectionsFelled, string woodType)
        {
            TreeEntityId = treeEntityId;
            CutterEntityId = cutterEntityId;
            SectionId = sectionId;
            FallingMask = fallingMask;
            SectionMask = sectionMask;
            SectionsFelled = sectionsFelled;
            WoodType = woodType;
        }

        /// <summary>The tree. This is the entity id the 1036 update must be addressed to.</summary>
        public long TreeEntityId { get; }

        /// <summary>The PLAYER entity whose beam did it. Who the wood belongs to.</summary>
        public long CutterEntityId { get; }

        /// <summary>The section that was cut, after any forwarding up the branch.</summary>
        public int SectionId { get; }

        /// <summary>The bits that came away.</summary>
        public int FallingMask { get; }

        /// <summary>The tree's NEW mask - what goes on the wire as 1036 sectionMask.</summary>
        public int SectionMask { get; }

        /// <summary>How many sections came away. The natural quantity for a yield.</summary>
        public int SectionsFelled { get; }

        /// <summary>
        /// Bossa's authored species for this tree - "birch" for `Tree`, recovered
        /// from the shipped <c>_unityworker</c> prefabs
        /// (docs/research/loop/data/tree_woodtypes.json). The client never learns
        /// it: <c>TreeFSimState.woodType</c> is written only by
        /// <c>TreeFsimVisualizer</c>, which is UnityWorker-only and absent from the
        /// client build. It is here because it is HALF OF THE INVENTORY GRANT and
        /// the other half is not ours to write - see the seam in
        /// <c>TreeCutterState_Handler</c>.
        /// </summary>
        public string WoodType { get; }

        public override string ToString()
        {
            return "tree " + TreeEntityId + " section " + SectionId + " cut by " + CutterEntityId
                + ": " + SectionsFelled + " x " + WoodType
                + ", mask -> " + Convert.ToString(SectionMask, 2);
        }
    }

    /// <summary>
    /// The state of every harvestable tree in the world, and WHEN a held beam
    /// takes the next chunk out of one.
    ///
    /// TWO JOBS, and they are here together because neither is meaningful alone:
    ///
    /// 1. It is the authority on each tree's <c>sectionMask</c>. The mask is not
    ///    a constant after the first cut, so the <c>1036</c> seed a LATER-JOINING
    ///    client is served has to come from here, or the second player walks up
    ///    to a half-chopped tree and sees it whole.
    /// 2. It supplies the cadence the wire does not. See
    ///    <see cref="TreeCutSignal"/>: the cut signal is a latch, so "hold the
    ///    beam and the tree comes apart" is a server-side timer or it does not
    ///    exist.
    ///
    /// WHY THE FIRST CUT WAITS A FULL INTERVAL. A latch arrives the instant the
    /// beam crosses onto a section, including while the player is sweeping past
    /// it on the way to something else. Cutting immediately would mean a section
    /// falls off every tree you glance at with the trigger held; waiting means the
    /// beam has to actually rest on it. The shipped multitool charges before it
    /// deploys, so this is also roughly what the animation implies.
    ///
    /// Pure: no ENet, no Improbable types, no game install. The clock is injected
    /// (<see cref="IClock"/>, same one <see cref="MirrorSchedule"/> uses) so
    /// "0.75 seconds" can be asserted on without sleeping - and, more importantly,
    /// so it is measured in SECONDS rather than in main-loop iterations. That
    /// distinction has already cost this project one debugging round: the loop
    /// turns once per ENet EVENT, not once per poll timeout, so counting
    /// iterations means a busy server chops hundreds of times a second.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly. The server
    /// is a single poll loop.
    /// </summary>
    public sealed class TreeHarvest
    {
        /// <summary>
        /// How long a held beam takes to remove one section. Invented - the
        /// original's rate lived in the GSim and <c>SalvageAndRepairState.period</c>
        /// has zero readers anywhere in the client, so there is nothing to be
        /// faithful to. Slow enough that a 12-section tree is not instantaneous,
        /// fast enough that holding a beam for it feels like doing something.
        /// </summary>
        public static readonly TimeSpan DefaultCutInterval = TimeSpan.FromSeconds(0.75);

        private sealed class Stand
        {
            public Stand(TreeTopology topology, string woodType)
            {
                Topology = topology;
                WoodType = woodType;
                SectionMask = topology.FullMask;
            }

            public TreeTopology Topology { get; }
            public string WoodType { get; }
            public int SectionMask { get; set; }
        }

        private sealed class Latch
        {
            public TreeCutSignal Signal;
            public TimeSpan DueAt;
        }

        private readonly IClock _clock;
        private readonly TimeSpan _interval;
        private readonly Dictionary<long, Stand> _trees = new Dictionary<long, Stand>();
        private readonly Dictionary<long, Latch> _latches = new Dictionary<long, Latch>();

        public TreeHarvest(IClock clock, TimeSpan? cutInterval = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _interval = cutInterval ?? DefaultCutInterval;

            if (_interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cutInterval),
                    "a non-positive cut interval would fell a whole tree in one main-loop turn");
            }
        }

        /// <summary>The cut cadence in force.</summary>
        public TimeSpan CutInterval => _interval;

        /// <summary>
        /// Declares a spawned tree, whole. Called when the tree's AddEntityOp goes
        /// out and its entity id is therefore known.
        ///
        /// Idempotent by design: every joining client walks the same spawn plan and
        /// reaches the tree's AddEntity step, but there is only ONE tree and it
        /// must not be reset to whole by the second player arriving. The first call
        /// wins and later ones are ignored.
        /// </summary>
        public bool Plant(long treeEntityId, TreeTopology topology, string woodType)
        {
            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }
            if (_trees.ContainsKey(treeEntityId))
            {
                return false;
            }

            _trees.Add(treeEntityId, new Stand(topology, woodType ?? string.Empty));
            return true;
        }

        /// <summary>Whether an entity id is a tree this server is tracking.</summary>
        public bool IsTree(long entityId) => _trees.ContainsKey(entityId);

        /// <summary>
        /// A tree's CURRENT mask, or null if it is not a tree. This is what the
        /// 1036 seed must use - not <see cref="TreeTopology.FullMask"/> - so that a
        /// client checking the tree out after it has been chopped is told the truth.
        /// </summary>
        public int? MaskOf(long treeEntityId)
        {
            return _trees.TryGetValue(treeEntityId, out Stand? stand) ? stand.SectionMask : null;
        }

        /// <summary>A tree's topology, or null if it is not a tree.</summary>
        public TreeTopology? TopologyOf(long treeEntityId)
        {
            return _trees.TryGetValue(treeEntityId, out Stand? stand) ? stand.Topology : null;
        }

        /// <summary>A tree's authored wood species, or null if it is not a tree.</summary>
        public string? WoodTypeOf(long treeEntityId)
        {
            return _trees.TryGetValue(treeEntityId, out Stand? stand) ? stand.WoodType : null;
        }

        /// <summary>
        /// A 1037 latch arrived from one player. Returns whether it changed
        /// anything - a repeat of the same latch does NOT restart the timer, or a
        /// client that re-sent it faster than the interval would postpone every cut
        /// forever.
        ///
        /// A signal naming an entity that is not a tree disengages rather than
        /// throwing: the beam legitimately rests on rocks, hulls and players, and
        /// this is client input, which is never trusted and never fatal.
        /// </summary>
        public bool OnCutSignal(long cutterEntityId, TreeCutSignal signal)
        {
            bool engaged = signal.IsEngaged && _trees.ContainsKey(signal.TreeEntityId);

            if (!engaged)
            {
                return _latches.Remove(cutterEntityId);
            }

            if (_latches.TryGetValue(cutterEntityId, out Latch? existing))
            {
                if (existing.Signal == signal)
                {
                    return false;
                }
                existing.Signal = signal;
                existing.DueAt = _clock.Elapsed + _interval;
                return true;
            }

            _latches.Add(cutterEntityId, new Latch { Signal = signal, DueAt = _clock.Elapsed + _interval });
            return true;
        }

        /// <summary>What a player's beam is currently resting on.</summary>
        public TreeCutSignal SignalOf(long cutterEntityId)
        {
            return _latches.TryGetValue(cutterEntityId, out Latch? latch) ? latch.Signal : TreeCutSignal.Disengaged;
        }

        /// <summary>
        /// Drops a departed player's latch. Belongs in the same place every other
        /// per-player collection is cleaned; a leaked latch would keep chopping a
        /// tree on behalf of somebody who logged out.
        /// </summary>
        public bool Forget(long cutterEntityId) => _latches.Remove(cutterEntityId);

        /// <summary>How many beams are currently resting on a tree section.</summary>
        public int EngagedCount => _latches.Count;

        /// <summary>
        /// Every cut whose timer has elapsed, applied. Call it once per main-loop
        /// turn; it is cheap when nothing is engaged (the common case is an empty
        /// dictionary).
        ///
        /// The timer is rearmed whether or not the cut succeeded, so a beam held on
        /// the last remaining section polls at the interval instead of spinning.
        /// Only cuts that actually changed a mask are returned - there is nothing
        /// to tell a client about a refusal.
        /// </summary>
        public IReadOnlyList<TreeSectionMaskChange> Due()
        {
            if (_latches.Count == 0)
            {
                return Array.Empty<TreeSectionMaskChange>();
            }

            TimeSpan now = _clock.Elapsed;
            List<TreeSectionMaskChange> changes = new List<TreeSectionMaskChange>();

            foreach (KeyValuePair<long, Latch> entry in _latches)
            {
                Latch latch = entry.Value;
                if (now < latch.DueAt)
                {
                    continue;
                }

                latch.DueAt = now + _interval;

                if (!_trees.TryGetValue(latch.Signal.TreeEntityId, out Stand? stand))
                {
                    continue;
                }

                TreeCut cut = stand.Topology.Cut(stand.SectionMask, latch.Signal.SectionId, latch.Signal.Above);
                if (!cut.DidCut)
                {
                    continue;
                }

                int felled = stand.Topology.ActiveCount(cut.FallingMask);
                stand.SectionMask = cut.RemainingMask;

                changes.Add(new TreeSectionMaskChange(
                    latch.Signal.TreeEntityId,
                    entry.Key,
                    cut.SectionId,
                    cut.FallingMask,
                    cut.RemainingMask,
                    felled,
                    stand.WoodType));
            }

            return changes;
        }
    }
}
