# FINDINGS — THE HARVESTABLE WORLD (trees)

## VERDICT: a harvestable tree is a SERVER ENTITY. It is never bundle scenery, on any island.

**Every harvestable tree in Worlds Adrift was a SpatialOS entity spawned by the
GSim. Not one of the 465,571 props baked into the 255 island bundles is a tree.**

Four independent proofs, three of them in code:

1. **The prop channel cannot produce an entity.** `PopulateStaticPrefabs.LoadAsset`
   instantiates the prefab and calls `InitObjectFromData`
   (`acs/PopulateStaticPrefabs.cs:389,394`), which sets `position/eulerAngles/
   localScale` and nothing else (`:417-441`). No `EntityObjectStorage` is attached.
2. **A non-entity cannot be aimed at.** `PlayerLookingAt.GetInteractiveObject`
   (`acs/Assets.Scripts.Player/PlayerLookingAt.cs:159-172`) climbs the parents and
   returns a GameObject **only** if it has an `InteractiveObjectVisualizer` or
   `EntityFinder.IsSpatialOsEntity(gameObject)` — otherwise null.
   `SalvagerAimerObserver.Update:82-88` feeds that into `IsValidHit`, and
   `IsSalvageable(null)` returns false at `:32-35`.
3. **A cut cannot be attributed.** `TreeCuttingBehaviour.cs:35,65` publish
   `section.tree.gameObject.EntityId()`. `GameObjectExtensions.EntityId()` is
   `GetSpatialOsEntity()?.EntityId ?? default(EntityId)`, and `GetSpatialOsEntity`
   needs an `EntityObjectStorage` in the parent chain. Scenery therefore publishes
   **`EntityId(0)`** — and `InvalidEntityId` is **-1**, so 0 is a plausible-looking
   id the server can never resolve. Silent, not loud.
4. **The geometry is never initialised.** `TreeVisualizer.OnEnable`
   (`acs/TreeVisualizer.cs:14-21`) is what calls `SetTreeDefaults()` and
   `InitializeTree(...)`, and it carries `[Require] TreeFSimStateReader`. No 1036,
   no `InitTreeSections`, so `sectionMask` and `sectionsActive` stay 0.

**Cost consequence: there is no "make the existing scenery interactive" shortcut,
because there is no existing scenery to make interactive.**

## ⭐ THE WORLD-WIDE CENSUS

`data/tree_prop_census.json`, reproducible via `data/tree_prop_census.py` (~2 min
under `MemoryMax=4G`). All 255 bundles, `static_objects` + `small_static_objects`
TextAssets, GUIDs resolved through `world-data/haven/guidlut.json` +
`oldassetlut.json`:

```
islands swept          255
total placements       465571
unresolved GUIDs       0          <- every single placement resolved
TREE-PROP placements   0
islands with >=1 tree  0
```

| category | placements | library prefabs | placed |
|---|---:|---:|---:|
| Ruins (Saborian) | 118,141 | 309 | 309 |
| Ruins (Kioki) | 97,189 | 457 | 436 |
| Foliage | 94,439 | 74 | 74 |
| Ruins (Miscellaneous) | 50,689 | 151 | 137 |
| grass | 39,408 | 26 | 26 |
| Rocks | 34,345 | 97 | 97 |
| Gameplay | 18,337 | 145 | 86 |
| Effects | 13,023 | 23 | 19 |
| **Trees** | **0** | **65** | **0** |

The zero is not an accident of the library — it holds **65** tree prefabs under
`IslandProps/Trees/` and designers placed **none**. 163 of 1,347 library entries
are never placed anywhere, and that unplaced set is *exactly the gameplay-entity
set*: all 65 trees, 36 loot ruin piles, 8 turrets, 6 loot containers, 3 databanks,
2 loot chests. Meanwhile 443 **Murals** — pure decoration in the same
`Island Rewards/` folder — *are* placed.

**The prop channel is decoration-only. Every gameplay class was server-spawned.**

