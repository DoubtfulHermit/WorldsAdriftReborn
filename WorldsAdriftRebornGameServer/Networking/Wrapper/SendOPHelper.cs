using System.Collections.Generic;
using System.ComponentModel;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using static WorldsAdriftRebornGameServer.DLLCommunication.EnetLayer;

namespace WorldsAdriftRebornGameServer.Networking.Wrapper
{
    internal class SendOPHelper
    {
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
                return SendAddComponentOp(destination, entityId, interestsArray, (uint)interests.Count);
            }
        }

        public static unsafe bool SendAddComponentOp(ENetPeerHandle destination, long entityId, Structs.Structs.InterestOverride* interests, uint interestCount, bool failOnComponentInitError = false )
        {
            List<Structs.Structs.AddComponentOp> serializedComponents = new List<Structs.Structs.AddComponentOp>();

            for (int i = 0; i < interestCount; i++)
            {
                uint len = 0;
                byte* buffer;
                ComponentsSerializer.InitAndSerialize(destination, entityId, interests[i].ComponentId, &buffer, &len);

                if (len <= 0)
                {
                    Console.WriteLine("[error] failed to initialize component " + interests[i].ComponentId);
                    if (failOnComponentInitError)
                    {
                        Console.WriteLine("[info] aborting send of components.");
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

            fixed (Structs.Structs.AddComponentOp* comps = serializedComponents.ToArray())
            {
                int len = 0;
                void* ptr = EnetLayer.PB_EXP_AddComponentOp_Serialize(entityId, comps, (uint)serializedComponents.Count, &len);

                if (ptr != null && len > 0)
                {
                    Console.WriteLine("[success] serialized all requested components, sending them to the game now...");

                    EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.SEND_COMPONENT_INTEREST, ptr, len, (int)ENetPacketFlag.RELIABLE);

                    return true;
                }
            }

            return false;
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
                        bool highRate = (componentId == 190602 || componentId == 1073);
                        EnetLayer.ENet_Send(destination, (int)EnetLayer.ENetChannel.COMPONENT_UPDATE_OP, ptr, len,
                            (int)(highRate ? ENetPacketFlag.UNRELIABLE : ENetPacketFlag.RELIABLE));
                        return true;
                    }
                }
            }

            return false;
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

                return true;
            }
        }
    }
}
