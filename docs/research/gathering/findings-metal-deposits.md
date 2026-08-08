# FINDINGS — METAL DEPOSITS

## LEAD: the cheapest metal node a player can deplete and collect is `MetalNugget`
**One entity, four components, no graph, no parent, no variant table** — and uniquely among
the metal prefabs **its geometry is BAKED into the prefab**, so it renders the instant it
spawns with no visualiser initialisation at all.

Seed **1099 + 1210 + 190602 + 190601** (the last two already seeded today) and you get a
nugget that is visible, is a legal beam target, publishes a shot event, **and** offers an
"E to pick up" prompt.

## THE "4-ENTITY GRAPH" CLAIM IS WRONG — the deposit is ONE entity
`MetalDepositVisualiser.cs:37-41` uses **`GetComponent`**, not `GetComponentInChildren`,
not a FetchEntity. Deposit, core and crust are **three MonoBehaviours on one GameObject =
one SpatialOS entity**. Verified against the shipped `metal_deposit_entity_unityclient`
(pathID 86108): all three visualisers on the root with cross-references correctly wired.
The crust geometry is *imported at runtime* into the same GameObject from a biome variant
prefab (`:133`).

Maximum graph is **1 deposit + N scrap + 0..1 atlas shard**; nugget and boulder are
free-standing.

### Dead reference fields (never read by any client code)
`MetalDepositStateData.coreId` · `MetalRockCoreStateData.depositId/islandId/attachedEntities`
· `MetalRockCrustStateData.coreId` · `MetalRockStateData.islandId/surfaceNuggets` ·
`MetalNuggetStateData.parentId/isSurfaceNugget`.
**LIVE and load-bearing:** `MetalRockScrapStateData.rockCoreId` and
`MetalDepositAtlasShardStateData.rockCoreId` — both must point at a live, initialised
deposit or the entity never renders.

### Components with ZERO client readers — do not seed
**1032 MetalRockState** (1 reader, in a scratch/editor toy not on any prefab) · **1033**
MetalRockClientState · **1034** MetalNuggetState · **12289** MetalRockCrustClientState ·
**2104** MetalRockSpawnState · **1030** RawMaterialSourceState · **1031** HarvesterState ·
**1174** SalvageableState.
**Correction: `MetalDepositCoreVisualiser` requires 2103 + 1016 + 190602 — NOT 1032.**

## COMPONENT CONTRACTS
- **1255 MetalDepositState** `{variantId, coreId}` — only `variantId` is used. **It must name
  a `MetalDepositVisuals` asset in the biome's PropLibrary (case-insensitive) or
  `InstantiateVariant` rejects and the visualiser sets `enabled = false` — an invisible,
  dead entity.** You never need to enumerate them: the client hands you a valid one (P2).
- **2103 MetalRockCoreState** `{depositId, attachedEntities, islandId, isDestroyed}` — only
  `isDestroyed` live. `attachedEntities` **must be non-null** (DeepCopy dereferences `.Count`).
- **12283 MetalRockCrustState** `{coreId, depositId, shotPoints, exploded}` + event
  `ShotCrustEvent{Vector3f offset}`. `shotPoints` **must be non-null**.
- **1016 ItemHealthState** `{health, maxHealth, vulnerabilityState, disableRepair}` — drives
  `HealthPct` → progressive core-crack damage models. Sizing: `SalvageShootDamage = 200`,
  `MinDeployInterval = 0.75s`, so **maxHealth 2000 ≈ 10 shots ≈ 7.5 s of beam**.
- **1099 SalvageAndRepairState** — makes something a legal beam target.
  **`originalMaterials` must be non-null** (`MaterialSourceVisualizer.OnEnable` dereferences it).
- **1210 InteractiveState** — `InteractiveObjectVisualizer.OnEnable:67` does
  `Interactions.FirstOrDefault(i => i.verb == Verb)`; **no matching entry ⇒ radius and
  timeToUse are 0 and the prompt never appears.**
  `InteractVerb`: `Default, Activate, PickUp, Man, Inventory, Craft, Harvest, Forced,
  Design, ReclaimShip, ShipBoost`.

## THE MINING LOOP
Aim (`SalvagerAimerObserver`, **`GetComponent` — ROOT ONLY**) → trigger → gate
(`GetComponentInEntity<Salvageable>` — **whole tree**) → publish 2105 `ShotEntityEvent` +
2106 `ShotEvent{entityId, shotCoordinate, shotDirection}`.

**Server then:** append the local-space offset to 12283 `shotPoints` + `AddShot(...)` in the
same update → crust fractures and deletes fragments within a randomised 0.2–0.3 m blast
radius → once holed, decrement 1016 → core cycles through crack models with SFX → at zero
set 2103 `isDestroyed` **and** 12283 `exploded`.

`SimulateShot` **returns the fragment count; 0 means the beam went through a hole to the
core** — that is the client's own "crust is broken here" tell.

**One-shot suppression:** `MetalDepositCoreVisualiser.OnCoreDestroyed` never explodes on the
*first* callback after enable — it only arms the flag. **So a late joiner seeded with
`isDestroyed=true` gets the correct silent state, not a replayed explosion.** Good design;
copy it.

