namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One inbound 1073 <c>ClientAuthoritativePlayerState</c> update, reduced to the
    /// three fields that bear on "aboard a ship or not" - and, crucially, to WHICH
    /// of them this particular update actually CARRIED.
    ///
    /// SpatialOS component updates are DELTAS: a field is present only when it
    /// changed. VERIFIED against the client's authorship
    /// (Assembly-CSharp, ClientAuthoritativePlayerMovement.CollectDataHighFrequency):
    /// relativeTo and relativeBias are set only at the moment the player steps onto
    /// or off a ground object, not on every tick, so a player standing still on a
    /// deck sends updates that carry NEITHER - just positionRelative and timestamp.
    /// A tracker therefore cannot read aboardness off a single update; it must
    /// accumulate the deltas. That accumulation lives in <see cref="AboardTracker"/>;
    /// this struct is one delta.
    ///
    /// VERIFIED field shapes (ilspycmd on Generated.Code.dll,
    /// ClientAuthoritativePlayerState.Update):
    ///   Option&lt;EntityId&gt;        relativeTo
    ///   Option&lt;float&gt;           relativeBias
    ///   Option&lt;Option&lt;bool&gt;&gt;    isRelativeToShip
    /// Pure numbers, so the handler can build one without the policy layer touching
    /// a game type.
    /// </summary>
    public readonly struct AboardSample
    {
        public AboardSample(
            bool relativeToChanged, long relativeTo,
            bool relativeBiasChanged, float relativeBias,
            bool isRelativeToShipChanged, bool isRelativeToShip)
        {
            RelativeToChanged = relativeToChanged;
            RelativeTo = relativeTo;
            RelativeBiasChanged = relativeBiasChanged;
            RelativeBias = relativeBias;
            IsRelativeToShipChanged = isRelativeToShipChanged;
            IsRelativeToShip = isRelativeToShip;
        }

        /// <summary>Whether this update carried a new relativeTo (the id the player stands on).</summary>
        public bool RelativeToChanged { get; }

        /// <summary>The new relativeTo entity id. Meaningful only when <see cref="RelativeToChanged"/>.</summary>
        public long RelativeTo { get; }

        /// <summary>Whether this update carried a new relativeBias (0 = free, 1 = attached).</summary>
        public bool RelativeBiasChanged { get; }

        /// <summary>The new relativeBias. Meaningful only when <see cref="RelativeBiasChanged"/>.</summary>
        public float RelativeBias { get; }

        /// <summary>Whether this update carried a new isRelativeToShip (the client's own "it's a ship" belief).</summary>
        public bool IsRelativeToShipChanged { get; }

        /// <summary>The new isRelativeToShip. Corroborating only - see <see cref="AboardPolicy"/>.</summary>
        public bool IsRelativeToShip { get; }
    }

    /// <summary>The aboard decision for one resolved (accumulated) player state.</summary>
    public readonly struct AboardVerdict
    {
        private AboardVerdict(bool isAboard, long shipRootEntityId)
        {
            IsAboard = isAboard;
            ShipRootEntityId = shipRootEntityId;
        }

        /// <summary>Whether the player is aboard a ship this server knows.</summary>
        public bool IsAboard { get; }

        /// <summary>The ship root entity id when <see cref="IsAboard"/>; otherwise 0.</summary>
        public long ShipRootEntityId { get; }

        public static AboardVerdict Aboard(long shipRootEntityId) => new AboardVerdict(true, shipRootEntityId);

        public static readonly AboardVerdict NotAboard = new AboardVerdict(false, 0);

        public bool Equals(AboardVerdict other) =>
            IsAboard == other.IsAboard && ShipRootEntityId == other.ShipRootEntityId;

        public override bool Equals(object? obj) => obj is AboardVerdict other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(IsAboard, ShipRootEntityId);

        public override string ToString() =>
            IsAboard ? "aboard ship " + ShipRootEntityId : "not aboard";
    }

    /// <summary>
    /// WHAT counts as "aboard a ship", from a player's RESOLVED 1073 state (the
    /// accumulated relativeTo and relativeBias, not a single delta) and the
    /// server's <see cref="ShipMembership"/> map. Stateless; the accumulation and
    /// the board/leave transitions are <see cref="AboardTracker"/>.
    ///
    /// THE DECISION, and why it is the entity-id match and not the flag. The client
    /// sends BOTH <c>relativeTo</c> (the id it stands on) and
    /// <c>isRelativeToShip</c> (its own belief that that thing is a ship). The
    /// authoritative signal is the id match against a ship SURFACE the server
    /// itself spawned:
    ///   1. It is robust to whether the hull's ShipVisualizer enabled on the
    ///      client. isRelativeToShip is true only when GetComponentInParent
    ///      &lt;ShipVisualizer&gt; succeeds; our seeded static hull may not enable
    ///      every visualizer, but the id the client stood on is reported regardless.
    ///   2. It cannot be spoofed into "aboard a ship that does not exist": an id
    ///      that is not a registered ship surface is not aboard, whatever the flag.
    /// And the island is why the flag is insufficient the OTHER way: a player on
    /// Haven also sends relativeBias = 1 and a valid relativeTo (the island), just
    /// with isRelativeToShip = false. The membership map excludes the island, so
    /// the id match already rejects it - the flag is never consulted.
    /// </summary>
    public static class AboardPolicy
    {
        /// <summary>
        /// The relativeBias above which the player is genuinely ATTACHED to the
        /// ground object rather than merely near it. 0.5, matching the client's own
        /// threshold: ClientAuthoritativePlayerMovement.SetPlayersInitialPosition
        /// takes the relative branch on <c>relativeBias &gt; 0.5</c> and the plain
        /// branch otherwise. The client only ever sends 0 or 1, so this sits
        /// comfortably between the two.
        /// </summary>
        public const float AttachedBiasThreshold = 0.5f;

        /// <summary>
        /// The aboard verdict for a resolved state. A player is aboard ship X when
        /// a relativeTo is known, the player is attached to it
        /// (<c>relativeBias &gt; 0.5</c>), and <paramref name="membership"/> maps
        /// that relativeTo to a ship root.
        /// </summary>
        /// <param name="relativeToKnown">
        /// Whether a relativeTo has ever been observed for this player. Before the
        /// first one - and the seed sets it to InvalidEntityId with bias 0 - the
        /// answer is trivially "not aboard".
        /// </param>
        public static AboardVerdict Evaluate(bool relativeToKnown, long relativeTo, float relativeBias, ShipMembership membership)
        {
            if (membership == null)
            {
                throw new ArgumentNullException(nameof(membership));
            }

            if (!relativeToKnown || relativeBias <= AttachedBiasThreshold)
            {
                return AboardVerdict.NotAboard;
            }

            long? root = membership.RootOf(relativeTo);
            return root.HasValue ? AboardVerdict.Aboard(root.Value) : AboardVerdict.NotAboard;
        }
    }
}
