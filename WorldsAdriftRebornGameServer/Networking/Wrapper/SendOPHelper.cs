using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using static WorldsAdriftRebornGameServer.DLLCommunication.EnetLayer;

namespace WorldsAdriftRebornGameServer.Networking.Wrapper
{
    internal class SendOPHelper
    {
        /// <summary>
        /// Wire metrics, send side. Every op this server sends leaves through
        /// this class, so this is the one place the outbound half of the 5 s
        /// [rates] line can be counted. Component updates are keyed by component
        /// id; everything else by its ENet channel (PeerRates.ChannelKey).
        /// Counted only on successful serialization+queue, mirroring what each
        /// method reports to its caller.
        /// </summary>
        private static void CountSend(ENetPeerHandle destination, uint key)
        {
            WorldsAdriftRebornGameServer.Rates.RecordSend(PeerIdentity.IdOf(destination), key);
        }

        public static unsafe bool SendAddEntityOP(ENetPeerHandle destination, long entityId, string prefabName, string prefabContext)
        {
            Structs.Structs.AddEntityOp addEntityOp;

            fixed (byte* pn = Translator.ToUtf8Cstr(prefabName))
            {
                fixed (byte* pc = Translator.ToUtf8Cstr(prefabContext))
                {
                    addEntityOp.PrefabName = pn;
                    addEntityOp.PrefabContext = pc;

                    int len = 0;

                    void* ptr = EnetLayer.PB_AddEntityOp_Serialize(&addEntityOp, &len, entityId);

                    if (ptr != null && len != 0)
                    {
                        EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.ADD_ENTITY_OP, ptr, len, (int)ENetPacketFlag.RELIABLE);
                        CountSend(destination, Multiplayer.PeerRates.ChannelKey((int)EnetLayer.ENetChannel.ADD_ENTITY_OP));
                        return true;
                    }
                    return false;
                }
            }
        }

