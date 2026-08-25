using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE per-hull mass snapshot cache - thin glue only. It gathers what the
    /// built-ship and mount ledgers know and hands it to
    /// <see cref="ShipMassEvaluator.Build"/>, where every mass decision lives
    /// (and is unit-tested); every CACHE decision - serve-or-rebuild, override
    /// change detection, invalidation semantics, revision continuity, the
    /// part-mass fallback - is <see cref="ShipMassSnapshotCachePolicy"/>'s (also
    /// unit-tested). Every consumer of ship mass - the 1121/1257 component
    /// writers, scalar flight, the vector shadow, agility and admin telemetry -
    /// reads the ONE snapshot cached here; nothing recomputes mass.
    ///
    /// Invalidation rides the hooks that already fire on mount, detach and
    /// salvage (<see cref="ShipFlightService.RefreshDomainOwnership"/> /
    /// <see cref="ShipFlightService.RetireHull"/>). A change of the
    /// WAREBORN_SHIP_MASS override is caught by comparing the raw value the
    /// cached snapshot was built with, so the knob stays live without a restart.
    /// </summary>
    internal static class ShipMassSnapshots
    {
        // Guards ByHull. Nearly every caller sits on the main ENet loop, but
        // ShipBuildTimerService completes builds on a THREADPOOL timer and that
        // path runs ComponentsSerializer.InitAndSerialize - one seed-list edit
        // (1257/1121 on a hull seed) away from reaching For/PartMassKgFor off
        // the loop, so the cache must not rely on single-threaded access. The
        // lock spans the ledger reads and the rebuild; both are cheap and rare.
        private static readonly object Gate = new object();

        private static readonly Dictionary<long, ShipMassCacheSlot> ByHull =
            new Dictionary<long, ShipMassCacheSlot>();

        /// <summary>The current mass truth for one hull, built on first demand.</summary>
        internal static ShipMassSnapshot For(long hullEntityId)
        {
            string? overrideRaw = Environment.GetEnvironmentVariable("WAREBORN_SHIP_MASS");
            lock (Gate)
            {
                ByHull.TryGetValue(hullEntityId, out ShipMassCacheSlot slot);
                if (ShipMassSnapshotCachePolicy.TryServe(slot, overrideRaw,
                    out ShipMassSnapshot cached))
                {
                    return cached;
                }

                ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                    InputFor(hullEntityId, overrideRaw),
                    ShipMassSnapshotCachePolicy.ContinuityPrevious(slot));
                ByHull[hullEntityId] = ShipMassSnapshotCachePolicy.Stored(overrideRaw, snapshot);
                if (ShipMassSnapshotCachePolicy.RevisionIsNews(slot, snapshot))
                {
                    Console.WriteLine("[mass] hull " + hullEntityId
                        + " revision " + snapshot.Revision
                        + " fingerprint " + snapshot.Fingerprint
                        + " hull " + Kg(snapshot.HullStructuralMassKg)
                        + " kg + " + snapshot.MountedParts.Count + " part(s) "
                        + Kg(snapshot.TotalMountedMassKg)
                        + " kg -> total " + Kg(snapshot.TotalFlightMassKg)
                        + " kg (flat model would say " + Kg(snapshot.LegacyFlatTotalMassKg) + " kg).");
                }
                return snapshot;
            }
        }

        /// <summary>
        /// This session's mass for one PART entity - what the 1121 writer serves.
        /// The glue only looks the entity up in the ledgers; which mass answers
        /// (hull snapshot vs the typed table) is the policy's fallback decision.
        /// </summary>
        internal static double PartMassKgFor(long partEntityId)
        {
            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(partEntityId);
            if (mount.HasValue)
            {
                return ShipMassSnapshotCachePolicy.PartMassKg(
                    For(mount.Value.HullEntityId), partEntityId,
                    mount.Value.ItemType, mount.Value.PrefabName, mount.Value.AttachmentType);
            }

            Multiplayer.Ship.LoosePartDefinition? loose = Crafting.LooseParts.DefFor(partEntityId);
            if (loose != null)
            {
                return ShipMassSnapshotCachePolicy.PartMassKg(null, partEntityId,
                    loose.ItemType, loose.PrefabName, loose.AttachmentType);
            }
            return ShipMassSnapshotCachePolicy.PartMassKg(null, partEntityId, null, null, null);
        }

        /// <summary>Forgets one hull's snapshot; the next read rebuilds from the ledgers.</summary>
        internal static void Invalidate(long hullEntityId)
        {
            lock (Gate)
            {
                // The policy keeps the stale snapshot so revision continuity
                // survives: For() feeds it to the evaluator as `previous` and the
                // evaluator decides whether the rebuild is a real change.
                if (ByHull.TryGetValue(hullEntityId, out ShipMassCacheSlot slot))
                {
                    ByHull[hullEntityId] = ShipMassSnapshotCachePolicy.Invalidated(slot);
                }
            }
        }

        /// <summary>Forgets a hull entirely (authoritative salvage/retire).</summary>
        internal static void Retire(long hullEntityId)
        {
            lock (Gate)
            {
                ByHull.Remove(hullEntityId);
            }
        }

        private static ShipMassInput InputFor(long hullEntityId, string? overrideRaw)
        {
            bool planDecoded = false;
            int cells = 0, decks = 0;
            double halfX = 0.0, halfY = 0.0, halfZ = 0.0;
            byte[]? hullBytes = Crafting.BuiltShips.HullBytesFor(hullEntityId);
            if (hullBytes != null
                && Multiplayer.Ship.ShipPlanModel.TryDecode(hullBytes, out Multiplayer.Ship.ShipPlanModel? plan, out _)
                && plan != null)
            {
                Multiplayer.Ship.ShipHullMetrics metrics = Multiplayer.Ship.ShipHullMetrics.Measure(plan);
                planDecoded = true;
                cells = metrics.CellCount;
                decks = metrics.DeckCount;
                (halfX, halfY, halfZ) = ShipMassSnapshotCachePolicy.HullHalfExtents(
                    metrics.BeamMetres, metrics.DeckPlaneMetres, metrics.KeelMetres);
            }

            var parts = new List<ShipMassPartInput>();
            foreach (KeyValuePair<long, Crafting.MountedParts.Mount> entry
                in Crafting.MountedParts.OnHull(hullEntityId))
            {
                Crafting.MountedParts.Mount mount = entry.Value;
                parts.Add(new ShipMassPartInput(entry.Key, mount.ItemType, mount.PrefabName,
                    mount.AttachmentType, mount.LocalOffset.MetresX,
                    mount.LocalOffset.MetresY, mount.LocalOffset.MetresZ));
            }

            return new ShipMassInput(hullEntityId,
                Crafting.BuiltShips.MaterialsFor(hullEntityId), planDecoded, cells, decks,
                halfX, halfY, halfZ, overrideRaw, parts);
        }

        private static string Kg(double value) =>
            value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
