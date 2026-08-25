using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// One hull's slot in the per-hull snapshot cache: the raw WAREBORN_SHIP_MASS
    /// value the cached snapshot was built with (or the invalidation sentinel),
    /// and the snapshot itself. The default slot (null snapshot) means "never
    /// built".
    /// </summary>
    public readonly record struct ShipMassCacheSlot(string? OverrideRaw, ShipMassSnapshot? Snapshot);

    /// <summary>
    /// EVERY decision the per-hull snapshot cache makes - serve-or-rebuild,
    /// override-change detection, invalidation semantics, revision continuity
    /// and the part-mass fallback - lives here where the test project can reach
    /// it. The game-assembly glue (<c>ShipMassSnapshots</c>) holds only the
    /// dictionary, its lock and the ledger reads, and calls these in the
    /// sequence the policy tests mirror.
    /// </summary>
    public static class ShipMassSnapshotCachePolicy
    {
        /// <summary>
        /// The override-slot value marking an invalidated entry. Contains NUL,
        /// which no environment variable value can, so it never collides with a
        /// real WAREBORN_SHIP_MASS setting and <see cref="TryServe"/> is
        /// guaranteed to miss.
        /// </summary>
        public const string InvalidationSentinel = "\0invalidated";

        /// <summary>
        /// Serve the cached snapshot only when one exists AND the raw override it
        /// was built with matches the current one. The comparison is what keeps
        /// the WAREBORN_SHIP_MASS knob live without a restart, and what makes an
        /// <see cref="Invalidated"/> slot force a rebuild.
        /// </summary>
        public static bool TryServe(ShipMassCacheSlot slot, string? overrideRaw,
            out ShipMassSnapshot snapshot)
        {
            if (slot.Snapshot != null && slot.OverrideRaw == overrideRaw)
            {
                snapshot = slot.Snapshot;
                return true;
            }
            snapshot = null!;
            return false;
        }

        /// <summary>
        /// What a rebuild feeds <see cref="ShipMassEvaluator.Build"/> as
        /// <c>previous</c>: the stale snapshot, even from an invalidated slot, so
        /// revision continuity survives - the evaluator decides whether the
        /// rebuild is a real change, and an invalidate-and-rebuild over unchanged
        /// inputs keeps its revision.
        /// </summary>
        public static ShipMassSnapshot? ContinuityPrevious(ShipMassCacheSlot slot) => slot.Snapshot;

        /// <summary>The slot to store after a rebuild.</summary>
        public static ShipMassCacheSlot Stored(string? overrideRaw, ShipMassSnapshot snapshot) =>
            new ShipMassCacheSlot(overrideRaw, snapshot);

        /// <summary>
        /// Marks a slot dirty without discarding the revision chain: the snapshot
        /// stays put as the next rebuild's <c>previous</c>, the sentinel makes
        /// <see cref="TryServe"/> miss. A never-built slot has nothing to dirty
        /// and stays as it is.
        /// </summary>
        public static ShipMassCacheSlot Invalidated(ShipMassCacheSlot slot) =>
            slot.Snapshot == null ? slot
            : new ShipMassCacheSlot(InvalidationSentinel, slot.Snapshot);

        /// <summary>
        /// True when a rebuild is worth telling the operator about: the first
        /// build for a hull, or a revision bump. A rebuild that proved nothing
        /// changed stays quiet.
        /// </summary>
        public static bool RevisionIsNews(ShipMassCacheSlot before, ShipMassSnapshot rebuilt) =>
            before.Snapshot == null || before.Snapshot.Revision != rebuilt.Revision;

        /// <summary>
        /// The one part-mass fallback: a part answers from its hull's snapshot
        /// when the snapshot carries its runtime id, and from the evaluator's
        /// typed table otherwise (a loose part, an unknown entity, or a mount the
        /// snapshot has not caught up with) - so the same trunk weighs the same
        /// before and after it is bolted down.
        /// </summary>
        public static double PartMassKg(ShipMassSnapshot? hullSnapshot, long partEntityId,
            string? itemType, string? prefabName, string? attachmentType)
        {
            if (hullSnapshot != null && hullSnapshot.TryPartMassKg(partEntityId, out double massKg))
            {
                return massKg;
            }
            return ShipMassEvaluator.PartMass(itemType, prefabName, attachmentType).MassKg;
        }

        /// <summary>
        /// Hull half-extents for the COM estimate from the measured plan metrics,
        /// floored at 0.25 m so a degenerate plan cannot hand the estimator a
        /// zero-thickness cuboid.
        /// </summary>
        public static (double HalfX, double HalfY, double HalfZ) HullHalfExtents(
            double beamMetres, double deckPlaneMetres, double keelMetres) =>
            (Math.Max(0.25, beamMetres * 0.5),
             Math.Max(0.25, deckPlaneMetres * 0.5),
             Math.Max(0.25, keelMetres * 0.5));
    }
}
