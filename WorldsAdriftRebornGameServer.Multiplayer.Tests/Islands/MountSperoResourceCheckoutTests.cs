using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE REPORTED BUG, pinned against the real catalogue rather than a fixture.
    ///
    /// A player teleported to Mount Spero (release-887053661, tier 1, zone A2),
    /// landed at global (-8694.589, -64.124, -3915.242) and was at
    /// (-8902.284, -73.686, -3987.665) minutes later. They saw manta rays and no
    /// resources at all. The production log for that visit reports 6 deposit adds
    /// and 6 deposit removes, and "net keys still held on that island: 2".
    ///
    /// These tests use the shipped release catalogue's own extracted AABB and
    /// deposit/databank positions, so they fail if either the geometry or the policy
    /// regresses - and the "2" they derive is the same 2 the live server logged,
    /// which is what makes this a reproduction rather than an illustration.
    /// </summary>
    public class MountSperoResourceCheckoutTests
    {
        private static readonly IslandId Spero = new("release-887053661");

        private static readonly FixedPointPosition PlayerPosition =
            FixedPointPosition.FromMetres(-8902.284, -73.686, -3987.665);

        private static ReleaseIslandRecord Record => ReleaseWorldCatalog.Require(Spero);

        private static IReadOnlyList<IslandResource> Nodes() =>
            Record.Deposits.Select(deposit => new IslandResource(0, deposit.Position, Spero))
                .Concat(Record.Databanks.Select(databank =>
                    new IslandResource(0, databank, Spero)))
                .Select((resource, index) => resource with { EntityId = index + 1 })
                .ToList();

        [Fact]
        public void The_player_was_standing_on_the_island_all_along()
        {
            Assert.Equal(0.0, Record.Envelope.DistanceSquaredTo(PlayerPosition, Record.Definition));
            Assert.True(Record.Envelope.Contains(PlayerPosition, Record.Definition));
        }

        /// <summary>
        /// Why "220 m from the landing point" was never the real measurement: the
        /// island is far wider than the bubble was, so where on it you stand only
        /// changes WHICH few nodes you hold.
        /// </summary>
        [Fact]
        public void The_island_is_much_wider_than_the_old_interest_bubble()
        {
            double widthX = Record.Envelope.MaxX - Record.Envelope.MinX;
            double widthZ = Record.Envelope.MaxZ - Record.Envelope.MinZ;

            Assert.True(widthX > 700.0, "extracted AABB X extent regressed: " + widthX);
            Assert.True(widthZ > 550.0, "extracted AABB Z extent regressed: " + widthZ);
            // 240 m is the production bubble's DIAMETER (120 m load radius).
            Assert.True(widthX > 240.0,
                "the island must be wider than the whole 120 m bubble's diameter");
        }

        [Fact]
        public void The_old_player_centred_bubble_reproduces_the_two_keys_the_server_logged()
        {
            IReadOnlyList<ResourceStreamAction> actions = ResourceInterestPolicy.Reconcile(
                PlayerPosition,
                Nodes().Select(node => (node.EntityId, node.Position)),
                new HashSet<long>(),
                loadRadius: 120.0,
                unloadRadius: 155.0);

            Assert.Equal(19, Nodes().Count);
            Assert.Equal(2, actions.Count);
            Assert.All(actions, action =>
                Assert.Equal(ResourceStreamActionKind.Add, action.Kind));
        }

        [Fact]
        public void Island_keyed_checkout_holds_the_whole_island_from_where_the_player_stood()
        {
            IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                new[]
                {
                    new IslandInterestCandidate(Spero,
                        Record.Envelope.DistanceSquaredTo(PlayerPosition, Record.Definition),
                        Nodes().Count),
                },
                new HashSet<IslandId>(),
                IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres,
                IslandResourceCheckoutPolicy.UnloadRadiusFor(
                    IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres),
                IslandResourceCheckoutPolicy.DefaultPerPeerResources);

            Assert.Equal(new[] { Spero }, admitted);

            IReadOnlyList<ResourceStreamAction> actions = ResourceInterestPolicy.Reconcile(
                PlayerPosition,
                IslandResourceCheckoutPolicy.Desire(Nodes(), admitted.ToHashSet()),
                new HashSet<long>());

            Assert.Equal(19, actions.Count);
            Assert.All(actions, action =>
                Assert.Equal(ResourceStreamActionKind.Add, action.Kind));
        }

        /// <summary>
        /// Walking must not empty the island any more. The peer starts holding
        /// everything at the landing point, walks to where the player actually was,
        /// and the reconcile has NOTHING to do - no removes, no re-adds. That absence
        /// is the entire fix.
        /// </summary>
        [Fact]
        public void Walking_across_the_island_produces_no_churn_at_all()
        {
            HashSet<IslandId> held = new() { Spero };
            HashSet<long> loaded = Nodes().Select(node => node.EntityId).ToHashSet();
            FixedPointPosition landing = Record.Definition.LocalToGlobal(
                Record.Landing.LocalX, Record.Landing.LocalY, Record.Landing.LocalZ);

            foreach (FixedPointPosition where in new[] { landing, PlayerPosition })
            {
                IReadOnlyList<IslandId> admitted = IslandResourceCheckoutPolicy.Admit(
                    new[]
                    {
                        new IslandInterestCandidate(Spero,
                            Record.Envelope.DistanceSquaredTo(where, Record.Definition),
                            Nodes().Count),
                    },
                    held,
                    IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres,
                    IslandResourceCheckoutPolicy.UnloadRadiusFor(
                        IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres),
                    IslandResourceCheckoutPolicy.DefaultPerPeerResources);

                Assert.Equal(new[] { Spero }, admitted);
                Assert.Empty(ResourceInterestPolicy.Reconcile(
                    where,
                    IslandResourceCheckoutPolicy.Desire(Nodes(), admitted.ToHashSet()),
                    loaded));
            }
        }

        /// <summary>
        /// Additions arrive nearest-first, which is what makes a 19-node island - or
        /// an 88-node one - feel populated within a couple of seconds rather than
        /// after the whole queue has drained.
        /// </summary>
        [Fact]
        public void Additions_are_ordered_nearest_to_the_player_first()
        {
            IReadOnlyList<ResourceStreamAction> actions = ResourceInterestPolicy.Reconcile(
                PlayerPosition,
                IslandResourceCheckoutPolicy.Desire(Nodes(), new HashSet<IslandId> { Spero }),
                new HashSet<long>());

            Dictionary<long, FixedPointPosition> byId =
                Nodes().ToDictionary(node => node.EntityId, node => node.Position);
            double previous = -1.0;
            foreach (ResourceStreamAction action in actions)
            {
                double distance = ResourceInterestPolicy.DistanceSquared(
                    PlayerPosition, byId[action.EntityId]);
                Assert.True(distance >= previous, "additions are not nearest-first");
                previous = distance;
            }
        }

        /// <summary>
        /// The island is inside the terrain gate by an enormous margin, so admitting
        /// its resources can never outrun the ground they sit on. The gate itself is
        /// still enforced per Add in ResourceInterestService; this only pins that the
        /// two radii are not in conflict.
        /// </summary>
        [Fact]
        public void The_resource_radius_sits_far_inside_the_terrain_radius()
        {
            Assert.True(
                IslandResourceCheckoutPolicy.UnloadRadiusFor(
                    IslandResourceCheckoutPolicy.DefaultLoadRadiusMetres)
                < IslandTerrainInterestPolicy.DefaultLoadRadiusMetres,
                "resources must never be admitted for terrain that is not even a load candidate");
        }
    }
}
