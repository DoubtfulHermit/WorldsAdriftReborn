# RESEARCH BRIEF 6 — MULTI-ISLAND WORLD

## Mission
Today the world is ONE island, hardcoded: `949069116@Island`, spawned at a fixed position by
WorldsAdriftRebornGameServer.cs, with every client given the SAME island entity id so
cross-client parenting resolves. The game install contains 255 island bundles
(~/Games/WorldsAdrift/Assets/unity/*@island_unityclient). Turning one island into a world is
probably the highest perceived-value-per-effort item available.

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md (rules 3, 4 and 10 especially -
  two-phase asset load, shared island entity id, and why RemoteRigMover ignores Parent)
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`)

## Sources of truth
- Decompiled game C#: SCRATCH/acs/   - generated: SCRATCH/gencode/   (SCRATCH = .../scratchpad)
- Game install: ~/Games/WorldsAdrift/ (Assets/unity island bundles, StreamingAssets,
  IslandLightingData, IslandOcclusionData, GameDB)

## Questions — answer ALL with file:line evidence
Q1. ISLAND ANATOMY: what components does an island entity need (we currently seed 190602
    TransformState, 190601 TransformHierarchyState and a few others - verify against what the
    client actually requests), and what does the client do with an island beyond rendering it?
Q2. PLACEMENT: where do island world POSITIONS come from? Is there authoritative world-layout
    data in the install (StreamingAssets, GameDB, island metadata) giving each island id a
    world position, or must we invent a layout? If data exists, give its location and format.
Q3. STREAMING: with 255 islands we cannot spawn all of them. How did the original stream
    islands by proximity (checkout radius / interest management), and what is the simplest
    thing WE can do - e.g. spawn the N nearest islands to each player, or a fixed small
    archipelago? Cite any client-side expectations about entity checkout.
Q4. PARENTING: standing on an island is the same transform-hierarchy problem as ships. Our
    RemoteRigMover deliberately ignores Parent because our single island never resolved as a
    parent. With multiple islands at different positions, what must change for players to be
    correctly positioned relative to the island they are on?
Q5. VERTICAL SLICE: define the smallest step up from today - e.g. THREE islands at known
    positions that both players can see, travel between (falling/gliding), and stand on.
    List the exact server changes.

## Deliverable
EXHAUSTIVE findings to SCRATCH/research/findings-world.md with file:line citations, a
recommended design, an ordered plan, risks, and explicit unverified items.
Return a summary under 700 words. Do NOT edit repo files.
