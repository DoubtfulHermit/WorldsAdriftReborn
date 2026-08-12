# VERIFY — do ship prefabs survive into the client bundle? (round 2, empirical)

**VERDICT: PATCH A MUST CHANGE.** `ShipPreprocessor` is **stripped from every shipped
ship prefab**. A Harmony postfix on `ShipPreprocessor.ExportProcess` will never fire,
because no instance of that MonoBehaviour exists in the shipped assets.

This was measured, not reasoned: all **49,734 MonoBehaviours** across
`resources.assets` + `globalgamemanagers.assets` + `sharedassets0.assets` were scanned
with every `m_Script` PPtr resolved to its `MonoScript.m_ClassName`.

## Where the ship prefabs actually are
**Not** in `~/Games/WorldsAdrift/Assets/unity/` — all 255 bundles there are islands
(zero `shipframe` hits in any manifest). They are in
`UnityClient@Windows_Data/resources.assets` (610 MB, 300,044 objects), **pre-split per
worker at build time**:

| path_id | name |
|---|---|
| 21360 / 21361 | `ShipFrame01_unityclient` / `_unityworker` |
| 29028 / 29029 | `ShipFrame02_unityclient` / `_unityworker` |
| 34156 / 34157 | `ShipFrame_unityclient` / `_unityworker` |
| 21362 / 21363 | `ShipFrameGhost_unityclient` / `_unityworker` |

**`ShipFrame` (unnumbered) is the real player-ship entity prefab** — it alone carries
`Ship`, `ShipVisualizer` and `MeshGenerator` (required by
`CustomShipFrameVisualizer.OnHullDataUpdated`), with empty `Generated`/`Hulls` children
for runtime mesh generation. `ShipFrame01`/`02` are **baked static frames** (216
GameObjects, 183 BoxColliders, 6 MeshRenderers, no MeshGenerator, no Ship). Nothing in
the decompile references the strings `"ShipFrame01"`/`"ShipFrame02"` — they arrive as
server-supplied prefab names.

## The census
```
ShipPreprocessor              0 instances
PathFollower                  0 instances
SSPDeadReckoningBehaviour     3   worker only
ShipPhysicalityVisualizer     3   worker only
SelfRighteningVisualizer      3   worker only
ShipControlVisualizer         3   worker only
ShipAbandonedBehaviour        3   worker only
SSPDeadReckoningVisualizer    6   BOTH client and worker   <- receive half present
Ship / ShipVisualizer         2   ShipFrame only
```
Byte-grep corroborates: the literal `ShipPreprocessor` occurs **0 times** in
`resources.assets`.

**Why:** `PrefabCompiler.ShouldRemoveFromPrefab` (`:74-79`) `DestroyImmediate`s any
`IPrefabExportProcessor` lacking `[KeepOnExportedPrefab]`. `ShipPreprocessor` has none.
`TransformNature` **does** (`TransformNature.cs:13`) and IS present on all four client
prefabs — the control proving the strip is selective, not incidental.

Confirming the processor already ran: the shipped `ShipFrame01_unityclient` root
component list matches `ShipPreprocessor.ExportProcess(UnityClient)` **exactly, in
source order** (28 components).

## Second, independent reason Patch A cannot work
`WorkerSpecificTemplateProvider.InitializeTemplateProvider` (`:13-20`) returns the
provider **without** the `PreprocessingGameObjectLoader` wrapper — the only thing that
calls `PrefabCompiler.Compile`. The repo already compensates in
`WorkerSpecificAssetDatabaseTemplateProvider_Patch.cs`.

## Colliders and rigidbodies — real, and kinematic
- **`ShipFrame`:** one root Rigidbody, mass 1.0, **isKinematic=True**, no gravity.
  `Ship.Rigidbody` byte-verified to reference exactly that root body. Colliders are
  generated at runtime — `MeshGenerator.cs:400-401` `AddComponent<MeshCollider>()`,
  `convex = true`.
