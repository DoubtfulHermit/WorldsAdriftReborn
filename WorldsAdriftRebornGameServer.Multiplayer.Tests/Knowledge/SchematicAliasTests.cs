using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The node-name -> recipe-id alias table that wires the knowledge tree to the
    /// recovered recipe catalogue. The tree carries display-ish node names ("Head
    /// Torch", "Storage Container", "Atlas Core Enhancer"); the catalogue keys are
    /// camelCase-ish ("headTorch", "storageContainer", "skyCoreAtlasEnhancer").
    /// <see cref="KnowledgeSpendPolicy.SchematicIdFor"/> must bridge them so unlocking
    /// a node learns the RIGHT recipe. Every expected id here exists as a key in
    /// Game/Items/Config/schematicData.json.
    /// </summary>
    public class SchematicAliasTests
    {
        [Theory]
        // Milestone + procedural ship parts.
        [InlineData("Shipbuilding", "shipyard")]
        [InlineData("WingsRootSchematic", "proceduralWingDefault")]
        // Explorer branch.
        [InlineData("Fuel Gauge", "fuelGauge")]
        [InlineData("Hip Lamp", "hipLamp")]
        [InlineData("Head Torch", "headTorch")]
        [InlineData("Glider", "glider")]
        [InlineData("Artificial Horizon", "artificialHorizon")]
        // SkyshipBuilder branch.
        [InlineData("Stairs", "stairs")]
        [InlineData("Medium Panel", "mediumPanel")]
        [InlineData("Window Panel", "window")]
        [InlineData("Large Panel", "largePanel")]
        [InlineData("Ship Railing", "railing")]
        [InlineData("Railing Corner", "railingCorner")]
        // Tradesman branch.
        [InlineData("Trunk", "trunk")]
        [InlineData("Mounted Box", "mountedBox")]
        [InlineData("Storage Container", "storageContainer")]
        [InlineData("Loom", "loom")]
        // Cooking branch.
        [InlineData("Campfire", "campFire")]
        [InlineData("Thuntomite Steak", "thuntomiteSteak")]
        [InlineData("Manta Steak", "mantaSteak")]
        [InlineData("Stove", "stove")]
        // AtlasEngineer branch.
        [InlineData("Atlas Core Enhancer", "skyCoreAtlasEnhancer")]
        [InlineData("Atlas Core Generator", "skyCoreGenerator")]
        [InlineData("Atlas Core Air Filter", "skyCoreAirFilter")]
        [InlineData("Atlas Core Coolant System", "skyCoreCoolantSystem")]
        [InlineData("Atlas Core Stabiliser", "skyCoreStabiliser")]
        [InlineData("Atlas Core Computer", "skyCoreComputer")]
        [InlineData("Atlas Core Circuitry Network", "skyCoreCircuitryNetwork")]
        [InlineData("Atlas Core Efficiency Module", "skyCoreEfficiencyModule")]
        [InlineData("Lifter", "atlasLifter")]
        public void A_mapped_node_resolves_to_its_recovered_recipe_id(string nodeId, string recipeId)
        {
            Assert.Equal(recipeId, KnowledgeSpendPolicy.SchematicIdFor(nodeId));
        }

        [Theory]
        // Nodes with no recovered recipe learn under their own id (harmless: an id
        // the catalogue does not carry is silently dropped by the client).
        [InlineData("Compass")]
        [InlineData("Makeshift Bandages")]
        [InlineData("Paint Can")]
        [InlineData("Bread")]
        [InlineData("EnginesRootSchematic")]
        [InlineData("PistolsRootSchematic")]
        public void An_unmapped_node_learns_under_its_own_id(string nodeId)
        {
            Assert.Equal(nodeId, KnowledgeSpendPolicy.SchematicIdFor(nodeId));
        }
    }
}
