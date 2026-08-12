using WorldsAdriftRebornGameServer.Multiplayer.Knowledge;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Knowledge
{
    /// <summary>
    /// The node-name -> recipe-id alias table that wires the knowledge tree to the
    /// recovered recipe catalogue. The tree carries display-ish node names ("Head
    /// Torch", "Storage Container", "Atlas Core Enhancer"); the catalogue keys are
    /// camelCase-ish ("headTorch", "storageContainer", "skyCoreAtlasEnhancer").
    /// <see cref="KnowledgeSpendPolicy.SchematicIdsFor"/> must bridge them so unlocking
    /// a node learns the RIGHT recipe(s). A node may learn SEVERAL (a foundational root
    /// grants a whole schematicList). Every expected id here exists as a key in
    /// Game/Items/Config/schematicData.json.
    /// </summary>
    public class SchematicAliasTests
    {
        [Theory]
        // Shipbuilding root: the functional-ship BASELINE (multi-grant).
        [InlineData("Shipbuilding", "shipyard")]
        [InlineData("Shipbuilding", "deck")]
        [InlineData("Shipbuilding", "helm")]
        [InlineData("Shipbuilding", "sail")]
        // Wings / Engines propulsion + power.
        [InlineData("WingsRootSchematic", "proceduralWingDefault")]
        [InlineData("EnginesRootSchematic", "proceduralEngineDefault")]
        [InlineData("EnginesSchematic2", "powerGenerator")]
        [InlineData("EnginesSchematicBonus1", "powerGenerator01")]
        // Explorer branch.
        [InlineData("Fuel Gauge", "fuelGauge")]
        [InlineData("Hip Lamp", "hipLamp")]
        [InlineData("Head Torch", "headTorch")]
        [InlineData("Glider", "glider")]
        [InlineData("Artificial Horizon", "artificialHorizon")]
        [InlineData("Compass", "headingIndicator")]
        [InlineData("Makeshift Bandages", "personalReviver")]
        [InlineData("Nervure Bandages", "altimeter")]
        // SkyshipBuilder (Stairs) branch: ship structure.
        [InlineData("Stairs", "stairs")]
        [InlineData("Medium Panel", "mediumPanel")]
        [InlineData("Window Panel", "window")]
        [InlineData("Large Panel", "largePanel")]
        [InlineData("Ship Railing", "railing")]
        [InlineData("Railing Corner", "railingCorner")]
        [InlineData("Crows Nest", "smallPanel")]
        [InlineData("Paint Drum", "horn")]
        [InlineData("Paint Can", "airspeedIndicator")]
        // Tradesman (Trunk) branch: furniture / storage.
        [InlineData("Trunk", "trunk")]
        [InlineData("Mounted Box", "mountedBox")]
        [InlineData("Storage Container", "storageContainer")]
        [InlineData("Shipping Container", "shippingContainer")]
        [InlineData("Loom", "loom")]
        [InlineData("Metal Chair", "cupboard")]
        [InlineData("Long Wooden Table", "barrel")]
        [InlineData("Long Metal Table", "assemblyStation")]
        [InlineData("Wooden Stool", "makeshiftStorage")]
        [InlineData("Dye", "clothMakeshift")]
        // Cooking (Campfire) branch.
        [InlineData("Campfire", "campFire")]
        [InlineData("Thuntomite Steak", "thuntomiteSteak")]
        [InlineData("Manta Steak", "mantaSteak")]
        [InlineData("Stove", "stove")]
        [InlineData("Bread", "thuntomiteStew")]
        [InlineData("Manta Burger", "moonshine")]
        // Atlas Engineer (Atlas Core Enhancer) branch: sky cores. The root grants the
        // BASIC core AND its enhancer (multi-grant).
        [InlineData("Atlas Core Enhancer", "atlasSkyCore")]
        [InlineData("Atlas Core Enhancer", "skyCoreAtlasEnhancer")]
        [InlineData("Atlas Core Generator", "skyCoreGenerator")]
        [InlineData("Atlas Core Air Filter", "skyCoreAirFilter")]
        [InlineData("Atlas Core Coolant System", "skyCoreCoolantSystem")]
        [InlineData("Atlas Core Stabiliser", "skyCoreStabiliser")]
        [InlineData("Atlas Core Computer", "skyCoreComputer")]
        [InlineData("Atlas Core Circuitry Network", "skyCoreCircuitryNetwork")]
        [InlineData("Atlas Core Efficiency Module", "skyCoreEfficiencyModule")]
        [InlineData("Lifter", "atlasLifter")]
        // Weapons: the roots grant the projectile; ammo tiers add variants.
        [InlineData("PistolsRootSchematic", "pistol")]
        [InlineData("PistolsSchematic2", "pistolBullets")]
        [InlineData("CannonsRootSchematic", "cannonball")]
        [InlineData("CannonsSchematic2", "cannonShell")]
        [InlineData("CannonsSchematicBonus1", "cannonball")]
        [InlineData("SwivelGunRootSchematic", "swivelGunShell")]
        [InlineData("SwivelGunSchematicBonus1", "swivelGunShell")]
        // Territory.
        [InlineData("Territory Control Tower", "territory_control_beacon")]
        public void A_mapped_node_learns_its_recovered_recipe_id(string nodeId, string recipeId)
        {
            Assert.Contains(recipeId, KnowledgeSpendPolicy.SchematicIdsFor(nodeId));
        }

        [Theory]
        // The grant-all-era coverage hacks that parked unrelated recipes on WEAPON /
        // rifle tiers (and moonshine on an engine node) are REMOVED. These nodes no
        // longer learn those recipes; the recipes are homed faithfully elsewhere
        // (moonshine -> Manta Burger, horn/airspeedIndicator -> ship-structure nodes)
        // or are starters (lamp/torch/guitar).
        [InlineData("RiflesRootSchematic", "lamp")]
        [InlineData("RiflesSchematic2", "torch")]
        [InlineData("RiflesSchematic3", "airspeedIndicator")]
        [InlineData("CannonsSchematic3", "guitar")]
        [InlineData("SwivelGunSchematic2", "horn")]
        [InlineData("EnginesSchematic3", "atlasSkyCore")]
        [InlineData("EnginesSchematicBonus2", "moonshine")]
        public void A_removed_coverage_hack_no_longer_learns_that_recipe(string nodeId, string recipeId)
        {
            Assert.DoesNotContain(recipeId, KnowledgeSpendPolicy.SchematicIdsFor(nodeId));
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
            Assert.Equal(new[] { nodeId }, KnowledgeSpendPolicy.SchematicIdsFor(nodeId));
        }
    }
}
