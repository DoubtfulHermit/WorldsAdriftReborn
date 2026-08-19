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
        /// Retail verbs exist for these (reviver/core=Activate) but their state
        /// serves do not yet - 1094 RespawnPointState, the core's GSIM-side
        /// activation. Advertising a prompt before the handler exists would be a lie,
        /// so None until then. Flipping any of these to a verb must come WITH its
        /// serve + 1211 handling.
        ///
        /// The four storage containers used to be on this list. They came off it when
        /// 1081 + 1236 started being seeded and the Inventory verb served - see
        /// <see cref="StorageContainersAdvertiseTheVerbTheirPrefabBakes"/>.
        /// </summary>
        [Theory]
        [InlineData("personalReviver")]
        [InlineData("atlasSkyCore")]
        public void KnownRetailVerbsNotYetServableStayNone(string itemType)
        {
            Assert.Equal(PartVerb.None, PartInteractionPolicy.VerbFor(itemType));
        }

        /// <summary>
        /// The four ship containers advertise Inventory, which is the verb
        /// ShipContainerPreprocessor.SetVerb bakes into their prefabs. This is not a
        /// free choice: InteractiveObjectVisualizer caches
        /// Interactions.FirstOrDefault(i => i.verb == Verb) ONCE at OnEnable, so any
        /// other verb - including the generic PickUp we served for months - leaves
        /// that lookup empty, the radius at zero, and NO prompt able to appear, with
        /// nothing logged on either side.
        /// </summary>
        [Theory]
        [InlineData("trunk")]
        [InlineData("mountedBox")]
        [InlineData("storageContainer")]
        [InlineData("shippingContainer")]
        public void StorageContainersAdvertiseTheVerbTheirPrefabBakes(string itemType)
        {
            Assert.Equal(PartVerb.Inventory, PartInteractionPolicy.VerbFor(itemType));
            Assert.Equal(PartVerb.Inventory, PartInteractionPolicy.SeedVerbFor(itemType));
        }

        /// <summary>
        /// A container is openable only once BOLTED DOWN. A loose one can be lifted
        /// away by anyone with a scanner, contents and all, so offering to fill it
        /// first would be offering a place to lose things.
        /// </summary>
        [Theory]
        [InlineData("trunk")]
        [InlineData("mountedBox")]
        [InlineData("storageContainer")]
        [InlineData("shippingContainer")]
        public void ContainersOpenOnlyOnceMounted(string itemType)
        {
            Assert.False(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, false));
            Assert.True(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, true));
        }

        /// <summary>
        /// The prompt numbers. Radius zero is the MetalNodes.PickUpRadius trap - the
        /// prompt simply never appears - and a hold nobody expects on a chest reads
        /// as an unresponsive prompt.
        /// </summary>
        [Fact]
        public void ContainerEntryValuesAreNonZeroRadiusAndInstant()
        {
            Assert.True(ShipContainers.InteractRadius > 0f);
            Assert.Equal(0f, ShipContainers.InteractTimeToUse);
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
        /// walking the real catalogue must yield Activate only for sail/lamp/horn and
        /// Inventory only for the four storage containers. A new catalogue row
        /// defaults to None (safe - no prompt), and this test documents that adding an
        /// interactable one means extending the policy AND the service together.
        ///
        /// SEVEN is the whole answer to "which ship parts respond to E" and it is
        /// spelled out here rather than counted, so that a part quietly gaining or
        /// losing a prompt is a failing test rather than a live surprise.
        /// </summary>
        [Fact]
        public void WholeCatalogueAuditsToExactlyTheSevenInteractableParts()
        {
            var interactable = LoosePartCatalogue.All
                .Where(def => PartInteractionPolicy.VerbFor(def.ItemType) != PartVerb.None)
                .Select(def => def.ItemType)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "horn", "lamp", "mountedBox", "sail",
                    "shippingContainer", "storageContainer", "trunk",
                },
                interactable);

            Assert.Equal(
                new[] { "mountedBox", "shippingContainer", "storageContainer", "trunk" },
                LoosePartCatalogue.All
                    .Where(def => PartInteractionPolicy.VerbFor(def.ItemType) == PartVerb.Inventory)
                    .Select(def => def.ItemType)
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToArray());
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
