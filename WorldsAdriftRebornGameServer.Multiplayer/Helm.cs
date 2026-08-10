namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The one ship PART this server bolts onto the static hull so the ship can
    /// be interacted with: a <c>Helm01</c> carrying the "Man" interaction verb.
    ///
    /// Like <see cref="MetalNodes"/> this is a bag of VALUES kept out of the
    /// component serializer so each can be asserted on natively. Two of them do
    /// real work and fail silently if wrong:
    ///
    ///   * <see cref="ManRadius"/> - a zero radius means
    ///     InteractiveObjectVisualizer.OnEnable finds the verb entry but with
    ///     radius 0, and the prompt never appears. Same trap the nugget's PickUp
    ///     entry documents.
    ///   * the deck offset - the helm is its OWN entity with its OWN global
    ///     190602, NOT a child of the hull, so if this offset is wrong the helm
    ///     floats off the deck. It is derived from the hull's registration so the
    ///     two move together (see <see cref="WorldEntities.Helm"/>).
    ///
    /// VERIFIED (ilspycmd on Generated.Code.dll) that the Man verb exists and is
    /// distinct from PickUp: <c>Bossa.Travellers.Interact.InteractVerb</c> is
    /// { Default=0, Activate=1, PickUp=2, Man=3, ... }. The client bakes this verb
    /// into the Helm prefab itself (HelmPreprocessor.SetVerb(InteractVerb.Man));
    /// we seed the same 1210 InteractiveState the nugget does, with Man in place
    /// of PickUp, so the visualizer's Interactions.FirstOrDefault(i =&gt; i.verb ==
    /// Man) resolves to a real entry rather than default(InteractionEntry).
    /// </summary>
    public static class Helm
    {
        /// <summary>
        /// The bare prefab name. The client appends its own worker suffix
        /// (so "Helm01", never "Helm01_unityclient"). It resolves without an
        /// island manifest for the same reason ShipFrame does: ship-part prefabs
        /// are baked into the always-resident resources.assets, and the client's
        /// dispatch ignores prefab CONTEXT for every name that does not start with
        /// Traveller/ModalErrorPopup/Spectator.
        /// </summary>
        public const string AssetName = "Helm01";

        /// <summary>The helm's registration key. One helm, one ship, for now.</summary>
        public const string Key = "helm-haven";

        // ------------------------------------------------------------------
        // The 1210 InteractiveState seed values (the "Man" prompt).
        //
        // VERIFIED constructor shapes (ilspycmd on Generated.Code.dll), identical
        // to the nugget's PickUp entry - only the verb differs:
        //   InteractiveStateData(bool available, EntityId inUseBy,
        //                        List<InteractionEntry> interactions, bool syncSchematics)
        //   InteractionEntry(InteractVerb verb, float radius, bool lockOnUse,
        //                    string activatedByItem, string description,
        //                    string lockedDescription, bool exclusiveUse, float timeToUse)
        //   enum Bossa.Travellers.Interact.InteractVerb { Default, Activate,
        //                    PickUp, Man, Inventory, ... } -> Man = 3
        // ------------------------------------------------------------------

        /// <summary>
        /// 1210 InteractionEntry.radius, metres. Non-zero or no prompt appears -
        /// the exact trap MetalNodes.PickUpRadius documents. Matched to the
        /// nugget's 3 m so "how close do I have to be" is one number across the
        /// two interaction seeds this server sends.
        /// </summary>
        public const float ManRadius = 3.0f;

        /// <summary>
        /// 1210 InteractionEntry.timeToUse, seconds. A short hold. Manning the
        /// helm for real (Route B, 1211 TriggerInteractWithObject -&gt; a server
        /// worker writing 1109 PilotState) is step 6 and downstream of the ship
        /// agent; this value only shapes the prompt's fill animation.
        /// </summary>
        public const float ManTimeToUse = 0.5f;

        // ------------------------------------------------------------------
        // Where the helm sits, relative to the hull's own registration.
        //
        // The helm is a SEPARATE entity placed by its own 190602 in GLOBAL
        // coordinates - the player-on-a-deck relationship is 1073, and the
        // part-to-hull relationship is 8066, but NEITHER parents the helm's
        // transform, so nothing positions it except its own seed. It is offset
        // from the hull's registration so the two cannot drift apart.
        // ------------------------------------------------------------------

        /// <summary>
        /// Metres the helm is raised above the hull's registration Y. ZERO,
        /// because the hull's deck plane is at the hull entity's own local y = 0
        /// (findings-first-ship, "The hull root") - so the hull's world Y already
        /// IS the deck, and a helm whose base pivot sits on the deck wants no
        /// lift. Documented and separate so it is one edit to raise the helm if a
        /// live client shows it sunk.
        /// </summary>
        public const double DeckUpMetres = 0.0;

        /// <summary>
        /// Metres the helm is offset along +Z (north, the direction the hull was
        /// placed 12 m further along than the spawn) from the hull's registration.
        /// The one-cell hull is ~4 m fore-to-aft at the client's ShipScale = 2, so
        /// ±2 m stays on the deck; 1 m places the helm off dead-centre without
        /// leaving the plan. APPROXIMATE - the helm prefab's own pivot has not
        /// been eyeballed against a running client - and deliberately inside
        /// <see cref="ManRadius"/> of the deck centre so it is reachable however
        /// the player boards.
        /// </summary>
        public const double DeckForwardMetres = 1.0;

        /// <summary>
        /// The helm's global 190602 seed: the hull's registration plus the deck
        /// offset, in fixed point. A pure function of the hull position so
        /// <see cref="WorldEntities.Helm"/> and the hull stay locked together and
        /// the arithmetic is asserted in the tests rather than pasted as literals.
        /// </summary>
        public static FixedPointPosition OnDeckOf(FixedPointPosition hull)
        {
            return new FixedPointPosition(
                hull.X,
                hull.Y + (long)(DeckUpMetres * FixedPointPosition.UnitsPerMetre),
                hull.Z + (long)(DeckForwardMetres * FixedPointPosition.UnitsPerMetre));
        }
    }
}
