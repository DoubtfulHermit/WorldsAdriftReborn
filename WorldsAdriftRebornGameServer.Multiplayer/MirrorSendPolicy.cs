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
        /// SalvagerAimerState: where the salvage beam is pointing, and how far it
        /// reaches. Client-authoritative; <c>SalvagerAimerObserver</c> is its
        /// writer and the source of the <c>HitInfo</c> that everything else on the
        /// harvest path reads. See <see cref="SalvagerMaxBoltDistance"/>.
        /// </summary>
        public const uint SalvagerAimerStateComponentId = 1231;

        /// <summary>
        /// TreeCutterState: which tree section the beam is resting on. The cut
        /// signal, and a LATCH rather than a pulse - see
        /// <see cref="TreeCutSignal"/>.
        /// </summary>
        public const uint TreeCutterStateComponentId = 1037;

        /// <summary>
        /// The three writers of <c>PlayerMultitoolVisualizer</c> - MultiToolPlayerState
        /// (2105), MultitoolSalvagerState (2106), MultitoolRepairerState (2002).
        ///
        /// ALL THREE OR NONE. They are <c>[Require]</c> WRITERS on one visualizer,
        /// and the injection system enables a visualizer only when EVERY one of its
        /// writers is injected (<c>EntityVisualizers.AllFieldWritersInjected</c>).
        /// Two out of three is worth exactly as much as zero: the beam never
        /// charges, and there is no error.
        /// </summary>
        public static readonly IReadOnlyList<uint> MultitoolComponents = new uint[] { 2105, 2106, 2002 };

        /// <summary>
        /// Components a client is granted AUTHORITY over on its OWN entity. A
        /// client only PUBLISHES components it holds authority over, so without
        /// TransformState here nobody ever sends a position and there is nothing
        /// to relay; without ClientAuthoritativePlayerState the bone writer never
        /// runs and every remote avatar stays in T-pose.
        ///
        /// Granting any of this against another player's entity would hand that
        /// client the other player's avatar - see <see cref="PlayerRegistry.Owns"/>.
        ///
        /// THE LAST FIVE ARE THE HARVEST PATH, and they are last on purpose - see
        /// <see cref="InjectedComponents"/> for the ordering argument. 1231 and
        /// 1037 are what let the server HEAR a chop; 2105/2106/2002 are what let
        /// the beam exist at all.
        ///
        /// 1211 InteractAgentState is deliberately NOT here. Granting it equips the
        /// gauntlet, whose input sink is priority class PlayerItem (3) against
        /// InteractAgent's (2), so it takes the LEFT MOUSE BUTTON away from the
        /// component that would otherwise report it. Chopping does not need 1211 -
        /// it is not an interaction verb, the tree has no
        /// <c>InteractiveObjectVisualizer</c> and no "press E" prompt - so adding
        /// it here would buy nothing and cost the left mouse button. See
        /// docs/research/loop/findings-harvest-transaction.md section 2.
        /// </summary>
        public static readonly IReadOnlyList<uint> AuthoritativeComponents = new uint[]
        {
            8050, 8051, 6908, 1260, 1097, 1003, 1241, 1082,
            TransformStateComponentId,
            ClientAuthoritativePlayerStateComponentId,
            UtilitySlotActivatedStateComponentId,
            RopeControlPointsComponentId,
            SalvagerAimerStateComponentId,
            TreeCutterStateComponentId,
            2105, 2106, 2002,
        };

        /// <summary>
        /// SchematicsLearnerGSimState. Injected but NOT granted: the client does
        /// not reliably ask for it, and <c>InventoryVisualiser</c> needs its reader.
        /// </summary>
        public const uint SchematicsLearnerGSimStateComponentId = 1080;

        /// <summary>
        /// The components the server pushes at a client's OWN entity during
        /// first-time setup, unprompted and IN THIS ORDER, on top of whatever the
        /// client asked for.
        ///
        /// It is a list rather than a set because the order is load-bearing and
        /// silent when wrong.
        ///
        /// WHY 1086 IS IN IT AND WHY IT IS EARLY. <c>LocalPlayerInit</c> carries
        /// <c>[Require] PlayerNameReader</c>, so it does not enable until 1086
        /// resolves - and until it enables there is no <c>LocalPlayer.Instance</c>.
        /// <c>SalvagerAimerObserver.Update</c> opens by early-returning unless
        /// <c>LocalPlayer.Exists</c> and <c>LocalPlayer.Instance.playerMove.Equipment.Multitool</c>
        /// is non-null. So the multitool writers granted at the end of this list
        /// are worth nothing while 1086 is outstanding: the beam has no player to
        /// belong to. Putting 1086 ahead of them makes the dependency a property of
        /// the batch instead of a property of the client's request ordering, which
        /// we do not control.
        ///
        /// Re-sending 1086 to a client that already asked for it is safe in a way
        /// that re-sending most components is not: it is a static string record, so
        /// unlike 190602 - whose re-send is a teleport - a duplicate cannot move or
        /// change anything. And this list only ever goes out during first-time
        /// setup, inside the loading screen, before any of it has been acted on.
        /// </summary>
        public static readonly IReadOnlyList<uint> InjectedComponents =
            new uint[] { SchematicsLearnerGSimStateComponentId, PlayerNameComponentId }
                .Concat(AuthoritativeComponents)
                .ToArray();

        /// <summary>
        /// <c>SalvagerAimerState.maxBoltDistance</c>, and it MUST be non-zero.
        ///
        /// <c>SalvagerAimerObserver.IsValidHit</c> is
        /// <c>AreWithinDistance(hit.point, playerPosition, _state.MaxBoltDistance) &amp;&amp; IsSalvageable(...)</c>.
        /// At the default 0 nothing is ever within distance, so <c>HitInfo</c> stays
        /// null forever, so <c>TreeCuttingBehaviour</c> publishes
        /// <c>{InvalidEntityId, -1, false}</c> - and its writer's
        /// <c>FinishAndSend</c> then suppresses every subsequent send, because
        /// nothing ever changes again. The server receives exactly ONE 1037 packet
        /// and never another. That failure looks precisely like "the grant did not
        /// work", which is the wrong place to spend a day.
        ///
        /// 10 m matches <c>PlayerMultitool._maxAimDistance = 10f</c>, the range of
        /// the raycast that actually deploys the salvager, so a target the aimer
        /// accepts is a target the beam can reach. The aimer's own raycast is 40 m
        /// wide; this is the shorter of the two and therefore the binding one.
        /// </summary>
        public const float SalvagerMaxBoltDistance = 10f;

        /// <summary>
        /// Whether an inbound component update is worth forwarding to the OTHER
        /// players' mirrors of the sender.
        ///
        /// 1231 and 1037 are filtered out, for a reason stronger than bandwidth.
        /// <c>RelayToOtherPlayers</c> re-addresses every relayed update to the
        /// SENDER's own entity id, which is right for a position and wrong for
        /// these two: their payloads are aiming state whose meaning is a reference
        /// to a THIRD entity (the tree), interpreted by behaviours that exist only
        /// on the local rig. A remote Traveller@Default has neither component
        /// seeded and no <c>SalvagerAimerObserver</c> to read them, so the best
        /// case is that every peer decodes and discards a packet at raycast rate,
        /// reliably-ordered, on the same channel that carries movement.
        ///
        /// The three multitool components are deliberately NOT filtered. They are
        /// the beam's own state - on/off, engaged, mode - and are the raw material
        /// for other players eventually SEEING someone chopping. Nothing consumes
        /// them on a remote rig today either, but unlike the aim state they are
        /// low-rate (they change when a mode or a trigger changes, not when the
        /// crosshair moves) and they carry no cross-entity reference to be
        /// misread.
        /// </summary>
        public static bool IsRelayedToOtherPlayers(uint componentId)
        {
            return componentId != SalvagerAimerStateComponentId
                && componentId != TreeCutterStateComponentId;
        }

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
