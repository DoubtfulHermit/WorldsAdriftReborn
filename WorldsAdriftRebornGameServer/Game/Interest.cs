using System;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// The server-side configuration of connect-time spatial interest - the glue
    /// that turns <see cref="InterestPolicy"/> (pure, env-and-geometry) into the two
    /// answers the spawn-plan walk needs: is gating on, and where is a given peer's
    /// interest centred.
    ///
    /// It exists as a small static holder rather than being folded into the giant
    /// server class for the same reason as <see cref="LoadBarrier"/>: the decision
    /// is read once from the environment and reused at every plan-walk turn, and
    /// keeping it here keeps the perform loop a one-line question.
    ///
    /// WHAT IT DOES. When <see cref="Enabled"/>, the plan walk fast-forwards a
    /// joining peer past every AfterPlayer world entity (trees, ore, deposits,
    /// databanks, atlas shards) that is more than <see cref="RadiusMetres"/> metres
    /// from that peer's interest centre - it is never told about them, so a
    /// resource-dense world only ever streams its NEARBY set to any one client. The
    /// barrier's initial set (island, ship hull, bolted parts) and the player's own
    /// avatar/ground are never gated.
    ///
    /// CONNECT-TIME ONLY, FOR NOW. <see cref="CenterFor"/> returns the fixed player
    /// spawn point, so the gate is evaluated once, at join, against where the player
    /// lands. The movement-driven follow-on (reveal entities as a player roams into
    /// range) is add-only - the client has NO wire path to REMOVE an entity
    /// (RegisterRemoveEntityCallback / RemoveEntityOp are unimplemented; see
    /// OnClientDisconnected) - and would swap this method's body for the peer's live
    /// position from <c>RelayEmitter</c> (which already tracks LastPosition per
    /// sender) plus a throttled per-peer rescan of the entities it has not yet been
    /// sent. That is scoped, not shipped here.
    /// </summary>
    internal static class Interest
    {
        /// <summary>
        /// The interest radius in metres, read once from the environment. Fixed for
        /// the process lifetime: a value that changed mid-run would gate a peer's
        /// join differently from the world it already holds.
        /// </summary>
        public static double RadiusMetres { get; } =
            InterestPolicy.RadiusMetresFrom(Environment.GetEnvironmentVariable(InterestPolicy.RadiusEnvVar));

        /// <summary>Whether connect-time interest gating is armed (a positive radius was configured).</summary>
        public static bool Enabled => InterestPolicy.IsEnabled(RadiusMetres);

        /// <summary>
        /// The interest centre for a peer. Connect-time: the fixed player spawn
        /// point, so every joiner is gated against where it lands. The movement
        /// follow-on would return the peer's live relayed position instead.
        /// </summary>
        public static FixedPointPosition CenterFor(ulong peerId) => SpawnPolicy.PlayerSpawnPosition;
    }
}
