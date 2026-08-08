namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>One step of the world-sync handshake a joining client walks through.</summary>
    public enum SpawnStep
    {
        /// <summary>Ask the client to load the Traveller prefab bundle.</summary>
        RequestPlayerAsset,

        /// <summary>Ask the client to load the island bundle.</summary>
        RequestIslandAsset,

        /// <summary>Create the shared island entity on the client.</summary>
        AddIslandEntity,

        /// <summary>Create this client's own player entity.</summary>
        AddPlayerEntity,
    }

    /// <summary>The client response that advances past a step.</summary>
    public enum SpawnAck
    {
        /// <summary>An AssetLoadRequestOp came back.</summary>
        AssetLoaded,

        /// <summary>An AddEntityOp came back.</summary>
        EntityAdded,
    }

    /// <summary>
    /// The ORDER in which a joining client is walked into the world, as data.
    ///
    /// It is a policy rather than four lambdas in a list because the order is
    /// load-bearing and silent when wrong:
    ///
    /// 1. The island's AddEntity - and therefore its colliders - must land
    ///    before the player's transform is published, or the player is placed
    ///    over geometry that does not exist yet and falls forever. There is no
    ///    fall damage on this server, so that failure is an infinite fall rather
    ///    than a death, and no WorldEdgePushback to catch it.
    /// 2. Each step is gated on the client ACKing the previous one, which is
    ///    also the only throttle on bundle loading anywhere in the system: the
    ///    client's asset loader is synchronous and unbudgeted.
    ///
    /// Nothing here talks to ENet; the server maps each step to the op that
    /// performs it. The value of the split is that "island before player" is now
    /// asserted by a test instead of being whatever order someone happened to
    /// type the lambdas in.
    /// </summary>
    public static class SpawnSequence
    {
        /// <summary>
        /// The steps, in order. The player ASSET is requested first only because
        /// it is the smaller download and the client needs it eventually either
        /// way; what matters is that the island ENTITY precedes the player
        /// ENTITY, which is what <see cref="IslandPrecedesPlayer"/> guards.
        /// </summary>
        public static readonly IReadOnlyList<SpawnStep> Steps = new[]
        {
            SpawnStep.RequestPlayerAsset,
            SpawnStep.RequestIslandAsset,
            SpawnStep.AddIslandEntity,
            SpawnStep.AddPlayerEntity,
        };

        /// <summary>
        /// The ack that advances past a step: the response type of the op that
        /// step sends.
        /// </summary>
        public static SpawnAck AckFor(SpawnStep step)
        {
            return step switch
            {
                SpawnStep.RequestPlayerAsset => SpawnAck.AssetLoaded,
                SpawnStep.RequestIslandAsset => SpawnAck.AssetLoaded,
                SpawnStep.AddIslandEntity => SpawnAck.EntityAdded,
                SpawnStep.AddPlayerEntity => SpawnAck.EntityAdded,
                _ => throw new ArgumentOutOfRangeException(nameof(step)),
            };
        }

        /// <summary>
        /// Whether a sequence puts the ground under the player before the player
        /// exists: the island's bundle requested and its entity created, both
        /// strictly before the player entity is created.
        /// </summary>
        public static bool IslandPrecedesPlayer(IReadOnlyList<SpawnStep> steps)
        {
            int islandAsset = IndexOf(steps, SpawnStep.RequestIslandAsset);
            int islandEntity = IndexOf(steps, SpawnStep.AddIslandEntity);
            int playerEntity = IndexOf(steps, SpawnStep.AddPlayerEntity);

            return islandAsset >= 0
                && islandEntity > islandAsset
                && playerEntity > islandEntity;
        }

        private static int IndexOf(IReadOnlyList<SpawnStep> steps, SpawnStep step)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] == step)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
