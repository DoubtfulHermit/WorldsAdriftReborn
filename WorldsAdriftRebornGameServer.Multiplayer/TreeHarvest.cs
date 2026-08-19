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
        /// The ONE section that splinters into wood at the cut point - the section
        /// the beam was actually resting on.
        ///
        /// WHY A CUT PAYS FOR ONE SECTION AND NOT FOR EVERYTHING IT SEVERED. Aim at
        /// the base of a nine-section palm and <see cref="FallingMask"/> is eight
        /// bits; paying for all eight is "the whole tree in one go", which is the
        /// exact complaint this split exists to answer. Retail never paid for the
        /// severed part either: <c>TreeSection.Harvest</c> (acs/TreeSection.cs:29-85)
        /// grants nothing at all - it hands the severed sections to
        /// <c>SpawnNewTree</c> as a NEW TREE, and you get their timber by chopping
        /// THAT, section by section, wherever it came to rest.
        ///
        /// So the wood is conserved rather than reduced: every section is paid for
        /// exactly once, on the cut where it is the section under the beam. A palm
        /// still yields nine wood; it just takes nine cuts instead of two.
        /// </summary>
        public int SplinterMask => 1 << SectionId;

        /// <summary>
        /// What actually falls: everything the cut severed EXCEPT the section that
        /// splintered into wood. This - not <see cref="FallingMask"/> - is the mask a
        /// felled log is dropped with, and the reason is conservation: the splintered
        /// section is already in somebody's inventory, so a log carrying it too would
        /// let the same section be harvested twice.
        ///
        /// Zero when the cut took a single outermost section. That is not a failure:
        /// a lone twig has nothing left to fall, so no log is dropped and the cut
        /// behaves exactly as it did before logs existed.
        /// </summary>
        public int LogMask => FallingMask & ~SplinterMask;

        /// <summary>
        /// How many sections this cut turns into wood immediately: everything it
        /// severed, minus everything that left in the log still owing its timber.
        ///
        /// One, for every cut authored data can produce, because the falling mask
        /// always contains the section the beam was on. Written as the DIFFERENCE
        /// rather than as the constant 1 so that it stays the exact complement of
        /// <see cref="LogMask"/> whatever the topology does - the two together are
        /// always <see cref="SectionsFelled"/>, which is the conservation law that
        /// stops a section being paid for twice or not at all.
        /// </summary>
        public int SectionsSplintered
        {
            get
            {
                int logSections = 0;
                for (int mask = LogMask; mask != 0; mask &= mask - 1)
                {
                    logSections++;
                }
                return SectionsFelled - logSections;
            }
        }

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
    /// One tree grew its sections back. Everything the wire fan-out needs to
    /// stand it up on every client's screen: the tree, and the mask it is now.
    ///
    /// There is no cutter and no yield here on purpose - regrowth is not a
    /// harvest, so nobody is paid for it. The <see cref="SectionMask"/> is always
    /// the whole tree (<see cref="TreeTopology.FullMask"/>); it is a field rather
    /// than implied so the same <c>SetSectionMask</c> fan-out that a cut uses can
    /// consume a respawn without a second code path.
    /// </summary>
    public readonly struct TreeRespawn
    {
        public TreeRespawn(long treeEntityId, int sectionMask)
        {
            TreeEntityId = treeEntityId;
            SectionMask = sectionMask;
        }

        /// <summary>The tree. This is the entity id the 1036 update must be addressed to.</summary>
        public long TreeEntityId { get; }

        /// <summary>The tree's NEW mask - the whole tree - to put on the wire as 1036 sectionMask.</summary>
        public int SectionMask { get; }

        public override string ToString()
        {
            return "tree " + TreeEntityId + " respawned, mask -> " + Convert.ToString(SectionMask, 2);
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

        /// <summary>
        /// How long a chopped tree stands as a diminished thing before it grows
        /// its sections back. THIS is the knob that P1-9 asked for - the old
        /// <c>Trees.RespawnTime</c> was a wire field the client never reads
        /// (<c>respawn_time</c> has zero references in the entire decompile), so
        /// "nothing respawns" was literally true; the regrowth this server drives
        /// is authored HERE instead, by resetting the tree's <c>sectionMask</c>
        /// back to whole, which is the only channel the client actually acts on.
        ///
        /// RECONSTRUCTED, not recovered. Retail's cadence lived in the GSim and is
        /// unrecoverable - even the units of the shipped <c>respawnTime</c> field
        /// are unknown. Five minutes is a deliberate, documented placeholder: long
        /// enough that regrowth reads as the world healing rather than as a bug,
        /// short enough that a session sees it happen. Tune it through the
        /// constructor; it is asserted on in seconds off the injected clock, never
        /// counted in main-loop turns (see <see cref="DefaultCutInterval"/> for why
        /// that distinction has already cost this project a debugging round).
        ///
        /// DETERMINISTIC: a fixed delay, no random jitter, so two servers fed the
        /// same cuts regrow every tree at the same instant.
        /// </summary>
        public static readonly TimeSpan DefaultRespawnDelay = TimeSpan.FromMinutes(5);

        /// <summary>
        /// HOW THIS RELATES TO RETAIL'S "UNDERSTORM". Retail did not regrow each
        /// tree on its own clock: the world reset on a GLOBAL cadence of roughly
        /// 1.5-2 hours, an understorm sweeping through and replacing resources
        /// (worldsadrift.fandom.com/wiki/Resources). That is a different SHAPE from
        /// what this class does, not merely a different number - one synchronized
        /// world-wide event versus N independent per-tree timers - and the
        /// difference is player-visible: retail let you strip an area bare and know
        /// it stayed bare until the storm, where this heals each tree quietly on its
        /// own schedule.
        ///
        /// The per-tree timer is what this server can honestly do today, because
        /// there is no weather/world-event system to hang a global reset on (the
        /// whole weather pillar is unserved). If an understorm is ever built, tree
        /// regrowth should STOP using its own delay and ride that event instead:
        /// the seam is exactly <see cref="DueRespawns"/>, which would become
        /// "reset every stand" called by the storm rather than "reset the stands
        /// whose timers elapsed". <see cref="UnderstormCadence"/> is that cadence,
        /// recorded here so the eventual global system has the number and so the
        /// operator can approximate it today by setting the delay to it.
        /// </summary>
        public static readonly TimeSpan UnderstormCadence = TimeSpan.FromMinutes(105);

        /// <summary>
        /// Reads a respawn delay from an operator-supplied string (the
        /// <c>WAREBORN_TREE_RESPAWN_SECONDS</c> knob), or null to accept
        /// <see cref="DefaultRespawnDelay"/>.
        ///
        /// Whole seconds, invariant culture, and anything unparseable or
        /// non-positive returns null rather than throwing: a typo in an environment
        /// variable must not stop a server booting, and the caller logs what it
        /// settled on. Seconds rather than minutes because the useful range spans
        /// "30 for testing the loop" to "6300 for retail's understorm cadence".
        /// </summary>
        public static TimeSpan? ParseRespawnDelay(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (!double.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double seconds))
            {
                return null;
            }

            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(seconds);
        }

        private sealed class Stand
        {
            public Stand(TreeTopology topology, string woodType, int sectionMask, bool felled)
            {
                Topology = topology;
                WoodType = woodType;
                SectionMask = sectionMask;
                Felled = felled;
            }

            public TreeTopology Topology { get; }
            public string WoodType { get; }
            public int SectionMask { get; set; }

            /// <summary>
            /// Whether this stand is a LOG lying on the ground rather than a tree
            /// rooted in the island. Two rules turn on it and nothing else does:
            /// a log never regrows (<see cref="DueRespawns"/> skips it - a trunk that
            /// sprouted back into a whole tree while you were chopping it would be
            /// absurd), and a log's LAST section is harvestable where a standing
            /// tree's is not (acs/TreeSection.cs:41-44 keeps a stump; a log has no
            /// stump to keep, and refusing its final section would strand that wood
            /// for ever).
            /// </summary>
            public bool Felled { get; }

            /// <summary>
            /// When this tree's sections grow back, or null while it has nothing to
            /// regrow (it is whole, or has never been cut). (Re)armed on every cut
            /// and cleared once the tree is whole again, so a tree only regrows
            /// after a full <see cref="DefaultRespawnDelay"/> in which NOBODY took a
            /// section out of it - an actively harvested tree never resets under the
            /// player mid-chop.
            /// </summary>
            public TimeSpan? RespawnDueAt { get; set; }
        }

        private sealed class Latch
        {
            public TreeCutSignal Signal;
            public TimeSpan DueAt;
        }

        private readonly IClock _clock;
        private readonly TimeSpan _interval;
        private readonly TimeSpan _respawnDelay;
        private readonly Dictionary<long, Stand> _trees = new Dictionary<long, Stand>();
        private readonly Dictionary<long, Latch> _latches = new Dictionary<long, Latch>();

        public TreeHarvest(IClock clock, TimeSpan? cutInterval = null, TimeSpan? respawnDelay = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _interval = cutInterval ?? DefaultCutInterval;
            _respawnDelay = respawnDelay ?? DefaultRespawnDelay;

            if (_interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(cutInterval),
                    "a non-positive cut interval would fell a whole tree in one main-loop turn");
            }
            if (_respawnDelay <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(respawnDelay),
                    "a non-positive respawn delay would regrow a tree the instant it was chopped");
            }
        }

        /// <summary>The cut cadence in force.</summary>
        public TimeSpan CutInterval => _interval;

        /// <summary>The regrowth delay in force.</summary>
        public TimeSpan RespawnDelay => _respawnDelay;

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

            _trees.Add(treeEntityId, new Stand(topology, woodType ?? string.Empty, topology.FullMask, felled: false));
            return true;
        }

        /// <summary>
        /// Declares a FELLED LOG harvestable - the trunk a cut just shed, lying
        /// wherever it came down, with only the sections it carried away.
        ///
        /// THIS IS WHAT MAKES A TREE COME APART PIECE BY PIECE. Retail's
        /// <c>TreeSection.Harvest</c> handed the severed sections to
        /// <c>SpawnNewTree</c> (acs/TreeSection.cs:81) as a WHOLE NEW TREE ENTITY -
        /// same prefab, same <c>TreeBase</c>, its own <c>sectionMask</c> - so the
        /// thing on the ground was chopped by exactly the same code that chopped the
        /// standing one. Planting the log here is that, and nothing more: from this
        /// call the log has a mask, a topology and a cut cadence, so a beam resting
        /// on it splits a piece off wherever it is aimed.
        ///
        /// <paramref name="sectionMask"/> is the log's mask and NOT the topology's
        /// full mask, which is the whole difference from <see cref="Plant"/>: a log
        /// is a fragment of a tree, and seeding it whole would have the severed crown
        /// check out as a complete second tree standing inside the first.
        /// </summary>
        public bool PlantFelled(long logEntityId, TreeTopology topology, string woodType, int sectionMask)
        {
            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }
            if (sectionMask == 0)
            {
                // A log with no sections is not a log. Refusing rather than planting
                // an empty stand keeps "IsTree means there is something to chop" true.
                return false;
            }
            if (_trees.ContainsKey(logEntityId))
            {
                return false;
            }

            _trees.Add(logEntityId,
                new Stand(topology, woodType ?? string.Empty, sectionMask & topology.FullMask, felled: true));
            return true;
        }

        /// <summary>
        /// Forgets a stand entirely, and every beam resting on it.
        ///
        /// For a LOG being retired: the entity is about to stop existing, so leaving
        /// its stand behind would let a held latch keep cutting a tree that is no
        /// longer on anybody's screen, and leaving the latch behind would keep that
        /// player's beam pointed at a dead id instead of disengaging.
        ///
        /// Returns whether there was anything to forget.
        /// </summary>
        public bool Uproot(long entityId)
        {
            bool removed = _trees.Remove(entityId);

            List<long>? orphaned = null;
            foreach (KeyValuePair<long, Latch> entry in _latches)
            {
                if (entry.Value.Signal.TreeEntityId == entityId)
                {
                    (orphaned ??= new List<long>()).Add(entry.Key);
                }
            }
            if (orphaned != null)
            {
                foreach (long cutter in orphaned)
                {
                    _latches.Remove(cutter);
                }
            }

            return removed;
        }

        /// <summary>Whether an entity id is a tree this server is tracking.</summary>
        public bool IsTree(long entityId) => _trees.ContainsKey(entityId);

        /// <summary>
        /// Whether a stand is a felled log rather than a rooted tree, or null if it
        /// is neither.
        /// </summary>
        public bool? IsFelled(long entityId)
        {
            return _trees.TryGetValue(entityId, out Stand? stand) ? stand.Felled : (bool?)null;
        }

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

                // THE LAST PIECE OF A LOG. Cut refuses at one active section
                // (TreeCutOutcome.RefusedLastSection) because acs/TreeSection.cs:41-44
                // keeps a stump: a rooted tree must never be chopped away to nothing.
                // A log has no stump to keep. Refusing here would leave one section of
                // every trunk lying on the ground, unharvestable, until its linger ran
                // out - so the final section of a FELLED stand is taken, and the log
                // is left with an empty mask for the caller to retire.
                if (!cut.DidCut)
                {
                    if (!stand.Felled
                        || cut.Outcome != TreeCutOutcome.RefusedLastSection
                        || stand.SectionMask == 0)
                    {
                        continue;
                    }

                    int lastSection = LowestSectionIn(stand.SectionMask);
                    stand.SectionMask = 0;
                    stand.RespawnDueAt = null;

                    changes.Add(new TreeSectionMaskChange(
                        latch.Signal.TreeEntityId,
                        entry.Key,
                        lastSection,
                        1 << lastSection,
                        0,
                        1,
                        stand.WoodType));
                    continue;
                }

                int felled = stand.Topology.ActiveCount(cut.FallingMask);
                stand.SectionMask = cut.RemainingMask;

                // A cut always leaves the tree smaller than whole, so it now has
                // sections to regrow. Arm (or push out) the regrowth timer: it
                // fires a full delay after the LAST cut, so a beam still working the
                // tree keeps it diminished and only an abandoned tree grows back.
                //
                // A LOG NEVER REGROWS. It is already a severed fragment; regrowing it
                // to its parent prefab's full mask would sprout a whole tree out of a
                // trunk lying on the ground.
                stand.RespawnDueAt = stand.Felled || stand.SectionMask == stand.Topology.FullMask
                    ? (TimeSpan?)null
                    : now + _respawnDelay;

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

        /// <summary>
        /// Whether a tree has a regrowth pending - it has been chopped and is
        /// waiting out its <see cref="RespawnDelay"/>. False for a whole tree, a
        /// tree that just respawned, and anything that is not a tree. Exposed so a
        /// test (and a diagnostic) can see the timer without reaching into state.
        /// </summary>
        public bool IsAwaitingRespawn(long treeEntityId)
        {
            return _trees.TryGetValue(treeEntityId, out Stand? stand) && stand.RespawnDueAt != null;
        }

        /// <summary>
        /// Every tree whose regrowth timer has elapsed, reset to whole and reported
        /// so the wire can stand it back up. Call it once per main-loop turn
        /// alongside <see cref="Due()"/>; it is cheap when nothing is regrowing -
        /// the common case walks the tree dictionary, finds every
        /// <c>RespawnDueAt</c> null, and allocates nothing.
        ///
        /// WHY IT RESETS TO THE WHOLE TREE, not to what was standing before the
        /// last cut: retail's respawn does the same - <c>TreeFsimVisualizer.Respawn</c>
        /// (acs/TreeFsimVisualizer.cs:146-150) sets the mask back to every section
        /// and calls <c>tree.ResetTree()</c>. The space-occupancy guard it runs
        /// first (an <c>OverlapCapsule</c> sweep that refuses to regrow into a
        /// player or a parked ship) is UnityWorker-only physics and cannot run on
        /// this server, so regrowth here is unconditional once the delay is up. That
        /// is called out honestly rather than faked: there is no collision authority
        /// to consult.
        ///
        /// The client needs no new component to show this. It reactivates the
        /// sections purely off the 1036 <c>sectionMask</c> climbing back to full
        /// (<c>TreeVisualizer</c> re-inits the section GameObjects), and
        /// <c>TreeClientVisualizer</c> plays a break effect ONLY on bits LEAVING the
        /// mask - so a mask going UP is silent, which is exactly a tree quietly
        /// standing whole again.
        ///
        /// DETERMINISTIC: fires exactly <see cref="RespawnDelay"/> after a tree's
        /// last cut, measured on the injected clock. No random jitter.
        /// </summary>
        public IReadOnlyList<TreeRespawn> DueRespawns()
        {
            if (_trees.Count == 0)
            {
                return Array.Empty<TreeRespawn>();
            }

            TimeSpan now = _clock.Elapsed;
            List<TreeRespawn>? respawns = null;

            foreach (KeyValuePair<long, Stand> entry in _trees)
            {
                Stand stand = entry.Value;
                if (stand.RespawnDueAt == null || now < stand.RespawnDueAt.Value)
                {
                    continue;
                }

                stand.SectionMask = stand.Topology.FullMask;
                stand.RespawnDueAt = null;

                (respawns ??= new List<TreeRespawn>()).Add(new TreeRespawn(entry.Key, stand.SectionMask));
            }

            return respawns ?? (IReadOnlyList<TreeRespawn>)Array.Empty<TreeRespawn>();
        }

        /// <summary>
        /// Operator-triggered understorm approximation: restore every damaged tree
        /// immediately and cancel its pending per-tree timer. Whole trees are not
        /// returned because no client update is needed for them.
        /// </summary>
        public IReadOnlyList<TreeRespawn> ResetAll()
        {
            List<TreeRespawn>? respawns = null;
            foreach (KeyValuePair<long, Stand> entry in _trees)
            {
                Stand stand = entry.Value;
                // A log is not a damaged tree, it is a piece of one. An understorm
                // that "restored" it would grow a whole tree out of a trunk on the
                // ground; the log simply lies there until its linger expires.
                if (stand.Felled) continue;
                if (stand.SectionMask == stand.Topology.FullMask) continue;
                stand.SectionMask = stand.Topology.FullMask;
                stand.RespawnDueAt = null;
                (respawns ??= new List<TreeRespawn>()).Add(
                    new TreeRespawn(entry.Key, stand.SectionMask));
            }
            return respawns ?? (IReadOnlyList<TreeRespawn>)Array.Empty<TreeRespawn>();
        }

        /// <summary>
        /// The lowest set bit's index in a non-zero mask. Only ever asked of a log
        /// with exactly one section left, where "lowest" and "only" are the same
        /// thing; written generally so a caller cannot depend on that.
        /// </summary>
        private static int LowestSectionIn(int sectionMask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((sectionMask & (1 << i)) != 0)
                {
                    return i;
                }
            }
            return 0;
        }
    }
}
