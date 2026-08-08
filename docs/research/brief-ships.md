# RESEARCH BRIEF 4 — SHIPS

## Mission
Determine HOW to get player-built, physics-driven airships working — the identity of
Worlds Adrift.

## CRITICAL FRAMING — read this before you form an opinion
The project owner has SEEN ships working in another Worlds Adrift Reborn effort (a private
or unpublished fork — the code is not available to us). So ships ARE achievable on this
client. Your job is NOT to deliver a feasibility verdict of "impossible". If you find
yourself concluding it cannot be done, you have mis-analysed something — dig further and
find the mechanism the other implementation must be using.

Frame every answer as: "here is the mechanism, here is what it costs us."

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md   (the twelve architecture rules —
  especially: we have NO server-side simulation, we relay component bytes verbatim; clients
  publish only components they hold AUTHORITY over; remote entities use prefab context
  "Default")
- /home/ttanurhan/Games/WAReborn-src/docs/component-ids.md (443 component ids — many ship
  related: ShipHullAgentState, ShipCoreVisualizer, ShipControlsBehaviour, PilotState 1109,
  RopeControlPoints 1098, ShipRegisteredCharactersState 4349, PlayerShipParentingVisualizer,
  CustomShipFrameVisualizer, ShipyardVisitorState, PlayerShipBlueprintInteractionState 1270...)
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`)

## Sources of truth
- Decompiled game C#:   SCRATCH/acs/
- Decompiled generated: SCRATCH/gencode/
- Game install: ~/Games/WorldsAdrift/  (prefabs, asset bundles, StreamingAssets, GameDB)
(SCRATCH = .../scratchpad)

## Questions — answer ALL with file:line evidence
Q1. ANATOMY. What IS a ship to this client? Enumerate the entity/entities involved (hull,
    core, parts, engines, sails, ropes), which prefab names/contexts they use, which
    components define them, and which visualizers render/drive them. Distinguish the ship
    ENTITY from the parts attached to it.
Q2. CONSTRUCTION. Trace ship building: the shipyard / blueprint flow
    (PlayerShipBlueprintInteractionState 1270, ShipyardVisitorState, BuilderVisualizer,
    ShipHullAgentState, hullData seen in schematics JSON). What does the client send when a
    player builds/places parts, and what must a server reply with? Is a ship design a blob
    (hullData) we can store and replay?
Q3. PHYSICS & AUTHORITY. This is the crux. The ship is a jointed physics object. Determine
    who simulates it in the original architecture (FSIM/UnityWorker vs client) and — given we
    have NO server-side simulation — whether authority over the ship's physics components can
    be granted to the PILOTING CLIENT, so it simulates and publishes while everyone else
    receives (exactly how we already do players via 190602/1073). Cite the [WorkerType]
    attributes and Writer requirements that decide this. If client-authoritative piloting is
    viable, say so and specify which components must be granted.
Q4. PARENTING. Players must stand/ride on a moving ship. We already know remote players are
    positioned by the transform-hierarchy system (TransformState.Parent + a parent entity
    carrying TransformHierarchyState) and that our single-island world never resolves parents
    (docs rule 10, RemoteRigMover positions globally instead). Ships make parenting
    mandatory. Specify exactly what the ship entity must carry for players to parent to it,
    and what changes on our side (RemoteRigMover currently IGNORES Parent — that will have to
    change).
Q5. MINIMUM VIABLE SHIP. Define the smallest thing that counts as a win, in order:
    (a) a static pre-built ship spawned in the world that players can see and stand on,
    (b) the same ship piloted by one player and correctly seen moving by the other,
    (c) player-built ships.
    For each, list the exact components/prefabs/handlers required. We want a staircase, not a
    cliff.
Q6. DEPENDENCIES. State plainly what ships need from the other three workstreams
    (persistence for ship designs, resources for parts, entity removal for
    creation/destruction) and what can proceed without them.

## Deliverable
Write EXHAUSTIVE findings to SCRATCH/research/findings-ships.md including:
- Answers to Q1-Q6 with file:line citations for every load-bearing claim
- The recommended mechanism (especially the authority/physics answer)
- A staged plan: static ship -> piloted ship -> built ship, with what each stage costs
- Risks, and an explicit list of anything you could NOT verify
Return a summary under 700 words. Do NOT edit repo files — research only.
