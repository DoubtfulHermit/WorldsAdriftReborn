# FINDINGS — PROGRESSION (knowledge, schematics, tools, lore, quests)

## LEAD: seed `8051 ToolState` to **2**, not 30 — then push 6, 14, 30
`ComponentsSerializer.cs:491` seeds `ToolStateData(30)` = *everything*. Seed **2**
(Salvage only) and the player starts with the one tool they can use, **with zero toasts**
— `ToolBehaviour.OnUnlockedToolsChanged` fires a feedback card for Scan/Repair/Build but
**never for Salvage** (`acs/ToolBehaviour.cs:43-61`). Then push `6` on any trigger and the
client, unprompted:
- pops the game's own **"SCANNER TOOL UNLOCKED"** card, text from the shipped GameDB (`:59`)
- lights the scanner hotbar slot (`HotBarScreen.cs:161-176`)
- fills the character-sheet slot (`CharacterToolSlot.cs:55-70`)
- adds it to the mouse-wheel cycle (`InteractAgentObserver.cs:254-277`)

Two more pushes (14 = +Repair, 30 = +Build) give three more unlock moments. **One changed
literal plus a SendComponentUpdateOp**, and it simultaneously fixes the login-toast bug.

## 8051 = 30 DECODED
Bitmask over `ToolType`: `Multitool=1, Salvage=2, Scan=4, Repair=8, Build=0x10, Grapple=0x20`.
`30 = Salvage|Scan|Repair|Build` — everything the mask can gate; Multitool and Grapple are
hardcoded true (`ToolBehaviour.cs:95-107`). Ladder: **2 → 6 → 14 → 30**.

**Correction: it is THREE toasts, not four.** Salvage has no `SendFeedbackEvent` call —
only a silent callback. The UI event payload `(ToolType)newTools` casts a *bitmask* to a
single-value enum; harmless, every consumer re-reads via `IsToolUnlocked`.

**Nothing in the shipped client ever calls `RequestToolUnlock`** — tool unlocks were
purely GSim-driven. That is why they are our cheapest lever: the server is *supposed* to
be the only actor.

## THE PROGRESSION GRAPH
```
SCAN (1330 ScannableState{knowledge}) → KNOWLEDGE (1332)
  ├─ spend on a node (client sends UseNode on 1334) → knowledgeNodeUses[id]++
  │    ├─ SCHEMATIC_FIXED/_LIST/_RANDOM → append to 1079 learnedSchematics
  │    ├─ SLOT       → raise maxXSchematics on 1079
  │    ├─ CIPHERSLOT → raise 1332 cipherSlotCounts
  │    └─ TECHNOLOGY → e.g. "Shipbuilding"
  └─ lifetimeKnowledge → free schematics at thresholds
→ SCHEMATICS (1079, resolved against 1097) → CRAFTING BOOK → BETTER ITEMS
```
**Tools sit OUTSIDE this chain** — 8051 is an independent bitmask with its own request bus.

## SIX COMPONENTS WE NEVER SEED — verified, 57 branches, none of these
| id | component | consequence |
|---|---|---|
| **1334** KnowledgeClientState | `ScanningAgentVisualizer` requires this **writer** → never enables → no knowledge events; `UseKnowledgeNode` would NRE |
| **8053** PlayerQuestState | `QuestManager` requires this **writer** |
| **8054** PlayerQuestRequestState | `QuestManager` requires this **reader** → **no quest system at all** |
| **1307** GlobalKnowledgeGraphDataState | on a *global* entity → node costs never overridden and `LifetimeKnowledgeNodePanel` builds **zero** nodes |
| **1330** ScannableState | nothing in the world is worth knowledge |
| **1331** ScanningAgentServerState | no "already scanned" dedup → rescans re-pay |

⚠ **`SendAddComponentOp` is called with `failOnComponentInitError: true`** and an unhandled
id leaves `len == 0`, which **aborts the whole batch** (`SendOPHelper.cs:85-94`). If the
client requests 1334/8053/8054 today, entire AddComponent batches are being dropped.
**Grep the server log for `[error] failed to initialize component` — a one-minute check
that reorders this whole plan.**

## KNOWLEDGE — the tree is BAKED INTO A PREFAB, not sent by the server
`KnowledgeManagerScreen.Connect()` builds it from `GetComponentsInChildren<KnowledgeNode>`
(`:349-350`), indexed by the **serialized** `nodeData.id`. The server's 1307 `graphJson`
only **overrides two fields on nodes that already exist** (`knowledgeCost`,
`usesToUnlock`, `:598-666`). An unknown id logs an error unless it contains "cipher".

