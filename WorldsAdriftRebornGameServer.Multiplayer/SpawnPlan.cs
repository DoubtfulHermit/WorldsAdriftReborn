namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The two things the server can do to walk any one entity into a client's
    /// world. Every step of the handshake is one of these applied to one entity.
    /// </summary>
    public enum SpawnOp
    {
        /// <summary>Ask the client to load a prefab bundle. Acked by AssetLoadRequestOp.</summary>
        RequestAsset,

        /// <summary>Create the entity on the client. Acked by AddEntityOp.</summary>
        AddEntity,
    }

    /// <summary>
    /// One step of a joining client's handshake: an op, and the thing it applies
    /// to.
    /// </summary>
    public sealed class SpawnPlanStep
    {
        public SpawnPlanStep(SpawnOp op, WorldEntity? entity)
        {
            Op = op;
            Entity = entity;
        }

        /// <summary>What is done.</summary>
        public SpawnOp Op { get; }

        /// <summary>
        /// What it is done to, or NULL for the joining peer's OWN player avatar.
        ///
        /// The player is deliberately not a <see cref="WorldEntity"/>: a world
        /// entity is one object shared by every client under one id, and a player
        /// avatar is the opposite of that - one per peer, with a per-peer id and a
        /// position the client itself becomes authoritative for a moment later.
        /// </summary>
        public WorldEntity? Entity { get; }

        /// <summary>Whether this step is about the joining peer's own avatar.</summary>
        public bool IsPlayer => Entity == null;

        /// <summary>
        /// The client response that advances past this step. Each step is gated on
        /// the client acking the previous one, which is also the only throttle on
        /// bundle loading anywhere in the system: the client's asset loader is
        /// synchronous and unbudgeted.
        /// </summary>
        public SpawnAck Ack => Op == SpawnOp.RequestAsset ? SpawnAck.AssetLoaded : SpawnAck.EntityAdded;

        public override string ToString()
        {
            return Op + " " + (Entity == null ? "<player>" : Entity.Key);
        }
    }

    /// <summary>
    /// The ORDER in which a joining client is walked into the world, for ANY set
    /// of registered world entities - as data.
    ///
    /// This is the generalisation of <see cref="SpawnSequence"/>, which described
    /// the same handshake for the one case that used to exist: exactly one island
    /// and one player. That description is still true and still asserted; it is
    /// now the degenerate output of this function, which
    /// <c>SpawnPlanTests.The_plan_for_an_island_only_world_is_the_old_four_step_sequence</c>
    /// pins so the two cannot drift.
    ///
    /// The shape is fixed and the content is not:
    /// <code>
    ///   RequestAsset  &lt;player&gt;
    ///   for each BeforePlayer entity:  RequestAsset e, AddEntity e
    ///   AddEntity     &lt;player&gt;
    ///   for each AfterPlayer  entity:  RequestAsset e, AddEntity e
    /// </code>
    ///
    /// WHY AN ENTITY'S ASSET IS REQUESTED IMMEDIATELY BEFORE ITS AddEntity, every
    /// time: the client only instantiates an entity whose prefab asset it has
    /// LOADED. When the remote-player mirror sent AddEntityOp("Traveller") without
    /// a preceding asset request, the AddEntity was silently dropped and no rig
    /// ever appeared. There is no error for this. Pairing the two ops in the plan
    /// is what makes that unforgettable rather than remembered.
    ///
    /// WHY THE PLAYER IS NOT SIMPLY LAST: an entity the player has to stand on
    /// must exist before the player's 190602 is published, or the player is placed
    /// over geometry that has not streamed in and falls forever - this server
    /// writes no HealthState so there is no fall damage to end it, and
    /// WorldEdgePushback never runs because we never send world bounds. That is
    /// the island's whole reason for being <see cref="SpawnOrder.BeforePlayer"/>.
    /// Anything that is NOT load-bearing for the player's footing belongs after,
    /// because every step before the player is a step the loading screen waits on.
    /// </summary>
    public static class SpawnPlan
    {
        /// <summary>
        /// The steps for one joining client, given everything registered. The
        /// AfterPlayer entities are emitted in registration order, unchanged.
        /// </summary>
        public static IReadOnlyList<SpawnPlanStep> For(WorldEntityRegistry registry)
        {
            // A predicate that never fires means "nothing is prioritised", so the
            // AfterPlayer block is emitted in plain registration order - byte for
            // byte the plan this method has always produced. The barrier path calls
            // the overload below with LoadBarrierPolicy.IsInitialKey.
            return For(registry, _ => false);
        }

        /// <summary>
        /// The steps for one joining client, with the AfterPlayer entities matching
        /// <paramref name="isInitial"/> emitted BEFORE the rest.
        ///
        /// WHY REORDER AT ALL. The loading barrier releases the player as soon as an
        /// INITIAL set (the ground and the player's ship) is ready on the client;
        /// the distant scenery keeps streaming afterwards. Each AfterPlayer entity
        /// is released one pacing interval apart, so if the initial entities sat at
        /// the BACK of the plan the barrier would have to wait out every tree and
        /// ore node before its own set even started - the pacer would put the delay
        /// back that the barrier exists to remove. Bringing the initial entities to
        /// the front streams exactly the load-bearing set first.
        ///
        /// WHAT DOES NOT CHANGE. This only permutes the AfterPlayer block; the
        /// player's own asset/entity steps and every BeforePlayer entity (the
        /// ground that must precede the player, see the type remarks) keep their
        /// positions, so <see cref="GroundPrecedesPlayer"/> and
        /// <see cref="EveryAssetIsRequestedBeforeItsEntity"/> hold exactly as
        /// before. Relative order WITHIN the initial group and within the distant
        /// group is preserved, so a hull still precedes the helm whose 8066 seed
        /// names it.
        /// </summary>
        public static IReadOnlyList<SpawnPlanStep> For(WorldEntityRegistry registry, Func<string, bool> isInitial)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (isInitial == null)
            {
                throw new ArgumentNullException(nameof(isInitial));
            }

            List<SpawnPlanStep> plan = new List<SpawnPlanStep>();

            // The player's own bundle first: it is the smaller download and the
            // client needs it either way. What matters is not this, it is that
            // every BeforePlayer entity is fully created below before the player
            // ENTITY is.
            plan.Add(new SpawnPlanStep(SpawnOp.RequestAsset, null));

            foreach (WorldEntity entity in registry.InOrder(SpawnOrder.BeforePlayer))
            {
                plan.Add(new SpawnPlanStep(SpawnOp.RequestAsset, entity));
                plan.Add(new SpawnPlanStep(SpawnOp.AddEntity, entity));
            }

            plan.Add(new SpawnPlanStep(SpawnOp.AddEntity, null));

            // Initial (ground + ship) AfterPlayer entities first, then distant
            // scenery - each group kept in registration order. A stable partition,
            // not a sort: two passes over the same list.
            IReadOnlyList<WorldEntity> afterPlayer = registry.InOrder(SpawnOrder.AfterPlayer);
            foreach (WorldEntity entity in afterPlayer)
            {
                if (isInitial(entity.Key))
                {
                    plan.Add(new SpawnPlanStep(SpawnOp.RequestAsset, entity));
                    plan.Add(new SpawnPlanStep(SpawnOp.AddEntity, entity));
                }
            }
            foreach (WorldEntity entity in afterPlayer)
            {
                if (!isInitial(entity.Key))
                {
                    plan.Add(new SpawnPlanStep(SpawnOp.RequestAsset, entity));
                    plan.Add(new SpawnPlanStep(SpawnOp.AddEntity, entity));
                }
            }

            return plan;
        }

        /// <summary>
        /// Whether a plan puts the ground under the player before the player
        /// exists: every <see cref="SpawnOrder.BeforePlayer"/> entity has its
        /// bundle requested and its entity created, both strictly before the
        /// player entity is created.
        ///
        /// The generalisation of <see cref="SpawnSequence.IslandPrecedesPlayer"/>.
        /// Wrong order is silent and looks like an endless fall.
        /// </summary>
        public static bool GroundPrecedesPlayer(IReadOnlyList<SpawnPlanStep> plan)
        {
            if (plan == null)
            {
                return false;
            }

            int playerEntity = IndexOf(plan, SpawnOp.AddEntity, null);
            if (playerEntity < 0)
            {
                return false;
            }

            foreach (SpawnPlanStep step in plan)
            {
                if (step.Entity == null || step.Entity.Order != SpawnOrder.BeforePlayer)
                {
                    continue;
                }

                int asset = IndexOf(plan, SpawnOp.RequestAsset, step.Entity);
                int entity = IndexOf(plan, SpawnOp.AddEntity, step.Entity);

                if (asset < 0 || entity <= asset || playerEntity <= entity)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether every entity in a plan has its bundle requested before it is
        /// created. Violating this is the failure with no error message: the
        /// client drops an AddEntityOp for a prefab it has not loaded and simply
        /// never shows the object.
        /// </summary>
        public static bool EveryAssetIsRequestedBeforeItsEntity(IReadOnlyList<SpawnPlanStep> plan)
        {
            if (plan == null)
            {
                return false;
            }

            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Op != SpawnOp.AddEntity)
                {
                    continue;
                }

                int asset = IndexOf(plan, SpawnOp.RequestAsset, plan[i].Entity);
                if (asset < 0 || asset > i)
                {
                    return false;
                }
            }

            return true;
        }

        private static int IndexOf(IReadOnlyList<SpawnPlanStep> plan, SpawnOp op, WorldEntity? entity)
        {
            for (int i = 0; i < plan.Count; i++)
            {
                if (plan[i].Op == op && ReferenceEquals(plan[i].Entity, entity))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
