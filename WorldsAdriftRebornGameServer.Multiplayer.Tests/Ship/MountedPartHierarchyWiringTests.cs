using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// IS THE MOUNTED-PART HIERARCHY KEY ACTUALLY PLUGGED IN? - the same guard
    /// <see cref="ShipContainerWiringTests"/> and
    /// <c>Flight.FlightForceModelWiringTests</c> exist for, aimed at PHASE SC5.
    ///
    /// The pure tests next door prove the policy answers correctly. They cannot prove
    /// the game server ASKS it, and the game-server assembly has no test project of its
    /// own (it needs a Windows game install to compile against). This failure is
    /// completely silent in both directions: leave one of the three transform sites on
    /// the old hardcoded <c>"~"</c> and the pipe is a Unity child on checkout and a
    /// follower on the next wake - the client would unparent and reparent it several
    /// times a second, which is the jitter SC5's risk 1 names - while every unit test
    /// stays green and no log line changes.
    ///
    /// So the wire is asserted the only way available from here: by reading the
    /// production source off disk. Deliberately COARSE. It cannot prove a pipe rides a
    /// flying hull; only a live craft can do that. It goes red the moment a seam is cut.
    /// </summary>
    public class MountedPartHierarchyWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string Serializer() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs");

        private static string MountService() => Source(
            "WorldsAdriftRebornGameServer", "Game", "PartMountService.cs");

        private static string FlightService() => Source(
            "WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        private static void DoesNotContain(string haystack, string needle, string why)
        {
            Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
                "Did not expect to find `" + needle + "`. " + why);
        }

        /// <summary>
        /// SITE 1 - the checkout seed. A late joiner, and any re-checkout, must receive
        /// the same hierarchy key the mount commit sent. If this one reverts to the
        /// hardcoded slot, the pipe is a Unity child for the player who bolted it on and
        /// a plain follower for everyone else - a placement surface that works for one
        /// person, which is the hardest possible bug to report.
        /// </summary>
        [Fact]
        public void TheCheckoutSeedAsksThePolicyForTheMountedPartsKey()
        {
            string serializer = Serializer();
            Contains(serializer, "MountedPartHierarchy",
                "the 190602 mounted-part seed must read the per-part hierarchy policy.");
            Contains(serializer, "HierarchyKeyFor(mount.Value.ItemType)",
                "the key must be derived from the MOUNT LEDGER's item type. Keying on the "
                + "prefab name instead would be silently switchable off by a per-schematic "
                + "prefab env override.");
            DoesNotContain(serializer, "BoltedPartTransform.RelativeSlotKey",
                "the mounted-part seed was the serializer's ONLY hardcoded \"~\" no-parent "
                + "sentinel, and that single decision is what breaks all five client parent "
                + "walks. If a new one appears here, it needs the same per-part policy.");
        }

        /// <summary>
        /// SITE 2 - the mount commit, the broadcast every player present receives at the
        /// moment the part is bolted on.
        /// </summary>
        [Fact]
        public void TheMountCommitBroadcastsThePerPartKey()
        {
            string service = MountService();
            Contains(service, "MountedPartHierarchy",
                "the 1070 commit's 190602 must read the per-part hierarchy policy.");
            Contains(service, "HierarchyKeyFor(mountedItemType)",
                "the committed key must come from the part's catalogue item type.");
            Contains(service, "localOffset, hullEntityId, mountHierarchyKey, stamp",
                "the commit's wake update must be BUILT with that key - deriving it and then "
                + "passing something else is exactly the kind of dead computation this repo "
                + "has shipped before.");
            DoesNotContain(service, "BoltedPartTransform.RelativeSlotKey",
                "no site in the mount commit may hardcode the \"~\" sentinel any more.");
        }

        /// <summary>
        /// SITE 3 - the in-flight wake, and RISK 1. A real-parent part must be SKIPPED,
        /// not merely sent the right key: the wake carries the parent field, and every
        /// ParentUpdated makes the client un-parent (CachedTransform.parent =
        /// OriginalParentTransform) before re-parenting. At the flight cadence that is a
        /// destroy/re-add of the part's rigidbody several times a second.
        /// </summary>
        [Fact]
        public void TheFlightWakeSKIPSMountedPartsThatAreRealUnityChildren()
        {
            string service = FlightService();
            Contains(service, "MountedPartHierarchy.IsUnityChild(mount.ItemType)",
                "the flight wake must ask which mounted parts are real Unity children.");

            int guard = service.IndexOf("MountedPartHierarchy.IsUnityChild(mount.ItemType)",
                StringComparison.Ordinal);
            int wake = service.IndexOf("mount.LocalOffset, hullEntityId,", StringComparison.Ordinal);
            Assert.True(guard >= 0 && wake >= 0 && guard < wake,
                "The Unity-child guard must come BEFORE the wake is built for a mounted part. "
                + "Testing it afterwards, or only logging it, still puts the parent field on "
                + "the wire and still churns the client's transform twice a second.");

            int skip = service.IndexOf("continue;", guard, StringComparison.Ordinal);
            Assert.True(skip >= 0 && skip < wake,
                "The guard must SKIP the part. A guard that falls through to the wake is a "
                + "guard in name only.");
        }

        /// <summary>
        /// The static hull's own wake keeps its own filter. Two filters, one shape: the
        /// static parts are keyed by world-entity key (BoltedPartTransform), the crafted
        /// mounts by catalogue item type (MountedPartHierarchy). Neither may be dropped.
        /// </summary>
        [Fact]
        public void TheStaticHullWakeStillExcludesItsOwnUnityChildren()
        {
            string motion = Source("WorldsAdriftRebornGameServer", "Game", "ShipPartMotionService.cs");
            Contains(motion, "BoltedPartTransform.IsUnityChild(part.Key)",
                "the static hull's deck must stay excluded from its heartbeat for the same "
                + "reason a mounted bar pipe is excluded from the flight wake.");
        }
    }
}
