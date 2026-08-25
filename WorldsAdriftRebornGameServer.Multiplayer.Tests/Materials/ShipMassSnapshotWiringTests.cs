using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// IS EVERY MASS CONSUMER ACTUALLY ON THE ONE SNAPSHOT? - the same
    /// source-scan guard <c>FlightForceModelWiringTests</c> is, aimed at the
    /// Step-1 acceptance rule: 1121, 1257, scalar flight, agility, the vector
    /// shadow and admin telemetry must all read the identical
    /// (Revision, Fingerprint, TotalFlightMassKg), and NO parallel mass
    /// derivation may survive. The game-server assembly has no test project, so
    /// the seams are asserted by reading the production source off disk - coarse,
    /// but it goes red the moment a consumer forks its own formula again.
    /// </summary>
    public sealed class ShipMassSnapshotWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string FlightService() =>
            Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

        private static string Serializer() =>
            Source("WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs");

        private static string SnapshotGlue() =>
            Source("WorldsAdriftRebornGameServer", "Game", "ShipMassSnapshots.cs");

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        private static void Lacks(string haystack, string needle, string why)
        {
            Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected NOT to find `" + needle + "`. " + why);
        }

        [Fact]
        public void The_snapshot_glue_calls_the_production_evaluator_and_ledgers_not_its_own_policy()
        {
            string glue = SnapshotGlue();
            Contains(glue, "ShipMassEvaluator.Build",
                "every mass decision must be made by the unit-tested evaluator; the glue only "
                + "gathers inputs.");
            Contains(glue, "ShipPlanModel.TryDecode",
                "cell/deck counts must come from the decoded hull plan.");
            Contains(glue, "ShipHullMetrics.Measure",
                "hull geometry must be measured, not assumed.");
            Contains(glue, "MountedParts.OnHull",
                "the part list must be the real mount ledger.");
            Contains(glue, "WAREBORN_SHIP_MASS",
                "the hull-mass override knob must stay live.");
            Lacks(glue, "HullMassCalculator.HullMassKg",
                "the glue must not derive hull mass itself - that is the evaluator's job, "
                + "where a test project can reach it.");
        }

        [Fact]
        public void The_snapshot_glue_defers_every_cache_decision_to_the_tested_policy_and_locks_the_cache()
        {
            string glue = SnapshotGlue();
            Contains(glue, "ShipMassSnapshotCachePolicy.TryServe",
                "serve-or-rebuild (including override-change detection) is the policy's "
                + "decision, mirrored by ShipMassSnapshotCachePolicyTests.");
            Contains(glue, "ShipMassSnapshotCachePolicy.ContinuityPrevious",
                "revision continuity across invalidation must come from the policy, "
                + "not an ad-hoc previous.");
            Contains(glue, "ShipMassSnapshotCachePolicy.Invalidated",
                "the invalidation sentinel semantics live in the policy.");
            Contains(glue, "ShipMassSnapshotCachePolicy.PartMassKg",
                "the mounted-vs-loose-vs-unknown part-mass fallback is the policy's.");
            Contains(glue, "lock (Gate)",
                "ShipBuildTimerService completes builds on a threadpool timer, so the "
                + "shared cache dictionary must not rely on single-threaded access.");
        }

        [Fact]
        public void Scalar_flight_agility_and_the_vector_shadow_read_the_one_snapshot()
        {
            string service = FlightService();
            Contains(service, "ShipMassSnapshots.For(hullEntityId).TotalFlightMassKg",
                "PropulsionFor must fly the snapshot total (which also feeds the wall "
                + "mass attenuation through ship.MassKg).");
            Contains(service, "ShipMassSnapshots.For(hullEntityId).HullStructuralMassKg",
                "AgilityScaleFor must scale off the snapshot's hull mass.");
            Contains(service, "HullMassCalculator.AgilityScale",
                "agility must keep the recovered inverse-sqrt shape.");
            Contains(service, "massSnapshot.TryPartMassKg",
                "the vector shadow must give each propulsor its own typed snapshot mass.");
        }

        [Fact]
        public void The_component_writers_serve_the_one_snapshot()
        {
            string serializer = Serializer();
            Contains(serializer, "ShipMassSnapshots.For(entityId).HullStructuralMassKg",
                "the 1257 ParentingMassAdderState hull mass must be the snapshot's.");
            Contains(serializer, "ShipMassSnapshots.PartMassKgFor(entityId)",
                "the 1121 OriginalMassState per-part mass must be the snapshot's typed value.");
        }

        [Fact]
        public void Admin_telemetry_exposes_the_snapshot_identity()
        {
            Contains(FlightService(), "massSnapshot.Revision, massSnapshot.Fingerprint",
                "the inspector must publish which snapshot revision/fingerprint the served "
                + "mass came from, so every consumer can be proven to agree.");
        }

        [Fact]
        public void The_snapshot_cache_is_invalidated_by_the_mount_detach_salvage_hooks()
        {
            string service = FlightService();
            Contains(service, "ShipMassSnapshots.Invalidate(hullEntityId)",
                "RefreshDomainOwnership fires on mount/detach/salvage; the stale snapshot "
                + "must die with it.");
            Contains(service, "ShipMassSnapshots.Retire(hullEntityId)",
                "an authoritatively salvaged hull must not leak its snapshot.");
        }

        [Fact]
        public void No_parallel_mass_derivation_survives()
        {
            string service = FlightService();
            Lacks(service, "DerivedHullMassKgFor",
                "the flight service's own hull-mass derivation was one of the three "
                + "parallel calculations Step 1 collapsed.");
            Lacks(service, "_hullMassByHull",
                "the per-hull mass cache is replaced by the one snapshot cache.");
            Lacks(service, "_agilityByHull",
                "the per-hull agility cache is replaced by the one snapshot cache.");
            Lacks(service, "* 50.0",
                "the raw flat-mass literals must not survive outside the evaluator.");
            Lacks(service, "ShipTotalMass.TotalFlightMassKg",
                "the retired flat formula must not creep back.");

            string serializer = Serializer();
            Lacks(serializer, "ShipMassKgFor",
                "the serializer's own hull-mass derivation was one of the three "
                + "parallel calculations Step 1 collapsed.");
            Lacks(serializer, "MountedPartMassKg",
                "the flat per-part constant must not creep back into the writers.");
        }
    }
}
