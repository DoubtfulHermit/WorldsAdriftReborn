# Findings — the shipyard "bubble" (influence dome)

**Method:** ilspycmd over the publicized client assembly
`StrippedAndPublicizedAssemblies/UnityClient@Windows_Data/Managed/Assembly-CSharp.dll`
(assemblies are STRIPPED: signatures and field names survive, method bodies are `throw null`),
plus a UnityPy scan of the local game install for the serialized prefab values.

## The bubble is the shipyard *influence dome*

The client's own vocabulary uses both words:

- `Shipyard` (MonoBehaviour) carries `public float ImpactRadius`, `public bool Deployed`,
  `public Transform _influenceDome`, `public ShipyardDomeTrigger _influenceDomeComponent`,
  `public bool _isActive`, `public GameObject _influenceRadiusGo`,
  `public DockableVisualizer DockedShip`, and `public bool IsWithinRange(Vector3 position)`.
- It also holds three animator/event name constants: `SpawnShipyard`, **`SpawnBubble`**,
  **`DespawnBubble`** — the dome is literally the "bubble" the player sees.
- `ShipyardDomeTrigger` (`[ExecuteInEditMode]`) renders and sizes it. Radius-relevant fields:
  `public float targetDomeRadius`, `_radiusThresholdScalar`, `_domeColliderOffset`, and the
  runtime `_actualDomeRadius`. Membership state: `_insideDome`, `_lastResult`,
  `HashSet<Rigidbody> _objectsInsideDome`. Presentation: dome renderers, ripple pool, flash
  curve, particle tints, day/night interior scalars, `_showOnInit`.
- Related types: `ShipyardDomeCameraEffect` (screen effect on crossing),
  `ShipyardDomeGrapplePreventer` (grapples cannot pass), `DomeRipple`, `ShipyardActiveEffect`
  (sparks/flicker while active).
- Membership symbols elsewhere in the assembly: `isInsideShipyardDome`,
  `PlayerEnterShipyardDome`, `PlayerExitShipyardDome`, `IsPlayerInsideShipyard`,
  `insideActiveDomes`.

## What makes the bubble appear (server-controllable truth)

`ShipyardVisualizer : VisualiserBase` is the SpatialOS visualizer:

```
[Require] public ShipyardStateReader _state;      // component 1205 ShipyardState
public DockableVisualizer DockedShip { get; set; }
public bool IsLocalPlayerRegistered { get; }
public void OnDockedShipChanged(EntityId entityId)
public void OnActived(bool state)
```

So the dome is driven from **component 1205 `ShipyardState`** — the `DockedShipId` change
handler and an "actived" flag — via `Shipyard._isActive` / `Shipyard.DockedShip`. This matches
the already-known client rule that a yard counts as *active* only when it has a docked ship
(`PlayerScannerTool.IsShipyardActive()` → `Shipyard.DockedShip != null`).

**Consequence for us:** the server already owns the bubble. Publishing 1205 `DockedShipId` at
dock time is exactly what raises it; clearing it lowers it. No client patch is needed, and the
integrated transactional docking path already publishes 1114/1205 atomically at capture — what
remains is making the *trigger conditions* match the player-described behavior.

## Radius: what is recovered and what is lost

| Value | Status |
|---|---|
| `Shipyard.ImpactRadius` default **35 m** | RECOVERED (client default, already cited in `docs/research/authentic-docking-discovery.md:31`) |
| `ShipyardDomeTrigger.targetDomeRadius` | **LOST** — prefab-serialized, see below |
| `_radiusThresholdScalar`, `_domeColliderOffset` | **LOST** — prefab-serialized |
| capture radius / angular tolerance / snap epsilon | LOST (already classified as WAReborn tuning) |

The dome's own radius lives in the shipyard **prefab**, not in the assembly. It is not
recoverable from this install:

- `resources.assets`, `level0`, `level1`, `globalgamemanagers.assets` contain **zero**
  MonoBehaviours whose script is `Shipyard`, `ShipyardDomeTrigger`, `ShipyardVisualizer`,
  `ShipyardDomeCameraEffect` or `ShipyardDomeGrapplePreventer` (UnityPy scan).
- The only cached asset bundles are `~/Games/WorldsAdrift/Assets/unity/*@island_unityclient`
  — **255 island bundles, no entity/prefab bundles** (UnityFS, Unity 5.6.1f1). WA streamed
  prefab bundles from a CDN that no longer exists.

**Therefore:** treat the bubble radius as `ImpactRadius = 35 m` (recovered), and label any
dome-mesh-specific scaling as an explicit approximation. Do not invent a separate dome radius
constant and present it as retail truth.

## Open question for live verification

Whether the visible dome mesh is exactly `ImpactRadius` or a scaled multiple
(`targetDomeRadius` × `_radiusThresholdScalar`) can only be settled by eye against a live
shipyard: raise the bubble, fly to its visible edge, and compare the hull position to the
yard position. Until then 35 m stands as the single radius for both the approach gate and the
"fully out of the bubble" departure boundary.
