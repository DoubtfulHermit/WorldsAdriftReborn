# The shipyard dock "bubble" — server behaviour

Behind `WAREBORN_FLIGHT_DOCKING_TXN=1` (which still requires
`WAREBORN_FLIGHT_COLLISION_OBSERVE=1` and `WAREBORN_FLIGHT_FIXED_STEP=1`). No new
flag. With the gate OFF the legacy radius-snap path
(`ShipFlightService.TryCaptureAtEmptyShipyard` → `ShipyardDockingPolicy.CanDock`,
9 m / 18 m) is byte-identical to before.

Client research this is built on: `docs/research/findings-shipyard-dome.md`.

## The rule, in the player's words

1. Ship inside the bubble **and above the shipyard**, player **leaves the helm**
   → the ship snaps into the docked position and the bubble appears.
2. Player gets back on the helm → the ship **stays** docked. Manning is not a
   departure.
3. Only when the ship **moves under propulsion** and is **fully out of the
   bubble** → undocked, bubble gone.

## Where each piece lives

| Concern | Type | Assembly |
|---|---|---|
| the bubble as geometry | `ShipyardBubble` (`Ship/Flight/ShipyardBubble.cs`) | Multiplayer (pure) |
| its values | `DockingTuning` (`Ship/AuthenticDockingLifecycle.cs`) | Multiplayer (pure) |
| the phase rules | `AuthenticDockingLifecycle.Step` / `ValidateApproach` | Multiplayer (pure) |
| the 1114/1205 truth | `DockingComponentProjection` | Multiplayer (pure) |
| gathering helm/yard/hull inputs | `ShipDockingRuntimeDriver` | game (thin glue) |
| publishing it | `ShipDockingTransaction` | game (thin glue) |

## The volume

`ShipyardBubble` is centred on the **yard**, not on the dock pose 6 m above it.

- `IsWithinRange` — **RECOVERED**: the client's `Shipyard.IsWithinRange(Vector3)`,
  a plain sphere of `ImpactRadius` = **35 m** (recovered default).
- `IsAboveYard` — at or above `yardY + DomeFloorOffsetMetres`.
- `ContainsDock` — both. This is the **dome**: the upper half of the influence
  sphere, which is exactly "inside the bubble and above the shipyard". The
  client's own name for the thing (`_influenceDome`, `SpawnBubble`) is a dome,
  i.e. a hemisphere standing on the yard.
- `HasFullyCleared(point, hullRadius)` — `distance - hullRadius > 35 + margin`.

### Constants and provenance

| Value | Default | Provenance | Why |
|---|---|---|---|
| `ApproachRadiusMetres` | 35 m | **RECOVERED** `Shipyard.ImpactRadius` | the bubble |
| `DomeFloorOffsetMetres` | 0 m | **WAReborn tuning** | see below |
| `BubbleExitMarginMetres` | 2 m | **WAReborn tuning** | see below |
| `DockInterpolationRatePerSecond` | 5/s | **RECOVERED** client code | unchanged |
| capture speed / angular ceilings | 2 m/s, 0.25 rad/s | **WAReborn tuning** | unchanged |
| dome MESH radius | — | **LOST** | prefab-serialized; we reuse `ImpactRadius` and label it an approximation |

**The vertical band (0 m offset).** The floor sits on the yard's own registered
Y. This is derived, not picked: `BuiltShipPlacement.HullNextTo` puts a built
hull `HoverHeightMetres` = `HullBodyHeightMetres` (3.4 m) +
`HoverClearanceMetres` (2.6 m) = **6.0 m straight above** that plane, and the
hull's deck plane is its own lowest point. So the yard's registration plane is
the bottom of everything the yard owns, a hull at or above it is genuinely over
the yard, and the convergence only ever has to settle a hull DOWN onto the dock
pose. The top needs no constant: the 35 m sphere already caps it. The offset is
a knob so that a live dome observed starting higher is one edit —
`ShipyardBubbleTests` pins the current value.

**The hysteresis (2 m).** Entry tests at exactly 35 m, exit only past 37 m. 2 m
is ~6% of the radius and four times the 0.48 m a hull covers in one 0.24 s
docking scan (`ShipMotionPolicy.SendIntervalSeconds`) at the 2 m/s capture
ceiling, so a single scan can never straddle the band and a hull hovering on the
visible edge cannot flap between linked and unlinked. Exit additionally
subtracts the hull's own yaw-invariant bounding radius, so "fully out" is
literal: a wide hull whose centre is past the margin but whose flank still
overlaps the dome has not cleared it.

## The phase rules that changed

| Before | Now |
|---|---|
| capture when within **9 m** of the dock pose, neutral and slow — manned or not | capture when the **helm is released**, the hull is inside the **dome**, and the motion is inside the capture band |
| approach gate = 35 m from the **dock pose** (sphere) | approach gate = 35 m from the **yard** and above it (`BelowShipyard` otherwise) |
| an approach held the yard forever if the hull drifted off with no propulsion | an approach that leaves the bubble releases the claim |
| departure completed past an **18 m** release radius from the dock pose | departure completes only **fully outside the bubble** (35 m + 2 m + hull radius) |
| `DockingFrame` was handed a precomputed `outsideReleaseEnvelope` bool | the frame carries the bubble and derives it — one source of truth |
| 1205 `DockedShipId` published from **Approaching** | published from **Captured** through **Departing**, cleared at the unlink |
| 1205 checkout always read the legacy ledger | reads the runtime's committed truth for a managed yard, legacy ledger otherwise |
| yard scan took the lowest entity id in range | takes the **nearest** yard whose dome contains the hull (id breaks ties) |

