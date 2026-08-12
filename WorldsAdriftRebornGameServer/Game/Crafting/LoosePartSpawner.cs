using System;
using System.Collections.Generic;
using System.Linq;
using Bossa.Travellers.Items;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
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
        /// crafting station <paramref name="stationEntityId"/>, owned by
        /// <paramref name="ownerCharacterUid"/> (the crafter's character uid). Returns the
        /// allocated part entity id, or null if nothing could be spawned. Env overrides for
        /// the lamp's prefab/attachment are applied here.
        /// </summary>
        internal static long? Spawn(long stationEntityId, LoosePartDefinition definition, string ownerCharacterUid)
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

            // A stable, cross-restart identity for this part, so its persisted loose record
            // can be found and removed the instant it becomes mounted (and re-added if it is
            // lifted off again) without ever double-spawning.
            string partUid = Guid.NewGuid().ToString("N");

            // Recorded BEFORE the broadcast so the serve branches (1120/8066/1108/
            // 1236/1013/1099) already resolve this part's per-entity truth when the
            // first peer checks it out.
            LooseParts.Register(partEntityId, part, partUid);

            // PERSIST the loose part so it survives a restart. Stores the EFFECTIVE (post
            // env-override) definition so a restore is byte-identical, the world-absolute
            // spawn position, identity rotation (a freshly-crafted part chose no facing) and
            // the crafter's owner uid. Restored on boot through the SAME LoosePartSpawnPlan.
            WorldStatePersistence.RecordLoosePart(new Multiplayer.Persistence.LoosePartRecord
            {
                PartUid = partUid,
                SchematicId = part.SchematicId,
                ItemType = part.ItemType,
                Title = part.Title,
                PrefabName = part.PrefabName,
                AttachmentType = part.AttachmentType,
                PartSpecificComponents = part.PartSpecificComponents.ToArray(),
                X = partPos.X,
                Y = partPos.Y,
                Z = partPos.Z,
                PackedRotation = Multiplayer.Placement.Quaternion32Packing.Identity,
                OwnerCharacterUid = ownerCharacterUid ?? "",
            });

            // MATERIALIZE (3.2 / 6.2): seed 1013 spawning=true (a full timer) BEFORE the
            // broadcast so the first checkout plays the dissolve-in, then flip to
            // spawning=false after the dissolve so the part becomes non-kinematic and
            // liftable (the flip is MANDATORY - a part left spawning=true is frozen). The
            // flip is one-shot, per-entity, drained on the main loop (DeferredActions).
            float materializeSeconds = Multiplayer.Ship.CraftableSpawnPolicy.MaterializeSeconds(
                Environment.GetEnvironmentVariable("WAREBORN_MATERIALIZE_SECONDS"));
            LooseParts.MarkSpawning(partEntityId, materializeSeconds);

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

            // Schedule the mandatory materialize flip: after the dissolve, mark the part
            // spawned (so a later checkout serves the liftable value) and push 1013
            // (false,0,0) to connected peers so anyone watching it dissolve can now lift it.
            ScheduleMaterializeFlip(partEntityId, materializeSeconds);

            return partEntityId;
        }

        /// <summary>
        /// Re-creates ONE persisted LOOSE part at boot from its record, via the SAME
        /// <see cref="LoosePartSpawnPlan"/> a runtime craft uses, so it is byte-identical
        /// and the connect-time spawn plan serves it to every joining client. Returns the
        /// allocated part entity id, or null if nothing could be registered.
        ///
        /// Unlike a fresh craft it does NOT play the materialize dissolve (a restored part
        /// is already settled/liftable), does NOT broadcast (there are no peers at boot) and
        /// does NOT re-persist (the record it came from is already on disk). It seeds the
        /// SAME crash-safe base set (190602/190601/1016/1099/1013/1120/8066 + part-specific)
        /// as a live craft, so the client renders and lifts it identically.
        /// </summary>
        internal static long? Restore(LoosePartRecord record)
        {
            LoosePartDefinition part = record.Definition();

            int sequence = LooseParts.NextSequence();
            WorldEntity registration = LoosePartSpawnPlan.For(sequence, record.Position(), part, record.PackedRotation);
            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long partEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            LooseParts.Register(partEntityId, part, record.PartUid);

            Console.WriteLine("[info] loose-part spawn: RESTORED loose '" + part.ItemType + "' (prefab '"
                + part.PrefabName + "') as part entity " + partEntityId + " at " + record.Position()
                + " (owner '" + record.OwnerCharacterUid + "'); it will be served to every joining client"
                + " via the spawn plan.");

            return partEntityId;
        }

        /// <summary>
        /// Moves a part's persisted state back from MOUNTED to LOOSE when it is LIFTED OFF a
        /// ship - called from the 1239 pickup handler after <c>MountedParts.Unmount</c>. The
        /// part's authoritative world position is unchanged (its 190602 spawn seed), so it is
        /// re-persisted as a loose part at that spot, carrying the SAME PartUid so it stays a
        /// single record across the mount/lift cycle. Without this a lift-then-restart would
        /// bring the part back still bolted on where it used to be. A no-op if the part has no
        /// PartUid or is no longer a known loose part.
        /// </summary>
        internal static void RepersistLiftedAsLoose(long partEntityId, string ownerCharacterUid)
        {
            string? partUid = LooseParts.PartUidFor(partEntityId);
            LoosePartDefinition? part = LooseParts.DefFor(partEntityId);
            if (string.IsNullOrEmpty(partUid) || part == null)
            {
                return;
            }

            FixedPointPosition pos = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(partEntityId)?.Position
                ?? Multiplayer.WorldEntities.ShipFramePosition();

            WorldStatePersistence.RemoveMountedPart(partUid!);
            WorldStatePersistence.RecordLoosePart(new LoosePartRecord
            {
                PartUid = partUid!,
                SchematicId = part.SchematicId,
                ItemType = part.ItemType,
                Title = part.Title,
                PrefabName = part.PrefabName,
                AttachmentType = part.AttachmentType,
                PartSpecificComponents = part.PartSpecificComponents.ToArray(),
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                PackedRotation = Multiplayer.Placement.Quaternion32Packing.Identity,
                OwnerCharacterUid = ownerCharacterUid ?? "",
            });
        }

        /// <summary>
        /// Re-creates ONE persisted MOUNTED part at boot, already ATTACHED to its ship's
        /// FRESH boot hull id <paramref name="hullEntityId"/>. It registers the part as a
        /// loose-part world entity (so ShipPartVisualizer's readers + the loose-part serve
        /// branches resolve) AND seeds the <see cref="MountedParts"/> ledger, so the
        /// 8066/190602/1120 mount serve branches re-seed it riding the hull - Parent(hull,"~")
        /// + the stored hull-local offset + the honored packed rotation + attached=true -
        /// exactly as a live 1070 commit's re-checkout does. Returns the part entity id.
        ///
        /// Does NOT broadcast (no peers at boot) and does NOT re-persist. The part's world-
        /// entity position is only a fallback (the mount 190602 branch overrides it with the
        /// hull-local offset), so it is registered at the hull's own world position.
        /// </summary>
        internal static long? RestoreMounted(MountedPartRecord record, long hullEntityId)
        {
            LoosePartDefinition part = record.Definition();

            FixedPointPosition basePos = WorldsAdriftRebornGameServer.WorldEntities.ByEntityId(hullEntityId)?.Position
                ?? Multiplayer.WorldEntities.ShipFramePosition();

            int sequence = LooseParts.NextSequence();
            WorldEntity registration = LoosePartSpawnPlan.For(sequence, basePos, part);
            WorldsAdriftRebornGameServer.WorldEntities.Register(registration);
            long partEntityId = WorldsAdriftRebornGameServer.WorldEntities.EntityIdFor(registration);

            // A mounted part is ALSO a loose part in the ledgers (the 1099/1013/1016 serve
            // branches read LooseParts), exactly as it is after a live craft->mount.
            LooseParts.Register(partEntityId, part, record.PartUid);
            MountedParts.Register(partEntityId, new MountedParts.Mount(
                hullEntityId,
                record.LocalOffset(),
                hullEntityId,
                part.PrefabName,
                part.AttachmentType,
                part.Title,
                part.ItemType,
                record.PackedRotation,
                record.OwnerCharacterUid));

            Console.WriteLine("[info] loose-part spawn: RESTORED MOUNTED '" + part.ItemType + "' (prefab '"
                + part.PrefabName + "') as part entity " + partEntityId + " attached to hull " + hullEntityId
                + " at hull-local " + record.LocalOffset() + " (owner '" + record.OwnerCharacterUid
                + "'); it will be served riding the hull via the spawn plan.");

            return partEntityId;
        }

        /// <summary>
        /// Schedule the one-shot 1013 spawning=true -> false flip that ends a part's
        /// materialize. Runs on the main poll loop (DeferredActions), so enumerating peers
        /// here is on the thread that owns the peer set. The ledger flip (MarkSpawned) makes
        /// every future checkout correct; the push updates peers already watching the dissolve.
        /// </summary>
        private static void ScheduleMaterializeFlip(long partEntityId, float seconds)
        {
            DeferredActions.After(seconds, () =>
            {
                LooseParts.MarkSpawned(partEntityId);

                int pushed = 0;
                foreach (ENetPeerHandle peer in ConnectedPeers())
                {
                    try
                    {
                        CraftableSpawningState.Update update = new CraftableSpawningState.Update();
                        update.SetSpawning(false);
                        update.SetTimeLeft(0f);
                        update.SetTotalTime(0f);
                        if (SendOPHelper.SendComponentUpdateOp(peer, partEntityId,
                                new List<uint> { 1013 }, new List<object> { update }))
                        {
                            pushed++;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[warning] loose-part spawn: could not push part " + partEntityId
                            + " spawning=false to a peer: " + e.Message);
                    }
                }

                Console.WriteLine("[info] loose-part spawn: part " + partEntityId
                    + " materialize complete; flipped 1013 spawning=false (ledger + " + pushed
                    + " peer push(es)); it is now liftable.");
            });
        }

        /// <summary>
        /// Applies the per-part prefab/attachment env overrides so a live prefab-name
        /// mismatch is a config change, not a rebuild - the escape hatch every row
        /// needs because a prefab name is the one value the client decompile cannot
        /// confirm (it only reads it back from 1120). For ANY part, the generic
        /// <c>WAREBORN_PART_PREFAB__&lt;schematicId&gt;</c> /
        /// <c>WAREBORN_PART_ATTACH__&lt;schematicId&gt;</c> pair wins first; the lamp
        /// additionally honours its original <c>WAREBORN_LAMP_PREFAB</c> /
        /// <c>WAREBORN_LAMP_ATTACH</c> names for back-compat. A blank/unset variable
        /// keeps the catalogue default.
        /// </summary>
        private static LoosePartDefinition ApplyEnvOverrides(LoosePartDefinition definition)
        {
            string? prefab = Environment.GetEnvironmentVariable("WAREBORN_PART_PREFAB__" + definition.SchematicId);
            string? attach = Environment.GetEnvironmentVariable("WAREBORN_PART_ATTACH__" + definition.SchematicId);

            // Legacy lamp-specific names, kept working; the generic per-part names above
            // take precedence when both are set.
            if (definition.SchematicId == LoosePartCatalogue.LampSchematicId)
            {
                if (string.IsNullOrWhiteSpace(prefab))
                {
                    prefab = Environment.GetEnvironmentVariable("WAREBORN_LAMP_PREFAB");
                }
                if (string.IsNullOrWhiteSpace(attach))
                {
                    attach = Environment.GetEnvironmentVariable("WAREBORN_LAMP_ATTACH");
                }
            }

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