- **`ShipFrame01`:** **183 real BoxColliders** (not triggers, enabled), one Rigidbody on
  the `Hulls` child, kinematic, gravity on. Client variant has 6 MeshFilter+Renderer;
  **the worker variant has ZERO renderers** — empirical proof that `PrefabCompiler`
  strips client visuals. **Do not just load `_unityworker`; the ship would be invisible.**

## BONUS FINDING THAT CHANGES THE PLAN
Byte-read of `FixedUpdateTransformNature` on `ShipFrame_unityclient`:
```
ClientNonAuthoritativeMode = Custom  ClientCanBeAuthoritative = False
FSimNonAuthoritativeMode   = Custom  FSimCanBeAuthoritative   = True
```
So `TransformNature.GetClientVisualizersToAdd()` added **nothing** to the ship's client
prefab — no `LocalTransformUpdaterBehaviour`, no teleport behaviour, no transform
behaviour at all. **The client ship prefab has no transform-writing path of any kind.**
Those must be added too.

## WHAT PATCH A MUST BECOME
A **postfix** on `WorkerSpecificAssetDatabaseTemplateProvider.GetEntityTemplate` (after
the repo's existing prefix runs `PrefabCompiler.Compile`), scoped to ship prefab names,
idempotent via a `GetComponent` guard (the prefab is cached and shared).

**Tier 1 — the `ShipPreprocessor` UnityWorker list, in source order** (`:50-73,108-111`):
`ShipPhysicalityVisualizer`; `SelfRighteningVisualizer` + set `.Amount` (default `2f` —
the serialized inspector value died with the preprocessor); `WindPhysicsVisualizer`;
`WallTorquePhysicsVisualizer`; `ShipControlVisualizer.Create(go, 2f, 1f)`;
`ShipMotionVisualizer`; `LightningAttractorVisualizer`; `ShipBoundsVisualizer`;
`ShipDeckSpawningVisualizer`; `RigidbodyCollisionBehaviour` +
`AddCollisionDispatchers(go)`; `RelativeParentBehaviour{IsTransformRoot=true}` then
`.TagColliders()` (**on ShipFrame01 this fans out to 184 marker+dispatcher triples — call
it, do not hand-roll**); `DeteriorateVisualiser` + its dispatchers;
`FSimDummyShipReadersVisualizer`; `WorldEdgePushback`; `ShipPositionDebugger`;
`ShipAbandonedBehaviour`; and finally **`SSPDeadReckoningBehaviour`** — the publisher,
the whole point.

**Tier 2 — the `FixedUpdateTransformNature` worker list, also absent client-side:**
`RelativeParentTransformUpdater`, `RelativeParentOverrideToGlobalBehaviour`,
`LocalTransformTeleportBehaviour`, `ParentEntityBehaviour`, plus a second
`RelativeParentBehaviour`+`RelativeParentMarker` pair.

**Traps:** `ShipPhysicalityVisualizer.Awake` does
`GetComponent<ShipVisualizer>().Ship.Rigidbody` — safe on `ShipFrame`, but it
**NullReferences on `ShipFrame01`/`02`**, which have neither. Scope the patch to
`ShipFrame`. Set `.enabled = false` on everything you add, mirroring the shipped prefab
(every `*Visualizer` ships at `m_Enabled=0`); `EntityVisualizers` enables them on
authority.

## TWO CORRECTIONS TO THE SHIP PLAN
1. **Patch B is probably unnecessary.** `BehaviourWorkerCompatibilityCache` has exactly
   one consumer — `PrefabCompiler.DisableWrongPlatformMonoBehaviours` (`:48`). If we add
   components *after* `Compile`, nothing destroys them.
2. **Stage A gets CHEAPER.** `ShipFrame01_unityclient` ships with baked geometry and 183
   real box colliders and needs **no `hullData` at all** (no `MeshGenerator`, so
   `OnHullDataUpdated` no-ops). Spawning it gives a visible, stand-on-able static ship
   immediately. Only `ShipFrame` needs the synthesised `CustomShipHullState` blob.
