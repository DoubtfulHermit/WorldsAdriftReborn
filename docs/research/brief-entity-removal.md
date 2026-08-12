# RESEARCH BRIEF 3 — ENTITY REMOVAL / DYNAMIC WORLD

## Mission
Implement the missing ability to REMOVE an entity. Today the server can only ADD entities.
There is no removal path at all: ENetChannel has no such channel, no RemoveEntityOp proto
exists, and the SDK's WorkerProtocol_Dispatcher_RegisterRemoveEntityCallback is an
unimplemented TODO (WorldsAdriftRebornCoreSdk/Exports.cpp:51-53).

This was originally scoped as cosmetic (despawn a disconnected player's stale avatar) but it
is now BLOCKING real content: harvesting a resource node means removing it, ships get
created and destroyed, items drop and are picked up. It is the missing half of dynamic
world content.

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md   (twelve architecture rules; rule 3
  explains the add path, which the removal path should mirror)
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`)
- The existing add pipeline end-to-end: server SendOPHelper.SendAddEntityOP ->
  PB_AddEntityOp_Serialize (C++) -> ENet channel ADD_ENTITY_OP -> client CoreSdkDll ->
  Dispatcher -> DispatchEventHandler (acs) -> entity instantiated.

## Sources of truth
- Decompiled game C#:   SCRATCH/acs/   (esp. Improbable.Unity.Core/DispatchEventHandler.cs)
- Decompiled generated: SCRATCH/gencode/
- SDK decompiled:       SCRATCH/sdk-decomp/
- C++ SDK source:       /home/ttanurhan/Games/WAReborn-src/WorldsAdriftRebornCoreSdk/
(SCRATCH = .../scratchpad)

## Questions — answer ALL with file:line evidence
Q1. CLIENT SIDE. What does the game client ALREADY have for entity removal? Find the
    RemoveEntityOp struct (Structs.h has one), the dispatcher's removal handling in acs, and
    exactly what the client needs to receive/be told in order to destroy an entity and clean
    up its GameObject. Does a native callback need registering (the Exports.cpp TODO), or can
    the client-side removal be driven another way?
Q2. WIRE FORMAT. Design the missing message, mirroring the add path exactly: a new .proto
    (the repo has 6 existing ones, e.g. AddEntityOp.proto), a new ENetChannel value, C++
    serialize/deserialize exports, and a SendOPHelper method. Give concrete code shapes.
    Note the build is mingw via WorldsAdriftRebornCoreSdk/build-mingw.sh and the SAME
    CoreSdkDll.dll is loaded by BOTH client and server (docs deployment note).
Q3. DISPATCHER WIRING. The client's CoreSdkDll must route the new channel to whatever
    destroys the entity. Trace how ADD_ENTITY_OP is routed today (Connection.cpp /
    Dispatcher.cpp / Callbacks.h / OpList.h) and specify the equivalent for removal. This is
    the riskiest part - be concrete and cite the add-path code you are mirroring.
Q4. ALTERNATIVES. If a full removal op is large, is there a cheaper interim that genuinely
    works (e.g. moving the entity far away, disabling its renderers via the mod, or an
    existing component that hides an entity)? Rank by honesty: say plainly if these are
    hacks that will not scale to resources/ships.
Q5. VERSION SAFETY. Adding an ENet channel changes the protocol. Both the server and every
    client must agree. Note the client pack distribution reality (friends run a zipped
    build) and recommend how to avoid a silent version mismatch.

## Deliverable
Write EXHAUSTIVE findings to SCRATCH/research/findings-entity-removal.md including:
- Answers to Q1-Q5 with file:line citations
- A complete implementation plan: every file to change, in order, with code shapes
- An honest effort estimate and the riskiest step called out
- Risks and an explicit list of anything you could NOT verify
Return a summary under 700 words. Do NOT edit repo files — research only.
