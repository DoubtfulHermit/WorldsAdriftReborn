# FINDINGS — RESOURCE NODE SPAWNING

## VERDICT: SERVER-AUTHORED POSITIONS. Drop the handshake.
Not because the handshake is broken — it would work — but because **the premise that made
it necessary is false.** Investigation A claimed *"positions exist NOWHERE locally"*. The
first half is wrong: **the island SURFACE GEOMETRY ships locally in all 255 bundles, baked
and complete, and it is the SAME array the client would sample.**

Extracted: **14,474,776 LOD0 vertices with normals, in 58.2 seconds of CPU, into 9.0 MiB.**
Committed to `docs/research/world-data/island-surfaces/`.

## THE CENSUS — Investigation B was right, and it is not close
All 255 bundles swept: `hasProxy TRUE: 0` · `hasProxy FALSE: 255` · `hasSurfaceData: 255/255`
· `islands with 0 lod0 cells: 0`.
The shipped island prefab is literally the **UnityClient** output of
`IslandPreprocessor.ExportProcess` — the entire UnityWorker branch is absent, and
`IslandPreprocessor` itself was stripped after running at Bossa's build time.

**Corollary neither investigation noted:** the mod's existing
`WorkerSpecificAssetDatabaseTemplateProvider_Patch` already calls `PrefabCompiler.Compile`
on every prefab — but for islands it is a **no-op**, because `InvokePrefabExportProcessors`
only calls processors *present on the prefab*, and `IslandPreprocessor` was stripped. The
"COMPILED PLAYER GAMEOBJECT!!!" log fires and nothing is added.

## `lod0Meshes` IS BAKED, and IS the collision surface
`GetLod0Meshes()` is `[ExposedMethod]` — an **editor button, never called at runtime**.
`Start()` only recomputes bounds, and `relativeBoundsHaveBeenCalculated` is already 1.
Verified four independent ways on the seed island:
- 497/497 cell transforms unrotated, unit scale — composition is **pure translation**
- mesh name `(i,j,k)_LOD0` predicts the transform exactly; **inferred cell size 64.00 m**
- **497 MeshColliders, 497 distinct meshes, 497/497 are lod0Meshes** ← the load-bearing one
- normals present and unit length

**So offline placement is placement on the exact geometry the runtime collides against.**

## `FindPlace` IS NOT A RAYCAST
```csharp
meshFilter = lod0Meshes[Random.Range(0, len-1)];
num2 = Random.Range(0, num-1);
worldPoint = meshFilter.transform.TransformPoint(sharedMesh.vertices[num2]);
valid = filter(normal) && !Physics.CheckSphere(worldPoint, 2f, layerMaskForCheck);
```
**"Pick a random LOD0 vertex, keep it if a normal filter passes and nothing is within 2 m."**
Three inputs, all available offline.

**And the normal filter for metal is a NO-OP** — `FindPlaceForMetalSpawn` opens with
`metalOnSurfaceProb = 1f;` then tests `Random.value < 1f`. So the shipped client places
metal on **any** face including undersides. The `dot(up,n) > 0.4` filter applies only to the
big-object fallback, **which is dead** (`bigObjectsAsset.Objects` is empty in the shipped
bundle and both call sites gate on `Count > 0`).

## MEASURED COST
| bundle | MiB | cells | vertices | sec |
|---|---:|---:|---:|---:|
| 1230084434 (largest) | 45.2 | 734 | 327,928 | 0.46 |
| 949069116 (seed) | 28.2 | 497 | 206,428 | 0.49 |
| 1434124226 (smallest) | 0.3 | 2 | 45 | 0.09 |

World totals: **14,474,776 vertices** · upward-facing (n.y>0.4) **4,912,066 (33.9%)** ·
thinned @8 m **208,502** · **58.2 s total CPU** · **9.0 MiB on disk**.
Cells/island: min 2, median 102, max 734.

The 2 m clearance test is reproducible too — props ship as TextAssets in the same bundle
(`static_objects` 223 + `small_static_objects` 733 = **956 props**), so it is a spatial-hash
query on 956 points. `layerMaskForCheck = 0xFFFFFEFF` excludes exactly layer 8 (terrain),
which is why a surface vertex doesn't fail against its own ground.

## END-TO-END PROOF (committed as `world-data/nodes-949069116.json`)
```
island 949069116 world origin = (14321.44, -527.0027, -4647.39648)
surface candidates 2897 -> after 2m prop clearance 2895 -> Poisson-disk @45m -> 440 sites
islandMeshCount=497, density=0.05 -> N=25 metal deposits
{"metalTypeId":"aluminium","quality":8,"islandLocal":[72.0,79.91,-56.0],
 "globalCoords":[14393.44,-447.093,-4703.396],
 "fixedPoint190602":[58955530,-1831292,-19265112]}
```
Island-local vertex → global metres → Q52.12 ×4096 → metal type and quality from the
community table. **Nothing missing.**

## COST/BENEFIT
| | server-authored | client handshake |
|---|---|---|
| one-time extraction | 58 s, 9 MiB in git | — |
| client mod change | **none** | new Harmony patch at a precise lifecycle point |
| new server mechanism | none | per-component authority on a **non-player** entity + claim/release registry + ForgetPeer hook |
| new update handler | none | 1011 handler + node-scoped relay |
| determinism | seedable, reviewable in git | `UnityEngine.Random` on whichever client won |
| failure mode | none | **designated spawner disconnects → world stops populating** |
| positions before first player | **yes** | no |
| respawn after depletion | pure server logic | round-trip through a client every time |

