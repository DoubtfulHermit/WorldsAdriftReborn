using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// One mounted part as the evaluator wants it: the runtime entity id, the
    /// catalogue strings <see cref="ShipPartKinds"/> classifies on, and the
    /// hull-local mount position for the COM estimate. The glue that gathers
    /// these decides nothing; every mass decision lives in
    /// <see cref="ShipMassEvaluator"/> where the test project can reach it.
    /// </summary>
    public readonly record struct ShipMassPartInput(
        long EntityId,
        string? ItemType,
        string? PrefabName,
        string? AttachmentType,
        double LocalXMetres,
        double LocalYMetres,
        double LocalZMetres);

    /// <summary>
    /// Everything <see cref="ShipMassEvaluator.Build"/> needs, gathered by thin
    /// glue from the built-ship and mount ledgers. <see cref="PlanDecoded"/>
    /// false means the hull blob was missing or would not decode; the evaluator
    /// then uses the reference fallback rather than a guessed geometry.
    /// </summary>
    public sealed class ShipMassInput
    {
        public ShipMassInput(long hullEntityId, HullMaterials? materials, bool planDecoded,
            int cellCount, int deckCount,
            double hullHalfExtentXMetres, double hullHalfExtentYMetres, double hullHalfExtentZMetres,
            string? hullMassOverrideRaw, IReadOnlyList<ShipMassPartInput> parts)
        {
            HullEntityId = hullEntityId;
            Materials = materials;
            PlanDecoded = planDecoded;
            CellCount = cellCount;
            DeckCount = deckCount;
            HullHalfExtentXMetres = hullHalfExtentXMetres;
            HullHalfExtentYMetres = hullHalfExtentYMetres;
            HullHalfExtentZMetres = hullHalfExtentZMetres;
            HullMassOverrideRaw = hullMassOverrideRaw;
            Parts = parts ?? Array.Empty<ShipMassPartInput>();
        }

        public long HullEntityId { get; }
        public HullMaterials? Materials { get; }
        public bool PlanDecoded { get; }
        public int CellCount { get; }
        public int DeckCount { get; }

        /// <summary>Zero or negative means "hull geometry unavailable" - COM/inertia stay zero.</summary>
        public double HullHalfExtentXMetres { get; }
        public double HullHalfExtentYMetres { get; }
        public double HullHalfExtentZMetres { get; }

        /// <summary>The raw WAREBORN_SHIP_MASS value; semantics are <see cref="ShipTotalMass.HullMassWithOverride"/> exactly.</summary>
        public string? HullMassOverrideRaw { get; }
        public IReadOnlyList<ShipMassPartInput> Parts { get; }
    }

    /// <summary>One typed per-part mass answer: the kilograms and where they come from.</summary>
    public readonly record struct PartMassVerdict(double MassKg, MassProvenance Provenance);

    /// <summary>
    /// THE one place a mounted part weighs anything. Builds the immutable
    /// <see cref="ShipMassSnapshot"/> every consumer reads, and owns the typed
    /// per-part mass table that replaced the old blanket 50 kg.
    ///
    /// PROVENANCE DISCIPLINE (STEP1_MASS_EVIDENCE.md): real per-part evidence
    /// survives for exactly three of the fixture's part types - wings, engines
    /// and (relationally) panels. Those carry values chosen INSIDE their
    /// community-measured ranges and are labelled <see cref="MassProvenance.WARebornTuning"/>,
    /// because the enum has no community-measured grade and calling a player
    /// measurement "Recovered" (Bossa's own data) would overstate it. Everything
    /// else is LOST at part level and stays at the old 50 kg default, now
    /// visibly labelled <see cref="MassProvenance.Approximation"/> - a lost value
    /// is never silently tuned, even where 50 kg is certainly high (an altimeter)
    /// - so no ship gets faster by fiat.
    /// </summary>
    public static class ShipMassEvaluator
    {
        /// <summary>
        /// The flat per-part default the server has always served, surviving ONLY
        /// here and only for parts whose retail mass is LOST. APPROXIMATION:
        /// order-of-magnitude corroborated in aggregate (player quotes put ~6
        /// propulsion-heavy parts at ~350-400 kg over hull), wrong per part.
        /// </summary>
        public const double DefaultPartMassKg = 50.0;

        /// <summary>
        /// A complete procedural wing measured 15-35 kg by aileron material in the
        /// community wing-science sheets (calculator era). The material of a live
        /// wing is not recorded server-side, so the range midpoint is chosen.
        /// WAREBORN TUNING anchored to a COMMUNITY-MEASURED range.
        /// </summary>
        public const double WingMassKg = 25.0;

        /// <summary>
        /// A complete engine measured 30-87 kg by material combo in the community
        /// engine-materials sheets (calculator era). Midpoint chosen on the same
        /// reasoning as <see cref="WingMassKg"/>.
        /// </summary>
        public const double EngineMassKg = 58.5;

        /// <summary>A LARGE panel costs 40 material units - RECOVERED relation (findings §2.1).</summary>
        public const double LargePanelUnits = 40.0;

        /// <summary>
        /// Medium/small panel unit counts were never published; a medium panel is
        /// assumed half a large one. APPROXIMATION, and the reason the whole
        /// medium-panel mass is labelled one.
        /// </summary>
        public const double MediumPanelShareOfLarge = 0.5;

        /// <summary>
        /// Medium panel via the recovered large-panel relation: 40 units x the
        /// interpolated medium share x the legacy metal's recovered kg/unit
        /// (the panel's own crafted material is not recorded server-side).
        /// 40 x 0.5 x 0.39 = 7.8 kg for the assumed iron panel.
        /// </summary>
        public static double MediumPanelMassKg =>
            LargePanelUnits * MediumPanelShareOfLarge
            * MaterialCatalog.Find(MaterialCatalog.LegacyMetalId)!.MassPerUnitKg;

        /// <summary>
        /// The typed mass of one part from its catalogue strings. Total: null or
        /// unrecognised input gets the labelled default, never a throw and never a
        /// guess dressed as recovery. This is the ONE table - the 1121 writer,
        /// the snapshot build and the production lift audit all come here.
        /// </summary>
        public static PartMassVerdict PartMass(string? itemType, string? prefabName,
            string? attachmentType)
        {
            string kind = ShipPartKinds.Classify(itemType, prefabName, attachmentType);
            return PartMassOfKind(kind, itemType);
        }

        private static PartMassVerdict PartMassOfKind(string kind, string? itemType)
        {
            if (kind == ShipPartKinds.Wing)
            {
                return new PartMassVerdict(WingMassKg, MassProvenance.WARebornTuning);
            }
            if (kind == ShipPartKinds.Engine)
            {
                return new PartMassVerdict(EngineMassKg, MassProvenance.WARebornTuning);
            }
            if (string.Equals(itemType, "mediumPanel", StringComparison.OrdinalIgnoreCase))
            {
                return new PartMassVerdict(MediumPanelMassKg, MassProvenance.Approximation);
            }
            return new PartMassVerdict(DefaultPartMassKg, MassProvenance.Approximation);
        }

        /// <summary>
        /// Builds the snapshot. <paramref name="previous"/> is the hull's last
        /// snapshot, if any: the revision continues from it, bumping only when
        /// the fingerprint actually changed, so an invalidate-and-rebuild over
        /// unchanged inputs does not masquerade as a mass change. The returned
        /// snapshot is always freshly built - entity ids are session state and
        /// must never be served stale from a fingerprint match.
        /// </summary>
        public static ShipMassSnapshot Build(ShipMassInput input, ShipMassSnapshot? previous)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            double derivedHull = input.PlanDecoded
                ? HullMassCalculator.HullMassKg(input.Materials, input.CellCount, input.DeckCount)
                : HullMassCalculator.ReferenceHullMassKg;
            double hullMass = ShipTotalMass.HullMassWithOverride(derivedHull, input.HullMassOverrideRaw);
            MassProvenance hullProvenance =
                hullMass != derivedHull ? MassProvenance.WARebornTuning
                : input.PlanDecoded ? MassProvenance.WARebornTuning
                : MassProvenance.Approximation;

            var sorted = new List<ShipMassPartInput>(input.Parts);
            sorted.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));

            var entries = new MountedPartMassEntry[sorted.Count];
            double totalMounted = 0.0;
            for (int i = 0; i < sorted.Count; i++)
            {
                ShipMassPartInput part = sorted[i];
                string kind = ShipPartKinds.Classify(part.ItemType, part.PrefabName, part.AttachmentType);
                PartMassVerdict verdict = PartMassOfKind(kind, part.ItemType);
                entries[i] = new MountedPartMassEntry(
                    part.EntityId,
                    StableKeyOf(part.ItemType, part.PrefabName),
                    kind,
                    // The mount ledger records no crafted material or quality, so
                    // the evidence is the raw identity pair: item type and prefab,
                    // kept SEPARATE so identity predicates (core/upgrade) consult
                    // the same two fields the production audit does.
                    part.ItemType ?? string.Empty,
                    part.PrefabName ?? string.Empty,
                    verdict.MassKg,
                    verdict.Provenance);
                totalMounted += verdict.MassKg;
            }

            double totalFlight = hullMass + totalMounted;
            double legacyFlat = hullMass + (entries.Length * DefaultPartMassKg);

            EstimateMassProperties(hullMass, input, sorted,
                out ShadowVector3 com, out ShadowVector3 inertia);

            string fingerprint = FingerprintOf(hullMass, hullProvenance, entries);
            int revision = previous == null ? 1
                : previous.Fingerprint == fingerprint ? previous.Revision
                : previous.Revision + 1;

            return new ShipMassSnapshot(input.HullEntityId, hullMass, hullProvenance,
                entries, totalMounted, totalFlight, com, inertia,
                inertiaIsApproximation: true, legacyFlat, revision, fingerprint);
        }

        private static string StableKeyOf(string? itemType, string? prefabName)
        {
            if (!string.IsNullOrEmpty(itemType)) return itemType!.ToLowerInvariant();
            if (!string.IsNullOrEmpty(prefabName)) return prefabName!.ToLowerInvariant();
            return "unknown";
        }

        /// <summary>
        /// COM/inertia through the existing <see cref="ShadowMassProperties.TryEstimate"/>
        /// shape: cuboid hull + every part as a point mass. The propulsor KIND on
        /// each point is irrelevant to the estimate (it reads only mass and
        /// position) but its validation demands one, so Engine with zero power
        /// stands in. Failure - unknown hull geometry, a part outside the safety
        /// bounds - leaves COM/inertia at zero; either way the result is an
        /// APPROXIMATION, because no collider data survives server-side.
        /// </summary>
        private static void EstimateMassProperties(double hullMassKg, ShipMassInput input,
            List<ShipMassPartInput> sorted, out ShadowVector3 com, out ShadowVector3 inertia)
        {
            com = ShadowVector3.Zero;
            inertia = ShadowVector3.Zero;
            if (input.HullHalfExtentXMetres <= 0.0 || input.HullHalfExtentYMetres <= 0.0
                || input.HullHalfExtentZMetres <= 0.0)
            {
                return;
            }

            var points = new List<ShadowPropulsor>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                ShipMassPartInput part = sorted[i];
                PartMassVerdict verdict = PartMass(part.ItemType, part.PrefabName, part.AttachmentType);
                points.Add(new ShadowPropulsor(ShadowPartKind.Engine,
                    new ShadowVector3(part.LocalXMetres, part.LocalYMetres, part.LocalZMetres),
                    ShadowQuaternion.Identity, power: 0.0, verdict.MassKg, torqueless: true));
            }

            var half = new ShadowVector3(input.HullHalfExtentXMetres,
                input.HullHalfExtentYMetres, input.HullHalfExtentZMetres);
            if (ShadowMassProperties.TryEstimate(hullMassKg, half, points,
                out ShadowMassProperties properties))
            {
                com = properties.CentreOfMass;
                inertia = properties.DiagonalInertiaKgM2;
            }
        }

        /// <summary>
        /// Deterministic across processes and runs: invariant-culture round-trip
        /// formatting in a fixed field order, hashed with SHA-256. Runtime entity
        /// ids are deliberately excluded AND the entries are hashed in a canonical
        /// sorted order, not the snapshot's ascending-EntityId order - a restart
        /// re-mints ids in persisted last-mount order, so id order is session
        /// state too, and a byte-identical ship must not fingerprint differently
        /// after a reboot. Sorting the canonical strings themselves (ordinal)
        /// orders by (StablePartKey, MaterialEvidence, PrefabEvidence, MassKg,
        /// Provenance).
        /// </summary>
        private static string FingerprintOf(double hullMassKg, MassProvenance hullProvenance,
            IReadOnlyList<MountedPartMassEntry> entries)
        {
            var parts = new string[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                MountedPartMassEntry entry = entries[i];
                parts[i] = "part:" + entry.StablePartKey
                    + ':' + entry.MaterialEvidence
                    // Prefab evidence participates: core/upgrade identity reads
                    // it, so a prefab change is a capacity-relevant change.
                    + ':' + entry.PrefabEvidence
                    + ':' + entry.MassKg.ToString("R", CultureInfo.InvariantCulture)
                    + ':' + (int)entry.Provenance;
            }
            Array.Sort(parts, StringComparer.Ordinal);

            var canonical = new StringBuilder();
            canonical.Append("hull:")
                .Append(hullMassKg.ToString("R", CultureInfo.InvariantCulture))
                .Append(':').Append((int)hullProvenance);
            for (int i = 0; i < parts.Length; i++)
            {
                canonical.Append('|').Append(parts[i]);
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
            var hex = new StringBuilder(32);
            // 16 bytes of SHA-256 is plenty for an equality witness.
            for (int i = 0; i < 16; i++)
            {
                hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            return hex.ToString();
        }
    }
}
