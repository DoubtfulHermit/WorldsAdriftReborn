# Worlds Adrift Reborn — Local Multiplayer Co-Presence

**Date:** 2026-08-07
**Status:** Approved, in implementation
**Base commit:** `cb81bcfe` (the commit the only published release was built from)

## Goal

Two game clients on one machine connect simultaneously, spawn on the same island,
and each sees the other player's avatar move in real time.

Explicitly out of scope: shared item pickups, combat, physics interaction,
persistence, remote/internet play, more than a handful of players.

## Why this is currently impossible

Three independent blockers, in dependency order.

### 1. Packets carry no sender identity (root cause)

`WorldsAdriftRebornCoreSdk/enetLayer.cpp`, the `ENET_EVENT_TYPE_RECEIVE` branch,
builds an `ENetPacket_Wrapper` from the ENet event but discards `event.peer`.
The struct has no field for it.

Consequence: the C# server cannot tell who sent a packet. Its main loop therefore
applies every inbound packet to *every* connected peer, with the acknowledgement
in-source: *"for now set it for every client, but we need to distinguish them by
their userData field"*.

This is why multiplayer needs a C++ change, not only a C# one.

### 2. The ENet host accepts one peer

`WorldsAdriftRebornGameServer.cs` calls `ENet_Create_Host(7777, 1, 5, 0, 0)`.
The second argument is `maxConnections` (`enetLayer.cpp`, `ENet_Create_Host`).

### 3. Nothing mirrors players to each other

The server sends a client its own player entity and the island. No code path
ever tells client B that client A exists. `OnClientDisconnected` only logs.

## Architecture

Pure policy modules with unit tests first; thin glue after. The decision logic
must be testable without ENet, Wine, or a running game.

### `PlayerRegistry` — pure, unit-tested

Owns the peer to entity relationship and nothing else.

```
Register(peerId, entityId)   -> void
Unregister(peerId)           -> entityId?          // for RemoveEntityOp
EntityOf(peerId)             -> entityId?
PeersExcept(peerId)          -> peerId[]           // relay targets
Others(peerId)               -> (peerId, entityId)[]  // mirror on join
```

`peerId` is an opaque value, never an `ENetPeerHandle`. No ENet type appears in
any signature. This is what keeps the module testable, and it is the seam a
future server-authoritative design would extend.

### `PeerIdentity` — thin

Maps the raw `ENetPeer*` from the new packet field to a stable `peerId`.
Isolated because pointer identity is the fiddly part and belongs in one place
rather than smeared through the packet loop.

### `RemotePlayerMirror` — policy, unit-tested

Given "player X joined", "player X sent update U", or "player X left", decides
which ops go to which peers. Returns a list of intents
`(targetPeer, opType, payload)` and sends nothing itself. A pure function of
registry state.

### `WorldsAdriftRebornGameServer.Main` — modified, stays thin

Resolves the sender via `PeerIdentity`, asks `RemotePlayerMirror` what to do,
hands the intents to the existing `SendOPHelper`. The existing sync-step
machinery is left structurally alone.

### C++ change — minimum viable

```c
struct ENetPacket_Wrapper {
    void*        data;
    long         dataLength;
    const char*  identifier;
    int          channel;
    ENetPacket*  packet;
    ENetPeer*    peer;      // NEW - appended, never inserted
};
```

Appended so existing field offsets stay valid. If the C# `[StructLayout]` mirror
and the DLL ever disagree, the failure degrades to "peer is garbage" instead of
silently corrupting `data` or `channel`. This matters because the build moves
from MSVC to mingw-w64.

**Deployment note:** the same `CoreSdkDll.dll` binary is loaded by both the game
client (as its SpatialOS SDK replacement, in `BepInEx/plugins/WorldsAdriftReborn/`)
and the game server. Verified identical by md5. A rebuilt DLL must be deployed to
both locations, and a bad build breaks both halves at once.

## Data flow

### Join (B connects while A is in-world)

Existing sync steps run unchanged for B. Two additions once B's spawn completes:

