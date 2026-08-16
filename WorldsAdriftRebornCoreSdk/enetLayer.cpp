#include "enetLayer.h"
#include <vector>

int __cdecl ENet_EXP_Initialize() {
    return ENet_Initialize();
}
ENetHost* __cdecl ENet_EXP_Create_Host(int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth) {
    return ENet_Create_Host(port, maxConnections, maxChannels, inBandwidth, outBandwidth);
}
ENetPeer* __cdecl ENet_EXP_Connect(char* hostname, int port, ENetHost* client, int maxChannels) {
    return ENet_Connect(hostname, port, client, maxChannels);
}
void __cdecl ENet_EXP_Disconnect(ENetPeer* peer, ENetHost* client) {
    ENet_Disconnect(peer, client);
}
void __cdecl ENet_EXP_Deinitialize(ENetHost* client) {
    ENet_Deinitialize(client);
}
ENetPacket_Wrapper* __cdecl ENet_EXP_Poll(ENetHost* client, int waitTime, OnNewClientConnected* callbackC, OnClientDisconnected* callbackD) {
    return ENet_Poll(client, waitTime, callbackC, callbackD);
}
void __cdecl ENet_EXP_Destroy_Packet(ENetPacket_Wrapper* packet) {
    ENet_Destroy_Packet(packet);
}
void __cdecl ENet_EXP_Send(ENetPeer* peer, int channel, const void* data, long len, int flag) {
    ENet_Send(peer, channel, data, len, flag);
}
int __cdecl ENet_EXP_PeerChannelCount(ENetPeer* peer) {
    return peer == NULL ? 0 : static_cast<int>(peer->channelCount);
}
void __cdecl ENet_EXP_Flush(ENetHost* client) {
    ENet_Flush(client);
}
void* __cdecl PB_EXP_AssetLoadRequestOp_Serialize(AssetLoadRequestOp* op, int* len) {
    return PB_AssetLoadRequestOp_Serialize(op, len);
}
void* __cdecl PB_EXP_AddEntityOp_Serialize(stripped_AddEntityOp* op, int* len, long entityId) {
    return PB_AddEntityOp_Serialize(op, len, entityId);
}
bool __cdecl PB_EXP_SendComponentInterest_Deserialize(const void* data, int len, long* entityId, InterestOverride** interest_override, unsigned int* interest_override_count) {
    return PB_SendComponentInterest_Deserialize(data, len, entityId, interest_override, interest_override_count);
}
void* __cdecl PB_EXP_AddComponentOp_Serialize(long entityId, PB_AddComponentOp* addComponentOp, unsigned int addComponentOp_count, int* len) {
    return PB_AddComponentOp_Serialize(entityId, addComponentOp, addComponentOp_count, len);
}
void* __cdecl PB_EXP_AuthorityChangeOp_Serialize(long entityId, Stripped_AuthorityChangeOp* authorityChangeOp, unsigned int authorityChangeOp_count, int* len) {
    return PB_AuthorityChangeOp_Serialize(entityId, authorityChangeOp, authorityChangeOp_count, len);
}
void* __cdecl PB_EXP_ComponentUpdateOp_Serialize(long entityId, PB_ComponentUpdateOp* componentUpdateOp, unsigned int componentUpdateOp_count, int* len) {
    return PB_ComponentUpdateOp_Serialize(entityId, componentUpdateOp, componentUpdateOp_count, len);
}
bool __cdecl PB_EXP_ComponentUpdateOp_Deserialize(const void* data, int len, long* entityId, PB_ComponentUpdateOp** componentUpdateOp, unsigned int* componentUpdateOp_count) {
    return PB_ComponentUpdateOp_Deserialize(data, len, entityId, componentUpdateOp, componentUpdateOp_count);
}
void* __cdecl PB_EXP_RemoveEntityOp_Serialize(std::int64_t entityId,
    const std::uint32_t* componentIds, std::uint32_t componentCount, int* len) {
    return PB_RemoveEntityOp_Serialize(entityId, componentIds, componentCount, len);
}
void __cdecl PB_EXP_Free(void* handle) {
    PB_Free(handle);
}

