namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One placed metal resource node - a <c>MetalNugget</c> - as a pure value:
    /// what it is called on the wire, where it sits, and the cosmetic metal it
    /// represents. No ENet, no Improbable types, no game install, so every field
    /// is asserted on natively in the test suite rather than by staring at a game
    /// client.
    ///
    /// A node is deliberately NOT a <see cref="WorldEntity"/> subtype - it is the
    /// DATA a <see cref="WorldEntity"/> registration is built from. The registry
    /// keeps the node facts (this type) alongside the mutable harvest state; the
    /// spawn seam only ever sees the <see cref="WorldEntity"/> that
    /// <see cref="MetalNodes"/> produces from one of these.
    /// </summary>
    public sealed class MetalNode
    {
        public MetalNode(string key, string metalType, int quality, FixedPointPosition position)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("a metal node needs a stable key", nameof(key));
            }
            if (string.IsNullOrWhiteSpace(metalType))
            {
                throw new ArgumentException("a metal node needs a metal type", nameof(metalType));
            }

            Key = key;
            MetalType = metalType;
            Quality = quality;
            Position = position;
        }

        /// <summary>Stable registration identity; the shared-entity-id key. Never on the wire.</summary>
        public string Key { get; }

        /// <summary>
        /// The metal this node represents. COSMETIC for a nugget: the shipped
        /// <c>MetalNugget</c> prefab carries no <c>ComponentMaterialColors</c>, so it
        /// always renders as aluminium regardless of this value
        /// (docs/research/gathering/findings-metal-deposits.md, "SURFACE NUGGETS").
        /// It is carried so the eventual salvage grant and the 1099 itemTypeId have a
        /// material to name, not because it changes what the player sees today.
        /// </summary>
        public string MetalType { get; }

        /// <summary>
        /// The community-table quality of the metal. Carried for the future grant;
        /// unused by the nugget's own rendering (1034 MetalNuggetState has zero
        /// client readers).
        /// </summary>
        public int Quality { get; }

        /// <summary>
        /// The 190602 TransformState.localPosition seed, Q52.12 fixed point, parent
        /// ABSENT - identical to how the tree and the island are placed. This is the
        /// only thing that puts the node anywhere; it is consumed once at OnEnable
        /// and must never be re-sent to a live entity.
        /// </summary>
        public FixedPointPosition Position { get; }

        public override string ToString()
        {
            return Key + " (" + MetalType + " q" + Quality + ") at " + Position;
        }
    }
}
