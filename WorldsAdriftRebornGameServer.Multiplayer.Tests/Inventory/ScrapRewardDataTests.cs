using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// THE SHIPPED SCRAP YIELD TABLE, ASSERTED AGAINST THE RULES THAT PAY IT OUT.
    ///
    /// <c>ScrapSalvagePolicyTests</c> proves the rules are right about a table; this
    /// proves the REAL table can be paid. It reads
    /// <c>Game/Items/Config/itemData.json</c> off disk exactly as the game server
    /// does, the way <c>LootScrapTableIntegrityTests</c> already does for the loot
    /// projection, because a payout that is unreachable in the data is invisible to
    /// any amount of unit testing.
    ///
    /// Everything asserted here is RECOVERED. Nothing below encodes a preference
    /// about how much scrap should be worth - only that every row the file carries
    /// resolves to something the inventory can actually hold.
    /// </summary>
    public class ScrapRewardDataTests
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

        private static JArray Items() => JArray.Parse(File.ReadAllText(Path.Combine(
            RepoRoot(), "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json")));

        private static string IdOf(JToken row) => (string?)row["itemTypeID"] ?? "";

        private static IEnumerable<JObject> Rows() => Items().Children<JObject>();

        private static IEnumerable<JObject> WithRewards() =>
            Rows().Where(r => r["rewards"] is JObject o && o.Properties().Any());

        /// <summary>
        /// THE ONE-CHARACTER BUG. The client gates its SALVAGE button on
        /// <c>ItemTypeId.StartsWith("scrapItem-")</c>
        /// (<c>acs/Travellers.UI.PlayerInventory/InventoryTooltipPopup.cs:113</c>), so
        /// <c>scrapItemselenistswoodenorrery</c> - missing its hyphen - carried a real
        /// tier-4 reward block that NO PLAYER COULD EVER REACH. An earlier audit
        /// logged it as a cosmetic description typo; it was an unreachable item.
        ///
        /// Asserted as a class, not as one id: if one id can lose a character, so can
        /// the next one somebody adds.
        /// </summary>
        [Fact]
        public void EveryRowWithARewardBlockIsOneTheClientWillOfferSalvageOn()
        {
            string[] unreachable = WithRewards()
                .Select(IdOf)
                .Where(id => !ScrapSalvagePolicy.IsScrap(id))
                .ToArray();

            Assert.True(unreachable.Length == 0,
                "These rows carry a salvage reward the client can never ask for, because their id does not "
                + "start with '" + ScrapSalvagePolicy.ScrapPrefix + "': " + string.Join(", ", unreachable));
        }

        /// <summary>
        /// The same typo class, looked for the other way round: an id that begins with
        /// a known prefix STEM but is missing the hyphen after it. This is what
        /// <c>scrapItemselenistswoodenorrery</c> looked like from the outside.
        /// </summary>
        [Theory]
        [InlineData("scrapItem")]
        [InlineData("steamInvBundle")]
        public void NoIdWearsAKnownPrefixWithoutItsHyphen(string stem)
        {
            string[] malformed = Rows()
                .Select(IdOf)
                .Where(id => id.Length > stem.Length
                             && id.StartsWith(stem, StringComparison.Ordinal)
                             && id[stem.Length] != '-')
                .ToArray();

            Assert.True(malformed.Length == 0,
                "'" + stem + "' must be followed by '-'; these are not: " + string.Join(", ", malformed));
        }

        /// <summary>
        /// A REPEATED ID IS LAST-WINS AND SILENT, on the server
        /// (<c>ItemHelper.AllItems</c>) and on the client alike
        /// (<c>acs/InventoryItemManager.cs:81</c> builds its <c>itemDict</c> the same
        /// way). It cost the Wooden Bowl its name and its whole reward block: the file
        /// carried <c>scrapItem-woodenbowl</c> twice and the second copy had neither.
        ///
        /// Identical repeats are tolerated - they resolve to the same item - but two
        /// rows that DISAGREE mean one of them is a lie nobody will ever see.
        /// </summary>
        [Fact]
        public void NoIdIsListedTwiceWithDifferentContents()
        {
            List<string> conflicting = new();

            foreach (IGrouping<string, JObject> group in Rows()
                         .Where(r => IdOf(r).Length > 0)
                         .GroupBy(IdOf))
            {
                if (group.Count() > 1
                    && group.Select(r => r.ToString(Newtonsoft.Json.Formatting.None)).Distinct().Count() > 1)
                {
                    conflicting.Add(group.Key);
                }
            }

            Assert.True(conflicting.Count == 0,
                "Listed more than once with differing contents; the LAST row silently wins: "
                + string.Join(", ", conflicting));
        }

        /// <summary>
        /// Every tier key in the file parses under the rule the payout uses. A key
        /// that does not parse is a yield nobody is ever paid, and it would be
        /// invisible - the item still salvages, just for less.
        /// </summary>
        [Fact]
        public void EveryRewardTierKeyParses()
        {
            List<string> bad = new();

            foreach (JObject row in WithRewards())
            {
                foreach (JProperty reward in ((JObject)row["rewards"]!).Properties())
                {
                    if (!ScrapSalvagePolicy.TryParseTierKey(reward.Name, out _, out _))
                    {
                        bad.Add(IdOf(row) + " -> '" + reward.Name + "'");
                    }
                }
            }

            Assert.True(bad.Count == 0, "Unparseable reward tier keys: " + string.Join(", ", bad));
        }

        /// <summary>
        /// A <c>.1</c>/<c>.2</c> key is a SECOND yield at the same tier, not a
        /// sub-tier. Two facts in the data say so and both are asserted, because the
        /// plan's original resolution rule ("the highest key whose integer part is n")
        /// depended on the opposite reading and would have deleted 23 base yields:
        /// every sub-key has its base key present, and its material always differs.
        /// </summary>
        [Fact]
        public void ASubKeyIsASecondYieldAtTheSameTierNotASubTier()
        {
            List<string> orphans = new();
            List<string> sameMaterial = new();
            int subKeys = 0;

            foreach (JObject row in WithRewards())
            {
                JObject rewards = (JObject)row["rewards"]!;

                foreach (JProperty reward in rewards.Properties())
                {
                    if (!ScrapSalvagePolicy.TryParseTierKey(reward.Name, out int tier, out int ordinal)
                        || ordinal == 0)
                    {
                        continue;
                    }

                    subKeys++;
                    string baseKey = tier.ToString(System.Globalization.CultureInfo.InvariantCulture);

                    if (rewards[baseKey] is not JObject baseRow)
                    {
                        orphans.Add(IdOf(row) + " -> '" + reward.Name + "'");
                        continue;
                    }

                    if ((string?)baseRow["item"] == (string?)reward.Value["item"])
                    {
                        sameMaterial.Add(IdOf(row) + " -> '" + reward.Name + "'");
                    }
                }
            }

            Assert.True(subKeys > 0, "The file no longer has any second yields; this test is now vacuous.");
            Assert.True(orphans.Count == 0, "Sub-keys with no base key: " + string.Join(", ", orphans));
            Assert.True(sameMaterial.Count == 0,
                "Sub-keys repeating their base key's material: " + string.Join(", ", sameMaterial));
        }

        /// <summary>
        /// Every material a scrap item pays must be a real row with a real footprint,
        /// or <c>InventoryPolicy.TryGrant</c> refuses it and the whole payout aborts -
        /// which reads in game as "salvage does nothing" for that one relic.
        /// </summary>
        [Fact]
        public void EveryYieldMaterialIsAnItemThatCanBePlaced()
        {
            Dictionary<string, JObject> byId = new();
            foreach (JObject row in Rows())
            {
                if (IdOf(row).Length > 0) byId[IdOf(row)] = row;
            }

            List<string> broken = new();

            foreach (JObject row in WithRewards())
            {
                foreach (JProperty reward in ((JObject)row["rewards"]!).Properties())
                {
                    string material = (string?)reward.Value["item"] ?? "";

                    if (!byId.TryGetValue(material, out JObject? definition))
                    {
                        broken.Add(IdOf(row) + " -> '" + material + "' (no itemData.json row)");
                        continue;
                    }

                    if ((int?)definition["width"] is not > 0 || (int?)definition["height"] is not > 0)
                    {
                        broken.Add(IdOf(row) + " -> '" + material + "' (no footprint)");
                    }
                }
            }

            Assert.True(broken.Count == 0, "Unpayable yields: " + string.Join(", ", broken));
        }

        /// <summary>
        /// SCRAP PAYS METAL, WOOD AND FUEL. Nothing else, and nothing else may be
        /// added: the claim that scrap salvaged into cloth, leather, glass and pigment
        /// was checked against all 134 reward blocks and is FALSE. Anything with one
        /// of those categories in this table would be an invention living next to 133
        /// genuine rows, which is how an invention gets read as a recovery a year
        /// later. See docs/plans/resource-economy.md Phase 5 step 4.
        /// </summary>
        [Fact]
        public void ScrapPaysOnlyMetalWoodAndFuel()
        {
            Dictionary<string, string> categories = new();
            foreach (JObject row in Rows())
            {
                if (IdOf(row).Length > 0) categories[IdOf(row)] = (string?)row["category"] ?? "";
            }

            HashSet<string> allowed = new(StringComparer.Ordinal) { "Metal", "Wood", "Fuel" };

            string[] offending = WithRewards()
                .SelectMany(r => ((JObject)r["rewards"]!).Properties())
                .Select(p => (string?)p.Value["item"] ?? "")
                .Distinct()
                .Where(m => !categories.TryGetValue(m, out string? c) || !allowed.Contains(c))
                .ToArray();

            Assert.True(offending.Length == 0,
                "Scrap yields outside {Metal, Wood, Fuel}: " + string.Join(", ", offending));
        }

        /// <summary>
        /// Amounts and qualities are read verbatim and never rolled, so a zero or
        /// negative amount would be a silently empty payout, and a quality outside
        /// 0-10 renders as nonsense on the client's item tooltip.
        /// </summary>
        [Fact]
        public void EveryRewardIsAPositiveAmountAtALegalQuality()
        {
            List<string> bad = new();

            foreach (JObject row in WithRewards())
            {
                foreach (JProperty reward in ((JObject)row["rewards"]!).Properties())
                {
                    int amount = (int?)reward.Value["a"] ?? 0;
                    int quality = (int?)reward.Value["q"] ?? -1;

                    if (amount <= 0) bad.Add(IdOf(row) + " '" + reward.Name + "' amount " + amount);
                    if (quality is < 0 or > 10) bad.Add(IdOf(row) + " '" + reward.Name + "' quality " + quality);
                }
            }

            Assert.True(bad.Count == 0, string.Join(", ", bad));
        }

        /// <summary>
        /// The table is worth what it was recovered as. A drop here means rows were
        /// lost; a jump means rows were invented. Both are worth a deliberate look
        /// rather than a silent green run.
        /// </summary>
        [Fact]
        public void TheRecoveredTableIsStillOneHundredAndThirtyFourRows()
        {
            Assert.Equal(134, WithRewards().Count());
        }
    }
}
