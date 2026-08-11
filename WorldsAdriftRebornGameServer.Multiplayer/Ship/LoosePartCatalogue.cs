namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pure map from a crafted recipe id to the LOOSE ship part it produces.
    /// One part is wired end-to-end for this milestone - the LAMP, the lowest
    /// physics-risk part per findings-ship-components.md - so this catalogue has one
    /// entry; adding the next part (helm, horn, ...) is one more definition and one
    /// more <see cref="ForSchematic"/> case, no new machinery.
    ///
    /// KEPT SEPARATE FROM THE RECIPE CATALOGUE. The recipe itself (materials, title,
    /// category "Shipyard") lives in the coordinator-owned schematicData.json and is
    /// served to the client over 1097; this file only answers the server-side
    /// question "when this recipe completes, what world part do I spawn, and with
    /// what 1120 metadata". The lamp's schematicId/itemType/title mirror that
    /// catalogue entry so the two agree, but nothing here edits it.
    ///
    /// PREFAB + ATTACHMENT ARE BEST-GUESS DEFAULTS. Neither the lamp's prefab name
    /// nor its attachmentType string is present in the client decompile (the client
    /// only reads them back from 1120), so they are the two values most likely to be
    /// wrong against a running client. They are overridable at spawn time via
    /// environment variables (see <c>Game.Crafting.LoosePartSpawner</c>) so a live
    /// mismatch is a config change, not a rebuild.
    /// </summary>
    public static class LoosePartCatalogue
    {
        /// <summary>The lamp recipe key, matching the "lamp" entry in schematicData.json.</summary>
        public const string LampSchematicId = "lamp";

        /// <summary>The lamp's best-guess bare prefab name (worker suffix appended client-side).</summary>
        public const string LampDefaultPrefab = "Lamp";

        /// <summary>
        /// The lamp's best-guess attachmentType. A decorative lamp is a
        /// surface-mounted part, and "shipSurfaces" is a valid
        /// BuilderVisualizer.GetAttachmentType string; an unrecognised value would
        /// still render and lift, degrading only placement snapping to None.
        /// </summary>
        public const string LampDefaultAttachment = "shipSurfaces";

        /// <summary>
        /// The lamp as a loose world part. Its functional seeds are 1108 LampState +
        /// 1236 IsTooDamagedToWorkState (the LampVisualizer requires beyond the shared
        /// ship-part base). Pure - the prefab/attachment overrides are applied by the
        /// impure spawner, so this default is deterministic for tests.
        /// </summary>
        public static LoosePartDefinition Lamp =>
            new LoosePartDefinition(
                schematicId: LampSchematicId,
                itemType: "lamp",
                title: "Lamp",
                prefabName: LampDefaultPrefab,
                attachmentType: LampDefaultAttachment,
                partSpecificComponents: new uint[] { 1108, 1236 });

        /// <summary>Whether this recipe produces a loose ship part (vs an inventory item or a hull).</summary>
        public static bool IsLoosePart(string? schematicId)
        {
            return schematicId == LampSchematicId;
        }

        /// <summary>
        /// The loose part a recipe produces, or null when the recipe is not a
        /// loose-part craft (a normal inventory item, or the separate hull-blueprint
        /// flow). The caller spawns the returned definition on craft completion.
        /// </summary>
        public static LoosePartDefinition? ForSchematic(string? schematicId)
        {
            return schematicId == LampSchematicId ? Lamp : null;
        }
    }
}
