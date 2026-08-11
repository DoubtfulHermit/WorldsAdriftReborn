using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using WorldsAdriftRebornGameServer.Networking.Wrapper;

namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// THE SPAWN. Turns a completed ship-part craft into a real, LOOSE (unattached)
    /// world entity standing next to the station that made it - the crafted-output
    /// counterpart of <see cref="BuiltShipSpawner"/>, and its exact mirror in shape:
    /// register a <see cref="WorldEntity"/>, allocate its shared entity id once, then
    /// broadcast AssetLoadRequest -> AddEntity -> the all-or-nothing seed batch to
    /// every connected peer.
    ///
    /// WHAT MAKES THE PART REAL. The seed set is the part definition's
    /// <see cref="LoosePartDefinition.SeedComponents"/> - the union of
    /// ShipPartVisualizer's [Require] readers (so the part renders and is liftable)
    /// and the part-specific functional ids (the lamp's 1108/1236, so it glows). The
    /// only per-part difference the serializer resolves is the 1120 metadata and the
    /// 1108/1236/1013 values, keyed off <see cref="LooseParts"/> by the part's
    /// entity id - recorded here BEFORE the broadcast so the first checkout sees it.
    ///
    /// MULTIPLAYER CLASSIFICATION: a ONE-TIME AddEntity + static seeds per crafted
    /// part, then ordinary interest serving - NOT a per-frame re-seed, NOT a
    /// high-rate relay. A loose part is a SHARED world entity every peer sees, so the
    /// same id is broadcast to all peers. It carries no motion stream: its 190602 is
    /// seeded once, world-absolute, from its own registered position.
    ///
    /// PREFAB + ATTACHMENT OVERRIDES. The two values not recoverable from the client
    /// decompile (prefabName, attachmentType) are overridable at runtime via
    /// WAREBORN_LAMP_PREFAB / WAREBORN_LAMP_ATTACH, so a live mismatch is a config
    /// change rather than a rebuild - the same escape hatch WAREBORN_SHIP_POS gives
    /// the hull position.
    /// </summary>
    internal static class LoosePartSpawner
    {
        /// <summary>
        /// Spawns <paramref name="definition"/> as a loose world part next to the
        /// crafting station <paramref name="stationEntityId"/>. Returns the allocated
        /// part entity id, or null if nothing could be spawned. Env overrides for the
        /// lamp's prefab/attachment are applied here.
        /// </summary>
        internal static long? Spawn(long stationEntityId, LoosePartDefinition definition)
        {
            LoosePartDefinition part = ApplyEnvOverrides(definition);

            FixedPointPosition stationPos;
            WorldEntity? station = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(stationEntityId);
            if (station != null)
            {
                stationPos = station.Position;
            }
            else
            {
                // A station that is not a registered world entity (should not happen
                // for a placed shipyard, which IS registered) - spawn next to the
                // default static-ship spot rather than at the origin, and say so.
                stationPos = Multiplayer.WorldEntities.ShipFramePosition();
                Console.WriteLine("[warn] loose-part spawn: station entity " + stationEntityId
                    + " is not a registered world entity; placing the part next to the default"
                    + " ship spot " + stationPos + " instead.");
            }

            FixedPointPosition partPos = LoosePartPlacement.NextTo(stationPos);

            int sequence = LooseParts.NextSequence();
            WorldEntity registration = LoosePartSpawnPlan.For(sequence, partPos, part);
            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long partEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            // Recorded BEFORE the broadcast so the serve branches (1120/8066/1108/
            // 1236/1013/1099) already resolve this part's per-entity truth when the
            // first peer checks it out.
            LooseParts.Register(partEntityId, part);

            int reached = 0;
            foreach (ENetPeerHandle peer in ConnectedPeers())
            {
                if (BroadcastToPeer(peer, partEntityId, registration))
                {
                    reached++;
                }
            }

            Console.WriteLine("[info] loose-part spawn: CRAFTED '" + part.ItemType + "' (prefab '"
                + part.PrefabName + "', attach '" + part.AttachmentType + "') as part entity "
                + partEntityId + " at " + partPos + " (" + part.SeedComponents.Count
                + " seeds), sent to " + reached + " peer(s). This is loose part #"
                + LooseParts.Count + " this session.");

            if (reached == 0)
            {
                Console.WriteLine("[warn] loose-part spawn: part " + partEntityId
                    + " was registered but reached no fully-connected peer; late joiners will still"
                    + " get it via the connect-time spawn plan.");
            }

            return partEntityId;
        }

        /// <summary>
        /// Applies WAREBORN_LAMP_PREFAB / WAREBORN_LAMP_ATTACH overrides to the lamp
        /// definition. Any other part passes through unchanged (its overrides can be
        /// added the same way). A blank/unset variable keeps the catalogue default.
        /// </summary>
        private static LoosePartDefinition ApplyEnvOverrides(LoosePartDefinition definition)
        {
            if (definition.SchematicId != LoosePartCatalogue.LampSchematicId)
            {
                return definition;
            }

            string? prefab = Environment.GetEnvironmentVariable("WAREBORN_LAMP_PREFAB");
            string? attach = Environment.GetEnvironmentVariable("WAREBORN_LAMP_ATTACH");

            string effectivePrefab = string.IsNullOrWhiteSpace(prefab) ? definition.PrefabName : prefab;
            string effectiveAttach = string.IsNullOrWhiteSpace(attach) ? definition.AttachmentType : attach;

            if (effectivePrefab == definition.PrefabName && effectiveAttach == definition.AttachmentType)
            {
                return definition;
            }

            return new LoosePartDefinition(
                definition.SchematicId,
                definition.ItemType,
                definition.Title,
                effectivePrefab,
                effectiveAttach,
                definition.PartSpecificComponents);
        }

        /// <summary>
        /// Sends one entity to one peer the proven way: AssetLoadRequest, AddEntity,
        /// then the all-or-nothing seed push with failOnComponentInitError TRUE.
        /// Mirrors <see cref="BuiltShipSpawner"/>'s BroadcastToPeer (a loose part goes
        /// out the exact same way a built hull does).
        /// </summary>
        private static bool BroadcastToPeer(ENetPeerHandle peer, long entityId, WorldEntity registration)
        {
            SendOPHelper.SendAssetLoadRequestOP(peer, "notNeeded?", registration.AssetName, registration.AssetContext);

            if (!SendOPHelper.SendAddEntityOP(peer, entityId, registration.AssetName, registration.AssetContext))
            {
                Console.WriteLine("[error] loose-part spawn: failed to send AddEntityOp for entity " + entityId + " to a peer.");
                return false;
            }

            List<Structs.Structs.InterestOverride> seeds = registration.SeedComponents
                .Select(id => new Structs.Structs.InterestOverride(id, 1))
                .ToList();

            if (!SendOPHelper.SendAddComponentOp(peer, entityId, seeds, true))
            {
                Console.WriteLine("[error] loose-part spawn: entity " + entityId
                    + " was created on a peer but its seed components were dropped; it will render inert.");
                return false;
            }

            return true;
        }

        private static IEnumerable<ENetPeerHandle> ConnectedPeers()
        {
            return PeerManager.Instance.playerState.Keys.ToList();
        }
    }
}
