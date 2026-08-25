using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship.Flight
{
    /// <summary>
    /// DO THE FOUR MERGED TRACKS STAY ON ONE CLOCK, ONE MINTER, ONE MASS AND ONE
    /// POSE THROUGH THE GAME-SIDE GLUE? - the same source-scan guard style as
    /// <c>ShipMassSnapshotWiringTests</c>, aimed at the Step-6 integration seams
    /// that live in the untestable game-server assembly. Each needle pins a
    /// cross-track decision the pure suites cannot see going missing: the glue
    /// has no test project, so these go red the moment an integration seam is
    /// unpicked.
    /// </summary>
    public sealed class FlightRuntimeIntegrationWiringTests
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
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string FlightService() =>
            Source("WorldsAdriftRebornGameServer", "Game", "ShipFlightService.cs");

        private static string DockingDriver() =>
            Source("WorldsAdriftRebornGameServer", "Game", "ShipDockingRuntimeDriver.cs");

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
        public void The_collision_observation_consumes_the_adapter_stamp_and_pose_not_its_own_mint()
        {
            string driver = DockingDriver();
            Contains(driver,
                "internal void ObserveAfterSlice(long hullEntityId, FlightAuthorityStamp stamp,",
                "the driver must RECEIVE the minted stamp; only the hull's "
                + "FlightAuthorityAdapter mints (contract section 3).");
            Contains(driver, "AuthoritativeFlightPose pose",
                "the observation's subject proxy must be built from the committed "
                + "authoritative pose, not a pose the driver reads elsewhere.");
            string service = FlightService();
            Contains(service, "_dockingDriver.ObserveAfterSlice(hullEntityId, stamp, adapter.CurrentPose",
                "the service must hand the driver the adapter's committed stamp+pose pair.");
            Contains(service, "adapter.TryCommitScalar(sliceEndStep, domain.Generation.Value, session.State)",
                "a pure-scalar hull's slice must be committed through the SAME minter "
                + "before it may be observed - never stamped ad hoc.");
        }

        [Fact]
        public void The_collision_proxy_mass_is_the_one_snapshot_total()
        {
            Contains(FlightService(),
                "double proxyMassKg = ShipMassSnapshots.For(hullEntityId).TotalFlightMassKg;",
                "the collision proxy must weigh what the ship actually weighs "
                + "(contract section 5 table) - no 1 kg fallback, no second evaluation.");
        }

        [Fact]
        public void The_transactional_docking_freeze_requests_a_vector_reseed()
        {
            string service = FlightService();
            int freeze = service.IndexOf(
                "if (result.HasValue && result.Value.FreezeVelocity)", StringComparison.Ordinal);
            Assert.True(freeze >= 0, "RunDockingScan lost its transactional freeze branch.");
            int reseed = service.IndexOf(
                "_vectorReseedRequested.Add(hullEntityId);", freeze, StringComparison.Ordinal);
            Assert.True(reseed >= 0 && reseed - freeze < 800,
                "The transactional freeze resets the session pose outside the vector "
                + "runtime (session.DockAt inside the driver); like legacy DockAt and "
                + "EmergencyStop it must request a vector reseed, or a promoted hull "
                + "flies on from its pre-freeze pose.");
        }

        [Fact]
        public void A_docked_restore_never_loads_a_stale_vector_state()
        {
            string service = FlightService();
            Contains(service, "&& !durable.WasDocked",
                "RegisterHull may only park a durable vector extension for a hull that "
                + "was NOT docked; a docked hull restores at rest at the dock pose "
                + "(WasDocked => restore-at-rest, no stale vector restore).");
            Contains(service, "_pendingVectorRestore[hullEntityId] = durable.Vector;",
                "the not-docked branch is what parks the durable vector state.");
        }

        [Fact]
        public void The_vector_runtime_commits_each_step_under_its_own_step_number()
        {
            Contains(FlightService(), "adapter?.TryCommitVector(slice.FirstStep + i, domain.Generation.Value",
                "each accepted 20 ms step commits under ITS step number - committing "
                + "slice-end numbers for every step would collapse stamp monotonicity "
                + "to publication cadence.");
        }

        [Fact]
        public void The_adapter_glue_consumes_the_parked_durable_vector_restore()
        {
            Contains(FlightService(), "_pendingVectorRestore.Remove(hullEntityId",
                "AdapterFor must consume the parked durable vector state; ignoring it "
                + "would silently re-seed every promoted hull from the scalar pose on "
                + "restart (mutation program item 9).");
        }

        [Fact]
        public void Legacy_docking_writers_are_unreachable_under_the_transaction()
        {
            string service = FlightService();
            int scan = service.IndexOf("private void RunDockingScan", StringComparison.Ordinal);
            Assert.True(scan >= 0, "RunDockingScan went missing.");
            int gate = service.IndexOf("if (!RuntimeFlags.DockingTxnEnabled)", scan,
                StringComparison.Ordinal);
            int legacy = service.IndexOf("TryCaptureAtEmptyShipyard(hullEntityId, session);", scan,
                StringComparison.Ordinal);
            Assert.True(gate >= 0 && legacy > gate,
                "the legacy radius-snap capture may only run when the docking "
                + "transaction is OFF (kill-list item 8).");
            Contains(service, "if (_dockingDriver.Manages(hullEntityId)) return;",
                "the legacy departure writer must not race a runtime-managed hull.");
        }
    }
}
