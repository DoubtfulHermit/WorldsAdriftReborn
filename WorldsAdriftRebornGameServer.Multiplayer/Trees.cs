namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The facts about the one tree species this server plants, recovered rather
    /// than invented, so the numbers can be diffed against their sources by eye.
    ///
    /// EVERYTHING HERE IS BOSSA'S except the two values called out as invented.
    /// That matters because a tree is one of the very few things in this project
    /// where authored content survived in a readable form, and inventing over the
    /// top of it would be a choice nobody could later tell from a recovery.
    /// </summary>
    public static class Trees
    {
        /// <summary>
        /// The prefab name that goes on the wire, BARE.
        ///
        /// "Tree" and not "tree_unityclient": the client appends the worker suffix
        /// itself in <c>WorkerSpecificPrefabName.GetWorkerSpecificPrefabName</c>,
        /// so a suffixed name would be suffixed twice and resolve to nothing.
        ///
        /// <c>docs/research/gathering/findings-node-spawning.md</c> says the
        /// opposite - that there is no `Tree` prefab and that all 72 tree entries
        /// are species-specific. It is WRONG, and demonstrably so:
        /// <c>entityprefabs/tree_unityclient</c> is line 289 of the very container
        /// listing it cites. See findings-harvestable-world.md, "CORRECTIONS TO
        /// EXISTING DOCUMENTS".
        /// </summary>
        public const string AssetName = "Tree";

        /// <summary>
        /// Bossa's authored species for the `Tree` prefab.
        ///
        /// Recovered, not chosen: <c>TreePreprocessor.woodType</c> is copied onto
        /// <c>TreeFsimVisualizer.woodType</c> at export, so it survives on the
        /// shipped <c>_unityworker</c> prefabs. All 65 tree prefabs were parsed
        /// (<c>docs/research/loop/data/tree_woodtypes.json</c>), 65/65 landed on one
        /// of the eight known woods, and <c>Tree_unityworker</c> is birch.
        ///
        /// The client never learns this - <c>TreeFSimState.woodType</c> is written
        /// only by the UnityWorker-only visualizer - so it exists purely to tell
        /// the eventual inventory grant what to grant.
        /// </summary>
        public const string WoodType = "birch";

        /// <summary>
        /// `Tree` has twelve sections, named <c>trunk_section1</c>..<c>trunk_section12</c>
        /// on the prefab and numbered 0..11 in <c>TreeBase.treeSections</c>.
        /// </summary>
        public const int SectionCount = 12;

        /// <summary>
        /// The recovered <c>TreeBase.branches</c> of `Tree`, read out of the
        /// serialized prefab in <c>UnityClient@Windows_Data/resources.assets</c>
        /// (resources.assets ships no MonoBehaviour typetrees, so the layout was
        /// parsed by hand; see docs/research/loop/data/tree_topology.py).
        ///
        /// Shape: a nine-section trunk 0 -> 1 -> 2 -> 3 -> 4 -> 6 -> 8 -> 9 -> 10
        /// with three single-section limbs - 5 off 4, 7 off 6, 11 off 9. Section 0
        /// is the stump: it is the only section that never appears as a CHILD in
        /// any branch, the only one with no <c>cutPoint</c> Transform, and the only
        /// one flagged non-harvestable. Those are three views of one fact, and
        /// together they are why the tree cannot be felled at the base.
        ///
        /// Confidence that this is recovery and not a lucky decode: the parse ends
        /// exactly on the object boundary (0 trailing bytes), the three fixed
        /// <c>[ExposedMethod]</c> strings decode as "Auto Fill Sections" /
        /// "Deparent All" / "Debug Initialize Tree" at the offsets the layout
        /// predicts, every section PPtr resolves to a MonoBehaviour whose script is
        /// <c>TreeSection</c> with <c>id</c> equal to its index, and the same parser
        /// run over all 130 TreeBases in the game gets 130/130 on all of those
        /// checks. <c>Tree_unityworker</c>, a separately serialized object,
        /// reports the identical topology.
        /// </summary>
        public static readonly IReadOnlyList<TreeBranch> Branches = new[]
        {
            new TreeBranch(0, 1, 2, 3, 4, 6, 8, 9, 10),
            new TreeBranch(4, 5),
            new TreeBranch(6, 7),
            new TreeBranch(9, 11),
        };

        /// <summary>
        /// Per-section <c>TreeSection.harvestable</c>, indexed by section id.
        /// Section 0 - the stump - is the only false one, on both the
        /// <c>_unityclient</c> and <c>_unityworker</c> prefabs.
        /// </summary>
        public static readonly IReadOnlyList<bool> Harvestable = new[]
        {
            false, true, true, true, true, true, true, true, true, true, true, true,
        };

        /// <summary>The `Tree` prefab's topology, ready to cut.</summary>
        public static TreeTopology Topology()
        {
            return new TreeTopology(SectionCount, Branches, Harvestable);
        }

        /// <summary>
        /// Every section standing. 4095, and the prefab's own authored
        /// <c>sectionMask</c> is 4095, which is the cross-check: this value is not
        /// derived from <see cref="SectionCount"/> alone by coincidence.
        /// </summary>
        public const int FullSectionMask = (1 << SectionCount) - 1;

        /// <summary>
        /// Seeded into <c>1036 TreeFSimState.massPerSection</c>. Invented; the
        /// client uses it only to set a Rigidbody mass on a tree that has no
        /// physics authority here, so it is visible nowhere.
        /// </summary>
        public const float MassPerSection = 1f;

        /// <summary>
        /// Seeded into <c>1036 TreeFSimState.resourcePerSection</c>. INVENTED, and
        /// unrecoverable: <c>resourcePerSection</c> has zero readers anywhere in
        /// the client, so no shipped value could be inferred from behaviour and
        /// none survives on the prefabs. One unit per section is the least
        /// surprising thing to have made up.
        /// </summary>
        public const int ResourcePerSection = 1;

        /// <summary>
        /// Seeded into <c>1036 TreeFSimState.sectionHealth</c>, one entry per
        /// section. Nothing in the client reads it - the damage model is one hit
        /// per section, hard-coded, at <c>acs/TreeSection.cs:73-74</c> - so the
        /// list exists to make the component structurally complete and for no other
        /// reason. The authored <c>TreeSection.connectionStrength</c> is 3 on all
        /// twelve sections, so 3 is at least the number Bossa wrote down.
        /// </summary>
        public const int SectionHealth = 3;

        /// <summary>
        /// FALSE, and this is a trap rather than a preference.
        ///
        /// <c>TreeBase.Dynamic</c>'s SETTER (acs/TreeBase.cs:95-110) calls
        /// <c>TreeAmbienceSfx.TryActivateFallingAudio()</c> on the true edge - a
        /// falling-tree audio loop on a tree that is not falling, because nothing
        /// on this server gives a tree physics authority. It also un-kinematics the
        /// Rigidbody via <c>HasAuthority</c>. A static tree is `dynamic = false`.
        ///
        /// ON RETAIL'S FALLING LOGS, since this is where that question lands.
        /// Retail felled sections really did topple and really could crush a player
        /// (worldsadrift.fandom.com/wiki/Trees). The mechanism is
        /// <c>TreeFsimVisualizer.SpawnNewTree</c> -> <c>TriggerSpawnNewTreeBit</c>
        /// (acs/TreeFsimVisualizer.cs:71-82): the severed part becomes ANOTHER tree
        /// entity carrying the falling mask plus the parent's linear and angular
        /// velocity, and the UnityWorker simulates it.
        ///
        /// THAT LOG IS BUILT NOW. See <see cref="TreeFall"/> and
        /// <c>Game.Gathering.FallingLogService</c>. What this field says is still
        /// true and still a trap - a STANDING tree must be <c>false</c> - but the
        /// three facts this comment used to close with need correcting, because one
        /// of them has expired:
        ///
        /// 1. STILL TRUE. The SIMULATION cannot be reproduced.
        ///    <c>TreeFsimVisualizer</c> is <c>[WorkerType(UnityWorker)]</c> and
        ///    absent from the client build, and <c>TreeBase.ResetCOMHackCoroutine</c>
        ///    keeps a dynamic tree KINEMATIC on the client
        ///    (<c>if (dynamic &amp;&amp; !WorldsAdrift.IsClient)</c>, acs/TreeBase.cs:555).
        ///    No client will ever tumble a log by itself - but nor did a retail
        ///    client, which is the point (2) turns on.
        /// 2. STILL TRUE, and now acted on. An ANIMATED fall is renderable. 190602
        ///    TransformState carries localPosition AND localRotation, and served
        ///    transform updates demonstrably move a world entity on the stock
        ///    client. Because of (1) that is not an approximation of what a retail
        ///    client saw, it is the same code path: the log is spawned as a second
        ///    tree entity holding the falling mask, and driven down an authored arc.
        /// 3. EXPIRED. "This server has no entity removal at all" was true when it
        ///    was written and is not any more: native channel 5 carries RemoveEntity
        ///    (docs/HANDOVER.md), so a log can be retired on a timer rather than
        ///    accumulating as permanent litter. Only the second half still holds -
        ///    "dangerous" remains impossible, because HealthState is seeded static
        ///    with no damage authority anywhere, so nothing can crush anybody.
        ///
        /// The standing tree's own behaviour is unchanged: the section leaves the
        /// mask, the client plays the authored break VFX and SFX on it
        /// (<c>TreeClientVisualizer</c> -> <c>TreeSection.ShowReplicatedVisualHitAndPlaySfx</c>),
        /// and the wood is granted to the cutter. The log is what now happens
        /// alongside that instead of the crown simply blinking out.
        /// </summary>
        public const bool Dynamic = false;

        /// <summary>
        /// Seeded into <c>1035 TreeState.scale</c>, and it MUST be (1,1,1).
        ///
        /// <c>TreeScaleVisualiser.OnEnable</c> (acs/Bossa.Travellers.Visualisers.Trees)
        /// is two lines: <c>transform.localScale = treeState.Scale.ToUnityVector()</c>,
        /// verbatim, with no guard. <c>Vector3d</c>'s default is (0,0,0), so
        /// forgetting this field yields a tree scaled to nothing: INVISIBLE, with
        /// working colliders, logging nothing at all. It would look exactly like a
        /// failed asset load, and it is the single most expensive thing on this
        /// path to get wrong.
        /// </summary>
        public const double Scale = 1.0;

        /// <summary>
        /// Seeded into <c>1035 TreeState.respawnTime</c>. Left at zero, and on the
        /// WIRE that still means nothing: <c>respawn_time</c> has zero references in
        /// the entire client decompile, not merely no readers - even its units are
        /// unknown, so no value here would change what any client does.
        ///
        /// Respawn is therefore not driven through this field - it CANNOT be. It is
        /// driven server-side by resetting a chopped tree's <c>sectionMask</c> back
        /// to whole, which is the one channel the client actually acts on
        /// (<c>TreeVisualizer</c> reactivates the sections off the 1036 mask). The
        /// real, tunable cadence lives with the timer that owns it,
        /// <see cref="TreeHarvest.DefaultRespawnDelay"/> - reconstructed there,
        /// because retail's value is unrecoverable. Trees DO respawn on this server
        /// now; this constant is only the inert wire seed the component still needs
        /// to be structurally complete.
        /// </summary>
        public const long RespawnTime = 0;

        /// <summary>
        /// Seeded into <c>1016 ItemHealthState</c>, as both health and maxHealth.
        ///
        /// EQUAL, and both non-zero, for two separate reasons in
        /// <c>SalvageableItemVisualiser</c>:
        /// health == 0 makes <c>OnEnable</c> call <c>VisualiseItemDeath()</c> and
        /// paint every renderer black; health &lt; maxHealth makes
        /// <c>IsDamaged()</c> true, and <c>IsSalvageable()</c> is
        /// <c>!IsDamaged() || IsRepairable()</c> - so a damaged tree is only
        /// salvageable if it is also repairable, which is a coin flip we have no
        /// reason to take. Equal and healthy is unambiguous.
        /// </summary>
        public const int ItemHealth = 100;
    }
}
