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
        // Full-catalogue coverage: weapons / procedural power.
        [InlineData("PistolsRootSchematic", "pistol")]
        [InlineData("PistolsSchematic2", "pistolBullets")]
        [InlineData("CannonsSchematicBonus1", "cannonball")]
        [InlineData("CannonsSchematic2", "cannonShell")]
        [InlineData("SwivelGunSchematicBonus1", "swivelGunShell")]
        [InlineData("EnginesRootSchematic", "proceduralEngineDefault")]
        [InlineData("EnginesSchematicBonus1", "powerGenerator01")]
        [InlineData("EnginesSchematicBonus2", "moonshine")]
        [InlineData("EnginesSchematic2", "powerGenerator")]
        [InlineData("Territory Control Tower", "territory_control_beacon")]
        // Ship structure / fittings.
        [InlineData("Crows Nest", "helm")]
        [InlineData("Paint Can", "smallPanel")]
        [InlineData("Paint Drum", "deck")]
        [InlineData("Shipping Container", "shippingContainer")]
        // Explorer instruments + field kit.
        [InlineData("Compass", "headingIndicator")]
        [InlineData("Makeshift Bandages", "personalReviver")]
        [InlineData("Nervure Bandages", "altimeter")]
        // Tradesman furniture / storage / clothing.
        [InlineData("Long Metal Table", "assemblyStation")]
        [InlineData("Metal Chair", "cupboard")]
        [InlineData("Long Wooden Table", "barrel")]
        [InlineData("Wooden Stool", "makeshiftStorage")]
        [InlineData("Dye", "clothMakeshift")]
        [InlineData("Herder's Poncho", "sail")]
        // Cooking.
        [InlineData("Bread", "thuntomiteStew")]
        // Reachability attachments on spare procedural nodes.
        [InlineData("EnginesSchematic3", "atlasSkyCore")]
        [InlineData("SwivelGunSchematic2", "horn")]
        [InlineData("CannonsSchematic3", "guitar")]
        [InlineData("RiflesRootSchematic", "lamp")]
        [InlineData("RiflesSchematic2", "torch")]
        [InlineData("RiflesSchematic3", "airspeedIndicator")]
        public void A_mapped_node_resolves_to_its_recovered_recipe_id(string nodeId, string recipeId)
        {
            Assert.Equal(recipeId, KnowledgeSpendPolicy.SchematicIdFor(nodeId));
        }

        [Theory]
        // Nodes with no recovered recipe learn under their own id (harmless: an id
        // the catalogue does not carry is silently dropped by the client). These are
        // procedural weapon nodes with no concrete recipe in the served catalogue.
        [InlineData("One-HandedBladesRootSchematic")]
        [InlineData("Two-HandedBluntRootSchematic")]
        [InlineData("SniperRiflesRootSchematic")]
        [InlineData("RiflesSchematic4")]
        [InlineData("WingsSchematic2")]
        public void An_unmapped_node_learns_under_its_own_id(string nodeId)
        {
            Assert.Equal(nodeId, KnowledgeSpendPolicy.SchematicIdFor(nodeId));
        }
    }
}
