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
        /// THE POWER GENERATOR IS THE REFUEL DOOR, and unlike every other verdict in
        /// this file it did not have to be reconstructed - the shipped client already
        /// labels it. <c>PowerGenerator01_unityclient</c> bakes an
        /// <c>InteractiveObjectVisualizer</c> with <c>Verb = Activate (1)</c> and a
        /// <c>TutorialHelper</c> whose <c>_interactionStep</c> is
        /// <c>17 = MOUSE_OVER_GENERATOR</c>; the Activate arm of
        /// <c>GetTutorialStep</c> falls through sail/respawner/lamp/horn/
        /// ShipCoreVisualizer - the generator has none of them - to that step, whose
        /// overlay asset <c>STANDARD_MOUSE_OVER_GENERATOR</c> carries exactly one
        /// control: <c>{ Name: "Refuel", Hold: true, InputButtons: [Interact] }</c>.
        ///
        /// This entry replaced a wrong verdict ("no component, no verb") that survived
        /// because the decompile has no PowerGenerator preprocessor - a preprocessor
        /// is an EXPORT-TIME script, and the question was always what it left in the
        /// prefab. Two schematic keys share the one prefab, so BOTH rows must carry
        /// the verb or half the generators a player can craft are inert.
        /// </summary>
        [Theory]
        [InlineData("powerGenerator")]
        [InlineData("powerGenerator01")]
        public void ThePowerGeneratorAdvertisesTheRefuelItsPrefabAlreadyPromises(string itemType)
        {
            Assert.Equal(PartVerb.Activate, PartInteractionPolicy.VerbFor(itemType));
            Assert.Equal(PartVerb.Activate, PartInteractionPolicy.SeedVerbFor(itemType));

            // Mount-operated: the prompt must flip available on the mount and unmount
            // commits, or the first generator to gain the verb is seeded correctly and
            // stays available=false forever - the container bug, verbatim.
            Assert.True(PartInteractionPolicy.IsMountOperated(itemType));
            Assert.False(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, false));
            Assert.True(PartInteractionPolicy.IsSeededInteractionAvailable(itemType, true));
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
        /// <see cref="StorageContainersAdvertiseTheVerbTheirPrefabBakes"/>. The atlas
        /// sky core came off it when fuel gave its baked Activate something to do.
        /// </summary>
        [Theory]
        [InlineData("personalReviver")]
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
        /// the two power-generator rows, and Inventory only for the four storage
        /// containers. A new catalogue row
        /// defaults to None (safe - no prompt), and this test documents that adding an
        /// interactable one means extending the policy AND the service together.
        ///
        /// NINE is the whole answer to "which ship parts respond to E" and it is
        /// spelled out here rather than counted, so that a part quietly gaining or
        /// losing a prompt is a failing test rather than a live surprise.
        /// </summary>
        [Fact]
        public void WholeCatalogueAuditsToExactlyTheNineInteractableParts()
        {
            var interactable = LoosePartCatalogue.All
                .Where(def => PartInteractionPolicy.VerbFor(def.ItemType) != PartVerb.None)
                .Select(def => def.ItemType)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();

            // atlasSkyCore is NOT here, and powerGenerator(01) IS. The core joined
            // as the refuel door on the argument that its Activate was baked and
            // unclaimed; the verb is, but the PROMPT is not - the client's own overlay
            // asset spells it "Activate Atlas Pulse", which names a real retail action
            // (1306). A prompt whose label we cannot honour is the same lie as a verb
            // we cannot honour.
            //
            // The power generator has the opposite property and that is why refuel
            // lives there now: PowerGenerator01_unityclient bakes
            // InteractiveObjectVisualizer(Verb = Activate) plus a TutorialHelper
            // pointing at MOUSE_OVER_GENERATOR, and that overlay asset
            // (STANDARD_MOUSE_OVER_GENERATOR) carries exactly one control, reading
            // { Name: "Refuel", Hold: true }. The client labels the door for us.
            //
            // Anything appearing here without a 1211 handler behind it is a prompt
            // that lies.
            Assert.Equal(
                new[]
                {
                    "horn", "lamp", "mountedBox", "powerGenerator", "powerGenerator01",
                    "sail", "shippingContainer", "storageContainer", "trunk",
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
        // The sky core seeds PickUp like any other inert part: we advertise it no
        // Activate, because the client would label that Activate "Activate Atlas
        // Pulse" and we do not serve 1306.
        [InlineData("atlasSkyCore", PartVerb.PickUp)]
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

        /// <summary>
        /// The set whose availability DEPENDS on being mounted is exactly the set the
        /// mount and unmount commits must broadcast a 1210 flip for. These were two
        /// hand-written lists in three files and they drifted the first time a verb
        /// was added: a container was seeded, prompted and then left permanently
        /// unavailable, which is a chest that can never be opened with every test
        /// green. Asserting the predicate AGREES with the availability function is
        /// what makes one of them the source of truth.
        /// </summary>
        [Theory]
        [InlineData("helm", true)]
        [InlineData("sail", true)]
        [InlineData("lamp", true)]
        [InlineData("horn", true)]
        [InlineData("trunk", true)]
        [InlineData("mountedBox", true)]
        [InlineData("storageContainer", true)]
        [InlineData("shippingContainer", true)]
        [InlineData("deck", false)]
        [InlineData("altimeter", false)]
        public void MountOperatedPartsAreExactlyThoseAvailableOnlyWhenMounted(
            string itemType, bool mountOperated)
        {
            Assert.Equal(mountOperated, PartInteractionPolicy.IsMountOperated(itemType));
            Assert.Equal(mountOperated,
                PartInteractionPolicy.IsSeededInteractionAvailable(itemType, isMounted: true));
            Assert.Equal(!mountOperated,
                PartInteractionPolicy.IsSeededInteractionAvailable(itemType, isMounted: false));
        }

        /// <summary>
        /// Walked over the REAL catalogue, so a new row cannot slip past the theory
        /// above by not being listed in it.
        /// </summary>
        [Fact]
        public void EveryMountOperatedRowInTheCatalogueAgreesWithItsAvailability()
        {
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                Assert.Equal(
                    PartInteractionPolicy.IsMountOperated(part.ItemType),
                    PartInteractionPolicy.IsSeededInteractionAvailable(part.ItemType, isMounted: true));
            }
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
