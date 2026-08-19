using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Pins the mounted-part 1210 verb mapping - the audited "what did retail let
    /// you do with this part" table - so a catalogue edit can never silently start
    /// advertising an interaction prompt the server does not handle (the "E does
    /// nothing" lie) or drop one it does.
    /// </summary>
    public class PartInteractionPolicyTests
    {
        [Theory]
        [InlineData("sail")]
        [InlineData("lamp")]
        [InlineData("horn")]
        public void ImplementedActivateParts(string itemType)
        {
            Assert.Equal(PartVerb.Activate, PartInteractionPolicy.VerbFor(itemType));
        }

        /// <summary>
        /// The helm's Man is served by the serializer's dedicated isHelm branch and
        /// handled by the flight service; the policy must answer None so the
        /// mounted-part branch never double-serves it.
        /// </summary>
        [Fact]
        public void HelmIsServedByItsOwnBranchNotThisPolicy()
        {
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor("helm"));
        }

        /// <summary>
        /// Retail verbs exist for these (storage=Inventory, reviver/core=Activate)
        /// but their state serves do not yet - 1081 InventoryState, 1094
        /// RespawnPointState, the core's GSIM-side activation. Advertising a prompt
        /// before the handler exists would be a lie, so None until then. Flipping
        /// any of these to a verb must come WITH its serve + 1211 handling.
        /// </summary>
        [Theory]
        [InlineData("trunk")]
        [InlineData("mountedBox")]
        [InlineData("storageContainer")]
        [InlineData("shippingContainer")]
        [InlineData("personalReviver")]
        public void KnownRetailVerbsNotYetServableStayNone(string itemType)
        {
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor(itemType));
        }

        /// <summary>
        /// Confirmed NOT interactable in retail (no InteractiveObjectVisualizer on
        /// their prefabs/preprocessors): engines and wings are helm-driven, core
        /// modules and instruments are passive, structure/decoration is inert.
        /// </summary>
        [Theory]
        [InlineData("proceduralEngineDefault")]
        [InlineData("proceduralWingDefault")]
        [InlineData("skyCoreAtlasEnhancer")]
        [InlineData("skyCoreGenerator")]
        [InlineData("skyCoreAirFilter")]
        [InlineData("skyCoreCoolantSystem")]
        [InlineData("skyCoreStabiliser")]
        [InlineData("skyCoreComputer")]
        [InlineData("skyCoreCircuitryNetwork")]
        [InlineData("skyCoreEfficiencyModule")]
        [InlineData("altimeter")]
        [InlineData("fuelGauge")]
        [InlineData("headingIndicator")]
        [InlineData("artificialHorizon")]
        [InlineData("airspeedIndicator")]
        [InlineData("powerGenerator")]
        [InlineData("powerGenerator01")]
        [InlineData("deck")]
        [InlineData("stairs")]
        [InlineData("railing")]
        [InlineData("railingCorner")]
        [InlineData("smallPanel")]
        [InlineData("mediumPanel")]
        [InlineData("largePanel")]
        [InlineData("window")]
        [InlineData("cupboard")]
        [InlineData("barrel")]
        public void NonInteractablePartsStayNone(string itemType)
        {
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor(itemType));
        }

        [Fact]
        public void UnknownAndNullItemTypesAreNone()
        {
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor(null));
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor(""));
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor("no-such-part"));
        }

        /// <summary>
        /// EVERY catalogue row has an explicit verdict covered by the cases above:
        /// walking the real catalogue must yield Activate only for sail/lamp/horn.
        /// A new catalogue row defaults to None (safe - no prompt), and this test
        /// documents that adding an interactable one means extending the policy AND
        /// the service together.
        /// </summary>
        [Fact]
        public void WholeCatalogueAuditsToExactlyTheFourActivateParts()
        {
            var interactable = LoosePartCatalogue.All
                .Where(def => PartInteractionPolicy.VerbFor(def.ItemType) != PartVerb.None)
                .Select(def => def.ItemType)
                .OrderBy(t => t)
                .ToArray();

            // atlasSkyCore joined the list as the REFUEL DOOR - the only ship part
            // whose Activate is prefab-baked and otherwise unclaimed. Anything else
            // appearing here without a 1211 handler behind it is a prompt that lies.
            Assert.Equal(new[] { "atlasSkyCore", "horn", "lamp", "sail" }, interactable);
        }

        [Fact]
        public void ActivateEntryValuesAreNonZeroRadiusAndInstant()
        {
            // radius 0 = the prompt never appears (the ManRadius trap); timeToUse 0
            // = instant toggle, no hold bar.
            Assert.True(PartInteractionPolicy.ActivateRadius > 0f);
            Assert.Equal(0f, PartInteractionPolicy.ActivateTimeToUse);
        }

        [Theory]
        [InlineData("helm", PartVerb.Man)]
        [InlineData("sail", PartVerb.Activate)]
        [InlineData("lamp", PartVerb.Activate)]
        [InlineData("horn", PartVerb.Activate)]
        [InlineData("atlasSkyCore", PartVerb.Activate)]
        [InlineData("deck", PartVerb.PickUp)]
        public void InitialCheckoutSeedsThePrefabBakedVerb(string itemType, PartVerb expected)
        {
            // The retail visualizer resolves and caches this entry only in OnEnable.
            // A loose helm/sail must therefore receive its eventual operational verb
            // before it mounts; availability, not an interaction-list replacement,
            // gates the transition.
            Assert.Equal(expected, PartInteractionPolicy.SeedVerbFor(itemType));
        }

        [Theory]
        [InlineData("helm")]
        [InlineData("sail")]
        [InlineData("lamp")]
        [InlineData("horn")]
        public void OperationalPartsBecomeAvailableOnlyAfterMount(string itemType)
        {
            Assert.False(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, false));
            Assert.True(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, true));
        }

        [Fact]
        public void OrdinaryPartsArePickupAvailableOnlyWhileLoose()
        {
            Assert.True(PartInteractionPolicy.IsSeededInteractionAvailable("deck", false));
            Assert.False(PartInteractionPolicy.IsSeededInteractionAvailable("deck", true));
        }

        /// <summary>The wire values mirrored from the decompiled InteractVerb enum.</summary>
        [Fact]
        public void VerbWireValuesMatchTheClientEnum()
        {
            Assert.Equal(1, (int)PartVerb.Activate);
            Assert.Equal(2, (int)PartVerb.PickUp);
            Assert.Equal(3, (int)PartVerb.Man);
            Assert.Equal(4, (int)PartVerb.Inventory);
        }
    }
}
