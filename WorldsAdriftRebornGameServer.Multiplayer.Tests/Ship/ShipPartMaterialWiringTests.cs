using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// IS THE FIX ACTUALLY PLUGGED IN? - the same guard
    /// <c>ScrapSalvageWiringTests</c> exists for, aimed at the invisible window.
    ///
    /// <c>LoosePartSeedMaterial</c> can be perfect and fully covered while the
    /// <c>1099</c> serve branch quietly keeps writing the old uniform Wood
    /// material, and every other test in this suite stays green while the window
    /// stays invisible. That is precisely the failure this repo has shipped twice.
    ///
    /// The game-server assembly has no test project - it needs a Windows game
    /// install to compile against - so the seam is asserted the only way available
    /// from here: by reading the production source off disk. This is a COARSE test.
    /// It cannot prove the material is right; <c>LoosePartSeedMaterialTests</c>
    /// does that. It proves the serve branch consults the policy at all, and it
    /// goes red the moment somebody re-hardcodes the material.
    /// </summary>
    public class ShipPartMaterialWiringTests
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

        private static string Serializer() => File.ReadAllText(Path.Combine(RepoRoot(),
            "WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs"));

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        /// <summary>
        /// The 1099 loose-part branch must read the per-part material off the
        /// definition. Without this the window goes back to being seeded Wood, hits
        /// "No appropriate mesh found for requested ship panel size!" and renders
        /// nothing - which is the state production is in today.
        /// </summary>
        [Fact]
        public void TheSalvageStateBranchTakesItsMaterialFromThePartDefinition()
        {
            string serializer = Serializer();

            Contains(serializer, "loosePart1099.SeedMaterial",
                "the 1099 serve branch must resolve the part's own seed material through "
                + "LoosePartDefinition.SeedMaterial, which is the only thing that gives the window "
                + "a metal material and therefore the only window mesh the client ships.");
            Contains(serializer, "seedMaterial.MaterialTypeId",
                "the RawMaterial written on the wire must carry the resolved materialTypeId - "
                + "ShipPanel.Init prefers it over the prefab's own _panelMaterial, so this is the "
                + "field that decides whether the window has a mesh.");
            Contains(serializer, "seedMaterial.Category",
                "the RawMaterial category must be the resolved one too: the client picks the wood "
                + "or metal mesh array from it, and PartGraphicsVariationByMaterial throws on any "
                + "value that is not \"Wood\" or \"Metal\".");
        }

        /// <summary>
        /// The old hardcoded pair must be GONE from the loose-part path. Leaving it
        /// beside the new lookup is the shape that would let a merge quietly revert
        /// the fix while both this test's first half and the policy tests stay
        /// green.
        /// </summary>
        [Fact]
        public void TheLoosePartBranchNoLongerHardcodesTheDeckWoodMaterial()
        {
            string serializer = Serializer();
            int branch = serializer.IndexOf("var loosePart1099 =", StringComparison.Ordinal);
            Assert.True(branch >= 0, "The 1099 loose-part branch has moved or been deleted.");

            int end = serializer.IndexOf("SalvageAndRepairStateData", branch, StringComparison.Ordinal);
            Assert.True(end > branch, "The 1099 loose-part branch no longer builds a SalvageAndRepairStateData.");
            string body = serializer.Substring(branch, end - branch);

            Assert.False(body.Contains("Multiplayer.Deck.MaterialTypeId", StringComparison.Ordinal),
                "The loose-part 1099 branch must not hardcode the deck's Wood material any more - that "
                + "uniform seed is exactly what made a crafted Window invisible. The Wood default now "
                + "lives in LoosePartSeedMaterial, where the window can differ from it.");
            Assert.False(body.Contains("Multiplayer.Deck.MaterialCategory", StringComparison.Ordinal),
                "Same for the category: it must come from the per-part policy, not a constant.");
        }
    }
}