### Why the library ships trees it never places: they are editor markers
Each `IslandProps/Trees/*` prefab carries an `ImprobableEntityInfo`
(`acs/ImprobableEntityInfo.cs:21`) whose `GetData()` returns
`{name = idName, position, rotation, localScale}`. Read from raw bytes:
`green tree -> idName "Tree"`, `palm tree 1 -> "TreePalm1"`,
`straight blue tree -> "TreeStraightBlue"`. A designer dropped a *picture of a
tree* in the world editor; export harvested `(idName, transform)` into a
server-side entity list and dropped the marker from the shipped prop JSON. 55
other `islandprops/*` markers work the same way (databanks -> `DataBank_001`,
loot piles -> `LootRuinPile1..24`).

Same shape as `acs/HarvestableEntity.cs` — a marker carrying
`{entityPrefabName, harvestableMaterialName, harvestableAmount}` whose entire
runtime behaviour is `Start() { Destroy(gameObject); }`, with zero references
anywhere in the client.

**The authored positions did not survive.**

## HAVEN HAS NO TREES. NOT ONE.

Haven (`1431299145`) places 1,285 props across 86 distinct prefabs:

| segment | placements | distinct |
|---|---:|---:|
| `IslandProps/Rocks/` | 436 | 20 |
| `IslandProps/Foliage/` | **342** | **14** |
| `IslandProps/Ruins (Miscellaneous)/` | 229 | 24 |
| `IslandProps/grass/` | **170** | **10** |
| `IslandProps/Effects/` | 84 | 5 |
| `IslandProps/Ruins (Saborian)/` | 24 | 13 |
| **total** | **1285** | **86** |

The 342 Foliage placements are 14 prefabs, every one a bush, mushroom, vine, wall
leaf, fern or desert plant — 134 Big Bush 05, 94 Thorn Wart, 23 Small Mushroom,
and so on. The whole 74-prefab Foliage *library* is likewise bushes, flowers,
vines, cacti, roots, mushrooms and wall-leaves; **there is no tree in it**.
Case-insensitive `tree` over all 1,285 Haven asset paths: **0 hits**.

Haven's foliage props are not even the same kind of object: 3-9 node prefabs of
`LODGroup` + `MeshFilter`/`MeshRenderer` (+ sometimes a collider). The union of
every MonoBehaviour across all 86 is a single `GIOnlyLight` on `Small Mushroom`.
No `TreeBase`, no `TreeSection`.

> **TRAP for whoever greps next.** Several foliage/grass LOD children carry a
> component literally named **`Tree`** — `Weeds Big 06_LOD0`,
> `wall leaves 5 back_LOD0`, `vine few leaves2_LOD0`. That is **Unity's built-in
> `Tree` class (classID 193)**, the SpeedTree wind instance, *not*
> `TreeBase`/`TreeSection`. `acs/Tree.cs` is a 5-line
> `MonoBehaviour { public Transform[] bones; }`, unrelated to harvesting.

### What this means for the 2017 footage
The player standing among trees **was not on Haven's terrain as it ships to us**.
Either a normal island, or Haven's trees were GSim-spawned entities like
everything else that made Haven Haven. Every tree we want, we place — which is
liberating: there is no authored layout to be unfaithful to, because none exists
on the client.

## WHAT MAKES A TREE CHOPPABLE

`entityprefabs/tree_unityclient` lives in
`UnityClient@Windows_Data/resources.assets` — **not** in any island bundle; all
255 declare `Dependencies: []`. Root `Tree_unityclient`, 148 nodes, 40
MonoBehaviours, with 12 `trunk_sectionN` children each carrying `TreeSection`.
The dump reproduces `TreePreprocessor.cs:59-100`'s UnityClient branch
component-for-component, which is the validation.

### The `[Require]` closure the client will demand

