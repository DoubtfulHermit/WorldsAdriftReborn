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
            // Scoped to AdapterFor's body and to the two-argument consume (with
            // the out parameter): RetireHull's cleanup Remove must not satisfy
            // this needle.
            string service = FlightService();
            int adapterFor = service.IndexOf(
                "private FlightAuthorityAdapter AdapterFor", StringComparison.Ordinal);
            Assert.True(adapterFor >= 0, "AdapterFor went missing.");
            int consume = service.IndexOf("_pendingVectorRestore.Remove(hullEntityId,",
                adapterFor, StringComparison.Ordinal);
            Assert.True(consume >= 0 && consume - adapterFor < 800,
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

        /// <summary>
        /// The bubble contract's three inputs only exist in the pure lifecycle if
        /// the glue actually gathers them: the live helm state, the yard's own
        /// geometry, and the hull's extent. A driver that stopped passing any of
        /// them would silently restore proximity docking with every pure test
        /// still green.
        /// </summary>
        [Fact]
        public void The_docking_scan_feeds_the_lifecycle_the_helm_state_and_the_yards_bubble()
        {
            string driver = DockingDriver();
            Contains(driver, "helmManned: session.IsManned",
                "capture is a HELM RELEASE event: the lifecycle can only refuse to "
                + "snap a ship somebody is flying if the driver tells it who is at "
                + "the wheel.");
            Contains(driver, "private static ShipyardBubble BubbleFor(long yardEntityId)",
                "the approach gate, the capture volume, the departure boundary and "
                + "the reviewed dock volume must all come from ONE bubble built from "
                + "the yard's own transform.");
            Contains(driver, "if (!bubble.ContainsDock(observedPose.Position)) continue;",
                "the yard scan must test the DOME (inside the bubble and above the "
                + "yard), not a bare sphere that also reaches under an island.");
            Contains(driver, "hullClearanceRadiusMetres: HullClearanceRadiusFor(hullEntityId)",
                "\"fully outside the bubble\" counts the hull's own extent, so the "
                + "departure frame must carry it.");
        }

        /// <summary>
        /// 1205 DockedShipId IS the bubble (RECOVERED - ShipyardVisualizer drives
        /// the influence dome from OnDockedShipChanged). The transaction stops
        /// writing the legacy dock ledger, so a checkout that still read the ledger
        /// would leave every late joiner with no dome around a docked ship.
        /// </summary>
        [Fact]
        public void The_yard_checkout_serves_the_runtimes_committed_bubble_truth()
        {
            string serializer = Source("WorldsAdriftRebornGameServer", "Game",
                "Components", "ComponentsSerializer.cs");
            Contains(serializer,
                "WorldsAdriftRebornGameServer.Flight.RuntimeDockedShipAt(entityId)",
                "the 1205 ShipyardState checkout must answer from the transactional "
                + "runtime for a managed yard.");
            Contains(serializer, "?? Crafting.BuiltShips.DockedShipFor(entityId)",
                "and fall back to the legacy ledger for every unmanaged yard, so the "
                + "docking-gate-off serve stays byte-identical.");
            Contains(DockingDriver(), "internal long? RuntimeDockedShipFor(long yardEntityId)",
                "null means 'this yard is not under the runtime' - the only honest "
                + "way for the serve to know when to fall back.");
        }

        /// <summary>
        /// The reviewed dock volume is what makes docking beside an island-placed
        /// yard possible at all. It must reach the clearance builder, and it must
        /// stay scoped to the docking decision.
        /// </summary>
        [Fact]
        public void The_docking_clearance_is_built_against_the_reviewed_dock_volume()
        {
            string driver = DockingDriver();
            Contains(driver, "observation.ClearanceFor(YardKey(yardPosition), bubble)",
                "an approach's clearance must be built with the yard's reviewed dock "
                + "volume, or an island-placed yard fails closed as CollisionBlocked "
                + "forever.");
            Contains(driver, "observation.ClearanceFor(runtime.Lifecycle.YardStableKey, bubble)",
                "and so must every subsequent frame's, or the approach is refused on "
                + "the step after it was granted.");
        }
    }
}
