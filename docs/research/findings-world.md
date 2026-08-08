# FINDINGS — MULTI-ISLAND WORLD

(Recorded by the orchestrator; the agent was blocked from writing this file.)

## Q2 — PLACEMENT: no world layout ships. We must invent it, in Bossa's own format.
Ruled out with evidence: all **255 island bundles** contain exactly one asset
(`Assets/Resources/EntityPrefabs/<id>@Island_unityclient.prefab`) with a single root
Transform at `localPos=(0,0,0)` (verified via UnityPy on 4 bundles); `IslandMetaData`
holds only `islandAuthor` + `islandName`; `clientGameDB.bytes` (2064 B) decrypts to UI
string tables only (`acs/GameDBAccessor.cs`, `GameDBConfig.cs`); `IslandLightingData`
manifests hold Unity **lightmaps** (`mapN.png`), not world maps; `IslandOcclusionData`
holds `ocdata.unity`; `level0/1`, `sharedassets*`, `resources.assets` contain zero
island ids; no JSON/CSV/XML anywhere.

**The authoring format survives in the decompile:**
- `acs/WorldEditorCore.cs:28-39` — `MapFile { WorldInfo, Haven,
  List<IslandStoreData> Islands, List<ZoneStoreData> Biomes, List<WallStoreData> Walls }`
- `acs/Assets.Scripts.UI.WorldEditor/IslandStoreData.cs:5-21` — `{float x,y,z; string Island;}`
- `WorldEditorCore.cs:74,280` — `WorldSize = 12000f`, bounds-checked on **x/z** → ±12 km
  square; `:63-66` altitude band **−600..+600**; `WorldEditorIslandsManager.cs:64,115`
  grid 1000f, `WorldUnitScale = 1f`
