using System.Runtime.InteropServices;
using static WorldsAdriftRebornGameServer.Structs.Structs;

namespace WorldsAdriftRebornGameServer.DLLCommunication
{
    internal class EnetLayer
    {
        public enum ENetChannel
        {
            ASSET_LOAD_REQUEST_OP = 0,
            ADD_ENTITY_OP = 1,
            SEND_COMPONENT_INTEREST = 2,
            AUTHORITY_CHANGE_OP = 3,
            COMPONENT_UPDATE_OP = 4,
            REMOVE_ENTITY_OP = 5
        }
        public enum ENetPacketFlag
        {
            RELIABLE = 0,
            UNRELIABLE = 1,
            UNRELIABLE_UNSEQUENCED = 2
        }
        /// <summary>
        /// Mirror of the C++ ENetPacket_Wrapper in enetLayer.h.
        ///
        /// Explicit offsets, because the native struct is NOT what a naive C#
        /// translation produces: C++ 'long' is 32-bit on Windows (LLP64) while
        /// C# 'long' is 64-bit. Measured native layout on x64 is
        /// size=48, data=0, dataLength=8, identifier=16, channel=24, packet=32,
        /// peer=40. The previous sequential layout only worked because the
        /// 4 bytes of padding after dataLength happened to be zeroed.
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        public struct ENetPacket_Wrapper
        {
            [FieldOffset(0)] public unsafe byte* Data;

            /// <summary>Native type is C++ 'long', which is 32-bit here.</summary>
            [FieldOffset(8)] public int DataLength;

            [FieldOffset(16)] public unsafe byte* UserData;
            [FieldOffset(24)] public int Channel;
            [FieldOffset(32)] public IntPtr Packet;

            /// <summary>The client that sent this packet. Zero if unavailable.</summary>
            [FieldOffset(40)] public IntPtr Peer;
        }

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Initialize")]
        public static extern int ENet_Initialize();

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Create_Host")]
        public static extern ENetHostHandle ENet_Create_Host( int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Poll")]
        public static unsafe extern ENetPacket_Wrapper* ENet_Poll( ENetHostHandle client, int waitTime, IntPtr callbackC, IntPtr callbackD );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Destroy_Packet")]
        public static extern void ENet_Destroy_Packet( IntPtr packet );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Send")]
        public static unsafe extern void ENet_Send( ENetPeerHandle peer, int channel, void* data, long len, int flag );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_TrySend")]
        public static unsafe extern int ENet_TrySend( ENetPeerHandle peer, int channel, void* data, long len, int flag );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Disconnect")]
        public static extern void ENet_Disconnect( IntPtr peer, ENetHostHandle client );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Deinitialize")]
        public static extern void ENet_Deinitialize( IntPtr client );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "ENet_EXP_Flush")]
        public static extern void ENet_Flush( ENetHostHandle client );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_AssetLoadRequestOp_Serialize")]
        public static unsafe extern void* PB_AssetLoadRequestOp_Serialize( AssetLoadRequestOp* op, int* len );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_AddEntityOp_Serialize")]
        public static unsafe extern void* PB_AddEntityOp_Serialize( AddEntityOp* op, int* len, long entityId );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_RemoveEntityOp_Serialize")]
        public static unsafe extern void* PB_RemoveEntityOp_Serialize(long entityId, int* len);

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_SendComponentInterest_Deserialize")]
        public static unsafe extern bool PB_EXP_SendComponentInterest_Deserialize(void* data, int len, long* entityId, InterestOverride** interest_override, uint* interest_override_count);

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_AddComponentOp_Serialize")]
        public static unsafe extern void* PB_EXP_AddComponentOp_Serialize( long entityId, AddComponentOp* addComponentOp, uint addComponentOp_count, int* len );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_AuthorityChangeOp_Serialize")]
        public static unsafe extern void* PB_EXP_AuthorityChangeOp_Serialize( long entityId, AuthorityChangeOp* authorityChangeOp, uint authorityChangeOp_count, int* len );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_ComponentUpdateOp_Serialize")]
        public static unsafe extern void* PB_EXP_ComponentUpdateOp_Serialize( long entityId, ComponentUpdateOp* componentUpdateOp, uint componentUpdateOp_count, int* len );

        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_ComponentUpdateOp_Deserialize")]
        public static unsafe extern bool PB_EXP_ComponentUpdateOp_Deserialize( void* data, int len, long* entityId, ComponentUpdateOp** componentUpdateOp, uint* componentUpdateOp_count );

        // Frees a buffer returned by any PB_*_Serialize export. Every serialize
        // return value MUST be handed back here exactly once after ENet_Send has
        // copied its bytes; not doing so leaked the buffer on every send. NULL is
        // a safe no-op. See the ownership contract in enetLayer.h.
        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "PB_EXP_Free")]
        public static unsafe extern void PB_Free( void* handle );

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void ENet_Poll_Callback( IntPtr peer );
    }
}
