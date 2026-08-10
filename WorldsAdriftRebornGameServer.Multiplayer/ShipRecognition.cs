namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The three components that make the client's OWN ShipVisualizer ENABLE on a
    /// server-spawned hull, plus the scalar values they carry.
    ///
    /// ShipVisualizer [Require]s exactly three readers and NOTHING from its base
    /// class (VERIFIED against the decompile: ShipVisualizer.cs fields, and
    /// docs/research/loop/data/req_shipframe.tsv):
    ///   8062 ShipOwnersDeprecatedState     - who owns the ship (deprecated path)
    ///   8071 ShipPartCountState            - how many of each part are bolted on
    ///   4349 ShipRegisteredCharactersState - the registered crew / revivers
    /// Until ALL THREE are present the injector leaves ShipVisualizer at
    /// m_Enabled = 0 and the hull is a bare frame the game does not recognise as a
    /// ship. A live session logged "unhandled component id 8062 / 8071 / 4349
    /// (entity 2)" - the client REQUESTED them over interest and the server had no
    /// branch, so it answered nothing and the visualizer never woke up.
    ///
    /// WHAT ENABLING IT BUYS, precisely. The player carry itself is driven by the
    /// hull's runtime PathFollower (seeded via 1130 + 190602), not by this
    /// visualizer - RepositionRelativeToGroundedObject gates on RelativePathFollower,
    /// not on IsRelativeToShip (VERIFIED, ClientAuthoritativePlayerMovement.cs
    /// :407-414, :447-510). What ShipVisualizer adds is RECOGNITION: with it enabled,
    /// GetComponentInParent&lt;ShipVisualizer&gt; on the grounded hull succeeds, so the
    /// client sets IsRelativeToShip and publishes the correct RelativeToShipUid, and
    /// the ship HUD / ownership / crew queries have real data. So these seeds make
    /// the ship a real recognised ship; they do not, by themselves, turn the carry
    /// on or off.
    ///
    /// 4349 also satisfies the ONLY [Require] of ShipRegisteredReviversVisualizer,
    /// which therefore enables too - it is a passive query object with NO OnEnable,
    /// so an empty crew list is completely safe (VERIFIED,
    /// ShipRegisteredReviversVisualizer.cs). No other visualizer on the prefab is
    /// satisfied by these three alone: every other one needs components we do not
    /// seed (1257, 1258, 1113, 1121, 1294, ...). Rule-7-safe.
    ///
    /// The values are "an UNOWNED, UNCREWED ship carrying the parts we actually bolt
    /// on": empty owners, empty crew, one Helm. None of them gates behaviour beyond
    /// the HUD.
    /// </summary>
    public static class ShipRecognition
    {
        /// <summary>
        /// The three recognition component ids, in a stable order. Appended to the
        /// hull's proactive seed set by
        /// <see cref="WorldEntities.HullSeedComponents"/> when recognition is on.
        /// </summary>
        public static readonly IReadOnlyList<uint> SeedComponents =
            new uint[] { 8062, 8071, 4349 };

        // 8071 ShipPartCountData only tracks Sail / Helm / Core / Respawner - the
        // deck and the hull are NOT part types in that enum
        // (ShipVisualizer.GetShipPartCount, VERIFIED). We always bolt exactly one
        // Helm; the cosmetic engine/sail are opt-in (WAREBORN_SHIP_PARTS) and are
        // deliberately NOT reflected, because 8071 is a single per-hull seed served
        // before we know which optional parts a given run enabled - and the count
        // feeds only the ship HUD, never the carry.
        public const uint AttachedSailCount = 0;
        public const uint AttachedHelmCount = 1;
        public const uint AttachedCoreCount = 0;
        public const uint AttachedRespawnerCount = 0;

        /// <summary>
        /// The ship mass carried in 8071 for the HUD's weight readout. Cosmetic:
        /// no client consumer of <c>ShipPartCountStateData.mass</c> gates behaviour
        /// (lift and complexity are 1258 ShipLiftState / 1257 ParentingMassAdderState,
        /// not this), and ShipVisualizer never reads it. Zero is the honest value for
        /// a hull whose real mass this server does not compute.
        /// </summary>
        public const float Mass = 0f;
    }
}
