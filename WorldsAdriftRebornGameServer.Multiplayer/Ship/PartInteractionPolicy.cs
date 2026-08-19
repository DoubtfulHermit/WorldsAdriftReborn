namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The interact verb a MOUNTED part's 1210 prompt advertises, mirrored from the
    /// decompiled client's InteractVerb enum (Schema.Bossa.Travellers.Interact.
    /// InteractVerb) so this assembly stays free of Improbable types. Values are the
    /// WIRE values - the serializer casts them onto the real enum.
    /// </summary>
    public enum PartVerb
    {
        /// <summary>Not interactable - serve no verb entry for this part.</summary>
        None = -1,

        /// <summary>InteractVerb.Activate = 1 (sail furl, lamp switch, horn honk).</summary>
        Activate = 1,

        /// <summary>InteractVerb.PickUp = 2 (ordinary loose crafted parts).</summary>
        PickUp = 2,

        /// <summary>InteractVerb.Man = 3 (helm - served by the serializer's own isHelm branch).</summary>
        Man = 3,

        /// <summary>InteractVerb.Inventory = 4 (the four ship storage containers).</summary>
        Inventory = 4,
    }

    /// <summary>
    /// THE data-driven map from a mounted part's catalogue itemType to the interact
    /// verb its 1210 prompt advertises - the audit of "what did retail let you do
    /// with this part", pinned by tests, consumed by the 1210 serve branch.
    ///
    /// Every verdict below is from the decompiled client (ground truth), part by
    /// part:
    ///
    ///   IMPLEMENTED HERE (prompt served + 1211 Activate handled):
    ///   * sail  - Activate. The sail prefab's InteractiveObjectVisualizer carries
    ///     Verb=Activate serialized (GetTutorialStep: Activate + SailVisualizer ->
    ///     MOUSE_OVER_SAIL_OPEN/_CLOSE). Toggling 1303 unfurled drives the furl
    ///     animation via SailControlVisuals.LateUpdate polling.
    ///   * lamp  - Activate (GetTutorialStep: Activate + LampVisualizer ->
    ///     MOUSE_OVER_SWITCH_ON/_OFF). Toggling 1108 enabled switches the light
    ///     (LampVisualizer.OnUpdated) and plays Play_LightSwitch.
    ///   * horn  - Activate (GetTutorialStep -> MOUSE_OVER_HORN). The 1107
    ///     SoundHorn EVENT plays Play_Ship_Horn01 (HornVisualizer.OnSoundHorn);
    ///     30 s recharge (see Horns.RechargeSeconds).
    ///   * atlasSkyCore - Activate (ShipCorePreprocessor.SetVerb(Activate)). THE
    ///     REFUEL DOOR. Retail refuelled at a fuel TANK part, whose prefab this
    ///     client cannot resolve (no fuel tank in the 349-name entity-prefab
    ///     census), and a verb cannot be invented because
    ///     InteractiveObjectVisualizer caches the entry matching its PREFAB-BAKED
    ///     verb once in OnEnable. The sky core is the only ship part whose Activate
    ///     is baked, unused and unclaimed - so holding E on it moves fuel from the
    ///     player's inventory into the hull's tank. A deviation from retail, stated
    ///     as one: docs/plans/feature-roadmap.md 13.4.
    ///   * trunk / mountedBox / storageContainer / shippingContainer - Inventory
    ///     (ShipContainerPreprocessor.SetVerb(InteractVerb.Inventory)). Unblocked
    ///     by the loot-container 1081 work: the four rows now seed 1081 + 1236
    ///     (ShipContainers.RequiredComponents) so InWorldInventoryVisualiser and
    ///     IsTooDamagedToWorkVisualizer both enable, and the E press is answered
    ///     with the same Interact(Inventory) echo a ruin chest gets. See
    ///     <see cref="ShipContainers"/>.
    ///
    ///   SERVED ELSEWHERE (existing branches this policy must NOT catch):
    ///   * helm - Man, served by the serializer's dedicated isHelm branch and
    ///     handled by the flight service. Returns None here so the mounted-part
    ///     branch never double-serves it (the isHelm check runs first anyway).
    ///
    ///   READY-TO-IMPLEMENT (retail verb known; needs its state serve first):
    ///   * personalReviver - Activate (GetTutorialStep -> MOUSE_OVER_REVIVER).
    ///     BLOCKED on serving 1094 RespawnPointState (owner/charge fields drive the
    ///     nameplate + gauge) and on a respawn flow that would give binding one any
    ///     meaning. None until then.
    ///
    ///   NOT INTERACTABLE IN RETAIL (confirmed - preprocessors add no
    ///   InteractiveObjectVisualizer; these parts are automatic or passive):
    ///   * proceduralEngineDefault / proceduralWingDefault - driven by the helm
    ///     through 1116/1124; never E-interactable.
    ///   * the 8 sky core modules - passive animators reading 1236 + ship 1258.
    ///   * altimeter/fuelGauge/headingIndicator/artificialHorizon/
    ///     airspeedIndicator - fully client-local physics readouts, gated only on
    ///     1236 is_functional.
    ///   * powerGenerator(01) - "generator" as a ship part is the sky core module;
    ///     no component, no verb.
    ///   * deck/stairs/railing(Corner)/smallPanel/mediumPanel/largePanel/window/
    ///     cupboard/barrel - pure structure/decoration (ShipPartPreprocessor adds
    ///     damage/salvage/placement only). Retail chairs/stools DID bake Man
    ///     (ShipFurniturePreprocessor) but none of those are in our catalogue.
    /// </summary>
    public static class PartInteractionPolicy
    {
        /// <summary>
        /// 1210 InteractionEntry.radius for the Activate parts, metres. Non-zero or
        /// the prompt never appears (the MetalNodes.PickUpRadius trap); 5 m covers a
        /// mast's own height so the prompt reaches the deck the player stands on.
        /// </summary>
        public const float ActivateRadius = 5f;

        /// <summary>
        /// 1210 InteractionEntry.timeToUse for the Activate parts. Zero = instant:
        /// TimedInteractionController fires immediately below 0.001, no hold bar -
        /// a light switch, not a crafting channel.
        /// </summary>
        public const float ActivateTimeToUse = 0f;

        /// <summary>
        /// The verb a mounted part of this catalogue itemType advertises on its
        /// 1210, or <see cref="PartVerb.None"/> when it must not advertise one
        /// (not interactable in retail, or its interaction is not implementable
        /// yet - see the class remarks for the per-part verdicts).
        /// </summary>
        public static PartVerb VerbFor(string? itemType)
        {
            switch (itemType)
            {
                case "sail":
                case "lamp":
                case "horn":
                case "atlasSkyCore":
                    return PartVerb.Activate;
                default:
                    // The four storage containers, keyed off the same table that
                    // owns their grid so a fifth container row cannot be added with
                    // a capacity but no prompt (or the reverse).
                    return ShipContainers.IsContainer(itemType)
                        ? PartVerb.Inventory
                        : PartVerb.None;
            }
        }

        /// <summary>
        /// The verb that must be present when a crafted part is FIRST checked out.
        /// InteractiveObjectVisualizer caches the entry matching its prefab-baked verb
        /// only in OnEnable; changing the list after mounting does not refresh that cache.
        /// Therefore future-interactable parts carry their baked verb even while loose,
        /// with availability gating whether it can actually be used.
        /// </summary>
        public static PartVerb SeedVerbFor(string? itemType)
        {
            if (itemType == "helm")
            {
                return PartVerb.Man;
            }

            PartVerb mountedVerb = VerbFor(itemType);
            return mountedVerb != PartVerb.None ? mountedVerb : PartVerb.PickUp;
        }

        /// <summary>
        /// Whether the seeded interaction is usable in the part's current attachment
        /// state. Helms/sails/lamps/horns/containers/sky cores operate only when
        /// mounted; ordinary parts can be picked up only while loose.
        ///
        /// A CONTAINER IS DELIBERATELY MOUNTED-ONLY, and it is the one entry here
        /// whose reason is not "retail did it that way". A loose part is lifted and
        /// re-spawned by the scanner, and salvaging one destroys the entity - so an
        /// openable loose trunk would be a place a player could put items and then
        /// lose them by picking the trunk up. Bolt it down first and the only route
        /// to the same loss is the salvage beam, which
        /// <see cref="ShipPartSalvagePolicy"/> refuses while the container holds
        /// anything.
        /// </summary>
        public static bool IsSeededInteractionAvailable(string? itemType, bool isMounted)
        {
            return IsMountOperated(itemType) ? isMounted : !isMounted;
        }

        /// <summary>
        /// Whether this part's seeded interaction only works once it is bolted down -
        /// and therefore whether the mount and unmount commits must BROADCAST an
        /// availability flip on its 1210.
        ///
        /// THIS EXISTS BECAUSE THE SET WAS WRITTEN OUT BY HAND IN THREE PLACES AND
        /// IMMEDIATELY DRIFTED. <c>PartMountService</c> tested
        /// <c>Man || Activate</c> at both the mount and the unmount seam, so the first
        /// container to gain the Inventory verb was seeded correctly, prompted
        /// correctly, and stayed <c>available=false</c> forever - a chest that could
        /// never be opened, with every test green and nothing logged. One predicate,
        /// consumed by all three, is the only shape that cannot do that again.
        /// </summary>
        public static bool IsMountOperated(string? itemType)
        {
            PartVerb verb = SeedVerbFor(itemType);
            return verb == PartVerb.Man
                || verb == PartVerb.Activate
                || verb == PartVerb.Inventory;
        }
    }
}
