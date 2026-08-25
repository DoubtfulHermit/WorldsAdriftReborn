using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// Where a mass NUMBER comes from, carried beside the number itself so no
    /// consumer - component writer, flight, telemetry or test - has to guess
    /// whether it is retail truth or our stand-in. A lost retail value ships as a
    /// labelled <see cref="Approximation"/>, never as an invented "recovered"
    /// figure; the label is the whole point.
    /// </summary>
    public enum MassProvenance
    {
        /// <summary>Shipped/decompiled retail value or equation.</summary>
        Recovered = 0,

        /// <summary>
        /// Our deliberate calibration (e.g. the iron-hull ~780 kg anchor, or a
        /// value chosen inside a community-measured range).
        /// </summary>
        WARebornTuning = 1,

        /// <summary>Placeholder shape (e.g. the flat per-part default).</summary>
        Approximation = 2,

        /// <summary>Lost retail data, no defensible stand-in.</summary>
        Unknown = 3
    }

    /// <summary>
    /// One mounted part's mass truth inside a <see cref="ShipMassSnapshot"/>.
    /// <see cref="EntityId"/> is the runtime id for THIS session and is never
    /// persisted as identity; <see cref="StablePartKey"/> is the itemType/prefab
    /// derived identity that survives a restart, and is what the snapshot
    /// fingerprint is computed over.
    /// </summary>
    public readonly record struct MountedPartMassEntry(
        long EntityId,
        string StablePartKey,
        string PartKind,
        string MaterialEvidence,
        double MassKg,
        MassProvenance Provenance);

    /// <summary>
    /// THE one mass truth for one hull: derived hull structural mass, every
    /// mounted part's typed mass with provenance, the flight total, and the
    /// approximate COM/inertia inputs the vector model wants. Immutable and
    /// entity-id-ordered; every consumer (1121, 1257, scalar flight, vector
    /// shadow, wall attenuation, telemetry) reads THIS and recomputes nothing,
    /// which is what <see cref="Revision"/>/<see cref="Fingerprint"/> let them
    /// prove.
    /// </summary>
    public sealed class ShipMassSnapshot
    {
        internal ShipMassSnapshot(long hullEntityId, double hullStructuralMassKg,
            MassProvenance hullProvenance, IReadOnlyList<MountedPartMassEntry> mountedParts,
            double totalMountedMassKg, double totalFlightMassKg,
            ShadowVector3 centreOfMassApprox, ShadowVector3 diagonalInertiaApproxKgM2,
            bool inertiaIsApproximation, double legacyFlatTotalMassKg,
            int revision, string fingerprint)
        {
            HullEntityId = hullEntityId;
            HullStructuralMassKg = hullStructuralMassKg;
            HullProvenance = hullProvenance;
            MountedParts = mountedParts;
            TotalMountedMassKg = totalMountedMassKg;
            TotalFlightMassKg = totalFlightMassKg;
            CentreOfMassApprox = centreOfMassApprox;
            DiagonalInertiaApproxKgM2 = diagonalInertiaApproxKgM2;
            InertiaIsApproximation = inertiaIsApproximation;
            LegacyFlatTotalMassKg = legacyFlatTotalMassKg;
            Revision = revision;
            Fingerprint = fingerprint;
        }

        public long HullEntityId { get; }

        /// <summary>
        /// <see cref="HullMassCalculator.HullMassKg(HullMaterials,int,int)"/> for a
        /// decoded plan, with the WAREBORN_SHIP_MASS override already applied.
        /// </summary>
        public double HullStructuralMassKg { get; }

        public MassProvenance HullProvenance { get; }

        /// <summary>ALWAYS sorted ascending by <see cref="MountedPartMassEntry.EntityId"/>.</summary>
        public IReadOnlyList<MountedPartMassEntry> MountedParts { get; }

        public double TotalMountedMassKg { get; }

        /// <summary>Hull (override applied) + mounted. The one flight mass.</summary>
        public double TotalFlightMassKg { get; }

        /// <summary>ALWAYS marked approximate: no collider data survives server-side.</summary>
        public ShadowVector3 CentreOfMassApprox { get; }

        public ShadowVector3 DiagonalInertiaApproxKgM2 { get; }

        /// <summary>True until Unity collider data is recovered - by construction.</summary>
        public bool InertiaIsApproximation { get; }

        /// <summary>
        /// What the retired flat model (hull + N x 50 kg) would have reported, so
        /// the operator can see the delta this snapshot introduced without
        /// re-deriving the old formula anywhere.
        /// </summary>
        public double LegacyFlatTotalMassKg { get; }

        /// <summary>Monotonic per hull; bumps whenever the fingerprint changes.</summary>
        public int Revision { get; }

        /// <summary>
        /// Deterministic invariant-culture hash over the hull-id-independent
        /// content (hull mass + provenance, each entry's stable key, evidence,
        /// mass and provenance, in stored order). Two builds over the same ship
        /// agree; every consumer of one frame can prove it read the same truth.
        /// </summary>
        public string Fingerprint { get; }

        /// <summary>
        /// This session's mass for one mounted part, by runtime entity id. Linear:
        /// a ship has a handful of parts, and the list is small and immutable.
        /// </summary>
        public bool TryPartMassKg(long partEntityId, out double massKg)
        {
            for (int i = 0; i < MountedParts.Count; i++)
            {
                if (MountedParts[i].EntityId == partEntityId)
                {
                    massKg = MountedParts[i].MassKg;
                    return true;
                }
            }
            massKg = 0.0;
            return false;
        }
    }
}
