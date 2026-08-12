# RESEARCH BRIEF 2 — ENVIRONMENT & RESOURCES (harvestables)

## Mission
Populate islands with HARVESTABLE resources — trees, rock, metal deposits, scrap — that
players can gather. This is the engine of the gameplay loop (farm -> craft -> build ships).
Today the server spawns exactly ONE entity besides players: the island itself
(`949069116@Island`, hardcoded). The world is otherwise empty.

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md   (the twelve architecture rules)
- /home/ttanurhan/Games/WAReborn-src/docs/component-ids.md (443 component ids)
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`)
- Note how entities are spawned today: WorldsAdriftRebornGameServer.cs (AssetLoadRequestOp
  then AddEntityOp then AddComponentOp), and docs rule 3 (two-phase mirror).

## Sources of truth
- Decompiled game C#:   SCRATCH/acs/
- Decompiled generated: SCRATCH/gencode/
- Game install (assets, 255 island bundles): ~/Games/WorldsAdrift/Assets/unity/
(SCRATCH = .../scratchpad)

## Questions — answer ALL with file:line evidence
Q1. REPRESENTATION. Are resource nodes (a) baked into the island asset bundle and rendered
    client-side with no server entity, or (b) separate SpatialOS entities the server must
    spawn? Decide with evidence. Strong hint they are entities: acs contains
    MetalDepositScrapVisualiser_fsim, MetalDepositCrustVisualiser, MetalDepositAtlasVisualiser_fsim,
    and the component map has resource-ish ids. Enumerate every harvestable-related
    visualizer and the components it [Require]s.
Q2. PREFABS. What prefab names/contexts do harvestable entities use (the analogue of
    "Traveller"/"Default" for players)? The client only instantiates entities whose prefab
    ASSET is loaded (docs rule 3) - determine what AssetLoadRequestOp we must send for e.g.
    a tree, a rock, a metal deposit. Search the asset bundles / prefab preprocessors.
Q3. PLACEMENT. Where do node POSITIONS come from? Is there island metadata (in the island
    bundle, StreamingAssets, GameDB, IslandLightingData/IslandOcclusionData) listing resource
    spawn points, or did the original server generate/store them? If the data exists locally,
    say exactly where and in what format.
Q4. HARVESTING. Trace the full interaction: player aims the multitool/scanner at a node ->
    what components/events fire -> what the server must respond with -> how the item ends up
    in inventory. Relevant existing pieces: InteractAgentState (1211/1212),
    InventoryModificationState (1082) whose handler already services equip, ReferenceDataState
    (1097) which serves item definitions via ItemHelper. Identify precisely which server-side
    handlers must be added.
Q5. NODE STATE. How is depletion represented (health? quantity? a state component?), and what
    must persist/relay so BOTH players see a node deplete and disappear. Note: entity REMOVAL
    has no wire message today (see the entity-removal research) - flag where that blocks us.
Q6. MINIMUM VIABLE. Define the smallest slice that produces a real loop: one node type, one
    tool, one item into inventory, visible to both players. Concrete component ids, prefab
    names, handlers.

## Deliverable
Write EXHAUSTIVE findings to SCRATCH/research/findings-resources.md including:
- Answers to Q1-Q6 with file:line citations
- A recommended minimal design and an ordered implementation plan
- Explicit dependencies on persistence and on entity removal
- Risks and an explicit list of anything you could NOT verify
Return a summary under 700 words. Do NOT edit repo files — research only.