- `WorldEditorCore.cs:148-149` — the data file was
  `../../gsim/src/main/resources/islands.json` (the studio's server tree). Metadata came
  from a now-dead Bossa Apps Script (`WorldEditorHttpSpreadsheetIslandRetriever.cs:13`).

**Recommendation:** hand-author `islands.json` in the exact `MapFile` shape.
*Unverified: nobody searched community archives for a preserved real `islands.json` —
worth one search before hand-authoring.*

## Coordinate encoding (prerequisite, verified directly)
- Fixed point is **Q52.12, factor 4096** —
  `acs/Improbable.Corelibrary.Math/FixedPointVector3Util.cs:9-11,32-39`. So the server's
  current seed `FixedPointVector3{0,100,0}` (`ComponentsSerializer.cs:59`) decodes to
  **(0, 0.024, 0) m** — the origin, NOT a sky position.
- Global→Unity is a **pure translation**, no axis swap or scale:
  `unityPos = globalPos − OffsetOrigin` (`AbstractDetermineOriginStrategy.cs:51-59`).
- **The client re-centres Unity's origin on the nearest checked-out island every 5 s**,
  50 m hysteresis, then reloads lighting/occlusion keyed on the numeric island id —
  `ActiveIslandBasedRemapping.cs:14,20,56-57,68-73,115,127-131`. Float precision over a
  24 km world is already solved by the shipped client.

## Q1 — ANATOMY: the current seed is correct; the component set is client-driven
`AddEntityOp` carries **no components** (`SendOPHelper.cs:13-36`); the client then asks
via `SEND_COMPONENT_INTEREST` and the server echoes what was requested
(`WorldsAdriftRebornGameServer.cs:620-687`). Hard requirement for the island to enable:
`[Require]` `IslandStateReader` (1041), `IslandFabricStateReader` (1042),
`TransformStateReader` (190602) — `IslandVisualiser.cs:17-24`. **Exactly matches the
existing seed.** 190601 (empty children) is what lets
`TransformParentHierarchyBehaviour` enable at all.

Position comes from **190602, not IslandState**: `IslandLocalTransformBase.cs:44` sets
`transform.position = transformState.LocalPosition.RemapGlobalToUnityVector()`.
`IslandStateData`'s fields are prefabname / **teleportTarget** / verticalSpeed / bounds /
creator (`gencode/.../IslandStateData.cs:33-201`) — the `Coordinates(0,0,0)` at
`ComponentsSerializer.cs:433` is a teleport target, not a world position. Islands get
their static transform stripped and replaced at spawn
(`DispatchEventHandler.cs:183-203`).

**Gaps:** 1010/1011 (resource spawners) have **no serializer handler**, so they are
silently dropped (`ComponentsSerializer.cs:524-527`) — no resource respawning today.
1254 (lightning) has a handler but must be requested.

## Q3 — STREAMING: server-driven, and asset loading is the hazard
There is **no** checkout radius, interest query or view distance anywhere client-side —
the Reborn server has total control and total responsibility. Bundle loading is
**synchronous and unthrottled** (`LocalAssetBundleLoader.cs:19-44` →
`AssetBundle.LoadFromFile`); entity *creation* is budgeted (100 ms,
`EntityEventHandler.cs:119-151`) but *loading* is not. Sizes: 255 bundles, **2.04 GiB**
total, median 5.8 MiB, max 45.2 MiB, current island 28.2 MiB. Full-255 rejected.
Teardown is orderly under default (non-pooled) config. `IslandLighting` is a singleton —
one island gets full fidelity, others are imposters — but this state is **per-client**,
so players near different islands don't conflict.

## Q4 — PARENTING: the shortcut survives this slice; here is exactly when it stops
Verified: `LocalTransformUpdaterBehaviour.GetLatestValue()` (`:171-202`) branches on
`HasParent()` and **structurally never calls `.Parent(...)`** — rule 10 confirmed. The
only client-side Parent writer is `LocalTransformTeleportBehaviour.cs:171-172`, driven by
a server `TeleportRequestState` the Reborn server never sends. So `LocalPosition` is
global today. The hierarchy is **bidirectional**: child `Parent{parentId,key}` + parent
`TransformHierarchyState.children` — the island's children list is seeded **empty**
(`ComponentsSerializer.cs:73`).

**Multiple islands do NOT break ignoring Parent**, because global coordinates are
absolute and island-agnostic, and every client renders islands from the same
server-published positions. The shortcut fails only when: (a) remote rigs get far away —
the floating origin recentres on the **local player's authoritative bounds only**
(`EntityBoundsReactiveDetermineOriginStrategy.cs:32-35`), so a distant remote rig
jitters; (b) islands ever move (vertical drift / end-of-world), since unparented players
won't ride them. Neither applies to a static 3-island slice.

**The real blocker is not parenting** — it is that
`ComponentsSerializer.InitAndSerialize` **switches on componentId alone**
(`:43-70`, `:459-465`), so all islands would be seeded at the same point, stacked. It
already receives `entityId` (and 1088 already uses it via `Appearances.Get`), so the seam
is clean.

## Q5 — VERTICAL SLICE: three islands, exact server changes
Island A at the origin so the existing player seed still lands on it:

| Island | asset | world (x,y,z) | 190602 fixed-point (×4096) |
|---|---|---|---|
| A spawn | `949069116@Island` | (0, 0, 0) | {0,0,0} |
| B | pick ~5 MiB bundle | (900, −120, 0) | {3686400, −491520, 0} |
| C | pick ~5 MiB bundle | (0, −260, 900) | {0, −1064960, 3686400} |

All server-side; **no client-mod change required**:
1. **`IslandLayout`** — pure policy class loading `islands.json` in `MapFile` shape;
   maps `entityId → (assetName, Vector3d)`. Unit-testable, no Wine.
2. **Reserve island entity ids deterministically at startup**, before any player ids —
   replace lazy `SharedIslandEntityId` (`WorldsAdriftRebornGameServer.cs:409-426`). Ids
   must be identical across clients (rule 4, now per-island).
3. **Loop the SyncStep chain** (`:476-546`) per island: AssetLoadRequest → ack →
   AddEntity → ack. The existing ack-gated chain **already staggers** loads one at a
   time — this is exactly the throttle Q3 says is missing.
4. **Make `InitAndSerialize` entity-aware for 190602** (and 190604): look up
   `IslandLayout.PositionOf(entityId)`, encode ×4096. Keep the player default for
   non-island ids.
5. **Seed player spawn** above island A's surface rather than (0, 0.024, 0).

**Risks:** real island radii unverified (seeded bounds `(100,100,100)` is a placeholder)
so 900 m spacing may need tuning; two extra ~5 MiB bundles add load time; 190604's role
for islands unconfirmed. **Do NOT introduce Parent in this slice** — it would re-arm the
"~90 km drop" failure that `PlayerVisualizer_Patch` exists to suppress.
