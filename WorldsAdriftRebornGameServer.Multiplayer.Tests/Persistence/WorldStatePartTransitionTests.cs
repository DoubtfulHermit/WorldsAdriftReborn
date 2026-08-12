using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    /// <summary>
    /// The ATOMIC loose&lt;-&gt;mounted transitions on the world snapshot. These are the
    /// in-memory half of the part-duplication / part-loss fix: a part must live in
    /// EXACTLY ONE of <see cref="WorldStateSnapshot.LooseParts"/> /
    /// <see cref="WorldStateSnapshot.MountedParts"/> after any transition, so the single
    /// Save that follows can never write a document with the part in both lists (a
    /// double-spawn on restore) or in neither (a part lost on restore).
    ///
    /// FAIL-BEFORE: the two transitions were two separate whole-file Saves
    /// (RecordMountedPart+Save then RemoveLoosePart+Save); a crash between them left the
    /// part in both lists (mount) or neither (lift). PASS-AFTER: MoveLooseToMounted /
    /// MoveMountedToLoose mutate both lists in one step and the caller Saves once.
    /// </summary>
    public class WorldStatePartTransitionTests
    {
        private const string Uid = "part-abc";

        private static LoosePartRecord Loose(string uid) => new LoosePartRecord
        {
            PartUid = uid,
            SchematicId = "lamp01",
            ItemType = "lamp",
            X = 10,
            Y = 20,
            Z = 30,
        };

        private static MountedPartRecord Mounted(string uid, int shipIndex = 0) => new MountedPartRecord
        {
            PartUid = uid,
            BuiltShipIndex = shipIndex,
            SchematicId = "lamp01",
            ItemType = "lamp",
            LocalX = 1,
            LocalY = 2,
            LocalZ = 3,
        };

        [Fact]
        public void MoveLooseToMounted_leaves_the_part_in_exactly_one_list()
        {
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.LooseParts.Add(Loose(Uid));

            bool removedLoose = snap.MoveLooseToMounted(Uid, Mounted(Uid));

            Assert.True(removedLoose);
            Assert.DoesNotContain(snap.LooseParts, r => r.PartUid == Uid);      // never both
            Assert.Single(snap.MountedParts, r => r.PartUid == Uid);           // exactly one
        }

        [Fact]
        public void MoveMountedToLoose_leaves_the_part_in_exactly_one_list()
        {
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.MountedParts.Add(Mounted(Uid));

            bool removedMounted = snap.MoveMountedToLoose(Uid, Loose(Uid));

            Assert.True(removedMounted);
            Assert.DoesNotContain(snap.MountedParts, r => r.PartUid == Uid);    // never neither
            Assert.Single(snap.LooseParts, r => r.PartUid == Uid);             // exactly one
        }

        [Fact]
        public void MoveLooseToMounted_is_idempotent_and_never_duplicates_the_mount()
        {
            // A re-mount (or a retried write) must upsert, not append a second mount.
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.LooseParts.Add(Loose(Uid));

            snap.MoveLooseToMounted(Uid, Mounted(Uid, shipIndex: 0));
            snap.MoveLooseToMounted(Uid, Mounted(Uid, shipIndex: 1));

            Assert.Empty(snap.LooseParts);
            Assert.Single(snap.MountedParts);                                  // one, not two
            Assert.Equal(1, snap.MountedParts[0].BuiltShipIndex);              // the latest wins
        }

        [Fact]
        public void MoveMountedToLoose_of_a_carried_part_keeps_a_single_loose_record()
        {
            // Lift-then-re-persist twice (a live carry re-persisting) upserts the loose record.
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.MountedParts.Add(Mounted(Uid));

            snap.MoveMountedToLoose(Uid, Loose(Uid));
            snap.MoveMountedToLoose(Uid, Loose(Uid));

            Assert.Empty(snap.MountedParts);
            Assert.Single(snap.LooseParts);
        }

        [Fact]
        public void DeduplicateLooseAgainstMounted_drops_a_part_present_in_both_lists()
        {
            // A document corrupted by a pre-fix non-atomic write: the part is in BOTH.
            // The guard drops the loose copy so restore spawns it ONCE, as mounted.
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.LooseParts.Add(Loose(Uid));
            snap.MountedParts.Add(Mounted(Uid));
            snap.LooseParts.Add(Loose("other-loose"));   // an innocent bystander stays

            int dropped = snap.DeduplicateLooseAgainstMounted();

            Assert.Equal(1, dropped);
            Assert.DoesNotContain(snap.LooseParts, r => r.PartUid == Uid);
            Assert.Contains(snap.LooseParts, r => r.PartUid == "other-loose");
            Assert.Single(snap.MountedParts);
        }

        [Fact]
        public void DeduplicateLooseAgainstMounted_is_a_noop_on_a_clean_snapshot()
        {
            WorldStateSnapshot snap = new WorldStateSnapshot();
            snap.LooseParts.Add(Loose("l1"));
            snap.MountedParts.Add(Mounted("m1"));

            Assert.Equal(0, snap.DeduplicateLooseAgainstMounted());
            Assert.Single(snap.LooseParts);
            Assert.Single(snap.MountedParts);
        }
    }
}
