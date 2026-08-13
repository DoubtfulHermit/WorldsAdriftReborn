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

        /// <summary>InteractVerb.Man = 3 (helm - served by the serializer's own isHelm branch).</summary>
        Man = 3,

        /// <summary>InteractVerb.Inventory = 4 (storage containers - not yet served, see policy).</summary>
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
    ///
    ///   SERVED ELSEWHERE (existing branches this policy must NOT catch):
    ///   * helm - Man, served by the serializer's dedicated isHelm branch and
    ///     handled by the flight service. Returns None here so the mounted-part
    ///     branch never double-serves it (the isHelm check runs first anyway).
    ///
    ///   READY-TO-IMPLEMENT (retail verb known; needs its state serve first):
    ///   * trunk/mountedBox/storageContainer/shippingContainer - Inventory
    ///     (ShipContainerPreprocessor.SetVerb(InteractVerb.Inventory)). BLOCKED on
    ///     serving 1081 InventoryState (+ inUseBy handshake + event_interact echo,
    ///     which InWorldInventoryVisualiser requires to open the UI). Advertising
    ///     the prompt before that serve exists would be a lie ("E does nothing"),
    ///     so None until then.
    ///   * personalReviver - Activate (GetTutorialStep -> MOUSE_OVER_REVIVER).
    ///     BLOCKED on serving 1094 RespawnPointState (owner/charge fields drive the
    ///     nameplate + gauge) and on a respawn flow that would give binding one any
    ///     meaning. None until then.
    ///   * atlasSkyCore - Activate (ShipCorePreprocessor.SetVerb(Activate)). The
    ///     shipped client has NO consumer of the resulting interact - core visuals
    ///     read ShipLiftState 1258 off the ship root - so retail's handler was
    ///     GSIM-side (core activation). None until the flight/lift feature wants it.
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
                    return PartVerb.Activate;
                default:
                    return PartVerb.None;
            }
        }
    }
}
