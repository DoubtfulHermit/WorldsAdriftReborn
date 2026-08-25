using System;
using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE per-hull mass snapshot cache - thin glue only. It gathers what the
    /// built-ship and mount ledgers know and hands it to
    /// <see cref="ShipMassEvaluator.Build"/>, where every mass decision lives
    /// (and is unit-tested). Every consumer of ship mass - the 1121/1257
    /// component writers, scalar flight, the vector shadow, agility and admin
    /// telemetry - reads the ONE snapshot cached here; nothing recomputes mass.
    ///
    /// Invalidation rides the hooks that already fire on mount, detach and
    /// salvage (<see cref="ShipFlightService.RefreshDomainOwnership"/> /
    /// <see cref="ShipFlightService.RetireHull"/>). A change of the
    /// WAREBORN_SHIP_MASS override is caught by comparing the raw value the
    /// cached snapshot was built with, so the knob stays live without a restart.
    /// Revision continuity is the evaluator's: a rebuild over unchanged inputs
    /// keeps the revision; a real change bumps it.
    /// </summary>
    internal static class ShipMassSnapshots
    {
        private static readonly Dictionary<long, (string? OverrideRaw, ShipMassSnapshot Snapshot)>
            ByHull = new Dictionary<long, (string?, ShipMassSnapshot)>();

        /// <summary>The current mass truth for one hull, built on first demand.</summary>
        internal static ShipMassSnapshot For(long hullEntityId)
        {
            string? overrideRaw = Environment.GetEnvironmentVariable("WAREBORN_SHIP_MASS");
            if (ByHull.TryGetValue(hullEntityId, out (string? OverrideRaw, ShipMassSnapshot Snapshot) cached)
                && cached.OverrideRaw == overrideRaw)
            {
                return cached.Snapshot;
            }

            ShipMassSnapshot snapshot = ShipMassEvaluator.Build(
                InputFor(hullEntityId, overrideRaw), cached.Snapshot);
            ByHull[hullEntityId] = (overrideRaw, snapshot);
            if (cached.Snapshot == null || cached.Snapshot.Revision != snapshot.Revision)
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

        /// <summary>
        /// This session's mass for one PART entity - what the 1121 writer serves.
        /// A mounted part answers from its hull's snapshot; a loose or unknown
        /// entity gets the evaluator's typed table directly, so the same trunk
        /// weighs the same before and after it is bolted down.
        /// </summary>
        internal static double PartMassKgFor(long partEntityId)
        {
            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(partEntityId);
            if (mount.HasValue)
            {
                if (For(mount.Value.HullEntityId).TryPartMassKg(partEntityId, out double massKg))
                {
                    return massKg;
                }
                return ShipMassEvaluator.PartMass(mount.Value.ItemType,
                    mount.Value.PrefabName, mount.Value.AttachmentType).MassKg;
            }

            Multiplayer.Ship.LoosePartDefinition? loose = Crafting.LooseParts.DefFor(partEntityId);
            if (loose != null)
            {
                return ShipMassEvaluator.PartMass(loose.ItemType,
                    loose.PrefabName, loose.AttachmentType).MassKg;
            }
            return ShipMassEvaluator.PartMass(null, null, null).MassKg;
        }

        /// <summary>Forgets one hull's snapshot; the next read rebuilds from the ledgers.</summary>
        internal static void Invalidate(long hullEntityId)
        {
            // Keep the entry so revision continuity survives: For() passes the
            // stale snapshot as `previous` and the evaluator decides whether the
            // rebuild is a real change. Marking the override slot dirty forces
            // that rebuild without discarding the revision chain.
            if (ByHull.TryGetValue(hullEntityId, out (string? OverrideRaw, ShipMassSnapshot Snapshot) cached))
            {
                ByHull[hullEntityId] = ("\0invalidated", cached.Snapshot);
            }
        }

        /// <summary>Forgets a hull entirely (authoritative salvage/retire).</summary>
        internal static void Retire(long hullEntityId)
        {
            ByHull.Remove(hullEntityId);
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
                halfX = Math.Max(0.25, metrics.BeamMetres * 0.5);
                halfY = Math.Max(0.25, metrics.DeckPlaneMetres * 0.5);
                halfZ = Math.Max(0.25, metrics.KeelMetres * 0.5);
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
