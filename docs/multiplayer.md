# Multiplayer: how it works and why

**Status: WORKING.** 2026-08-07: two clients on one machine, each seeing the
other's avatar at its live position on the same island. Verified by screenshot
and logs (RemoteRigMover applying advancing positions on both clients).

This document records what the working design is and — more importantly — WHY
each piece is the way it is. Every rule below was paid for with a failed test
round; the commit history on the `multiplayer` branch carries the evidence.

## The design in one paragraph

The server tracks which peer owns which player entity (`PlayerRegistry`), and
on join mirrors each player to every other client (`RemotePlayerMirror`
returns intents; the main loop executes them). Remote players are spawned with
prefab context **"Default"** (the game's own remote-player rig), seeded with a
minimal component set, and positioned by a client-side mod component
(`RemoteRigMover`) that applies the relayed `TransformState` stream. Movement
flows because each client is granted authority over its own TransformState —
clients only publish components they have authority over — and the server
relays every client `ComponentUpdateOp` verbatim to all other peers.

## The rules, and what breaks if you ignore them

### Server side

1. **Packets must carry their sender.** The C++ `ENetPacket_Wrapper` carries
   `ENetPeer* peer` (appended field; C# mirror uses an explicit 48-byte layout
   because C++ `long` is 32-bit on Windows). Without it the server applied
   every packet to every client — single-player by construction.

2. **Remote players use prefab context "Default", never "Player".** The
   client's `DispatchEventHandler` maps context→asset: "Player" is
   `Traveller@Player`, the FULL LOCAL RIG (~90 local-only components,
   LocalPlayerInit, camera proxies). Mirroring with "Player" instantiates a
   second local player and steals camera and identity — every early regression
   traces to this. "Default" is the plain `Traveller`: the shipped
   remote-player rig.

3. **The mirror is two-phase.** Clients only instantiate entities whose prefab
   asset is loaded. Send `AssetLoadRequestOp("Traveller","Default")` first,
   park the AddEntity/AddComponents ops per peer, flush on that peer's next
   asset ack. (Known simplification: the ack payload is unparsed; a joining
   client's own spawn acks can race the flush.)

4. **All clients get the SAME island entity id.** Cross-client references
   (transform parenting) resolve per-client by entity id. With per-client
   island ids a relayed Parent reference resolves to nothing.

5. **Grant each client authority over its own TransformState (190602).**
   Clients only PUBLISH components they hold authority over. Without this
   grant nobody ever sends a position and there is nothing to relay.

6. **First-time setup + authority may only run against the sender's OWN
   entity** (checked via the registry). Matching "any player entity" hands
   authority over someone else's avatar to whichever client asks first.

7. **Remote seed = {190602 TransformState, 1086 PlayerName, 1081
   InventoryState, 1088 PlayerPropertiesState} and nothing more.** 1081+1088
   are the `[Require]`s of `CharacterCustomisationVisualizer`, which builds
   the visible body. Larger seeds enable visualizers against default data and
   their OnEnable subscriptions throw, killing the enable chain.

8. **Never read `ComponentDatabase.MetaclassMap` before the game populates
   it.** Its private ctor runs once and scans currently-loaded assemblies; an
   early read leaves the map permanently empty and breaks all component
   initialization. The component id map lives in `docs/component-ids.md`
   (443 ids, extracted statically from Generated.Code.dll with ilspycmd).

### Client side (BepInEx mod, `Patching/Multiplayer/`)

9. **Unity `[Require]` gating only affects OnEnable/Update — Awake and Start
   always run.** Prefab singletons assigned there are stolen by ANY new rig:
   `CameraSelectionVisualizer.Awake` (`Instance = this`) and
   `CameraProxy.Start` (retargets the camera). Keep-first Harmony guards
   protect both. (With context "Default" the plain rig carries neither, but
   the guards stay as insurance.)

10. **The plain rig has no `CharacterTransformVisualizer`.** It positions
    itself only via transform-hierarchy parenting — and the authoritative
    writer (`LocalTransformUpdaterBehaviour`) publishes unparented GLOBAL
    coordinates and never sets Parent in this flow, so the hierarchy system
    never moves it. `RemoteRigMover` fills the gap: polls the rig's
    `TransformStateReader` and applies `RemapGlobalToUnityVector()` /
    `ToUnityQuaternion()` each frame; if a Parent ever appears it yields to
    the game's hierarchy system. The reader is reflection-grabbed from
    `TransformChildHierarchyBehaviour` because runtime-added components never
    receive `[Require]` injection; it scans both duplicate behaviours for a
    non-null reader, and forces the root rigidbody kinematic so physics can't
    fight the writes.

11. **`LocalPlayer` is a scene object, not on the prefab.** Never use its
    root to identify "my rig" (that mistake froze both players). The reliable
    anchor is `CameraProxy_Patch.OwnerRoot` — the rig that claimed the camera.

12. **BepInEx ships with `WriteUnityLog = false`** — all mod `Debug.Log`
    output is invisible until flipped in `BepInEx.cfg`. Hours were lost to
    this; both installs have it enabled.

## Diagnostics built in (keep them)

- `RemoteRigSweeper`: one-shot rig component inventory; 5s remote-rig
  position/layer/renderer/culling report; camera inventory.
- `RemoteRigMover`: logs reader acquisition, mode (parented/unparented), and
  periodic applied positions with timestamps.
- Server `TransformSampleLogger`: reflection-dumps a sample of relayed
  190602 payloads — the log states what senders actually publish.

## Build & deploy quick reference

- Server: `dotnet build WorldsAdriftRebornGameServer -c Release
  -p:WorldsAdriftGameDir=$HOME/Games/WorldsAdrift`, copy the two DLLs to
  `~/Games/WAReborn-servers/WorldsAdriftRebornGameServer/`.
- Client mod: same for `WorldsAdriftReborn`, copy `WorldsAdriftReborn.dll` to
  BOTH installs' `BepInEx/plugins/WorldsAdriftReborn/`.
- C++ SDK: `WorldsAdriftRebornCoreSdk/build-mingw.sh` then
  `deploy-coresdk.sh` (the same DLL is loaded by client AND server; originals
  backed up as `*.orig-2023`).
- Unit tests: `dotnet test WorldsAdriftRebornGameServer.Multiplayer.Tests`
  (26 tests, pure policy, no Wine needed; mutation-checked non-vacuous).