| id | component | required by | we have a branch? |
|---|---|---|---|
| 190601 | `TransformHierarchyState` | `TransformParentHierarchyBehaviour` | yes |
| 190602 | `TransformState` | `FixedUpdateLerpLocalTransformBehaviour` | yes |
| **1035** | `TreeState` | `TreeClientVisualizer`, `TreeScaleVisualiser` | no |
| **1036** | `TreeFSimState` | `TreeClientVisualizer`, `TreeVisualizer` | no |
| **1016** | `ItemHealthState` | `SalvageableItemVisualiser` | no |
| **1099** | `SalvageAndRepairState` | `Salvageable` base | no |
| **1183** | `ReconsumablesState` | `ReconsumablesClient` | no |
| **1232** | `RigidbodyCollisionReporterState` | `RigidbodyCollisionVisualizer` | no |
| **4333** | `DeteriorateState` | `DeteriorateVisualizerClient` | no |
| **4400** | `TrackedEntityLoadState` | `TrackedEntityLoadClientVisualizer` | no |

**Eight new serializer branches, all-or-nothing.** `SendOPHelper.cs:85-94`: a
component whose `InitAndSerialize` yields `len <= 0` logs
`[error] failed to initialize component NNNN`, and because the non-player interest
path passes `failOnComponentInitError: true`
(`WorldsAdriftRebornGameServer.cs:753`) the **whole batch** is dropped.
**Symptom: a fully-rendered, completely inert tree.**

### Which visualiser does what
- **`TreeVisualizer`** (no `[WorkerType]`, so it runs on the client) is the
  geometry: `OnEnable` -> `SetTreeDefaults()` -> `InitializeTree(...)` ->
  `InitTreeSections` activates/deactivates each section by mask bit.
- **`TreeClientVisualizer`** (`[WorkerType(UnityClient)]`) is feedback only — on a
  mask change it fires `ShowReplicatedVisualHitAndPlaySfx()` on the section that
  just left the mask.
- **`TreeFsimVisualizer` is `[WorkerType(UnityWorker)]` and is absent from the
  client build.** `TreeSection.Harvest()` is only ever called from
  `TreeStateOnTreeSectionIsCut`, i.e. only on the FSim. **We have no FSim, so the
  mask arithmetic must be reimplemented server-side**; firing a `TreeSectionIsCut`
  event on 1035 at a client does nothing.
- **There is no "press E" prompt.** Chopping is not an interaction verb — the tree
  has no `InteractiveObjectVisualizer`. `HighlightableObject.Create(go, Tree)`
  registers it for the *scanner-goggle highlight*, not interaction. The affordance
  is: point the multitool in Salvage mode and hold.

### Good news buried in the target test
`SalvagerAimerObserver.IsSalvageable` uses `GetComponent<SalvageableItemVisualiser>()`,
which finds the component **even while disabled by an unresolved `[Require]`**,
and `IsSalvageable()` short-circuits on a null state. **The beam accepts the tree
even if 1099 and 1016 are junk seeds.** They must still be *sent*; their contents
do not matter.

### Two traps in the seeds
- **`1035.scale` MUST be `Vector3d(1,1,1)`.** `TreeScaleVisualiser.cs:15-18`
  writes `transform.localScale` verbatim and `Vector3d`'s default is `(0,0,0)`. A
  scale-0 tree is invisible, has working colliders, and logs nothing.
- Keep **`1036.dynamic = false`**; its setter starts the falling-audio loop.

### The cut signal is a LATCH, not a pulse
`TreeCuttingBehaviour.Update` publishes `{treeEntityId, sectionId, aboveOrBelow}`
every frame, but `FinishAndSend` runs `FinishAndSend_ResolveDiff` and suppresses a
send when nothing changed. **One 1037 packet when the beam moves onto a section,
one when it leaves. No per-hit pulse.** Cadence must come from a server timer
(cheap) or a 2105 shot event (faithful).

## THE YIELD IS 100% SERVER AUTHORITY

- `TreeFSimState.resource_per_section` — **zero readers in `acs/`**.
- `TreeState.respawn_time` — **zero references in `acs/` at all**.
- `TreeFSimState.wood_type` — written only by `TreeFsimVisualizer`, which is
  UnityWorker-only. **The client never learns a tree's species.**
- `RawMaterialSourceState` (1030) and `HarvesterState` (1031) have **zero
  references in `acs/`**. Whatever hold-to-gather system they served is not in
  this build.
- The grant is a **full-replacement 1081 push**; the "+N Oak Wood" feedback comes
  free from the client diffing successive 1081 lists.

