# FINDINGS — TREE HARVEST

## VERDICT: buildable, and SMALLER than either prior pass concluded
The server harvests a tree by publishing **one integer** (`TreeFSimState.sectionMask`, 1036)
and the stock client removes the sections with full visuals and SFX. `TreeVisualizer` (no
`[WorkerType]`) does the geometry; `TreeClientVisualizer` does impact VFX + wood-break SFX.
**Both verified present on the shipped `Tree_unityclient` prefab.** Neither needs the FSim.

**The biggest unknown is NOT the tree.** It is whether the client's interest batch for a
tree entity can be served at all — see F1.

## ADJUDICATING THE TWO PRIOR PASSES
- **"Tree MVP retracted, FSim-only"** — **WRONG.** Pass 2 was right to overturn it.
- **"The multitool beam cannot fire at all"** — **HALF WRONG.** The *beam* (engage, warm-up,
  charge, discharge, VFX, `ShotEntity`) lives in `PlayerMultitool` which has **zero
  `[Require]` fields**, and `PlayerMultitool.cs:209` gates on nothing networked. What
  2105/2106/2002 gate is *publishing the shot*; what 1231 gates is *`HitInfo`*. Both are
  still hard prerequisites — for different reasons than stated. **The beam itself is free.**
- **"`maxBoltDistance` must be seeded"** — **RIGHT.**
- **"The MVP needs 1099+1016 for `IsSalvageable`"** — **WRONG for trees.**
  `SalvageableItemVisualiser.IsSalvageable()` returns true even with an unresolved
  `[Require]`, because `IsDamaged()` short-circuits on `state == null`. (They must still be
  *seeded* — see F1.)
- **"The yield ships in `resourcePerSection`"** — **TRUE of the schema, MISLEADING.**
  `grep resourcePerSection` across the whole client returns **zero hits**. No client code
  reads it. Same for `TreeState.respawnTime`. Both are pure server-side convention slots.
- **"Prefabs at `EntityPrefabs/Environment/Tree*`"** — wrong path (keys are flat,
  `entityprefabs/tree_unityclient`), right conclusion: send `"Tree"`.

## THE ONE FIELD THAT MATTERS — and the one that will silently kill you
`1036 TreeFSimState.sectionMask` is the harvest channel. Seed `(1 << sectionCount) - 1`.
**`1035 TreeState.scale` MUST be `Vector3d(1,1,1)`.** `TreeScaleVisualiser.cs:15-18` writes
`transform.localScale` from it, and `Vector3d`'s default is `(0,0,0)`. **A zero seed gives a
scale-0 invisible tree with working colliders and no log line.** This is the single most
likely silent failure in the feature.

Fields with **zero client readers**: `sectionHealth`, `sectionCount`, `resourcePerSection`,
`woodType`, `prefabName`, `respawnTime`. Seed them for correctness, but nothing renders them.
`dynamic` must stay **false** — its setter starts the tree-falling audio loop.

## THE DAMAGE MODEL — the shipped game was ONE HIT PER SECTION
`TreeSection.cs:73-84` reads literally:
```csharp
connectionStrength = 0;
connectionStrength--;
```
**The multi-hit design implied by the initial `3` was short-circuited before ship.**
`sectionHealth` in a live tree only ever holds `3` (intact) or `-1` (cut). The per-shot
*feel* came from the tool: `WarmUpDurationSec = 2f`, `MinDeployInterval = 0.75f`.

Harvest splits the mask: `falling = WalkTree(section)` (that section plus everything rooted
above it), `remaining = sectionMask & ~falling`. **The game never clears the last section** —
`if (tree.sectionsActive <= 1) return;`. So "harvested out" is `popcount(mask) == 1`, and the
stump (section 0, non-harvestable on every shipped tree) always remains. **Do not try to
reach mask 0** — nothing on the client removes the tree, and there is no `RemoveEntityOp`.

## 1037 IS EDGE-TRIGGERED, NOT LEVEL-TRIGGERED
`FinishAndSend_ResolveDiff` suppresses a send when nothing changed. **The server gets one
1037 packet when the beam moves ONTO a section and one when it leaves. There is never a
per-hit pulse from 1037.** So 1037 is *latched aim state*; the hit cadence must come from
elsewhere. Two designs:
- **Design A (faithful):** seed+grant 1037, 1231, 2105, 2106, 2002; handler on 2105
  `shotEntityEvent` is the per-hit signal. Also replicates the shot VFX to other players.
- **Design B (cheapest):** seed+grant 1037 + 1231 only; server timer applies one hit every
  0.75 s while the latch names a valid section. 2 seeds instead of 5, one handler.
  **Ship B first, then A.**

`SalvagerAimerStateData` has **four** fields — a prior pass listed three and missed
`lookDirectionEuler`.

## GEOMETRY — measured from the shipped assets
`Tree_unityclient`: **12 sections, 4 branches** — `root=0 sections=[1,2,3,4,6,8,9,10]`,
`root=4 [5]`, `root=6 [7]`, `root=9 [11]`. `sectionMask = 4095`. `harvestable` true on 1–11,
**false on 0** (the stump). `dynamic=false`, `fsimVisualizer=NULL` (which is why the
client runs the mesh-combine).