```
registry.Register(peerB, entityB)
mirror.OnJoin(peerB):
    for (peerA, entityA) in registry.Others(peerB):
        to B: AddEntityOp(entityA, "Traveller", "Player")
        to B: AddComponentOp(entityA, components, authoritative: false)
    for peerA in registry.PeersExcept(peerB):
        to A: AddEntityOp(entityB, "Traveller", "Player")
        to A: AddComponentOp(entityB, components, authoritative: false)
```

`authoritative: false` is essential. `SendAuthorityChangeOp` must only ever be
called for a peer's own entity; granting authority over a remote avatar would
make the client try to drive another player's character.

### Movement

```
A's client -> ComponentUpdateOp(entityA, componentId, bytes)
   PeerIdentity resolves sender = peerA        // impossible before this work
   existing local handling (unchanged)
   mirror.OnUpdate(peerA, entityA, componentId, bytes)
        -> relay verbatim to registry.PeersExcept(peerA)
```

Relayed **verbatim**. The server does not deserialize: it cannot for most
component IDs (only 3 handlers exist), and re-serializing would add failure
modes for no benefit.

### Disconnect

```
OnClientDisconnected(peer):
    entityId = registry.Unregister(peerId)
    to all remaining peers: RemoveEntityOp(entityId)
```

`SendOPHelper` has no `SendRemoveEntityOp` today; this needs a new send path and
the correct ENet channel. Unverified. If the client rejects a remove op, the
fallback is leaving the stale avatar in place — cosmetic, not blocking.

Both players spawn on the existing hardcoded island `949069116`, which is what
co-presence requires: same island means actually near each other.

## Error handling

Governing rule: **one player's failure must never abort the loop.** The
`ComponentUpdateManager` crash fixed on 2026-08-07 killed the entire server from
a single reflection call; that shape must not return.

| Condition | Response |
|---|---|
| Packet from unregistered peer | Ignore, log once per peer. Normal during join/teardown races. |
| `EntityOf` null mid-relay | Skip that target, keep serving others. |
| Relay to a peer that just disconnected | Catch at send, unregister, continue. |
| `RemoveEntityOp` rejected by client | Log, leave stale avatar. |
| Peer cap reached | Refuse politely and log; never silently drop. |

### mingw ABI risk

The highest-consequence risk, because the game itself loads this DLL.

- Append the `peer` field rather than insert it.
- Preserve the untouched 2023 DLL as `CoreSdkDll.dll.orig-2023` in both locations.
- A btrfs reflink snapshot of the known-good game install is kept at
  `~/Games/WorldsAdrift.known-good`.
- **Gate 0 runs before any multiplayer work** (below).

## Testing

### Unit — runs natively on Linux, no Wine or game required

`PlayerRegistry`: register/unregister/lookup; `PeersExcept` excludes self;
double-register; unregister-unknown; entity id reuse after disconnect.

`RemotePlayerMirror`: first player joining produces no mirror ops; a second
player produces exactly the reciprocal pair; an update from A targets only B;
disconnect produces removals for remaining peers only.

Tests call the production types directly. A test that reimplements the relay
rules would blind the suite to those rules going missing.

### Integration

Proving two avatars see each other requires two clients actually moving.
Synthetic input is not used. The loop is: bring up servers and both clients,
watch the logs, and have the user move the characters and report what they see.

### Gates

```
Gate 0  stock mingw rebuild -> single-player still works    (toolchain proof)
Gate 1  peer identity resolves; two clients connect, no cross-wiring
Gate 2  B sees A's avatar exist (may be frozen or T-posed)  (riskiest step)
Gate 3  B sees A move
Gate 4  A disconnects, avatar disappears
```

Gate 0 is a hard stop: if a stock mingw build cannot reach the island, the
toolchain is disqualified before any multiplayer debugging begins.

Gate 2 is the likeliest stall. The client needed a `1109 PilotState` injection
hack to render even the *local* player; a remote avatar will probably need its
own component set, found by experiment. If Gate 2 proves impossible, the work
stops with Gate 1 delivered — two clients coexisting cleanly — which is still
strictly better than the current state.

## Known unknowns

1. **Which component ID carries position** is not yet identified. The relay
   needs it. `1003` and `1082` are known to flow (`1082` is wearable-equip);
   transform is unmapped.
2. **Whether the client will render a non-authoritative player entity** at all.
   See Gate 2.