### ⭐ RECOVERED: Bossa's authored wood species for all 65 tree prefabs
`TreePreprocessor.woodType` (default `"elm"`) is a per-species authoring field
copied onto `TreeFsimVisualizer.woodType` at export, so it **survives on the
shipped `_unityworker` prefabs**. Parsed from raw MonoBehaviour bytes for all 65:
**65/65 hits, every value one of the eight known woods** — which is the
validation, since the wood list was derived independently in
`gathering/data/materials.tsv`.

```
palm 19 · ash 14 · chestnut 9 · cedar 8 · hemlock 4 · elm 4 · oak 4 · birch 3
Tree -> birch      TreeOrange -> birch      TreeStraightDarkGreen -> birch
all TreePalm* -> palm       TreeDessert2/3, TreeDessertLeaf1/2 -> hemlock
TreeWonky?Leaf3 -> elm   ?Leaf4/5 -> chestnut   ?Leaf6 -> oak
TreeWonky?Leaf1/2/7 -> ash   ?LongLeaf2/7 -> cedar
TreeStraightBlue/Pink -> ash   TreeStraightRed -> chestnut
```

Full table: `data/tree_woodtypes.json`. **`Tree` yields birch.** Recovered Bossa
content, not invention. **Still invented: the amount per section, and quality.**

## SMALLEST CHANGE FOR ONE CHOPPABLE TREE ON HAVEN

Ship **`Tree`** — 12 sections, 4 branches, `sectionMask = 4095`, section 0
non-harvestable. **Not a palm**: `TreePalmStubby` has harvestable sections with no
`cutPoint`, which NREs in `ShowReplicatedVisualHitAndPlaySfx`.

**0. Half a day, no code — log the real interest list.** Add a `Console.WriteLine`
of `interests[i].ComponentId` in the `else` at `SendOPHelper.cs:85-94`, spawn one
tree by hand, read the log. The ten-id table is the *static* `[Require]` closure;
`ExtractVisualizers` walks the whole hierarchy. **Gates everything, costs nothing.**

**1. Eight serializer branches** — 1035, 1036, 1016, 1099, 1183, 1232, 4333, 4400.
Seven may be structurally-valid stubs. Only two carry meaning:
```
1035 TreeState      scale = Vector3d(1,1,1)   <- NOT the default; (0,0,0) = invisible
1036 TreeFSimState  sectionMask = 4095, sectionCount = 12, dynamic = FALSE,
                    massPerSection = 1f, sectionHealth = [3]*12, woodType = "birch"
```
`InitAndSerialize` already receives `entityId` and `SpawnPolicy.TransformSeedFor`
already branches on it, so the tree's 190602 slots into the existing mechanism —
add the tree branch before the generic one, at a Haven island-local coordinate a
few metres in front of the player spawn, on a measured LOD0 vertex.

