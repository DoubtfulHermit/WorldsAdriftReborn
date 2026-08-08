namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>How a relayed component update must be delivered.</summary>
    public enum RelayReliability
    {
        /// <summary>Reliable-ordered. The default for anything not superseded every tick.</summary>
        Reliable,

        /// <summary>
        /// Unreliable. Only for high-rate streams whose next packet replaces this
        /// one anyway; reliable delivery would head-of-line stall on any loss.
        /// </summary>
        Unreliable,
    }

    /// <summary>
    /// The values the server is allowed to put on the wire when mirroring and
    /// relaying players, and nothing else. Every constant here was paid for by a
    /// failed two-client test round; the comments say which one.
    ///
    /// This type exists so those values can be asserted on directly rather than
    /// living as literals inside ENet-calling code where only a human staring at
    /// two game clients could catch a change.
    ///
    /// See docs/multiplayer.md for the narrative and docs/component-ids.md for
    /// what the numbers mean.
    /// </summary>
    public static class MirrorSendPolicy
    {
        /// <summary>Prefab asset every player avatar is spawned from.</summary>
        public const string PrefabName = "Traveller";

        /// <summary>
        /// Prefab context for a MIRRORED (remote) player. The client's
        /// DispatchEventHandler maps context to asset, and "Default" selects the
        /// plain Traveller: the game's own shipped remote-player rig.
        /// </summary>
        public const string RemotePrefabContext = "Default";

        /// <summary>
        /// Prefab context for a client's OWN player. "Player" selects
        /// Traveller@Player - the FULL LOCAL RIG (~90 local-only components,
        /// LocalPlayerInit, camera proxies). Sending this for a remote player
        /// instantiates a second local player that steals the camera and the
        /// local-player identity; every early mirroring regression traces to it.
        /// It is a constant here purely so a test can assert it is NEVER the
        /// context used for a remote.
        /// </summary>
        public const string LocalPrefabContext = "Player";

        /// <summary>
        /// TransformState: a player's position and rotation. What has to reach
        /// other clients for them to see anyone move.
        /// </summary>
        public const uint TransformStateComponentId = 190602;

        /// <summary>
        /// ClientAuthoritativePlayerState: the player's bone/animation bytes.
        /// Granted to the owner so its movement writer publishes; seeded on the
        /// remote rig so BoneAnimationReader binds instead of staying in T-pose.
        /// </summary>
        public const uint ClientAuthoritativePlayerStateComponentId = 1073;

        /// <summary>
        /// UtilitySlotActivatedState: head/body/feet utility slot active flag.
        /// The glider is a body utility, so this is what opens the wings on a
        /// remote rig.
        /// </summary>
        public const uint UtilitySlotActivatedStateComponentId = 6910;

        /// <summary>RopeControlPoints: the grapple rope, drawn on remotes by RemoteGrappleLine.</summary>
        public const uint RopeControlPointsComponentId = 1098;

        /// <summary>PlayerName.</summary>
        public const uint PlayerNameComponentId = 1086;

        /// <summary>InventoryState - a [Require] of CharacterCustomisationVisualizer.</summary>
        public const uint InventoryStateComponentId = 1081;

        /// <summary>PlayerPropertiesState - the other [Require] of CharacterCustomisationVisualizer, and the appearance carrier.</summary>
        public const uint PlayerPropertiesStateComponentId = 1088;

        /// <summary>
        /// CharacterControlsData. Means "this is the character you control".
        /// MUST NOT be seeded on a remote avatar: doing so gave each client two
        /// entities carrying player state and detached the camera to a top-down
        /// view with neither avatar drawn.
        /// </summary>
        public const uint CharacterControlsDataComponentId = 1072;

        /// <summary>
        /// PilotState. Injected on a client's OWN entity (PlayerExternalDataVisualizer
        /// nullrefs without it) but MUST NOT be seeded on a remote: it steals the
        /// PilotVisualizer singleton and pokes LocalPlayer.
        /// </summary>
        public const uint PilotStateComponentId = 1109;

        /// <summary>
        /// Components seeded onto a mirrored remote avatar, and nothing more.
        /// Kept minimal on purpose: a larger seed enables visualizers against
        /// default data and their OnEnable subscriptions throw, which kills the
        /// whole enable chain and leaves an invisible avatar.
        /// </summary>
        public static readonly IReadOnlyList<uint> RemoteSeedComponents = new uint[]
        {
            TransformStateComponentId,
            PlayerNameComponentId,
            InventoryStateComponentId,
            PlayerPropertiesStateComponentId,
            ClientAuthoritativePlayerStateComponentId,
            UtilitySlotActivatedStateComponentId,
            RopeControlPointsComponentId,
        };

        /// <summary>
        /// Components a client is granted AUTHORITY over on its OWN entity. A
        /// client only PUBLISHES components it holds authority over, so without
        /// TransformState here nobody ever sends a position and there is nothing
        /// to relay; without ClientAuthoritativePlayerState the bone writer never
        /// runs and every remote avatar stays in T-pose.
        ///
        /// Granting any of this against another player's entity would hand that
        /// client the other player's avatar - see <see cref="PlayerRegistry.Owns"/>.
        /// </summary>
        public static readonly IReadOnlyList<uint> AuthoritativeComponents = new uint[]
        {
            8050, 8051, 6908, 1260, 1097, 1003, 1241, 1082,
            TransformStateComponentId,
            ClientAuthoritativePlayerStateComponentId,
            UtilitySlotActivatedStateComponentId,
            RopeControlPointsComponentId,
        };

        /// <summary>
        /// Whether a parked mirror op may be sent again on a later attempt.
        ///
        /// ONLY AddEntity. A client that was still loading the prefab silently
        /// drops an AddEntity, so it has to be repeated or the other player never
        /// appears (the one-way visibility bug) - and AddEntity carries no
        /// component data, so repeating it cannot move anyone.
        ///
        /// Resending AddComponents is what caused the SKY-LAUNCH: it re-applied
        /// the DEFAULT seeded TransformState to an already-moving player and
        /// teleported them into the air.
        /// </summary>
        public static bool MayResend(MirrorOp op)
        {
            return op == MirrorOp.AddEntity;
        }

        /// <summary>
        /// Delivery mode for a relayed component update.
        ///
        /// High-rate streams (transform, bone/animation) are superseded every
        /// tick, so a lost packet is irrelevant - while reliable-ordered delivery
        /// stalls the whole channel on any loss, which reads as stutter over the
        /// internet. Everything else stays reliable, because a dropped one-shot
        /// (appearance, glider state, rope) never comes back.
        /// </summary>
        public static RelayReliability RelayReliabilityFor(uint componentId)
        {
            return componentId == TransformStateComponentId
                || componentId == ClientAuthoritativePlayerStateComponentId
                ? RelayReliability.Unreliable
                : RelayReliability.Reliable;
        }
    }
}
