using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// IS SHIP STORAGE ACTUALLY PLUGGED IN? - the same guard
    /// <see cref="Inventory.ScrapSalvageWiringTests"/> exists for, applied to the
    /// four seams that make a trunk a chest.
    ///
    /// This feature is unusually exposed to this repo's recurring failure. Every
    /// piece of it fails SILENTLY: an unsatisfied <c>[Require]</c> leaves a
    /// correct-looking prop with no log line, a mismatched verb leaves a prompt that
    /// can never appear, and a missing <c>Ensure</c> hands the container the player
    /// starter kit permanently. The pure tests next door prove the policy is right.
    /// They cannot prove the game server calls it, and the game-server assembly has
    /// no test project of its own - it needs a Windows game install to compile
    /// against. So the wire is asserted the only way available from here: by reading
    /// the production source off disk.
    ///
    /// Deliberately COARSE. It cannot prove storage works; only a live craft can do
    /// that. It proves the connections exist, and it goes red the moment one is
    /// deleted.
    /// </summary>
    public class ShipContainerWiringTests
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
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string Serializer() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs");

        private static string InteractHandler() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "Update", "Handlers",
            "InteractAgentState_Handler.cs");

        private static string InventoryHandler() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "Update", "Handlers",
            "InventoryModificationState_Handler.cs");

        private static string ContainerService() => Source(
            "WorldsAdriftRebornGameServer", "Game", "ShipContainerService.cs");

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        /// <summary>
        /// THE GAUNTLET TRAP. <c>InventoryService.ForEntity</c>'s create-factory is
        /// <c>InventoryWire.DefaultModel</c> - the player starter kit - and
        /// <c>InventoryStore.Bind</c> runs a factory at most once per key. A container
        /// that reaches the 1081 branch unbound holds four gauntlets in a 10x18 belt
        /// grid for the rest of the session and cannot be corrected. The Ensure call
        /// must therefore come BEFORE ForEntity, not merely exist.
        /// </summary>
        [Fact]
        public void TheInventorySeedBindsAContainerBeforeItAsksForItsModel()
        {
            string serializer = Serializer();
            Contains(serializer, "ShipContainerService.Ensure(entityId)",
                "Without it a bolted trunk opens onto the player starter kit, permanently.");

            int ensure = serializer.IndexOf("ShipContainerService.Ensure(entityId)",
                StringComparison.Ordinal);
            int forEntity = serializer.IndexOf("Inventory.InventoryService.ForEntity(entityId)",
                StringComparison.Ordinal);
            Assert.True(forEntity > 0, "The 1081 branch must still read the store via ForEntity.");
            Assert.True(ensure < forEntity,
                "Ensure must run BEFORE ForEntity. Bind's factory runs once per key, so an "
                + "Ensure that arrives second changes nothing at all and leaves the container "
                + "holding the starter kit - with everything looking correctly wired.");
        }

        /// <summary>
        /// THE VERB. The prefab bakes <c>Inventory</c>;
        /// <c>InteractiveObjectVisualizer</c> caches its matching entry once at
        /// OnEnable. Serving the generic PickUp fallback instead means the cache is
        /// empty, the radius is zero, and no prompt can EVER appear - with no error
        /// on either side. This asserts both the branch and its own radius constant,
        /// because a zero radius is the same invisible failure by a different route.
        /// </summary>
        [Fact]
        public void TheInteractionSeedServesTheInventoryVerbWithItsOwnRadius()
        {
            string serializer = Serializer();
            // TWO separate lines, and both are load-bearing. The GATE decides whether
            // a container's verb survives the mounted-part filter at all; the BRANCH
            // builds the entry. Asserting only the bare name "PartVerb.Inventory"
            // passes with the gate deleted, because the branch still mentions it -
            // this test escaped exactly that mutation once, so the gate is now
            // asserted in full.
            Contains(serializer, "|| seededVerb == Multiplayer.Ship.PartVerb.Inventory",
                "The mounted-part filter keeps ONLY Activate unless Inventory is named here, so "
                + "without this line a container silently falls through to the generic PickUp "
                + "entry its prefab never looks for - and nothing logs it.");
            Contains(serializer, "else if (mountedPartVerb == Multiplayer.Ship.PartVerb.Inventory)",
                "The entry branch itself. Without it the verb survives the gate and is then built "
                + "with the Activate pair's 5 m radius and the wrong log line.");
            Contains(serializer, "Multiplayer.Ship.ShipContainers.InteractRadius",
                "A container entry with radius 0 is a prompt that never appears - the "
                + "MetalNodes.PickUpRadius trap.");
            Contains(serializer, "InteractVerb.Inventory",
                "The entry itself must carry the client's Inventory verb.");
        }

        /// <summary>
        /// THE ECHO. The panel does not open because the client holds 1081; it opens
        /// because an <c>Interact</c> event arrives on the container's own 1210. The
        /// 1211 dispatch already routed Inventory to the loot service, which answers
        /// false for a ship part - so without this second call the prompt appears,
        /// the player presses E, and nothing happens.
        /// </summary>
        [Fact]
        public void ThePressOfEIsRoutedToTheShipContainerService()
        {
            Contains(InteractHandler(), "ShipContainerService.OpenContainer(player, entityId, man.target.Id)",
                "The loot service returns false for a ship part, so the Inventory dispatch must fall "
                + "through to this one or E does nothing on a trunk.");
        }

        /// <summary>
        /// THE MOVE. Taking something out of a container and putting something in are
        /// separate events from opening it, gated by their own membership check. A
        /// container that opens but refuses every move is worse than one that does
        /// not open, because it looks like it works.
        /// </summary>
        [Fact]
        public void CrossInventoryMovesAcceptAMountedShipContainer()
        {
            string handler = InventoryHandler();
            Contains(handler, "ShipContainerService.IsContainer(entityId)",
                "The cross-inventory gate must recognise a ship container as a legal other end.");
            Contains(handler, "MountedParts.Is(entityId)",
                "...but only while it is bolted down. A loose container can be lifted away with "
                + "whatever was just stashed in it.");
            Contains(handler, "ShipContainerService.Ensure(containerId)",
                "Both move paths must bind before ForEntity, or the first move into a "
                + "never-checked-out container lands in a bag of gauntlets.");
        }

        /// <summary>
        /// THE LOSS THIS FEATURE CREATES. Before storage existed, salvaging a trunk
        /// destroyed nothing. Now it can destroy whatever is inside, and the player
        /// sees a normal salvage with a full refund. The pure policy refuses it; this
        /// asserts the service asks the question at all.
        /// </summary>
        [Fact]
        public void TheSalvageBeamAsksWhetherTheContainerIsEmpty()
        {
            Contains(
                Source("WorldsAdriftRebornGameServer", "Game", "Crafting",
                    "MountedPartSalvageService.cs"),
                "containerHoldsItems: ShipContainerService.ItemCount(partEntityId) > 0",
                "The policy's ContainerNotEmpty verdict is unreachable unless the real count is "
                + "passed in. Hardcoding false here would keep every test green and delete "
                + "players' belongings.");
        }

        /// <summary>
        /// THE FLIP. A container is seeded with its Inventory entry while still loose
        /// and <c>available=false</c>, because the client caches the entry once at
        /// OnEnable. Mounting must broadcast the flip or the prompt never appears -
        /// and this seam tested a hand-written <c>Man || Activate</c> list, so the
        /// first container to gain a verb was invisible on a green suite. The
        /// predicate is now the policy's, and both the mount and the unmount seam
        /// must use it rather than re-spelling the set.
        /// </summary>
        [Fact]
        public void MountingAContainerFlipsItsPromptOn()
        {
            string mount = Source("WorldsAdriftRebornGameServer", "Game", "PartMountService.cs");

            Assert.Equal(2, CountOf(mount, "PartInteractionPolicy.IsMountOperated("));
            Assert.Equal(0, CountOf(mount, "== PartVerb.Man"));
            Assert.Equal(0, CountOf(mount, "== PartVerb.Activate"));
        }

        private static int CountOf(string haystack, string needle)
        {
            int count = 0;
            int at = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = haystack.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }
            return count;
        }

        /// <summary>
        /// <c>ItemCount</c> must not BIND. If asking a container what it holds created
        /// its inventory, the salvage path would bind every container it shot at -
        /// including, on the very first shot, one that had never been checked out.
        /// </summary>
        [Fact]
        public void AskingAContainerWhatItHoldsDoesNotCreateItsInventory()
        {
            Contains(ContainerService(), "InventoryService.KeyOf(entityId) == null",
                "ItemCount must return 0 for an unbound container rather than calling ForEntity, "
                + "which binds.");
        }
    }
}
