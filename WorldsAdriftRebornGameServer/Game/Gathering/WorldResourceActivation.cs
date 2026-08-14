using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// Makes a registered resource authoritative exactly once, independently of
    /// whether any particular peer currently has it checked out. Spatial interest
    /// controls visibility only; it must never decide whether harvesting exists.
    /// </summary>
    internal sealed class WorldResourceActivation
    {
        private readonly WorldEntityRegistry _world;
        private readonly TreeHarvest _trees;
        private readonly NodeRegistry _nodes;
        private readonly MetalHarvest _metal;
        private readonly AtlasShardRegistry _shards;
        private readonly FuelCanisterRegistry _fuel;

        public WorldResourceActivation(
            WorldEntityRegistry world,
            TreeHarvest trees,
            NodeRegistry nodes,
            MetalHarvest metal,
            AtlasShardRegistry shards,
            FuelCanisterRegistry fuel)
        {
            _world = world;
            _trees = trees;
            _nodes = nodes;
            _metal = metal;
            _shards = shards;
            _fuel = fuel;
        }

        public bool Activate(WorldEntity entity, long entityId)
        {
            bool activated = false;
            string? treeWood = TreeSpecies.WoodFor(entity.AssetName);
            if (treeWood != null)
            {
                TreeTopology topology = TreeTopologies.For(entity.AssetName) ?? Trees.Topology();
                if (_trees.Plant(entityId, topology, treeWood))
                {
                    Console.WriteLine("[world-resource] activated tree '" + entity.Key + "' as entity "
                        + entityId + ": " + topology.SectionCount + " sections, " + treeWood + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == MetalNodes.AssetName)
            {
                MetalNode? node = MetalNodes.ByKey(entity.Key);
                if (node != null && _nodes.Register(entityId, node))
                {
                    HarvestReward.Register(node.MetalType,
                        new Multiplayer.Gathering.YieldRule(node.MetalType, 1));
                    _metal.Place(entityId, MetalNodes.NuggetYieldUnits);
                    Console.WriteLine("[world-resource] activated metal node '" + entity.Key
                        + "' as entity " + entityId + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == MetalDeposits.AssetName)
            {
                MetalNode? deposit = MetalDeposits.ByKey(entity.Key);
                if (deposit != null && _nodes.Register(entityId, deposit))
                {
                    HarvestReward.Register(deposit.MetalType,
                        new Multiplayer.Gathering.YieldRule(deposit.MetalType, 1));
                    _metal.Place(entityId, MetalDeposits.YieldUnits, MetalDeposits.ShotsToDeplete);
                    Console.WriteLine("[world-resource] activated deposit '" + entity.Key
                        + "' as entity " + entityId + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == AtlasShardCatalogue.AssetName)
            {
                string? hostKey = AtlasShardCatalogue.HostKeyOf(entity.Key);
                long? hostId = hostKey == null ? null : _world.BoundEntityIdFor(hostKey);
                if (hostId == null)
                {
                    Console.WriteLine("[warning] [world-resource] atlas shard '" + entity.Key
                        + "' has no bound host '" + (hostKey ?? "?") + "'; not activated.");
                }
                else if (_shards.Register(entityId, hostId.Value, AtlasShardCatalogue.DefaultSlotId))
                {
                    Console.WriteLine("[world-resource] activated atlas shard '" + entity.Key
                        + "' as entity " + entityId + " in deposit " + hostId.Value + ".");
                    activated = true;
                }
            }

            if (FuelPods.IsPodKey(entity.Key) && _fuel.Register(entityId))
            {
                HarvestReward.Register(FuelPods.ItemTypeId,
                    new Multiplayer.Gathering.YieldRule(FuelPods.ItemTypeId, 1));
                Console.WriteLine("[world-resource] activated fuel canister '" + entity.Key
                    + "' as entity " + entityId + ".");
                activated = true;
            }

            if (entity.AssetName == Databanks.AssetName
                && DatabankLedger.Register(entityId, Databanks.GrantAmount,
                    Databanks.NoteTitle, Databanks.NoteDescription))
            {
                Console.WriteLine("[world-resource] activated databank '" + entity.Key
                    + "' as entity " + entityId + ".");
                activated = true;
            }

            return activated;
        }

        public int ActivateBoundResources()
        {
            int count = 0;
            foreach (WorldEntity entity in _world.Registrations)
            {
                if (!ResourceInterestPolicy.IsStreamedResourceKey(entity.Key)) continue;
                long? entityId = _world.BoundEntityIdFor(entity.Key);
                if (entityId.HasValue && Activate(entity, entityId.Value)) count++;
            }
            return count;
        }
    }
}