**66 of 72 client tree prefabs are real sectioned trees**; the 6 `WoodlandTree*` have no
`TreeBase` and are static props. Sections are **NOT uniform** — 9/1, 9/3, 10/4, 11/4, 11/5,
**12/4 (`Tree`)**, 13/9, 14/6, 14/9.
**Traps in the outliers:** `TreePalmStubby` ids 5–12 are harvestable **with no `cutPoint`** →
guaranteed NRE. `TreePalmStubby02` has ids 0 *and* 1 non-harvestable. **Ship `Tree` first.**

**Clearing a lower section does NOT topple the ones above here.** The original split the mask
*and* spawned a new dynamic entity via `SpawnNewTreeBit`. **Unbuildable on this server** — no
dynamic entity creation, no physics authority. **The severed subtree pops out of existence.**
Expect "it doesn't fall over" as the first complaint.

## FAILURE MODES — F1 IS THE ONE THAT MATTERS
**F1 — the interest batch aborts and the tree gets NOTHING.**
`WorldsAdriftRebornGameServer.cs:684` passes `failOnComponentInitError: **true**`, and the
tree prefab's `[Require]` closure asks for **at least eight ids `ComponentsSerializer` has no
branch for**: **1035, 1036, 1016, 1099, 1183, 4333, 4400, 1232**. One unhandled id aborts the
**entire** batch. **Symptom: the tree appears, fully rendered, and is completely inert.**
Tell: `[error] failed to initialize component NNNN` then `[info] aborting send of components.`
**No prior pass flagged this all-or-nothing behaviour.**

**F2** zero `scale` → invisible tree, no log line.
**F3** the tree branch must precede the generic 190602 at `:57`, which answers *every* entity
with the origin.
**F4** multi-hit health is **invisible** — nothing reads `sectionHealth`, so hits 1..n-1 give
zero feedback. **Ship 1 hit per section** (what the game shipped) or drive feedback elsewhere.
**F5** `ShowReplicatedVisualHitAndPlaySfx` guards `cutPoint` for position then dereferences
`cutPoint.rotation` unconditionally — NREs on stubby palms. Per-callback try/catch means the
section still disappears; **silently degraded, never fatal.**
**F7** 1231 seeded with `maxBoltDistance = 0` → `HitInfo` null forever → 1037 publishes once
and the diff-resolver suppresses everything after. **Zero packets is the EXPECTED look of
this bug**, indistinguishable from "the grant didn't work".
**F8 (Design A)** `PlayerMultitoolVisualizer.OnEnable` dereferences `PlayerMultitool`, which
`LocalPlayerInit` assigns — and `LocalPlayerInit` waits only on 1086. **Order the seeds so
1086 lands no later than 2105/2106/2002 in the same batch.**
**F10** do not copy `SectionMask(int.MaxValue)` on respawn — use `(1<<N)-1`, or
`TreeClientVisualizer`'s diff bookkeeping holds phantom bits.
**F12** `SendOPHelper.cs:217` passes `updates.Count` instead of `cupdates.Count` — send one
component id per call and it cannot bite.

## PLAN
**Step 0 (half a day, NO CODE):** log the requested id list in the `else` at `:681-688`,
spawn a tree by hand, read the log. **This gates everything.**
**1.** Pure `TreeTopology` (port `WalkTree` + the mask split), `TreeStore`, `TreeCutterLatch`
in `...Multiplayer/` — the test project references only that assembly.
**2.** Add 1037 + 1231 to `AuthoritativeComponents` (+2105/2106/2002 for A). **Not 1035/1036
— those are server-authoritative.** Update `MirrorSendPolicyTests`.
**3.** Seed branches, **all reading `entityId`** (the 1088/AppearanceStore precedent).
**4.** Spawn: AssetLoadRequest `"Tree"` → ack → AddEntity, **same entity id on every peer**.
For an MVP, latch the last decoded player position and spawn 5 m in front — **one hardcoded
coordinate away from a demo**, no island handshake needed.
**5.** Handlers (auto-registered by attribute).
**6.** **Push 1036 to EVERY peer directly** — `RelayToOtherPlayers` substitutes the sender's
own entity id and would relabel the update as being about the player's avatar. Send **only**
`SetSectionMask`; never `Data.ToUpdate()`.
**7.** Grant wood via the existing 1081 full-replacement path; item ids **≥1104**.
**8.** Tests: `WalkTree(4)` → `{4,5}`; harvesting section 9 clears bits 9 and 11; the last
harvestable section is refused; a late joiner's seed reflects the **current** mask, not 4095.

## COULD NOT DETERMINE
**The exact interest list the client sends for a tree** — the §Step 0 table is the static
`[Require]` closure, but `ExtractVisualizers` walks the whole hierarchy so a child could add
an id. **Five-minute empirical check, and it gates everything.**
Original damage/yield numbers (**you are inventing these**). The units of `respawnTime`.
Whether all eight stub seeds are constructible (field layouts read, ctor arities unverified).
Nothing was executed.
