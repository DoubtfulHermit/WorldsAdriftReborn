#pragma once

#include "enet/enet.h"
#include "Logger.h"

#include "Structs.h"
#include "AssetLoadRequestOp.pb.h"
#include "AddEntityOp.pb.h"
#include "SendComponentInterest.pb.h"
#include "AddComponentOp.pb.h"
#include "AuthorityChangeOp.pb.h"
#include "ComponentUpdateOp.pb.h"

#define DLL_EXPORT extern "C" __declspec(dllexport)

#define CH_AssetLoadRequestOp 0
#define CH_AddEntityOp 1
#define CH_SendComponentInterest 2
#define CH_AuthorityChangeOp 3
#define CH_ComponentUpdateOp 4
#define CH_RemoveEntityOp 5

typedef void OnNewClientConnected(ENetPeer* peer);
typedef void OnClientDisconnected(ENetPeer* peer);

struct ENetPacket_Wrapper {
    void* data;
    long dataLength;
    const char* identifier;
    int channel;
    ENetPacket* packet;
    // Which client sent this packet. Appended rather than inserted so the
    // offsets above stay valid: a mismatch with the C# mirror then shows up as a
    // bad peer instead of silently corrupting data or channel.
    // x64 layout: struct is 48 bytes, peer at offset 40.
    ENetPeer* peer;
};

DLL_EXPORT int __cdecl ENet_EXP_Initialize();
DLL_EXPORT ENetHost* __cdecl ENet_EXP_Create_Host(int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth);
DLL_EXPORT ENetPeer* __cdecl ENet_EXP_Connect(char* hostname, int port, ENetHost* client, int maxChannels);
DLL_EXPORT void __cdecl ENet_EXP_Disconnect(ENetPeer* peer, ENetHost* client);
DLL_EXPORT void __cdecl ENet_EXP_Deinitialize(ENetHost* client);
DLL_EXPORT ENetPacket_Wrapper* __cdecl ENet_EXP_Poll(ENetHost* client, int waitTime, OnNewClientConnected* callbackC, OnClientDisconnected* callbackD);
DLL_EXPORT void __cdecl ENet_EXP_Destroy_Packet(ENetPacket_Wrapper* packet);
DLL_EXPORT void __cdecl ENet_EXP_Send(ENetPeer* peer, int channel, const void* data, long len, int flag);
DLL_EXPORT int __cdecl ENet_EXP_PeerChannelCount(ENetPeer* peer);
DLL_EXPORT void __cdecl ENet_EXP_Flush(ENetHost* client);

DLL_EXPORT void* __cdecl PB_EXP_AssetLoadRequestOp_Serialize(AssetLoadRequestOp* op, int* len);
DLL_EXPORT void* __cdecl PB_EXP_AssetLoadedAck_Serialize(AssetLoaded* ack, int* len);
DLL_EXPORT bool __cdecl PB_EXP_AssetLoadedAck_Deserialize(
    const void* data, int len, AssetLoaded* ack);
DLL_EXPORT void __cdecl PB_EXP_AssetLoadedAck_Free(AssetLoaded* ack);
DLL_EXPORT void* __cdecl PB_EXP_AddEntityOp_Serialize(stripped_AddEntityOp* op, int* len, long entityId);
DLL_EXPORT bool __cdecl PB_EXP_SendComponentInterest_Deserialize(const void* data, int len, long* entityId, InterestOverride** interest_override, unsigned int* interest_override_count);
DLL_EXPORT void* __cdecl PB_EXP_AddComponentOp_Serialize(long entityId, PB_AddComponentOp* addComponentOp, unsigned int addComponentOp_count, int* len);
DLL_EXPORT void* __cdecl PB_EXP_AuthorityChangeOp_Serialize(long entityId, Stripped_AuthorityChangeOp* authorityChangeOp, unsigned int authorityChangeOp_count, int* len);
DLL_EXPORT void* __cdecl PB_EXP_ComponentUpdateOp_Serialize(long entityId, PB_ComponentUpdateOp* componentUpdateOp, unsigned int componentUpdateOp_count, int* len);
DLL_EXPORT bool __cdecl PB_EXP_ComponentUpdateOp_Deserialize(const void* data, int len, long* entityId, PB_ComponentUpdateOp** componentUpdateOp, unsigned int* componentUpdateOp_count);
DLL_EXPORT void* __cdecl PB_EXP_RemoveEntityOp_Serialize(
    std::int64_t entityId, const std::uint32_t* componentIds,
    std::uint32_t componentCount, int* len);

// Frees a buffer previously returned by any PB_*_Serialize export. See the
// ownership contract on PB_Free below. NULL is a safe no-op.
DLL_EXPORT void __cdecl PB_EXP_Free(void* handle);

