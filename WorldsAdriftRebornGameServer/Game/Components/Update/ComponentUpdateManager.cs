using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Improbable.Entity.Component;
using Improbable.Worker;
using Improbable.Worker.Internal;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Game.Components.Update.Handlers;
using WorldsAdriftRebornGameServer.Networking.Singleton;
using Bossa.Travellers.Inventory;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.Refdata;

namespace WorldsAdriftRebornGameServer.Game.Components.Update
{
    internal class ComponentUpdateManager
    {
        private static ComponentUpdateManager instance { get; set; }
        public static ComponentUpdateManager Instance
        {
            get
            {
                return instance ?? (instance = new ComponentUpdateManager());
            }
        }
        protected delegate void RegisterDelegate(ENetPeerHandle player, long entityId, object clientComponentUpdate, object serverComponentData);
        private readonly Dictionary<ulong, RegisterDelegate> _handlers = new Dictionary<ulong, RegisterDelegate>();

        /// <summary>
        /// Everything the per-packet path needs for one component id, resolved
        /// once and remembered. Before this cache existed, EVERY inbound
        /// component update paid: a linear scan of ClientComponentVtables, a
        /// GetDelegateForFunctionPointer, a GetMethods()+LINQ reflection lookup,
        /// a full scan of all ~443 ComponentDatabase.MetaclassMap entries with an
        /// interface cast each, and a MakeGenericMethod+Invoke - to recompute a
        /// value that is a PURE function of the component id. At two players'
        /// update rates that cost alone could exceed packet inter-arrival time,
        /// which (with the old one-packet-per-iteration pump) is exactly the
        /// unbounded-queue death spiral observed live. Now a packet costs one
        /// dictionary lookup plus one delegate call.
        /// </summary>
        private readonly struct DispatchEntry
        {
            /// <summary>Cached vtable deserializer; null = the game defines no vtable for this id.</summary>
            public readonly ComponentProtocol.ClientDeserialize? Deserialize;

            /// <summary>Handler-table key (hash of the metaclass type name); 0 = no metaclass found.</summary>
            public readonly ulong HandlerHash;

            public DispatchEntry(ComponentProtocol.ClientDeserialize? deserialize, ulong handlerHash)
            {
                Deserialize = deserialize;
                HandlerHash = handlerHash;
            }
        }

        private readonly Dictionary<uint, DispatchEntry> _dispatchCache = new Dictionary<uint, DispatchEntry>();