int ENet_Initialize() {
    return enet_initialize();
}

ENetHost* ENet_Create_Host(int port, int maxConnections, int maxChannels, int inBandwidth, int outBandwidth) {
    if (port != 0) {
        ENetAddress address;

        address.host = ENET_HOST_ANY;
        address.port = port;

        return enet_host_create(&address, maxConnections, maxChannels, inBandwidth, outBandwidth);
    }

    return enet_host_create(NULL, maxConnections, maxChannels, inBandwidth, outBandwidth);
}

ENetPeer* ENet_Connect(char* hostname, int port, ENetHost* client, int maxChannels) {
    if (client == NULL || hostname == NULL) {
        Logger::Debug("[ERROR] Either no valid client or host was given, cannot connect.");
        return NULL;
    }

    ENetAddress address;
    ENetEvent event;
    ENetPeer* peer;

    enet_address_set_host(&address, hostname);
    address.port = port;

    peer = enet_host_connect(client, &address, maxChannels, 0);

    if (peer == NULL) {
        Logger::Debug("[ERROR] No available peers for initiating an ENet connection.");
        return NULL;
    }

    // timeout after 5 secs
    if (enet_host_service(client, &event, 5000) > 0 && event.type == ENET_EVENT_TYPE_CONNECT) {
        return peer;
    }

    enet_peer_reset(peer);
    return NULL;
}

void ENet_Disconnect(ENetPeer* peer, ENetHost* client) {
    if (peer == NULL || client == NULL) {
        return;
    }

    ENetEvent event;

    enet_peer_disconnect(peer, 0);

    // wait for 3 secs to disconnect gracefully
    while (enet_host_service(client, &event, 3000) > 0) {
        switch (event.type) {
        case ENET_EVENT_TYPE_RECEIVE:
            enet_packet_destroy(event.packet);
            break;
        case ENET_EVENT_TYPE_DISCONNECT:
            return;
        }
    }

    // failed to gracefully disconnect, force it now
    enet_peer_reset(peer);
}

void ENet_Deinitialize(ENetHost* client) {
    if (client != NULL) {
        enet_host_destroy(client);
    }

    enet_deinitialize();
}

/**
 polls the connection for new events. if a new client connected to the server and the callback is provided it will be invoked.
 if a packet was received it will be returned and needs to be destroyed when finished processing.
*/
ENetPacket_Wrapper* ENet_Poll(ENetHost* client, int waitTime, OnNewClientConnected* callbackC, OnClientDisconnected* callbackD) {
    if (client == NULL) {
        return NULL;
    }

    ENetEvent event;

    if (enet_host_service(client, &event, waitTime) == 0) {
        return NULL;
    }

    ENetPacket_Wrapper* packet = NULL;

    switch (event.type) {
    case ENET_EVENT_TYPE_CONNECT:
        if (callbackC != NULL) {
            callbackC(event.peer);
        }
        break;

    case ENET_EVENT_TYPE_RECEIVE:
        packet = new ENetPacket_Wrapper();

        packet->data = (void*)(event.packet->data);
        packet->dataLength = event.packet->dataLength;
        packet->identifier = reinterpret_cast<char const*>(event.packet->userData);
        packet->channel = event.channelID;
        packet->packet = event.packet;
        // Without this the server cannot tell who sent a packet, and has to
        // apply every packet to every connected client.
        packet->peer = event.peer;

        break;

    case ENET_EVENT_TYPE_DISCONNECT:
        if (callbackD != NULL) {
            callbackD(event.peer);
        }

        event.peer->data = NULL;
        break;
    }

    return packet;
}

void ENet_Destroy_Packet(ENetPacket_Wrapper* packet) {
    if (packet != NULL) {
        enet_packet_destroy(packet->packet);
        delete packet;
    }
}