The handshake's only genuine advantage is placement from live physics against real collider
shapes. Marginal on a 2 m test — and you can be more conservative offline instead (I used a
45 m Poisson-disk separation, which the original never did).
**On the record:** the shipped client places metal on any face; my offline pass restricted
to `n.y > 0.4`. A deliberate policy choice, not a limitation.

## THE HANDSHAKE — specified as contingency, with a trap worth having anyway
**THE BOOTSTRAP TRAP nobody found.** `EntityVisualizers.CalculateRequiredComponents` only
adds a visualizer's required **readers** once **all its writers are already injected**.
`IslandProxyVisualizer` requires a *writer* for 1011, injected only by
`OnAuthorityChanged(hasAuthority:true)`, which needs the component to already exist.
**Therefore the client will NEVER ask for 1010 or 1011 via SEND_COMPONENT_INTEREST.** The
server must push both unprompted (the existing `injectedEarly` trick). **A plan that waits
for the client to request them deadlocks silently forever.**
Mandatory order: `SendAddComponentOp([1010,1011])` → `SendAuthorityChangeOp([1011])` →
writer injected → `OnEnable`.

Other confirmed traps: **`batchSize = 0` is a silent permanent no-op** (`Mathf.Min(0,n)==0`,
and the "failed to find a spawn point" warning is *inside* the loop so it never fires);
`spawnInterval = 0` runs every frame; **both are read ONLY in `OnEnable`**. Seed the game's
own field initialisers: **`batchSize = 30`, `spawnInterval = 10f`**.
`FabricTransform.position` is **`Coordinates` — doubles in metres, NOT fixed point.**
`AddComponent` must set `.enabled = false` immediately or `OnEnable` NREs on line one.
**Today the server has no authority ledger at all** — the one call site is gated on
`Players.Owns`, i.e. only a peer's own player entity. **This entire section is work you do
not have to do.**

## NODE TYPES — the headline correction
**Every node visualizer requires READERS ONLY. No node needs an authority grant.** The
`[Require]`-writer blocker applies to the *player's* tools, not to nodes.
**The "4-entity metal graph" is wrong** — root+core+crust are one entity (they
`GetComponent` each other); scrap and atlas shards are separate.

Ranked cheapest first:
1. **Databank** — 1 entity, 1 component (8073). No writers, no parent gate, no biome. **Ship first.**
2. **Metal boulder** — 1 entity, 1 component (12280 `int variant`). "The metal that works today."
3. **Nugget / scrap** — 1 entity, **0** required components to render.
4. Loot pile / chest — 1210 + 1081.
5. Atlas compass chest — 2 small readers.
6. Egg — 1235 + 1099.
7. **Tree** — 8 components for full fidelity, per-species `sectionCount`, and
   **`TreeState.scale` is applied verbatim so (1,1,1) or the tree is invisible.**
8. **Metal deposit** — most expensive by far. `IsInitialised` demands all five components or
   **zero art** (the prefab has no built-in mesh), plus `FindBiomeAsync` blocks forever
   without a GlobalEntity carrying 1253 + 8064.
9. Habitat — skip; needs a client writer on 4341 and the AI stack is UnityWorker-only.

**PREFAB NAMING CORRECTION: there is no `Tree` prefab.** 349 container keys extracted
(`world-data/prefab-keys.txt`): 72 tree entries, all species-specific (`treepalm1`,
`treewonky2leaf3`…). **Sending `"Tree"` fails the asset load.**

## PLACEMENT POLICY
Inferred (not recovered): `count = max(minMetalRockDeposits, metalDepositDensity ×
islandMeshCount)` — the client reports `islandMeshCount` upstream and has **no other use for
it**, so scaling spawn counts by it is the only reason to send it.
`spawnInterval = 10f` / `batchSize = 30` tell you the intended **cadence**: the original
trickled a large island in over a minute or two. **Keep that pacing — it doubles as the
asset-load throttle.**

First implementation for island 949069116 (tier 3): **25 metal** (`0.05 × 497`, ≥45 m apart)
· **5 databanks** (exact, from the community table) · **8 eggs** · **0 trees** (defer).
Corpus: metal correlates hard with tier (t1 mean 0.0 → t4 **2.4**); databanks are dense and
reliable (median 5, on all 254). **Most islands have no survey table and need the density
formula alone.**

## ORDERED PLAN
**0.** Extraction — done, committed.
**1.** **One databank, one island.** Serializer branch for 8073; make **190602 entity-aware
first** (it hardcodes the origin); new pure `NodeLayout` in `...Multiplayer`; **reserve node
ids GLOBALLY from `EntityIdAllocator`** or you get one node per player; append a SyncStep
(**note the last step never advances — insert before the terminal one**).
**2.** Metal boulders + nuggets, 25/island. **The first thing that looks like a populated world.**
**3.** Node-scoped relay — before harvesting, or depletion won't replicate.
**4.** Harvesting — unblock the multitool first (2105/2106/2002/1231 + non-zero
`maxBoltDistance`). Entity removal is **not** needed; depletion is state-based.
**5.** Loot containers, then trees. **6.** Metal deposits, only after a GlobalEntity exists.

## COULD NOT DETERMINE
**Metal deposit `variantId` strings** — `resources.assets` ships **no MonoBehaviour
typetrees** (all 20,255 readable MonoBehaviours expose only the 4 base fields; island
*bundles* do ship typetrees, `resources.assets` does not). Needs UnityPy `TypeTreeGenerator`
against `Assembly-CSharp.dll`. **Blocks metal deposits only.**
Whether those biome PropLibrary entries are non-empty at all. Original respawn cadence and
the density constant. `WALogger`'s exact line prefix. The authored MonoBehaviour list inside
node prefabs. Nothing was executed.