int ENet_Initialize();
// set port to 0 if you are a client
ENetHost* ENet_Create_Host(int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth);
ENetPeer* ENet_Connect(char* hostname, int port, ENetHost* client, int maxChannels);
void ENet_Disconnect(ENetPeer* peer, ENetHost* client);
void ENet_Deinitialize(ENetHost* client);
ENetPacket_Wrapper* ENet_Poll(ENetHost* client, int waitTime, OnNewClientConnected* callbackC, OnClientDisconnected* callbackD);
void ENet_Destroy_Packet(ENetPacket_Wrapper* packet);
/*
 * Packet flags for ENet_Send. These are OUR OWN wire values, mirroring the C#
 * EnetLayer.ENetPacketFlag enum - they are deliberately NOT ENet's
 * ENET_PACKET_FLAG_* constants.
 *
 * The two numbering schemes collide: ENET_PACKET_FLAG_RELIABLE is (1 << 0) == 1,
 * which is WAR_PACKET_UNRELIABLE here. Passing the ENet constant to ENet_Send
 * therefore asks for the exact opposite of what it looks like it asks for, which
 * is what every caller in Connection.cpp used to do.
 */
enum WarPacketFlag {
    WAR_PACKET_RELIABLE = 0,
    WAR_PACKET_UNRELIABLE = 1,
    WAR_PACKET_UNRELIABLE_UNSEQUENCED = 2
};

void ENet_Send(ENetPeer* peer, int channel, const void* data, long len, int flag);
void ENet_Flush(ENetHost* client);

void* PB_AssetLoadRequestOp_Serialize(AssetLoadRequestOp* op, int* len);
bool PB_AssetLoadRequestOp_Deserialize(const void* data, int len, AssetLoadRequestOp* op);
void* PB_AssetLoadedAck_Serialize(AssetLoaded* ack, int* len);
bool PB_AssetLoadedAck_Deserialize(const void* data, int len, AssetLoaded* ack);
void PB_AssetLoadedAck_Free(AssetLoaded* ack);
void* PB_AddEntityOp_Serialize(stripped_AddEntityOp* op, int* len, long entityId);
bool PB_AddEntityOp_Deserialize(const void* data, int len, AddEntityOp* op);
void* PB_RemoveEntityOp_Serialize(std::int64_t entityId,
    const std::uint32_t* componentIds, std::uint32_t componentCount, int* len);
bool PB_RemoveEntityOp_Deserialize(const void* data, int len, RemoveEntityOp* op,
    RemoveComponentOp** components, int* componentCount);
void* PB_SendComponentInterest_Serialize(long entityId, InterestOverride* interest_override, unsigned int interest_override_count, int* len);
bool PB_SendComponentInterest_Deserialize(const void* data, int len, long* entityId, InterestOverride** interest_override, unsigned int* interest_override_count);
void* PB_AddComponentOp_Serialize(long entityId, PB_AddComponentOp* addComponentOp, unsigned int addComponentOp_count, int* len);
bool PB_AddComponentOp_Deserialze(const void* data, int len, long* entityId, PB_AddComponentOp** addComponentOp, unsigned int* addComponentOp_count);
void* PB_AuthorityChangeOp_Serialize(long entityId, Stripped_AuthorityChangeOp* authorityChangeOp, unsigned int authorityChangeOp_count, int* len);
bool PB_AuthorityChangeOp_Deserialize(const void* data, int len, long* entityId, Stripped_AuthorityChangeOp** authorityChangeOp, unsigned int* authorityChangeOp_count);
void* PB_ComponentUpdateOp_Serialize(long entityId, PB_ComponentUpdateOp* componentUpdateOp, unsigned int componentUpdateOp_count, int* len);
bool PB_ComponentUpdateOp_Deserialize(const void* data, int len, long* entityId, PB_ComponentUpdateOp** componentUpdateOp, unsigned int* componentUpdateOp_count);

/*
 * OWNERSHIP CONTRACT for the six PB_*_Serialize functions above.
 *
 * Each returns a buffer allocated with new[] that is OWNED BY THE CALLER, with
 * *len set to its size, or NULL on bad args / serialize failure (with *len 0,
 * nothing to free). The caller MUST hand a non-NULL return value back to PB_Free
 * exactly once, after it has consumed the bytes.
 *
 * The bytes are consumed synchronously: every caller passes the pointer to
 * ENet_Send, whose enet_packet_create COPIES it (memcpy - ENET_PACKET_FLAG_
 * NO_ALLOCATE is never set on these sends), so nothing in ENet keeps the pointer
 * and the caller frees immediately after ENet_Send returns. Before this contract
 * existed the functions returned std::string::data() and the owning std::string
 * was never deleted on success - a leak on EVERY send, in client and server.
 */
void PB_Free(void* handle);
