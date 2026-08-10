namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The DECISION behind an 8066 <c>ShipRootState</c> seed, kept pure so it can
    /// be asserted on without the game assemblies.
    ///
    /// VERIFIED (ilspycmd on Generated.Code.dll):
    ///   struct ShipRootStateData { Option&lt;EntityId&gt; shipRoot; bool isRoot; }
    ///   ShipRootState.Data(Option&lt;EntityId&gt; shipRoot, bool isRoot)
    /// and it is SERVER-WRITTEN ONLY - the reader (ShipRootState.Reader) has no
    /// Trigger* command, so no client ever authors it. Its sole consumer is
    /// Assets.Scripts.Visualisers.Ship.ShipPartVisualizer.ShipEntityId, which
    /// returns <c>shipRoot.Value</c> (or InvalidEntityId when the option is
    /// absent) - i.e. a bolted-on part reads THIS to learn which hull it belongs
    /// to. That is exactly the N+1-entities-linked-by-8066 model in
    /// findings-first-ship.
    ///
    /// This struct carries only primitives; the component serializer turns it
    /// into the Improbable <c>Option&lt;EntityId&gt;</c>/<c>ShipRootState.Data</c>,
    /// because those types live in the game assembly the pure layer must not
    /// reference.
    ///
    /// NOTE ON CURRENT REACH. Only <see cref="Part"/> is exercised today: the
    /// client requests 8066 for a Helm01 (ShipPartVisualizer's [Require] reader),
    /// never for the ShipFrame hull (which carries no ShipPartVisualizer). The
    /// hull's static-hull seed set is the sibling ship agent's, and is not
    /// widened here. <see cref="Root"/> exists so that IF the hull ever seeds
    /// 8066 - a one-line change there - the isRoot=true value is already defined
    /// and tested rather than invented under pressure.
    /// </summary>
    public readonly struct ShipRootSeed
    {
        private ShipRootSeed(bool isRoot, bool hasShipRoot, long shipRootEntityId)
        {
            IsRoot = isRoot;
            HasShipRoot = hasShipRoot;
            ShipRootEntityId = shipRootEntityId;
        }

        /// <summary>The 8066 <c>isRoot</c> field: true on the hull, false on a bolted-on part.</summary>
        public bool IsRoot { get; }

        /// <summary>
        /// Whether the 8066 <c>shipRoot</c> Option is present. A part always points
        /// at its hull; the hull points at itself, so this is true in both cases
        /// here - but the field exists because the schema's option CAN be absent
        /// (ShipPartVisualizer.ShipEntityId returns InvalidEntityId for that), and
        /// a seed builder that could not express absence would be lying about the
        /// shape.
        /// </summary>
        public bool HasShipRoot { get; }

        /// <summary>
        /// The entity id <c>shipRoot</c> points at when <see cref="HasShipRoot"/> is
        /// true: the hull's own entity id, whether this seed is for the hull or a
        /// part of it.
        /// </summary>
        public long ShipRootEntityId { get; }

        /// <summary>
        /// The hull's own 8066. isRoot=true, and shipRoot points at ITSELF rather
        /// than being absent, so any code that resolves a part-or-root's shipRoot
        /// (ShipPartVisualizer.GetShipPartVisualizer walks shipRoot.Value) lands on
        /// the hull whether it started from a part or from the hull. Not reached
        /// today - see the type remarks.
        /// </summary>
        public static ShipRootSeed Root(long hullEntityId) =>
            new ShipRootSeed(isRoot: true, hasShipRoot: true, shipRootEntityId: hullEntityId);

        /// <summary>
        /// A bolted-on part's 8066: isRoot=false, shipRoot pointing at the hull.
        /// This is what makes the Helm a member of the ship rather than a free
        /// entity that happens to sit on the deck.
        /// </summary>
        public static ShipRootSeed Part(long hullEntityId) =>
            new ShipRootSeed(isRoot: false, hasShipRoot: true, shipRootEntityId: hullEntityId);
    }
}
