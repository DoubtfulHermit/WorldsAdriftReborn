using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RelayBot
{
    /// <summary>
    /// P/Invoke surface over the CoreSdk shim's ENet transport exports - the same
    /// entry points the game server binds in
    /// WorldsAdriftRebornGameServer/DLLCommunication/EnetLayer.cs, resolved here
    /// against the NATIVE Linux build (tools/relaybot/build-coresdk-native.sh).
    /// </summary>
    public static class Enet
    {
        /// <summary>Channel map, fixed by enetLayer.h. 6 channels, hard-capped.</summary>
        public const int ChAssetLoadRequestOp = 0;
        public const int ChAddEntityOp = 1;
        public const int ChSendComponentInterest = 2;
        public const int ChAuthorityChangeOp = 3;
        public const int ChComponentUpdateOp = 4;
        public const int ChRemoveEntityOp = 5;
        public const int ChannelCount = 6;

        /// <summary>
        /// OUR wire flag values (WarPacketFlag in enetLayer.h), NOT ENet's
        /// ENET_PACKET_FLAG_* constants - the two numbering schemes collide and
        /// passing ENet's RELIABLE (1) here would request UNRELIABLE.
        /// </summary>
        public const int FlagReliable = 0;
        public const int FlagUnreliable = 1;

        /// <summary>
        /// Mirror of the C++ ENetPacket_Wrapper. Explicit offsets: with C++
        /// `long` being 32-bit on Windows and 64-bit here, the field OFFSETS are
        /// identical on both (alignment pads dataLength's slot to 8 bytes either
        /// way) - only the width of DataLength differs, and the C++ side
        /// value-initializes the struct, so reading the full 8 bytes is safe on
        /// both. Size 48, peer at 40, matching the server's C# mirror.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        public struct PacketWrapper
        {
            [FieldOffset(0)] public IntPtr Data;
            [FieldOffset(8)] public long DataLength;
            [FieldOffset(16)] public IntPtr Identifier;
            [FieldOffset(24)] public int Channel;
            [FieldOffset(32)] public IntPtr Packet;
            [FieldOffset(40)] public IntPtr Peer;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void PollCallback(IntPtr peer);

        private const string Lib = "CoreSdkDll";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Initialize")]
        public static extern int Initialize();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Create_Host")]
        public static extern IntPtr CreateHost(int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Connect")]
        public static extern IntPtr Connect([MarshalAs(UnmanagedType.LPUTF8Str)] string hostname, int port, IntPtr client, int maxChannels);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Poll")]
        public static unsafe extern PacketWrapper* Poll(IntPtr client, int waitTimeMs, IntPtr callbackConnect, IntPtr callbackDisconnect);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Destroy_Packet")]
        public static extern void DestroyPacket(IntPtr packet);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Send")]
        public static unsafe extern void Send(IntPtr peer, int channel, void* data, long len, int flag);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Flush")]
        public static extern void Flush(IntPtr client);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Disconnect")]
        public static extern void Disconnect(IntPtr peer, IntPtr client);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Deinitialize")]
        public static extern void Deinitialize(IntPtr client);

        public static unsafe void Send(IntPtr peer, int channel, byte[] payload, int flag)
        {
            fixed (byte* p = payload)
            {
                Send(peer, channel, p, payload.Length, flag);
            }
        }

        /// <summary>
        /// Maps DllImport("CoreSdkDll") onto the native shim. Probe order:
        /// $RELAYBOT_CORESDK, then libCoreSdkDll.so next to the executable (the
        /// csproj copies it there), then ../build-native relative to the source
        /// tree for `dotnet run` from a fresh checkout.
        /// </summary>
        [ModuleInitializer]
        internal static void InstallResolver()
        {
            NativeLibrary.SetDllImportResolver(typeof(Enet).Assembly, (name, assembly, searchPath) =>
            {
                if (name != Lib)
                {
                    return IntPtr.Zero;
                }

                var candidates = new List<string>();
                string env = Environment.GetEnvironmentVariable("RELAYBOT_CORESDK");
                if (!string.IsNullOrEmpty(env))
                {
                    candidates.Add(env);
                }
                candidates.Add(Path.Combine(AppContext.BaseDirectory, "libCoreSdkDll.so"));
                candidates.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                    "..", "..", "..", "..", "build-native", "libCoreSdkDll.so")));

                foreach (string candidate in candidates)
                {
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                    {
                        return handle;
                    }
                }

                throw new DllNotFoundException(
                    "libCoreSdkDll.so not found. Build it with tools/relaybot/build-coresdk-native.sh "
                    + "or point RELAYBOT_CORESDK at it. Tried: " + string.Join(", ", candidates));
            });
        }
    }
}