        // The hash that keys _handlers. The algorithm lives in the pure
        // Multiplayer assembly (ComponentHash) with pinned test vectors, because
        // registration (this method, hashing TBase) and dispatch (hashing the
        // metaclass type) only ever meet if they hash identically.
        public ulong GetHash<T>()
        {
            return Multiplayer.ComponentHash.OfTypeFullName(typeof(T).FullName!);
        }
        private ComponentUpdateManager()
        {
            // Only scan our own assembly: every handler is defined here, and calling
            // GetTypes() on the game's assemblies (Generated.Code, Assembly-CSharp)
            // aborts the process with a fatal CLR error (0x80131506) under .NET 6,
            // which no try/catch can recover from.
            RegisterAllComponentUpdateHandlers(Assembly.GetExecutingAssembly());
        }
        private static bool IsSubclassOfRawGeneric( Type generic, Type toCheck )
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                Type cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                {
                    return true;
                }
                toCheck = toCheck.BaseType;
            }
            return false;
        }
        private void RegisterAllComponentUpdateHandlers(Assembly assembly)
        {
            IEnumerable<Type> definedHandlers = assembly.GetTypes()
                .Where(t => t.GetCustomAttributes(typeof(RegisterComponentUpdateHandler), true).Length > 0);

            MethodInfo registerMethod = this.GetType().GetMethods()
                .Where(m => m.Name == nameof(RegisterComponentUpdateHandler))
                .Where(m => m.IsGenericMethod)
                .FirstOrDefault();

            foreach(Type type in definedHandlers)
            {
                if(IsSubclassOfRawGeneric(typeof(IComponentUpdateHandler<,,>), type))
                {
                    Type type_baseComponentUpdate = type.BaseType.GetGenericArguments()[0];
                    Type type_clientComponentUpdate = type.BaseType.GetGenericArguments()[1];
                    Type type_serverComponentData = type.BaseType.GetGenericArguments()[2];

                    // dynamically create instance of handler
                    Type handlerMethodArgTypes = typeof(Action<,,,>).MakeGenericType(typeof(ENetPeerHandle), typeof(long), type_clientComponentUpdate, type_serverComponentData);
                    object handler = Activator.CreateInstance(type);
                    Delegate handlerMethod = Delegate.CreateDelegate(handlerMethodArgTypes, handler, type.GetMethod("HandleUpdate", new Type[] { typeof(ENetPeerHandle), typeof(long), type_clientComponentUpdate, type_serverComponentData }));

                    // register created handler
                    MethodInfo genericRegisterComponent = registerMethod.MakeGenericMethod(type_baseComponentUpdate, type_clientComponentUpdate, type_serverComponentData);
                    genericRegisterComponent.Invoke(this, new object[] { handlerMethod });

                    Console.WriteLine("[success] registered ComponentUpdate handler for type " + type_baseComponentUpdate);
                }
            }
        }
        public void RegisterComponentUpdateHandler<TBase, TClient, TServer>(Action<ENetPeerHandle, long, TClient, TServer> onProcess)
        {
            ulong hash = GetHash<TBase>();
            if (!_handlers.ContainsKey(hash))
            {
                _handlers.Add(hash, null);
            }

            _handlers[hash] = ( ENetPeerHandle player, long entityId, object clientComponentUpdate, object serverComponentData ) =>
            {
                onProcess(player, entityId, (TClient)clientComponentUpdate, (TServer)serverComponentData);
            };
        }

        /// <summary>
        /// The per-id lookup, memoized. The vtable array is fixed at startup and
        /// the metaclass map is the game's own static database, so both scans are
        /// pure functions of the component id: the FIRST packet of an id pays the
        /// old linear-scan price, every later one is a dictionary hit. "No vtable"
        /// is cached too - the ids the game sends but never defined were the ones
        /// paying the full scan on EVERY packet.
        /// </summary>
        private DispatchEntry DispatchFor(uint componentId)
        {
            if (_dispatchCache.TryGetValue(componentId, out DispatchEntry cached))
            {
                return cached;
            }

            ComponentProtocol.ClientDeserialize? deserialize = null;
            for (int i = 0; i < ComponentsManager.Instance.ClientComponentVtables.Length; i++)
            {
                if (ComponentsManager.Instance.ClientComponentVtables[i].ComponentId == componentId)
                {
                    // Caching the delegate also keeps it (and its function
                    // pointer wrapper) alive for the process lifetime.
                    deserialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientDeserialize>(ComponentsManager.Instance.ClientComponentVtables[i].Deserialize);
                    break;
                }
            }

            ulong hash = 0;
            if (deserialize != null)
            {
                foreach (IComponentMetaclass componentMetaclass in ComponentDatabase.MetaclassMap.Values)
                {
                    if (componentMetaclass is IComponentFactory componentFactory && componentFactory.ComponentId == componentId)
                    {
                        // Same value the old reflective path produced: it called
                        // GetHash<factory type>() via MakeGenericMethod, which is
                        // FNV over the factory type's FullName.
                        hash = Multiplayer.ComponentHash.OfTypeFullName(componentFactory.GetType().FullName!);
                        break;
                    }
                }
            }

            DispatchEntry entry = new DispatchEntry(deserialize, hash);
            _dispatchCache[componentId] = entry;
            return entry;
        }

        public unsafe bool HandleComponentUpdate(ENetPeerHandle player, long entityId, uint componentId, byte* componentData, int componentDataLength)
        {
            bool success = false;
            ServerLog.Trace("[info] trying to handle a ComponentUpdateOp for ", componentId);

            DispatchEntry dispatch = DispatchFor(componentId);

            if (dispatch.Deserialize != null)
            {
                if (GameState.Instance.ComponentMap.ContainsKey(player) && GameState.Instance.ComponentMap[player].ContainsKey(entityId) && GameState.Instance.ComponentMap[player][entityId].ContainsKey(componentId))
                {

                    ComponentProtocol.ClientObject* wrapper = ClientObjects.ObjectAlloc();

                    if (dispatch.Deserialize(componentId, 1, componentData, (uint)componentDataLength, &wrapper))
                    {
                        // now we got a reference to the deserialized component, we can use it to update the component that we already have for the player.
                        object storedComponent = ClientObjects.Instance.Dereference(GameState.Instance.ComponentMap[player][entityId][componentId]);
                        object newComponent = ClientObjects.Instance.Dereference(wrapper->Reference);

                        // The handler table is consulted per packet (not baked
                        // into the cache entry) so a handler registered after a
                        // component's first packet would still be found - the
                        // same order-independence the old path had.
                        if (_handlers.TryGetValue(dispatch.HandlerHash, out RegisterDelegate handler))
                        {
                            handler(player, entityId, newComponent, storedComponent);
                            success = true;
                        }

                        if (!success)
                        {
                            ServerLog.Trace("[warning] could not find a handler for component update on ", componentId);
                        }

                        ClientObjects.Instance.DestroyReference(wrapper->Reference);
                    }
                    else
                    {
                        ServerLog.Trace("[error] failed to deserialize ComponentUpdateOp data for id ", componentId);
                    }

                    ClientObjects.ObjectFree(componentId, 1, wrapper);

                }
                else
                {
                    ServerLog.Trace("[warning] could not match requested ComponentUpdate with local stored values.");
                }
            }

            if (!success)
            {
                ServerLog.Trace("[error] if no other error above, no matching component for id ", componentId, " defined in the game.");
            }
            return success;
        }
    }
}