void ENet_Send(ENetPeer* peer, int channel, const void* data, long len, int flag) {
    if (peer == NULL || data == NULL || len == 0) {
        return;
    }

    // `flag` is a WarPacketFlag (see enetLayer.h), NOT an ENET_PACKET_FLAG_*.
    enet_uint32 pf = ENET_PACKET_FLAG_RELIABLE;
    if (flag == WAR_PACKET_UNRELIABLE) {
        pf = ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT;
    }
    else if (flag == WAR_PACKET_UNRELIABLE_UNSEQUENCED) {
        pf = ENET_PACKET_FLAG_UNSEQUENCED | ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT;
    }

    ENetPacket* packet = enet_packet_create(data, len, pf);

    // enet_peer_send returns -1 for a disconnected peer or an out-of-range
    // channel, and does NOT take ownership on failure. Ignoring it leaked one
    // packet per relay attempt to a ghost peer for its whole timeout window,
    // and would silently swallow every packet on a channel a peer never
    // negotiated - the exact failure mode a new channel would hit on an
    // un-updated client.
    if (enet_peer_send(peer, channel, packet) < 0) {
        Logger::Debug("[error] enet_peer_send failed (channel out of range, or peer not connected); packet dropped.");
        enet_packet_destroy(packet);
    }
}

void ENet_Flush(ENetHost* client) {
    if (client == NULL) {
        return;
    }

    enet_host_flush(client);
}

// Frees a buffer handed out by any PB_*_Serialize. The argument MUST be the exact
// pointer that was returned (a new[]-allocated block); NULL is a safe no-op. See
// the ownership contract in enetLayer.h.
void PB_Free(void* handle) {
    delete[] static_cast<unsigned char*>(handle);
}

// Copies a just-serialized protobuf payload into a fresh caller-owned buffer and
// hands back ownership (freed via PB_Free). Central so all six serialize exports
// share ONE allocation/ownership path. Returning std::string::data() directly -
// what these functions used to do - leaked the std::string on every send.
static void* PB_TakeOwnership(const std::string& serialized, int* len) {
    std::size_t n = serialized.size();
    unsigned char* out = new unsigned char[n];
    memcpy(out, serialized.data(), n);
    *len = static_cast<int>(n);
    return out;
}

static void PB_AppendVarint(std::string& output, std::uint64_t value) {
    do {
        unsigned char b = static_cast<unsigned char>(value & 0x7f);
        value >>= 7;
        output.push_back(static_cast<char>(value ? (b | 0x80) : b));
    } while (value);
}

static bool PB_ReadVarint(const unsigned char* bytes, int len, int* cursor,
    std::uint64_t* value) {
    *value = 0;
    for (int shift = 0; *cursor < len && shift < 64; shift += 7) {
        unsigned char b = bytes[(*cursor)++];
        *value |= static_cast<std::uint64_t>(b & 0x7f) << shift;
        if ((b & 0x80) == 0) return true;
    }
    return false;
}

void* PB_RemoveEntityOp_Serialize(std::int64_t entityId,
    const std::uint32_t* componentIds, std::uint32_t componentCount, int* len) {
    if (len == NULL) return NULL;
    std::string serialized;
    serialized.push_back(static_cast<char>(0x08)); // field 1, int64 varint
    PB_AppendVarint(serialized, static_cast<std::uint64_t>(entityId));
    for (std::uint32_t i = 0; i < componentCount; ++i) {
        serialized.push_back(static_cast<char>(0x10)); // field 2, uint32 varint
        PB_AppendVarint(serialized, componentIds[i]);
    }
    return PB_TakeOwnership(serialized, len);
}

bool PB_RemoveEntityOp_Deserialize(const void* data, int len, RemoveEntityOp* op,
    RemoveComponentOp** components, int* componentCount) {
    if (data == NULL || op == NULL || components == NULL || componentCount == NULL || len < 2)
        return false;
    const unsigned char* bytes = static_cast<const unsigned char*>(data);
    int cursor = 0;
    bool sawEntity = false;
    std::vector<std::uint32_t> ids;
    while (cursor < len) {
        std::uint64_t tag;
        if (!PB_ReadVarint(bytes, len, &cursor, &tag)) return false;
        std::uint64_t value;
        if ((tag & 7) != 0 || !PB_ReadVarint(bytes, len, &cursor, &value)) return false;
        if (tag == 0x08) {
            op->EntityId = static_cast<std::int64_t>(value);
            sawEntity = true;
        } else if (tag == 0x10) {
            ids.push_back(static_cast<std::uint32_t>(value));
        }
    }
    if (!sawEntity) return false;
    *componentCount = static_cast<int>(ids.size());
    *components = ids.empty() ? nullptr : new RemoveComponentOp[ids.size()];
    for (std::size_t i = 0; i < ids.size(); ++i) {
        (*components)[i].EntityId = op->EntityId;
        (*components)[i].ComponentId = ids[i];
    }
    return true;
}

