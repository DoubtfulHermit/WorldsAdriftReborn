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
        private static class HashCache<T>
        {
            public static bool Initialized;
            public static ulong Id;
        }

        protected delegate void RegisterDelegate(ENetPeerHandle player, long entityId, object clientComponentUpdate, object serverComponentData);
        private readonly Dictionary<ulong, RegisterDelegate> _handlers = new Dictionary<ulong, RegisterDelegate>();

        //FNV-1 64 bit hash
        public ulong GetHash<T>()
        {
            if (HashCache<T>.Initialized)
            {
                return HashCache<T>.Id;
            }

            ulong hash = 14695981039346656037UL; //offset
            string typeName = typeof(T).FullName;
            for (int i = 0; i < typeName.Length; i++)
            {
                hash ^= typeName[i];
                hash *= 1099511628211UL; //prime
            }
            HashCache<T>.Initialized = true;
            HashCache<T>.Id = hash;
            return hash;
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

        public unsafe bool HandleComponentUpdate(ENetPeerHandle player, long entityId, uint componentId, byte* componentData, int componentDataLength)
        {
            bool success = false;
            ServerLog.Trace("[info] trying to handle a ComponentUpdateOp for ", componentId);

            for (int i = 0; i < ComponentsManager.Instance.ClientComponentVtables.Length; i++)
            {
                if (ComponentsManager.Instance.ClientComponentVtables[i].ComponentId == componentId)
                {
                    if (GameState.Instance.ComponentMap.ContainsKey(player) && GameState.Instance.ComponentMap[player].ContainsKey(entityId) && GameState.Instance.ComponentMap[player][entityId].ContainsKey(componentId))
                    {

                        ComponentProtocol.ClientObject* wrapper = ClientObjects.ObjectAlloc();
                        ComponentProtocol.ClientDeserialize deserialize = Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientDeserialize>(ComponentsManager.Instance.ClientComponentVtables[i].Deserialize);

                        if (deserialize(componentId, 1, componentData, (uint)componentDataLength, &wrapper))
                        {
                            // now we got a reference to the deserialized component, we can use it to update the component that we already have for the player.
                            object storedComponent = ClientObjects.Instance.Dereference(GameState.Instance.ComponentMap[player][entityId][componentId]);
                            object newComponent = ClientObjects.Instance.Dereference(wrapper->Reference);

                            ulong hash = 0;
                            MethodInfo genericGetHash = this.GetType().GetMethods()
                            .Where(m => m.Name == nameof(GetHash))
                            .Where(m => m.IsGenericMethod)
                            .FirstOrDefault();

                            foreach (IComponentMetaclass componentMetaclass in ComponentDatabase.MetaclassMap.Values)
                            {
                                IComponentFactory componentFactory = componentMetaclass as IComponentFactory;
                                if(componentFactory != null && genericGetHash != null && componentFactory.ComponentId == componentId)
                                {
                                    MethodInfo getHash = genericGetHash.MakeGenericMethod(componentFactory.GetType());
                                    hash = (ulong)getHash.Invoke(this, new object[] { });
                                    break;
                                }
                            }

                            if(_handlers.TryGetValue(hash, out RegisterDelegate handler))
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

                    break;
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
