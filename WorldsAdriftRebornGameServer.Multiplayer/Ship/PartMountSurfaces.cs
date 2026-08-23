namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The client <c>PlacementLocationType</c> a loose part's <c>1120 attachmentType</c>
    /// resolves to - a PURE mirror of the two decompiled client steps that together decide
    /// WHICH ship surface the unmodified client raycasts the instant a part is lifted:
    /// <c>BuilderVisualizer.GetAttachmentType(string)</c> (the string -&gt;
    /// <c>AttachmentType</c> map) and <c>ShipPartPlacement.DeterminePlacementType</c>
    /// (<c>AttachmentType</c> -&gt; <c>PlacementLocationType</c>).
    ///
    /// WHY THE SERVER CARES ABOUT A CLIENT-ONLY CHOICE. The server never sees Unity layers,
    /// but it AUTHORS the <c>attachmentType</c> string (<see cref="LoosePartDefinition"/>),
    /// and that string is the whole reason a helm can - or cannot - be placed across the
    /// deck. A <see cref="PartMountSurface.ShipDeck"/> part raycasts the walkable deck
    /// collider (<c>Layers.ShipAttachmentSolid</c>, tag <c>"ShipDeck"</c>) that our built
    /// ship's <see cref="Multiplayer.Deck"/> presents, so it mounts anywhere on the deck;
    /// a <see cref="PartMountSurface.ShipSurfaces"/> part raycasts <c>Layers.Environment</c>
    /// with no tag and never hits that deck at all. Pinning this map lets the
    /// "helm mounts across the whole deck, not one spot" contract be asserted natively
    /// instead of on a running client.
    /// </summary>
    public enum PartMountSurface
    {
        /// <summary>Unknown / unrecognised attachmentType - degrades to None (no ship surface).</summary>
        None,

        /// <summary>The hull SIDE (panels, engines, wings). Raycast <c>Layers.ShipAttachable</c>.</summary>
        ShipSide,

        /// <summary>The walkable DECK (helm, rails, decorations). Raycast <c>Layers.ShipAttachmentSolid</c>, tag <c>"ShipDeck"</c>.</summary>
        ShipDeck,

        /// <summary>An arbitrary object surface. Raycast <c>Layers.Environment</c>.</summary>
        Entity,

        /// <summary>Any ship SURFACE (instruments, horns). Raycast <c>Layers.Environment</c>, NO tag - does NOT hit the ShipDeck collider.</summary>
        ShipSurfaces,

        /// <summary>A frame DECK GRID cell. Raycast <c>Layers.ShipAttachment</c>.</summary>
        DeckGrid,

        /// <summary>A sky-core module slot.</summary>
        CoreModule,
    }

    /// <summary>
    /// The pure attachmentType -&gt; <see cref="PartMountSurface"/> resolver. Kept engine-free
    /// so the exact client contract (which surface a given part raycasts) is unit-tested,
    /// not discovered on a live client.
    /// </summary>
    public static class PartMountSurfaces
    {
        /// <summary>
        /// Converts legacy generic-surface metadata into a surface the reconstructed
        /// built ship actually exposes. Retail's <c>shipSurfaces</c> path raycasts the
        /// Environment layer; our generated hull/deck presents its usable placement
        /// area as <c>ShipAttachmentSolid</c>. Keeping the legacy value therefore makes
        /// decorations land only on incidental frame colliders. Normalize it to the
        /// deck for both newly crafted definitions and persisted pre-fix records.
        /// Specialized side, grid, engine, wing, and core-module attachments retain
        /// their authored placement mechanics.
        /// </summary>
        /// <param name="itemType">
        /// The catalogue row's itemType. A FLIGHT INSTRUMENT is exempt: it is the one
        /// class of part that is MEANT to land on something already bolted down rather
        /// than on the hull's own skin, and retail says so in
        /// <c>acs/ShipInstrument.cs</c> by giving instruments - and only instruments -
        /// an overlap exemption for anything attached to a ship. The rewrite below
        /// exists for the helm and for structure, whose <c>shipSurfaces</c> raycast
        /// really does find nothing on our generated hull; it must not also drag
        /// instruments onto the floor. See <see cref="ShipInstruments"/> for the whole
        /// argument, the trade it costs, and the one line that reverses it.
        /// </param>
        public static string NormalizeForBuiltShip(string? attachmentType, string? itemType = null)
        {
            if (ShipInstruments.IsInstrument(itemType))
            {
                // Migrate both freshly-authored definitions and persisted pre-SC5
                // records. Existing instruments were saved as "deck" while Bar Pipes
                // still used the no-parent "~" transform; after a restart they must
                // receive the same retail surface as a newly-crafted instrument or a
                // player would have to throw the old gauges away and recraft them.
                return ShipInstruments.MountSurface;
            }
            return attachmentType == "shipSurfaces" ? "deck" : attachmentType ?? "none";
        }

        /// <summary>
        /// The surface a part with this <c>1120 attachmentType</c> mounts on, mirroring
        /// <c>BuilderVisualizer.GetAttachmentType</c> + <c>ShipPartPlacement
        /// .DeterminePlacementType</c>. An unrecognised string is
        /// <see cref="PartMountSurface.None"/>, exactly as the client's <c>_ =&gt;
        /// AttachmentType.None</c> fallthrough (a part that then mounts on nothing).
        ///
        /// Note the client maps <c>DeckForward</c> to the ShipDeck surface too, and a
        /// <c>Wing</c> to <c>ShipSide</c> in <c>DeterminePlacementType</c> (its
        /// <c>ShipSide | ShipDeck</c> widening is a separate runtime Hack in
        /// <c>StartPlacing</c>, not the base type map, so it is not encoded here).
        /// </summary>
        public static PartMountSurface ForAttachmentType(string? attachmentType)
        {
            return attachmentType switch
            {
                "none" => PartMountSurface.None,
                "side" => PartMountSurface.ShipSide,
                "deck" => PartMountSurface.ShipDeck,
                "deckForward" => PartMountSurface.ShipDeck,
                "deckGrid" => PartMountSurface.DeckGrid,
                "engine" => PartMountSurface.ShipSide,
                "wing" => PartMountSurface.ShipSide,
                "shipSurfaces" => PartMountSurface.ShipSurfaces,
                "coreModule" => PartMountSurface.CoreModule,
                _ => PartMountSurface.None,
            };
        }

        /// <summary>
        /// Whether a part with this attachmentType raycasts the walkable DECK collider our
        /// built ship presents (the <see cref="Multiplayer.Deck"/> ShipAttachmentSolid box).
        /// This is the "can it be placed across the deck at all" fact: TRUE for a helm now
        /// that it is <c>"deck"</c>, FALSE for the old <c>"shipSurfaces"</c> best-guess that
        /// raycast the Environment layer and only landed on one incidental spot.
        /// </summary>
        public static bool MountsOnDeckSurface(string? attachmentType)
        {
            return ForAttachmentType(attachmentType) == PartMountSurface.ShipDeck;
        }
    }
}
