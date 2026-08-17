using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer.Persistence
{
    /// <summary>
    /// One deployed structure a player placed and that must reappear in the same
    /// spot after a restart - a shipyard today, any <c>Deployables</c> row tomorrow.
    /// It is the durable form of a <c>Placement.PlacedShipyards</c> entry plus the
    /// placement transform the in-memory ledger never kept, and it carries EXACTLY
    /// what the spawn path needs to rebuild an identical world entity: the item type
    /// (which resolves the asset + seed component set via <c>Deployables</c>), the
    /// position and packed rotation the player chose, and the owner.
    ///
    /// The entity id is deliberately NOT stored. Entity ids only need to agree across
    /// clients WITHIN a session; a restored deployable is allocated a fresh id at
    /// boot and its ledger keys on that, so persisting the old id would be a value
    /// nothing could ever match again.
    ///
    /// A plain settable-property record because it is round-tripped by
    /// <see cref="System.Text.Json"/>, which needs a parameterless constructor and
    /// public setters to rehydrate it.
    /// </summary>
    public sealed class PlacedDeployableRecord
    {
        /// <summary>The crafted item type that deployed this (e.g. "shipyard"). Resolves the asset + seeds.</summary>
        public string ItemTypeId { get; set; } = "";

        /// <summary>Placement position, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long X { get; set; }

        /// <summary>Placement position, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long Y { get; set; }

        /// <summary>Placement position, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long Z { get; set; }

        /// <summary>The packed <c>Quaternion32</c> facing the player placed it at.</summary>
        public uint PackedRotation { get; set; }

        /// <summary>The character uid of the player who placed it (may be empty on a session-key placement).</summary>
        public string OwnerCharacterUid { get; set; } = "";

        /// <summary>The placement position as a <see cref="FixedPointPosition"/>.</summary>
        public FixedPointPosition Position() => new FixedPointPosition(X, Y, Z);
    }

    /// <summary>
    /// One ship a player built and that must reappear after a restart. It stores the
    /// hull's own position (the deck's is derived from it exactly as at build time,
    /// <c>BuiltShipPlacement.DeckOn</c>) and the hull geometry bytes the 1209 serves,
    /// base64-encoded by <see cref="System.Text.Json"/> so the file stays a single
    /// human-openable document. Like a placed deployable, no entity id is stored.
    /// </summary>
    public sealed class BuiltShipRecord
    {
        /// <summary>
        /// Stable-index tombstone. Mounted parts refer to this list index, so salvaging
        /// cannot compact the list; restore skips this record and new ships append.
        /// </summary>
        public bool Salvaged { get; set; }

        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullX { get; set; }

        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullY { get; set; }

        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullZ { get; set; }

        /// <summary>Level heading at the last authoritative flight save, radians.</summary>
        public double HullYawRadians { get; set; }

        /// <summary>The hull geometry blob the 1209 CustomShipHullState serves (base64 in JSON).</summary>
        public byte[] HullBytes { get; set; } = System.Array.Empty<byte>();

        /// <summary>
        /// The character uid of the ship's owner - the shipyard owner who built it. The
        /// yard's own 1205 registration grants build access; this keeps the ship's record
        /// owned like the deployables and round-trips the owner across restart. Empty for
        /// a legacy record written before ownership threading.
        /// </summary>
        public string OwnerCharacterUid { get; set; } = "";

        /// <summary>
        /// The position of the SHIPYARD that built this ship, in Q52.12 fixed-point units.
        /// The shipyard restores as its own deployable at this exact position, so on boot
        /// the ship is re-DOCKED to the deployable found there. Without this link a
        /// restored shipyard has no docked ship, and the client's
        /// <c>PlayerScannerTool.IsShipyardActive()</c> (= <c>DockedShip != null</c>) reports
        /// it INACTIVE - the "nearby shipyard is inactive" bug. Zero for a legacy record
        /// written before the dock-link was persisted (that ship restores un-docked).
        /// </summary>
        public long ShipyardX { get; set; }

        /// <summary>The shipyard-centre position, in Q52.12 fixed-point units.</summary>
        public long ShipyardY { get; set; }

        /// <summary>The shipyard-centre position, in Q52.12 fixed-point units.</summary>
        public long ShipyardZ { get; set; }

        // ------------------------------------------------------------------
        // WHAT THE SHIP IS MADE OF. Four additive fields, chosen so that their JSON
        // defaults ("" and 0) mean exactly the LEGACY behaviour - the same pattern
        // SailUnfurled and LampOff use below. System.Text.Json leaves an absent
        // member at its initializer, so every ship written before this change loads
        // with empty ids, and Materials() restates it as the birch-and-iron it has
        // always actually been (the server hardcoded Deck.MaterialTypeId = "birch"
        // and mapped "Metal" -> "iron"). No migration pass, no reordering of
        // BuiltShips (MountedPartRecord.BuiltShipIndex references that order), and
        // an older binary reading a newer file simply ignores the extra members.
        // ------------------------------------------------------------------

        /// <summary>The itemTypeId of the wood the frame was built from. Empty for a legacy record.</summary>
        public string HullWoodId { get; set; } = "";

        /// <summary>The itemTypeId of the metal the fittings were built from. Empty for a legacy record.</summary>
        public string HullMetalId { get; set; } = "";

        /// <summary>Quality 1..10 of the wood; 0 for a legacy record.</summary>
        public int HullWoodQuality { get; set; }

        /// <summary>Quality 1..10 of the metal; 0 for a legacy record.</summary>
        public int HullMetalQuality { get; set; }

        /// <summary>
        /// What this hull is made of, with a legacy record restated as birch+iron so
        /// it keeps exactly the mass and appearance it has today.
        /// </summary>
        public Materials.HullMaterials Materials() =>
            new Materials.HullMaterials(HullWoodId, HullWoodQuality, HullMetalId, HullMetalQuality).OrLegacy();

        /// <summary>Records what a completed craft actually consumed.</summary>
        public void SetMaterials(Materials.HullMaterials materials)
        {
            if (materials == null)
            {
                return;
            }
            HullWoodId = materials.WoodId ?? "";
            HullMetalId = materials.MetalId ?? "";
            HullWoodQuality = materials.WoodId == null ? 0 : materials.WoodQuality;
            HullMetalQuality = materials.MetalId == null ? 0 : materials.MetalQuality;
        }

        /// <summary>The hull position as a <see cref="FixedPointPosition"/>.</summary>
        public FixedPointPosition HullPosition() => new FixedPointPosition(HullX, HullY, HullZ);

        public void UpdatePose(FixedPointPosition position, double yawRadians)
        {
            HullX = position.X;
            HullY = position.Y;
            HullZ = position.Z;
            HullYawRadians = double.IsFinite(yawRadians) ? yawRadians : 0.0;
        }

        public void DockTo(FixedPointPosition shipyardPosition)
        {
            ShipyardX = shipyardPosition.X;
            ShipyardY = shipyardPosition.Y;
            ShipyardZ = shipyardPosition.Z;
        }

        /// <summary>
        /// The building shipyard's position as a <see cref="FixedPointPosition"/>, or null
        /// for a legacy record with no persisted dock link (all-zero).
        /// </summary>
        public FixedPointPosition? ShipyardPosition() =>
            (ShipyardX == 0 && ShipyardY == 0 && ShipyardZ == 0)
                ? (FixedPointPosition?)null
                : new FixedPointPosition(ShipyardX, ShipyardY, ShipyardZ);

        /// <summary>
        /// Removes the current dock link after the ship leaves its shipyard. The record
        /// itself remains at the same stable list index (mounted parts reference that
        /// index); a later successful capture may set a new dock link with DockTo.
        /// </summary>
        public void ClearShipyardDock()
        {
            ShipyardX = 0;
            ShipyardY = 0;
            ShipyardZ = 0;
        }
    }

    /// <summary>
    /// One crafted-but-unmounted (LOOSE) ship part that must reappear after a restart.
    /// It is the durable form of a <c>Crafting.LooseParts</c> ledger entry plus the
    /// spawn transform the in-memory ledger never kept, and it carries EXACTLY what
    /// <c>LoosePartSpawner.Restore</c> needs to rebuild a byte-identical loose part: the
    /// full <see cref="LoosePartDefinition"/> fields (so the restore is identical even
    /// when a live env-override changed the prefab/attachment), the world-absolute
    /// position and packed rotation it spawned at, the owner (the crafter's character
    /// uid), and a stable <see cref="PartUid"/>.
    ///
    /// WHY A STABLE PartUid AND NOT THE ENTITY ID. Entity ids only agree within a
    /// session; a restored part is allocated a fresh id at boot. The <see cref="PartUid"/>
    /// is a durable, cross-restart identity so a loose part that later becomes MOUNTED
    /// can have its <see cref="LoosePartRecord"/> removed and re-expressed as a
    /// <see cref="MountedPartRecord"/> without guessing which record is which.
    /// </summary>
    public sealed class LoosePartRecord
    {
        /// <summary>Stable cross-restart identity, correlating a loose part with its mount transition.</summary>
        public string PartUid { get; set; } = "";

        /// <summary>The recipe/catalogue key this part was crafted from.</summary>
        public string SchematicId { get; set; } = "";

        /// <summary>The 1120 itemType / 1099 salvage itemTypeId (the EFFECTIVE value spawned).</summary>
        public string ItemType { get; set; } = "";

        /// <summary>The 1120 title shown to the player.</summary>
        public string Title { get; set; } = "";

        /// <summary>The EFFECTIVE prefab the client loads (post env-override), so restore is identical.</summary>
        public string PrefabName { get; set; } = "";

        /// <summary>The EFFECTIVE 1120 attachmentType string (post env-override).</summary>
        public string AttachmentType { get; set; } = "";

        /// <summary>The part-specific functional component ids (e.g. the lamp's 1108/1236).</summary>
        public uint[] PartSpecificComponents { get; set; } = System.Array.Empty<uint>();

        /// <summary>Spawn position, world-absolute, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long X { get; set; }

        /// <summary>Spawn position, world-absolute, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long Y { get; set; }

        /// <summary>Spawn position, world-absolute, in Q52.12 fixed-point units (the 190602 seed).</summary>
        public long Z { get; set; }

        /// <summary>The packed <c>Quaternion32</c> facing (identity for a loose part that chose none).</summary>
        public uint PackedRotation { get; set; } = Placement.Quaternion32Packing.Identity;

        /// <summary>The character uid of the player who crafted it.</summary>
        public string OwnerCharacterUid { get; set; } = "";

        /// <summary>The spawn position as a <see cref="FixedPointPosition"/>.</summary>
        public FixedPointPosition Position() => new FixedPointPosition(X, Y, Z);

        /// <summary>Rebuilds the exact <see cref="LoosePartDefinition"/> this part spawned from.</summary>
        public LoosePartDefinition Definition() => new LoosePartDefinition(
            SchematicId, ItemType, Title, PrefabName, AttachmentType, PartSpecificComponents);
    }

    /// <summary>
    /// One loose part MOUNTED onto a built ship that must reappear after a restart,
    /// ALREADY ATTACHED and in the same place on its ship. It is the durable form of a
    /// <c>Crafting.MountedParts</c> ledger entry, tied to its ship by
    /// <see cref="BuiltShipIndex"/> - the position of the owning ship in
    /// <see cref="WorldStateSnapshot.BuiltShips"/>, which is stable because ships are
    /// only ever appended and restored in that same order. Restore looks the index back
    /// up to the ship's FRESH boot hull entity id and seeds the part riding it.
    ///
    /// It carries the full part definition (to re-spawn the part entity), the hull-local
    /// mount transform (offset + packed rotation), the owner, and the same
    /// <see cref="PartUid"/> the loose record had, so the two never double-spawn.
    /// </summary>
    public sealed class MountedPartRecord
    {
        /// <summary>Stable cross-restart identity (the same PartUid the loose record carried).</summary>
        public string PartUid { get; set; } = "";

        /// <summary>Index into <see cref="WorldStateSnapshot.BuiltShips"/> of the ship this rides.</summary>
        public int BuiltShipIndex { get; set; }

        /// <summary>The recipe/catalogue key this part was crafted from.</summary>
        public string SchematicId { get; set; } = "";

        /// <summary>The 1120 itemType / 1099 salvage itemTypeId.</summary>
        public string ItemType { get; set; } = "";

        /// <summary>The 1120 title.</summary>
        public string Title { get; set; } = "";

        /// <summary>The EFFECTIVE prefab the client loads.</summary>
        public string PrefabName { get; set; } = "";

        /// <summary>The EFFECTIVE 1120 attachmentType string.</summary>
        public string AttachmentType { get; set; } = "";

        /// <summary>The part-specific functional component ids.</summary>
        public uint[] PartSpecificComponents { get; set; } = System.Array.Empty<uint>();

        /// <summary>Hull-LOCAL mount offset, in Q52.12 fixed-point units (the 190602 Parent(hull,"~") offset).</summary>
        public long LocalX { get; set; }

        /// <summary>Hull-LOCAL mount offset, in Q52.12 fixed-point units.</summary>
        public long LocalY { get; set; }

        /// <summary>Hull-LOCAL mount offset, in Q52.12 fixed-point units.</summary>
        public long LocalZ { get; set; }

        /// <summary>The packed <c>Quaternion32</c> hull-local rotation the player placed the part at.</summary>
        public uint PackedRotation { get; set; } = Placement.Quaternion32Packing.Identity;

        /// <summary>The character uid of the player who mounted it.</summary>
        public string OwnerCharacterUid { get; set; } = "";

        /// <summary>
        /// SAIL ONLY: whether the sail's canvas is out (1303 unfurled), so a relog /
        /// restart restores the rigging a player set. False for every other part type
        /// and for legacy records (JSON default), which is exactly the fresh-mount
        /// state - so an old save loads unchanged.
        /// </summary>
        public bool SailUnfurled { get; set; }

        /// <summary>
        /// LAMP ONLY: whether the lamp is switched OFF (1108 enabled=false). Stored
        /// INVERTED on purpose: the JSON default (false) must mean the fresh-mount /
        /// legacy-record state, and a lamp's proven default is ON - so absence of the
        /// field restores an old save's lamps exactly as they always were.
        /// </summary>
        public bool LampOff { get; set; }

        /// <summary>The hull-local mount offset as a <see cref="FixedPointPosition"/>.</summary>
        public FixedPointPosition LocalOffset() => new FixedPointPosition(LocalX, LocalY, LocalZ);

        /// <summary>Rebuilds the exact <see cref="LoosePartDefinition"/> the mounted part spawned from.</summary>
        public LoosePartDefinition Definition() => new LoosePartDefinition(
            SchematicId, ItemType, Title, PrefabName, AttachmentType, PartSpecificComponents);
    }

    /// <summary>
    /// The whole of the shared, server-owned world state that survives a restart: the
    /// deployables players have placed and the ships they have built. Per-player state
    /// (inventory, designs, progression, position) is NOT here - it keys on character
    /// uid and belongs in its own per-character store; this file is the shared world
    /// every client sees, written by the single server poll loop.
    ///
    /// Serialised as one atomic JSON document by <see cref="AtomicJsonFile"/>. One
    /// shared file rather than a file-per-entity because the set is small, the writer
    /// is single-threaded, and a whole-file rewrite on the occasional placement is far
    /// simpler to reason about (and to make crash-safe) than many little files.
    /// </summary>
    public sealed class WorldStateSnapshot
    {
        /// <summary>Every deployable to re-register and re-spawn at boot.</summary>
        public List<PlacedDeployableRecord> PlacedDeployables { get; set; } = new List<PlacedDeployableRecord>();

        /// <summary>Every built ship to re-register and re-spawn at boot.</summary>
        public List<BuiltShipRecord> BuiltShips { get; set; } = new List<BuiltShipRecord>();

        /// <summary>Every MOUNTED part to re-spawn already attached to its built ship at boot.</summary>
        public List<MountedPartRecord> MountedParts { get; set; } = new List<MountedPartRecord>();

        /// <summary>Every crafted-but-unmounted LOOSE part to re-spawn at boot.</summary>
        public List<LoosePartRecord> LooseParts { get; set; } = new List<LoosePartRecord>();
    }
}
