using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHAT RETAIL DID WHEN A TREE CAME APART, and the arithmetic that lets this
    /// server do the same thing without an FSim.
    ///
    /// THE RETAIL SEQUENCE, from the decompile, in order:
    /// <list type="number">
    /// <item><c>TreeSection.Harvest</c> (acs/TreeSection.cs:29-85) computes two
    ///   masks - <c>num</c>, what comes away, and <c>num2</c>, what is left.</item>
    /// <item>It calls <c>TreeFsimVisualizer.SpawnNewTree(salvagerId, num)</c>
    ///   FIRST (acs/TreeSection.cs:78), which fires the <c>TriggerSpawnNewTreeBit</c>
    ///   event carrying the PARENT tree's position and rotation, the falling mask,
    ///   the salvager, and the parent's linear and angular velocity.</item>
    /// <item>Only THEN <c>ChangeMask(num2)</c> (acs/TreeSection.cs:79) shrinks the
    ///   standing tree.</item>
    /// <item>The severed part exists as ANOTHER tree entity - the same prefab, at
    ///   the same pose, with only the fallen sections in its mask and
    ///   <c>dynamic = true</c> - and the UnityWorker tips it over with real
    ///   physics.</item>
    /// </list>
    ///
    /// That ordering is not incidental and is reproduced exactly by
    /// <c>Game.Gathering.FallingLogService</c>: the log appears while the crown is
    /// still standing, and the crown then vanishes underneath it. Reversed, there
    /// is a frame in which the tree is visibly bald.
    ///
    /// WHY THIS IS RENDERABLE HERE, when <c>Trees.Dynamic</c>'s remarks once said
    /// a fall was blocked. Three things decide it and all three now point the same
    /// way:
    /// <list type="number">
    /// <item>The CLIENT never simulated the fall even on retail.
    ///   <c>TreeBase.ResetCOMHackCoroutine</c> (acs/TreeBase.cs:548-560) only
    ///   un-kinematics the rigidbody when <c>dynamic &amp;&amp; !WorldsAdrift.IsClient</c>;
    ///   on a client a dynamic tree stays KINEMATIC and is moved purely by served
    ///   190602 updates, through <c>FixedUpdateLerpLocalTransformBehaviour</c>,
    ///   which interpolates between them. A served arc is therefore not an
    ///   approximation of what a retail client saw - it is the same code path.</item>
    /// <item><c>dynamic = true</c> is the flag that ENABLES that path.
    ///   <c>TreeBase.SetupRelativeTransformBehaviour</c> (acs/TreeBase.cs:191-198)
    ///   disables <c>RelativeParentTransformChildHierarchyBehaviour</c> when a tree
    ///   is NOT dynamic - a static tree ignores transform updates by construction -
    ///   and <c>TreeBase.Dynamic</c>'s setter starts the falling-tree audio loop,
    ///   which on a log that is actually falling is exactly right rather than the
    ///   trap it is on a standing tree.</item>
    /// <item>Entity REMOVAL exists now. When the analysis on
    ///   <see cref="Trees.Dynamic"/> was written this server had none, so every
    ///   felled log would have been permanent litter; native channel 5 carries
    ///   <c>RemoveEntity</c> today (docs/HANDOVER.md) and the log is retired on a
    ///   timer.</item>
    /// </list>
    ///
    /// WHAT IS STILL NOT RETAIL, stated rather than faked:
    /// <list type="bullet">
    /// <item>The fall is AUTHORED, not simulated. It cannot bounce off a ship, come
    ///   to rest on a slope, or land differently twice.</item>
    /// <item>It cannot CRUSH anybody. There is no damage authority on this server at
    ///   all, so a log passing through a player does nothing.</item>
    /// <item>The log does not come to rest ON anything. It lands flat at the height
    ///   its parent stood at, so a trunk felled on a slope will not lie along it.</item>
    /// </list>
    ///
    /// THE LOG IS CHOPPABLE, and the note that used to stand here saying it could not
    /// be was wrong about the reason rather than about the risk. It said the log's
    /// wood "was already granted when the sections left the standing tree", which was
    /// true only because the cut paid for every section it severed - so felling a
    /// nine-section palm at the base handed over the whole tree in one go, which is
    /// precisely the thing retail never did and players complained about.
    ///
    /// The fix is at the other end. A cut now pays for ONE section, the one under the
    /// beam (<see cref="TreeSectionMaskChange.SplinterMask"/>), and everything else it
    /// severed leaves in the log (<see cref="TreeSectionMaskChange.LogMask"/>) still
    /// owing its timber. The log is planted in <c>TreeHarvest</c> as a felled stand,
    /// so the same beam, the same cadence and the same topology take it apart where it
    /// lies - and a piece split off a log becomes another log, exactly as
    /// <c>TreeSection.Harvest</c> spawned a new tree from a new tree
    /// (acs/TreeSection.cs:81). Every section is paid for exactly once, on the cut
    /// where it is the section under the beam, so the total timber in a tree is
    /// unchanged; only the number of cuts it takes to get it went up.
    ///
    /// Pure: no ENet, no Improbable types, no game install, no clock.
    /// </summary>
    public static class TreeFall
    {
        /// <summary>
        /// How long the topple itself takes. RECONSTRUCTED, not recovered: retail's
        /// fall was a rigid body under Unity gravity and had no authored duration to
        /// find. 1.6 s is what a tree-sized body takes to swing ninety degrees about
        /// its base under gravity to within the tolerance anybody can see, and it is
        /// long enough to read as a fall rather than as a snap.
        /// </summary>
        public static readonly TimeSpan DefaultFallDuration = TimeSpan.FromSeconds(1.6);

        /// <summary>
        /// How long the log lies on the ground before it is removed, measured from
        /// the LAST thing that happened to it.
        ///
        /// TWO MINUTES, WHERE THIS ONCE SAID TWELVE SECONDS, and the change is a
        /// consequence of the log becoming choppable. Twelve seconds was sized for a
        /// prop: long enough to notice, short enough not to litter. A log you have to
        /// walk over to and take apart at one section per
        /// <c>TreeHarvest.DefaultCutInterval</c> needs to outlive the walk and the
        /// chopping, or a player watches their timber evaporate mid-swing.
        ///
        /// It is a QUIET wait, which is what makes the length affordable: a landed
        /// log has been silent since its last pose (see <see cref="LandedRepeats"/>),
        /// so a clearing full of them costs no bandwidth at all - only entity slots,
        /// which <see cref="DefaultMaxConcurrent"/> bounds. And the clock is reset by
        /// <see cref="FallingLogs.Touch"/> on every cut, so a log being actively
        /// worked never expires under the player; only an abandoned one does.
        /// </summary>
        public static readonly TimeSpan DefaultLingerDuration = TimeSpan.FromSeconds(120);

        /// <summary>
        /// How often one falling log's 190602 is pushed while it is moving. 20 Hz,
        /// the same cadence <see cref="RelayCadence"/> holds every other moving
        /// thing on this server to, and for the same reason: the client
        /// INTERPOLATES between transform updates
        /// (<c>FixedUpdateLerpLocalTransformBehaviour</c>), so a higher rate buys no
        /// smoothness and costs the bandwidth that caused this project's desync
        /// spiral.
        ///
        /// Measured in SECONDS off an injected clock, never in main-loop turns: the
        /// loop turns once per ENet EVENT, so counting turns would push hundreds of
        /// updates a second on a busy server. That mistake has already cost this
        /// project one debugging round - see <see cref="TreeHarvest.DefaultCutInterval"/>.
        /// </summary>
        public static readonly TimeSpan PoseInterval =
            RelayCadencePolicy.IntervalFor(RelayCadencePolicy.DefaultHz);

        /// <summary>
        /// How many EXTRA copies of the final, flat pose go out after the log has
        /// landed.
        ///
        /// BELT AND BRACES AGAINST A DROPPED PACKET, and not optional. 190602 is in
        /// <c>MirrorSendPolicy</c>'s unreliable set - correctly, because a
        /// superseding pose stream must never build a reliable backlog - so the one
        /// update that says "the log is down" can simply be lost. If it is, and it
        /// was the last thing ever sent for that entity, the log hangs in the air at
        /// whatever angle the previous update left it until it is removed.
        ///
        /// Four is what <c>ShipFerryService.RestRepeats</c> settled on for the same
        /// hazard on the same channel, and it costs four packets per felled section.
        /// </summary>
        public const int LandedRepeats = 4;

        /// <summary>
        /// <c>TreeFSimState.dynamic</c> for a LOG, as opposed to
        /// <see cref="Trees.Dynamic"/> for a standing tree.
        ///
        /// TRUE, and the two must disagree. <c>Trees.Dynamic</c>'s remarks call
        /// <c>dynamic = true</c> a trap, and on a standing tree it is one: the
        /// setter (acs/TreeBase.cs:95-110) starts <c>TreeAmbienceSfx</c>'s
        /// falling-tree audio loop on the true edge, which on a tree that is not
        /// falling is a permanent creaking noise for no reason.
        ///
        /// On a log it is not a trap, it is the point. The audio is a falling tree
        /// because the thing IS a falling tree, and the flag additionally
        /// <list type="bullet">
        /// <item>leaves <c>RelativeParentTransformChildHierarchyBehaviour</c>
        ///   ENABLED (acs/TreeBase.cs:191-198 disables it only when NOT dynamic), so
        ///   the entity follows the transforms this server sends it - a static tree
        ///   ignores them by construction;</item>
        /// <item>does NOT hand the client physics: <c>ResetCOMHackCoroutine</c>'s
        ///   <c>if (dynamic &amp;&amp; !WorldsAdrift.IsClient)</c> (acs/TreeBase.cs:548-560)
        ///   means a client leaves the rigidbody kinematic and replays what it is
        ///   told, which is exactly the behaviour this server can drive.</item>
        /// </list>
        /// </summary>
        public const bool LogIsDynamic = true;

        /// <summary>
        /// The first entity id a felled log may use.
        ///
        /// A DISJOINT BAND, deliberately far above anything the world registry can
        /// reach. <see cref="EntityIdAllocator"/> hands out ids from 1 upwards and a
        /// world has a few thousand entities, so two billion cannot collide with a
        /// registered one within the life of any process.
        ///
        /// The band exists because a log is NOT a world registration and must not
        /// become one. Registering it would put it in the connect-time spawn plan
        /// (a joiner would be sent a log that is about to be removed), in the
        /// loading barrier's count, and in the domain host's expected-owned list -
        /// three places that assume a registration is permanent. Keeping logs out of
        /// the registry entirely means every one of those paths is untouched by this
        /// feature, at the price of the serializer having to resolve a log's pose
        /// from <see cref="FallingLogs"/> instead. That trade is the right way round:
        /// three narrow reads beat three silent leaks.
        /// </summary>
        public const long FirstLogEntityId = 2_000_000_000L;

        /// <summary>
        /// How many logs may be in the air at once, across the whole world.
        ///
        /// A BUDGET, not a guess. Each log is a live entity being pushed a 190602 at
        /// <see cref="PoseInterval"/> to every peer that can see it, and a player
        /// sweeping a beam along a treeline fells a section every
        /// <see cref="TreeHarvest.DefaultCutInterval"/>. The standing rule in
        /// docs/multiplayer.md is that no new feature may add an unbounded relayed
        /// sender. Over the cap the section simply vanishes as it did before -
        /// degraded, never dropped work.
        ///
        /// TWENTY-FOUR, WHERE THIS ONCE SAID EIGHT, and raising it does NOT raise the
        /// wire cost, which is the only reason it is allowed. Two things changed
        /// under it:
        /// <list type="number">
        /// <item>A log now lives for <see cref="DefaultLingerDuration"/> rather than
        ///   twelve seconds, because it has to survive being chopped, so more are
        ///   alive at once for the same amount of chopping. But a landed log is
        ///   SILENT - it has sent nothing since its last pose - so those extra slots
        ///   cost entity ids, not bandwidth.</item>
        /// <item>A piece broken off a log that is ALREADY DOWN is dropped settled
        ///   (see <c>alreadyDown</c> on <see cref="FallingLogs.Drop"/>) and streams no
        ///   arc at all. Only the first fall off a STANDING tree streams.</item>
        /// </list>
        /// So the sender cost is bounded by how many logs are simultaneously
        /// TOPPLING, which one chopper holds at about <see cref="DefaultFallDuration"/>
        /// over <see cref="TreeHarvest.DefaultCutInterval"/> - between two and three -
        /// rather than by this number. Twenty-four is sized so that a player taking a
        /// nine-section palm apart never loses a piece to the budget.
        /// </summary>
        public const int DefaultMaxConcurrent = 24;

        /// <summary>
        /// Whether felled logs are switched on, from the operator's
        /// <c>WAREBORN_TREE_FALL</c> string.
        ///
        /// ON unless the operator writes exactly "0". The repo convention for a new
        /// periodic sender is off-by-default, and this deliberately departs from it:
        /// a tree that vanishes when cut is a REGRESSION against retail that the
        /// falling log exists to fix, and a fix nobody enables is not a fix. What
        /// makes the departure affordable is that this sender is bounded by
        /// construction (<see cref="DefaultMaxConcurrent"/> logs, each silent the
        /// moment it settles) rather than by an operator remembering a flag. "0" is
        /// the kill switch, and it must restore the previous behaviour EXACTLY - a
        /// section simply leaves the mask, as it did before.
        /// </summary>
        public static bool FallEnabled(string? raw)
        {
            return raw == null || raw.Trim() != "0";
        }

        /// <summary>
        /// A log budget from the operator's <c>WAREBORN_TREE_FALL_MAX</c> string, or
        /// null to accept <see cref="DefaultMaxConcurrent"/>.
        ///
        /// Zero is ACCEPTED and means "no logs" - a second way to switch the feature
        /// off, and the one an operator reaches for when they want to leave the flag
        /// alone. Negative and unparseable values fall back rather than throwing: a
        /// typo in an environment variable must never stop a server booting.
        /// </summary>
        public static int? ParseBudget(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)
                || !int.TryParse(raw.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int budget)
                || budget < 0)
            {
                return null;
            }
            return budget;
        }

        /// <summary>
        /// The angle a log has swung through, in degrees, <paramref name="elapsed"/>
        /// into a fall of <paramref name="duration"/>. 0 at the moment of the cut,
        /// 90 once it is down, and clamped to 90 for ever after.
        ///
        /// QUADRATIC, not linear: a body toppling about a pivot under gravity is
        /// accelerating, so it barely moves for the first third and then goes over
        /// fast. A linear sweep reads as a door closing rather than as a tree
        /// falling, and it is the single cheapest thing that makes this look right.
        /// </summary>
        public static double FallAngleDegrees(TimeSpan elapsed, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return 90.0;
            }
            if (elapsed <= TimeSpan.Zero)
            {
                return 0.0;
            }
            if (elapsed >= duration)
            {
                return 90.0;
            }

            double t = elapsed.TotalSeconds / duration.TotalSeconds;
            return 90.0 * t * t;
        }

        /// <summary>
        /// WHICH WAY the log goes over, as a compass bearing in degrees.
        ///
        /// Retail let physics decide, seeded from the parent's linear and angular
        /// velocity (<c>TreeFsimVisualizer.SpawnNewTree</c>); a rooted tree's
        /// velocity is zero, so on retail the direction came out of the collision
        /// solver and nothing else. There is no solver here, so the direction is
        /// DERIVED rather than random - a fixed hash of the tree and the section -
        /// for two reasons that both matter:
        ///
        /// 1. Every player must see the same log go the same way. A random direction
        ///    picked per-recipient would have two people watching the same tree fall
        ///    in different directions.
        /// 2. A server replayed against the same cuts must produce the same world.
        ///    Nothing in this assembly is allowed to be non-deterministic.
        ///
        /// Successive cuts on the same tree get different bearings because the
        /// section id is in the hash, so a tree does not shed every limb onto the
        /// same spot.
        /// </summary>
        public static double FallHeadingDegrees(long treeEntityId, int sectionId)
        {
            // FNV-1a over the two identifying numbers. Any stable mixing function
            // would do; this one is written out so the value cannot drift with a
            // framework version the way GetHashCode() legally can.
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;
            ulong bits = unchecked((ulong)treeEntityId);
            for (int i = 0; i < 8; i++)
            {
                hash = (hash ^ ((bits >> (i * 8)) & 0xFF)) * prime;
            }
            uint section = unchecked((uint)sectionId);
            for (int i = 0; i < 4; i++)
            {
                hash = (hash ^ ((section >> (i * 8)) & 0xFF)) * prime;
            }

            return hash % 360UL;
        }

        /// <summary>
        /// WHETHER ONE PEER IS SHOWN A FELLED LOG.
        ///
        /// THIS IS THE PREDICATE THAT SILENTLY BROKE THE WHOLE FEATURE, and it is a
        /// pure function now for exactly that reason. The first version asked one
        /// question - "did we send this peer an AddEntity for the parent tree?", read
        /// off <c>EntitySendLedger</c> - and on the live server the answer was NO for
        /// every tree in the release world. Release-world trees are streamed in by
        /// <c>ResourceInterestService</c>, which keeps its OWN per-peer loaded set and
        /// never writes the global send ledger. So every cut built a log, ticked it
        /// down its arc and retired it, having shown it to nobody: the server-side
        /// evidence read <c>shown to 0 peer(s)</c> on the same line as
        /// <c>pushed sectionMask ... to 1 checked-out peer(s)</c>. The tree lost its
        /// sections on screen and nothing fell, which is the vanishing bug exactly.
        ///
        /// So the question to ask is not "did we announce the tree through one
        /// particular door" but "does this peer HOLD the tree", and the two ledgers
        /// that can answer are OR-ed rather than picked between:
        /// <list type="bullet">
        /// <item><paramref name="addEntitySent"/> - the send ledger, which is the
        ///   truth for a tree in the connect-time spawn plan.</item>
        /// <item><paramref name="holdsTreeComponents"/> - the peer has components of
        ///   the tree checked out, which is the truth for a STREAMED tree and is the
        ///   very same evidence the mask push uses to decide who is told the tree
        ///   shrank. Tying the log to it means the log and the mask can never
        ///   disagree about who is watching.</item>
        /// </list>
        ///
        /// <paramref name="canReceiveRemove"/> stays a veto rather than joining the
        /// OR, because it is about a different thing: a peer with no
        /// <c>REMOVE_ENTITY_OP</c> channel could be shown the log perfectly well and
        /// then never be able to have it taken away. Permanent litter is worse than a
        /// missing log, so such a peer is simply never shown one.
        /// </summary>
        public static bool MayShowLog(bool addEntitySent, bool holdsTreeComponents, bool canReceiveRemove)
        {
            return canReceiveRemove && (addEntitySent || holdsTreeComponents);
        }

        /// <summary>
        /// The log's rotation <paramref name="elapsed"/> into its fall, in the
        /// game's packed <c>Quaternion32</c> wire form - what goes on the wire as
        /// 190602 <c>localRotation</c>.
        ///
        /// COMPOSED ON THE PARENT'S ROTATION, not replacing it: the log starts life
        /// as a copy of the standing tree and must therefore start at the standing
        /// tree's exact facing, or it would visibly SNAP to upright before it began
        /// to fall. <see cref="HelmMountLock.Compose"/> is "apply b, then a", so the
        /// world-frame topple goes on the left and the tree's own rotation on the
        /// right.
        ///
        /// The topple is a rotation about a HORIZONTAL axis perpendicular to
        /// <paramref name="headingDegrees"/>, so the tree goes over towards that
        /// bearing rather than twisting on the spot.
        /// </summary>
        public static uint PackedRotationAt(uint parentPackedRotation, double headingDegrees,
            TimeSpan elapsed, TimeSpan duration)
        {
            double angle = FallAngleDegrees(elapsed, duration);
            if (angle <= 0.0)
            {
                return parentPackedRotation;
            }

            double heading = headingDegrees * Math.PI / 180.0;
            double half = angle * Math.PI / 360.0;
            double sin = Math.Sin(half);

            // Axis perpendicular to the heading, in the ground plane: tipping about
            // it drops the crown towards (sin heading, 0, cos heading).
            (float W, float X, float Y, float Z) topple = (
                (float)Math.Cos(half),
                (float)(sin * Math.Cos(heading)),
                0f,
                (float)(sin * -Math.Sin(heading)));

            (float w, float x, float y, float z) = Quaternion32Packing.Decode(parentPackedRotation);
            (float cw, float cx, float cy, float cz) = HelmMountLock.Compose(topple, (w, x, y, z));

            return Quaternion32Packing.Encode(cw, cx, cy, cz);
        }
    }

    /// <summary>
    /// One log's pose at one instant: everything a 190602 push needs, and nothing
    /// else. Position is carried even though an authored topple about the base does
    /// not move the entity origin, because a caller that sent only a rotation would
    /// have to remember that fact.
    /// </summary>
    public readonly struct FallingLogPose
    {
        public FallingLogPose(long logEntityId, FixedPointPosition position, uint packedRotation, bool landed)
        {
            LogEntityId = logEntityId;
            Position = position;
            PackedRotation = packedRotation;
            Landed = landed;
        }

        /// <summary>The log entity this pose addresses.</summary>
        public long LogEntityId { get; }

        /// <summary>Where the log is. Constant for the life of the log; see the type remarks.</summary>
        public FixedPointPosition Position { get; }

        /// <summary>The 190602 <c>localRotation</c> to send, packed.</summary>
        public uint PackedRotation { get; }

        /// <summary>
        /// Whether this is the pose that finishes the fall. The LAST pose of a fall
        /// is always flagged, and it is always the full ninety degrees, so a client
        /// cannot be left holding a log frozen at eighty-nine because a tick landed
        /// slightly early.
        /// </summary>
        public bool Landed { get; }

        public override string ToString()
        {
            return "log " + LogEntityId + " rot=" + PackedRotation + (Landed ? " (down)" : " (falling)");
        }
    }

    /// <summary>
    /// One felled log, as it is dropped: the whole registration a spawner needs.
    /// </summary>
    public readonly struct FelledLog
    {
        public FelledLog(long logEntityId, long treeEntityId, string assetName, string assetContext,
            FixedPointPosition position, uint packedRotation, int sectionMask, int sectionCount,
            string woodType, double headingDegrees)
        {
            LogEntityId = logEntityId;
            TreeEntityId = treeEntityId;
            AssetName = assetName;
            AssetContext = assetContext;
            Position = position;
            PackedRotation = packedRotation;
            SectionMask = sectionMask;
            SectionCount = sectionCount;
            WoodType = woodType;
            HeadingDegrees = headingDegrees;
        }

        /// <summary>The new entity that IS the log.</summary>
        public long LogEntityId { get; }

        /// <summary>The tree it came off. The log is served to exactly this tree's viewers.</summary>
        public long TreeEntityId { get; }

        /// <summary>
        /// The prefab, taken from the PARENT'S registration rather than from
        /// <see cref="Trees.AssetName"/>. A palm must shed a palm.
        /// </summary>
        public string AssetName { get; }

        /// <summary>The parent's asset context, for the same reason.</summary>
        public string AssetContext { get; }

        /// <summary>The parent tree's position - the log starts exactly where the crown was.</summary>
        public FixedPointPosition Position { get; }

        /// <summary>The parent tree's rotation, packed. The log's rotation SEED, before any fall.</summary>
        public uint PackedRotation { get; }

        /// <summary>
        /// The sections that came away - the mask the log renders, and the exact
        /// value <c>TreeSection.Harvest</c> passes to <c>SpawnNewTree</c>.
        /// </summary>
        public int SectionMask { get; }

        /// <summary>The parent prefab's section count, so the log seeds a structurally complete 1036.</summary>
        public int SectionCount { get; }

        /// <summary>The parent's authored wood. Seeded for completeness; the wood was already granted.</summary>
        public string WoodType { get; }

        /// <summary>The bearing this log goes over towards. See <see cref="TreeFall.FallHeadingDegrees"/>.</summary>
        public double HeadingDegrees { get; }

        public override string ToString()
        {
            return "log " + LogEntityId + " off tree " + TreeEntityId
                + " mask=" + Convert.ToString(SectionMask, 2)
                + " heading=" + HeadingDegrees.ToString("0") + " deg";
        }
    }

    /// <summary>
    /// Every log currently in the air or lying on the ground, and WHEN each one
    /// needs a transform push or a removal.
    ///
    /// The clock-driven half of <see cref="TreeFall"/>, shaped exactly like
    /// <see cref="TreeHarvest"/> and for the same reasons: the cadence is in
    /// SECONDS off an injected clock rather than in main-loop turns, and it is not
    /// thread-safe because the server is a single poll loop.
    ///
    /// IT IS A BUDGET AS WELL AS A REGISTRY. <see cref="Drop"/> refuses once
    /// <see cref="MaxConcurrent"/> logs are live, so a player working a treeline
    /// cannot make this server send an unbounded number of transform updates. A
    /// refusal is not an error: the section vanishes the way it did before falling
    /// logs existed, which is a worse-looking cut and nothing more.
    /// </summary>
    public sealed class FallingLogs
    {
        private sealed class Log
        {
            public long TreeEntityId;
            public string AssetName = string.Empty;
            public string AssetContext = string.Empty;
            public FixedPointPosition Position;
            public uint ParentRotation;
            public double Heading;
            public int SectionMask;
            public int SectionCount;
            public string WoodType = string.Empty;
            public TimeSpan DroppedAt;
            public TimeSpan NextPoseAt;

            /// <summary>
            /// When the linger starts counting from. Reset by <see cref="Touch"/>
            /// every time somebody cuts this log, so a trunk being actively taken
            /// apart never expires under the player mid-swing.
            /// </summary>
            public TimeSpan LastTouchedAt;

            /// <summary>
            /// How long this particular log's topple takes. A log shed by a STANDING
            /// tree gets the real arc; a piece broken off a log that is already lying
            /// down gets nothing to swing through, so this is
            /// <see cref="TimeSpan.Zero"/> for it and the pose stream ends after the
            /// landed repeats. Per-log rather than per-registry because both kinds
            /// coexist in the same clearing.
            /// </summary>
            public TimeSpan FallDuration;

            /// <summary>How many flat poses have gone out. Counts up to 1 + LandedRepeats.</summary>
            public int LandedSends;

            /// <summary>Nothing more will ever be sent for this log.</summary>
            public bool Settled;
        }

        private readonly IClock _clock;
        private readonly TimeSpan _fallDuration;
        private readonly TimeSpan _linger;
        private readonly TimeSpan _poseInterval;
        private readonly int _maxConcurrent;
        private readonly int _landedRepeats;
        private readonly Dictionary<long, Log> _logs = new Dictionary<long, Log>();
        private long _nextEntityId = TreeFall.FirstLogEntityId;

        public FallingLogs(IClock clock, TimeSpan? fallDuration = null, TimeSpan? lingerDuration = null,
            int? maxConcurrent = null, TimeSpan? poseInterval = null, int? landedRepeats = null)
        {
            _landedRepeats = landedRepeats ?? TreeFall.LandedRepeats;
            if (_landedRepeats < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(landedRepeats),
                    "a negative repeat count is not a repeat count");
            }
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _fallDuration = fallDuration ?? TreeFall.DefaultFallDuration;
            _linger = lingerDuration ?? TreeFall.DefaultLingerDuration;
            _poseInterval = poseInterval ?? TreeFall.PoseInterval;
            _maxConcurrent = maxConcurrent ?? TreeFall.DefaultMaxConcurrent;

            if (_fallDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(fallDuration),
                    "a log with no fall duration would be down before it was ever drawn standing");
            }
            if (_linger < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(lingerDuration),
                    "a negative linger would retire a log before it landed");
            }
            if (_poseInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(poseInterval),
                    "a non-positive pose interval would push a transform per main-loop turn");
            }
            if (_maxConcurrent < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrent),
                    "a negative log budget is not a budget");
            }
        }

        /// <summary>How long a topple takes.</summary>
        public TimeSpan FallDuration => _fallDuration;

        /// <summary>How long a landed log lies there before it is removed.</summary>
        public TimeSpan LingerDuration => _linger;

        /// <summary>How many logs may be live at once.</summary>
        public int MaxConcurrent => _maxConcurrent;

        /// <summary>How many are live now.</summary>
        public int Count => _logs.Count;

        /// <summary>Whether another log would fit inside the budget.</summary>
        public bool HasCapacity => _logs.Count < _maxConcurrent;

        /// <summary>
        /// The next log entity id. Monotonic from
        /// <see cref="TreeFall.FirstLogEntityId"/> and NEVER reused, even after the
        /// log it named has been removed - the same rule
        /// <see cref="EntityIdAllocator"/> keeps, and for the same reason: a packet
        /// still in flight for a retired log must never be able to name a new one.
        /// </summary>
        public long NextEntityId() => _nextEntityId++;

        /// <summary>Whether an entity id is a log this registry owns.</summary>
        public bool IsLog(long entityId) => _logs.ContainsKey(entityId);

        /// <summary>
        /// A log's <c>1036 sectionMask</c>, or null if it is not a log.
        ///
        /// THE SERIALIZER MUST CONSULT THIS BEFORE THE HARVEST LEDGER. A log is not
        /// planted in <see cref="TreeHarvest"/> - deliberately, so nobody can chop a
        /// log and be paid for wood that was already granted - and the 1036 branch
        /// falls back to <c>Trees.FullSectionMask</c> for an entity it does not
        /// recognise. Without this lookup every log would check out as a WHOLE tree
        /// standing inside the one it fell off.
        /// </summary>
        public int? MaskOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.SectionMask : null;
        }

        /// <summary>A log's parent prefab section count, or null if it is not a log.</summary>
        public int? SectionCountOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.SectionCount : null;
        }

        /// <summary>A log's authored wood, or null if it is not a log.</summary>
        public string? WoodTypeOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.WoodType : null;
        }

        /// <summary>The tree a log came off, or null if it is not a log.</summary>
        public long? TreeOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.TreeEntityId : null;
        }

        /// <summary>A log's prefab, or null if it is not a log. The 1035 prefabName seed.</summary>
        public string? AssetNameOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.AssetName : null;
        }

        /// <summary>A log's asset context, or null if it is not a log.</summary>
        public string? AssetContextOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.AssetContext : null;
        }

        /// <summary>A log's position, or null if it is not a log. The 190602 localPosition seed.</summary>
        public FixedPointPosition? PositionOf(long logEntityId)
        {
            return _logs.TryGetValue(logEntityId, out Log? log) ? log.Position : (FixedPointPosition?)null;
        }

        /// <summary>
        /// A log's rotation RIGHT NOW, or null if it is not a log - the 190602
        /// localRotation seed.
        ///
        /// Clock-derived rather than the value it was dropped with, because a player
        /// who checks the log out halfway through its fall must be seeded at the
        /// angle it has already reached. Seeded upright, the log would snap flat the
        /// moment the next pose arrived.
        /// </summary>
        public uint? RotationOf(long logEntityId)
        {
            if (!_logs.TryGetValue(logEntityId, out Log? log))
            {
                return null;
            }

            return RotationOf(log, _clock.Elapsed);
        }

        /// <summary>
        /// One log's rotation at one instant.
        ///
        /// A log with no fall duration is a piece that broke off something already
        /// lying on the ground: it was seeded with the pose its parent log had at the
        /// moment it split, and it must KEEP that pose exactly. Running it through
        /// <see cref="TreeFall.PackedRotationAt"/> with a zero duration would report a
        /// full ninety degrees of topple and tip an already-flat trunk over a second
        /// time, so it is short-circuited rather than clamped.
        /// </summary>
        private static uint RotationOf(Log log, TimeSpan now)
        {
            if (log.FallDuration <= TimeSpan.Zero)
            {
                return log.ParentRotation;
            }

            return TreeFall.PackedRotationAt(log.ParentRotation, log.Heading,
                now - log.DroppedAt, log.FallDuration);
        }

        /// <summary>Every live log, so a caller can clean up on shutdown.</summary>
        public IReadOnlyCollection<long> Live => _logs.Keys.ToList();

        /// <summary>
        /// Turns one cut into a log, or refuses.
        ///
        /// Returns null when the budget is full or the cut severed nothing - both of
        /// which a caller must treat as "no log this time", never as a failure.
        /// <paramref name="logEntityId"/> is allocated by the caller because entity
        /// ids are the impure side's business.
        /// </summary>
        public FelledLog? Drop(long logEntityId, TreeSectionMaskChange change,
            string assetName, string assetContext, FixedPointPosition position,
            uint parentRotation, int sectionCount, bool alreadyDown = false)
        {
            // THE LOG CARRIES WHAT FELL, NOT WHAT WAS SEVERED. The section under the
            // beam splintered into the chopper's inventory; putting it in the log too
            // would let the same section be harvested a second time off the ground.
            // See TreeSectionMaskChange.LogMask.
            if (change.LogMask == 0)
            {
                return null;
            }
            if (!HasCapacity)
            {
                return null;
            }
            if (_logs.ContainsKey(logEntityId))
            {
                return null;
            }

            double heading = TreeFall.FallHeadingDegrees(change.TreeEntityId, change.SectionId);
            TimeSpan now = _clock.Elapsed;

            // A PIECE BROKEN OFF SOMETHING ALREADY ON THE GROUND DOES NOT TOPPLE.
            // Only the first fall - off a rooted tree - has ninety degrees to swing
            // through. A sub-piece is seeded at its parent log's CURRENT pose and is
            // down from the instant it exists, which is both what it should look like
            // and the reason raising the log budget costs no bandwidth: a settled
            // drop streams no arc, just the landed repeats that guarantee its one
            // pose survives a dropped packet.
            TimeSpan fallDuration = alreadyDown ? TimeSpan.Zero : _fallDuration;

            _logs.Add(logEntityId, new Log
            {
                TreeEntityId = change.TreeEntityId,
                AssetName = assetName ?? string.Empty,
                AssetContext = assetContext ?? string.Empty,
                Position = position,
                ParentRotation = parentRotation,
                Heading = heading,
                SectionMask = change.LogMask,
                SectionCount = sectionCount,
                WoodType = change.WoodType ?? string.Empty,
                DroppedAt = now,
                LastTouchedAt = now,
                FallDuration = fallDuration,
                // The first pose is due immediately: it is the log standing exactly
                // where the crown was, and it must be on the wire before the crown's
                // mask push removes the crown.
                NextPoseAt = now,
                Settled = false,
            });

            return new FelledLog(logEntityId, change.TreeEntityId,
                assetName ?? string.Empty, assetContext ?? string.Empty, position, parentRotation,
                change.LogMask, sectionCount, change.WoodType ?? string.Empty, heading);
        }

        /// <summary>
        /// Somebody just cut this log: restart its linger and record its new mask.
        /// Returns false if it is not a log.
        ///
        /// THE LINGER RESET IS THE POINT. A log now has to survive being taken apart
        /// at one section per <c>TreeHarvest.DefaultCutInterval</c>, and a fixed
        /// countdown from the moment it landed would delete a half-chopped trunk out
        /// from under the player. Counting from the LAST cut instead means an
        /// abandoned log still expires on schedule and a worked one never does - the
        /// same shape as <c>TreeHarvest</c>'s regrowth timer, and for the same reason.
        ///
        /// <paramref name="sectionMask"/> keeps this registry's copy in step with the
        /// harvest ledger so nothing downstream can serve a stale mask.
        /// </summary>
        public bool Touch(long logEntityId, int sectionMask)
        {
            if (!_logs.TryGetValue(logEntityId, out Log? log))
            {
                return false;
            }

            log.LastTouchedAt = _clock.Elapsed;
            log.SectionMask = sectionMask;
            return true;
        }

        /// <summary>
        /// Drops one log without waiting for its linger - for a log whose last
        /// section has just been chopped off, which has nothing left to draw.
        /// Returns whether it was there. The caller still owes it a RemoveEntity;
        /// this only forgets it here.
        /// </summary>
        public bool Forget(long logEntityId) => _logs.Remove(logEntityId);

        /// <summary>
        /// Every log whose next pose is due, advanced. Call once per main-loop turn;
        /// it allocates nothing when no log is falling.
        ///
        /// A SETTLED LOG IS SILENT. Once the flat pose has gone out
        /// <see cref="TreeFall.LandedRepeats"/> extra times the log is never pushed
        /// again - it just lies there until <see cref="DueRemovals"/> retires it -
        /// so the steady-state cost of a clearing full of logs is zero, not
        /// <see cref="TreeFall.PoseInterval"/> forever.
        /// </summary>
        public IReadOnlyList<FallingLogPose> DuePoses()
        {
            if (_logs.Count == 0)
            {
                return Array.Empty<FallingLogPose>();
            }

            TimeSpan now = _clock.Elapsed;
            List<FallingLogPose>? poses = null;

            foreach (KeyValuePair<long, Log> entry in _logs)
            {
                Log log = entry.Value;
                if (log.Settled || now < log.NextPoseAt)
                {
                    continue;
                }

                TimeSpan elapsed = now - log.DroppedAt;
                bool landed = elapsed >= log.FallDuration;

                // Clamping to the fall duration rather than passing `elapsed`
                // straight through is what guarantees the last pose is exactly
                // ninety degrees. A tick that arrives late would otherwise never
                // emit the final angle at all.
                TimeSpan sample = landed ? log.FallDuration : elapsed;

                uint rotation = log.FallDuration <= TimeSpan.Zero
                    ? log.ParentRotation
                    : TreeFall.PackedRotationAt(log.ParentRotation, log.Heading, sample, log.FallDuration);

                log.NextPoseAt = now + _poseInterval;
                if (landed)
                {
                    log.LandedSends++;
                    log.Settled = log.LandedSends > _landedRepeats;
                }

                (poses ??= new List<FallingLogPose>()).Add(
                    new FallingLogPose(entry.Key, log.Position, rotation, landed));
            }

            return poses ?? (IReadOnlyList<FallingLogPose>)Array.Empty<FallingLogPose>();
        }

        /// <summary>
        /// Every log whose time is up, forgotten and reported so the caller can send
        /// its RemoveEntity. Call once per main-loop turn alongside
        /// <see cref="DuePoses"/>.
        ///
        /// A log is retired <see cref="LingerDuration"/> after it LANDS, not after
        /// it was dropped, so lengthening the fall never shortens the time the trunk
        /// is visible on the ground.
        ///
        /// The log leaves this registry as it is reported. A caller that fails to
        /// send the removal leaves a log on screen for ever, which is why the caller
        /// sends first and reports second.
        /// </summary>
        public IReadOnlyList<long> DueRemovals()
        {
            if (_logs.Count == 0)
            {
                return Array.Empty<long>();
            }

            TimeSpan now = _clock.Elapsed;
            List<long>? expired = null;

            foreach (KeyValuePair<long, Log> entry in _logs)
            {
                // Measured from the LAST cut (Touch), or from the drop if nobody has
                // touched it, so a log being taken apart is never retired mid-chop.
                if (now - entry.Value.LastTouchedAt < entry.Value.FallDuration + _linger)
                {
                    continue;
                }
                (expired ??= new List<long>()).Add(entry.Key);
            }

            if (expired == null)
            {
                return Array.Empty<long>();
            }

            foreach (long id in expired)
            {
                _logs.Remove(id);
            }
            return expired;
        }

        /// <summary>
        /// Drops every live log without reporting it. For a caller that is tearing
        /// the world down and will not be sending any more removals.
        /// </summary>
        public void Clear() => _logs.Clear();
    }
}
