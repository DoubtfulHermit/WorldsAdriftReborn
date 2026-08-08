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
   InventoryState, 1088 PlayerPropertiesState, 1073
   ClientAuthoritativePlayerState, 6910 UtilitySlotActivatedState, 1098
   RopeControlPoints} and nothing more.** 1081+1088 are the `[Require]`s of
   `CharacterCustomisationVisualizer`, which builds the visible body; 1073
   drives BoneAnimationReader (without it remotes stay in T-pose); 6910 opens
   the glider wings and 1098 carries the grapple rope. Larger seeds enable
   visualizers against default data and their OnEnable subscriptions throw,
   killing the enable chain. Never 1072 CharacterControlsData or 1109
   PilotState: those mean "this is the character you control". The set lives in
   `MirrorSendPolicy.RemoteSeedComponents` and is asserted on in the tests.

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
    fight the writes. (Re-verified in the decompile by
    `docs/research/findings-world.md`: `LocalTransformUpdaterBehaviour.GetLatestValue`
    branches on `HasParent()` and *structurally never calls* `.Parent(...)`; the
    only client-side Parent writer is `LocalTransformTeleportBehaviour`, driven
    by a `TeleportRequestState` this server never sends. So `LocalPosition` is
    global today — and stays global with several islands, because global
    coordinates are island-agnostic.)

11. **`LocalPlayer` is a scene object, not on the prefab.** Never use its
    root to identify "my rig" (that mistake froze both players). The reliable
    anchor is `CameraProxy_Patch.OwnerRoot` — the rig that claimed the camera.

12. **BepInEx ships with `WriteUnityLog = false`** — all mod `Debug.Log`
    output is invisible until flipped in `BepInEx.cfg`. Hours were lost to
    this; both installs have it enabled.

### Cross-cutting (added 2026-08-08 from the research pass in `docs/research/`)

Rules 1-12 were each paid for with a failed test round. These four come from the
seven research reports. 15 and 16 were also paid for — 15 with a session-long log
flood we caused ourselves, 16 with the character roster work. 13 and 14 are static
analysis only; nothing was executed to produce them. Numbering continues rather
than reflows because the findings cite rules 7 and 10 by number.

13. **Fixed point is Q52.12 — divide by 4096, and only then is it metres.**
    `Improbable.Corelibrary.Math/FixedPointVector3Util.cs:9-11,32-39`. The
    server's default transform seed
    `FixedPointVector3{0, 100, 0}` (`ComponentsSerializer.cs:59`) is therefore
    **(0, 0.024, 0) m — the world origin**, not 100 m up. Anyone reading the
    raw longs as metres will mis-site every entity by a factor of 4096 and will
    "explain" bugs with a sky position that does not exist. The 190602 value for
    a point at world (900, -120, 0) is `{3686400, -491520, 0}`.
    Global→Unity is a pure translation (`unityPos = globalPos − OffsetOrigin`,
    `AbstractDetermineOriginStrategy.cs:51-59`) — no axis swap, no scale.
    (Note the historic name "sky-teleport bug" in the server comments and commit
    messages: the *bug* was real — re-seeding the default transform onto a live
    player — but the destination was the origin, under the island, not the sky.)
    Evidence: `findings-world.md`.

    **UPDATE — there is no single default seed any more.** `InitAndSerialize` is
    entity-aware for 190602: `Multiplayer.SpawnPolicy` gives the island Haven's
    world position and a player the spawn point (`SpawnPolicy.cs`, tested in
    `SpawnPolicyTests`). That makes rule 7's "never resend AddComponents" *more*
    important, not less: the accidental destination used to be the world origin,
    which is where the island was, and is now 17 km away and 300 m below it —
    an out-of-world drop with no `WorldEdgePushback` (it gates on world bounds
    this server never sends) and no fall damage to end it.

14. **`[WorkerType]` is defeatable. An unresolvable `[Require]` writer is not.**
    A `[WorkerType(WorkerPlatform.UnityWorker)]` behaviour can be brought onto
    the client — the compatibility test is one cache lookup
    (`BehaviourWorkerCompatibilityCache.IsCompatibleBehaviour`) and only three
    classes in the whole ship physics stack carry the attribute. But a
    visualizer that `[Require]`s a **writer** for a component nobody is
    authoritative over never enables at all, and there is no patch that fixes
    that from the client — the server has to grant authority and seed the
    component. This is the difference between "the tree harvest sim is
    FSim-only, so we would have to reimplement it" (a `[WorkerType]` problem,
    surmountable) and "the multitool beam cannot fire at all because 2105,
    2106, 2002 and 1231 have no writer" (a `[Require]`-writer problem, a hard
    prerequisite for every harvesting design). Check which of the two you have
    before costing any feature.
    Evidence: `findings-ships.md` (Q3), `findings-resources.md` (corrections 1-2).

15. **A component the server fabricates can damage the client. Seed by entity,
    never by component id alone.** `ComponentsSerializer.InitAndSerialize`
    switches on `componentId` alone and answers whatever the client asked for,
    so **any** entity requesting 1139 gets a hardcoded `WeatherCellState`, and
    every entity gets the same default transform. Both together mean several
    entities land in weather cell (0,0), whose Cantor pair id is `0`, and every
    one after the first hits an error branch that forgets to mark the entity —
    so it re-fires every FixedUpdate, forever. That was **10,280 error blocks
    (68% of the log) in a single-client session and 212,214 (93%) with two**.
    We caused it; the client is only the messenger. Rule 7 says seed *little*;
    this says seed *per entity*. The seam is already clean — `InitAndSerialize`
    receives `entityId` and 1088 already uses it.
    Evidence: `findings-weather.md`, `findings-world.md` (Q4).

16. **`Cosmetics == null` is what marks an empty character slot** —
    `LobbySystem.cs:509`, uid non-empty AND `Cosmetics == null`. An empty `{}`
    is not empty: it is classified as a real character and then NREs in
    `CharacterCustomisationVisualizer.cs:422`. Two more roster rules of the
    same kind: `hasMainCharacter` must be present because the client reads it
    with `GetValue` and NREs on absence (`:515`); and the save endpoint's
    response is parsed by the **same** reader as the character list
    (`:429-435`), so replying `"{}"` traps the player on the creation screen.
    All three are pinned by tests in `WorldsAdriftServer.Tests/RosterPolicyTests.cs`.
    Evidence: `findings-persistence.md` (Q1); shipped in "Persist the character roster".

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
  (pure policy, no Wine needed; mutation-checked non-vacuous).
  **`docs/testing.md` says which of the rules above are covered, which are
  not, and what still needs two clients and a human.** Read it before trusting
  a green run.
