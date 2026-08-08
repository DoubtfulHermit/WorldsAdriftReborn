# RESEARCH BRIEF 1 — PERSISTENCE

## Mission
Make characters, inventory and progress SURVIVE a logout. Today nothing persists:
`WorldsAdriftServer/Handlers/CharacterScreen/CharacterSaveHandler.cs` receives the client's
save POST and discards it ("todo for future: store changes"). Every session starts fresh.
This is the #1 blocker to the project being a game rather than a demo, and it is a
prerequisite for resources and ships (farmed/crafted things must survive).

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md   (the twelve architecture rules)
- /home/ttanurhan/Games/WAReborn-src/docs/component-ids.md (443 component ids)
- The repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`)

## Sources of truth
- Decompiled game C#:      SCRATCH/acs/
- Decompiled generated:    SCRATCH/gencode/
- SDK decompiled:          SCRATCH/sdk-decomp/
(SCRATCH = the directory containing this brief's parent, i.e. .../scratchpad)

## Questions — answer ALL with file:line evidence
Q1. CHARACTER. Trace the full character lifecycle: what the client POSTs to
    /character/{build}/steam/{id}/{uid} (exact JSON shape, cite CharacterCreationData in acs),
    what the client expects back from /characterList (CharacterListResponse), and what
    minimum stored state makes the game restore the SAME character (appearance, name, uid)
    rather than generating a new one. Note the current uid is the literal placeholder
    "valid-UIDs-have-at-least-one-" — determine the real uid rules (issue #17 upstream).
Q2. INVENTORY. Where does inventory truly live? The server seeds 1081 InventoryState from
    ItemHelper/itemData.json, the client fakes item metadata (WorldsAdriftReborn/Patching/
    Inventory/InventoryPatches.cs), and InventoryModificationState_Handler (1082) mutates
    slotType on equip. Determine exactly what must be persisted server-side to restore a
    player's inventory: item ids, types, positions, slot assignments, hotbar, stash.
Q3. SCHEMATICS/PROGRESSION. 1080 SchematicsLearnerGSimState, 1260 SchematicsUnlearnerState,
    1241 LorePiecesCollectorClientState. What does the client expect on login for learned
    schematics, and is that needed for crafting to work across sessions?
Q4. WORLD STATE. Does anything else need persisting for a session to feel continuous —
    player position/island, health (1077), equipped wearables (1280)? Distinguish
    "must persist" from "can be defaulted".
Q5. STORAGE DESIGN. Recommend the concrete storage: file-per-player JSON vs SQLite vs
    LiteDB, keyed how (Steam id? character uid?), and where it plugs into the existing
    server (which handlers read/write). The server is .NET 6 running under Wine on a VPS —
    weigh dependencies accordingly. Keep it SIMPLE and inspectable.
Q6. IDENTITY. The game server (ENet) has NO identity link to the login server (HTTP) —
    verified earlier. For per-player persistence the game server must know WHO a peer is.
    Determine the smallest reliable way to establish that (does any early client message
    carry a name/uid? 1086 PlayerName? the client's own published components?).

## Deliverable
Write EXHAUSTIVE findings to SCRATCH/research/findings-persistence.md including:
- Answers to Q1-Q6 with file:line citations for every load-bearing claim
- A concrete recommended design (storage shape + which files change)
- An ordered implementation plan, smallest correct steps first
- Risks, and an explicit list of anything you could NOT verify
Return a summary under 700 words. Do NOT edit repo files — research only.