`LifetimeKnowledgeNodePanel` reads a **fifth** section and **throws** without it (`:34-35`):
`jObject["unlockedByLifetimeKnowledge"]["schematics"]` — key = schematic id, value =
lifetimeKnowledge threshold. Nodes stay hidden until `knowledgeNodeUses["Shipbuilding"] > 0`.

**"Empty map deactivates every node" — substantially right, mechanism corrected.** Branches
and chrome are hardcoded visible (`KnowledgeBranch.cs:81`, `KnowledgeManagerScreen.cs:231`).
What collapses is the *tree*: `nodeData.purchased` is only ever set from the uses map
(`:689-693`), so no root is purchased, every child fails `allParentsPurchased` and hides.
**Net: exactly one visible, unaffordable node per branch.**

**Seed `knowledge: 0`, both maps EMPTY.** The empty map is correct — do not fake node ids.
**The real node ids are unrecoverable** (serialized in the prefab); only `"Shipbuilding"`
is confirmed. **The server never needs the graph anyway**: the client sends the node id it
wants (`UseNode{string id}`) having already checked affordability, so a 1334 handler need
only increment the uses map, deduct a cost, and reply `KnowledgeUseResponse(Success)`.
Publish costs via 1307 and both sides agree by construction.
`_MAX_KNOWLEDGE_POINTS = 10000` is the bar denominator — stay well under.

## SCHEMATICS — `defaultSchematics` vs `learnedSchematics` is a SERVER CONVENTION ONLY
`SchematicSystem.UpdateNonShipSchematics` merges both into one `HashSet<string>` (`:124-138`).
Downstream **nothing can tell them apart**. The property that behaves like "learned" is
**`SchematicData.unlearnable`** in the reference JSON — it drives the per-category count
display and the unlearn button. The caps (`maxInventorySchematics` etc.) are
**display-only**; no enforcement exists anywhere.
**Rule:** starter recipes in `defaultSchematics` with `unlearnable:false`; purchases in
`learnedSchematics` with `unlearnable:true`.

Four original learning paths: knowledge purchase (dominant) · lifetime thresholds ·
entitlements (1080 `schematicsFromEntitlements`) · **proximity**
(`ProxySchematicEnablerState` gates some on standing near an enabler).
Unlearn is client-initiated on **1260** (already granted).

`AddSchematicLearnt(SchematicLearnt{title})` on 1079 produces a real in-game
**"SCHEMATIC LEARNED"** card from GameDB row `KeySCHEMATIC_LEARNED` — one update.

### The "static defaultSchematics is enough" claim — THREE omitted conditions
(a) **The id must resolve or it is silently dropped** (`CharacterLearnedSchematicLibrary.cs:58-62`).
    **Escape hatch nobody noted: a string may itself be a full `SchematicData` JSON document**
    — `LookupSchematic` falls through to `TryParseProcedural` (`SchematicsReferenceStore.cs:68-81`).
    The server can author schematics inline without touching 1097.
(b) `GsimReferenceDataLoaded` must be true or the add is **rejected outright** (`:227-237`),
    and `AllReferenceAndPlayerDataLoaded` needs **all five** refdata types.
(c) **`category` must parse or the UI throws** — `Enum.Parse` at `:342`. Legal:
    `Shipyard, Personal, CraftingStation, Cooking, Clothing, None`.
(d) **"Usable" means VISIBLE, not craftable** — 1003's handler is a pure echo.

Today only `"glider"` resolves (defined in both `ReferenceDataRequestState_Handler.cs:57`
and `InventoryPatches.cs:62`); anything else silently vanishes.

## NewPlayerState (8055) — FIVE consumers, two of them new
Seeded `true`. While true: crew beacon button hidden · unlearn button hidden · ship-building
quest deferred · **Haven death screen stripped** (`DeathScreen.cs:121-127`) · **biome-name
notification suppressed on respawn** (`RespawnVisualizer.cs:508-517`).
It is not "hide two buttons" — it means **"the player is still in Haven"**.
**No client writer exists.** The server is the only possible actor. **Correct value: `false`.**
Only the `true→false` edge does anything (a 5 s bloom flash), so seeding false is silent.

**Correction to "waits forever on an unreachable event":** right outcome, wrong mechanism,
and the real one is worse. `NewPlayerFlagUpdated` *is* reachable. The genuine blocker is
that **`StartTracking()` never runs** — `QuestManager.AddQueuedUpQuests` blocks on
`while (_gsimStateReader == null)` and neither 8053 nor 8054 is seeded. Flipping 8055 alone
changes nothing about quests.

