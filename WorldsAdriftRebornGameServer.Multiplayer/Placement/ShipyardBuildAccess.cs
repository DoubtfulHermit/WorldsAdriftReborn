using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// The PER-PLAYER "which shipyard has this player been given build access to" grant,
    /// the server side of the exact gate the client's ship tool refuses on:
    /// <c>PlayerScannerTool.DeployItem</c> reports
    /// <c>ErrorMsgNotRegisteredToShipyard</c> ("Interact with shipyard to gain access.")
    /// whenever its <c>Shipyard</c> is null (PlayerScannerTool.cs:455-457). That
    /// <c>Shipyard</c> is resolved from the player's own
    /// <c>ShipyardVisitorVisualizer.ShipyardVisualizer</c>, which the client's
    /// <c>ShipyardVisitorVisualizer.Update</c> populates PURELY from the player's
    /// <c>1219 ShipyardVisitorState.ShipyardId</c> being a valid entity id
    /// (ShipyardVisitorVisualizer.cs:130-133, resolving via
    /// <c>SpatialOS.Universe.Get(ShipyardId).GetComponent&lt;ShipyardVisualizer&gt;()</c>).
    /// No registration list is consulted on that path - a valid 1219 ShipyardId alone
    /// grants access.
    ///
    /// So "grant build access" is: set the player's 1219 ShipyardId to the shipyard they
    /// interacted with. This ledger is the server's memory of that grant, so the 1219
    /// serve branch reports the granted yard on every (re)checkout of the player's own
    /// 1219 - the same ledger-backed serve the shipyard's 1205 DockedShipId uses - and a
    /// live 1219 update is pushed the moment the player opens the console
    /// (PlacementService.OpenShipyardConsole).
    ///
    /// MULTIPLAYER-SAFE: the grant is PER PLAYER (keyed by the player's own entity id)
    /// and event-driven (written once per console interaction, read on serve). The
    /// client's 1219 carries a SINGLE ShipyardId, so a player has access to exactly the
    /// yard they last interacted with; re-granting simply overwrites - no shared mutable
    /// state, no relay, no per-frame traffic.
    ///
    /// Kept a PURE instance class so grant/overwrite/revoke is unit-tested natively; the
    /// one process-wide instance the serializer and interact path share is
    /// <see cref="Shared"/>, and tests build their own <c>new ShipyardBuildAccess()</c>.
    ///
    /// NOT thread-safe, deliberately: written and read on the single server poll loop.
    /// </summary>
    public sealed class ShipyardBuildAccess
    {
        /// <summary>
        /// The one process-wide instance the runtime shares (the interact path grants,
        /// the 1219 serve branch reads). Tests use an isolated <c>new</c> instead.
        /// </summary>
        public static ShipyardBuildAccess Shared { get; } = new ShipyardBuildAccess();

        private readonly Dictionary<long, long> _shipyardByPlayer = new Dictionary<long, long>();

        /// <summary>
        /// Grants <paramref name="playerEntityId"/> build access to
        /// <paramref name="shipyardEntityId"/> - i.e. the value the player's 1219
        /// ShipyardId should now report. Overwrites any prior grant, because the client's
        /// 1219 holds a SINGLE shipyard id (the yard they are currently at).
        /// </summary>
        public void Grant(long playerEntityId, long shipyardEntityId)
        {
            _shipyardByPlayer[playerEntityId] = shipyardEntityId;
        }

        /// <summary>
        /// The shipyard entity id this player has build access to, or 0 (an INVALID
        /// EntityId) when they have none - exactly the value the 1219
        /// <c>ShipyardVisitorState.ShipyardId</c> seed wants for "no yard, no access".
        /// </summary>
        public long ShipyardFor(long playerEntityId)
        {
            return _shipyardByPlayer.TryGetValue(playerEntityId, out long shipyardId) ? shipyardId : 0;
        }

        /// <summary>Whether this player currently has build access to any shipyard.</summary>
        public bool HasAccess(long playerEntityId)
        {
            return _shipyardByPlayer.ContainsKey(playerEntityId);
        }

        /// <summary>
        /// Revokes a player's access (used if the player leaves / the yard is removed),
        /// returning the shipyard id they HAD access to, or 0 if none. The caller then
        /// pushes 1219 with an invalid ShipyardId so the client drops the yard.
        /// </summary>
        public long Revoke(long playerEntityId)
        {
            if (_shipyardByPlayer.TryGetValue(playerEntityId, out long shipyardId))
            {
                _shipyardByPlayer.Remove(playerEntityId);
                return shipyardId;
            }
            return 0;
        }

        /// <summary>
        /// Revokes EVERY player's grant that points at <paramref name="shipyardEntityId"/> -
        /// the yard was packed back into inventory (station pickup), so no 1219 may keep
        /// naming it. Returns the players whose grant was dropped, so the caller can push
        /// each a cleared 1219 if it wants to. Grants at other yards are untouched.
        /// </summary>
        public IReadOnlyList<long> RevokeAllFor(long shipyardEntityId)
        {
            List<long> revoked = new List<long>();
            foreach (KeyValuePair<long, long> grant in _shipyardByPlayer)
            {
                if (grant.Value == shipyardEntityId)
                {
                    revoked.Add(grant.Key);
                }
            }
            foreach (long playerEntityId in revoked)
            {
                _shipyardByPlayer.Remove(playerEntityId);
            }
            return revoked;
        }
    }
}