void* PB_AssetLoadRequestOp_Serialize(AssetLoadRequestOp* op, int* len) {
    if (op == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::AssetLoadRequestOp* pb_op = new WorldsAdriftRebornCoreSdk::AssetLoadRequestOp();

    if (op->AssetType != NULL) {
        pb_op->set_assettype(op->AssetType);
    }
    if (op->Name != NULL) {
        pb_op->set_name(op->Name);
    }
    if (op->Context != NULL) {
        pb_op->set_context(op->Context);
    }
    if (op->Url != NULL) {
        pb_op->set_url(op->Url);
    }
    
    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_AssetLoadRequestOp_Deserialize(const void* data, int len, AssetLoadRequestOp* op) {
    if (data == NULL || op == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::AssetLoadRequestOp* pb_op = new WorldsAdriftRebornCoreSdk::AssetLoadRequestOp();
    // todo: maybe find a way to avoid the internal copy of std::string here.
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);
    
    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        delete str;

        return false;
    }

    // Every field crosses the Worker SDK ABI as a C string. Reserve the byte
    // written by the terminator: allocating exactly payload.size() and then
    // writing field[len] poisoned the Windows heap once per field. With the
    // larger Wareborn spawn plan, Windows eventually reported c0000374 while
    // activating a later prefab even though the damage happened here.
    if (pb_op->has_assettype()) {
        std::size_t len = pb_op->assettype().size();
        op->AssetType = new char[len + 1];
        memcpy(op->AssetType, pb_op->assettype().data(), len);
        op->AssetType[len] = '\0';
    }
    if (pb_op->has_name()) {
        std::size_t len = pb_op->name().size();
        op->Name = new char[len + 1];
        memcpy(op->Name, pb_op->name().data(), len);
        op->Name[len] = '\0';
    }
    if (pb_op->has_context()) {
        std::size_t len = pb_op->context().size();
        op->Context = new char[len + 1];
        memcpy(op->Context, pb_op->context().data(), len);
        op->Context[len] = '\0';
    }
    if (pb_op->has_url()) {
        std::size_t len = pb_op->url().size();
        op->Url = new char[len + 1];
        memcpy(op->Url, pb_op->url().data(), len);
        op->Url[len] = '\0';
    }

    delete pb_op;
    delete str;

    return true;
}

void* PB_AddEntityOp_Serialize(stripped_AddEntityOp* op, int* len, long entityId) {
    if (op == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::AddEntityOp* pb_op = new WorldsAdriftRebornCoreSdk::AddEntityOp();

    pb_op->set_entityid(entityId);
    if (op->PrefabContext != NULL) {
        pb_op->set_prefabcontext(op->PrefabContext);
    }
    if (op->PrefabName != NULL) {
        pb_op->set_prefabname(op->PrefabName);
    }

    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_AddEntityOp_Deserialize(const void* data, int len, AddEntityOp* op) {
    if (data == NULL || op == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::AddEntityOp* pb_op = new WorldsAdriftRebornCoreSdk::AddEntityOp();
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);

    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        delete str;

        return false;
    }

    if (pb_op->has_entityid()) {
        op->EntityId = pb_op->entityid();
    }
    if (pb_op->has_prefabcontext()) {
        std::size_t len = pb_op->prefabcontext().size();
        op->PrefabContext = new char[len + 1];
        memcpy(op->PrefabContext, pb_op->prefabcontext().data(), len);
        op->PrefabContext[len] = '\0';
    }
    if (pb_op->has_prefabname()) {
        std::size_t len = pb_op->prefabname().size();
        op->PrefabName = new char[len + 1];
        memcpy(op->PrefabName, pb_op->prefabname().data(), len);
        op->PrefabName[len] = '\0';
    }

    delete pb_op;
    delete str;

    return true;
}

void* PB_SendComponentInterest_Serialize(long entityId, InterestOverride* interest_override, unsigned int interest_override_count, int* len) {
    if (interest_override == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::SendComponentInterest* pb_op = new WorldsAdriftRebornCoreSdk::SendComponentInterest();

    pb_op->set_entityid(entityId);
    for (int i = 0; i < interest_override_count; i++) {
        WorldsAdriftRebornCoreSdk::InterestOverride* io = pb_op->add_components();
        io->set_componentid(interest_override[i].ComponentId);
        io->set_isinterested(interest_override[i].IsInterested);
    }

    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_SendComponentInterest_Deserialize(const void* data, int len, long* entityId, InterestOverride** interest_override, unsigned int* interest_override_count) {
    if (data == NULL || entityId == NULL || interest_override_count == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::SendComponentInterest* pb_op = new WorldsAdriftRebornCoreSdk::SendComponentInterest();
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);

    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        *interest_override_count = 0;

        return false;
    }

    if (pb_op->has_entityid()) {
        *entityId = pb_op->entityid();
    }
    *interest_override_count = pb_op->components_size();
    *interest_override = new InterestOverride[pb_op->components_size()];
    for (int i = 0; i < pb_op->components_size(); i++) {
        (*interest_override)[i].ComponentId = pb_op->components(i).componentid();
        (*interest_override)[i].IsInterested = pb_op->components(i).isinterested();
    }

    delete pb_op;
    delete str;

    return true;
}

void* PB_AddComponentOp_Serialize(long entityId, PB_AddComponentOp* addComponentOp, unsigned int addComponentOp_count, int* len) {
    if (addComponentOp == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::AddComponentOp* pb_op = new WorldsAdriftRebornCoreSdk::AddComponentOp();

    pb_op->set_entityid(entityId);
    for (int i = 0; i < addComponentOp_count; i++) {
        WorldsAdriftRebornCoreSdk::ComponentData* data = pb_op->add_components();
        data->set_componentid(addComponentOp[i].ComponentId);
        data->set_data(std::string(addComponentOp[i].ComponentData, addComponentOp[i].DataLength));
        data->set_datalength(addComponentOp[i].DataLength);
    }

    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_AddComponentOp_Deserialze(const void* data, int len, long* entityId, PB_AddComponentOp** addComponentOp, unsigned int* addComponentOp_count) {
    if (data == NULL || entityId == NULL || addComponentOp_count == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::AddComponentOp* pb_op = new WorldsAdriftRebornCoreSdk::AddComponentOp();
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);

    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        *addComponentOp_count = 0;

        return false;
    }

    if (pb_op->has_entityid()) {
        *entityId = pb_op->entityid();
    }
    *addComponentOp_count = pb_op->components_size();
    *addComponentOp = new PB_AddComponentOp[*addComponentOp_count];
    for (int i = 0; i < *addComponentOp_count; i++) {
        (*addComponentOp)[i].ComponentId = pb_op->components(i).componentid();
        (*addComponentOp)[i].DataLength = pb_op->components(i).datalength();
        (*addComponentOp)[i].ComponentData = new char[(*addComponentOp)[i].DataLength];
        memcpy((*addComponentOp)[i].ComponentData, pb_op->components(i).data().data(), (*addComponentOp)[i].DataLength);
    }

    delete pb_op;
    delete str;

    return true;
}

void* PB_AuthorityChangeOp_Serialize(long entityId, Stripped_AuthorityChangeOp* authorityChangeOp, unsigned int authorityChangeOp_count, int* len) {
    if (authorityChangeOp == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::AuthorityChangeOpWrapper* pb_op = new WorldsAdriftRebornCoreSdk::AuthorityChangeOpWrapper();

    pb_op->set_entityid(entityId);
    for (unsigned int i = 0; i < authorityChangeOp_count; i++) {
        WorldsAdriftRebornCoreSdk::AuthorityChange* op = pb_op->add_oplist();
        op->set_componentid(authorityChangeOp[i].ComponentId);
        op->set_hasauthority(authorityChangeOp[i].HasAuthority);
    }

    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_AuthorityChangeOp_Deserialize(const void* data, int len, long* entityId, Stripped_AuthorityChangeOp** authorityChangeOp, unsigned int* authorityChangeOp_count) {
    if (data == NULL || entityId == NULL || authorityChangeOp_count == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::AuthorityChangeOpWrapper* pb_op = new WorldsAdriftRebornCoreSdk::AuthorityChangeOpWrapper();
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);

    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        *authorityChangeOp_count = 0;

        return false;
    }

    if (pb_op->has_entityid()) {
        *entityId = pb_op->entityid();
    }
    *authorityChangeOp_count = pb_op->oplist_size();
    *authorityChangeOp = new Stripped_AuthorityChangeOp[*authorityChangeOp_count];
    for (int i = 0; i < *authorityChangeOp_count; i++) {
        (*authorityChangeOp)[i].ComponentId = pb_op->oplist(i).componentid();
        (*authorityChangeOp)[i].HasAuthority = pb_op->oplist(i).hasauthority();
    }

    delete pb_op;
    delete str;

    return true;
}

void* PB_ComponentUpdateOp_Serialize(long entityId, PB_ComponentUpdateOp* componentUpdateOp, unsigned int componentUpdateOp_count, int* len) {
    if (componentUpdateOp == NULL || len == NULL) {
        if (len != NULL) {
            *len = 0;
        }
        return NULL;
    }

    WorldsAdriftRebornCoreSdk::ComponentUpdateOp* pb_op = new WorldsAdriftRebornCoreSdk::ComponentUpdateOp();

    pb_op->set_entityid(entityId);
    for (unsigned int i = 0; i < componentUpdateOp_count; i++) {
        WorldsAdriftRebornCoreSdk::ComponentData_ComponentUpdate* data = pb_op->add_components();
        data->set_componentid(componentUpdateOp[i].ComponentId);
        data->set_data(componentUpdateOp[i].ComponentData, componentUpdateOp[i].DataLength);
        data->set_datalength(componentUpdateOp[i].DataLength);
    }

    std::string serialized;

    if (!pb_op->SerializeToString(&serialized)) {
        delete pb_op;

        *len = 0;
        return NULL;
    }

    delete pb_op;

    // Hand back a caller-owned copy (freed via PB_Free). Returning
    // serialized.data() leaked the std::string on every send - see FIX 2.
    return PB_TakeOwnership(serialized, len);
}

bool PB_ComponentUpdateOp_Deserialize(const void* data, int len, long* entityId, PB_ComponentUpdateOp** componentUpdateOp, unsigned int* componentUpdateOp_count) {
    if (data == NULL || componentUpdateOp_count == NULL || entityId == NULL) {
        return false;
    }

    WorldsAdriftRebornCoreSdk::ComponentUpdateOp* pb_op = new WorldsAdriftRebornCoreSdk::ComponentUpdateOp();
    std::string* str = new std::string(reinterpret_cast<char const*>(data), len);

    if (!pb_op->ParseFromString(*str)) {
        delete pb_op;
        *componentUpdateOp_count = 0;

        return false;
    }

    if (pb_op->has_entityid()) {
        *entityId = pb_op->entityid();
    }
    *componentUpdateOp_count = pb_op->components_size();
    *componentUpdateOp = new PB_ComponentUpdateOp[*componentUpdateOp_count];
    for (int i = 0; i < *componentUpdateOp_count; i++) {
        (*componentUpdateOp)[i].ComponentId = pb_op->components(i).componentid();
        (*componentUpdateOp)[i].DataLength = pb_op->components(i).datalength();
        (*componentUpdateOp)[i].ComponentData = new char[(*componentUpdateOp)[i].DataLength];
        memcpy((*componentUpdateOp)[i].ComponentData, pb_op->components(i).data().data(), (*componentUpdateOp)[i].DataLength);
    }

    delete pb_op;
    delete str;

    return true;
}
