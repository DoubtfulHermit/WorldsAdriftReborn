namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One authored branch of a tree: a chain of sections growing out of a
    /// <see cref="Root"/> section. The client's own <c>TreeBase.Branch</c>
    /// (<c>{ public int root; public int[] sections; }</c>), field for field.
    ///
    /// A tree is NOT a single chain. `Tree` is a trunk of nine sections with
    /// three single-section limbs hanging off it, and the difference matters:
    /// cutting a trunk section must fell the limbs above it and leave the limbs
    /// below it standing. That is what <see cref="TreeTopology.WalkTree"/>
    /// computes and it is the whole reason this module exists.
    /// </summary>
    public sealed class TreeBranch
    {
        public TreeBranch(int root, params int[] sections)
        {
            if (sections == null)
            {
                throw new ArgumentNullException(nameof(sections));
            }

            Root = root;
            Sections = sections;
        }

        /// <summary>The section this branch grows OUT of. Not part of the branch.</summary>
        public int Root { get; }

        /// <summary>The branch's own sections, from the root outwards.</summary>
        public IReadOnlyList<int> Sections { get; }
    }

    /// <summary>What one attempted cut did, or why it did nothing.</summary>
    public enum TreeCutOutcome
    {
        /// <summary>The mask changed. <see cref="TreeCut.FallingMask"/> was severed.</summary>
        Cut,

        /// <summary>The section id is outside 0..sectionCount-1. Client input; never trusted.</summary>
        RefusedUnknownSection,

        /// <summary>
        /// One section is left standing. The shipped game never clears the last
        /// one (<c>TreeSection.Harvest</c>: <c>if (tree.sectionsActive &lt;= 1) return;</c>),
        /// so a chopped-out tree ends as a stump rather than as nothing.
        /// </summary>
        RefusedLastSection,

        /// <summary>
        /// The aimed section is not harvestable and there is no active section
        /// above it to forward to. On `Tree` that is section 0, the stump: it is
        /// the only section with no <c>cutPoint</c>, the only one that is never a
        /// child in any branch, and the only one flagged non-harvestable.
        /// </summary>
        RefusedNoTargetAbove,

        /// <summary>
        /// The split produced an empty side and there is nothing above to forward
        /// to. The client throws <c>new Exception("This shouldn't happen")</c>
        /// here; a server must not, because this is reachable from ORDINARY
        /// input - a latch still naming a section that a previous tick already
        /// felled produces exactly this. See <see cref="TreeTopology.Cut"/>.
        /// </summary>
        RefusedDegenerate,
    }

    /// <summary>The result of one attempted cut. A value, so it can be asserted on.</summary>
    public readonly struct TreeCut
    {
        public TreeCut(TreeCutOutcome outcome, int sectionId, int fallingMask, int remainingMask)
        {
            Outcome = outcome;
            SectionId = sectionId;
            FallingMask = fallingMask;
            RemainingMask = remainingMask;
        }

        /// <summary>What happened.</summary>
        public TreeCutOutcome Outcome { get; }

        /// <summary>
        /// The section that was actually cut, which is NOT always the one aimed
        /// at: a non-harvestable or wrongly-angled hit forwards up the branch.
        /// -1 when nothing was cut.
        /// </summary>
        public int SectionId { get; }

        /// <summary>The bits that were severed. 0 unless <see cref="Outcome"/> is Cut.</summary>
        public int FallingMask { get; }

        /// <summary>
        /// The tree's new sectionMask. On any refusal this is the mask that was
        /// passed in, so a caller may always store it unconditionally.
        /// </summary>
        public int RemainingMask { get; }

        /// <summary>Whether the mask changed.</summary>
        public bool DidCut => Outcome == TreeCutOutcome.Cut;

        public override string ToString()
        {
            return Outcome + " section " + SectionId
                + " falling=" + Convert.ToString(FallingMask, 2)
                + " remaining=" + Convert.ToString(RemainingMask, 2);
        }
    }

    /// <summary>
    /// WHICH SECTIONS FALL when a tree is cut - the arithmetic that the FSim used
    /// to own and that this server has to own instead.
    ///
    /// WHY IT IS HERE AT ALL. <c>TreeSection.Harvest()</c> is only ever called
    /// from <c>TreeStateOnTreeSectionIsCut</c>, i.e. only on the FSim worker, and
    /// <c>TreeFsimVisualizer</c> is <c>[WorkerType(UnityWorker)]</c> - it is not
    /// in the client build at all. There is no FSim on this server, so firing a
    /// TreeSectionIsCut event at a client does precisely nothing. The client's
    /// half of chopping is: read <c>1036 TreeFSimState.sectionMask</c>, activate
    /// and deactivate section GameObjects by bit, play a hit effect on whichever
    /// section just left the mask. The MASK IS THE PROTOCOL, and computing it is
    /// entirely ours.
    ///
    /// <c>WalkTree</c> and <c>WalkFrom</c> below are ports of
    /// <c>acs/TreeBase.cs:286-340</c>, kept structurally identical rather than
    /// "cleaned up", because a cleaner traversal that disagrees in a corner would
    /// disagree INVISIBLY: the client renders whatever mask it is handed, so a
    /// wrong answer is not an error, it is a tree that falls apart wrongly.
    ///
    /// THE ONE SUBSTITUTION. The client asks <c>gameObject.activeSelf</c> whether
    /// a section is still standing; <c>InitTreeSections</c> is what sets that, and
    /// it sets it from the mask bit. So a mask bit IS activeSelf, and every
    /// traversal here takes the mask as its liveness oracle.
    ///
    /// Pure: no ENet, no Improbable types, no game install, no clock.
    /// </summary>
    public sealed class TreeTopology
    {
        private readonly bool[] _harvestable;

        /// <param name="sectionCount">How many sections the prefab has. `Tree` has 12.</param>
        /// <param name="branches">The prefab's authored <c>TreeBase.branches</c>.</param>
        /// <param name="harvestable">
        /// Per-section <c>TreeSection.harvestable</c>, indexed by section id. A hit
        /// on a non-harvestable section is forwarded up the branch instead of
        /// cutting, which is how the stump survives being aimed at.
        /// </param>
        public TreeTopology(int sectionCount, IReadOnlyList<TreeBranch> branches, IReadOnlyList<bool> harvestable)
        {
            if (sectionCount <= 0 || sectionCount > 31)
            {
                // 31, not 32: the mask is a signed int on the wire
                // (TreeFSimStateData.sectionMask is `int`), and bit 31 is its sign.
                throw new ArgumentOutOfRangeException(nameof(sectionCount),
                    "a tree has between 1 and 31 sections; the wire mask is a signed int");
            }
            if (branches == null)
            {
                throw new ArgumentNullException(nameof(branches));
            }
            if (harvestable == null)
            {
                throw new ArgumentNullException(nameof(harvestable));
            }
            if (harvestable.Count != sectionCount)
            {
                throw new ArgumentException(
                    "harvestable must have one entry per section (" + sectionCount + ")", nameof(harvestable));
            }

            foreach (TreeBranch branch in branches)
            {
                Validate(branch.Root, sectionCount, "branch root");
                foreach (int section in branch.Sections)
                {
                    Validate(section, sectionCount, "branch section");
                }
            }

            SectionCount = sectionCount;
            Branches = branches;
            _harvestable = harvestable.ToArray();
        }

        private static void Validate(int section, int sectionCount, string what)
        {
            if (section < 0 || section >= sectionCount)
            {
                throw new ArgumentOutOfRangeException(what,
                    what + " " + section + " is outside 0.." + (sectionCount - 1));
            }
        }

        /// <summary>How many sections the prefab has.</summary>
        public int SectionCount { get; }

        /// <summary>The authored branches.</summary>
        public IReadOnlyList<TreeBranch> Branches { get; }

        /// <summary>
        /// Every section standing: the mask a freshly spawned tree is seeded with.
        /// <c>TreeBase.SetTreeDefaults</c> computes the same thing as
        /// <c>2^treeSections.Length - 1</c>.
        /// </summary>
        public int FullMask => (1 << SectionCount) - 1;

        /// <summary>Whether a section can be cut at all.</summary>
        public bool IsHarvestable(int section)
        {
            return section >= 0 && section < SectionCount && _harvestable[section];
        }

        /// <summary>Whether a section is still standing in a mask.</summary>
        public static bool IsInMask(int section, int sectionMask)
        {
            return section >= 0 && (sectionMask & (1 << section)) != 0;
        }

        /// <summary>How many sections are standing. The client's <c>sectionsActive</c>.</summary>
        public int ActiveCount(int sectionMask)
        {
            int count = 0;
            for (int i = 0; i < SectionCount; i++)
            {
                if (IsInMask(i, sectionMask))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Every section that would come away with <paramref name="startSection"/>:
        /// itself, everything further out along its branch, and every branch
        /// hanging off any of those.
        ///
        /// Verbatim port of <c>TreeBase.WalkTree</c> (acs/TreeBase.cs:286-302).
        /// Two behaviours of the original are deliberately preserved because they
        /// are load-bearing:
        ///
        /// 1. <paramref name="startSection"/> is added FIRST and UNCONDITIONALLY,
        ///    before any liveness test. A walk from an already-felled section
        ///    therefore returns a mask with a bit the tree no longer has - which
        ///    is exactly the case <see cref="Cut"/>'s correction clauses exist to
        ///    catch, and removing this would silently change what they see.
        /// 2. The start section is then usually added a SECOND time by
        ///    <c>WalkFrom</c>, since the walk resumes at its own index. Duplicates
        ///    are harmless because callers OR the results into a mask; the list is
        ///    returned rather than the mask only so a test can see the traversal.
        /// </summary>
        public IReadOnlyList<int> WalkTree(int startSection, int sectionMask)
        {
            List<int> sectionsList = new List<int> { startSection };

            foreach (TreeBranch branch in Branches)
            {
                for (int j = 0; j < branch.Sections.Count; j++)
                {
                    if (branch.Sections[j] == startSection)
                    {
                        WalkFrom(branch, j, sectionsList, sectionMask);
                    }
                }
            }

            return sectionsList;
        }

        /// <summary>
        /// Verbatim port of <c>TreeBase.WalkFrom</c> (acs/TreeBase.cs:324-340).
        /// Note the order: sub-branches are recursed into BEFORE the section they
        /// hang off is added, so the list comes back leaf-first. Irrelevant to the
        /// mask, preserved so the traversal can be compared against the original.
        ///
        /// <c>_walked</c> guards the recursion. The original has no guard because
        /// Bossa's authored data is a tree; ours arrives through a public
        /// constructor, and a branch cycle would otherwise be a stack overflow
        /// that takes the whole server down.
        /// </summary>
        private void WalkFrom(TreeBranch branch, int index, List<int> sectionsList, int sectionMask)
        {
            WalkFrom(branch, index, sectionsList, sectionMask, new HashSet<TreeBranch>());
        }

        private void WalkFrom(TreeBranch branch, int index, List<int> sectionsList, int sectionMask, HashSet<TreeBranch> walked)
        {
            if (!walked.Add(branch))
            {
                return;
            }

            for (int i = index; i < branch.Sections.Count; i++)
            {
                foreach (TreeBranch other in Branches)
                {
                    if (other.Root == branch.Sections[i])
                    {
                        WalkFrom(other, 0, sectionsList, sectionMask, walked);
                    }
                }

                // The client's `treeSections[n] != null && activeSelf`. activeSelf
                // is set from the mask bit by InitTreeSections, so this IS the mask.
                if (IsInMask(branch.Sections[i], sectionMask))
                {
                    sectionsList.Add(branch.Sections[i]);
                }
            }
        }

        /// <summary>
        /// <see cref="WalkTree"/> as a bitmask. The client's
        /// <c>TreeSection.CalculateSectionMaskMinusThisSection</c>
        /// (acs/TreeSection.cs:87-96), which despite its name computes what COMES
        /// AWAY, not what is left.
        /// </summary>
        public int FallingMaskFor(int startSection, int sectionMask)
        {
            int mask = 0;
            foreach (int section in WalkTree(startSection, sectionMask))
            {
                mask |= 1 << section;
            }
            return mask;
        }

        /// <summary>
        /// The next standing section outward from <paramref name="id"/>, or null.
        /// Port of <c>TreeBase.FindSectionAbove</c> (acs/TreeBase.cs:304-322).
        ///
        /// It looks exactly ONE step: if the immediate next section on the branch
        /// has already been felled it returns null rather than searching further
        /// out. That is not an oversight to fix - it is what makes a stale aim at
        /// an already-felled section resolve to
        /// <see cref="TreeCutOutcome.RefusedDegenerate"/> instead of quietly
        /// chopping something the player is no longer pointing at.
        /// </summary>
        public int? SectionAbove(int id, int sectionMask)
        {
            foreach (TreeBranch branch in Branches)
            {
                if (branch.Root == id && branch.Sections.Count > 0 && IsInMask(branch.Sections[0], sectionMask))
                {
                    return branch.Sections[0];
                }

                for (int j = 0; j < branch.Sections.Count; j++)
                {
                    if (branch.Sections[j] == id
                        && branch.Sections.Count > j + 1
                        && IsInMask(branch.Sections[j + 1], sectionMask))
                    {
                        return branch.Sections[j + 1];
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// ONE hit on one section. Port of <c>TreeSection.Harvest</c>
        /// (acs/TreeSection.cs:29-85), minus the two things that need an FSim -
        /// spawning the severed part as a new dynamic entity, and the health
        /// bookkeeping.
        ///
        /// THE DAMAGE MODEL IS ONE HIT PER SECTION, and that is the original's,
        /// not a simplification: acs/TreeSection.cs:73-74 is literally
        /// <c>connectionStrength = 0; connectionStrength--;</c> - the authored
        /// <c>connectionStrength = 3</c> is overwritten, not decremented. Nothing
        /// in the client reads <c>TreeFSimState.sectionHealth</c> either, so a
        /// multi-hit health model would be invisible on screen no matter what we
        /// did with it.
        ///
        /// <paramref name="above"/> is the wire's <c>aboveOrBelow</c>: whether the
        /// beam landed above the section's own origin. A high hit is forwarded one
        /// section outward, so aiming near the top of a section cuts the next one
        /// up - which is what makes chopping feel like it follows the crosshair.
        ///
        /// THE THREE CORRECTION CLAUSES are kept even though authored data cannot
        /// trigger the first two, because the THIRD is reachable in normal play:
        /// the latch that drives this keeps naming a section after that section
        /// has been felled, and the walk from a felled section returns a bit the
        /// mask no longer has (see <see cref="WalkTree"/>). The client answers that
        /// case by throwing; we answer it with
        /// <see cref="TreeCutOutcome.RefusedDegenerate"/> and no state change.
        /// </summary>
        /// <param name="sectionMask">The tree's current mask.</param>
        /// <param name="sectionId">The section the beam named.</param>
        /// <param name="above">The wire's aboveOrBelow for that hit.</param>
        public TreeCut Cut(int sectionMask, int sectionId, bool above)
        {
            HashSet<int> visited = new HashSet<int>();
            int id = sectionId;
            bool isAbove = above;

            // The original recurses through TreeSection.Harvest. Iterating with a
            // visited set instead means a forwarding cycle - which authored data
            // cannot produce but a caller-supplied topology could - ends in a
            // refusal rather than in a stack overflow.
            while (true)
            {
                if (id < 0 || id >= SectionCount)
                {
                    return Refused(TreeCutOutcome.RefusedUnknownSection, sectionMask);
                }
                if (!visited.Add(id))
                {
                    return Refused(TreeCutOutcome.RefusedDegenerate, sectionMask);
                }

                int? sectionAbove = SectionAbove(id, sectionMask);

                if (!_harvestable[id])
                {
                    // acs/TreeSection.cs:31-37. The stump forwards upward and is
                    // never itself removed.
                    if (sectionAbove == null)
                    {
                        return Refused(TreeCutOutcome.RefusedNoTargetAbove, sectionMask);
                    }
                    id = sectionAbove.Value;
                    isAbove = false;
                    continue;
                }

                if (ActiveCount(sectionMask) <= 1)
                {
                    return Refused(TreeCutOutcome.RefusedLastSection, sectionMask);
                }

                bool nothingAbove = sectionAbove == null;

                int falling = FallingMaskFor(id, sectionMask);
                if ((sectionMask | falling) != sectionMask)
                {
                    falling &= sectionMask;
                }

                int remaining = sectionMask & ~falling;
                if ((sectionMask | remaining) != sectionMask)
                {
                    remaining &= sectionMask;
                }
                if ((remaining & falling) != 0)
                {
                    falling &= ~remaining;
                }

                bool degenerate = remaining == 0 || falling == 0;

                if (degenerate && nothingAbove)
                {
                    // acs/TreeSection.cs:62-65 throws here. We do not.
                    return Refused(TreeCutOutcome.RefusedDegenerate, sectionMask);
                }

                if ((isAbove && !nothingAbove) || degenerate)
                {
                    id = sectionAbove!.Value;
                    isAbove = false;
                    continue;
                }

                return new TreeCut(TreeCutOutcome.Cut, id, falling, remaining);
            }
        }

        private static TreeCut Refused(TreeCutOutcome outcome, int sectionMask)
        {
            // RemainingMask is the mask that came in, so a caller can store the
            // result unconditionally without first asking whether it cut.
            return new TreeCut(outcome, -1, 0, sectionMask);
        }
    }
}
