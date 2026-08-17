using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Placement
{
    /// <summary>
    /// The per-player shipyard build-access grant - the server memory that decides what
    /// the player's 1219 ShipyardVisitorState.ShipyardId reports, and therefore whether
    /// PlayerScannerTool.Shipyard is non-null (no "Interact with shipyard to gain
    /// access." on the crafted-part lift). Each test uses its own isolated instance so
    /// there is no cross-test static leak; the runtime shares ShipyardBuildAccess.Shared.
    /// </summary>
    public class ShipyardBuildAccessTests
    {
        [Fact]
        public void No_grant_means_no_access_and_an_invalid_shipyard_id()
        {
            var access = new ShipyardBuildAccess();

            // 0 is an INVALID EntityId - exactly what the 1219 serve wants for "no yard",
            // which the client reads as Shipyard == null and refuses the lift.
            Assert.False(access.HasAccess(playerEntityId: 7));
            Assert.Equal(0, access.ShipyardFor(playerEntityId: 7));
        }

        [Fact]
        public void Granting_reports_the_shipyard_the_player_gains_access_to()
        {
            var access = new ShipyardBuildAccess();

            access.Grant(playerEntityId: 7, shipyardEntityId: 4242);

            Assert.True(access.HasAccess(7));
            Assert.Equal(4242, access.ShipyardFor(7));
        }

        [Fact]
        public void Access_is_per_player_and_does_not_bleed_across_players()
        {
            var access = new ShipyardBuildAccess();

            access.Grant(playerEntityId: 7, shipyardEntityId: 4242);

            // A different player has no access from player 7's grant.
            Assert.False(access.HasAccess(8));
            Assert.Equal(0, access.ShipyardFor(8));
        }

        [Fact]
        public void Re_granting_overwrites_because_the_client_1219_holds_one_yard()
        {
            var access = new ShipyardBuildAccess();

            access.Grant(7, 4242);
            access.Grant(7, 9001); // interacted with a second yard

            Assert.Equal(9001, access.ShipyardFor(7));
        }

        [Fact]
        public void Revoke_clears_access_and_returns_the_prior_yard()
        {
            var access = new ShipyardBuildAccess();
            access.Grant(7, 4242);

            long had = access.Revoke(7);

            Assert.Equal(4242, had);
            Assert.False(access.HasAccess(7));
            Assert.Equal(0, access.ShipyardFor(7));
        }

        [Fact]
        public void Revoking_a_player_with_no_grant_is_a_harmless_zero()
        {
            var access = new ShipyardBuildAccess();
            Assert.Equal(0, access.Revoke(7));
        }

        [Fact]
        public void Shared_is_a_single_stable_instance()
        {
            Assert.Same(ShipyardBuildAccess.Shared, ShipyardBuildAccess.Shared);
        }

        [Fact]
        public void RevokeAllFor_drops_every_grant_at_that_yard_and_names_the_players()
        {
            // The station-pickup path: the yard is packed, so every 1219 grant
            // naming it must go, and grants at OTHER yards must survive.
            var access = new ShipyardBuildAccess();
            access.Grant(7, 4242);
            access.Grant(8, 4242);
            access.Grant(9, 9001);

            var revoked = access.RevokeAllFor(4242);

            Assert.Equal(new[] { 7L, 8L }, revoked.OrderBy(p => p).ToArray());
            Assert.False(access.HasAccess(7));
            Assert.False(access.HasAccess(8));
            Assert.Equal(9001, access.ShipyardFor(9));
        }

        [Fact]
        public void RevokeAllFor_an_unknown_yard_is_a_harmless_empty_list()
        {
            var access = new ShipyardBuildAccess();
            access.Grant(7, 4242);

            Assert.Empty(access.RevokeAllFor(1234));
            Assert.True(access.HasAccess(7));
        }
    }
}