**2. Two branches + two grants on the player** — `1231 SalvagerAimerState`
(`maxBoltDistance` non-zero, or `HitInfo` is null forever and the diff-resolver
suppresses every 1037 after the first, which looks exactly like "the grant didn't
work") and `1037 TreeCutterState`. Add both to
`MirrorSendPolicy.AuthoritativeComponents` and update its tests. **Filter both out
of the relay.**

**3. Three more grants for the beam** — 2105, 2106, 2002, all writers on
`PlayerMultitoolVisualizer`. Order the seeds so **1086 lands no later than these
in the same batch**.

**4. A pure `TreeTopology` module** — port `TreeBase.WalkTree` and `WalkFrom`
verbatim, plus the mask split from `TreeSection.Harvest`:
`falling = bits(WalkTree(section))`, `remaining = sectionMask & ~falling`, and
**refuse when `sectionsActive <= 1`** — the shipped game never clears the last
section. The damage model is **one hit per section**: `TreeSection.cs:73-74` is
literally `connectionStrength = 0; connectionStrength--;`. Nothing reads
`sectionHealth`, so multi-hit health would be invisible.

**5. Spawn.** `AssetLoadRequest("Tree", "Default")` -> ack -> `AddEntity`, **same
entity id on every peer**. **`"Tree"` is the correct name**: the client appends the
worker suffix itself via `WorkerSpecificPrefabName.GetWorkerSpecificPrefabName`.

**6. One 1037 handler.** On a latch naming a valid `(treeEntityId, sectionId)`,
run a ~0.75 s server timer; each tick apply the split and **push the new 1036
`SetSectionMask` to every peer directly**. Do **not** route through
`RelayToOtherPlayers`, which substitutes the sender's own entity id. Send **only**
`SetSectionMask`, never `Data.ToUpdate()`.

**7. Grant wood** via the existing 1081 path — `itemTypeId = "birch"`.

**What you will NOT get:** severed sections **vanish instead of falling**. The
original split the mask *and* spawned a new dynamic entity via
`TreeFsimVisualizer.SpawnNewTree`, needing dynamic entity creation and physics
authority we lack. Expect "it doesn't fall over" as the first complaint.

## CORRECTIONS TO EXISTING DOCUMENTS

**`gathering/findings-node-spawning.md` is WRONG on the prefab name.** It claims
there is no `Tree` prefab, that all 72 tree entries are species-specific, and that
`"Tree"` fails the asset load. `entityprefabs/tree_unityclient` **is line 289 of
the very file it cites**. The reasoning is also inverted: container keys are
worker-suffixed because the *client* appends the suffix; the server sends the
**bare** name. `gathering/findings-tree-harvest.md` was right and this document
overturned it wrongly.

**`findings-haven.md`'s foliage count is wrong.** Stated `~430 rocks · ~370
foliage · ~200 metal ruins · ~25 Saborian · ~85 VFX`. Measured: 436 / **342** /
229 / 24 / 84. The defensible foliage numbers are 342 (strict) or 512 (Foliage +
grass); 370 is neither. Those categories sum to ~1,110 against a stated 1,285 —
**the 170 `grass` placements were omitted entirely**. Its characterisation stands
and strengthens: every placement is generic scenery, **and contains no tree of any
kind**.

**`gathering/findings-interaction.md` — confirmed and generalised.** It flagged
that only 1 of 255 bundles was scanned. The full census closes that: every loot
pile, chest, container, databank and turret prefab is placed **zero** times
world-wide. No longer extrapolation.

**`gathering/findings-tree-harvest.md` — independently reproduced, no
corrections.** What it could not know is the subject of this document: there was
never a bundle-scenery shortcut to weigh against the entity route.

## NOT VERIFIED

**Inferred:**
- That `IslandProps/Trees/*` markers were consumed by a GSim-side island importer.
  The `idName -> entity prefab` mapping plus the 0/255 sweep make it the only
  consistent reading, but **the importer is not in this decompile** and the call
  site of `GetData()` was never found.
- That all ten `[Require]` ids will be requested. `ExtractVisualizers` walks the
  whole hierarchy and child scripts were read only for `[Require]` attributes.
  **This is exactly what step 0 exists to check.**
- That the eight stub seeds are constructible — schema field layouts were read,
  generated `.Data` constructor arities were not compiled against.

**Assumed on someone else's authority:** the Haven spawn coordinate and corrected
LOD0 surface, taken wholesale from `findings-haven.md` / `findings-spawn.md`.

**Could not determine:** the original tree positions on any island, Haven
included (GSim data; the 0/255 sweep is strong negative evidence but cannot prove
a universal negative over data not on this machine) · the yield **amount** per
section and its quality — `resourcePerSection` has no reader anywhere, **you are
inventing these** · the units of `respawnTime` · whether Haven ever had trees.

**Nothing was executed. No game was launched.** Static analysis plus asset
extraction.

## DATA COMMITTED (`data/`)
- **`tree_prop_census.json`** — all 255 bundles: per-island placement counts,
  category breakdown, tree counts, full world-wide asset histogram (1,184 distinct
  assets, 465,571 placements, 0 unresolved GUIDs).
- `tree_prop_census.py` — reproduces it in ~2 min under `MemoryMax=4G`.
- **`tree_woodtypes.json`** — Bossa's authored `woodType` for all 65 tree species,
  recovered from the `_unityworker` prefabs. 65/65 clean.
- `tree_woodtypes.py` — reproduces it.