        public static unsafe bool SendAssetLoadRequestOP(ENetPeerHandle destination, string assetType, string assetName, string assetContext)
        {
            Structs.Structs.AssetLoadRequestOp assetLoadRequestOp;

            fixed (byte* at = Translator.ToUtf8Cstr(assetType))
            {
                fixed (byte* name = Translator.ToUtf8Cstr(assetName))
                {
                    fixed (byte* context = Translator.ToUtf8Cstr(assetContext))
                    {
                        assetLoadRequestOp.AssetType = at;
                        assetLoadRequestOp.Name = name;
                        assetLoadRequestOp.Context = context;

                        int len = 0;

                        void* ptr = EnetLayer.PB_AssetLoadRequestOp_Serialize(&assetLoadRequestOp, &len);

                        if (ptr != null && len != 0)
                        {
                            EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.ASSET_LOAD_REQUEST_OP, ptr, len, (int)ENetPacketFlag.RELIABLE);
                            CountSend(destination, Multiplayer.PeerRates.ChannelKey((int)EnetLayer.ENetChannel.ASSET_LOAD_REQUEST_OP));
                            return true;
                        }
                        return false;
                    }
                }
            }
        }

        public static unsafe bool SendAddComponentOp( ENetPeerHandle destination, long entityId, List<Structs.Structs.InterestOverride> interests, bool failOnComponentInitError = false )
        {
            fixed(Structs.Structs.InterestOverride* interestsArray = interests.ToArray())
            {
                return SendAddComponentOp(destination, entityId, interestsArray, (uint)interests.Count, failOnComponentInitError);
            }
        }

        /// <summary>
        /// Serializes a batch of components for one entity and sends it.
        ///
        /// THE DIAGNOSTIC THIS METHOD OWES ITS CALLERS. When
        /// <paramref name="failOnComponentInitError"/> is true - which it is on
        /// every non-mirror path - ONE component id with no branch in
        /// ComponentsSerializer drops the ENTIRE batch. The entity is already
        /// created by then, so what you get is a fully-rendered, completely inert
        /// object: right model, right place, no behaviour, and nothing in the
        /// client log at all.
        ///
        /// The whole requested list is therefore printed BEFORE any of it is
        /// attempted, not just the id that failed. The ten-id [Require] closure
        /// anyone derives statically is a lower bound: the client's
        /// ExtractVisualizers walks the entire prefab hierarchy, so the list it
        /// actually asks for is only knowable by reading it off the wire. That
        /// reading is what these two lines are for; see
        /// docs/research/loop/findings-harvestable-world.md step 0.
        ///
        /// THE ONE EXCEPTION TO ALL-OR-NOTHING: a component the server has
        /// DECIDED this entity does not have. In real SpatialOS a ComponentInterest
        /// is answered with the subset the entity actually has, so answering four
        /// ids with three is the normal case, not a fault. Those ids are skipped
        /// here without failing the batch and without an [error]; see
        /// Multiplayer.ComponentAbsencePolicy for which ones and why. An id
        /// nobody predicted is still fatal and still loud - that distinction is
        /// the entire value of this diagnostic.
        /// </summary>
        public static unsafe bool SendAddComponentOp(ENetPeerHandle destination, long entityId, Structs.Structs.InterestOverride* interests, uint interestCount, bool failOnComponentInitError = false )
        {
            List<Structs.Structs.AddComponentOp> serializedComponents = new List<Structs.Structs.AddComponentOp>();

            Console.WriteLine("[interest] entity " + entityId + " wants " + interestCount + " component(s): "
                + DescribeInterests(interests, interestCount)
                + (failOnComponentInitError
                    ? " (ALL-OR-NOTHING: one UNSEEDED id drops the whole batch; ids the entity"
                      + " is known not to have are omitted without failing)"
                    : " (best effort: unseeded ids are skipped)"));

            int knownAbsent = 0;

            for (int i = 0; i < interestCount; i++)
            {
                uint len = 0;
                byte* buffer;
                Multiplayer.ComponentSeedOutcome outcome = ComponentsSerializer.InitAndSerialize(
                    destination, entityId, interests[i].ComponentId, &buffer, &len);

                // Deliberately absent: no bytes, no error, no batch failure. The
                // serializer has already printed its own [known-absent] line, so
                // this path stays silent and only the per-batch tally below
                // reports it.
                if (outcome == Multiplayer.ComponentSeedOutcome.KnownAbsent)
                {
                    knownAbsent++;
                    continue;
                }

                if (!Multiplayer.ComponentAbsencePolicy.BelongsInBatch(outcome) || len <= 0)
                {
                    Console.WriteLine("[error] failed to initialize component " + interests[i].ComponentId
                        + " of entity " + entityId + " (component " + (i + 1) + " of " + interestCount
                        + "; " + serializedComponents.Count + " already serialized; outcome " + outcome + ").");
                    if (Multiplayer.ComponentAbsencePolicy.DropsBatch(outcome, failOnComponentInitError))
                    {
                        // Not "one component missing" - EVERY component in this
                        // batch is being thrown away, including the ones that
                        // serialized fine, and the entity keeps whatever it had.
                        Console.WriteLine("[error] DROPPING the whole AddComponent batch for entity " + entityId
                            + " because component " + interests[i].ComponentId + " has no seed."
                            + " The entity will render and do nothing. Requested: "
                            + DescribeInterests(interests, interestCount));
                        return false;
                    }
                    continue;
                }

                Console.WriteLine("[success] initialized and serialized componentId " + interests[i].ComponentId);
                Structs.Structs.AddComponentOp component;

                component.ComponentId = interests[i].ComponentId;
                component.ComponentData = buffer;
                component.DataLength = (int)len;

                serializedComponents.Add(component);
            }

            // One line per batch, not per component: "the client asked for four
            // and got three, on purpose". Only printed when something was
            // actually left out.
            if (knownAbsent > 0)
            {
                Console.WriteLine(Multiplayer.ComponentAbsencePolicy.DescribeBatchOmissions(
                    entityId, interestCount, serializedComponents.Count, knownAbsent));
            }

            // An interest answered entirely with "the entity does not have any of
            // those" is a SUCCESS with nothing to send. Falling through would
            // take `fixed` over an empty array - a null pointer - and report
            // failure, which on the setup path costs the caller the rest of its
            // sequence via `continue`.
            if (serializedComponents.Count == 0 && knownAbsent == interestCount && interestCount > 0)
            {
                return true;
            }

            fixed (Structs.Structs.AddComponentOp* comps = serializedComponents.ToArray())
            {
                int len = 0;
                void* ptr = EnetLayer.PB_EXP_AddComponentOp_Serialize(entityId, comps, (uint)serializedComponents.Count, &len);

                if (ptr != null && len > 0)
                {
                    Console.WriteLine("[success] serialized all requested components, sending them to the game now...");

                    EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.SEND_COMPONENT_INTEREST, ptr, len, (int)ENetPacketFlag.RELIABLE);
                    CountSend(destination, Multiplayer.PeerRates.ChannelKey((int)EnetLayer.ENetChannel.SEND_COMPONENT_INTEREST));

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The requested component ids as a flat, greppable list. Ids only - the
        /// InterestOverride's other field is the interest LEVEL, which is 1
        /// everywhere in this server and has never been the thing that was wrong.
        /// </summary>
        private static unsafe string DescribeInterests(Structs.Structs.InterestOverride* interests, uint interestCount)
        {
            if (interests == null || interestCount == 0)
            {
                return "[]";
            }

            System.Text.StringBuilder ids = new System.Text.StringBuilder("[");
            for (int i = 0; i < interestCount; i++)
            {
                if (i > 0)
                {
                    ids.Append(", ");
                }
                ids.Append(interests[i].ComponentId);
            }
            return ids.Append(']').ToString();
        }

        /// <summary>
        /// Forwards an already-serialized component update to another client
        /// without touching its contents.
        ///
        /// Distinct from <see cref="SendComponentUpdateOp"/>, which re-serializes
        /// from live component objects. Relaying one player's movement to another
        /// must not do that: the server has handlers for only a handful of the
        /// component ids in play, so it cannot round-trip most of them, and
        /// re-serializing would add failure modes for no benefit.
        /// </summary>
        public static unsafe bool SendRawComponentUpdateOp(ENetPeerHandle destination, long entityId, uint componentId, byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return false;
            }

            fixed (byte* raw = data)
            {
                Structs.Structs.ComponentUpdateOp cupdate;
                cupdate.ComponentId = componentId;
                cupdate.ComponentData = raw;
                cupdate.DataLength = data.Length;

                Structs.Structs.ComponentUpdateOp[] one = { cupdate };

                fixed (Structs.Structs.ComponentUpdateOp* u = one)
                {
                    int len = 0;
                    void* ptr = EnetLayer.PB_EXP_ComponentUpdateOp_Serialize(entityId, u, 1, &len);

                    if (ptr != null && len > 0)
                    {
                        // High-rate streams (190602 transform, 1073 bone/animation)
                        // go UNRELIABLE: they are superseded every tick, so a lost
                        // packet is irrelevant, while reliable-ordered delivery
                        // causes head-of-line stalls on any loss - very visible as
                        // stutter over the internet. Everything else stays reliable.
                        // The classification itself lives in MirrorSendPolicy so it
                        // is testable without a packet.
                        bool highRate = Multiplayer.MirrorSendPolicy.RelayReliabilityFor(componentId)
                                        == Multiplayer.RelayReliability.Unreliable;
                        EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.COMPONENT_UPDATE_OP, ptr, len,
                            (int)(highRate ? ENetPacketFlag.UNRELIABLE : ENetPacketFlag.RELIABLE));
                        CountSend(destination, componentId);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Serializes ONE typed component update into the same payload bytes a
        /// client would have put on the wire, using the game's own generated
        /// serializer - the exact machinery <see cref="SendComponentUpdateOp"/>
        /// uses, minus the send and minus the per-call console lines.
        ///
        /// It exists for the relay emitter, which serializes at emit cadence
        /// (20 Hz x players x recipients) rather than once per rare event, so two
        /// things matter here that the older method could ignore:
        ///
        /// * IT MUST NOT LOG. A console line per call is the ServerLog bug all
        ///   over again.
        /// * IT MUST NOT LEAK. The generated serializer hands back a buffer
        ///   allocated with AllocHGlobal (ExpandableUnmanagedMemoryStream.
        ///   TakeOwnershipOfBuffer - ownership is the caller's, the name says
        ///   so). SendComponentUpdateOp never frees it, which is invisible at a
        ///   tree-cut every 0.75 s and is ~50 KB/s of unmanaged leak at emit
        ///   cadence. The bytes are copied to a managed array and the native
        ///   buffer freed immediately.
        ///
        /// Returns null when the serializer produced nothing, which for a
        /// well-formed update of a known component does not happen.
        /// </summary>
        public static unsafe byte[]? SerializeComponentUpdatePayload(uint componentId, object update)
        {
            ComponentProtocol.ClientSerialize serializer = ComponentsManager.Instance.GetSerializerForComponent(componentId);

            ComponentProtocol.ClientObject* cobj = ClientObjects.ObjectAlloc();
            cobj->Reference = ClientObjects.Instance.CreateReference(update);

            byte* cbuffer = null;
            uint len = 0;
            byte[]? payload = null;
            try
            {
                serializer(componentId, 1, cobj, &cbuffer, &len);

                if (cbuffer != null && len > 0)
                {
                    payload = new byte[len];
                    Marshal.Copy(new IntPtr(cbuffer), payload, 0, (int)len);
                }
            }
            finally
            {
                if (cbuffer != null)
                {
                    ClientObjects.BufferFree(cbuffer);
                }
                // ObjectFree also destroys the reference (see the SDK's
                // ObjectFree: DestroyReference then FreeHGlobal).
                ClientObjects.ObjectFree(componentId, 1, cobj);
            }

            return payload;
        }

        public static unsafe bool SendComponentUpdateOp(ENetPeerHandle destination, long entityId, List<uint> componentId, List<object> updates )
        {
            if(componentId.Count != updates.Count)
            {
                Console.WriteLine("[error] SendComponentUpdateOp: component id's and update count must match.");
                return false;
            }

            List<Structs.Structs.ComponentUpdateOp> cupdates = new List<Structs.Structs.ComponentUpdateOp>();

            for(int i = 0; i < updates.Count; i++)
            {
                ComponentProtocol.ClientSerialize serializer = ComponentsManager.Instance.GetSerializerForComponent(componentId[i]);
                ulong refId = ClientObjects.Instance.CreateReference(updates[i]);

                ComponentProtocol.ClientObject* cobj = ClientObjects.ObjectAlloc();
                byte* cbuffer = null;
                uint len = 0;
                Structs.Structs.ComponentUpdateOp cupdate;

                cobj->Reference = refId;
                serializer(componentId[i], 1, cobj, &cbuffer, &len);

                if(len > 0)
                {
                    Console.WriteLine("[success] serialized stored component after update. " + componentId[i] + ")");

                    cupdate.ComponentId = componentId[i];
                    cupdate.ComponentData = cbuffer;
                    cupdate.DataLength = (int)len;

                    cupdates.Add(cupdate);
                }

                ClientObjects.Instance.DestroyReference(cobj->Reference);
                ClientObjects.ObjectFree(componentId[i], 1, cobj);
            }

            fixed (Structs.Structs.ComponentUpdateOp* u = cupdates.ToArray())
            {
                int len = 0;
                void* ptr = EnetLayer.PB_EXP_ComponentUpdateOp_Serialize(entityId, u, (uint)updates.Count, &len);

                if(ptr != null && len > 0)
                {
                    Console.WriteLine("[success] serialized ComponentUpdateOp message for client.");

                    EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.COMPONENT_UPDATE_OP, ptr, len, (int)ENetPacketFlag.RELIABLE);
                    foreach (Structs.Structs.ComponentUpdateOp sent in cupdates)
                    {
                        CountSend(destination, sent.ComponentId);
                    }

                    return true;
                }
            }

            return false;
        }

        public static unsafe bool SendAuthorityChangeOp(ENetPeerHandle destination, long entityId, List<uint> components)
        {
            fixed (Structs.Structs.AuthorityChangeOp* authChangeOps = components.Select(p => new Structs.Structs.AuthorityChangeOp(p, true)).ToArray())
            {
                int len = 0;
                void* ptr = EnetLayer.PB_EXP_AuthorityChangeOp_Serialize(entityId, authChangeOps, (uint)components.Count, &len);

                if (ptr == null || len <= 0)
                {
                    Console.WriteLine("[error] failed to serialize AuthorityChangeOp for component");
                    return false;
                }

                Console.WriteLine("[info] serialized all AuthorityChangeOp instructions for authoritative components.");
                EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.AUTHORITY_CHANGE_OP, ptr, len, (int)ENetPacketFlag.RELIABLE);
                CountSend(destination, Multiplayer.PeerRates.ChannelKey((int)EnetLayer.ENetChannel.AUTHORITY_CHANGE_OP));

                return true;
            }
        }
    }
}
