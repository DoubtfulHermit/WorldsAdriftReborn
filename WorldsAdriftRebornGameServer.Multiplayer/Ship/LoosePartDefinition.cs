using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pure, engine-free description of ONE craftable ship PART as a LOOSE
    /// (unattached) world entity - the crafted-output counterpart of a
    /// <see cref="BuiltShipPlacement"/> hull. It carries only what the server must
    /// author onto the part's SpatialOS components so an unmodified client renders
    /// it, can lift it, and (for a lamp) lights it: the prefab to load, the logical
    /// attachment metadata (<c>1120 ShipPartState</c>), and the exact all-or-nothing
    /// seed component set.
    ///
    /// WHY A LOOSE PART IS A WORLD ENTITY, NOT AN INVENTORY FLAG. Original Worlds
    /// Adrift mounts a ship part by picking up an existing
    /// <c>ShipPartVisualizer</c> world entity in a shipyard and sending a
    /// <c>1070 PlacePart</c> event; the builder payload operates on an entity id, so
    /// a part MUST exist in the world before it can ever be mounted
    /// (findings-ship-components.md, "Phase 1"). This type is what a completed craft
    /// spawns.
    ///
    /// THE SEED SET IS ALL-OR-NOTHING AND MEASURED, NOT GUESSED. The client's
    /// interest batch on a ship-part entity is applied with
    /// failOnComponentInitError TRUE (the same rule the hull's seed obeys), so every
    /// id in <see cref="SeedComponents"/> MUST have a branch in
    /// <c>ComponentsSerializer</c> or the whole batch drops and the part renders
    /// fully but inert. The set is the UNION of the <c>[Require]</c> readers of the
    /// two visualizers that make a lamp a working, liftable part:
    ///   ShipPartVisualizer -> 8066, 1120, 190602, 190601, 1016, 1013
    ///     (ShipPartVisualizer.cs:22-38 - ShipRootState, ShipPartState,
    ///      TransformState, TransformHierarchyState, ItemHealthState,
    ///      CraftableSpawningState)
    ///   LampVisualizer     -> 1108, 1236, 1099
    ///     (LampVisualizer.cs:13-20 - LampState, IsTooDamagedToWorkState,
    ///      SalvageAndRepairState)
    /// The common base is shared by ANY ship part; the part-specific ids
    /// (<see cref="PartSpecificComponents"/>) are the lamp's 1108/1236. Everything
    /// else the prefab asks for (mass, lightning, deteriorate, collision) is served
    /// best-effort over interest or left dormant, exactly as the hull leaves its
    /// non-essential visualizers disabled.
    /// </summary>
    public sealed class LoosePartDefinition
    {
        /// <param name="schematicId">The recipe key this part is crafted from (the catalogue key, e.g. "lamp").</param>
        /// <param name="itemType">The 1120 itemType / salvage itemTypeId (e.g. "lamp").</param>
        /// <param name="title">The 1120 title shown to the player (e.g. "Lamp").</param>
        /// <param name="prefabName">
        /// The bare prefab/bundle name the client loads (worker suffix appended
        /// client-side). NOT recoverable from the client decompile - the client only
        /// ever reads it back from 1120.prefabName (ShipPartVisualizer.cs:94) - so it
        /// is a best-guess default, overridable at spawn time without a rebuild.
        /// </param>
        /// <param name="attachmentType">
        /// The 1120 attachmentType string, mapped by BuilderVisualizer.GetAttachmentType
        /// (one of none/side/deck/wing/deckGrid/deckForward/engine/shipSurfaces/coreModule;
        /// anything else degrades safely to None). Also server refdata, not in the
        /// decompile. Legacy "shipSurfaces" values are normalized to "deck" because
        /// reconstructed ships do not expose retail's Environment-layer skin; this
        /// affects placement snapping, not whether the part renders.
        /// </param>
        /// <param name="partSpecificComponents">
        /// The functional component ids unique to this part type (the lamp's
        /// 1108 LampState + 1236 IsTooDamagedToWorkState), appended to
        /// <see cref="BaseShipPartComponents"/> to form <see cref="SeedComponents"/>.
        /// </param>
        public LoosePartDefinition(
            string schematicId,
            string itemType,
            string title,
            string prefabName,
            string attachmentType,
            IReadOnlyList<uint> partSpecificComponents)
        {
            SchematicId = schematicId;
            ItemType = itemType;
            Title = title;
            PrefabName = prefabName;
            // The current built ship has a real ShipDeck placement collider but no
            // retail Environment-layer ShipSurfaces skin. Normalize here (rather than
            // only in the catalogue) so old loose/mounted records and live env
            // overrides cannot resurrect the one-incidental-frame placement bug.
            AttachmentType = PartMountSurfaces.NormalizeForBuiltShip(attachmentType);
            PartSpecificComponents = partSpecificComponents;
        }

        /// <summary>The recipe key this part is crafted from.</summary>
        public string SchematicId { get; }

        /// <summary>The 1120 itemType, also the 1099 salvage itemTypeId.</summary>
        public string ItemType { get; }

        /// <summary>The 1120 title.</summary>
        public string Title { get; }

        /// <summary>The bare prefab name the client loads for this part.</summary>
        public string PrefabName { get; }

        /// <summary>The 1120 attachmentType string.</summary>
        public string AttachmentType { get; }

        /// <summary>The functional component ids unique to this part type.</summary>
        public IReadOnlyList<uint> PartSpecificComponents { get; }

        /// <summary>
        /// The components EVERY loose ship part carries so
        /// <c>ShipPartVisualizer</c> enables (making it render/lift) plus its salvage
        /// base: 190602 TransformState (position, no ship parent), 190601
        /// TransformHierarchyState (empty children so it stays liftable), 1016
        /// ItemHealthState, 1099 SalvageAndRepairState, 1013 CraftableSpawningState
        /// (spawning=false so it is non-kinematic and pickable), 1120 ShipPartState
        /// (attached=false), 8066 ShipRootState (no ship). 190602 is FIRST: it is the
        /// position every other behaviour reads back, and the batch is applied in
        /// order.
        /// </summary>
        public static readonly IReadOnlyList<uint> BaseShipPartComponents =
            // 1246 is shared deliberately: many ship-part prefabs carry variation
            // visualizers below the root (and therefore outside a root-only prefab
            // census). A stable seed is harmless when unused and prevents a valid
            // crafted entity from retaining an invisible material/mesh child.
            new uint[] { 190602, 190601, 1016, 1099, 1013, 1120, 8066, 1246 };

        /// <summary>
        /// This part's full proactive, all-or-nothing seed set:
        /// <see cref="BaseShipPartComponents"/> followed by
        /// <see cref="PartSpecificComponents"/>. Every id has a ComponentsSerializer
        /// branch keyed on the LooseParts ledger; a missing one would drop the whole
        /// batch and leave the part inert.
        /// </summary>
        public IReadOnlyList<uint> SeedComponents =>
            BaseShipPartComponents.Concat(PartSpecificComponents).ToArray();
    }
}