**Quests are worth wiring and unusually cheap** — 8053 is a **client-side Writer** holding
`{runningQuests, completedQuests}`, so the client owns progression and we only seed, grant
and persist. 8054 lets the server *start* a quest by name. ~30 condition types already
exist including `ToolUnlockedCondition`, `HaveKnowledgeCondition`, `UseSalvageToolCondition`.
**But content-limited:** quest definitions are `ScriptableObject`s in client `Resources`
keyed by int id — **we can only start quests Bossa authored.** Only id 100 is confirmed.

## LORE — the cheapest authored content in the game
**1240** (server-owned, seeded) + **1241** (client-owned, seeded **and already granted**).
`LorePiece{title, pieceNumber, totalPiecesInSet, text}` — content entirely server-authored.
Handshake: server sets `KnownLore` on 1240 → client requests missing texts on 1241 (retrying
every 5 s) → server answers `RequestLorePiecesDataResponse` on 1240 → the in-game **Logbook**
fills, grouped by title into Incomplete/Completed with unread markers.
**One new handler. Zero new seeds, zero client patches, zero world entities.**

**But lore feeds NOTHING.** Exhaustive search found no link between lore and knowledge.
It is a collection log, not a currency — which makes it the ideal carrier for milestone
rewards ("you unlocked the Scanner" → award a lore piece explaining it).

**Databanks are a different thing and strictly worse as a first collectible**: they need
8073 on the object, a 1243 **writer** on the island, and their registration lives in a
`[WorkerType(UnityWorker)]` behaviour we cannot host. More prerequisites than a material node.

## PERSISTENCE SCHEMA
Progression lives in the **game** server; the shipped persistence lives in the **login**
server, with no project reference between them. **Lift `JsonFileStore` into
`...Multiplayer`** (the pure tested assembly) and have both reference it. Key on
`characterUid` from the 1088 update. `<data>/players/<characterUid>.json`:
`unlockedTools` · `isNewPlayer` · `knowledge` · `lifetimeKnowledge` · `knowledgeNodeUses` ·
`cipherSlotCounts` · `alreadyScannedEntities` (else rescans re-pay) · `learnedSchematics` ·
the four `max*` caps · `knownLore` · `quests{running,completed}`.
**Excluded:** `defaultSchematics` (server policy — config file), the knowledge graph
(deployment config), position (the sky-teleport bug).
Pure `ProgressionPolicy` + thin `ProgressionRepository`, mirroring `RosterPolicy`/`CharacterRepository`.
**Autosave is mandatory, not belt-and-braces** — disconnect may not fire.

## ORDERED PLAN
**Step 0 (5 min):** grep the log for `[error] failed to initialize component`. If
1334/8053/8054 appear, A4 jumps to the front.
**A — make the UI stop lying (all literals):** A1 `ToolStateData(30)`→`(2)` · A2
`NewPlayerStateData(true)`→`(false)` · A3 `defaultSchematics = {"glider"}` · A4 seed
1334/8053/8054 (+ 1334 and 8053 authoritative — client writers) · A5 knowledge seed to 0/empty.
**B — real progression:** B1 per-player store · B2 tool grants that mean something
(**the first true progression loop, needs no world content**) · B3 lore as the reward channel
· B4 quests (log `Quests.Collection` first) · B5 knowledge earn+spend (needs a 1307 branch
with `unlockedByLifetimeKnowledge` or the panel throws) · B6 schematic grants on purchase ·
B7 crafting (largest piece; deliberately last).
**A1–A5 and B1–B3 are entirely independent of the harvesting prerequisite chain.**

## HOUSEKEEPING
`ChangeLogLoader_Patch.cs` constructs ~40 component Datas with values identical to the live
seeds. It is a **dead protobuf serialization scratchpad** — every object is discarded and
the method returns false. **Editing it changes nothing.** The live seeding is
`ComponentsSerializer.cs`.

## COULD NOT DETERMINE
Whether the client requests 1334/8053/8054 today (one log grep). The real
`KnowledgeNode.nodeData.id` list (serialized in the prefab — without it no working
`graphJson`). The contents of `QuestCollection`. Cipher id format. The original knowledge
economy numbers — GSim-side, **must be invented**. Whether the 8051 seed lands before
`ToolBehaviour.OnEnable` (decides whether the toast-suppression Harmony patch has anything
to read). Nothing was executed.
