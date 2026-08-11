using System.Collections.Generic;

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
        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullX { get; set; }

        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullY { get; set; }

        /// <summary>Hull-centre position, in Q52.12 fixed-point units.</summary>
        public long HullZ { get; set; }

        /// <summary>The hull geometry blob the 1209 CustomShipHullState serves (base64 in JSON).</summary>
        public byte[] HullBytes { get; set; } = System.Array.Empty<byte>();

        /// <summary>The hull position as a <see cref="FixedPointPosition"/>.</summary>
        public FixedPointPosition HullPosition() => new FixedPointPosition(HullX, HullY, HullZ);
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
    }
}
