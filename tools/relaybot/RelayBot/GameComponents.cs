using System.Runtime.InteropServices;
using Improbable.Worker.Internal;

namespace RelayBot
{
    /// <summary>
    /// The INNER component payload codec: the game's own generated vtable
    /// machinery, driven exactly the way the game server drives it
    /// (SendOPHelper.SendComponentUpdateOp / ComponentUpdateManager). Bytes
    /// produced here are what a real client produces, because they come from the
    /// same generated code.
    ///
    /// objType is the SDK's ClientObjectType: 1 = Update, 2 = Snapshot/Data.
    /// The server serializes seeds with 2 and deserializes client updates with 1;
    /// this bot does the mirror image.
    /// </summary>
    public static class GameComponents
    {
        public const byte TypeUpdate = 1;
        public const byte TypeSnapshot = 2;

        /// <summary>
        /// One process-wide lock around every vtable call. Two bots serialize
        /// concurrently at ~72 calls/s total; the generated code and
        /// ClientObjects' reference table were only ever exercised
        /// single-threaded by the server, so this buys safety at a cost nothing
        /// here can measure.
        /// </summary>
        private static readonly object Gate = new object();

        private static readonly Dictionary<uint, (ComponentProtocol.ClientSerialize Serialize,
            ComponentProtocol.ClientDeserialize Deserialize,
            ComponentProtocol.ClientBufferFree BufferFree)> Codecs = new();

        static GameComponents()
        {
            // The game SDK has exactly two native imports (verified with ilspycmd
            // over all three assemblies): "CoreSdkDll" - the shim, our native
            // build of which Enet.cs already resolves - and ONE import from
            // "msvcrt.dll": memcpy, used by ExpandableUnmanagedMemoryStream when
            // a serialized component outgrows its first buffer. glibc's memcpy
            // has the identical contract, so on Linux msvcrt maps to libc.
            NativeLibrary.SetDllImportResolver(typeof(ClientObjects).Assembly, (name, _, _) =>
            {
                if (name.StartsWith("msvcrt", StringComparison.OrdinalIgnoreCase))
                {
                    return NativeLibrary.Load("libc.so.6");
                }
                if (name == "CoreSdkDll")
                {
                    // Same probe logic as the bot's own transport imports.
                    return NativeLibrary.Load("libCoreSdkDll.so",
                        typeof(Enet).Assembly, DllImportSearchPath.AssemblyDirectory);
                }
                return IntPtr.Zero;
            });

            // Force-load Generated.Code BEFORE the first MetaclassMap access: the
            // database's private ctor scans currently-loaded assemblies exactly
            // once, and an early read leaves it permanently empty
            // (docs/multiplayer.md rule 8 - same trap, client side).
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(Improbable.Corelibrary.Transforms.TransformState).TypeHandle);

            if (ComponentDatabase.MetaclassMap.Count == 0)
            {
                throw new InvalidOperationException("ComponentDatabase.MetaclassMap is empty - Generated.Code did not load.");
            }
        }

        private static (ComponentProtocol.ClientSerialize, ComponentProtocol.ClientDeserialize, ComponentProtocol.ClientBufferFree) CodecFor(uint componentId)
        {
            if (!Codecs.TryGetValue(componentId, out var codec))
            {
                ComponentProtocol.ClientComponentVtable vt = ComponentDatabase.MetaclassMap[componentId].Vtable;
                codec = (
                    Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientSerialize>(vt.Serialize),
                    Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientDeserialize>(vt.Deserialize),
                    Marshal.GetDelegateForFunctionPointer<ComponentProtocol.ClientBufferFree>(vt.BufferFree));
                Codecs[componentId] = codec;
            }
            return codec;
        }

        /// <summary>Serializes a generated Update/Data object to its wire bytes.</summary>
        public static unsafe byte[] Serialize(uint componentId, byte objType, object componentObject)
        {
            lock (Gate)
            {
                var (serialize, _, bufferFree) = CodecFor(componentId);

                ulong refId = ClientObjects.Instance.CreateReference(componentObject);
                ComponentProtocol.ClientObject* wrapper = ClientObjects.ObjectAlloc();
                try
                {
                    wrapper->Reference = refId;
                    byte* buffer = null;
                    uint length = 0;
                    serialize(componentId, objType, wrapper, &buffer, &length);
                    if (buffer == null || length == 0)
                    {
                        throw new InvalidOperationException("vtable serialize produced no bytes for component " + componentId);
                    }

                    byte[] result = new byte[length];
                    Marshal.Copy((IntPtr)buffer, result, 0, (int)length);
                    bufferFree(componentId, buffer);
                    return result;
                }
                finally
                {
                    ClientObjects.Instance.DestroyReference(refId);
                    Marshal.FreeHGlobal((IntPtr)wrapper);
                }
            }
        }

        /// <summary>
        /// Deserializes wire bytes back to the generated object
        /// (TransformState.Update for (190602, TypeUpdate), and so on).
        /// Returns null when the generated code rejects the bytes.
        /// </summary>
        public static unsafe object Deserialize(uint componentId, byte objType, byte[] data, int length)
        {
            lock (Gate)
            {
                var (_, deserialize, _) = CodecFor(componentId);

                // The generated thunk ignores what the pointer refers to on
                // entry: it writes null, then ObjectAlloc()s its OWN wrapper into
                // *obj and stores the managed reference id in it. So pass an
                // empty slot and free what comes back. (The server pre-allocates
                // a wrapper here and leaks it on every packet; not copied.)
                ComponentProtocol.ClientObject* wrapper = null;
                bool ok;
                fixed (byte* p = data)
                {
                    ok = deserialize(componentId, objType, p, (uint)length, &wrapper);
                }

                if (wrapper == null)
                {
                    return null;
                }

                object result = null;
                if (ok)
                {
                    result = ClientObjects.Instance.Dereference(wrapper->Reference);
                    ClientObjects.Instance.DestroyReference(wrapper->Reference);
                }
                Marshal.FreeHGlobal((IntPtr)wrapper);
                return result;
            }
        }
    }
}