## `shotPoints` — the best-designed piece in the system
**Damage IS the point cloud.** No counter, no health bar, no per-fragment bitmask. Each
point erases whatever fragments lie within a randomised radius.
Live path → `SimulateShot` (physics debris, shrinks over 6.5 s).
Late-join path → `SimulatePastShot` (renderer off, collider off, **instant, silent**).

**Two consequences to design around:**
1. **Replay is not pixel-identical** — the blast radius is re-rolled per replayed point.
   Statistically equivalent hole. Invisible in practice.
2. **There is a transform inconsistency in the shipped client**: the live path transforms by
   `_visuals.transform`, the replay path by `base.transform` (the entity root). They coincide
   only if the crust child sits at local identity. **Encode against `base.transform`** —
   that is the transform the server actually knows.

**`shotPoints` is `Vector3f` (plain float) — NO 4096 factor.** 190602 is Q52.12 and DOES
need ×4096. **Mixing these up is the easiest way to get this wrong.**
**Cap the list** — it is replicated in full on every update and replayed linearly on every
join. 40–60 points is already a destroyed crust.

**Confirmed: no client path ever destroys the deposit GameObject.** Depletion is entirely
state-based, so the missing `RemoveEntityOp` does **not** block this milestone.

## SURFACE NUGGETS — the answer is split
**(a) The nugget SPAWNING system is gone.** 1032's `SpawnNuggets`/`SpawnSurfacePieces` events
and `RandomNuggetChooser.PickRandom` have **zero callers**; 1032/1034 have zero readers.
Whatever drove that handshake was stripped from this build. **Do not build on it.**
**(b) The nugget PREFAB is alive and fully self-contained.** No parent lookup anywhere;
geometry baked; six capsule colliders.

Two caveats: it has no `ComponentMaterialColors`, so it **always renders as aluminium**
regardless of `materialTypeId` (cosmetic); and it has **no depletion feedback of any kind**
(`IsSalvageable() => true`, `IsDamaged() => false` unconditionally). **Workaround: teleport
it via 190602** — one component update, and the transform behaviours are already live.

## COLLECTION — three mechanics, two routes, BOTH BLOCKED TODAY
- **Route A, beam salvage (scrap):** `MetalScrapSalvaging : ISalvagingFeedback` makes the
  scrap **vanish client-side immediately** — real collection feedback with no removal op.
- **Route B, interact pickup (nugget, atlas):** `IssueInteraction` → 1211
  `TriggerInteractWithObject(entityId, verb)`.
- **Route C:** none for the deposit itself — its yield is the scrap it releases.

**Route A needs 2105/2106/2002/1231 (none seeded, none granted). Route B needs 1211 — seeded
but ABSENT from `authoritativeComponents`, so `InteractAgentObserver` never enables.**
Rule 14 hitting a second subsystem nobody had checked.

Grant is always server-authored: 1082 has zero data fields, no `AddItem` verb, and no
`InventoryStateWriter` exists in the client. **The "+N Iron" SFX is free** from the client's
own 1081 diff.

## SEQUENCING VERDICT — right advice, false reason
"4-entity graph" is **false**. But there is a real reason to be careful that nobody caught:
**`metal_deposit_entity`'s root carries NO `Salvageable` and NO `InteractiveObjectVisualizer`**
(verified against the shipped prefab). So `SalvagerAimerObserver.IsSalvageable`, which uses
`GetComponent` on the **root**, means **a deposit can never be a 1231 aim target — verified,
not inferred.** Whether `PlayerMultitool`'s whole-tree gate passes depends on whether the
runtime-imported variant carries a `Salvageable` — **the one thing not determinable statically.**

**Metal before trees, unambiguously.** Trees are a dead end for harvesting
(`TreeFsimVisualizer` is UnityWorker-only, and `IslandProxyVisualizer.OnSpawnResources`
handles only `Metal` and `Egg`). **Metal is the only resource line where every stage —
spawn, aim, hit, deplete, collect — has a live client implementation.**

### Milestones
**M0** "a metal thing exists" — one `MetalNugget`, 4 components, nothing new on the player.
Proves per-entity seeding and the asset-ack sequence for a non-player entity. One afternoon.
**M1** "I shot it and the server knows" — grant 2105/2106/2002/1231. **Unblocks every
harvesting design in the game**, and the nugget's root `MaterialSourceVisualizer` makes both
gates **provably** pass.
**M2** "+1 Iron" — push a replacement 1081; SFX free; optionally teleport the nugget.
**M3** the real deposit — **all the depletion feedback comes free**, because none of it
depends on a client gate. Only the input signal is at risk; if the variant lacks a
`Salvageable`, a one-line Harmony postfix on the static `IsSalvageable` fixes it.

**Do not start with scrap or atlas** — both hard-require a live initialised deposit, and
scrap pool-spawns its model, so a scrap without a core is invisible.

## COULD NOT DETERMINE
**Whether the runtime-imported `MetalDepositVisuals` variant carries a `Salvageable`** —
decides whether the client will ever report a hit on a deposit. Cheapest resolution is
empirical: finish P3, spawn one, aim, watch for a 2106 update.
The damage→yield formula (invent it). The live-vs-replay transform mismatch. Nothing executed.
