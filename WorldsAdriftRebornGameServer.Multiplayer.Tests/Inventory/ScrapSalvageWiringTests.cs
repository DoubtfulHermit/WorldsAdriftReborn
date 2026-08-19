using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// IS THE FEATURE ACTUALLY PLUGGED IN? - the guard against this repo's most
    /// expensive recurring failure.
    ///
    /// Tree felling shipped with a green suite and was shown to nobody for days,
    /// because the tests stopped at the pure model and never reached the service
    /// that runs. The same shape is available here: <c>ScrapSalvagePolicy</c> can be
    /// perfect and fully covered while <c>1082 tryToConsume</c> quietly still says
    /// "no consumable effects", and every other test in this suite would stay green.
    ///
    /// The game-server assembly has no test project - it cannot have one, it needs a
    /// Windows game install to compile against - so the seam is asserted the only
    /// way that is available from here: by reading the production source off disk,
    /// the same way <c>LootScrapTableIntegrityTests</c> reads itemData.json. This is
    /// deliberately a COARSE test. It cannot prove the salvage is correct; the
    /// policy tests do that. It proves the wire is connected, and it goes red if
    /// somebody deletes the connection.
    /// </summary>
    public class ScrapSalvageWiringTests
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

        private static string Handler() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "Update", "Handlers",
            "InventoryModificationState_Handler.cs");

        private static string Service() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Inventory", "ScrapSalvageService.cs");

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        /// <summary>
        /// The 1082 handler must dispatch tryToConsume. Without this line the client's
        /// SALVAGE click reaches the server and is thrown away, which is exactly the
        /// state production was in before this phase.
        /// </summary>
        [Fact]
        public void TheInventoryRequestBusDispatchesTryToConsume()
        {
            string handler = Handler();

            Contains(handler, "requests += HandleTryToConsume(",
                "1082 tryToConsume must be dispatched alongside the other inventory events, and it must "
                + "contribute to the request tally - the tally is what triggers the unconditional 1081 push "
                + "that stops the client's inventory panel greying out forever.");

            Contains(handler, "private static int HandleTryToConsume(",
                "The dispatch needs a handler to dispatch to.");
        }

        /// <summary>
        /// tryToConsume must be OFF the refusal list. If it were both dispatched and
        /// noted, the player would be told "refusing 1 tryToConsume request" in the
        /// log of a salvage that actually worked.
        /// </summary>
        [Fact]
        public void TryToConsumeIsNoLongerListedAsUnimplemented()
        {
            Assert.False(Handler().Contains("Note(update.tryToConsume.Count", StringComparison.Ordinal),
                "tryToConsume is served now; leaving it in LogUnimplemented would count every salvage twice "
                + "and log a refusal for a request that was honoured.");
        }

        /// <summary>
        /// The handler must reach the salvage service, and the service must reach the
        /// pure policy. Either link missing is a feature that exists only in the test
        /// suite.
        /// </summary>
        [Fact]
        public void TheHandlerReachesTheSalvageServiceAndTheServiceReachesThePolicy()
        {
            Contains(Handler(), "ScrapSalvageService.TrySalvage(",
                "The handler branch has to call something. If it does not call the service, scrap is still inert.");

            Contains(Service(), "ScrapSalvagePolicy.Salvage(",
                "The service must delegate the decision to the pure policy - that is where atomicity and the "
                + "tier rules are, and where they are tested.");
        }

        /// <summary>
        /// The payout has to come from the shipped reward table, not from anything
        /// invented in the service.
        /// </summary>
        [Fact]
        public void TheServiceIsFedTheShippedRewardTable()
        {
            Contains(Service(), "InventoryWire.ScrapRewards",
                "The yields must come from itemData.json's recovered rewards blocks.");

            Contains(
                Source("WorldsAdriftRebornGameServer", "Game", "Inventory", "InventoryWire.cs"),
                "item.rewards",
                "InventoryWire is the only reader of ValidItem.rewards; nothing else may parse that block.");

            string items = Source("WorldsAdriftRebornGameServer", "Game", "Items", "ItemHelper.cs");

            Contains(items, "rewards { get; set; }",
                "ValidItem must actually deserialise the rewards block, or every lookup returns nothing and "
                + "every salvage refuses with NoRewardBlock.");

            // System.Text.Json binds by exact property name and is case-sensitive by
            // default, so a "tidied" RewardRow with C#-shaped names binds NOTHING and
            // fails completely silently: the block parses, every field is zero, and
            // every salvage refuses. The names have to stay the file's names.
            foreach (string field in new[]
                     {
                         "public int a { get; set; }",
                         "public int q { get; set; }",
                         "public string item { get; set; }",
                     })
            {
                Contains(items, field,
                    "RewardRow's property names ARE the itemData.json keys - System.Text.Json matches them "
                    + "exactly and case-sensitively. Renaming one binds it to nothing, silently.");
            }
        }

        /// <summary>
        /// A salvage that pays but does not toast is a payout the player has to go
        /// looking for. The 8060 FeedbackListener event is the acknowledgement.
        /// </summary>
        [Fact]
        public void ASuccessfulSalvageToastsOnTheHarvestersHud()
        {
            Contains(Service(), "SalvageFeedback.Send(",
                "The native 'Salvaged Iron x30' toast is the only in-world confirmation the player gets.");
        }

        /// <summary>
        /// The event names an inventory entity id and a peer can put anything in it.
        /// Honouring a foreign id would let one player consume items out of a chest -
        /// or out of somebody else's bag - from a hand-built packet.
        /// </summary>
        [Fact]
        public void ATryToConsumeNamingSomebodyElsesInventoryIsRefused()
        {
            Contains(Handler(), "consume.inventoryEntityId.Id != playerEntityId",
                "tryToConsume must be gated on the sender's own inventory, the way the cross-inventory paths "
                + "are gated on one end being the sender's entity.");
        }

        /// <summary>
        /// The source-tier stamp. Without it every relic pays its lowest authored tier
        /// and multi-tier scrap quietly pays the wrong QUALITY - which nobody notices
        /// until they craft with it.
        /// </summary>
        [Fact]
        public void LootContainersStampTheIslandTierOntoWhatTheyHold()
        {
            Contains(
                Source("WorldsAdriftRebornGameServer", "Game", "Loot", "LootStock.cs"),
                "LootContainerLedger.TierOf(entityId)",
                "A chest knows its tier; it has to hand that to BindContainer or the stamp is never written.");

            string service = Source("WorldsAdriftRebornGameServer", "Game", "Inventory", "InventoryService.cs");

            Contains(service, "ScrapSalvagePolicy.SourceTierMetaKey",
                "BindContainer must write the tier under the key the salvage policy reads back. Two different "
                + "spellings of that key is a bug with no symptom other than wrong quality.");

            // Building the stamp and then not handing it to the grant is the version
            // of this bug that leaves every trace of the feature in place, so the
            // stamp has to be asserted where it is USED, not only where it is made.
            Contains(service, "new Dictionary<string, string>(stamp)",
                "The stamp must reach the granted item's meta. A BindContainer that builds it and then grants "
                + "with an empty dictionary looks completely correct and pays every relic its lowest tier.");
        }
    }
}