Unchanged: the collision-clearance evidence rules, the stamp rules, the
transaction's durable-first ordering and republish debt, the claim registry, the
5/s convergence, velocity-zero on capture, and every fail-closed path.

### Helm release is modelled as the unmanned STATE, not an edge

`session.IsManned`. A hull that arrives already unmanned — drifted, or restored
at boot sitting in a yard — captures for the same reason a hull whose pilot just
stood up does: nobody is at its helm. That is also the legacy repair case the
old capture path documented.

Note the interaction with `FlightSession.Dismount`: the throttle is a **latched
lever** and survives leaving the helm, so a pilot who walks away with the
throttle still forward leaves the hull under power (`propulsion != None`) and it
will not capture. Zeroing the lever before stepping off is the explicit stop
command, exactly as `Dismount`'s own doc comment says. Unfurled sails are
propulsion too: furl before docking.

## The bubble as published truth

`DockingComponentProjection.RaisesBubble(phase)` = `Captured | Docked |
Departing`, and `YardDockedHullEntityId` follows it. **RECOVERED**:
`ShipyardVisualizer` drives the influence dome from component 1205
`ShipyardState.DockedShipId` via `OnDockedShipChanged`, and a yard counts as
active only while `Shipyard.DockedShip != null` — so that field IS the bubble.

- **Approaching**: 1114 carries the yard id with `ApproachingDock = true` (the
  server-side reservation, which is what that flag is for); 1205 stays 0.
  Raising it here would inflate the dome around a ship still flying in.
- **Captured**: the snap. 1205 names the hull → the bubble comes up, in the same
  atomic transaction as the durable snapshot and the frozen pose.
- **Departing**: 1114 `Docked = false`, but 1205 still names the hull → the dome
  stays up while the player flies out of it.
- **Released**: 1205 cleared on the remembered yard → the dome falls.

The 1205 checkout serve (`ComponentsSerializer`) asks
`ShipFlightService.RuntimeDockedShipAt(yard)` first and falls back to
`BuiltShips.DockedShipFor` when that returns null (i.e. for every yard the
runtime does not manage, which is every yard with the gate off). The transaction
deliberately stops writing the legacy dock ledger, so without this a late joiner
would check out a docked yard with no dome around it.

## The island-envelope blocker — closed

Previously deferred as "live docking near islands fails closed under
conservative envelopes". Island terrain proxies are conservative AABB envelopes
and an island-placed yard is by construction inside its island's envelope, so
beside any yard worth docking at the hull is permanently in contact with a box
that says nothing about the air it is in, and every approach failed closed as
`CollisionBlocked`.

`CollisionClearanceRecord.From` now takes an optional **reviewed dock volume** —
the yard's own influence sphere, supplied only by the docking driver. A
`Terrain`-kind contact whose point lies inside it is not counted as a blocker.
The whole sphere, not just the docking half, because the terrain a yard stands
on is below it.

The safety properties are unchanged and pinned by
`The_reviewed_dock_volume_never_launders_truncation_hulls_or_distant_terrain`:

- truncation, caps, dropped proxies and hard rejections still force
  `EvaluationComplete = false`, which `IsClear` refuses. Nothing about the
  exemption touches that computation;
- hull-hull contacts are never exempt — another ship in the volume still blocks;
- contacts outside the volume are never exempt;
- no physical behaviour changes: a `ConservativeEnvelope` proxy can never produce
  a response anyway (`RejectedAmbiguousGeometry`), so this evidence's only
  consumer was ever the docking veto.

## Known conflict with the recovered picture

The player's wording is "inside the bubble AND above the shipyard". The
recovered client membership test, `Shipyard.IsWithinRange`, is a **plain
sphere** — retail's own range check has no vertical band. The implemented
reading keeps the recovered sphere as the horizontal reach and adds the band as
declared WAReborn tuning, which is both what the player asked for and what the
word "dome" implies.

The alternative reading — a pure sphere, matching `IsWithinRange` exactly — is
one config value away and needs no code change: `DomeFloorOffsetMetres` at
`-35.0` (anything ≤ `-ImpactRadius`) makes `ContainsDock` identical to
`IsWithinRange`. It is not the default because it lets a hull hovering *under*
an island-mounted yard dock to it, which is the case the player's wording
exists to exclude.

## Open question for live verification

Whether the visible dome mesh is exactly `ImpactRadius` or a scaled multiple can
only be settled by eye (`findings-shipyard-dome.md`). Until then 35 m is the
single radius for the approach gate, the capture volume, the departure boundary
and the reviewed dock volume. If the live dome turns out larger or smaller, one
`ApproachRadiusMetres` edit moves all four together.
